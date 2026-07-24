using System.Windows.Media;
using Source2Boost.App.Mvvm;
using Source2Boost.Core;

namespace Source2Boost.App.ViewModels;

/// <summary>
/// Одна строка списка «Твики»: заголовок/описание/эффект + состояние тумблера (IsChecked,
/// two-way). Заменяет ручную сборку карточки в code-behind — теперь это DataTemplate.
/// </summary>
public sealed class TweakRowViewModel : ObservableObject
{
    public ITweak Tweak { get; }
    public bool IsSoon { get; }
    public bool IsExperimental { get; }

    public string Title { get; }
    public string Description { get; }
    public string Impact { get; }

    public string ChipText { get; }
    /// <summary>Полупрозрачная заливка чипа (одинакова в обеих темах, потому фиксирована).</summary>
    public Brush ChipBackground { get; }
    /// <summary>Ключ ресурса цвета текста чипа (Good/Warn/Crit/Muted) — резолвится через
    /// DynamicResource в шаблоне, чтобы менялся вместе с темой.</summary>
    public string ChipForegroundKey { get; }

    public bool IsEnabled => !IsSoon;
    public double RowOpacity => IsSoon ? 0.55 : 1.0;

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }

    public TweakRowViewModel(ITweak tw, bool isSoon, bool isChecked, string lang)
    {
        Tweak = tw;
        IsSoon = isSoon;
        IsExperimental = tw is IExperimental;
        _isChecked = isChecked;
        Title = tw.Title.For(lang);
        Description = tw.Description.For(lang);
        Impact = tw.Impact.For(lang);

        (ChipText, ChipBackground, ChipForegroundKey) =
            isSoon ? RiskChip.Soon() :
            IsExperimental ? RiskChip.Experimental() :
            RiskChip.For(tw.Risk);
    }
}
