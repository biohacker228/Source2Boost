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

/// <summary>Контекст выполнения: инфо о железе, логер, сервис бэкапа.</summary>
public sealed class TweakContext
{
    public required HardwareInfo Hardware { get; init; }
    public required IBackupService Backup { get; init; }
    public Action<string>? Log { get; init; }

    public void Trace(string m) => Log?.Invoke(m);
}
