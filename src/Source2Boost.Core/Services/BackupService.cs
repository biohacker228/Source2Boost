using System.Diagnostics;

namespace Source2Boost.Core;

public interface IBackupService
{
    /// <summary>Папка текущей сессии бэкапов.</summary>
    string SessionDir { get; }

    /// <summary>Экспорт ветки реестра в .reg. Вызывать ДО изменения.</summary>
    void BackupRegistryKey(string fullKeyPath);

    /// <summary>Сохранить произвольный факт отката (json-строка) под именем.</summary>
    void SaveState(string name, string json);
    string? LoadState(string name);

    /// <summary>Точка восстановления Windows (может занять время; best-effort).</summary>
    bool CreateRestorePoint(string description);
}

/// <summary>
/// Бэкап-сервис: пишет .reg-экспорты и состояние в backups\&lt;timestamp&gt;\
/// и создаёт точку восстановления Windows перед агрессивными профилями.
/// </summary>
public sealed class BackupService : IBackupService
{
    public string SessionDir { get; }
    private readonly string _stateDir;
    private readonly Action<string>? _log;

    public BackupService(string rootDir, Action<string>? log = null)
    {
        _log = log;
        SessionDir = Path.Combine(rootDir, "backups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        _stateDir = Path.Combine(rootDir, "state"); // стабильно между запусками -> откат переживает перезапуск
        Directory.CreateDirectory(SessionDir);
        Directory.CreateDirectory(_stateDir);
    }

    public void BackupRegistryKey(string fullKeyPath)
    {
        var safe = fullKeyPath.Replace('\\', '_').Replace(':', '_');
        var outFile = Path.Combine(SessionDir, $"{safe}.reg");
        if (File.Exists(outFile)) return; // уже сохранено в этой сессии
        var psi = new ProcessStartInfo("reg.exe", $"export \"{fullKeyPath}\" \"{outFile}\" /y")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        try
        {
            using var p = Process.Start(psi);
            p?.WaitForExit(10000);
            _log?.Invoke($"backup reg: {fullKeyPath} -> {outFile} (exit {p?.ExitCode})");
        }
        catch (Exception ex) { _log?.Invoke($"backup reg FAILED {fullKeyPath}: {ex.Message}"); }
    }

    public void SaveState(string name, string json)
        => File.WriteAllText(Path.Combine(_stateDir, $"{name}.json"), json);

    public string? LoadState(string name)
    {
        var f = Path.Combine(_stateDir, $"{name}.json");
        return File.Exists(f) ? File.ReadAllText(f) : null;
    }

    public bool CreateRestorePoint(string description)
    {
        // System Restore через PowerShell. Требует, чтобы защита системы была включена.
        var cmd = $"Checkpoint-Computer -Description \"{description}\" -RestorePointType MODIFY_SETTINGS";
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(120000);
            var ok = p.ExitCode == 0;
            _log?.Invoke($"restore point '{description}': {(ok ? "ok" : "failed")}");
            return ok;
        }
        catch (Exception ex) { _log?.Invoke($"restore point FAILED: {ex.Message}"); return false; }
    }
}
