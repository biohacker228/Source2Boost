using System.Diagnostics;
using System.Globalization;

namespace Source2Boost.Core;

/// <summary>Итоговая статистика фреймтайма по CSV PresentMon. GpuBusyMs — среднее время активной
/// работы GPU в кадре (мс), если PresentMon его отдал; иначе null.</summary>
public sealed record FrametimeStats(
    int Frames, double AvgFps, double Low1Fps, double Low01Fps, double MaxStutterMs, double StdDevMs,
    double? GpuBusyMs = null)
{
    /// <summary>Доля кадра, занятая GPU (0..1). null если нет данных GPU-busy.</summary>
    public double? GpuBusyFraction => GpuBusyMs is > 0 && AvgFps > 0 ? GpuBusyMs / (1000.0 / AvgFps) : null;
}

/// <summary>
/// Обёртка над Intel PresentMon: запуск замера cs2.exe и разбор CSV.
/// Приложение уже под админом, поэтому PresentMon запускается напрямую.
/// </summary>
public static class PresentMonService
{
    public static bool IsCs2Running() => Process.GetProcessesByName("cs2").Length > 0;

    /// <summary>Ищет PresentMon рядом с приложением, затем в tools\ репозитория (dev).
    /// ВАЖНО: версионный exe 2.x проверяем ПЕРВЫМ — в dev-фолбэке tools\ может лежать старый
    /// PresentMon.exe 1.x с ДРУГИМ синтаксисом флагов (наши --process_name/--v1_metrics он не
    /// понимает → CSV не создаётся). Установщик кладёт 2.x под именем PresentMon.exe, так что
    /// в проде подхватится он.</summary>
    public static string? FindExe()
    {
        var names = new[] { "PresentMon-2.5.1-x64.exe", "PresentMon.exe" };
        var dirs = new List<string> { AppContext.BaseDirectory };
        // dev-фолбэк: ...\src\App\bin\Debug\net8.0-windows -> подняться к корню репозитория\tools
        try
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && d is not null; i++, d = d.Parent)
            {
                var tools = Path.Combine(d.FullName, "tools");
                if (Directory.Exists(tools)) { dirs.Add(tools); break; }
            }
        }
        catch { }
        foreach (var dir in dirs)
            foreach (var n in names)
            {
                var p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    /// <summary>Каталог для CSV-замеров.</summary>
    public static string CapturesDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Source2Boost", "captures");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Захват фреймтайма cs2.exe на seconds секунд. Возвращает (csv, stats, error).</summary>
    public static async Task<(string? csv, FrametimeStats? stats, string? error)> CaptureAsync(int seconds, string label = "run")
    {
        var exe = FindExe();
        if (exe is null) { Logger.Warn("capture: PresentMon.exe not found"); return (null, null, "PresentMon.exe не найден рядом с приложением."); }
        if (!IsCs2Running()) { Logger.Info("capture: cs2.exe not running"); return (null, null, "CS2 (cs2.exe) не запущен. Запусти игру и зайди в матч/карту."); }
        Logger.Info($"capture: start ({seconds}s) via {exe}");

        var csv = Path.Combine(CapturesDir(), $"{label}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var psi = new ProcessStartInfo(exe)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[]
        {
            "--process_name", "cs2.exe", "--output_file", csv, "--timed", seconds.ToString(),
            "--terminate_after_timed", "--stop_existing_session", "--v1_metrics", "--no_console_stats",
        }) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) { Logger.Warn("capture: failed to start PresentMon"); return (null, null, "не удалось запустить PresentMon."); }
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderr = await p.StandardError.ReadToEndAsync();
            var stdout = await stdoutTask;
            await p.WaitForExitAsync();
            await Task.Delay(300);
            if (!File.Exists(csv))
            {
                var so = stdout.Replace('\n', ' ').Trim();
                var se = stderr.Replace('\n', ' ').Trim();
                Logger.Warn($"capture: no CSV (exit {p.ExitCode}) via {Path.GetFileName(exe)}. stdout: {so} | stderr: {se}");
                // Признак старого PresentMon 1.x: не понимает наши 2.x-флаги.
                if (se.IndexOf("unrecognized", StringComparison.OrdinalIgnoreCase) >= 0)
                    return (null, null, "Найден несовместимый PresentMon (старая версия). Переустанови приложение — нужен PresentMon 2.x.");
                return (null, null, "PresentMon не создал CSV (запущен ли cs2.exe и рендерит ли кадры?).");
            }
            var stats = Parse(csv);
            Logger.Info(stats is null
                ? $"capture: CSV parsed empty ({csv})"
                : $"capture: done avg={stats.AvgFps} 1%={stats.Low1Fps} 0.1%={stats.Low01Fps} frames={stats.Frames} ({csv})");
            return (csv, stats, stats is null ? "CSV пуст или без данных фреймтайма." : null);
        }
        catch (Exception ex) { Logger.Error("capture: exception", ex); return (null, null, ex.Message); }
    }

    /// <summary>Разбор CSV PresentMon (v1_metrics: колонка msBetweenPresents) в статистику.</summary>
    public static FrametimeStats? Parse(string csvPath)
    {
        try
        {
            var lines = File.ReadAllLines(csvPath);
            if (lines.Length < 3) return null;
            var header = lines[0].Split(',');
            int col = Array.FindIndex(header, h => h.Trim().Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase));
            if (col < 0) col = Array.FindIndex(header, h => h.Trim().IndexOf("msBetweenPresents", StringComparison.OrdinalIgnoreCase) >= 0);
            if (col < 0) return null;
            // GPU-busy (мс активной работы GPU в кадре): для вердикта CPU/GPU-bound. Может отсутствовать.
            int gcol = Array.FindIndex(header, h => h.Trim().Equals("msGPUActive", StringComparison.OrdinalIgnoreCase));
            if (gcol < 0) gcol = Array.FindIndex(header, h => h.Trim().IndexOf("GPUBusy", StringComparison.OrdinalIgnoreCase) >= 0);

            var ft = new List<double>(lines.Length);
            double gpuSum = 0; int gpuN = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (col >= parts.Length) continue;
                if (double.TryParse(parts[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0)
                    ft.Add(v);
                if (gcol >= 0 && gcol < parts.Length &&
                    double.TryParse(parts[gcol], NumberStyles.Float, CultureInfo.InvariantCulture, out var gv) && gv >= 0)
                    { gpuSum += gv; gpuN++; }
            }
            if (ft.Count < 10) return null;
            double? gpuBusy = gpuN > 0 ? Math.Round(gpuSum / gpuN, 3) : (double?)null;

            var sorted = ft.OrderBy(x => x).ToList();
            int n = sorted.Count;
            double sum = 0, sumsq = 0;
            foreach (var v in ft) sum += v;
            double mean = sum / n;
            foreach (var v in ft) sumsq += (v - mean) * (v - mean);
            double sd = Math.Sqrt(sumsq / n);

            double Pct(double p) => sorted[Math.Min(n - 1, (int)Math.Floor(p / 100.0 * n))];
            double p99 = Pct(99), p999 = Pct(99.9);

            return new FrametimeStats(
                Frames: n,
                AvgFps: Math.Round(1000.0 / mean, 1),
                Low1Fps: Math.Round(1000.0 / p99, 1),
                Low01Fps: Math.Round(1000.0 / p999, 1),
                MaxStutterMs: Math.Round(sorted[^1], 1),
                StdDevMs: Math.Round(sd, 2),
                GpuBusyMs: gpuBusy);
        }
        catch { return null; }
    }
}
