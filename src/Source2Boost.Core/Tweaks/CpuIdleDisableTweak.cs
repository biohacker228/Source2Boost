using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Source2Boost.Core;

/// <summary>
/// Отключает простой процессора (C-states) на активной схеме питания: SUB_PROCESSOR
/// IDLEDISABLE=1. CPU держит частоту постоянно, без ухода в сон ядер — максимально ровный
/// фреймтайм и минимальная латентность пробуждения. ЦЕНА: выше энергопотребление и нагрев
/// (тепловой троттлинг всё равно защитит). Обратимо. High.
/// Парсинг powercfg — locale-independent (берём предпоследний 0x, вывод локализован).
/// </summary>
public sealed class CpuIdleDisableTweak : ITweak
{
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string IdleDisable = "5d76a2ca-e8c0-402f-a133-2158492d58ad";

    public string Id => "cpu-idle-disable";
    public TweakCategory Category => TweakCategory.CpuPower;
    public RiskLevel Risk => RiskLevel.High;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "Отключить простой CPU (C-states)", "Вимкнути простій CPU (C-states)", "Disable CPU idle (C-states)");
    public L10n Description { get; } = new(
        "🔴 Запрещает ядрам процессора уходить в сон — частота держится постоянно: самый ровный фреймтайм и мгновенный отклик. Цена: выше нагрев и энергопотребление (следи за температурой CPU). Обратимо.",
        "🔴 Забороняє ядрам процесора йти у сон — частота тримається постійно: найрівніший фреймтайм і миттєвий відгук. Ціна: вищий нагрів і споживання (слідкуй за температурою CPU). Оборотно.",
        "🔴 Stops the CPU cores from sleeping — clocks stay pinned: steadiest frametime and instant response. Cost: higher heat and power draw (watch CPU temps). Reversible.");
    public L10n Impact { get; } = new(
        "+ровный фреймтайм (−нагрев)", "+рівний фреймтайм (−нагрів)", "+steady frametime (−heat)");

    public bool IsSupported(TweakContext ctx) => true;

    public bool IsApplied(TweakContext ctx) => QueryAcIndex() == 1;

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            if (ctx.Backup.LoadState(Id) is null)
            {
                var cur = QueryAcIndex();
                ctx.Backup.SaveState(Id, cur?.ToString(CultureInfo.InvariantCulture) ?? "0");
            }
            RunPowercfg($"-attributes {SubProcessor} {IdleDisable} -ATTRIB_HIDE"); // раскрыть скрытую настройку
            RunPowercfg($"-setacvalueindex scheme_current {SubProcessor} {IdleDisable} 1");
            RunPowercfg("-setactive scheme_current");
            ctx.Trace($"applied {Id}: IDLEDISABLE=1");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            var orig = int.TryParse(ctx.Backup.LoadState(Id), out var v) ? v : 0; // дефолт: простой разрешён
            RunPowercfg($"-setacvalueindex scheme_current {SubProcessor} {IdleDisable} {orig}");
            RunPowercfg("-setactive scheme_current");
            ctx.Trace($"reverted {Id} -> {orig}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    /// <summary>AC-индекс IDLEDISABLE активной схемы (locale-independent: предпоследний 0x).</summary>
    private static int? QueryAcIndex()
    {
        var outp = RunPowercfg($"/query scheme_current {SubProcessor} {IdleDisable}");
        var ms = Regex.Matches(outp, @"0x([0-9a-fA-F]+)");
        if (ms.Count < 2) return null;
        return int.TryParse(ms[ms.Count - 2].Groups[1].Value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
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
