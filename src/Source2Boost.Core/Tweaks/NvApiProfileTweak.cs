namespace Source2Boost.Core;

/// <summary>
/// Игровой профиль драйвера NVIDIA через NVAPI (то же, что «Панель управления NVIDIA» →
/// «Управление параметрами 3D», но автоматически): питание = максимум производительности,
/// низкая задержка (1 предрендер-кадр), верт. синхронизация = выкл, кэш шейдеров = вкл.
/// Показывается только при рабочем NVAPI (драйвер NVIDIA установлен). Обратимо (Revert
/// возвращает значения по умолчанию). Эффект — при следующем запуске CS2.
/// </summary>
public sealed class NvApiProfileTweak : ITweak
{
    public string Id => "nvidia-driver-profile";
    public TweakCategory Category => TweakCategory.Nvidia;
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "Игровой профиль драйвера NVIDIA", "Ігровий профіль драйвера NVIDIA", "NVIDIA driver game profile");
    public L10n Description { get; } = new(
        "Автоматически ставит в драйвере NVIDIA игровые настройки (как вручную в панели управления): питание = максимум производительности, низкая задержка ввода, верт. синхронизация выключена, фильтрация текстур = производительность, кэш шейдеров включён. Обратимо; эффект при следующем запуске CS2.",
        "Автоматично ставить у драйвері NVIDIA ігрові налаштування (як вручну в панелі керування): живлення = максимум продуктивності, низька затримка вводу, верт. синхронізація вимкнена, фільтрація текстур = продуктивність, кеш шейдерів увімкнений. Оборотно; ефект при наступному запуску CS2.",
        "Automatically applies gaming settings in the NVIDIA driver (the same as doing it by hand in the control panel): power = max performance, low input latency, vertical sync off, texture filtering = performance, shader cache on. Reversible; takes effect next time you launch CS2.");
    public L10n Impact { get; } = new(
        "-задержка, +частота GPU", "-затримка, +частота GPU", "-latency, +GPU clocks");

    // Только при рабочем NVAPI (иначе твик скрыт — как и другие несовместимые).
    public bool IsSupported(TweakContext ctx) => ctx.Hardware.IsNvidia && NvApi.IsAvailable();

    public bool IsApplied(TweakContext ctx) => NvApi.IsGamingProfileApplied();

    public TweakResult Apply(TweakContext ctx)
    {
        var ok = NvApi.SetGamingProfile(true);
        ctx.Trace($"{Id}: apply -> {ok}");
        return ok ? TweakResult.Ok() : TweakResult.Fail("NVAPI отклонил запись (проверь версию драйвера).");
    }

    public TweakResult Revert(TweakContext ctx)
    {
        var ok = NvApi.SetGamingProfile(false);
        ctx.Trace($"{Id}: revert -> {ok}");
        return ok ? TweakResult.Ok() : TweakResult.Fail("NVAPI отклонил запись.");
    }
}
