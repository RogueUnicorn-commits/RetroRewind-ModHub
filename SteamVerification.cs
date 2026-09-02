using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RogueUnicorn.StoreTransfer;

internal sealed record SteamVerificationResult(bool Verified, string? InstallPath, string Reason);

internal static class SteamVerification
{
    public const int AppId = 3552140;
    private const string InstallDirName = "RetroRewind";

    public static SteamVerificationResult Verify()
    {
        var steamRoots = FindSteamRoots();
        if (steamRoots.Count == 0)
            return new(false, null, "Steam could not be located on this PC.");

        foreach (var steamRoot in steamRoots)
        {
            foreach (var library in FindLibraries(steamRoot))
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
                if (!File.Exists(manifest))
                    continue;

                var installDir = ReadValue(manifest, "installdir");
                if (string.IsNullOrWhiteSpace(installDir))
                    installDir = InstallDirName;

                var gameRoot = Path.Combine(library, "steamapps", "common", installDir);
                if (!Directory.Exists(gameRoot))
                    continue;

                var executable = FindGameExecutable(gameRoot);
                if (executable == null)
                    continue;

                return new(true, gameRoot,
                    $"Steam AppID {AppId} is installed and its game executable was found.");
            }
        }

        return new(false, null,
            "The Steam installation for Retro Rewind (AppID 3552140) could not be verified.");
    }

    private static string? FindGameExecutable(string gameRoot)
    {
        var candidates = new[]
        {
            Path.Combine(gameRoot, "RetroRewind.exe"),
            Path.Combine(gameRoot, "RetroRewind", "Binaries", "Win64", "RetroRewind-Win64-Shipping.exe"),
            Path.Combine(gameRoot, "Binaries", "Win64", "RetroRewind-Win64-Shipping.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static HashSet<string> FindSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRegistryPath(roots, RegistryHive.CurrentUser, RegistryView.Default,
            @"Software\Valve\Steam", "SteamPath");
        AddRegistryPath(roots, RegistryHive.LocalMachine, RegistryView.Registry64,
            @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddRegistryPath(roots, RegistryHive.LocalMachine, RegistryView.Registry64,
            @"SOFTWARE\Valve\Steam", "InstallPath");
        AddRegistryPath(roots, RegistryHive.LocalMachine, RegistryView.Registry32,
            @"SOFTWARE\Valve\Steam", "InstallPath");

        var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(common)) roots.Add(common);

        return roots;
    }

    private static void AddRegistryPath(HashSet<string> roots, RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);
            var value = key?.GetValue(valueName) as string;
            if (value is { Length: > 0 } && Directory.Exists(value))
                roots.Add(value);
        }
        catch { }
    }

    private static IEnumerable<string> FindLibraries(string steamRoot)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        libraries.Add(steamRoot);

        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            try
            {
                var text = File.ReadAllText(vdf);
                foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase))
                {
                    var path = match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
                    if (Directory.Exists(path)) libraries.Add(path);
                }
            }
            catch { }
        }

        return libraries;
    }

    private static string? ReadValue(string path, string key)
    {
        try
        {
            var text = File.ReadAllText(path);
            var match = Regex.Match(text,
                $"\"{Regex.Escape(key)}\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.IgnoreCase);
            return match.Success
                ? match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"")
                : null;
        }
        catch
        {
            return null;
        }
    }
}
