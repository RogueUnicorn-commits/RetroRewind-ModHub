using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Shell;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;

namespace RogueUnicorn.StoreTransfer;

internal static class CrashLogger
{
    private static readonly object Sync = new();
    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "CrashLogs");

    public static string Write(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"Crash_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.log");
            var sb = new StringBuilder();
            sb.AppendLine("Retro Rewind: ModHub Crash Log");
            sb.AppendLine($"Time: {DateTime.Now:O}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"Runtime: {Environment.Version}");
            sb.AppendLine($"Process: {Environment.ProcessPath}");
            sb.AppendLine();
            sb.AppendLine(exception.ToString());
            lock (Sync) File.WriteAllText(path, sb.ToString());
            return path;
        }
        catch
        {
            return string.Empty;
        }
    }
}

public partial class App : WpfApplication
{
    private sealed record SymbolicLinkBatchItem(string Source, string Target);
    private const string SingleInstanceMutexName = "Local\\RetroRewindModhub.SingleInstance";
    private const string NexusPipeName = "RetroRewindModhub.NexusRequests";
    private static Mutex? SingleInstanceMutex;
    private static int FatalCrashHandled;
    private static Thread? SplashThread;
    private static Window? StartupSplash;

    protected override async void OnStartup(StartupEventArgs e)
    {
        RegisterCrashHandlers();
        base.OnStartup(e);
        try
        {
            // Launch Game is a true headless shortcut. Handle it before the
            // single-instance routing and before any startup/splash code so
            // selecting it from the Windows taskbar can never create/show
            // the ModHub window. A second process simply performs the launch
            // and exits; this is also correct when ModHub is already running.
            var earlyTaskbarAction = GetTaskbarAction(e.Args);
            if (string.Equals(earlyTaskbarAction, "launchgame", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    LaunchGameFromTaskbarHeadless();
                    Shutdown(0);
                    return;
                }
                catch (Exception ex)
                {
                    CrashLogger.Write("TaskbarLaunchGame", ex);
                    MessageBox.Show(
                        "Retro Rewind could not be launched.\n\n" + ex.Message,
                        "Launch Retro Rewind",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Shutdown(1);
                    return;
                }
            }

            // Windows taskbar Jump List commands are routed into the existing
            // single instance for UI commands. Launch Game is intentionally
            // excluded because it is handled headlessly above.
            RegisterTaskbarJumpList();

            // The application itself must remain a normal (non-elevated) process.
            // Only this narrow helper mode is launched with UAC when a symbolic
            // link must be created. It performs exactly one operation and exits.
            if (e.Args.Length >= 3 && string.Equals(e.Args[0], "--rr-create-link", StringComparison.OrdinalIgnoreCase))
            {
                var source = e.Args[1];
                var target = e.Args[2];
                try
                {
                    if (File.Exists(target) || Directory.Exists(target))
                        File.Delete(target);
                    File.CreateSymbolicLink(target, source);
                    Shutdown(0);
                    return;
                }
                catch (Exception ex)
                {
                    CrashLogger.Write("ElevatedCreateSymbolicLink", ex);
                    Shutdown(1);
                    return;
                }
            }
            
            if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--rr-create-links-batch", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var batchFile = e.Args[1];
                    var json = File.ReadAllText(batchFile);
                    var links = System.Text.Json.JsonSerializer.Deserialize<List<SymbolicLinkBatchItem>>(json)
                                ?? new List<SymbolicLinkBatchItem>();

                    foreach (var link in links)
                    {
                        if (string.IsNullOrWhiteSpace(link.Source) || string.IsNullOrWhiteSpace(link.Target))
                            continue;

                        if (File.Exists(link.Target) || Directory.Exists(link.Target))
                            File.Delete(link.Target);

                        File.CreateSymbolicLink(link.Target, link.Source);
                    }

                    try { File.Delete(batchFile); } catch { }
                    Shutdown(0);
                    return;
                }
                catch (Exception ex)
                {
                    CrashLogger.Write("ElevatedCreateSymbolicLinksBatch", ex);
                    try { if (e.Args.Length >= 2) File.Delete(e.Args[1]); } catch { }
                    Shutdown(1);
                    return;
                }
            }

            var nexusUri = e.Args.FirstOrDefault(a => a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase));
            var taskbarAction = GetTaskbarAction(e.Args);
            SingleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
            if (!createdNew)
            {
                if (!string.IsNullOrWhiteSpace(nexusUri))
                    ForwardNexusUriToExistingInstance(nexusUri);
                else if (!string.IsNullOrWhiteSpace(taskbarAction))
                    ForwardTaskbarActionToExistingInstance(taskbarAction);
                Shutdown(0);
                return;
            }

            // Register the nxm:// handler before the Steam verification so the
            // handler is available whenever the app is successfully running.
            RegisterNxmProtocol();
            // Steam verification is part of the startup readiness barrier. It runs
            // asynchronously while the independent splash remains visible.
            // Keep the user-facing splash alive for the entire startup pipeline.
            // The splash has its own STA dispatcher so it remains responsive while
            // the main WPF dispatcher is busy constructing/preloading the app.
            StartStartupSplash();

            var steam = await Task.Run(SteamVerification.Verify);
            if (!steam.Verified)
            {
                StopStartupSplash();
                MessageBox.Show(
                    "Retro Rewind could not be verified as a Steam installation.\n\n" +
                    steam.Reason,
                    "Steam Verification Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            bool dark = IsDarkMode();
            var window = new MainWindow(dark);
            MainWindow = window;

            // The main window is created/shown, but remains fully transparent and
            // off the taskbar until MainWindow's startup coordinator reports that
            // Home and the preloadable application data are ready.
            window.Opacity = 0;
            window.ShowInTaskbar = false;
            StartNexusPipeServer(window);

            if (!string.IsNullOrWhiteSpace(nexusUri))
                window.Loaded += async (_, _) =>
                {
                    await Task.Yield();
                    await window.HandleNexusUriAsync(nexusUri);
                };

            window.Show();

            // Do not start a second wave of work after the splash closes. The
            // MainWindow startup coordinator owns all required initialization.
            try
            {
                await window.StartupReady;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);

                window.Opacity = 1;
                window.ShowInTaskbar = true;

                // Let the first visible frame reach the compositor before removing
                // the splash. This prevents the splash-to-main transition from
                // feeling like a frozen hand-off.
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await Task.Delay(100);

                StopStartupSplash();
            }
            catch
            {
                window.Opacity = 1;
                window.ShowInTaskbar = true;
                StopStartupSplash();
                throw;
            }

            if (!string.IsNullOrWhiteSpace(taskbarAction))
                await window.Dispatcher.InvokeAsync(() => window.HandleTaskbarCommand(taskbarAction));

            if (!string.IsNullOrWhiteSpace(nexusUri))
                await window.Dispatcher.InvokeAsync(() => window.HandleNexusUriAsync(nexusUri));

        }
        catch (Exception ex)
        {
            var log = CrashLogger.Write("Startup", ex);
            MessageBox.Show(
                "Retro Rewind: ModHub encountered an unexpected error.\n\n" +
                "A crash log was saved next to the executable." +
                (string.IsNullOrWhiteSpace(log) ? "" : "\n\n" + log) +
                "\n\n" + ex.Message,
                "Retro Rewind: ModHub",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void StartStartupSplash()
    {
        var ready = new ManualResetEventSlim(false);

        SplashThread = new Thread(() =>
        {
            try
            {
                var splash = new StartupSplashWindow();
                StartupSplash = splash;
                splash.Closed += (_, _) => splash.Dispatcher.InvokeShutdown();
                splash.Show();
                ready.Set();
                Dispatcher.Run();
            }
            catch
            {
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "RetroRewind Startup Splash"
        };
        SplashThread.SetApartmentState(ApartmentState.STA);
        SplashThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    internal static StartupSplashWindow? GetStartupSplash() => StartupSplash as StartupSplashWindow;

    private static void StopStartupSplash()
    {
        try
        {
            var splash = StartupSplash;
            if (splash != null)
            {
                splash.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { splash.Close(); } catch { }
                }), DispatcherPriority.Send);
            }
        }
        catch { }

        StartupSplash = null;
    }

    private async Task VerifySteamAfterShowAsync(MainWindow window)
    {
        try
        {
            var steam = await Task.Run(SteamVerification.Verify);

            if (steam.Verified)
                return;

            await window.Dispatcher.InvokeAsync(() =>
            {
                if (window.IsVisible)
                {
                    MessageBox.Show(
                        "Retro Rewind could not be verified as a Steam installation.\\n\\n" +
                        steam.Reason +
                        "\\n\\nThe Store Transfer application will now close.",
                        "Steam Verification Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    window.Close();
                }
            });
        }
        catch (Exception ex)
        {
            var log = CrashLogger.Write("SteamVerification", ex);
            await window.Dispatcher.InvokeAsync(() =>
            {
                if (!window.IsVisible)
                    return;

                MessageBox.Show(
                    "Retro Rewind could not verify the Steam installation.\\n\\n" +
                    (string.IsNullOrWhiteSpace(log) ? "" : "A crash log was saved next to the executable.\\n\\n") +
                    ex.Message,
                    "Steam Verification Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                window.Close();
            });
        }
    }

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            // A single UI failure can surface through several WPF/AppDomain paths.
            // Only the first fatal exception should create a crash report or shutdown.
            if (Interlocked.Exchange(ref FatalCrashHandled, 1) != 0)
            {
                args.Handled = true;
                return;
            }

            var log = CrashLogger.Write("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                "Retro Rewind: ModHub encountered an unexpected error.\n\n" +
                "A crash log was saved next to the executable." +
                (string.IsNullOrWhiteSpace(log) ? "" : "\n\n" + log) +
                "\n\n" + args.Exception.Message,
                "Retro Rewind: ModHub",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // If DispatcherUnhandledException already handled the fatal UI error,
            // do not create another duplicate crash log.
            if (Volatile.Read(ref FatalCrashHandled) != 0)
                return;

            Interlocked.Exchange(ref FatalCrashHandled, 1);
            if (args.ExceptionObject is Exception ex)
                CrashLogger.Write("AppDomain.UnhandledException", ex);
            else
                CrashLogger.Write("AppDomain.UnhandledException", new Exception(Convert.ToString(args.ExceptionObject)));
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLogger.Write("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static void ForwardNexusUriToExistingInstance(string uri)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", NexusPipeName, PipeDirection.Out);
                client.Connect(500);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true);
                writer.WriteLine(uri);
                writer.Flush();
                return;
            }
            catch
            {
                Thread.Sleep(150);
            }
        }
    }

    private static string? GetTaskbarAction(IEnumerable<string> args)
    {
        const string prefix = "--rr-taskbar=";
        return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?.Substring(prefix.Length)
            .Trim();
    }

    private static void LaunchGameFromTaskbarHeadless()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var primary = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Retro Rewind Modhub",
            "RetroRewindModhub.json");
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RetroRewind",
            "RetroRewindModhub.json");

        var configPath = File.Exists(primary) ? primary : fallback;
        if (File.Exists(configPath))
        {
            try
            {
                values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(configPath))
                         ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The ModHub launch settings could not be read.", ex);
            }
        }

        var steam = SteamVerification.Verify();
        if (!steam.Verified || string.IsNullOrWhiteSpace(steam.InstallPath))
            throw new InvalidOperationException("Retro Rewind could not be verified as a Steam installation.");

        var gameRoot = steam.InstallPath;
        var projectRoots = new[]
        {
            gameRoot,
            Path.Combine(gameRoot, "RetroRewind"),
            Path.Combine(gameRoot, "Binaries", "Win64")
        };

        var savedPairs = values.GetValueOrDefault("settings.runPairs");
        var exeNames = new List<string>();
        var libraries = new List<string>();
        if (!string.IsNullOrWhiteSpace(savedPairs))
        {
            foreach (var pair in savedPairs.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(new[] { '|' }, 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    exeNames.Add(parts[0].Trim());
                    if (!string.IsNullOrWhiteSpace(parts[1])) libraries.Add(parts[1].Trim());
                }
            }
        }

        if (exeNames.Count == 0)
            exeNames.Add("RetroRewind-Win64-Shipping.exe");
        if (libraries.Count == 0)
            libraries.Add("dwmapi.dll");

        var exe = exeNames
            .SelectMany(name => projectRoots.Select(root => Path.Combine(root, name)))
            .FirstOrDefault(File.Exists);
        if (exe == null)
            throw new FileNotFoundException(
                $"The selected game executable '{exeNames[0]}' could not be found.");

        var library = libraries.FirstOrDefault() ?? "dwmapi.dll";
        if (library.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase))
        {
            var exeDirectory = Path.GetDirectoryName(exe)!;
            var proxy = Path.Combine(exeDirectory, "dwmapi.dll");
            var ue4ss = Path.Combine(exeDirectory, "ue4ss", "UE4SS.dll");
            if (!File.Exists(proxy) || !File.Exists(ue4ss))
                throw new FileNotFoundException(
                    "Force Load Library is set to dwmapi.dll, but the UE4SS proxy or UE4SS.dll is missing beside the game executable.");
        }

        RunLaunchHelper.Launch(exe, (values.GetValueOrDefault("settings.runArguments") ?? "").Trim(), libraries.Take(1));
    }

    private static void RegisterTaskbarJumpList()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;

            var jumpList = new JumpList();

            jumpList.JumpItems.Add(new JumpTask
            {
                ApplicationPath = exe,
                Arguments = "--rr-taskbar=launchgame",
                Title = "Launch Game",
                Description = "Launch Retro Rewind without opening ModHub",
                CustomCategory = "Retro Rewind ModHub"
            });
            jumpList.JumpItems.Add(new JumpTask
            {
                ApplicationPath = exe,
                Arguments = "--rr-taskbar=home",
                Title = "Home",
                Description = "Open the ModHub home page",
                CustomCategory = "Retro Rewind ModHub"
            });
            jumpList.JumpItems.Add(new JumpTask
            {
                ApplicationPath = exe,
                Arguments = "--rr-taskbar=mods",
                Title = "Mods",
                Description = "Open the installed mods manager",
                CustomCategory = "Retro Rewind ModHub"
            });
            jumpList.JumpItems.Add(new JumpTask
            {
                ApplicationPath = exe,
                Arguments = "--rr-taskbar=conflicts",
                Title = "Conflict Checker",
                Description = "Open the mod conflict checker",
                CustomCategory = "Retro Rewind ModHub"
            });
            jumpList.JumpItems.Add(new JumpTask
            {
                ApplicationPath = exe,
                Arguments = "--rr-taskbar=configuremods",
                Title = "Configure Mods",
                Description = "Open mod configuration settings",
                CustomCategory = "Retro Rewind ModHub"
            });
            jumpList.JumpItems.Add(new JumpTask
            {
                ApplicationPath = exe,
                Arguments = "--rr-taskbar=refresh",
                Title = "Refresh",
                Description = "Refresh the current ModHub page",
                CustomCategory = "Retro Rewind ModHub"
            });
            jumpList.JumpItems.Add(new JumpTask
            {
                ApplicationPath = exe,
                Arguments = "--rr-taskbar=exit",
                Title = "Exit",
                Description = "Close Retro Rewind ModHub",
                CustomCategory = "Retro Rewind ModHub"
            });

            JumpList.SetJumpList(Current, jumpList);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("TaskbarJumpList", ex);
        }
    }

    private static void ForwardTaskbarActionToExistingInstance(string action)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", NexusPipeName, PipeDirection.Out);
                client.Connect(500);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true);
                writer.WriteLine("rr-taskbar:" + action);
                writer.Flush();
                return;
            }
            catch
            {
                Thread.Sleep(150);
            }
        }
    }

    private static void StartNexusPipeServer(MainWindow window)
    {
        _ = Task.Run(async () =>
        {
            while (!window.Dispatcher.HasShutdownStarted)
            {
                try
                {
                    using var server = new NamedPipeServerStream(NexusPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    if (window.Dispatcher.HasShutdownStarted) break;
                    using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                    var request = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(request))
                        continue;

                    await window.Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            var isTaskbarRequest = request.StartsWith("rr-taskbar:", StringComparison.OrdinalIgnoreCase);
                            var taskbarCommand = isTaskbarRequest
                                ? request.Substring("rr-taskbar:".Length).Trim()
                                : string.Empty;

                            // Launch Game is intentionally headless when an existing
                            // ModHub instance is already running. Showing the hidden
                            // window here defeats the taskbar shortcut's purpose.
                            if (!isTaskbarRequest || !string.Equals(taskbarCommand, "launchgame", StringComparison.OrdinalIgnoreCase))
                                window.ShowFromExternalRequest();

                            if (isTaskbarRequest)
                            {
                                window.HandleTaskbarCommand(taskbarCommand);
                            }
                            else if (request.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
                            {
                                await window.HandleNexusUriAsync(request);
                            }
                        }
                        catch (Exception ex)
                        {
                            CrashLogger.Write("ExternalPipeRequest", ex);
                        }
                    });
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    await Task.Delay(200);
                }
            }
        });
    }

    internal static void RegisterNxmProtocol()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm");
            if (key == null) return;
            key.SetValue("", "URL:Nexus Mods Link");
            key.SetValue("URL Protocol", "");
            using var command = key.CreateSubKey(@"shell\open\command");
            command?.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch { }
    }

    private static bool IsDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
