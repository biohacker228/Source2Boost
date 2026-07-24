using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Source2Boost.Core;

/// <summary>
/// Настройки загрузчика для ровного фрейм-пейсинга: disabledynamictick=yes,
/// useplatformclock=false, tscsyncpolicy=Enhanced. Точный откат: исходное состояние
/// {current} парсится из bcdedit /enum — если значения не было, при откате оно удаляется.
/// Требует прав администратора и перезагрузки.
/// </summary>
public sealed class BcdEditTweak : ITweak
{
    // Целевое имя -> желаемое значение (как его печатает bcdedit /set).
    private static readonly (string Name, string Value)[] Targets =
    {
        ("disabledynamictick", "Yes"),
        ("useplatformclock",   "No"),        // false
        ("tscsyncpolicy",      "Enhanced"),
    };

    public string Id => "bcdedit-timers";
    public TweakCategory Category => TweakCategory.CpuPower;
    public RiskLevel Risk => RiskLevel.High;
    public bool RequiresRestart => true;

    public L10n Title { get; } = new(
        "Таймеры загрузчика (bcdedit)", "Таймери завантажувача (bcdedit)", "Bootloader timers (bcdedit)");
    public L10n Description { get; } = new(
        "🔴 Настраивает параметры загрузчика Windows, отвечающие за системный таймер — ниже задержка таймера, ровнее кадры. Редко влияет на стабильность; полностью обратимо, нужна перезагрузка.",
        "🔴 Налаштовує параметри завантажувача Windows, що відповідають за системний таймер — нижча затримка таймера, рівніші кадри. Рідко впливає на стабільність; повністю оборотно, потрібне перезавантаження.",
        "🔴 Tunes the Windows bootloader options that control the system timer — lower timer latency, steadier frames. Rarely affects stability; fully reversible, needs a reboot.");
    public L10n Impact { get; } = new(
        "-латентность таймера", "-латентність таймера", "-timer latency");

    public bool IsSupported(TweakContext ctx) => true;

    public bool IsApplied(TweakContext ctx)
    {
        var cur = ReadCurrent();
        foreach (var (name, val) in Targets)
        {
            if (!cur.TryGetValue(name, out var v)) return false;
            if (!string.Equals(v, val, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            var cur = ReadCurrent();
            // Снимок оригинала фиксируем ТОЛЬКО ОДИН РАЗ.
            var prior = ctx.Backup.LoadState(Id);
            var originals = prior is not null
                ? (JsonSerializer.Deserialize<Dictionary<string, string?>>(prior) ?? new())
                : new Dictionary<string, string?>();

            foreach (var (name, val) in Targets)
            {
                if (!originals.ContainsKey(name))
                    originals[name] = cur.TryGetValue(name, out var ov) ? ov : null; // null = значения не было
                Run($"/set {name} {val}");
            }
            ctx.Backup.SaveState(Id, JsonSerializer.Serialize(originals));
            ctx.Trace($"applied {Id}");
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
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(json);

            foreach (var (name, _) in Targets)
            {
                if (originals is not null && originals.TryGetValue(name, out var ov))
                {
                    if (ov is null) Run($"/deletevalue {name}");     // не было -> удалить
                    else Run($"/set {name} {ov}");                   // восстановить исходное
                }
                else
                {
                    // нет данных об исходнике — безопаснее удалить наше значение
                    Run($"/deletevalue {name}");
                }
            }
            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    /// <summary>Прочитать текущие значения {current} как name->value (только присутствующие).</summary>
    private static Dictionary<string, string> ReadCurrent()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var outp = Run("/enum {current}");
        foreach (var line in outp.Split('\n'))
        {
            var m = Regex.Match(line.Trim(), @"^([A-Za-z]+)\s+(.+?)\s*$");
            if (!m.Success) continue;
            var name = m.Groups[1].Value;
            foreach (var (target, _) in Targets)
                if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                    result[target] = m.Groups[2].Value.Trim();
        }
        return result;
    }

    private static string Run(string args)
    {
        var psi = new ProcessStartInfo("bcdedit.exe", args)
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "";
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return o;
        }
        catch { return ""; }
    }
}
