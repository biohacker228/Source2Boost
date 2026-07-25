using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>
/// Определение путей Steam / CS2 на текущей машине.
/// Всё best-effort: любой сбой -> null/пусто, никогда не бросает.
/// </summary>
public static class Cs2Paths
{
    /// <summary>AppId CS2 в Steam.</summary>
    public const string Cs2AppId = "730";

    /// <summary>Путь установки Steam из HKCU\Software\Valve\Steam!SteamPath, или null.</summary>
    public static string? SteamPath()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var p = k?.GetValue("SteamPath") as string;
            return string.IsNullOrWhiteSpace(p) ? null : p.Replace('/', '\\');
        }
        catch { return null; }
    }

    /// <summary>Все библиотеки Steam (корневые пути), включая основную установку.</summary>
    public static IReadOnlyList<string> LibraryFolders()
    {
        var result = new List<string>();
        var steam = SteamPath();
        if (steam is null) return result;
        result.Add(steam);
        try
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (var line in File.ReadAllLines(vdf))
                {
                    // строки вида:   "path"    "D:\\SteamLibrary"
                    var m = Regex.Match(line, "\"path\"\\s*\"(.+?)\"");
                    if (!m.Success) continue;
                    var path = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (!result.Contains(path, StringComparer.OrdinalIgnoreCase))
                        result.Add(path);
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Полный путь к cs2.exe, или null если не найден.</summary>
    public static string? Cs2ExePath()
    {
        foreach (var lib in LibraryFolders())
        {
            try
            {
                var exe = Path.Combine(lib, "steamapps", "common",
                    "Counter-Strike Global Offensive", "game", "bin", "win64", "cs2.exe");
                if (File.Exists(exe)) return exe;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Путь к cs2_video.txt (настройки графики CS2) в userdata активного аккаунта Steam.
    /// Перебирает все userdata\&lt;id&gt;\730\local\cfg\cs2_video.txt и берёт САМЫЙ свежий (текущий
    /// залогиненный аккаунт). null, если не найден.</summary>
    public static string? Cs2VideoConfigPath()
    {
        var steam = SteamPath();
        if (steam is null) return null;
        try
        {
            var userdata = Path.Combine(steam, "userdata");
            if (!Directory.Exists(userdata)) return null;
            string? best = null; DateTime bestTime = DateTime.MinValue;
            foreach (var acc in Directory.EnumerateDirectories(userdata))
            {
                var cfg = Path.Combine(acc, "730", "local", "cfg", "cs2_video.txt");
                if (!File.Exists(cfg)) continue;
                var t = File.GetLastWriteTimeUtc(cfg);
                if (t > bestTime) { bestTime = t; best = cfg; }
            }
            return best;
        }
        catch { return null; }
    }

    /// <summary>Папка shader-кэша CS2 (appid 730) внутри Steam, или null если Steam не найден.</summary>
    public static string? Cs2ShaderCacheDir()
    {
        var steam = SteamPath();
        return steam is null ? null : Path.Combine(steam, "steamapps", "shadercache", Cs2AppId);
    }

    /// <summary>Папка ...\game\csgo (корень контента CS2), или null если установка не найдена.</summary>
    public static string? Cs2CsgoDir()
    {
        foreach (var lib in LibraryFolders())
        {
            try
            {
                var csgo = Path.Combine(lib, "steamapps", "common",
                    "Counter-Strike Global Offensive", "game", "csgo");
                if (Directory.Exists(csgo)) return csgo;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Папка ...\game\csgo\cfg для autoexec.cfg, или null если установка CS2 не найдена.</summary>
    public static string? Cs2CfgDir()
    {
        var csgo = Cs2CsgoDir();
        return csgo is null ? null : Path.Combine(csgo, "cfg");
    }

    /// <summary>Папка ...\game\csgo\replays для .dem-файлов, или null если установка не найдена.</summary>
    public static string? Cs2ReplaysDir()
    {
        var csgo = Cs2CsgoDir();
        return csgo is null ? null : Path.Combine(csgo, "replays");
    }
}
