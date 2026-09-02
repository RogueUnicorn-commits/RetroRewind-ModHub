using System.Text;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Globalization;
using System;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using SharpVectors.Converters;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipes;
using LibVLCSharp.Shared;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using VlcVideoView = LibVLCSharp.WPF.VideoView;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using Panel = System.Windows.Controls.Panel;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;

using System.Net.Http;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Xml.Linq;
namespace RogueUnicorn.StoreTransfer;

public partial class MainWindow : Window
{
    private sealed record PakDragInfo(string[] Paths);

    private string SavePath(string? fileName = null)
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RetroRewind", "Saved", "SaveGames", "Save.sav");

        if (string.IsNullOrWhiteSpace(fileName))
            return basePath;

        return Path.IsPathRooted(fileName)
            ? fileName
            : Path.Combine(Path.GetDirectoryName(basePath) ?? string.Empty, fileName);
    }

    private async void MainWindow_StartupLoaded(object sender, RoutedEventArgs e)
    {
        if (_startupInitialized)
            return;

        _startupInitialized = true;

        try
        {
            SetStartupOverlayStatus("Preparing ModHub…", 5, "Loading application configuration and core services.");

            // The dedicated splash is the only startup surface. The main window remains
            // hidden until startup is complete, so it cannot paint a black/opaque overlay
            // over the loading information.

            SetStartupOverlayStatus("Preparing Home…", 15, "Loading Steam profile and cached account data.");
            LoadSteamHomeProfile();

            // Refresh Home's online sources while the main window is still
            // invisible. These are explicitly startup data because they populate
            // the first page the user sees.
            SetStartupOverlayStatus("Refreshing Nexus account…", 25, "Updating account status and avatar information.");
            await RefreshNexusHomeAccountAsync(force: true);

            SetStartupOverlayStatus("Loading Steam news…", 35, "Refreshing the Home news feed.");
            await RefreshHomeNewsAsync();

            SetStartupOverlayStatus("Preparing Mod Manager…", 50, "Loading cached mod list and updating installed PAK and UE4SS mods.");
            LoadModListCache();
            _mode = "mods";
            RefreshModManager();
            // Perform the expensive filesystem discovery during boot, while the
            // startup overlay is still covering the main window. Navigation later
            // reuses the already-rendered snapshot instead of rescanning.
            BeginModManagerRefresh(force: true);
            await WaitForModManagerRefreshAsync();

            SetStartupOverlayStatus("Checking UE4SS…", 57, "Checking UE4SS integrity and looking for a newer release.");
            await CheckUe4ssHealthAsync();

            SetStartupOverlayStatus("Preparing Videos…", 62, "Building the video library index.");
            _mode = "videos";
            RefreshVideosPage();
            await WaitForVideoLibraryRefreshAsync();

            SetStartupOverlayStatus("Preparing Downloads…", 70, "Building the downloads list.");
            _mode = "downloads";
            RefreshDownloadsPage();
            await WaitForDownloadsRefreshAsync();

            SetStartupOverlayStatus("Preparing Asset Workshop…", 80, "Loading the Asset Workshop index/cache.");
            _mode = "assets";
            await LoadAssetWorkshopPaksAsync();

            SetStartupOverlayStatus("Preparing Conflict Checker…", 84, "Conflict Checker is ready; press F5 to scan installed PAKs.");
            _mode = "conflicts";

            SetStartupOverlayStatus("Checking Required Files…", 87, "Checking and downloading missing external tools.");
            _mode = "requiredfiles";
            await CheckRequiredFilesOnStartupAsync();

            SetStartupOverlayStatus("Preparing Video Editor…", 93, "Checking video editor dependencies and hardware acceleration.");
            await WarmVideoEditorHardwareAsync();

            SetStartupOverlayStatus("Finalizing Home…", 97, "Restoring the Home page and preparing the first interactive frame.");
            _mode = "home";
            UpdateMode();

            // UpdateMode starts Home refreshes again; wait for the online state to
            // settle so the first Home display is not immediately replaced by a
            // second wave of work after the splash disappears.
            await RefreshNexusHomeAccountAsync(force: false);
            await RefreshHomeNewsAsync();

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);

            SetStartupOverlayStatus("Ready", 100, "ModHub is ready.");
            if (StartupOverlay != null) StartupOverlay.Visibility = Visibility.Collapsed;

            // Claim all ModHub-controlled files only after startup work is complete.
            // The lock prevents normal external edits, deletes, and renames while
            // ModHub is running. Config saves temporarily release and reacquire it.
            NormalizeAndLockModHubControlledFiles();

            _startupReadyTcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup coordinator failed: {ex}");
            try
            {
                if (StartupOverlay != null)
                    StartupOverlay.Visibility = Visibility.Collapsed;
            }
            catch { }

            // Do not leave the application permanently hidden if a non-critical
            // preload fails. The user still gets the fully constructed UI.
            _startupReadyTcs.TrySetResult(false);
        }
    }

    private void SetStartupOverlayStatus(string status, double progress, string detail)
    {
        try { App.GetStartupSplash()?.SetStatus(status, progress); } catch { }
        if (StartupOverlayStatus != null)
            StartupOverlayStatus.Text = status;
        if (StartupOverlayDetail != null)
            StartupOverlayDetail.Text = detail;
        if (StartupOverlayProgress != null)
            StartupOverlayProgress.Value = progress;

    }

    private string ModListCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Retro Rewind Modhub", "modlist.cache");

    private sealed class ModListCacheData
    {
        public List<ModEntry> PakMods { get; set; } = new();
        public List<ModEntry> Ue4ssMods { get; set; } = new();
        public List<PendingModEntry> PendingMods { get; set; } = new();
        public DateTime UpdatedUtc { get; set; }
    }

    private void LoadModListCache()
    {
        try
        {
            var path = ModListCachePath;
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var cache = JsonSerializer.Deserialize<ModListCacheData>(json);
            if (cache == null) return;
            _cachedPakMods = cache.PakMods ?? new List<ModEntry>();
            _cachedUe4ssMods = cache.Ue4ssMods ?? new List<ModEntry>();
            _cachedPendingMods = cache.PendingMods ?? new List<PendingModEntry>();
            _modCacheUpdatedUtc = DateTime.MinValue;
            _modUiAppliedVersion = -1;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not load mod list cache: {ex.Message}");
            _cachedPakMods = null;
            _cachedUe4ssMods = null;
            _cachedPendingMods = null;
            _modUiAppliedVersion = -1;
        }
    }

    private void SaveModListCache()
    {
        try
        {
            if (_cachedPakMods == null || _cachedUe4ssMods == null || _cachedPendingMods == null) return;
            var path = ModListCachePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new ModListCacheData
            {
                PakMods = _cachedPakMods,
                Ue4ssMods = _cachedUe4ssMods,
                PendingMods = _cachedPendingMods,
                UpdatedUtc = DateTime.UtcNow
            };
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not save mod list cache: {ex.Message}");
        }
    }

    private async Task WaitForModManagerRefreshAsync()
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        while (_modRefreshInProgress && DateTime.UtcNow < timeout)
            await Task.Delay(50);
    }

    private async Task WaitForVideoLibraryRefreshAsync()
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        while (_videoLibraryRefreshInProgress && DateTime.UtcNow < timeout)
            await Task.Delay(50);
    }

    private async Task WaitForDownloadsRefreshAsync()
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        while (_downloadsRefreshInProgress && DateTime.UtcNow < timeout)
            await Task.Delay(50);
    }

    private async Task WarmVideoEditorHardwareAsync()
    {
        try
        {
            var ffmpeg = Path.Combine(ToolsDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpeg)) return;

            if (_videoEditorRenderEngine == null)
            {
                _videoEditorRenderEngine = new VideoEditorRenderEngine();
                _videoEditorRenderEngine.FrameReady += OnVideoEditorRenderFrame;
                _videoEditorRenderEngine.Error += message =>
                    Debug.WriteLine($"Video Editor render engine warm-up: {message}");
            }

            await _videoEditorRenderEngine.DetectHardwareAccelerationAsync(
                ffmpeg, CancellationToken.None);

            await Dispatcher.InvokeAsync(UpdateVideoEditorAccelerationStatus);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Video Editor hardware warm-up failed: {ex}");
        }
    }

    private async Task<BlueprintDialogInfo> ReadBlueprintDialogInfo(object? dialogInfo = null)
    {
        // Blueprint metadata is optional; retain the existing UI state when
        // no metadata file is available.
        return new BlueprintDialogInfo
        {
            StoreName = string.Empty,
            Version = string.Empty,
            FileName = dialogInfo?.ToString() ?? string.Empty,
            FileSize = string.Empty,
            SourceSave = string.Empty,
            Rooms = string.Empty,
            Objects = string.Empty
        };
    }

    private UIElement BuildBlueprintVersionBadge(string? version)
    {
        return new TextBlock
        {
            Text = version ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private async Task SelectBlueprintWorkflowAsync()
    {
        await Task.CompletedTask;
        SelectBlueprint_Click(this, new RoutedEventArgs());
    }

    private void SelectBlueprint_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Blueprint",
            Filter = "Blueprint files|*.json;*.blueprint;*.bp|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var folder = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);
        }
        catch
        {
            // Selection itself is valid; downstream blueprint handling will
            // report any actual processing error.
        }
    }

    private static string GetCurrencySymbol()
    {
        try
        {
            // Follow the Windows regional settings used by the current user.
            return CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol;
        }
        catch
        {
            return "£";
        }
    }

    private sealed record VideoReplacement(string CustomFile, string? GameRelativePath);

    private void EnsureModDataLayout()
    {
        try
        {
            var modsRoot = ModsRoot;
            Directory.CreateDirectory(modsRoot);

            // Migrate the legacy defaults store to the new UE4SS settings store.
            MigrateModFile("ModDefaults.json", Path.Combine(modsRoot, "UE4SSSettings.json"));
            MigrateModFile(Path.Combine(modsRoot, "ModDefaults.json"), Path.Combine(modsRoot, "UE4SSSettings.json"));

            // Move/merge the legacy download store into Mods\_downloads.
            var legacyDownloads = Path.Combine(AppContext.BaseDirectory, "_downloads");
            var newDownloads = Path.Combine(modsRoot, "_downloads");
            if (Directory.Exists(legacyDownloads))
            {
                Directory.CreateDirectory(newDownloads);

                foreach (var file in Directory.EnumerateFiles(legacyDownloads, "*", SearchOption.TopDirectoryOnly))
                {
                    var destination = Path.Combine(newDownloads, Path.GetFileName(file));
                    if (!File.Exists(destination))
                    {
                        try { File.Move(file, destination); }
                        catch { /* Leave the legacy file in place if it cannot be moved. */ }
                    }
                }

                // Remove the old folder only when it is completely empty.
                if (!Directory.EnumerateFileSystemEntries(legacyDownloads).Any())
                {
                    try { Directory.Delete(legacyDownloads); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("EnsureModDataLayout", ex);
        }
    }

    private static void MigrateModFile(string legacyName, string destination)
    {
        try
        {
            var legacy = Path.IsPathRooted(legacyName) ? legacyName : Path.Combine(AppContext.BaseDirectory, legacyName);
            if (!File.Exists(legacy) || File.Exists(destination)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(legacy, destination);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("MigrateModFile", ex);
        }
    }

    private void AddBlueprintInfoField(Grid grid, int column, int row, string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 14, 7) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)Resources["SecondaryBrush"],
            FontSize = 12
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "Unknown" : value,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = value
        });
        Grid.SetColumn(panel, column);
        Grid.SetRow(panel, row);
        grid.Children.Add(panel);
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _preFullscreenWindowState = WindowState;
            _preFullscreenWindowStyle = WindowStyle;
            _preFullscreenResizeMode = ResizeMode;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref mi))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                _preFullscreenBounds = new Rect(
                    Left, Top, ActualWidth, ActualHeight);

                Left = mi.rcMonitor.Left / dpi.DpiScaleX;
                Top = mi.rcMonitor.Top / dpi.DpiScaleY;
                Width = (mi.rcMonitor.Right - mi.rcMonitor.Left) / dpi.DpiScaleX;
                Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / dpi.DpiScaleY;
            }

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            Topmost = true;
            _isFullscreen = true;
        }
        else
        {
            Topmost = false;
            WindowStyle = _preFullscreenWindowStyle;
            ResizeMode = _preFullscreenResizeMode;

            if (_preFullscreenWindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                if (_preFullscreenBounds.Width > 0)
                {
                    Left = _preFullscreenBounds.Left;
                    Top = _preFullscreenBounds.Top;
                    Width = _preFullscreenBounds.Width;
                    Height = _preFullscreenBounds.Height;
                }
                WindowState = _preFullscreenWindowState;
            }

            _isFullscreen = false;
        }
    }

    internal void HandleTaskbarCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        command = command.Trim().ToLowerInvariant();

        if (command == "exit")
        {
            BeginShutdown();
            return;
        }

        if (command == "launchgame")
        {
            LaunchGame_Click(this, new RoutedEventArgs());
            return;
        }

        if (command == "refresh")
        {
            switch (_mode)
            {
                case "home":
                    _ = RefreshNexusHomeAccountAsync(force: true);
                    _ = RefreshHomeNewsAsync(force: true);
                    if (HomeSteamOverlay.Visibility == Visibility.Visible)
                    {
                        if (HomeSteamAchievementsView.Visibility == Visibility.Visible)
                            _ = RefreshSteamHomeAchievementsAsync();
                        else if (HomeSteamGamesView.Visibility == Visibility.Visible)
                            _ = RefreshSteamHomeGamesAsync();
                    }
                    break;
                case "mods":
                    BeginModManagerRefresh(force: true);
                    break;
                case "conflicts":
                    RefreshConflictCheckPage();
                    break;
                case "configuremods":
                    RefreshModConfigurationPanels();
                    break;
                case "videos":
                    RefreshVideosPage();
                    break;
                case "videoeditor":
                    RefreshVideoEditorUi();
                    break;
                case "requiredfiles":
                    _ = RefreshRequiredFilesPage();
                    break;
                case "downloads":
                    RefreshDownloadsPage();
                    break;
                case "assets":
                case "asset_texture":
                case "asset_staticmesh":
                case "asset_skeletalmesh":
                case "asset_material":
                case "asset_animation":
                case "asset_audio":
                case "asset_blueprint":
                case "asset_niagara":
                case "asset_particle":
                case "asset_widget":
                case "asset_world":
                case "asset_other":
                    RefreshAssetWorkshopPage();
                    break;
                case "mergemods":
                    RefreshModManager();
                    break;
            }
            return;
        }

        var validModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "home", "mods", "conflicts", "configuremods"
        };

        if (!validModes.Contains(command))
            return;

        if (command == "mods")
            _modRefreshInProgress = false;

        RememberCurrentPaths();
        _mode = command;
        UpdateMode();
        RestoreRememberedPaths();

        if (command == "home")
            _ = RefreshNexusHomeAccountAsync(force: true);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_posterBrowserFullscreenPreviewVisible && e.Key == Key.Escape)
        {
            e.Handled = true;
            ClosePosterBrowserFullscreenPreview();
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.Z || e.Key == Key.Y) && PosterImageEditorGrid?.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            if (e.Key == Key.Z && _posterImageEditorUndo.Count > 1) { var current = _posterImageEditorUndo.Pop(); _posterImageEditorRedo.Push(current); ApplyPosterImageEditorState(_posterImageEditorUndo.Peek()); }
            else if (e.Key == Key.Y && _posterImageEditorRedo.Count > 0) { var next = _posterImageEditorRedo.Pop(); _posterImageEditorUndo.Push(next); ApplyPosterImageEditorState(next); }
            return;
        }

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            switch (_mode)
            {
                case "home":
                    _ = RefreshNexusHomeAccountAsync(force: true);
                    _ = RefreshHomeNewsAsync(force: true);
                    if (HomeSteamOverlay.Visibility == Visibility.Visible)
                    {
                        if (HomeSteamAchievementsView.Visibility == Visibility.Visible)
                            _ = RefreshSteamHomeAchievementsAsync();
                        else if (HomeSteamGamesView.Visibility == Visibility.Visible)
                            _ = RefreshSteamHomeGamesAsync();
                    }
                    break;

                case "mods":
                    // Navigation reuses the already-rendered cached list. F5 is the
                    // explicit request to rescan the installed mods.
                    BeginModManagerRefresh(force: true);
                    break;
                case "conflicts":
                    RefreshConflictCheckPage();
                    break;
                case "configuremods":
                    RefreshModConfigurationPanels();
                    break;
                case "videos":
                    RefreshVideosPage();
                    break;
                case "videoeditor":
                    RefreshVideoEditorUi();
                    break;
                case "requiredfiles":
                    _ = RefreshRequiredFilesPage();
                    break;
                case "downloads":
                    RefreshDownloadsPage();
                    break;
                case "assets":
                case "asset_texture":
                case "asset_staticmesh":
                case "asset_skeletalmesh":
                case "asset_material":
                case "asset_animation":
                case "asset_audio":
                case "asset_blueprint":
                case "asset_niagara":
                case "asset_particle":
                case "asset_widget":
                case "asset_world":
                case "asset_other":
                    RefreshAssetWorkshopPage();
                    break;
                case "mergemods":
                    RefreshModManager();
                    break;
            }
            return;
        }

        if (e.Key == Key.F11)
        {
            e.Handled = true;
            ToggleFullscreen();
            return;
        }

        if (e.Key == Key.Escape && _activeSlidePanel != null)
        {
            e.Handled = true;
            _activeSlidePanel.OnEscapeClose?.Invoke();
            _activeSlidePanel.Close();
        }
    }

    private void ShowSlidePanel(OverlayDialogHost host, SlidePanelMode mode)
    {
        if (_activeSlidePanel != null)
            _activeSlidePanel.Close();

        _activeSlidePanel = host;
        SlidePanelHost.Content = host;

        Grid.SetColumn(SlidePanelSurface, 0);
        Grid.SetColumnSpan(SlidePanelSurface, 1);
        Grid.SetRow(SlidePanelSurface, 0);
        Grid.SetRowSpan(SlidePanelSurface, 1);
        SlidePanelSurface.Width = mode == SlidePanelMode.Right ? Math.Min(520, Math.Max(360, ActualWidth * 0.34)) : (ActualWidth > 0 ? ActualWidth * 0.80 : double.NaN);
        SlidePanelSurface.Height = double.NaN;
        SlidePanelSurface.HorizontalAlignment = mode == SlidePanelMode.Right ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        SlidePanelSurface.VerticalAlignment = VerticalAlignment.Stretch;
        SlidePanelSurface.Margin = new Thickness(0);
        SlidePanelSurface.RenderTransform = mode == SlidePanelMode.Right
            ? new TranslateTransform(Math.Max(520, SlidePanelSurface.Width), 0)
            : new TranslateTransform(0, -Math.Max(ActualHeight, 600));
        SlidePanelOverlay.IsHitTestVisible = true;
        SlidePanelSurface.IsHitTestVisible = true;
        SlidePanelOverlay.Visibility = Visibility.Visible;
        SlidePanelSurface.Focus();

        var transform = (TranslateTransform)SlidePanelSurface.RenderTransform;
        var animation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (mode == SlidePanelMode.Right) transform.BeginAnimation(TranslateTransform.XProperty, animation);
        else transform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void HideSlidePanel(OverlayDialogHost host, SlidePanelMode mode)
    {
        if (!ReferenceEquals(_activeSlidePanel, host)) return;

        var transform = (TranslateTransform)SlidePanelSurface.RenderTransform;
        var travel = mode == SlidePanelMode.Right ? Math.Max(520, SlidePanelSurface.ActualWidth > 0 ? SlidePanelSurface.ActualWidth : 520) : -Math.Max(ActualHeight, 600);
        var animation = new DoubleAnimation
        {
            To = travel,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_activeSlidePanel, host)) return;
            SlidePanelHost.Content = null;
            SlidePanelOverlay.Visibility = Visibility.Collapsed;
            _activeSlidePanel = null;
        };
        if (mode == SlidePanelMode.Right) transform.BeginAnimation(TranslateTransform.XProperty, animation);
        else transform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void SlidePanelBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        _activeSlidePanel?.OnBackdropClose?.Invoke();
        _activeSlidePanel?.Close();
        e.Handled = true;
    }

    private async Task<string?> SelectBlueprintAsync()
    {
        var dialog = new OverlayDialogHost(this, SlidePanelMode.Right)
        {
            Background = (Brush)Resources["CardBrush"],
            Foreground = (Brush)Resources["ForegroundBrush"]
        };

        var outer = new Grid { Margin = new Thickness(12, 18, 12, 18) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "SELECT BLUEPRINT",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(title, 0);
        outer.Children.Add(title);

        var subtitle = new TextBlock
        {
            Text = "Choose a .rrblueprint file.",
            Foreground = (Brush)Resources["SecondaryBrush"],
            Margin = new Thickness(0, 34, 0, 12)
        };
        Grid.SetRow(subtitle, 0);
        outer.Children.Add(subtitle);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(scroll, 1);

        var list = new StackPanel
        {
            Margin = new Thickness(0, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        scroll.Content = list;
        outer.Children.Add(scroll);

        var close = new Button
        {
            Content = L("Cancel"),
            Width = 100,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
            Style = (Style)Resources["SettingsButtonStyle"]
        };
        close.Click += (_, _) => dialog.DialogResult = false;
        Grid.SetRow(close, 2);
        outer.Children.Add(close);

        dialog.Content = outer;
        dialog.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Escape)
            {
                keyArgs.Handled = true;
                dialog.DialogResult = false;
            }
        };

        string folder = BlueprintFolderPath;
        var files = new List<string>();
        if (Directory.Exists(folder))
        {
            files = Directory.EnumerateFiles(folder, "*.rrblueprint", SearchOption.AllDirectories)
                .OrderByDescending(File.GetCreationTime)
                .ThenByDescending(File.GetLastWriteTime)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        void AddNoBlueprintsMessage()
        {
            list.Children.Clear();
            list.Children.Add(new TextBlock
            {
                Text = "No .rrblueprint files found.",
                Foreground = (Brush)Resources["SecondaryBrush"],
                Margin = new Thickness(16, 12, 16, 12)
            });
        }

        if (files.Count == 0)
        {
            AddNoBlueprintsMessage();
        }
        else
        {
            foreach (var path in files)
            {
                var card = new Border
                {
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    BorderBrush = (Brush)Resources["BorderBrush"],
                    Background = (Brush)Resources["SecondaryCardBrush"]
                };

                var info = await ReadBlueprintDialogInfo(path);
                var created = File.GetCreationTime(path);

                // Keep the card as one continuous surface. The delete button is
                // overlaid inside the card instead of reserving a separate grid
                // column, so adding it does not create a visible gap.
                var cardGrid = new Grid();

                var content = new StackPanel { Margin = new Thickness(16, 10, 16, 10) };

                var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var storeTitle = new TextBlock
                {
                    Text = info.StoreName,
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = info.StoreName,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(storeTitle, 0);
                header.Children.Add(storeTitle);

                var versionBadge = BuildBlueprintVersionBadge(info.Version);
                Grid.SetColumn(versionBadge, 1);
                header.Children.Add(versionBadge);

                content.Children.Add(header);

                var details = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                AddBlueprintInfoField(details, 0, 0, "File Name", info.FileName);
                AddBlueprintInfoField(details, 1, 0, "File Size", info.FileSize);
                AddBlueprintInfoField(details, 2, 0, "Source Save", info.SourceSave);
                AddBlueprintInfoField(details, 0, 1, "Unlocked Room(s)", info.Rooms);
                AddBlueprintInfoField(details, 1, 1, "Object(s)", info.Objects);
                // Creation date belongs directly beneath Source Save.
                AddBlueprintInfoField(details, 2, 1, "Date created", created.ToString("yyyy-MM-dd HH:mm"));

                content.Children.Add(details);

                var selectButton = new Button
                {
                    Content = content,
                    Tag = path,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = (Brush)Resources["ForegroundBrush"],
                    Style = (Style)Resources["SaveSlotButtonStyle"]
                };
                selectButton.Click += (_, _) =>
                {
                    dialog.SelectedValue = (string)selectButton.Tag;
                    dialog.DialogResult = true;
                };
                Grid.SetRow(selectButton, 0);
                cardGrid.Children.Add(selectButton);

                var deleteButton = new Button
                {
                    Content = "Delete",
                    Tag = path,
                    Width = 82,
                    Height = 34,
                    Margin = new Thickness(0, 0, 10, 10),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new Thickness(8, 3, 8, 3),
                    Foreground = Brushes.White,
                    Background = (Brush)Resources["CoreUiRedBrush"],
                    BorderBrush = (Brush)Resources["CoreUiRedBrush"],
                    BorderThickness = new Thickness(1),
                    Style = (Style)Resources["BrowseButtonStyle"]
                };
                deleteButton.Click += (_, _) =>
                {
                    var confirm = MessageBox.Show(
                        this,
                        $"Delete this blueprint permanently?\n\n{Path.GetFileName(path)}\n\nThis cannot be undone.",
                        "Delete Blueprint",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);

                    if (confirm != MessageBoxResult.Yes)
                        return;

                    try
                    {
                        File.Delete(path);
                        list.Children.Remove(card);
                        if (list.Children.Count == 0)
                            AddNoBlueprintsMessage();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            this,
                            $"The blueprint could not be deleted.\n\n{ex.Message}",
                            "Delete Blueprint Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                };
                Grid.SetRow(deleteButton, 0);
                // Overlay the button in the same card cell; no reserved column/gap.
                Panel.SetZIndex(deleteButton, 10);
                cardGrid.Children.Add(deleteButton);

                card.Child = cardGrid;
                list.Children.Add(card);
            }
        }

        if (dialog.ShowDialog() != true)
            return null;

        return dialog.SelectedValue as string;
    }

    private async Task UpdateBlueprintStoreName(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ImportBlueprintStoreName.Text = "No blueprint selected";
            ImportBlueprintInfo.Text = "Select a .rrblueprint file.";
            return;
        }

        try
        {
            var result = await RunEngine($"blueprint_metadata {Q(path)}");
            if (result.code == 0)
            {
                using var doc = JsonDocument.Parse(result.stdout);
                if (doc.RootElement.TryGetProperty("shop_name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()))
                    ImportBlueprintStoreName.Text = n.GetString()!;
                else
                    ImportBlueprintStoreName.Text = Path.GetFileNameWithoutExtension(path);
            }
            else
                ImportBlueprintStoreName.Text = Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            ImportBlueprintStoreName.Text = Path.GetFileNameWithoutExtension(path);
        }
        ImportBlueprintInfo.Text = Info(path, ".rrblueprint");
    }

    private async void SelectSave_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && !button.IsEnabled)
            return;

        await SelectSaveAsync(sender, e);
    }

    private async Task SelectSaveAsync(object sender, RoutedEventArgs e)
    {
        string destination = "source";
        if (sender is FrameworkElement fe)
        {
            if (fe.Name == "TargetSelectSaveButton" || fe.Name == "ImportTargetSelectSaveButton")
                destination = "target";
            else if (fe.Name == "InspectorSelectSaveButton")
                destination = "info";
            else if (fe.Name == "HealthSelectSaveButton")
                destination = "health";
        }
var dialog = new OverlayDialogHost(this, SlidePanelMode.Right)
        {
            Background = (Brush)Resources["CardBrush"],
            Foreground = (Brush)Resources["ForegroundBrush"]
        };

        var outer = new Grid { Margin = new Thickness(12, 18, 12, 18) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = _mode == "transfer"
                ? (destination == "source" ? "SELECT SOURCE SAVE" : "SELECT TARGET SAVE")
                : "SELECT SAVE",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(title, 0);
        outer.Children.Add(title);

        var subtitle = new TextBlock
        {
            Text = "Choose a save slot. Missing save slots cannot be selected.",
            Foreground = (Brush)Resources["SecondaryBrush"],
            Margin = new Thickness(0, 34, 0, 12)
        };
        Grid.SetRow(subtitle, 0);
        outer.Children.Add(subtitle);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(scroll, 1);

        var slots = new StackPanel
        {
            Margin = new Thickness(0, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        scroll.Content = slots;
        outer.Children.Add(scroll);

        var close = new Button
        {
            Content = L("Cancel"),
            Width = 100,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
            Style = (Style)Resources["SettingsButtonStyle"]
        };
        close.Click += (_, _) => dialog.DialogResult = false;
        Grid.SetRow(close, 2);
        outer.Children.Add(close);

        dialog.Content = outer;

        dialog.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Escape)
            {
                keyArgs.Handled = true;
                dialog.DialogResult = false;
            }
        };

        // Load each of the five fixed game save slots and show its metadata.
        for (int i = 1; i <= 5; i++)
        {
            string fileName = $"Player_Save{i}.sav";
            string path = SavePath(fileName);

            SaveSlotView slot = await ReadSaveSlotView(fileName, path);

            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Resources["BorderBrush"],
                Background = (Brush)Resources["SecondaryCardBrush"]
            };

            if (!slot.Exists)
            {
                var missing = new TextBlock
                {
                    Text = "Missing Save Slot",
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Resources["SecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 10, 16, 10)
                };
                card.Child = missing;
            }
            else
            {
                bool dayZeroUnsupported = IsDayZeroOrEarlier(slot);
                bool matchingPlayerSave5GameDay = MatchesUnsupportedGameDay(slot);

                bool alreadySelectedOnOtherSide =
                    _mode == "transfer" &&
                    ((destination == "source" &&
                      !string.IsNullOrWhiteSpace(TargetBox.Text) &&
                      string.Equals(Path.GetFullPath(TargetBox.Text), Path.GetFullPath(slot.Path), StringComparison.OrdinalIgnoreCase)) ||
                     (destination == "target" &&
                      !string.IsNullOrWhiteSpace(SourceBox.Text) &&
                      string.Equals(Path.GetFullPath(SourceBox.Text), Path.GetFullPath(slot.Path), StringComparison.OrdinalIgnoreCase)));

                var button = new Button
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = (Brush)Resources["ForegroundBrush"],
                    Style = (Style)Resources["SaveSlotButtonStyle"],
                    Content = BuildSaveSlotContent(
                        slot,
                        slot.BackupDetected
                            ? "BACKUP SAVE DETECTED\nYOUR SAVE FILE COULD NOT BE LOADED PROPERLY, BUT A BACKUP COPY WAS FOUND."
                            : dayZeroUnsupported
                                ? "Day 0 Not Supported"
                                : matchingPlayerSave5GameDay
                                    ? "Game Day Not Supported"
                                    : alreadySelectedOnOtherSide
                                        ? (destination == "target" ? "SOURCE SAVE" : "TARGET SAVE")
                                        : null),
                    Tag = slot.Path,
                    IsEnabled = !alreadySelectedOnOtherSide
                        && !dayZeroUnsupported
                        && !matchingPlayerSave5GameDay,
                    ToolTip = slot.BackupDetected
                        ? "A vanilla backup was found. It will be validated and copied to the normal save filename before use."
                        : dayZeroUnsupported
                        ? "This save has Game day less than 1 and cannot be selected."
                        : matchingPlayerSave5GameDay
                            ? "This save has the same unsupported Game day state as Player_Save5.sav."
                            : alreadySelectedOnOtherSide
                            ? (destination == "source"
                                ? "This save is already selected as the target save."
                                : "This save is already selected as the source save.")
                            : null
                };

                if (alreadySelectedOnOtherSide || dayZeroUnsupported)
                {
                    card.Opacity = 0.55;
                    card.BorderBrush = (Brush)Resources["BorderBrush"];
                }

                button.MouseEnter += (_, _) =>
                {
                    card.Background = (Brush)Resources["AccentHoverBrush"];
                    card.BorderBrush = (Brush)Resources["AccentBrush"];
                };
                button.MouseLeave += (_, _) =>
                {
                    card.Background = (Brush)Resources["SecondaryCardBrush"];
                    card.BorderBrush = (Brush)Resources["BorderBrush"];
                };
                button.PreviewMouseDown += (_, _) =>
                {
                    card.Background = (Brush)Resources["AccentPressedBrush"];
                    card.BorderBrush = (Brush)Resources["AccentBrush"];
                };
                button.PreviewMouseUp += (_, _) =>
                {
                    if (button.IsMouseOver)
                    {
                        card.Background = (Brush)Resources["AccentHoverBrush"];
                        card.BorderBrush = (Brush)Resources["AccentBrush"];
                    }
                };

                button.Click += async (_, _) =>
                {
                    var selectedPath = (string)button.Tag;
                    if (slot.BackupDetected)
                    {
                        try
                        {
                            selectedPath = await PromoteVanillaBackupIfNeeded(selectedPath);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                this,
                                ex.Message,
                                "Backup Save Check Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            return;
                        }
                    }
                    if (destination == "source")
                    {
                        if (_mode == "transfer")
                        {
                            _transferSourceHealthOk = false;
                            _transferTargetHealthOk = false;
                            TargetBox.Text = "";
                            TransferTargetPath = null;
                            TargetStoreName.Text = "No save selected";
                            SourceHealthStatus.Text = "Waiting for a healthy source save.";
                            TransferActionButton.Visibility = Visibility.Collapsed;
                        }

                        SourceBox.Text = selectedPath;
                        _ = UpdateStoreName(selectedPath, SourceStoreName);
                        if (_mode == "transfer") TransferSourcePath = selectedPath;
                        else if (_mode == "export") ExportSourcePath = selectedPath;
                    }
                    else if (destination == "target")
                    {
                        TargetBox.Text = selectedPath;
                        if (_mode == "import")
                        {
                            ImportTargetPath = selectedPath;
                            _ = UpdateStoreName(selectedPath, ImportTargetStoreName);
                        }
                        else
                        {
                            _ = UpdateStoreName(selectedPath, TargetStoreName);
                            if (_mode == "transfer") TransferTargetPath = selectedPath;
                        }
                    }
                    else if (destination == "info")
                    {
                        InspectorSaveBox.Text = selectedPath;
                        _ = UpdateStoreName(selectedPath, InspectorStoreName);
                        InfoSavePath = selectedPath;
                        RememberCurrentPaths();
                    }
                    dialog.DialogResult = true;
                };

                card.Child = button;
            }

            slots.Children.Add(card);
        }

        bool selected = dialog.ShowDialog() == true;

        if (!selected)
        {
            if (destination != "info")
            {
                if (_mode == "transfer") ResetTransferState();
                else if (_mode == "export") ResetExportState();
                else if (_mode == "import") ResetImportState();
                else if (_mode == "manage") { SourceBox.Text = ""; SourceSelectSaveButton.IsEnabled = true; }
                else if (_mode == "health")
                {
                    HealthSavePath = null;
                    HealthResultsPanel.Children.Clear();
                    HealthRunButton.IsEnabled = true;
                }
            }
            return;
        }

        if (destination == "source" && File.Exists(SourceBox.Text))
        {
            // Lock the Transfer source selector immediately after a save is chosen.
            // The health check and metadata work below are asynchronous, so leaving
            // this enabled would allow the user to open the selector again while
            // the first save is still being validated. ResetTransferState() restores
            // the button if validation fails or the user resets the transfer.
            if (_mode == "transfer")
            {
                SourceSelectSaveButton.IsEnabled = false;
                SourceHealthStatus.Text = "Checking save…";
                UpdateActionButtons();
            }

            SourceInfo.Text = Info(SourceBox.Text, ".sav");
            await UpdateSaveDetails(SourceBox.Text, SourceCount, SourceMovies, SourceLevel, SourceMoney, SourceGameDate, SourceLastPlayed);

            if (_mode == "manage")
            {
                SourceHealthStatus.Text = "Checking save…";
                var manageHealth = await CheckSingleSaveHealth(SourceBox.Text);
                if (!manageHealth.Passed)
                {
                    SourceHealthStatus.Text = "Warning";
                    MessageBox.Show(this,
                        "The selected save failed the health check.\n\n" +
                        (string.IsNullOrWhiteSpace(manageHealth.Log) ? "No additional diagnostic information was returned." : manageHealth.Log),
                        "Save Health Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SourceSelectSaveButton.IsEnabled = true;
                    return;
                }
                SourceHealthStatus.Text = $"Passed • {manageHealth.Objects} objects";
                SourceSelectSaveButton.IsEnabled = true;
                UpdateActionButtons();
                return;
            }

            if (_mode == "export")
            {
                SourceHealthStatus.Text = "Checking save…";
                _exportSourceHealthOk = false;

                var exportHealth = await CheckSingleSaveHealth(SourceBox.Text);
                if (!exportHealth.Passed)
                {
                    SourceHealthStatus.Text = "Warning";
                    MessageBox.Show(
                        this,
                        "The selected save failed its health check.\n\n" +
                        (string.IsNullOrWhiteSpace(exportHealth.Log)
                            ? "No additional diagnostic information was returned."
                            : exportHealth.Log),
                        "Save Health Check Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ResetExportState();
                    return;
                }

                _exportSourceHealthOk = true;
                SourceHealthStatus.Text = $"Passed • {exportHealth.Objects} objects";
                SourceSelectSaveButton.Visibility = Visibility.Collapsed;
                BlueprintNameBox.Visibility = Visibility.Visible;
                ExportActionButton.Visibility = Visibility.Visible;
                ResetExportButton.Visibility = Visibility.Visible;
                ResetExportButton.IsEnabled = true;
                BlueprintExportStatus.Visibility = Visibility.Visible;
                BlueprintExportStatus.Text = "Enter a blueprint name to enable export.";
                BlueprintExportStatus.Foreground = (Brush)Resources["SecondaryBrush"];
                UpdateActionButtons();
            }
            else
            {
                _transferSourceHealthOk =
                    await CheckTransferSaveHealth(SourceBox.Text, SourceHealthStatus, "The source save");

                if (!_transferSourceHealthOk)
                {
                    ResetTransferState();
                    return;
                }

                ResetTransferButton.IsEnabled = true;
                // The source save is now locked for this transfer. The user must
                // reset the transfer before selecting a different source save.
                SourceSelectSaveButton.IsEnabled = false;
                UpdateActionButtons();

                // A healthy source automatically advances to the target selector.
                await SelectSaveAsync(TargetSelectSaveButton, new RoutedEventArgs());
            }
        }
        else if (destination == "target" && File.Exists(TargetBox.Text))
        {
            if (_mode == "import")
            {
                ImportTargetInfo.Text = Info(TargetBox.Text, ".sav");
                await UpdateSaveDetails(
                    TargetBox.Text, ImportTargetCount, ImportTargetMovies,
                    ImportTargetLevel, ImportTargetMoney,
                    ImportTargetGameDate, ImportTargetLastPlayed);

                ImportBlueprintHealthStatus.Text = "Target save health check: Checking…";
                _importTargetHealthOk = false;
                UpdateActionButtons();

                var targetHealth = await CheckSingleSaveHealth(TargetBox.Text);
                if (!targetHealth.Passed)
                {
                    ImportBlueprintHealthStatus.Text = "Target save health check: Warning";
                    MessageBox.Show(
                        this,
                        "The selected target save failed its health check.\n\n" +
                        (string.IsNullOrWhiteSpace(targetHealth.Log)
                            ? "No additional diagnostic information was returned."
                            : targetHealth.Log),
                        "Target Save Health Check Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ResetImportState();
                    return;
                }

                var blueprintRooms = ReadBlueprintRoomUnlocks(ImportBlueprintPath ?? SourceBox.Text);
                if (!blueprintRooms.Ok)
                {
                    MessageBox.Show(
                        this,
                        "The blueprint room unlock data could not be read.\n\n" + blueprintRooms.Error,
                        "Room Compatibility Check Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ResetImportState();
                    return;
                }

                var targetRooms = await ReadSaveRoomUnlocks(TargetBox.Text);
                if (!targetRooms.Ok)
                {
                    MessageBox.Show(
                        this,
                        "The target save room unlock data could not be read.\n\n" + targetRooms.Error,
                        "Room Compatibility Check Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ResetImportState();
                    return;
                }

                var compatibility = CheckRoomCompatibility(
                    blueprintRooms.Rooms, targetRooms.Rooms,
                    "Blueprint", "Target save");

                if (!compatibility.Ok)
                {
                    ImportBlueprintHealthStatus.Text =
                        "Room compatibility: Warning — " + compatibility.Error;
                    MessageBox.Show(
                        this,
                        compatibility.Error,
                        "Room Compatibility Check Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ResetImportState();
                    return;
                }

                _importTargetHealthOk = true;
                ImportBlueprintHealthStatus.Text =
                    $"Target save health check: Passed • {targetHealth.Objects} objects\n" +
                    $"Room compatibility: Passed • {FormatRoomList(UnlockedRoomNames(targetRooms.Rooms))}";
                ImportTargetSelectSaveButton.Visibility = Visibility.Collapsed;
                ImportActionButton.Visibility = Visibility.Visible;
                ResetImportButton.Visibility = Visibility.Visible;
                ResetImportButton.IsEnabled = true;
                UpdateActionButtons();
            }
            else
            {
                TargetInfo.Text = Info(TargetBox.Text, ".sav");
                await UpdateSaveDetails(TargetBox.Text, TargetCount, TargetMovies, TargetLevel, TargetMoney, TargetGameDate, TargetLastPlayed);

                _transferTargetHealthOk =
                    await CheckTransferSaveHealth(TargetBox.Text, SourceHealthStatus, "The target save");

                if (!_transferTargetHealthOk)
                {
                    ResetTransferState();
                    return;
                }

                SourceSelectSaveButton.Visibility = Visibility.Collapsed;
                var sourceRooms = await ReadSaveRoomUnlocks(TransferSourcePath ?? SourceBox.Text);
                var targetRooms = await ReadSaveRoomUnlocks(TargetBox.Text);

                if (!sourceRooms.Ok || !targetRooms.Ok)
                {
                    SourceHealthStatus.Text = "Room compatibility: Warning";
                    MessageBox.Show(
                        this,
                        "Room unlock data could not be read.\n\n" +
                        (!sourceRooms.Ok ? "Source: " + sourceRooms.Error : "") +
                        (!targetRooms.Ok ? "\nTarget: " + targetRooms.Error : ""),
                        "Room Compatibility Check Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ResetTransferState();
                    return;
                }

                var compatibility = CheckRoomCompatibility(
                    sourceRooms.Rooms, targetRooms.Rooms,
                    "Source save", "Target save");

                if (!compatibility.Ok)
                {
                    SourceHealthStatus.Text = "Room compatibility: Warning — " + compatibility.Error;
                    MessageBox.Show(
                        this,
                        compatibility.Error,
                        "Room Compatibility Check Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ResetTransferState();
                    return;
                }

                SourceHealthStatus.Text =
                    $"Passed • Rooms: {FormatRoomList(UnlockedRoomNames(sourceRooms.Rooms))}";

                TransferActionButton.Visibility = Visibility.Visible;
                ResetTransferButton.Visibility = Visibility.Visible;
                ResetTransferButton.IsEnabled = true;
                UpdateActionButtons();
            }
        }
        else if (destination == "info" && File.Exists(InspectorSaveBox.Text))
        {
            await LoadInspector(InspectorSaveBox.Text);
        }
    }

    private async Task UpdateStoreName(string path, TextBlock target)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            target.Text = "No save selected";
            return;
        }

        try
        {
            var result = await RunEngine($"metadata {Q(path)}");
            if (result.code != 0)
            {
                target.Text = Path.GetFileNameWithoutExtension(path);
                return;
            }

            using var doc = JsonDocument.Parse(result.stdout);
            if (doc.RootElement.TryGetProperty("shop_name", out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                target.Text = value.GetString()!;
            }
            else
            {
                target.Text = Path.GetFileNameWithoutExtension(path);
            }
        }
        catch
        {
            target.Text = Path.GetFileNameWithoutExtension(path);
        }
    }

    private string? FindHighestVanillaBackup(string canonicalPath)
    {
        if (File.Exists(canonicalPath))
            return null;

        var directory = Path.GetDirectoryName(canonicalPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        var baseName = Path.GetFileNameWithoutExtension(canonicalPath);
        var escaped = Regex.Escape(baseName);
        var regex = new Regex(
            "^" + escaped + @"_backup(?<number>[0-9]*)\.sav$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var candidates = new List<(string Path, int Number)>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.sav", SearchOption.TopDirectoryOnly))
        {
            var match = regex.Match(Path.GetFileName(file));
            if (!match.Success)
                continue;

            var numberText = match.Groups["number"].Value;
            var number = string.IsNullOrEmpty(numberText) ? 0 :
                int.TryParse(numberText, out var n) ? n : -1;

            if (number >= 0)
                candidates.Add((file, number));
        }

        // Highest numbered backup wins. The unnumbered _backup.sav is number 0.
        return candidates
            .OrderByDescending(x => x.Number)
            .ThenByDescending(x => File.GetLastWriteTimeUtc(x.Path))
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    private async Task<string> PromoteVanillaBackupIfNeeded(string canonicalPath)
    {
        if (File.Exists(canonicalPath))
            return canonicalPath;

        var backup = FindHighestVanillaBackup(canonicalPath);
        if (string.IsNullOrWhiteSpace(backup))
            return canonicalPath;

        // Backups are read-only sources. Validate first, then COPY. Never move,
        // rename, delete, or otherwise modify the backup itself.
        var check = await CheckSaveSanity(backup, "The detected backup save");
        if (!check.Ok)
            throw new InvalidDataException(
                "BACKUP SAVE DETECTED, but the backup failed its sanity check.\n\n" +
                check.Error);

        File.Copy(backup, canonicalPath, false);

        // Validate the newly created canonical copy before allowing any operation.
        var copied = await CheckSaveSanity(canonicalPath, "The restored save copy");
        if (!copied.Ok)
        {
            DeleteFileIfPresent(canonicalPath);
            throw new InvalidDataException(
                "The detected backup passed validation, but the restored save copy failed validation. " +
                "The backup file was not modified.\n\n" + copied.Error);
        }

        return canonicalPath;
    }

    private async Task<SaveSlotView> ReadSaveSlotView(string fileName, string path)
    {
        try
        {
            var backupPath = FindHighestVanillaBackup(path);
            var metadataPath = File.Exists(path) ? path : backupPath;
            if (string.IsNullOrWhiteSpace(metadataPath))
                return new SaveSlotView(fileName, path, false, "—", "—", "—", "—", "—", "—", "—", "—", 0);

            var result = await RunEngine($"metadata {Q(metadataPath)}");
            if (result.code != 0)
                return new SaveSlotView(
                    fileName, path, true, "Unable to read", "Unable to read", "Unable to read",
                    "Unable to read", "Unable to read", "Unable to read", "Unable to read", "Unable to read",
                    CountBackups(path),
                    !File.Exists(path) && !string.IsNullOrWhiteSpace(backupPath),
                    backupPath);

            using var doc = JsonDocument.Parse(result.stdout);
            var root = doc.RootElement;

            string Get(string name)
            {
                if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                    return "—";
                return value.ValueKind == JsonValueKind.Number
                    ? value.ToString()
                    : (value.GetString() ?? "—");
            }

            var shopName = Get("shop_name");
            var movies = Get("total_movies");
            var level = Get("level");
            var storeValue = Get("store_value");
            if (storeValue != "—") storeValue = GetCurrencySymbol() + storeValue;

            var backupCount = CountBackups(path);

            return new SaveSlotView(
                fileName,
                path,
                true,
                shopName,
                movies,
                Get("money64") == "—" ? "—" : GetCurrencySymbol() + Get("money64"),
                level,
                storeValue,
                Get("game_date"),
                Get("game_day"),
                Get("last_played"),
                backupCount,
                !File.Exists(path) && !string.IsNullOrWhiteSpace(backupPath),
                backupPath);
        }
        catch
        {
            var fallbackBackupPath = FindHighestVanillaBackup(path);
            return new SaveSlotView(
                    fileName, path, true, "Unable to read", "Unable to read", "Unable to read",
                    "Unable to read", "Unable to read", "Unable to read", "Unable to read", "Unable to read",
                    CountBackups(path),
                    !File.Exists(path) && !string.IsNullOrWhiteSpace(fallbackBackupPath),
                    fallbackBackupPath);
        }
    }

    private static bool IsDayZeroOrEarlier(SaveSlotView slot)
    {
        if (!slot.Exists)
            return false;

        if (!int.TryParse(slot.GameDay, out var gameDay))
            return false;

        return gameDay < 1;
    }

    private static bool MatchesUnsupportedGameDay(SaveSlotView slot)
    {
        if (!slot.Exists)
            return false;

        // Player_Save5.sav has no readable Game day value. A save with an
        // unavailable Game day therefore matches the Player_Save5 condition.
        return !int.TryParse(slot.GameDay, out _);
    }

    private int CountBackups(string savePath)
    {
        try
        {
            var directory = SaveFolderPath;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return 0;

            var baseName = Path.GetFileNameWithoutExtension(savePath);
            var escapedBase = Regex.Escape(baseName);

            // Count backups belonging specifically to this save slot. This includes
            // the original numbered backup convention plus Workshop and Store Transfer
            // backups created for the same save.
            var pattern = new Regex(
                "^" + escapedBase +
                @"(?:_backup(?:[0-9]+)?|_WorkshopBackup_.+|_StoreTransferBackup_.+)\.sav$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return Directory.EnumerateFiles(directory, "*.sav", SearchOption.TopDirectoryOnly)
                .Count(file => pattern.IsMatch(Path.GetFileName(file)));
        }
        catch
        {
            return 0;
        }
    }

    private Grid BuildSaveSlotContent(SaveSlotView slot, string? roleTag = null)
    {
        // Compact save selector layout: a single header row followed by
        // two compact rows of metadata. This keeps all five save slots
        // visible with minimal scrolling.
        var panel = new Grid
        {
            Margin = new Thickness(14, 10, 14, 10)
        };

        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < 4; i++)
            panel.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

        var storeName = new TextBlock
        {
            Text = slot.ShopName == "—" ? "RETRO REWIND" : slot.ShopName,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 7)
        };
        Grid.SetRow(storeName, 0);
        Grid.SetColumn(storeName, 0);
        Grid.SetColumnSpan(storeName, string.IsNullOrWhiteSpace(roleTag) ? 3 : 1);
        panel.Children.Add(storeName);

        if (!string.IsNullOrWhiteSpace(roleTag))
        {
            var tag = new Border
            {
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(8, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Resources["CardBrush"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 680
            };
            tag.Child = new TextBlock
            {
                Text = roleTag,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Resources["SecondaryBrush"],
                TextWrapping = TextWrapping.NoWrap,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(tag, 0);
            Grid.SetColumn(tag, 1);
            Grid.SetColumnSpan(tag, 3);
            panel.Children.Add(tag);
        }

        AddSlotField(panel, 1, 0, "Movies", slot.Movies);
        AddSlotField(panel, 1, 1, "Money", slot.Money);
        AddSlotField(panel, 1, 2, "Game date", slot.GameDate);
        AddSlotField(panel, 1, 3, "Last played", slot.LastPlayed);

        AddSlotField(panel, 2, 0, "Level", slot.Level);
        AddSlotField(panel, 2, 1, "Store value", slot.StoreValue);
        AddSlotField(panel, 2, 2, "Game day", slot.GameDay);
        AddSlotField(panel, 2, 3, "Backups", slot.Backups.ToString());

        return panel;
    }

    private void AddSlotField(Grid panel, int row, int column, string label, string value)
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(0, 0, 12, 2)
        };

        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = (Brush)Resources["SecondaryBrush"]
        });

        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        Grid.SetRow(stack, row);
        Grid.SetColumn(stack, column);
        panel.Children.Add(stack);
    }
}
