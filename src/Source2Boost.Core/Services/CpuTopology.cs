using System.Runtime.InteropServices;

namespace Source2Boost.Core;

/// <summary>Тип «неправильной» для CS2 топологии CPU, из-за которой движок Source 2 недодаёт
/// FPS/плавность даже на сильном железе (планировщик уводит главный поток «не туда»).</summary>
public enum CpuTopologyKind
{
    /// <summary>Обычный CPU (один кластер, без E-ядер) — проблемы планирования нет.</summary>
    Simple,
    /// <summary>Intel-гибрид (P + E ядра): потоки CS2 уезжают на медленные E-ядра.</summary>
    IntelHybrid,
    /// <summary>AMD с двумя+ CCD (5900X/5950X/7900X/7950X/9900X/X3D…): игра не держится за быстрый/кэш-CCD.</summary>
    AmdMultiCcd
}

/// <summary>
/// Детект топологии логических процессоров через <c>GetLogicalProcessorInformationEx</c>.
/// Нужен для железо-специфичного аффинити CS2: у Intel-гибридов — маска P-ядер, у много-CCD
/// Ryzen — маска первого CCD (у X3D это как раз кэш-CCD). Работает в пределах одной processor
/// group (≤64 логических процессора — покрывает всё потребительское железо).
/// </summary>
public static class CpuTopology
{
    public sealed record Result(
        CpuTopologyKind Kind,
        ulong RecommendedMask,   // на какие лог. процессоры сажать CS2 (0 = не трогать)
        int TargetCores,         // сколько лог. процессоров в маске
        string Describe);        // короткое человекочитаемое описание

    /// <summary>Посчитать рекомендацию по аффинити для CS2 под текущий CPU. Кэшируется на сессию.</summary>
    private static Result? _cached;
    public static Result Detect(HardwareInfo? hw = null)
    {
        if (_cached is not null) return _cached;
        try { _cached = Compute(hw); }
        catch { _cached = new Result(CpuTopologyKind.Simple, 0, 0, "обычный CPU"); }
        return _cached;
    }

    private static Result Compute(HardwareInfo? hw)
    {
        bool isAmd = (hw?.CpuName ?? "").Contains("AMD", StringComparison.OrdinalIgnoreCase)
                     || (hw?.CpuName ?? "").Contains("Ryzen", StringComparison.OrdinalIgnoreCase);

        // 1) Ядра + классы эффективности (Intel-гибрид).
        var cores = ReadCores();               // список (mask, efficiencyClass)
        var effClasses = cores.Select(c => c.eff).Distinct().ToList();
        if (effClasses.Count > 1)
        {
            byte maxEff = effClasses.Max();
            ulong pMask = 0; int pCount = 0;
            foreach (var c in cores)
                if (c.eff == maxEff) { pMask |= c.mask; pCount += PopCount(c.mask); }
            if (pMask != 0)
                return new Result(CpuTopologyKind.IntelHybrid, pMask, pCount,
                    $"P-ядра ({pCount} лог.) — E-ядра исключены");
        }

        // 2) L3-кэши (CCD у AMD). Несколько L3 = несколько CCD.
        if (isAmd)
        {
            var l3 = ReadL3Masks();            // маски по каждому L3 (=CCD)
            if (l3.Count > 1)
            {
                // Первый L3 = CCD0. У X3D-чипов кэш-CCD именно первый — то, что нужно CS2.
                var ccd0 = l3[0];
                int n = PopCount(ccd0);
                if (ccd0 != 0)
                    return new Result(CpuTopologyKind.AmdMultiCcd, ccd0, n,
                        $"первый CCD ({n} лог.) — второй CCD исключён");
            }
        }

        return new Result(CpuTopologyKind.Simple, 0, 0, "один кластер, аффинити не требуется");
    }

    // ---------- Разбор буфера GetLogicalProcessorInformationEx ----------

    private static List<(ulong mask, byte eff)> ReadCores()
    {
        var result = new List<(ulong, byte)>();
        var buf = Query(RelationProcessorCore);
        if (buf.Length == 0) return result;

        int offset = 0;
        while (offset + 8 <= buf.Length)
        {
            int rel = BitConverter.ToInt32(buf, offset);
            int size = BitConverter.ToInt32(buf, offset + 4);
            if (size <= 0 || offset + size > buf.Length) break;
            if (rel == RelationProcessorCore)
            {
                // PROCESSOR_RELATIONSHIP: Flags(1) EfficiencyClass(1) Reserved(20) GroupCount(2) GroupMask[]
                byte eff = buf[offset + 8 + 1];
                int groupCount = BitConverter.ToUInt16(buf, offset + 8 + 22);
                int gm = offset + 8 + 24;          // начало GROUP_AFFINITY[]
                ulong mask = 0;
                for (int g = 0; g < groupCount; g++)
                {
                    // GROUP_AFFINITY: Mask(nuint=8) Group(2) Reserved(6) = 16 байт
                    ushort group = BitConverter.ToUInt16(buf, gm + 8);
                    if (group == 0) mask |= (ulong)BitConverter.ToUInt64(buf, gm);
                    gm += 16;
                }
                result.Add((mask, eff));
            }
            offset += size;
        }
        return result;
    }

    private static List<ulong> ReadL3Masks()
    {
        var result = new List<ulong>();
        var buf = Query(RelationCache);
        if (buf.Length == 0) return result;

        int offset = 0;
        while (offset + 8 <= buf.Length)
        {
            int rel = BitConverter.ToInt32(buf, offset);
            int size = BitConverter.ToInt32(buf, offset + 4);
            if (size <= 0 || offset + size > buf.Length) break;
            if (rel == RelationCache)
            {
                // CACHE_RELATIONSHIP: Level(1) Assoc(1) LineSize(2) CacheSize(4) Type(4) Reserved(18) GroupCount(2) GroupMasks[]
                byte level = buf[offset + 8];
                if (level == 3)
                {
                    int groupCount = BitConverter.ToUInt16(buf, offset + 8 + 30);
                    int gm = offset + 8 + 32;
                    ulong mask = 0;
                    for (int g = 0; g < groupCount; g++)
                    {
                        ushort group = BitConverter.ToUInt16(buf, gm + 8);
                        if (group == 0) mask |= (ulong)BitConverter.ToUInt64(buf, gm);
                        gm += 16;
                    }
                    if (mask != 0) result.Add(mask);
                }
            }
            offset += size;
        }
        return result;
    }

    private static byte[] Query(int relationship)
    {
        uint len = 0;
        GetLogicalProcessorInformationEx(relationship, IntPtr.Zero, ref len);
        if (len == 0) return Array.Empty<byte>();
        var ptr = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(relationship, ptr, ref len)) return Array.Empty<byte>();
            var buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, (int)len);
            return buf;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static int PopCount(ulong v)
    {
        int c = 0; while (v != 0) { v &= v - 1; c++; } return c;
    }

    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);
}
