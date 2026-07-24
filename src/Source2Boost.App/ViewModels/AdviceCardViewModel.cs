using Source2Boost.Core;

namespace Source2Boost.App.ViewModels;

/// <summary>Read-only карточка-совет с чипом уровня (предупреждение/рекомендуется/опционально).
/// Общая для списков «Что можно улучшить» (Диагноз) и «BIOS».</summary>
public sealed class AdviceCardViewModel
{
    public string Title { get; }
    public string ChipText { get; }
    /// <summary>Ключ ресурса цвета чипа (Crit/Accent/Muted) — резолвится DynamicResource в шаблоне.</summary>
    public string ChipForegroundKey { get; }
    public string Detail { get; }

    public AdviceCardViewModel(string title, string chipText, string chipKey, string detail)
    {
        Title = title;
        ChipText = chipText;
        ChipForegroundKey = chipKey;
        Detail = detail;
    }

    /// <summary>Собирает карточку из совета уровня BiosTipLevel в заданном языке.</summary>
    public static AdviceCardViewModel FromLevel(BiosTipLevel level, L10n title, L10n detail, string lang)
    {
        var (chipKey, levelKey) = level switch
        {
            BiosTipLevel.Warning     => ("Crit", "bios.level.warn"),
            BiosTipLevel.Recommended => ("Accent", "bios.level.rec"),
            _                        => ("Muted", "bios.level.opt"),
        };
        return new AdviceCardViewModel(title.For(lang), Loc.T(levelKey), chipKey, detail.For(lang));
    }
}
