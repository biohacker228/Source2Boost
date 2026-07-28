using System.Text.Json;
using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>Одна запись реестра, которой управляет твик.</summary>
public sealed record RegEntry(string Name, RegistryValueKind Kind, object Value);

/// <summary>Снимок исходного значения для точного отката.</summary>
internal sealed record OriginalValue(bool Existed, string? Kind, string? Value);

/// <summary>
/// Универсальный твик реестра: применяет набор значений под одним ключом,
/// сохраняя исходники для точного отката (или удаления, если значения не было).
/// </summary>
public sealed class RegistryTweak : ITweak
{
    public string Id { get; }
    public TweakCategory Category { get; }
    public RiskLevel Risk { get; }
    public L10n Title { get; }
    public L10n Description { get; }
    public L10n Impact { get; }
    public bool RequiresRestart { get; }

    private readonly RegistryHive _hive;
    private readonly string _subKey;
    private readonly IReadOnlyList<RegEntry> _entries;
    private readonly Func<TweakContext, bool>? _supported;
    private readonly Func<TweakContext, bool>? _isApplied;

    public RegistryTweak(string id, TweakCategory category, RiskLevel risk,
        L10n title, L10n description, L10n impact,
        RegistryHive hive, string subKey, IReadOnlyList<RegEntry> entries,
        bool requiresRestart = false, Func<TweakContext, bool>? supported = null,
        Func<TweakContext, bool>? isApplied = null)
    {
        Id = id; Category = category; Risk = risk;
        Title = title; Description = description; Impact = impact;
        _hive = hive; _subKey = subKey; _entries = entries;
        RequiresRestart = requiresRestart; _supported = supported; _isApplied = isApplied;
    }

    private RegistryKey Root => RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
    private string FullKeyPath => $"{HivePrefix(_hive)}\\{_subKey}";

    public bool IsSupported(TweakContext ctx) => _supported?.Invoke(ctx) ?? true;

    public bool IsApplied(TweakContext ctx)
    {
        if (_isApplied is not null) return _isApplied(ctx);   // кастомная семантика детекта
        using var root = Root;
        using var k = root.OpenSubKey(_subKey);
        if (k is null) return false;
        foreach (var e in _entries)
        {
            var cur = k.GetValue(e.Name);
            if (cur is null || !ValueEquals(cur, e.Value)) return false;
        }
        return true;
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            ctx.Backup.BackupRegistryKey(FullKeyPath);
            // Снимок оригинала фиксируем ТОЛЬКО ОДИН РАЗ: если твик уже применяли,
            // повторный Apply не должен затирать истинный исходник (иначе откат сломается).
            var prior = ctx.Backup.LoadState(Id);
            var originals = prior is not null
                ? (JsonSerializer.Deserialize<Dictionary<string, OriginalValue>>(prior) ?? new())
                : new Dictionary<string, OriginalValue>();
            using (var root = Root)
            using (var k = root.CreateSubKey(_subKey, writable: true))
            {
                if (k is null) return TweakResult.Fail($"cannot open {FullKeyPath}");
                foreach (var e in _entries)
                {
                    if (!originals.ContainsKey(e.Name))
                    {
                        var existing = k.GetValue(e.Name);
                        originals[e.Name] = existing is null
                            ? new OriginalValue(false, null, null)
                            : new OriginalValue(true, k.GetValueKind(e.Name).ToString(), existing.ToString());
                    }
                    k.SetValue(e.Name, e.Value, e.Kind);
                }
            }
            ctx.Backup.SaveState(Id, JsonSerializer.Serialize(originals));
            ctx.Trace($"applied {Id} @ {FullKeyPath}");
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
            using var k = root.CreateSubKey(_subKey, writable: true);
            if (k is null) return TweakResult.Fail($"cannot open {FullKeyPath}");
            foreach (var e in _entries)
            {
                if (originals is not null && originals.TryGetValue(e.Name, out var ov))
                {
                    if (!ov.Existed) { k.DeleteValue(e.Name, throwOnMissingValue: false); }
                    else k.SetValue(e.Name, Coerce(ov), e.Kind);
                }
                else
                {
                    // нет данных об исходнике — безопаснее удалить наше значение
                    k.DeleteValue(e.Name, throwOnMissingValue: false);
                }
            }
            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    private static object Coerce(OriginalValue ov)
    {
        if (ov.Kind == RegistryValueKind.DWord.ToString() && int.TryParse(ov.Value, out var i)) return i;
        return ov.Value ?? "";
    }

    private static bool ValueEquals(object cur, object desired)
    {
        if (desired is int di) return cur is int ci && ci == di;
        return string.Equals(cur.ToString(), desired.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string HivePrefix(RegistryHive h) => h switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.Users => "HKU",
        RegistryHive.ClassesRoot => "HKCR",
        _ => h.ToString()
    };
}
