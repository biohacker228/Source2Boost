using System.Text.Json;
using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>
/// Задаёт файл подкачки ФИКСИРОВАННОГО размера на системном диске (min = max), отключая
/// авто-управление Windows. Растущий/сжимающийся pagefile — источник микрофризов на слабых
/// ПК с малым объёмом RAM: система на ходу меняет его размер под нагрузкой. Фиксированный
/// размер убирает эти скачки. Применимо когда RAM ≤ 8 ГБ (иначе не нужно). Пишется в реестр
/// Memory Management (PagingFiles + AutomaticManagedPagefile); полностью обратимо, нужна
/// перезагрузка. Medium.
/// </summary>
public sealed class PagefileFixedTweak : ITweak
{
    private const string MmKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const string PagingFilesValue = "PagingFiles";
    private const string AutoManagedValue = "AutomaticManagedPagefile";
    private const int FixedSizeMb = 8192; // 8 ГБ фикс — комфортно для систем с ≤ 8 ГБ RAM

    public string Id => "pagefile-fixed";
    public TweakCategory Category => TweakCategory.Memory;
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresRestart => true;

    public L10n Title { get; } = new(
        "Фиксированный файл подкачки", "Фіксований файл підкачки", "Fixed-size page file");
    public L10n Description { get; } = new(
        "Задаёт pagefile фиксированного размера (8 ГБ, min=max) на системном диске и отключает авто-управление. На слабом ПК Windows на ходу меняет размер подкачки под нагрузкой — это даёт микрофризы; фиксированный размер их убирает. Лучше всего на SSD. Обратимо, нужна перезагрузка.",
        "Задає pagefile фіксованого розміру (8 ГБ, min=max) на системному диску й вимикає авто-керування. На слабкому ПК Windows на ходу змінює розмір підкачки під навантаженням — це дає мікрофризи; фіксований розмір їх прибирає. Найкраще на SSD. Оборотно, потрібне перезавантаження.",
        "Sets a fixed-size page file (8 GB, min=max) on the system drive and disables auto-management. On a weak PC Windows resizes the page file on the fly under load, causing micro-stutter; a fixed size removes it. Best on an SSD. Reversible, needs a reboot.");
    public L10n Impact { get; } = new(
        "-статтер подкачки", "-статтер підкачки", "-paging stutter");

    // Актуально только для машин с малым объёмом памяти (≤ 8 ГБ). На 16+ ГБ подкачка почти не задействуется.
    public bool IsSupported(TweakContext ctx) => ctx.Hardware.RamGb is > 0 and <= 8;

    private static RegistryKey Root =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    private static string SystemDrive
    {
        get
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            return string.IsNullOrEmpty(root) ? @"C:\" : root; // напр. "C:\"
        }
    }

    private static string DesiredLine => $@"{SystemDrive}pagefile.sys {FixedSizeMb} {FixedSizeMb}";

    public bool IsApplied(TweakContext ctx)
    {
        using var root = Root;
        using var k = root.OpenSubKey(MmKey);
        if (k is null) return false;
        if (k.GetValue(AutoManagedValue) is int am && am != 0) return false;
        if (k.GetValue(PagingFilesValue) is not string[] lines) return false;
        // Есть строка с равными ненулевыми min/max (т.е. фиксированный размер).
        foreach (var line in lines)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3 && int.TryParse(p[^2], out var mn) && int.TryParse(p[^1], out var mx)
                && mn > 0 && mn == mx) return true;
        }
        return false;
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            ctx.Backup.BackupRegistryKey($@"HKLM\{MmKey}");
            var prior = ctx.Backup.LoadState(Id);
            var originals = prior is not null
                ? (JsonSerializer.Deserialize<Dictionary<string, OriginalValue>>(prior) ?? new())
                : new Dictionary<string, OriginalValue>();

            using (var root = Root)
            using (var k = root.CreateSubKey(MmKey, writable: true))
            {
                if (k is null) return TweakResult.Fail($"cannot open {MmKey}");

                if (!originals.ContainsKey(PagingFilesValue))
                {
                    var cur = k.GetValue(PagingFilesValue) as string[];
                    originals[PagingFilesValue] = cur is null
                        ? new OriginalValue(false, null, null)
                        : new OriginalValue(true, "MultiString", string.Join('\n', cur));
                }
                if (!originals.ContainsKey(AutoManagedValue))
                {
                    var cur = k.GetValue(AutoManagedValue);
                    originals[AutoManagedValue] = cur is null
                        ? new OriginalValue(false, null, null)
                        : new OriginalValue(true, "DWord", cur.ToString());
                }

                k.SetValue(AutoManagedValue, 0, RegistryValueKind.DWord);
                k.SetValue(PagingFilesValue, new[] { DesiredLine }, RegistryValueKind.MultiString);
            }
            ctx.Backup.SaveState(Id, JsonSerializer.Serialize(originals));
            ctx.Trace($"applied {Id}: {DesiredLine}");
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

            using var root = Root;
            using var k = root.CreateSubKey(MmKey, writable: true);
            if (k is null) return TweakResult.Fail($"cannot open {MmKey}");

            // PagingFiles
            if (originals is not null && originals.TryGetValue(PagingFilesValue, out var pf) && pf.Existed)
                k.SetValue(PagingFilesValue, (pf.Value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries),
                    RegistryValueKind.MultiString);
            else
                k.SetValue(PagingFilesValue, Array.Empty<string>(), RegistryValueKind.MultiString);

            // AutomaticManagedPagefile — по умолчанию Windows управляет сам (=1)
            if (originals is not null && originals.TryGetValue(AutoManagedValue, out var am) && am.Existed
                && int.TryParse(am.Value, out var v))
                k.SetValue(AutoManagedValue, v, RegistryValueKind.DWord);
            else
                k.SetValue(AutoManagedValue, 1, RegistryValueKind.DWord);

            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }
}
