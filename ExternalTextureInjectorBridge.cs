using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RogueUnicorn.StoreTransfer;

/// <summary>
/// Process bridge to the separately installed RRModHubTextureInjector.exe.
/// The injector is kept in the RetroRewindModHub_Data folder beside the application executable.
/// </summary>
internal static class ExternalTextureInjectorBridge
{
    private const string InjectorExeName = "RRModHubTextureInjector.exe";

    internal static string ExportPng(string uassetPath, string outputDirectory)
        => Run("export", ("--input", uassetPath), ("--output", outputDirectory));

    internal static string Replace(string uassetPath, string imagePath, string outputDirectory)
        => Run("replace", ("--input", uassetPath), ("--image", imagePath), ("--output", outputDirectory));

    private static string Run(string command, params (string Key, string Value)[] arguments)
    {
        var exe = FindInjectorExecutable();
        if (!File.Exists(exe))
            throw new FileNotFoundException(
                $"{InjectorExeName} was not found. Place it in Documents\\Retro Rewind Modhub\\Tools.", exe);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(command);
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument.Key);
            psi.ArgumentList.Add(argument.Value);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {InjectorExeName}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);

        var result = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.StartsWith("RESULT=", StringComparison.Ordinal));
        if (result == null)
            throw new InvalidDataException($"{InjectorExeName} completed without returning a result path.");
        return result["RESULT=".Length..];
    }

    private static string GetDataDirectory()
        => Path.Combine(AppContext.BaseDirectory, "RetroRewindModHub_Data");

    private static string FindInjectorExecutable()
        => Path.Combine(GetDataDirectory(), InjectorExeName);
}
