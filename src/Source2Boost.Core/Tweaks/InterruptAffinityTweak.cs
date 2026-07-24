using System.Text.Json;
using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>
/// Уводит прерывания видеокарты с ядра CPU0 на остальные ядра
/// (…\Device Parameters\Interrupt Management\Affinity Policy: DevicePolicy=4
/// «SpecifiedProcessors» + AssignmentSetOverride = маска всех ядер, КРОМЕ CPU0).
/// CPU0 обслуживает большинство системных DPC/ISR — сняв с него прерывания тяжёлой GPU,
/// получаем ровнее фреймтайм и меньше стуттера на многоядерных системах.
/// Устройство ищется консервативно: только PCI дисплейные адаптеры NVIDIA (VEN_10DE)
/// или AMD (VEN_1002). Точный откат: исходные значения по каждому устройству сохраняются.
/// High, нужна перезагрузка. Обратимо.
/// </summary>
public sealed class InterruptAffinityTweak : ITweak
{
    private const string AffinitySubPath = @"Device Parameters\Interrupt Management\Affinity Policy";
    private const string PolicyValue = "DevicePolicy";
    private const string MaskValue = "AssignmentSetOverride";
    // IrqPolicySpecifiedProcessors — применять маску AssignmentSetOverride.
    private const int IrqPolicySpecifiedProcessors = 4;

    public string Id => "interrupt-affinity-gpu";
    public TweakCategory Category => TweakCategory.Frametime;
    public RiskLevel Risk => RiskLevel.High;
    public bool RequiresRestart => true;

    public L10n Title { get; } = new(
        "Прерывания GPU мимо CPU0", "Переривання GPU повз CPU0", "GPU interrupts off CPU0");
    public L10n Description { get; } = new(
        "🔴 Уводит прерывания видеокарты с ядра №0 на остальные ядра. Ядро №0 обычно занято системными задачами — разгрузив его от прерываний тяжёлой видеокарты, получаем ровнее фреймтайм. Обратимо, нужна перезагрузка.",
        "🔴 Відводить переривання відеокарти з ядра №0 на інші ядра. Ядро №0 зазвичай зайняте системними задачами — розвантаживши його від переривань важкої відеокарти, отримуємо рівніший фреймтайм. Оборотно, потрібне перезавантаження.",
        "🔴 Steers the GPU's interrupts off core #0 onto the other cores. Core #0 is usually busy with system work — offloading the heavy GPU's interrupts there yields steadier frametime. Reversible, needs a reboot.");
    public L10n Impact { get; } = new(
        "-стуттер прерываний", "-стуттер переривань", "-interrupt stutter");

    // Многоядерная система с дискретной GPU: снятие CPU0 оставляет достаточно ядер.
    public bool IsSupported(TweakContext ctx) => ctx.Hardware.HasDiscreteGpu && ctx.Hardware.CpuThreads >= 4;

    private static RegistryKey Root =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    /// <summary>Маска всех логических процессоров, КРОМЕ CPU0 (бит 0), как 8 байт little-endian.</summary>
    private static byte[] MaskWithoutCpu0(int threads)
    {
        int n = Math.Clamp(threads, 2, 64);
        ulong all = n >= 64 ? ulong.MaxValue : ((1UL << n) - 1UL);
        ulong mask = all & ~1UL; // убрать CPU0
        return BitConverter.GetBytes(mask); // little-endian на x64
    }

    public bool IsApplied(TweakContext ctx)
    {
        var devices = GpuDevices.FindDiscreteDisplayAdapters();
        if (devices.Count == 0) return false;
        using var root = Root;
        foreach (var dev in devices)
        {
            using var k = root.OpenSubKey($@"{dev}\{AffinitySubPath}");
            if (k?.GetValue(PolicyValue) is not int v || v != IrqPolicySpecifiedProcessors) return false;
            if (k.GetValue(MaskValue) is not byte[]) return false;
        }
        return true;
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            var devices = GpuDevices.FindDiscreteDisplayAdapters();
            if (devices.Count == 0)
                return TweakResult.Fail("no confirmed discrete display device found under PCI enum");

            var mask = MaskWithoutCpu0(ctx.Hardware.CpuThreads);

            // Снимок оригиналов фиксируем ТОЛЬКО ОДИН РАЗ (по каждому устройству, по каждому значению).
            var prior = ctx.Backup.LoadState(Id);
            var originals = prior is not null
                ? (JsonSerializer.Deserialize<Dictionary<string, OriginalValue>>(prior) ?? new())
                : new Dictionary<string, OriginalValue>();

            using (var root = Root)
            {
                foreach (var dev in devices)
                {
                    ctx.Backup.BackupRegistryKey($@"HKLM\{dev}\{AffinitySubPath}");
                    using var k = root.CreateSubKey($@"{dev}\{AffinitySubPath}", writable: true);
                    if (k is null) { ctx.Trace($"{Id}: cannot open {dev}, skip"); continue; }

                    CaptureOnce(originals, $"{dev}|{PolicyValue}", k, PolicyValue);
                    CaptureOnce(originals, $"{dev}|{MaskValue}", k, MaskValue);

                    k.SetValue(PolicyValue, IrqPolicySpecifiedProcessors, RegistryValueKind.DWord);
                    k.SetValue(MaskValue, mask, RegistryValueKind.Binary);
                    ctx.Trace($"{Id}: DevicePolicy=4 + mask @ {dev}");
                }
            }
            ctx.Backup.SaveState(Id, JsonSerializer.Serialize(originals));
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            var json = ctx.Backup.LoadState(Id);
            var originals = json is null
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, OriginalValue>>(json);

            var targets = originals?.Keys.Select(k => k.Split('|')[0]).Distinct().ToList()
                          ?? GpuDevices.FindDiscreteDisplayAdapters();
            using var root = Root;
            foreach (var dev in targets)
            {
                using var k = root.OpenSubKey($@"{dev}\{AffinitySubPath}", writable: true);
                if (k is null) continue;
                RestoreOrDelete(originals, $"{dev}|{PolicyValue}", k, PolicyValue, RegistryValueKind.DWord);
                RestoreOrDelete(originals, $"{dev}|{MaskValue}", k, MaskValue, RegistryValueKind.Binary);
            }
            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    private static void CaptureOnce(Dictionary<string, OriginalValue> orig, string key, RegistryKey k, string valueName)
    {
        if (orig.ContainsKey(key)) return;
        var ex = k.GetValue(valueName);
        orig[key] = ex is null
            ? new OriginalValue(false, null, null)
            : new OriginalValue(true, k.GetValueKind(valueName).ToString(),
                ex is byte[] b ? Convert.ToBase64String(b) : ex.ToString());
    }

    private static void RestoreOrDelete(Dictionary<string, OriginalValue>? orig, string key,
        RegistryKey k, string valueName, RegistryValueKind kind)
    {
        if (orig is not null && orig.TryGetValue(key, out var ov) && ov.Existed)
        {
            object val = kind == RegistryValueKind.Binary
                ? Convert.FromBase64String(ov.Value ?? "")
                : (kind == RegistryValueKind.DWord && int.TryParse(ov.Value, out var i) ? i : (object)(ov.Value ?? ""));
            k.SetValue(valueName, val, kind);
        }
        else
        {
            // значения не было по умолчанию — просто удаляем наше
            k.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}
