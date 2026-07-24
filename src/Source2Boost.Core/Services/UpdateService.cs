using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Source2Boost.Core;

/// <summary>Информация об обновлении, разобранная из JSON-манифеста фида.</summary>
public sealed record UpdateInfo(Version Version, string DownloadUrl, string? Sha256, string? Notes, bool Mandatory);

/// <summary>
/// Каркас авто-обновления. Логика простая и надёжная для приложения, которое ставится
/// установщиком (Inno Setup): читаем маленький JSON-манифест по HTTPS, сравниваем версию
/// с текущей сборкой, при наличии новой — скачиваем новый Setup.exe во временную папку,
/// (опц.) сверяем SHA-256 и отдаём путь наружу. Запуск установщика и выход приложения —
/// на стороне App (это UI-действие и оно требует явного согласия пользователя).
///
/// Формат манифеста (update.json):
/// { "version": "1.2.0", "url": "https://.../Source2Boost-Setup.exe",
///   "sha256": "ABCD…", "notes": "что нового", "mandatory": false }
///
/// БЕЗОПАСНОСТЬ: ничего не скачивается и не запускается само. Проверка — только чтение JSON.
/// Скачивание/установка инициируются пользователем из UI. SHA-256 (если указан в манифесте)
/// защищает от битой/подменённой загрузки.
/// </summary>
public static class UpdateService
{
    // TODO(publish): заменить на реальный адрес фида перед публикацией.
    // Удобно хостить на GitHub Releases: положить update.json в репозиторий (raw-ссылка)
    // и заливать Source2Boost-Setup.exe в assets релиза.
    public const string DefaultFeedUrl =
        "https://raw.githubusercontent.com/OWNER/REPO/main/update.json";

    /// <summary>Версия текущей сборки приложения (из атрибутов сборки).</summary>
    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(0, 0, 0);

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Source2Boost-Updater");
        return c;
    }

    /// <summary>
    /// Прочитать манифест и вернуть <see cref="UpdateInfo"/>, если доступна БОЛЕЕ новая версия;
    /// иначе <c>null</c>. Любая сетевая ошибка/недоступность фида → <c>null</c> (не мешаем работе).
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(string? feedUrl = null, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            var json = await client.GetStringAsync(feedUrl ?? DefaultFeedUrl, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("version", out var vEl) ||
                !Version.TryParse(vEl.GetString(), out var ver))
                return null;
            if (!root.TryGetProperty("url", out var urlEl) || string.IsNullOrWhiteSpace(urlEl.GetString()))
                return null;

            // безопасность: принимаем только https-ссылку на загрузку
            var url = urlEl.GetString()!;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null;

            if (ver <= CurrentVersion) return null; // уже актуально

            string? sha = root.TryGetProperty("sha256", out var s) ? s.GetString() : null;
            string? notes = root.TryGetProperty("notes", out var n) ? n.GetString() : null;
            bool mandatory = root.TryGetProperty("mandatory", out var m) && m.ValueKind == JsonValueKind.True;

            return new UpdateInfo(ver, url, sha, notes, mandatory);
        }
        catch { return null; }
    }

    /// <summary>
    /// Скачать установщик обновления во временную папку, при наличии — сверить SHA-256.
    /// Возвращает путь к скачанному .exe. Бросает исключение при сетевой ошибке или несовпадении хэша.
    /// Вызывается ТОЛЬКО после явного согласия пользователя в UI.
    /// </summary>
    public static async Task<string> DownloadAsync(
        UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Source2Boost-Update");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"Source2Boost-Setup-{info.Version}.exe");

        using (var client = CreateClient())
        {
            client.Timeout = TimeSpan.FromMinutes(10); // загрузка может быть большой
            using var resp = await client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                                         .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(target);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }

        if (!string.IsNullOrWhiteSpace(info.Sha256))
        {
            var actual = ComputeSha256(target);
            if (!actual.Equals(info.Sha256!.Replace("-", "").Trim(), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(target); } catch { }
                throw new InvalidOperationException("SHA-256 скачанного файла не совпал с манифестом.");
            }
        }

        return target;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }
}
