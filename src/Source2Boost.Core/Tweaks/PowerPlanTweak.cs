using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Source2Boost.Core;

/// <summary>
/// Активирует план электропитания «Максимальная производительность» (Ultimate Performance),
/// сохраняя ранее активный план для точного отката. Если Ultimate недоступен — High Performance.
/// </summary>
public sealed class PowerPlanTweak : ITweak
{
    // Шаблон Ultimate Performance (скрытый) и встроенный High Performance.
    private const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public string Id => "power-plan-max";
    public TweakCategory Category => TweakCategory.CpuPower;
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "План питания «Максимум»", "План живлення «Максимум»", "Ultimate power plan");
    public L10n Description { get; } = new(
        "Включает и активирует план Ultimate Performance (или High Performance) — CPU перестаёт сбрасывать частоты.",
        "Вмикає та активує план Ultimate Performance (або High Performance) — CPU перестає скидати частоти.",
        "Enables and activates the Ultimate Performance plan (or High Performance) — the CPU stops down-clocking.");
    public L10n Impact { get; } = new(
        "+стабильный FPS", "+стабільний FPS", "+stable FPS");

    public bool IsSupported(TweakContext ctx) => true;

    public bool IsApplied(TweakContext ctx)
    {
        var guid = ActiveScheme();
        if (guid is null) return false;
        // Сравниваем по GUID активного плана, НЕ по имени: имя схемы локализовано (на рус.
        // Windows Ultimate-дубль называется «Максимальная производительность»), и поиск по
        // англ. слову «Ultimate» давал false после применения → тумблер откатывался.
        var applied = ctx.Backup.LoadState(Id + ".active")?.Trim();
        if (!string.IsNullOrEmpty(applied))
            return string.Equals(guid, applied, StringComparison.OrdinalIgnoreCase);
        return string.Equals(guid, HighPerformance, StringComparison.OrdinalIgnoreCase);
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            var curGuid = ActiveScheme();
            var prevActive = ctx.Backup.LoadState(Id + ".active")?.Trim();
            // Исходный план для отката фиксируем, только если сейчас активен НЕ наш ранее
            // поставленный план (иначе перезапишем истинный оригинал своим же значением).
            if (curGuid is not null && !string.Equals(curGuid, prevActive, StringComparison.OrdinalIgnoreCase))
                ctx.Backup.SaveState(Id, curGuid);

            // Переиспользовать ранее созданный Ultimate-дубль, если он ещё есть (сверка по GUID,
            // без локализованных имён), иначе создать новый из шаблона.
            var ult = (!string.IsNullOrEmpty(prevActive) && SchemeExists(prevActive))
                ? prevActive
                : ExtractGuid(RunPowercfg($"-duplicatescheme {UltimateTemplate}"));

            var target = ult ?? HighPerformance; // фолбэк — встроенный High Performance
            RunPowercfg($"-setactive {target}");
            ctx.Backup.SaveState(Id + ".active", target);
            ctx.Trace($"applied {Id}: active={target} (ultimate={ult is not null})");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            var saved = ctx.Backup.LoadState(Id);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                RunPowercfg($"-setactive {saved.Trim()}");
                ctx.Trace($"reverted {Id} -> {saved.Trim()}");
            }
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    /// <summary>GUID активной схемы питания (имя не разбираем — оно локализовано).</summary>
    private static string? ActiveScheme() => ExtractGuid(RunPowercfg("/getactivescheme"));

    /// <summary>Существует ли схема с таким GUID (GUID в выводе powercfg locale-независим).</summary>
    private static bool SchemeExists(string guid)
        => RunPowercfg("/list").Contains(guid, StringComparison.OrdinalIgnoreCase);

    private static string? ExtractGuid(string s)
    {
        var m = Regex.Match(s,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value : null;
    }

    private static string RunPowercfg(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
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
