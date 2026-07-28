using System.Diagnostics;

namespace Source2Boost.Core;

/// <summary>
/// Применяет маску аффинити к процессу cs2.exe (сажает главный поток движка на «правильные»
/// ядра — P-ядра у Intel-гибрида или кэш-CCD у AMD). CS2 может сбрасывать аффинити, поэтому
/// вызывается повторно из сторожа, пока игра запущена.
/// </summary>
public static class Cs2Affinity
{
    /// <summary>Поставить маску всем процессам cs2. Возвращает, скольким удалось.</summary>
    public static int Apply(ulong mask)
    {
        if (mask == 0) return 0;
        int done = 0;
        foreach (var p in SafeGet("cs2"))
        {
            try { p.ProcessorAffinity = (nint)mask; done++; }
            catch { /* нет прав/процесс исчез */ }
            finally { p.Dispose(); }
        }
        return done;
    }

    /// <summary>Вернуть аффинити на все логические процессоры (при откате твика).</summary>
    public static int Reset()
    {
        int n = Environment.ProcessorCount;
        ulong all = n >= 64 ? ulong.MaxValue : (1UL << n) - 1;
        return Apply(all);
    }

    /// <summary>Желаемая маска cs2 с учётом ОБОИХ твиков сразу, чтобы они не конфликтовали:
    /// «правильные ядра» (топология) и «без ЦП0» (увести игру с ядра системных прерываний/DPC).
    /// База = маска топологии (если включена) либо все ядра; «без ЦП0» гасит бит 0. Если оба
    /// выключены — 0 (аффинити не управляем). Пустую маску не отдаём (страховка → все ядра).</summary>
    public static ulong DesiredMask(HardwareInfo? hw, bool topology, bool noCore0)
    {
        if (!topology && !noCore0) return 0;
        int n = Environment.ProcessorCount;
        ulong all = n >= 64 ? ulong.MaxValue : (1UL << n) - 1;
        ulong baseMask = topology ? CpuTopology.Detect(hw).RecommendedMask : all;
        if (baseMask == 0) baseMask = all;
        if (noCore0) baseMask &= ~1UL;
        return baseMask == 0 ? all : baseMask;
    }

    private static Process[] SafeGet(string name)
    {
        try { return Process.GetProcessesByName(name); }
        catch { return Array.Empty<Process>(); }
    }
}
