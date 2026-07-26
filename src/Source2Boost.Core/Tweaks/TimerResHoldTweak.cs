namespace Source2Boost.Core;

/// <summary>
/// Эксперимент: активно удерживать максимально точный системный таймер (~0.5 мс) через
/// <see cref="TimerResolution"/>, пока работает приложение. На многих системах ровняет фреймтайм,
/// но выигрыш НЕ гарантирован — кандидат на замер. Флаг-намерение хранится в состоянии; сам запрос
/// живёт в процессе приложения (переустанавливается при старте App, как и аффинити CS2).
/// В связке с реестровым <c>timer-resolution-global</c> запрос становится глобальным (влияет на CS2).
/// Полностью обратим. Помечен <see cref="IExperimental"/> — вне профилей и оценки.
/// </summary>
public sealed class TimerResHoldTweak : ITweak, IExperimental
{
    public const string StateKey = "timer-res-hold";

    public string Id => "timer-res-hold";
    public TweakCategory Category => TweakCategory.Frametime;
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "Держать таймер 0.5 мс", "Тримати таймер 0.5 мс", "Hold 0.5 ms timer");

    public L10n Description { get; } = new(
        "Приложение активно запрашивает максимально точный системный таймер (~0.5 мс) и держит его, пока запущено — планировщик Windows «просыпается» чаще, на многих ПК ровнее фреймтайм. Работает сразу (перезагрузка не нужна). Для влияния на CS2 включи ещё реестровый «Глобальный таймер разрешения». Выигрыш НЕ гарантирован — замерь до/после. Полностью обратимо.",
        "Застосунок активно запитує максимально точний системний таймер (~0.5 мс) і тримає його, поки запущений — планувальник Windows «прокидається» частіше, на багатьох ПК рівніший фреймтайм. Працює одразу. Для впливу на CS2 увімкни ще реєстровий «Глобальний таймер роздільності». Виграш НЕ гарантований — зміряй. Повністю оборотно.",
        "The app actively requests the finest system timer (~0.5 ms) and holds it while running — Windows' scheduler wakes more often, smoothing frametime on many PCs. Works immediately (no reboot). To affect CS2, also enable the registry 'Global timer resolution'. Benefit NOT guaranteed — measure before/after. Fully reversible.");

    public L10n Impact { get; } = new(
        "?ровность (замерь)", "?рівність (зміряй)", "?smoothness (measure)");

    public bool IsSupported(TweakContext ctx) => true;

    public bool IsApplied(TweakContext ctx) => ctx.Backup.LoadState(StateKey) == "1";

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            ctx.Backup.SaveState(StateKey, "1");
            bool ok = TimerResolution.RequestMax();
            ctx.Trace($"{Id}: enabled, request 0.5ms timer -> {(ok ? "ok" : "failed")}, current={TimerResolution.CurrentUnits()}x100ns");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            ctx.Backup.SaveState(StateKey, "0");
            TimerResolution.Reset();
            ctx.Trace($"{Id}: disabled, timer request released");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }
}
