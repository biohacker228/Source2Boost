using System.Text.RegularExpressions;

namespace Source2Boost.Core;

/// <summary>
/// Прогноз среднего FPS в CS2. ВАЖНО: это ОРИЕНТИР, а не точный замер — CS2 сильно
/// зависит от карты/режима/настроек. Если есть реальный замер PresentMon, берём его
/// как «текущий»; иначе оцениваем по железу грубой эвристикой (CS2 упирается в CPU/память).
/// «Потенциал» = текущий × остаточный запас наших твиков (программный потолок ~25%),
/// плюс отдельная прикидка с BIOS (XMP/выключенный VBS).
/// </summary>
public sealed record FpsForecast(
    int CurrentAvg,
    int PotentialSoftwareAvg,
    int PotentialWithBiosAvg,
    bool FromMeasurement);

public static class FpsEstimator
{
    // Программный потолок прироста от наших твиков (из практики на старом железе).
    private const double SoftwareCeiling = 0.25;

    /// <summary>Грубая оценка среднего FPS в CS2 только по железу (без замера).</summary>
    public static int HeuristicBaseline(HardwareInfo hw)
    {
        int threads = hw.CpuThreads > 0 ? hw.CpuThreads : Math.Max(2, hw.CpuCores);
        double perThread = 15.0;                 // ~FPS на поток, средние настройки
        double gen = CpuGenMultiplier(hw.CpuName);
        double raw = threads * perThread * gen;  // напр. i7-6700: 8×15×0.87 ≈ 104

        double gpuCap = GpuSoftCap(hw);          // слабая встройка ограничивает сверху
        double est = Math.Min(raw, gpuCap);
        return (int)Math.Round(Math.Clamp(est, 40, 600));
    }

    public static FpsForecast Forecast(HardwareInfo hw, double appliedFraction, double? measuredAvg)
    {
        bool fromMeas = measuredAvg is > 0;
        double current = fromMeas ? measuredAvg!.Value : HeuristicBaseline(hw);

        // Остаточный программный запас: чем меньше применено, тем больше можно добрать.
        double unrealized = Math.Clamp(1.0 - appliedFraction, 0, 1);
        double potentialSoftware = current * (1.0 + SoftwareCeiling * unrealized);

        // BIOS-прикидка поверх программного максимума.
        double biosUplift = 0;
        if (hw.RamMixedKit || (hw.RamSpeedMhz > 0 && hw.RamSpeedMhz <= 2400)) biosUplift += 0.10; // XMP/память
        if (BiosAdvisor.MemoryIntegrityOn()) biosUplift += 0.05;                                   // VBS off
        double potentialBios = potentialSoftware * (1.0 + biosUplift);

        return new FpsForecast(
            CurrentAvg: (int)Math.Round(current),
            PotentialSoftwareAvg: (int)Math.Round(potentialSoftware),
            PotentialWithBiosAvg: (int)Math.Round(potentialBios),
            FromMeasurement: fromMeas);
    }

    /// <summary>Множитель поколения CPU (Skylake 6-е ≈ 0.87, новее — выше). Грубо по названию.</summary>
    private static double CpuGenMultiplier(string cpuName)
    {
        var gen = IntelCoreGen(cpuName);
        if (gen > 0) return Math.Clamp(0.75 + 0.06 * (gen - 4), 0.7, 1.7);
        if (cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
        {
            // Ryzen: по первой цифре серии (1000..7000).
            var m = Regex.Match(cpuName, @"Ryzen\s+\d\s+(\d)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var s))
                return Math.Clamp(0.85 + 0.08 * (s - 1), 0.85, 1.6);
            return 1.1;
        }
        return 1.0; // неизвестный CPU — нейтрально
    }

    /// <summary>Поколение Intel Core из названия (i7-6700 → 6, i5-12400 → 12). 0 если не Intel Core.</summary>
    private static int IntelCoreGen(string cpuName)
    {
        var m = Regex.Match(cpuName, @"i[3579]-(\d{4,5})");
        if (!m.Success) return 0;
        var num = m.Groups[1].Value;
        // 4-значные → первая цифра (6700→6); 5-значные → первые две (12400→12).
        return num.Length == 5 ? int.Parse(num.Substring(0, 2)) : int.Parse(num.Substring(0, 1));
    }

    /// <summary>Верхняя граница FPS из-за GPU. Дискретные не ограничивают CS2; встройки — да.</summary>
    private static double GpuSoftCap(HardwareInfo hw)
    {
        if (hw.IsNvidia || hw.GpuVendor.Equals("AMD", StringComparison.OrdinalIgnoreCase))
            return 600;                          // дискретная — CS2 в неё не упрётся на этих CPU
        return 90;                               // встроенная графика — реальный потолок
    }
}
