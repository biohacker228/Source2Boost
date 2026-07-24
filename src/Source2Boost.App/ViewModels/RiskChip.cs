using System.Windows.Media;
using Source2Boost.Core;

namespace Source2Boost.App.ViewModels;

/// <summary>Чип риска (текст + полупрозрачная заливка + ключ цвета текста) — общий для
/// списков «Твики» и «Откат», чтобы одинаковый вид не дублировался в двух местах.</summary>
public static class RiskChip
{
    public static (string Text, Brush Background, string ForegroundKey) For(RiskLevel risk) => risk switch
    {
        RiskLevel.Safe   => (Loc.T("risk.safe"),    Soft(0x32, 0xCD, 0x32), "Good"),
        RiskLevel.Medium => (Loc.T("risk.medium"),  Soft(0xF5, 0xC5, 0x3D), "Warn"),
        RiskLevel.High   => (Loc.T("risk.high"),    Soft(0xFF, 0x6B, 0x6F), "Crit"),
        _                => (Loc.T("risk.extreme"), Soft(0xC0, 0x1F, 0x4A), "Crit"),
    };

    public static (string Text, Brush Background, string ForegroundKey) Soon()
        => (Loc.T("risk.soon"), Soft(0x8D, 0x80, 0x69), "Muted");

    /// <summary>Бейдж «эксперимент» (Лаборатория) — фиолетовый, отличается от риск-чипов.</summary>
    public static (string Text, Brush Background, string ForegroundKey) Experimental()
        => (Loc.T("risk.experimental"), Soft(0x9B, 0x6D, 0xFF), "Accent");

    private static SolidColorBrush Soft(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(40, r, g, b));
        brush.Freeze();
        return brush;
    }
}
