namespace Source2Boost.Core;

/// <summary>
/// Числовая оценка оптимизации 0–100 — насколько применён потенциал наших твиков.
/// Считается ДЕТЕРМИНИРОВАННО: каждый поддерживаемый твик весит по риску
/// (Safe=1, Medium=2, High=3, т.к. более «жирные» твики дают больший прирост FPS),
/// score = 100 × вес_применённых / вес_всех_поддерживаемых. Плюс отдельно
/// возвращаем разбивку, чтобы показать пользователю «сделано X из Y».
/// </summary>
public sealed record ScoreResult(
    int Score,
    int AppliedCount,
    int SupportedCount,
    double AppliedWeight,
    double TotalWeight)
{
    /// <summary>Доля применённого (0..1) — для прогноза остаточного прироста.</summary>
    public double Fraction => TotalWeight <= 0 ? 0 : AppliedWeight / TotalWeight;
}

public static class OptimizationScore
{
    private static double Weight(RiskLevel r) => r switch
    {
        RiskLevel.Safe => 1.0,
        RiskLevel.Medium => 2.0,
        _ => 3.0,
    };

    public static ScoreResult Compute(TweakContext ctx)
    {
        double total = 0, applied = 0;
        int supported = 0, appliedCount = 0;

        foreach (var tw in TweakCatalog.All())
        {
            // «Безбашенные» Extreme и «скоро»-заглушки в оценку не считаем.
            if (tw.Risk == RiskLevel.Extreme || tw is IComingSoon) continue;

            bool ok;
            try { ok = tw.IsSupported(ctx); } catch { ok = false; }
            if (!ok) continue;                     // неприменимые на этом железе не штрафуют

            var w = Weight(tw.Risk);
            total += w;
            supported++;

            bool on;
            try { on = tw.IsApplied(ctx); } catch { on = false; }
            if (on) { applied += w; appliedCount++; }
        }

        var score = total <= 0 ? 0 : (int)Math.Round(100.0 * applied / total);
        return new ScoreResult(score, appliedCount, supported, applied, total);
    }
}
