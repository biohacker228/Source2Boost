using System.Text.RegularExpressions;

namespace Source2Boost.Core;

/// <summary>Грубый класс «силы» ПК для CS2 — только для подбора профиля по умолчанию и текста-диагноза.
/// Это НЕ точный бенчмарк (для точного есть PresentMon), а эвристика по железу.</summary>
public enum HardwareTier
{
    /// <summary>Слабый по меркам CS2 2020-х: старая платформа/мало потоков/DDR3. Нужно выжимать всё.</summary>
    Weak,
    /// <summary>Бюджетный, но живой: 6 потоков / средняя платформа.</summary>
    Budget,
    /// <summary>Массовый: 8 потоков современной платформы.</summary>
    Mainstream,
    /// <summary>Мощный: 12+ потоков.</summary>
    HighEnd,
    /// <summary>Энтузиастский: 16–24+ потоков современной платформы.</summary>
    Enthusiast
}

/// <summary>
/// Классификатор железа под CS2. Главная ценность — НЕ ярлык «слабый/мощный», а два вывода:
/// (1) какой профиль по умолчанию рекомендовать, (2) человекочитаемый диагноз «почему».
///
/// Ключевая идея универсальности: на СТАРОЙ платформе выигрыш даёт снятие митигаций/приоритеты
/// (профиль «Максимум»), а на МОЩНОЙ современной — упор не в мощность, а в движок Source 2
/// (один поток + топология CPU): аффинити, таймер, MPO, standby-память, low-latency (профиль
/// «Оптимальный»). Поэтому «даже на мощном ПК 300→500» лечится не грубой силой, а сглаживанием.
/// </summary>
public static class HardwareClassifier
{
    public sealed record Result(
        HardwareTier Tier,
        Profile RecommendedProfile,
        CpuTopologyKind Topology,
        L10n Headline,   // краткий диагноз: почему именно этот профиль
        L10n Focus);     // на что делаем упор (набор твиков)

    private static Result? _cached;

    /// <summary>Классифицировать текущую машину. Кэшируется на сессию.</summary>
    public static Result Classify(HardwareInfo? hw)
    {
        if (_cached is not null) return _cached;
        if (hw is null) return Fallback(new HardwareInfo());   // не кэшируем «пустой» ответ
        try { _cached = Compute(hw); }
        catch { _cached = Fallback(hw); }
        return _cached;
    }

    private static Result Fallback(HardwareInfo hw) => new(
        HardwareTier.Mainstream, Profile.Optimal, CpuTopologyKind.Simple,
        new L10n("Профиль подобран по умолчанию.", "Профіль підібрано за замовчуванням.", "Default profile selected."),
        new L10n("Сглаживание фреймтайма и приоритет CS2.", "Згладжування фреймтайму та пріоритет CS2.",
                 "Frametime smoothing and CS2 priority."));

    private static Result Compute(HardwareInfo hw)
    {
        int threads = hw.CpuThreads > 0 ? hw.CpuThreads : Math.Max(hw.CpuCores, 1);
        var topo = CpuTopology.Detect(hw).Kind;

        // Возраст платформы: DDR3/низкий DDR4 и поколение CPU — главный сигнал «старое/слабое».
        bool ddr3Like = hw.RamRatedMhz > 0 && hw.RamRatedMhz <= 2133;
        bool ddr5Like = hw.RamRatedMhz >= 4000;
        int gen = CpuGeneration(hw.CpuName, out bool isAmd);
        bool old = ddr3Like
                   || (!isAmd && gen is > 0 and <= 7)     // Intel ≤ 7-го поколения (Skylake/Kaby и старше)
                   || (isAmd && gen is > 0 and <= 2);      // Ryzen 1000/2000
        bool modern = ddr5Like
                      || (!isAmd && gen >= 11)             // Intel 11-е поколение и новее
                      || (isAmd && gen >= 5);              // Ryzen 5000 и новее

        // Тир: сначала по потокам, потом коррекция на возраст платформы.
        HardwareTier tier =
            threads >= 24 || (threads >= 16 && modern) ? HardwareTier.Enthusiast :
            threads >= 12 ? HardwareTier.HighEnd :
            threads >= 8 && !old ? HardwareTier.Mainstream :
            threads >= 6 && !old ? HardwareTier.Budget :
            threads >= 8 && old ? HardwareTier.Budget :   // i7-6700/7700: 8 потоков, но старая платформа
            HardwareTier.Weak;

        if (hw.RamSingleChannel && tier > HardwareTier.Weak) tier--;   // одноканал — крупное узкое место
        if (!hw.HasDiscreteGpu && tier > HardwareTier.Weak) tier--;    // только встройка

        // Рекомендуемый профиль:
        //   старая платформа/слабый  → «Максимум» (снятие митигаций/приоритеты дают тут больше всего %);
        //   современный              → «Оптимальный» (упор в движок, безопасность не жертвуем зря).
        Profile rec = (tier == HardwareTier.Weak || old) ? Profile.Maximum : Profile.Optimal;

        var (head, focus) = Diagnose(topo, tier, old, hw);
        return new Result(tier, rec, topo, head, focus);
    }

    /// <summary>Диагноз «почему такой профиль» + на что упор. Приоритет — по топологии CPU.</summary>
    private static (L10n head, L10n focus) Diagnose(CpuTopologyKind topo, HardwareTier tier, bool old, HardwareInfo hw)
    {
        switch (topo)
        {
            case CpuTopologyKind.IntelHybrid:
                return (
                    new L10n(
                        "Гибрид P/E-ядра. Планировщик Windows иногда кидает CS2 на медленные E-ядра — сажаем игру на быстрые P-ядра.",
                        "Гібрид P/E-ядра. Планувальник Windows іноді кидає CS2 на повільні E-ядра — саджаємо гру на швидкі P-ядра.",
                        "Hybrid P/E cores. Windows sometimes throws CS2 onto slow E-cores — we pin the game to the fast P-cores."),
                    new L10n("Аффинити на P-ядра + сглаживание фреймтайма.",
                             "Афініті на P-ядра + згладжування фреймтайму.",
                             "P-core affinity + frametime smoothing."));

            case CpuTopologyKind.AmdMultiCcd:
                return (
                    new L10n(
                        "Несколько CCD-чиплетов. Частая причина «дёрганья» на мощных Ryzen — переходы между CCD; держим CS2 на кэш-CCD.",
                        "Кілька CCD-чиплетів. Часта причина «смикання» на потужних Ryzen — переходи між CCD; тримаємо CS2 на кеш-CCD.",
                        "Multiple CCD chiplets. A common cause of stutter on strong Ryzen is cross-CCD hops; we keep CS2 on the cache CCD."),
                    new L10n("Аффинити на CCD0 + сглаживание фреймтайма.",
                             "Афініті на CCD0 + згладжування фреймтайму.",
                             "CCD0 affinity + frametime smoothing."));
        }

        // Простой CPU (один кластер) — делим по возрасту платформы.
        if (old || tier == HardwareTier.Weak)
            return (
                new L10n(
                    "По меркам CS2 это слабый CPU — выжимаем максимум: снятие тяжёлых митигаций, приоритет игры, точные таймеры.",
                    "За мірками CS2 це слабкий CPU — вичавлюємо максимум: зняття важких мітигацій, пріоритет гри, точні таймери.",
                    "For CS2 this is a weak CPU — we squeeze everything: dropping heavy mitigations, game priority, precise timers."),
                new L10n("Снятие митигаций + приоритет + питание CPU.",
                         "Зняття мітигацій + пріоритет + живлення CPU.",
                         "Mitigations off + priority + CPU power."));

        // Современный сильный/средний одноядерно-упорный: упор в движок, не в мощность.
        return (
            new L10n(
                "Мощности хватает — но Source 2 упирается в один поток. Лечим не силой, а сглаживанием: точный таймер, отключение MPO, приоритет, чистка standby-памяти, low-latency.",
                "Потужності вистачає — але Source 2 впирається в один потік. Лікуємо не силою, а згладжуванням: точний таймер, вимкнення MPO, пріоритет, чистка standby-пам'яті, low-latency.",
                "You have the horsepower — but Source 2 is single-thread bound. We fix it with smoothing, not brute force: precise timer, MPO off, priority, standby-memory cleanup, low-latency."),
            new L10n("Анти-стуттер набор: таймер, MPO, standby-память, low-latency.",
                     "Анти-стуттер набір: таймер, MPO, standby-пам'ять, low-latency.",
                     "Anti-stutter set: timer, MPO, standby memory, low-latency."));
    }

    /// <summary>
    /// Поколение CPU из названия. Intel «i7-6700» → 6, «i5-12400F» → 12, «i9-9900K» → 9.
    /// AMD «Ryzen 5 3600» → 3, «Ryzen 7 5800X3D» → 5, «Ryzen 5 7600» → 7. 0 = не распознано.
    /// </summary>
    internal static int CpuGeneration(string cpuName, out bool isAmd)
    {
        cpuName ??= "";
        isAmd = cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                || cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);

        if (isAmd)
        {
            // Ryzen N XXXX — берём тысячи из 4-значного модельного номера.
            var m = Regex.Match(cpuName, @"Ryzen\s+\d\s+(\d{4,5})", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var model))
            {
                int series = model >= 10000 ? model / 1000 : model / 1000; // 3600→3, 5800→5, 7600→7
                return series;
            }
            return 0;
        }

        // Intel Core iX-NNNN(K/F/…). Поколение = ведущие цифры номера (2 цифры если 5-значный).
        var mi = Regex.Match(cpuName, @"i[3579]-(\d{4,5})", RegexOptions.IgnoreCase);
        if (mi.Success)
        {
            var digits = mi.Groups[1].Value;
            // 5 цифр (12400, 14700) → первые две; 4 цифры (6700, 9900) → первая.
            return digits.Length >= 5 ? int.Parse(digits[..2]) : int.Parse(digits[..1]);
        }
        return 0;
    }

    /// <summary>Сброс кэша (для тестов/смены железа).</summary>
    public static void ResetCache() => _cached = null;
}
