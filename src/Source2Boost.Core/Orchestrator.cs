namespace Source2Boost.Core;

/// <summary>Итог применения одного твика.</summary>
public sealed record AppliedTweak(ITweak Tweak, TweakResult Result);

/// <summary>
/// Оркестратор: применяет/откатывает наборы твиков, создаёт точку
/// восстановления перед агрессивными профилями, собирает результаты.
/// </summary>
public sealed class Orchestrator
{
    private readonly TweakContext _ctx;
    public Orchestrator(TweakContext ctx) => _ctx = ctx;

    /// <summary>Применить профиль целиком (режим «в один клик»).</summary>
    public IReadOnlyList<AppliedTweak> ApplyProfile(Profile profile)
        => Apply(TweakCatalog.ForProfile(profile), createRestorePoint: profile != Profile.Safe);

    /// <summary>Применить выбранный набор (режим чек-листа).</summary>
    public IReadOnlyList<AppliedTweak> Apply(IEnumerable<ITweak> tweaks, bool createRestorePoint)
    {
        var list = tweaks.ToList();
        var risky = list.Any(t => t.Risk != RiskLevel.Safe);
        if (createRestorePoint && risky)
            _ctx.Backup.CreateRestorePoint("Source2Boost — перед оптимизацией");

        var results = new List<AppliedTweak>(list.Count);
        foreach (var t in list)
        {
            if (!t.IsSupported(_ctx)) { _ctx.Trace($"skip (unsupported): {t.Id}"); continue; }
            if (t.IsApplied(_ctx)) { results.Add(new AppliedTweak(t, TweakResult.Ok("already applied"))); continue; }
            results.Add(new AppliedTweak(t, t.Apply(_ctx)));
        }
        return results;
    }

    /// <summary>Откатить конкретные твики.</summary>
    public IReadOnlyList<AppliedTweak> Revert(IEnumerable<ITweak> tweaks)
        => tweaks.Select(t => new AppliedTweak(t, t.Revert(_ctx))).ToList();

    /// <summary>Откатить всё, что каталог умеет.</summary>
    public IReadOnlyList<AppliedTweak> RevertAll() => Revert(TweakCatalog.All());
}
