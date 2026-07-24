using System.Diagnostics;

namespace Source2Boost.Core;

/// <summary>
/// «Безбашенный» твик (Extreme): полностью выключает защиту Microsoft Defender в реальном
/// времени (Set-MpPreference -DisableRealtimeMonitoring $true). Возвращает CPU, который АВ
/// тратит на постоянное сканирование, — но это РЕАЛЬНАЯ ДЫРА в безопасности. НЕ входит ни в
/// один профиль, только вручную. Обратимо. ВНИМАНИЕ: при включённой Tamper Protection Windows
/// заблокирует изменение — тогда нужно сперва выключить «Защиту от подделки» вручную.
/// </summary>
public sealed class DefenderRealtimeOffTweak : ITweak
{
    public string Id => "defender-realtime-off";
    public TweakCategory Category => TweakCategory.Services;
    public RiskLevel Risk => RiskLevel.Extreme;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "Выключить Defender (realtime)", "Вимкнути Defender (realtime)", "Disable Defender (realtime)");
    public L10n Description { get; } = new(
        "🔴🔴 БЕЗБАШЕННО: полностью отключает антивирус Defender в реальном времени — весь CPU, что он тратил на сканирование, уходит в игру. Это СНИМАЕТ ЗАЩИТУ от вирусов, включай только осознанно и на чистой системе. Обратимо. Если не срабатывает — выключи «Защиту от подделки» (Tamper Protection) в Безопасности Windows.",
        "🔴🔴 БЕЗБАШЕННО: повністю вимикає антивірус Defender у реальному часі — весь CPU, що він витрачав на сканування, іде в гру. Це ЗНІМАЄ ЗАХИСТ від вірусів, вмикай лише свідомо й на чистій системі. Оборотно. Якщо не спрацьовує — вимкни «Захист від підробки» (Tamper Protection).",
        "🔴🔴 UNHINGED: fully disables Defender real-time protection — all the CPU it spent scanning goes to the game. This REMOVES virus protection; enable only knowingly on a clean system. Reversible. If it doesn't stick, turn off Tamper Protection in Windows Security first.");
    public L10n Impact { get; } = new(
        "+CPU (−−защита)", "+CPU (−−захист)", "+CPU (−−security)");

    public bool IsSupported(TweakContext ctx) => true;

    public bool IsApplied(TweakContext ctx)
        => RunPs("(Get-MpPreference).DisableRealtimeMonitoring").Trim()
            .StartsWith("True", StringComparison.OrdinalIgnoreCase);

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            var outp = RunPs("Set-MpPreference -DisableRealtimeMonitoring $true");
            ctx.Trace($"applied {Id}: {outp.Trim()}");
            return IsApplied(ctx)
                ? TweakResult.Ok()
                : TweakResult.Fail("Не удалось — вероятно включена Tamper Protection. Выключи её вручную и повтори.");
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            RunPs("Set-MpPreference -DisableRealtimeMonitoring $false");
            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

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
