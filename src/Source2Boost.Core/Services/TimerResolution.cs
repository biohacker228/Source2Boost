using System.Runtime.InteropServices;

namespace Source2Boost.Core;

/// <summary>
/// Активное удержание МАКСИМАЛЬНОГО разрешения системного таймера (обычно 0.5 мс) через
/// <c>NtSetTimerResolution</c>. Пока наш процесс держит запрос — планировщик Windows «просыпается»
/// чаще, что на многих системах заметно РОВНЯЕТ фреймтайм (меньше микрозадержек подачи кадра).
///
/// Отличие от твика <c>timer-resolution-global</c>: тот лишь РАЗРЕШАЕт точный таймер в полноэкранных
/// играх (реестр), а этот АКТИВНО ЗАПРАШИВАЕТ 0.5 мс здесь и сейчас. В связке с ним запрос
/// становится глобальным (влияет и на CS2), поэтому в описании твика советуем включить оба.
/// Запрос действует, пока жив наш процесс; при выходе Windows сама снимает его.
/// </summary>
public static class TimerResolution
{
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    /// <summary>Текущее фактическое разрешение таймера в 100-нс единицах (0 если не удалось).</summary>
    public static uint CurrentUnits()
    {
        try { return NtQueryTimerResolution(out _, out _, out var cur) == 0 ? cur : 0; }
        catch { return 0; }
    }

    /// <summary>Запросить максимально точный таймер (самое малое значение = ~0.5 мс). Идемпотентно.</summary>
    public static bool RequestMax()
    {
        try
        {
            if (NtQueryTimerResolution(out _, out var maxRes, out _) != 0) return false; // maxRes = самое точное
            return NtSetTimerResolution(maxRes, true, out _) == 0;
        }
        catch { return false; }
    }

    /// <summary>Снять наш запрос точного таймера (Windows вернёт разрешение по умолчанию).</summary>
    public static bool Reset()
    {
        try { return NtSetTimerResolution(0, false, out _) == 0; }
        catch { return false; }
    }
}
