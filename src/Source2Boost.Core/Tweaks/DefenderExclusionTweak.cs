using System.Diagnostics;

namespace Source2Boost.Core;

/// <summary>
/// Добавляет cs2.exe и его папку в исключения Microsoft Defender: антивирус в реальном
/// времени перестаёт сканировать игру — меньше рывков CPU на слабом процессоре.
/// Apply: Add-MpPreference; Revert: Remove-MpPreference. Пути берутся из Cs2Paths.
/// </summary>
public sealed class DefenderExclusionTweak : ITweak
{
    public string Id => "defender-exclusion-cs2";
    public TweakCategory Category => TweakCategory.Services;
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "Ускорить Defender: исключить CS2", "Прискорити Defender: виключити CS2", "Speed up Defender: exclude CS2");
    public L10n Description { get; } = new(
        "РЕКОМЕНДУЕМЫЙ способ убрать нагрузку от антивируса: Defender перестаёт сканировать cs2.exe и папку игры — меньше хитчей CPU. Защиту системы в целом НЕ снимает и, в отличие от полного отключения Defender, надёжно держится (не откатывается «Защитой от подделки»). Обратимо.",
        "РЕКОМЕНДОВАНИЙ спосіб прибрати навантаження від антивіруса: Defender перестає сканувати cs2.exe і папку гри — менше хітчів CPU. Захист системи НЕ знімає й, на відміну від повного вимкнення, надійно тримається. Оборотно.",
        "The RECOMMENDED way to remove AV overhead: Defender stops scanning cs2.exe and the game folder — fewer CPU hitches. It does NOT remove system protection and, unlike fully disabling Defender, it reliably sticks (Tamper Protection doesn't revert it). Reversible.");
    public L10n Impact { get; } = new(
        "-хитчи CPU", "-хітчі CPU", "-CPU hitches");

    private const string ProcessName = "cs2.exe";

    public bool IsSupported(TweakContext ctx) => Cs2Paths.Cs2ExePath() is not null;

    public bool IsApplied(TweakContext ctx)
    {
        var dir = Cs2Dir();
        if (dir is null) return false;
        var procs = RunPs("(Get-MpPreference).ExclusionProcess -join \"`n\"");
        var paths = RunPs("(Get-MpPreference).ExclusionPath -join \"`n\"");
        return procs.Contains(ProcessName, StringComparison.OrdinalIgnoreCase)
            && paths.Contains(dir, StringComparison.OrdinalIgnoreCase);
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            var dir = Cs2Dir();
            if (dir is null) return TweakResult.Fail("cs2 dir not found");
            var outp = RunPs($"Add-MpPreference -ExclusionProcess '{ProcessName}' -ExclusionPath '{Esc(dir)}'");
            ctx.Backup.SaveState(Id, dir); // запоминаем точный путь для аккуратного отката
            ctx.Trace($"applied {Id}: excl {ProcessName} + {dir} {outp.Trim()}");
            return IsApplied(ctx) ? TweakResult.Ok() : TweakResult.Fail("exclusion not present after apply");
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            var dir = ctx.Backup.LoadState(Id) ?? Cs2Dir();
            RunPs($"Remove-MpPreference -ExclusionProcess '{ProcessName}'");
            if (dir is not null) RunPs($"Remove-MpPreference -ExclusionPath '{Esc(dir)}'");
            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    private static string? Cs2Dir()
    {
        var exe = Cs2Paths.Cs2ExePath();
        return exe is null ? null : Path.GetDirectoryName(exe);
    }

    private static string Esc(string s) => s.Replace("'", "''");

    private static string RunPs(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "";
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return o;
        }
        catch { return ""; }
    }
}
