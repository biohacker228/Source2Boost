namespace Source2Boost.Core;

/// <summary>Уровень риска твика. Влияет на то, в какой профиль он попадает и как подсвечивается в UI.</summary>
public enum RiskLevel
{
    /// <summary>Полностью обратимо, на стабильность не влияет.</summary>
    Safe,
    /// <summary>Глубже; применяется с бэкапом и точкой восстановления.</summary>
    Medium,
    /// <summary>Может задеть стабильность/безопасность/нагрев. Только профиль MAX + явное согласие.</summary>
    High,
    /// <summary>«Безбашенное»: сильная жертва безопасности/поведения ОС. НЕ входит ни в один профиль —
    /// только вручную в списке твиков, с явной пометкой. Полностью обратимо.</summary>
    Extreme
}

public enum TweakCategory
{
    Frametime,
    Nvidia,
    CpuPower,
    Memory,
    Services,
    Network
}

/// <summary>Профиль применения набора твиков.</summary>
public enum Profile
{
    /// <summary>«Безопасный» — только Safe-твики (риск 0, обратимо, без перезагрузки).</summary>
    Safe,
    /// <summary>«Оптимальный» — Safe + Medium. Баланс прироста и безопасности (профиль по умолчанию).</summary>
    Optimal,
    /// <summary>«Максимум» — всё, включая High/агрессив (Spectre off, bcdedit, Nagle). Максимум выжимания.</summary>
    Maximum
}

/// <summary>Строка на трёх языках. Ключи: ru, uk, en.</summary>
public sealed class L10n
{
    public string Ru { get; init; } = "";
    public string Uk { get; init; } = "";
    public string En { get; init; } = "";

    public L10n() { }
    public L10n(string ru, string uk, string en) { Ru = ru; Uk = uk; En = en; }

    public string For(string lang) => lang switch
    {
        "uk" => Uk,
        "en" => En,
        _ => Ru
    };
}

/// <summary>Результат применения/отката твика.</summary>
public readonly record struct TweakResult(bool Success, string? Message = null)
{
    public static TweakResult Ok(string? m = null) => new(true, m);
    public static TweakResult Fail(string m) => new(false, m);
}
