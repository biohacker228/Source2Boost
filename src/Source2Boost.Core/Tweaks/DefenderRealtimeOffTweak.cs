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
        "🔴🔴 БЕЗБАШЕННО, НЕ обязателен: обычно хватает твика «Ускорить Defender: исключить CS2» (безопаснее и держится). Этот же полностью отключает антивирус в реальном времени — весь CPU со сканирования уходит в игру, но это СНИМАЕТ ЗАЩИТУ от вирусов. Только осознанно и на чистой системе. Обратимо. Часто НЕ держится: Windows возвращает Defender сама — тогда надо вручную выключить «Защиту от подделки» (Tamper Protection) в Безопасности Windows.",
        "🔴🔴 БЕЗБАШЕННО, НЕ обов'язковий: зазвичай досить твіка «Прискорити Defender: виключити CS2» (безпечніше й тримається). Цей повністю вимикає антивірус у реальному часі — CPU зі сканування йде в гру, але це ЗНІМАЄ ЗАХИСТ. Лише свідомо. Оборотно. Часто НЕ тримається: Windows повертає Defender — тоді вимкни «Захист від підробки» вручну.",
        "🔴🔴 UNHINGED, NOT required: the «Speed up Defender: exclude CS2» tweak is usually enough (safer, and it sticks). This one fully disables real-time protection — all scan CPU goes to the game, but it REMOVES virus protection. Only knowingly, on a clean system. Reversible. Often does NOT stick: Windows re-enables Defender unless you turn off Tamper Protection manually in Windows Security.");
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
                : TweakResult.Fail("Не удалось: включена Tamper Protection (Защита от подделки) — она сама откатит отключение Defender. Надёжнее и безопаснее используй твик «Исключение cs2.exe в Defender» — он не требует отключать Tamper Protection и не снимает защиту с системы.");
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
