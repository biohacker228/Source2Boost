namespace Source2Boost.Core;

/// <summary>
/// Единая единица оптимизации. Из этих классов строятся оба режима:
/// «в один клик» (весь профиль) и продвинутый чек-лист (по одному).
/// </summary>
public interface ITweak
{
    /// <summary>Стабильный идентификатор (для сохранения состояния/логов).</summary>
    string Id { get; }

    TweakCategory Category { get; }
    RiskLevel Risk { get; }

    /// <summary>Короткое название на 3 языках.</summary>
    L10n Title { get; }

    /// <summary>Что делает и зачем — на 3 языках.</summary>
    L10n Description { get; }

    /// <summary>Ожидаемый эффект, короткой строкой (напр. "+ровность", "-латентность").</summary>
    L10n Impact { get; }

    /// <summary>Нужен ли перезапуск/перелогин для полного эффекта.</summary>
    bool RequiresRestart { get; }

    /// <summary>Применим ли твик на текущей машине (напр. NVIDIA-твик без NVIDIA — false).</summary>
    bool IsSupported(TweakContext ctx);

    /// <summary>Уже применён?</summary>
    bool IsApplied(TweakContext ctx);

    /// <summary>Применить. Бэкап уже сделан оркестратором до вызова.</summary>
    TweakResult Apply(TweakContext ctx);

    /// <summary>Откатить к исходному состоянию.</summary>
    TweakResult Revert(TweakContext ctx);
}

/// <summary>
/// Маркер «скоро»: твик показывается в списке, но неактивен (тогл выключен, бейдж «скоро»).
/// Не входит ни в один профиль и не учитывается в оценке оптимизации.
/// </summary>
public interface IComingSoon { }

/// <summary>
/// Маркер «эксперимент» (Лаборатория): твик РАБОТАЕТ и полностью обратим, но его польза
/// пока НЕ доказана — эффект надо мерить бенчмарком на конкретном железе (A/B). Поэтому он
/// НЕ входит в профили «в один клик» и НЕ учитывается в оценке оптимизации; применяется
/// только вручную из списка твиков, с бейджем «эксперимент». Доказавшие прирост переезжают
/// в обычный каталог, вредные — удаляются.
/// </summary>
public interface IExperimental { }

/// <summary>
/// Декоратор, помечающий любой существующий твик как экспериментальный (<see cref="IExperimental"/>).
/// Полностью делегирует поведение вложенному твику — меняется только классификация
/// (вне профилей/оценки, отдельный бейдж в UI). Это позволяет «обкатывать» кандидата, не
/// переписывая его логику, и одним движением перевести его в прод (убрать обёртку).
/// </summary>
public sealed class ExperimentalTweak : ITweak, IExperimental
{
    private readonly ITweak _inner;
    public ExperimentalTweak(ITweak inner) => _inner = inner;

    public string Id => _inner.Id;
    public TweakCategory Category => _inner.Category;
    public RiskLevel Risk => _inner.Risk;
    public L10n Title => _inner.Title;
    public L10n Description => _inner.Description;
    public L10n Impact => _inner.Impact;
    public bool RequiresRestart => _inner.RequiresRestart;
    public bool IsSupported(TweakContext ctx) => _inner.IsSupported(ctx);
    public bool IsApplied(TweakContext ctx) => _inner.IsApplied(ctx);
    public TweakResult Apply(TweakContext ctx) => _inner.Apply(ctx);
    public TweakResult Revert(TweakContext ctx) => _inner.Revert(ctx);
}

/// <summary>Контекст выполнения: инфо о железе, логер, сервис бэкапа.</summary>
public sealed class TweakContext
{
    public required HardwareInfo Hardware { get; init; }
    public required IBackupService Backup { get; init; }
    public Action<string>? Log { get; init; }

    public void Trace(string m) => Log?.Invoke(m);
}
