using System.Text.Json;
using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>
/// Включает Message-Signaled Interrupts (MSI) для видеокарты (NVIDIA или AMD):
/// ...\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties MSISupported=1.
/// Устройство определяется КОНСЕРВАТИВНО: только PCI-девайсы вендоров NVIDIA (VEN_10DE) или
/// AMD (VEN_1002) с ClassGUID = класс дисплейных адаптеров — чтобы не задеть, например, аудио GPU.
/// Точный откат: исходное значение MSISupported по каждому устройству сохраняется/восстанавливается.
/// </summary>
public sealed class MsiModeTweak : ITweak
{
    private const string MsiSubPath = @"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
    private const string ValueName = "MSISupported";

    public string Id => "msi-mode-gpu";
    public TweakCategory Category => TweakCategory.Nvidia;
    public RiskLevel Risk => RiskLevel.High;
    public bool RequiresRestart => true;

    public L10n Title { get; } = new(
        "MSI-режим прерываний GPU", "MSI-режим переривань GPU", "GPU MSI interrupt mode");
    public L10n Description { get; } = new(
        "🔴 Переводит видеокарту (NVIDIA или AMD) на современный режим прерываний MSI — ниже задержка прерываний, ровнее кадры. Меняется только у подтверждённой видеокарты; обратимо, нужна перезагрузка.",
        "🔴 Переводить відеокарту (NVIDIA або AMD) на сучасний режим переривань MSI — нижча затримка переривань, рівніші кадри. Змінюється лише у підтвердженої відеокарти; оборотно, потрібне перезавантаження.",
        "🔴 Switches the GPU (NVIDIA or AMD) to the modern MSI interrupt mode — lower interrupt latency, steadier frames. Only the confirmed GPU is changed; reversible, needs a reboot.");
    public L10n Impact { get; } = new(
        "-латентность прерываний", "-латентність переривань", "-interrupt latency");

    public bool IsSupported(TweakContext ctx) => ctx.Hardware.HasDiscreteGpu;

    private static RegistryKey Root =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    public bool IsApplied(TweakContext ctx)
    {
        var devices = GpuDevices.FindDiscreteDisplayAdapters();
        if (devices.Count == 0) return false;
        using var root = Root;
        foreach (var dev in devices)
        {
            using var k = root.OpenSubKey($@"{dev}\{MsiSubPath}");
            if (k?.GetValue(ValueName) is not int v || v != 1) return false;
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

            // Снимок оригиналов фиксируем ТОЛЬКО ОДИН РАЗ (по каждому устройству).
            var prior = ctx.Backup.LoadState(Id);
            var originals = prior is not null
                ? (JsonSerializer.Deserialize<Dictionary<string, OriginalValue>>(prior) ?? new())
                : new Dictionary<string, OriginalValue>();

            using (var root = Root)
            {
                foreach (var dev in devices)
                {
                    ctx.Backup.BackupRegistryKey($@"HKLM\{dev}\{MsiSubPath}");
                    using var k = root.CreateSubKey($@"{dev}\{MsiSubPath}", writable: true);
                    if (k is null) { ctx.Trace($"{Id}: cannot open {dev}, skip"); continue; }
                    if (!originals.ContainsKey(dev))
                    {
                        var ex = k.GetValue(ValueName);
                        originals[dev] = ex is null
                            ? new OriginalValue(false, null, null)
                            : new OriginalValue(true, k.GetValueKind(ValueName).ToString(), ex.ToString());
                    }
                    k.SetValue(ValueName, 1, RegistryValueKind.DWord);
                    ctx.Trace($"{Id}: MSISupported=1 @ {dev}");
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

            // откатываем ровно те устройства, что записывали (по бэкапу), иначе — текущие найденные
            var targets = originals?.Keys.ToList() ?? GpuDevices.FindDiscreteDisplayAdapters();
            using var root = Root;
            foreach (var dev in targets)
            {
                using var k = root.OpenSubKey($@"{dev}\{MsiSubPath}", writable: true);
                if (k is null) continue;
                if (originals is not null && originals.TryGetValue(dev, out var ov) && ov.Existed)
                    k.SetValue(ValueName, Coerce(ov), RegistryValueKind.DWord);
                else
                    k.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    private static object Coerce(OriginalValue ov)
        => ov.Kind == RegistryValueKind.DWord.ToString() && int.TryParse(ov.Value, out var i)
            ? i
            : (object)(ov.Value ?? "");
}
