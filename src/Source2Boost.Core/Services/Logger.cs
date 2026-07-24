using System.Text;

namespace Source2Boost.Core;

/// <summary>
/// Простой потокобезопасный файловый логгер без внешних зависимостей.
/// Пишет в %LOCALAPPDATA%\Source2Boost\logs\log-yyyyMMdd.txt (ежедневный файл).
/// Формат строки: [ISO-8601] [LEVEL] message. Исключения пишутся со стеком.
/// При инициализации удаляет логи старше 14 дней.
/// </summary>
public static class Logger
{
    /// <summary>Сколько дней хранить логи перед удалением.</summary>
    public const int RetentionDays = 14;

    private static readonly object _gate = new();
    private static bool _pruned;

    /// <summary>Папка с логами: %LOCALAPPDATA%\Source2Boost\logs.</summary>
    public static string LogsDir
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Source2Boost", "logs");
        }
    }

    /// <summary>Полный путь к файлу лога за сегодня.</summary>
    public static string LogPath => Path.Combine(LogsDir, $"log-{DateTime.Now:yyyyMMdd}.txt");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        if (ex is null) { Write("ERROR", message); return; }
        Write("ERROR", $"{message} | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
    }

    /// <summary>
    /// Записать ошибку и вернуть КОРОТКИЙ код для пользователя (вида <c>S2B-0723194512</c>).
    /// Код = момент времени, поэтому по нему сразу находится нужная строка в файле лога:
    /// пользователь присылает код — мы грепаем его в логах и видим полный стек.
    /// </summary>
    public static string ErrorCode(string where, Exception ex)
    {
        var code = $"S2B-{DateTime.Now:MMddHHmmss}";
        Error($"[{code}] {where}", ex);
        return code;
    }

    /// <summary>Записать итог применения/отката твика в лог единым форматом.</summary>
    public static void LogTweakResult(string tweakId, string action, bool success, string? message = null)
    {
        var line = $"tweak {tweakId} {action}: {(success ? "OK" : "FAIL")}"
                   + (string.IsNullOrWhiteSpace(message) ? "" : $" — {message}");
        if (success) Info(line); else Warn(line);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(LogsDir);
                if (!_pruned) { Prune(); _pruned = true; }
                var line = $"[{DateTime.Now:o}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch { /* логгер никогда не должен ронять приложение */ }
    }

    /// <summary>Удалить файлы логов старше RetentionDays. Вызывается один раз за процесс.</summary>
    private static void Prune()
    {
        try
        {
            if (!Directory.Exists(LogsDir)) return;
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var f in Directory.EnumerateFiles(LogsDir, "log-*.txt"))
            {
                try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); }
                catch { /* заблокирован/нет доступа — пропускаем */ }
            }
        }
        catch { }
    }
}
