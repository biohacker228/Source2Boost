using System.Text.Json;
using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>
/// Обратимый «деблоат»: отключает через политики реестра навязчивые фоновые механизмы Windows —
/// авто-подгрузку «рекомендованных» приложений, подсказки/рекламу в меню Пуск и Проводнике,
/// Кортану, ленту «Новости и интересы»/виджеты и телеметрию до минимума. НИЧЕГО НЕ УДАЛЯЕТ
/// (только политики-выключатели), поэтому полностью обратимо. Антивирус Defender НЕ трогает —
/// его при желании «замораживают» отдельные твики (defender-exclusion / defender-realtime-off).
/// </summary>
public sealed class DebloatTweak : ITweak
{
    private sealed record Entry(RegistryHive Hive, string SubKey, string Name, object Value);

    // Все значения — DWord. Ключи — общеизвестные политики деблоата, безопасные и обратимые.
    private static readonly Entry[] Entries =
    {
        // Авто-установка «рекомендованных»/спонсорских приложений и рекламный контент
        new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableConsumerAccountStateContent", 1),
        // Подсказки/реклама и авто-приложения (для текущего пользователя)
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0),
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0),
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "PreInstalledAppsEnabled", 0),
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "OemPreInstalledAppsEnabled", 0),
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0),
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0),
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled", 0),
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled", 0),
        // Кортана
        new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0),
        // Лента «Новости и интересы» / виджеты на панели задач
        new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0),
        // Телеметрия до минимума (Basic/Security — полностью выключить нельзя в Home)
        new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
    };

    public string Id => "debloat";
    public TweakCategory Category => TweakCategory.Services;
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "Деблоат Windows", "Деблоат Windows", "Windows debloat");
    public L10n Description { get; } = new(
        "Отключает навязчивый фон Windows: авто-установку «рекомендованных» приложений, рекламу и подсказки в Пуске и Проводнике, Кортану, ленту «Новости и интересы» и телеметрию. Ничего не удаляет — только политики-выключатели, поэтому полностью обратимо. Антивирус не трогает.",
        "Вимикає нав'язливий фон Windows: авто-встановлення «рекомендованих» додатків, рекламу й підказки в Пуску та Провіднику, Кортану, стрічку «Новини та інтереси» і телеметрію. Нічого не видаляє — лише політики-вимикачі, тож повністю оборотно. Антивірус не чіпає.",
        "Turns off intrusive Windows background: auto-installed 'suggested' apps, ads and tips in Start and Explorer, Cortana, the 'News and interests' feed, and telemetry. Removes nothing — policy switches only, fully reversible. Doesn't touch antivirus.");
    public L10n Impact { get; } = new(
        "-фоновый мусор", "-фонове сміття", "-background clutter");

    public bool IsSupported(TweakContext ctx) => true;

    private static RegistryKey Root(RegistryHive h) => RegistryKey.OpenBaseKey(h, RegistryView.Registry64);

    public bool IsApplied(TweakContext ctx)
    {
        foreach (var e in Entries)
        {
            using var root = Root(e.Hive);
            using var k = root.OpenSubKey(e.SubKey);
            if (k?.GetValue(e.Name) is not int v || v != (int)e.Value) return false;
        }
        return true;
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            var prior = ctx.Backup.LoadState(Id);
            var originals = prior is not null
                ? (JsonSerializer.Deserialize<Dictionary<string, OriginalValue>>(prior) ?? new())
                : new Dictionary<string, OriginalValue>();

            foreach (var e in Entries)
            {
                var mapKey = $"{e.Hive}|{e.SubKey}|{e.Name}";
                ctx.Backup.BackupRegistryKey($@"{HivePrefix(e.Hive)}\{e.SubKey}");
                using var root = Root(e.Hive);
                using var k = root.CreateSubKey(e.SubKey, writable: true);
                if (k is null) { ctx.Trace($"{Id}: cannot open {e.SubKey}, skip"); continue; }
                if (!originals.ContainsKey(mapKey))
                {
                    var ex = k.GetValue(e.Name);
                    originals[mapKey] = ex is null
                        ? new OriginalValue(false, null, null)
                        : new OriginalValue(true, k.GetValueKind(e.Name).ToString(), ex.ToString());
                }
                k.SetValue(e.Name, e.Value, RegistryValueKind.DWord);
            }
            ctx.Backup.SaveState(Id, JsonSerializer.Serialize(originals));
            ctx.Trace($"applied {Id} ({Entries.Length} policies)");
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
            foreach (var e in Entries)
            {
                var mapKey = $"{e.Hive}|{e.SubKey}|{e.Name}";
                using var root = Root(e.Hive);
                using var k = root.OpenSubKey(e.SubKey, writable: true);
                if (k is null) continue;
                if (originals is not null && originals.TryGetValue(mapKey, out var ov) && ov.Existed
                    && int.TryParse(ov.Value, out var iv))
                    k.SetValue(e.Name, iv, RegistryValueKind.DWord);
                else
                    k.DeleteValue(e.Name, throwOnMissingValue: false); // не было — удаляем наш выключатель
            }
            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    private static string HivePrefix(RegistryHive h) => h switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        _ => h.ToString()
    };
}
