using System.Text;
using System.Text.RegularExpressions;

namespace Source2Boost.Core;

/// <summary>
/// Генерирует/откатывает игровой конфиг графики CS2 (cs2_video.txt) под МАКСИМАЛЬНЫЙ FPS
/// (competitive-low: тени/эффекты/AO/шейдеры low, MSAA off, VSync off). НЕ трогает разрешение,
/// частоту обновления и идентификаторы GPU — только настройки качества. Оригинал сохраняется
/// побайтово для точного отката.
///
/// ВАЖНО: CS2 читает файл при СТАРТЕ и перезаписывает при выходе. Поэтому применять нужно, когда
/// игра ЗАКРЫТА (иначе она затрёт изменения). Значения из нашего конфига станут активными при
/// следующем запуске и сохранятся, пока пользователь не поменяет графику в самой игре.
/// </summary>
public static class Cs2VideoConfig
{
    /// <summary>Настройки max-FPS: ключ setting.* → значение. Только качество, без разрешения/устройства.</summary>
    private static readonly (string key, string val)[] MaxFps =
    {
        ("setting.cpu_level", "0"),                  // legacy master → low
        ("setting.gpu_level", "0"),                  // legacy master → low
        ("setting.videocfg_shadow_quality", "0"),   // тени low
        ("setting.videocfg_dynamic_shadows", "0"),  // динамические тени OFF (макс FPS)
        ("setting.videocfg_texture_detail", "0"),   // текстуры low
        ("setting.videocfg_ao_detail", "0"),        // ambient occlusion off
        ("setting.videocfg_particle_detail", "0"),  // частицы low
        // FidelityFX (FSR) ПОЛНОСТЬЮ выключен: рендер в нативном разрешении, без апскейла и блюра.
        // FSR поднимает FPS за счёт рендера в пониженном разрешении — но мылит картинку, для
        // соревновательной чёткости выключаем (три ключа = гарантированно нативно).
        ("setting.videocfg_fsr_detail", "0"),       // дропдаун FidelityFX → Disabled
        ("setting.r_csgo_fsr_upsample", "0"),       // апскейл FSR off
        ("setting.mat_viewportscale", "1"),         // рендер-скейл 100% (нативно)
        ("setting.videocfg_hdr_detail", "0"),       // HDR off (реальный прирост, часто включён по умолчанию)
        ("setting.shaderquality", "0"),             // шейдеры low
        ("setting.msaa_samples", "0"),              // MSAA off
        ("setting.r_texturefilteringquality", "0"), // фильтрация текстур минимум (bilinear)
        ("setting.r_csgo_cmaa_enable", "0"),        // CMAA off
        ("setting.mat_vsync", "0"),                 // VSync OFF (важно для FPS и задержки)
        // NVIDIA Reflex ВЫКЛЮЧЕН (по решению: некоторым он ощутимо портит плавность фреймтайма).
        // Технически Reflex снижает задержку без потери качества картинки, но это дело вкуса —
        // на нашем CPU-bound железе эффект минимален, а ровность важнее.
        ("setting.r_low_latency", "0"),             // Reflex OFF
    };

    public static string? ConfigPath() => Cs2Paths.Cs2VideoConfigPath();

    private static string BackupPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Source2Boost", "cs2_video_original.txt");

    /// <summary>Наш max-FPS конфиг уже применён? (все ключевые значения на месте).</summary>
    public static bool IsApplied()
    {
        var path = ConfigPath();
        if (path is null || !File.Exists(path)) return false;
        var text = SafeRead(path);
        if (text is null) return false;
        // Проверяем несколько «маркерных» настроек (если игра их перетёрла или пресет ещё не
        // применён — считаем не применённым). HDR/тени включены в маркеры: у игрока с HDR on
        // статус честно покажет, что прирост ещё доступен.
        foreach (var (key, val) in new[] { ("setting.msaa_samples", "0"), ("setting.mat_vsync", "0"),
                                           ("setting.videocfg_shadow_quality", "0"), ("setting.shaderquality", "0"),
                                           ("setting.videocfg_hdr_detail", "0"), ("setting.videocfg_dynamic_shadows", "0") })
            if (ReadValue(text, key) != val) return false;
        return true;
    }

    /// <summary>Применить max-FPS конфиг. Возвращает (успех, сообщение/ошибка).</summary>
    public static (bool ok, string message) ApplyMaxFps()
    {
        var path = ConfigPath();
        if (path is null) return (false, "no-config");
        if (PresentMonService.IsCs2Running()) return (false, "cs2-running");
        try
        {
            var text = SafeRead(path);
            if (text is null) return (false, "read-failed");

            // Бэкап оригинала — только ОДИН раз (чтобы повторное применение не затёрло настоящий исходник).
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(BackupPath)!);
            if (!File.Exists(BackupPath)) File.WriteAllText(BackupPath, text, new UTF8Encoding(false));

            foreach (var (key, val) in MaxFps) text = SetValue(text, key, val);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Logger.Info($"cs2-video: applied max-fps preset @ {path}");
            return (true, "ok");
        }
        catch (Exception ex) { Logger.Error("cs2-video: apply failed", ex); return (false, ex.Message); }
    }

    /// <summary>Вернуть исходный конфиг графики из бэкапа.</summary>
    public static (bool ok, string message) RestoreOriginal()
    {
        var path = ConfigPath();
        if (path is null) return (false, "no-config");
        if (PresentMonService.IsCs2Running()) return (false, "cs2-running");
        try
        {
            if (!File.Exists(BackupPath)) return (false, "no-backup");
            var orig = SafeRead(BackupPath);
            if (orig is null) return (false, "read-failed");
            File.WriteAllText(path, orig, new UTF8Encoding(false));
            Logger.Info($"cs2-video: restored original @ {path}");
            return (true, "ok");
        }
        catch (Exception ex) { Logger.Error("cs2-video: restore failed", ex); return (false, ex.Message); }
    }

    /// <summary>Есть ли сохранённый оригинал (можно ли откатить).</summary>
    public static bool HasBackup() => File.Exists(BackupPath);

    private static string? SafeRead(string p)
    {
        try { return File.ReadAllText(p); } catch { return null; }
    }

    private static string? ReadValue(string text, string key)
    {
        var m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Заменить значение ключа; если ключа нет — добавить перед закрывающей скобкой.</summary>
    private static string SetValue(string text, string key, string val)
    {
        var pattern = "(\"" + Regex.Escape(key) + "\"\\s*\")([^\"]*)(\")";
        if (Regex.IsMatch(text, pattern))
            return Regex.Replace(text, pattern, "${1}" + val + "${3}");
        // ключа нет — вставляем перед последней '}'
        int brace = text.LastIndexOf('}');
        if (brace < 0) return text;
        var line = $"\t\"{key}\"\t\t\"{val}\"\n";
        return text.Insert(brace, line);
    }
}
