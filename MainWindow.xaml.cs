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
using System.Windows.Interop;
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
    // PAK load-order reordering is handled entirely inside the application.
    // We intentionally do not use WPF DragDrop.DoDragDrop here: nested Buttons
    // and ScrollViewer mouse capture made native drag/drop unreliable.
    private Grid? _pakDragRow;
    private ListBox? _pakDragList;
    private Point _pakDragStartPoint;
    private bool _pakDragArmed;
    private bool _pakDragActive;
    private string? _pakDragTargetPath;
    private bool _pakDragInsertAfter;
    private string[] _pakDragPaths = Array.Empty<string>();
    private Grid? _ue4ssDragRow;
    private ListBox? _ue4ssDragList;
    private Point _ue4ssDragStartPoint;
    private bool _ue4ssDragArmed;
    private bool _ue4ssDragActive;
    private string? _ue4ssDragSourceName;
    private string? _ue4ssDragTargetName;
    private bool _ue4ssDragInsertAfter;




    private string SaveFolderPath => Path.GetDirectoryName(SavePath()) ?? string.Empty;

    private string BlueprintFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Retro Rewind Modhub",
        "Blueprints");






    // View model used by the save-slot selector.  This type was accidentally
    // omitted from the Phase 1 package while its consumers remained in
    // MainWindow.xaml.cs.
    private sealed class BlueprintDialogInfo
    {
        public string StoreName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public string SourceSave { get; set; } = string.Empty;
        public string Rooms { get; set; } = string.Empty;
        public string Objects { get; set; } = string.Empty;
    }

    private sealed class SaveSlotView
    {
        public string FileName { get; }
        public string Path { get; }
        public bool Exists { get; }
        public string ShopName { get; }
        public string Movies { get; }
        public string Money { get; }
        public string Level { get; }
        public string StoreValue { get; }
        public string GameDate { get; }
        public string GameDay { get; }
        public string LastPlayed { get; }
        public int Backups { get; }
        public bool BackupDetected { get; }
        public string? BackupPath { get; }

        public SaveSlotView(
            string fileName,
            string path,
            bool exists,
            string shopName,
            string movies,
            string money,
            string level,
            string storeValue,
            string gameDate,
            string gameDay,
            string lastPlayed,
            int backups,
            bool backupDetected = false,
            string? backupPath = null)
        {
            FileName = fileName;
            Path = path;
            Exists = exists;
            ShopName = shopName;
            Movies = movies;
            Money = money;
            Level = level;
            StoreValue = storeValue;
            GameDate = gameDate;
            GameDay = gameDay;
            LastPlayed = lastPlayed;
            Backups = backups;
            BackupDetected = backupDetected;
            BackupPath = backupPath;
        }
    }


    
    private string _mode = "transfer";
    // Import is only permitted after the selected blueprint passes the same
    // blueprint sanity check used after blueprint creation.
    private bool _importBlueprintHealthOk = false;
    private bool _exportSourceHealthOk = false;
    private bool _importTargetHealthOk = false;
    private bool _transferSourceHealthOk = false;
    private bool _transferTargetHealthOk = false;
    private bool _operationBusy = false;

    private bool _sidebarExpanded;
    private bool _modManagerExpanded;
    private bool _saveManagerExpanded;
    private bool _homeNewsLoading;
    private bool _gameActive;
    private bool _ue4ssIntegrityMissing;
    private bool _ue4ssUpdateAvailable;
    private string _ue4ssLatestVersion = string.Empty;

    private string _detectedGameName = string.Empty;
    private DispatcherTimer? _gameActivityTimer;
    private DispatcherTimer? _resourceUsageTimer;
    private Process? _resourceUsageProcess;
    private TimeSpan _resourceUsageLastCpu;
    private DateTime _resourceUsageLastSampleUtc;
    private bool _startupInitialized;
    private readonly TaskCompletionSource<bool> _startupReadyTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task StartupReady => _startupReadyTcs.Task;
    private bool _modRefreshInProgress;
    private CancellationTokenSource? _modRefreshCts;
    private List<ModEntry>? _cachedPakMods;
    private List<ModEntry>? _cachedUe4ssMods;
    private List<PendingModEntry>? _cachedPendingMods;
    private DateTime _modCacheUpdatedUtc = DateTime.MinValue;
    private int _modStateRefreshVersion;
    private int _modSnapshotVersion;
    private int _modUiAppliedVersion = -1;
    private Dictionary<string, NexusModMetadata>? _modListMetadataCache;
    private bool _videoLibraryRefreshInProgress;
    private List<string>? _cachedVideoLibrary;
    private DateTime _videoLibraryCacheUpdatedUtc = DateTime.MinValue;
    private CancellationTokenSource? _videoLibraryRefreshCts;
    private bool _downloadsRefreshInProgress;
    private List<DownloadEntry>? _cachedDownloads;
    private DateTime _downloadsCacheUpdatedUtc = DateTime.MinValue;
    private CancellationTokenSource? _downloadsRefreshCts;
    private bool _showHiddenDownloads;
    private readonly Dictionary<string, ActiveDownloadState> _activeDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _activeDownloadsSync = new();
    private DispatcherTimer? _downloadUiTimer;
    private string _nexusAccountPremiumStatus = "Unknown";
    private string _steamApiKey = "";
    private DateTime _nexusAccountStatusCheckedUtc = DateTime.MinValue;
    private string _nexusHomeUserId = "";
    private string _nexusHomeUserName = "";
    private string _nexusHomeAvatarUrl = "";
    private string _nexusHomeAccountType = "";
    private int _nexusHomeDailyRemaining = -1;
    private int _nexusHomeDailyLimit = -1;
    private int _nexusHomeHourlyRemaining = -1;
    private int _nexusHomeHourlyLimit = -1;
    private DateTime _nexusHomeAccountCheckedUtc = DateTime.MinValue;
    private bool _nexusHomeRefreshInProgress;
    private bool _steamHomeRefreshInProgress;
    private List<SteamHomeGame> _steamHomeGames = new();
    private List<SteamHomeAchievement> _steamHomeAchievements = new();
    private DateTime _nexusLastBackgroundRefreshUtc = DateTime.MinValue;
    private CancellationTokenSource? _conflictScanCts;
    private bool _conflictScanInProgress;
    private double _conflictSmoothScrollTarget;
    private bool _conflictSmoothScrollRunning;
    private List<PakConflictIndexEntry> _conflictIndex = new();
    private const string PakConflictIndexFileName = "PakConflictIndex.json";
    private bool _nexusBackgroundRefreshInProgress;
    private CancellationTokenSource? _nexusBackgroundCts;
    private CancellationTokenSource? _homeNewsCts;
    private readonly Dictionary<string, (DateTime CachedUtc, List<SteamNewsItem> Items)> _homeNewsCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _homeNewsLastRefreshUtc = DateTime.MinValue;
    private static readonly TimeSpan BackgroundMetadataRefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HomeNewsRefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GameActivityPollInterval = TimeSpan.FromSeconds(3);
    private DispatcherTimer? _sidebarGroupAnimationTimer;
    private StackPanel? _sidebarGroupAnimatingPanel;
    private double _sidebarGroupAnimationStart;
    private double _sidebarGroupAnimationTarget;
    private DateTime _sidebarGroupAnimationStarted;
    private DispatcherTimer? _sidebarAnimationTimer;
    private double _sidebarAnimationStart;
    private double _sidebarAnimationTarget;
    private DateTime _sidebarAnimationStarted;
    private const double SidebarCollapsedWidth = 64;
    private const double SidebarExpandedWidth = 220;
    private DispatcherTimer? _infoSaveWatcher;
    private bool _infoRefreshBusy;
    private DateTime _infoLastWriteUtc = DateTime.MinValue;
    private long _infoLastLength = -1;
    private readonly ObservableCollection<ObjectRow> _allObjects = new();
    private string _selectedObjectGroup = "Decorations";
    private string _selectedFont = "Gillius ADF";
    private static string AppSupportFolder => Path.Combine(AppContext.BaseDirectory, "RetroRewindModHub_Data");
    private static string DefaultModhubFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Retro Rewind Modhub");
    private readonly string EnginePath = Path.Combine(AppContext.BaseDirectory, "RetroRewindModHub_Data", "Engine", "engine.py");
    private string RememberedPathsFile => Path.Combine(ModhubFolderPath, "RetroRewindModhub.json");
    private string _saveFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RetroRewind", "Saved", "SaveGames");
    private string _modsFolderPath = "";
    private string _selectedPalette = "60s Mod";
    private string _nexusApiKey = "";
    private bool _showUe4ssDefaultMods;
    private string _powerSaveMode = "auto";
    private bool _runAsAdmin;
    private bool _autoStartWithWindowsLogin;
    private bool _enableWindowsNotifications;
    private bool _closeToTaskbar;
    private List<string> _runForceLoadLibraries = new() { "dwmapi.dll" };
    private List<string> _runLaunchExecutables = new() { "RetroRewind-Win64-Shipping.exe" };
    private string _runArguments = "";

    private bool _requiredFilesUpdateCheckInProgress;
    private bool _requiredFilesInstallError;
    private bool _allowWindowClose;
    private bool _shutdownStarted;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private string _modManagerPath = "";
    private string _modManagerType = "";
    private string PakLoadOrderFile => Path.Combine(ModsRoot, "PAKLoadOrder.json");
    private string ModhubFolderPath => DefaultModhubFolder;
    private string ModsRoot => string.IsNullOrWhiteSpace(_modsFolderPath) ? Path.Combine(DefaultModhubFolder, "Mods") : _modsFolderPath;
    private string PakVirtualRoot => Path.Combine(ModsRoot, "PAK");
    private ModEntry? _selectedConfigMod;
    private readonly HashSet<string> _selectedPakModPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedPakGroups = new(StringComparer.OrdinalIgnoreCase);
    private bool _bulkSymbolicLinkOperation;
    private DateTime _lastPakSelectionUtc = DateTime.MinValue;
    private string? _selectedConfigPath;
    private string? _selectedConfigType;
    private string? _selectedConfigDefinitionPath;
    private readonly List<ConfigField> _configFields = new();
    private readonly Dictionary<int, BundleState> _bundleStates = new();
    private readonly Dictionary<string, YamlTableState> _yamlTables = new(StringComparer.OrdinalIgnoreCase);
    private string ModDefaultsFile => Path.Combine(ModsRoot, "UE4SSSettings.json");
    private const string ModHubControlledSuffix = ".RRModHub.CONTROLLED";
    private readonly List<FileStream> _modHubControlledFileLocks = new();
    private readonly object _modHubControlledFileLocksSync = new();
    private string VideoLibraryRoot => Path.Combine(ModsRoot, "Videos");
    private string VideoEditorRoot => Path.Combine(VideoLibraryRoot, "modhub");
    private string VideoEditorTempRoot => Path.Combine(VideoEditorRoot, "_tmp");
    private string VideoReplacementsFile => Path.Combine(ModsRoot, "VideoReplacements.json");
    private bool _refreshingVideosUi;
    private string? _selectedVideoLibraryFile;
    private string? _videoEditorInputFile;
    private string? _videoEditorOriginalName;
    private string? _videoEditorTempFile;
    private bool _videoEditorPreviewLoaded;
    private bool _videoEditorPreviewPreparing;
    private bool _videoEditorPreviewFallbackAttempted;
    private string? _videoEditorPreviewFile;
    private bool _videoEditorTimelineUpdating;
    private TimeSpan _videoEditorPreviewDuration = TimeSpan.Zero;
    private TimeSpan _videoEditorSourceDuration = TimeSpan.Zero;
    private readonly DispatcherTimer _videoEditorPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
    // Windows Media Foundation fallback for cases where LibVLC cannot create a
    // working WPF drawable. This keeps ordinary MP4 playback available.
    private MediaElement? _videoEditorFallbackMediaElement;
    private bool _videoEditorUsingFallbackMediaElement;
    private bool _videoEditorIsPlaying;
    private TimeSpan _videoEditorPendingAudioPosition = TimeSpan.Zero;
    private bool _videoEditorAudioWantedPlaying;
    private string? _videoEditorFallbackFile;
    private string? _videoEditorAudioClockFile;
    private double _videoEditorFrameRate = 30.0;
    private Process? _videoEditorRealtimeProcess;
    private CancellationTokenSource? _videoEditorRealtimeCts;
    private WriteableBitmap? _videoEditorRealtimeBitmap;
    private Image? _videoEditorRealtimeImage;
    private const int VideoEditorRealtimeWidth = 960;
    private const int VideoEditorRealtimeHeight = 540;
    private bool _videoEditorRealtimeActive;
    private VideoEditorRenderEngine? _videoEditorRenderEngine;

    private LibVLC? _videoEditorLibVlc;
    private VlcMediaPlayer? _videoEditorMediaPlayer;
    private Media? _videoEditorMedia;
    private bool _videoEditorLibVlcReady;
    private bool _videoEditorPreviewError;
    private CancellationTokenSource? _videoEditorPreviewCts;
    private CancellationTokenSource? _videoEditorLiveRenderCts;
    private CancellationTokenSource? _videoEditorEffectRenderCts;
    private TimeSpan _videoEditorLiveSegmentStart = TimeSpan.Zero;
    private TimeSpan _videoEditorLiveSegmentDuration = TimeSpan.FromSeconds(8);
    private Window? _videoEditorEffectsOverlayWindow;
    private Grid? _videoEditorEffectsOverlayRoot;
    private WpfRectangle? _videoEditorEffectsVignette;
    private WpfRectangle? _videoEditorEffectsHue;
    private WpfRectangle? _videoEditorEffectsFlicker;
    private Grid? _videoEditorEffectsScanlines;
    private Grid? _videoEditorEffectsChroma;
    private Grid? _videoEditorEffectsTear;

    private static readonly string[] VideoSlotNames =
    {
        "RR_Channel_Adult.mp4",
        "RR_Channel_Drama.mp4",
        "RR_Channel_Fantasy.mp4",
        "RR_Channel_Horror.mp4",
        "RR_Channel_Kid.mp4",
        "RR_Channel_Police.mp4",
        "RR_Channel_Public.mp4",
        "RR_Channel_Romance.mp4",
        "RR_Channel_Scifi.mp4"
    };


    // Long lists use WPF's virtualized, pixel-based scrolling (VirtualizingPanel.ScrollUnit=Pixel).
    // Avoid a global frame-by-frame wheel animator: it competes with WPF layout/virtualization
    // when item heights or async content change and is the source of visible jitter.

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
                return match;

            // WPF text elements such as Run/Span are ContentElements, not Visuals.
            // VisualTreeHelper.GetParent throws for them, so walk the content tree first.
            if (child is ContentElement)
            {
                child = ContentOperations.GetParent(child as ContentElement);
                continue;
            }

            if (child is Visual || child is Visual3D)
            {
                child = VisualTreeHelper.GetParent(child);
                continue;
            }

            break;
        }

        return null;
    }



    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCustomMaximize();
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_closeToTaskbar)
        {
            HideToTaskbar();
            return;
        }

        Close();
    }

    private void HideToTaskbar()
    {
        try
        {
            InitializeTrayIcon();
            Hide();
            ShowInTaskbar = false;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("HideToTaskbar", ex);
            ShowInTaskbar = true;
        }
    }

    private void ToggleCustomMaximize()
    {
        if (_isFullscreen)
            return;

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateCustomMaximizeButton();
    }

    private void UpdateCustomMaximizeButton()
    {
        // The title-bar buttons are image-backed themed controls. Never replace
        // their content with Unicode glyphs when the window state changes.
        if (MaximizeWindowButton != null)
        {
            MaximizeWindowButton.ToolTip = WindowState == WindowState.Maximized
                ? "Restore"
                : "Maximize";
            System.Windows.Automation.AutomationProperties.SetName(
                MaximizeWindowButton,
                WindowState == WindowState.Maximized ? "Restore" : "Maximize");
        }
    }


    public MainWindow(bool dark)
    {
        InitializeComponent();
        ApplyProductVersionToUi();

        // Migrate the existing video tools before the Video Editor can initialize
        // LibVLC. This keeps upgrades from breaking playback when Tools moves from
        // the application folder to Documents.
        MigrateLegacyToolsFolder();

        SourceInitialized += (_, _) => InstallMaximizeWorkAreaHook();
UpdateUe4ssSharedScriptsButtons();
        // LibVLCSharp.WPF's VideoView is created in code instead of XAML.
        // WPF content cannot reliably render above the native VideoView (airspace),
        // so the live CRT/VHS preview effects are rendered by a transparent owned
        // overlay window instead of being placed inside the VideoView.
        VideoEditorPreviewHost.Content = null;

        // Restore persisted application settings before any page/theme is shown.
        // The startup-overlay build had RestoreSettings() defined but never
        // invoked, so every launch fell back to the in-memory defaults.
        RestoreSettings();
        UpdateUe4ssSpecialFoldersButtons();

        _videoEditorPreviewTimer.Tick += VideoEditorPreviewTimer_Tick;
        StateChanged += (_, _) => PositionVideoEditorEffectsOverlay();
        StateChanged += (_, _) => UpdateCustomMaximizeButton();
        Loaded += (_, _) => UpdateCustomMaximizeButton();
        VideoEditorPreviewBorder.LayoutUpdated += (_, _) => PositionVideoEditorEffectsOverlay();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        // Register at the Window level with handledEventsToo so child Buttons cannot
        // swallow the mouse sequence before the load-order drag logic sees it.
        AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(PakReorder_PreviewMouseDown), true);
        AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(PakReorder_PreviewMouseMove), true);
        AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(PakReorder_PreviewMouseUp), true);
        // The main window is kept behind the external startup splash. The startup
        // coordinator runs after Loaded while this window remains invisible, so
        // heavy page/data preparation happens before the user can interact with it.
        SetSidebarButtonWidths(SidebarCollapsedWidth);
        ObjectList.ItemsSource = _allObjects;
        // ApplyTheme establishes the base resource dictionary. RestoreSettings()
        // then restores the user's persisted palette/font; re-apply them here
        // because the base theme must not overwrite the saved selection.
        ApplyTheme(dark);
        ApplySelectedPalette();
        ApplyFontSelection();
        ApplyLanguage();
        UpdateWindowsAutoStartRegistration(_autoStartWithWindowsLogin);
        // ModHub always runs unelevated. Administrator permission is requested
        // only for the individual symbolic-link creation operation.
        _runAsAdmin = false;
        UpdateAdminModeFooter();
        _mode = "home";
        Loaded += MainWindow_StartupLoaded;
        Loaded += MainWindow_TrayStartupLoaded;
        // Start the app resource monitor after the initial visual state is ready.
        // It samples this process only, so the title-bar values reflect ModHub's
        // actual CPU, RAM and GPU usage rather than static placeholders.
        StartResourceUsageMonitoring();

        // Make Home the first rendered page. Setting _mode alone does not change
        // the XAML visibility states that start collapsed.
        UpdateMode();
        Closed += (_, _) =>
        {
            try { _resourceUsageTimer?.Stop(); } catch { }
            try { _gameActivityTimer?.Stop(); } catch { }
            try { _resourceUsageProcess?.Dispose(); } catch { }
            DisposeResourceUsageCounters();
            ReleaseModHubControlledFileLocks();
        };
    }


    private OverlayDialogHost? _activeSlidePanel;
    private enum SlidePanelMode { Right, Bottom }



    // Drawer dimensions are intentionally wider than the original 500px panel so
    // save/blueprint metadata can breathe without truncating important fields.
    private const double RightDrawerWidth = 720;
    private const double BottomDrawerHeight = 430;

    private sealed class OverlayDialogHost : ContentControl
    {
        private readonly MainWindow _owner;
        private readonly SlidePanelMode _mode;
        private DispatcherFrame? _frame;
        private bool _closing;
        private bool? _dialogResult;

        public object? SelectedValue { get; set; }
        public Action? OnBackdropClose { get; set; }
        public Action? OnEscapeClose { get; set; }
        public bool? DialogResult
        {
            get => _dialogResult;
            set
            {
                _dialogResult = value;
                Close();
            }
        }

        public OverlayDialogHost(MainWindow owner, SlidePanelMode mode)
        {
            _owner = owner;
            _mode = mode;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Focusable = true;
        }

        public bool? ShowDialog()
        {
            _owner.ShowSlidePanel(this, _mode);
            Focus();
            _frame = new DispatcherFrame();
            Dispatcher.PushFrame(_frame);
            return _dialogResult;
        }

        public void Close()
        {
            if (_closing) return;
            _closing = true;
            _owner.HideSlidePanel(this, _mode);
            if (_frame != null)
                _frame.Continue = false;
        }
    }

    private bool _isFullscreen;
    private WindowState _preFullscreenWindowState = WindowState.Maximized;
    private WindowStyle _preFullscreenWindowStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _preFullscreenResizeMode = ResizeMode.CanResize;
    private Rect _preFullscreenBounds;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private const int WM_GETMINMAXINFO = 0x0024;

    private void InstallMaximizeWorkAreaHook()
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(MainWindowWndProc);
    }

    private IntPtr MainWindowWndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO && lParam != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO
                {
                    cbSize = Marshal.SizeOf<MONITORINFO>()
                };

                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    // MINMAXINFO is the native structure Windows supplies for
                    // maximization bounds. Use the monitor work area so the
                    // custom-chrome window stays above the taskbar.
                    var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    info.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
                    info.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
                    info.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
                    info.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;
                    Marshal.StructureToPtr(info, lParam, false);
                    handled = true;
                }
            }
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }












































    private void NormalizeAndLockModHubControlledFiles()
    {
        try { NormalizeEnabledMarkers(ModsRoot); } catch { }
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            NormalizeEnabledMarkers(GetUe4ssModsRoot(gameRoot));
        }
        catch { }

        RelockModHubControlledFiles();
    }

    private static void NormalizeEnabledMarkers(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "enabled.txt", SearchOption.AllDirectories).ToList())
        {
            var target = file + ModHubControlledSuffix;
            try
            {
                if (!File.Exists(target))
                    File.Move(file, target);
            }
            catch { }
        }
    }

    private void RelockModHubControlledFiles()
    {
        ReleaseModHubControlledFileLocks();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { if (Directory.Exists(ModsRoot)) roots.Add(ModsRoot); } catch { }
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var ueRoot = GetUe4ssModsRoot(gameRoot);
            if (Directory.Exists(ueRoot)) roots.Add(ueRoot);
        }
        catch { }

        foreach (var root in roots)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*" + ModHubControlledSuffix, SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var file in files)
            {
                try
                {
                    var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    lock (_modHubControlledFileLocksSync) _modHubControlledFileLocks.Add(stream);
                }
                catch { }
            }
        }
    }

    private void ReleaseModHubControlledFileLocks()
    {
        lock (_modHubControlledFileLocksSync)
        {
            foreach (var stream in _modHubControlledFileLocks)
            {
                try { stream.Dispose(); } catch { }
            }
            _modHubControlledFileLocks.Clear();
        }
    }

    private void WithModHubControlledFilesUnlocked(Action action)
    {
        ReleaseModHubControlledFileLocks();
        try { action(); }
        finally { RelockModHubControlledFiles(); }
    }

    private static (string path, string type, string? definitionPath)? FindModConfig(string modPath)
    {
        if (!Directory.Exists(modPath)) return null;

        // YAML is optional UI metadata only. If an ordinary .yaml definition is
        // present, ModHub claims it before reading any settings. Never create a
        // synthetic configuration file from YAML.
        const string controlledSuffix = ".yaml.RRModHub.CONTROLLED";
        string? controlledYaml = Directory.EnumerateFiles(modPath, "*.yaml.RRModHub.CONTROLLED", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(controlledYaml))
        {
            var yaml = Directory.EnumerateFiles(modPath, "*.yaml", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(p => !p.EndsWith(controlledSuffix, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(yaml))
            {
                var target = yaml + ".RRModHub.CONTROLLED";
                try
                {
                    if (!File.Exists(target)) File.Move(yaml, target);
                    controlledYaml = target;
                }
                catch
                {
                    // If the rename fails, do not consume the uncontrolled YAML
                    // as metadata. The real config can still be edited without it.
                    controlledYaml = null;
                }
            }
        }

        var scripts = Path.Combine(modPath, "Scripts");
        if (Directory.Exists(scripts))
        {
            var lua = Directory.EnumerateFiles(scripts, "config.lua", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(lua))
                return (lua, "lua", controlledYaml);
        }

        // Lua is primary. Only if Scripts/config.lua is absent do we fall back
        // to the mod's root-level config.ini.
        var ini = Directory.EnumerateFiles(modPath, "config.ini", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(ini))
            return (ini, "ini", controlledYaml);

        return null;
    }


    private Dictionary<string, ModDefaultRecord> LoadModDefaults()
    {
        try
        {
            if (!File.Exists(ModDefaultsFile)) return new(StringComparer.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(File.ReadAllText(ModDefaultsFile));
            var result = new Dictionary<string, ModDefaultRecord>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var obj = property.Value;
                if (obj.ValueKind != JsonValueKind.Object) continue;
                string modPath = obj.TryGetProperty("ModPath", out var mp) ? mp.GetString() ?? "" : "";
                string configPath = obj.TryGetProperty("ConfigPath", out var cp) ? cp.GetString() ?? "" : "";
                string configType = obj.TryGetProperty("ConfigType", out var ct) ? ct.GetString() ?? "lua" : "lua";
                var defaults = ReadStringMap(obj, "Defaults");
                var custom = ReadStringMap(obj, "Custom");

                // Migrate the previous OriginalContent array format in memory.
                if (defaults.Count == 0 && obj.TryGetProperty("OriginalContent", out var original) && original.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in original.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        foreach (var kv in item.EnumerateObject())
                            defaults[kv.Name] = kv.Value.ToString();
                    }
                }

                result[property.Name] = new ModDefaultRecord(modPath, configPath, configType, defaults, custom);
            }
            return result;
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement obj, string propertyName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!obj.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object) return result;
        foreach (var kv in value.EnumerateObject()) result[kv.Name] = kv.Value.ToString();
        return result;
    }




















    private Dictionary<string, string?> LoadConfig()
    {
        try
        {
            var path = RememberedPathsFile;

            if (!File.Exists(path))
            {
                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RetroRewind", "RetroRewindModhub.json");
                if (File.Exists(fallback))
                    path = fallback;
            }

            if (!File.Exists(path))
                return new Dictionary<string, string?>();

            return JsonSerializer.Deserialize<Dictionary<string, string?>>(
                       File.ReadAllText(path))
                   ?? new Dictionary<string, string?>();
        }
        catch
        {
            return new Dictionary<string, string?>();
        }
    }
















    // Fonts are bundled with the application so the selected style is available
    // on every supported machine. The first option is the default.
    private static readonly string[] SupportedFonts =
    {
        "Gillius ADF",            // bundled, geometric 60s/70s feel — default
        "Universalis ADF Std",    // bundled, clean geometric retro
        "Gillius ADF No2",        // bundled, condensed 70s/80s feel
        "Berenis ADF Pro",        // bundled, rounded retro serif
        "Accanthis ADF Std"       // bundled, decorative retro serif
    };

    private static readonly Dictionary<string, string> BundledFontFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Gillius ADF"] = "GilliusADF-Regular.otf",
            ["Universalis ADF Std"] = "UniversalisADFStd-Regular.otf",
            ["Gillius ADF No2"] = "GilliusADFNo2-Cond.otf",
            ["Berenis ADF Pro"] = "BerenisADFPro-Regular.otf",
            ["Accanthis ADF Std"] = "AccanthisADFStd-Regular.otf"
        };

    private static readonly string[] SupportedLanguages =
    {
        "English", "Spanish"
    };


    private static readonly string FontFallback = "Gillius ADF";






















    private string? TransferSourcePath;
    private string? TransferTargetPath;
    private string? ExportSourcePath;
    private string? ImportBlueprintPath;
    private string? ImportTargetPath;
    private string? InfoSavePath;
    private string? HealthSavePath;

    private sealed class PakVersionManifest
    {
        public string ModName { get; set; } = "";
        public string Version { get; set; } = "";
        public string OriginalPakName { get; set; } = "";
        public string PakFileName { get; set; } = "";
        public string InstalledAtLocal { get; set; } = "";
        public DateTime InstalledAtUtc { get; set; }
        public string Md5 { get; set; } = "";
        public string? NexusGame { get; set; }
        public int NexusModId { get; set; }
        public int NexusFileId { get; set; }
        public int NexusCurrentFileCount { get; set; } = -1;
        public string NexusName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string Repository { get; set; } = "";
        public string NexusUrl { get; set; } = "";
        public string NexusModName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string Uploader { get; set; } = "";
        public string UploaderUrl { get; set; } = "";
        public string NewestVersion { get; set; } = "";
        public string FileTime { get; set; } = "";
        public string FileMd5 { get; set; } = "";
        public string FileSha256 { get; set; } = "";
        public long FileSize { get; set; }
        public int Category { get; set; } = -1;
        public Dictionary<string, string> Mo2MetaFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<PakConflictFile> ConflictFiles { get; set; } = new();
    }

    private sealed class ActiveDownloadState
    {
        public string Id { get; init; } = "";
        public string NexusModName { get; init; } = "";
        public string FileName { get; init; } = "";
        public string Version { get; init; } = "Unknown";
        public string Type { get; init; } = "PAK/UE4SS";
        public string DestinationPath { get; init; } = "";
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public DateTime StartedUtc { get; init; }
        public DateTime LastSampleUtc { get; set; }
        public long LastSampleBytes { get; set; }
        public DateTime LastNotificationUtc { get; set; }
        public string PremiumStatus { get; set; } = "Unknown";
        public bool IsBootstrapUe4ss { get; init; }
    }
    private sealed class ConfigField
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public string Type { get; init; } = "text";
        public string? Description { get; init; }
        public FrameworkElement Editor { get; init; } = null!;
    }
    private sealed class BundleState
    {
        public int Index { get; init; }
        public string Genre { get; init; } = "";
        public bool Enabled { get; set; }
        public CheckBox Toggle { get; init; } = null!;
    }
    private sealed class YamlTableState
    {
        public string Key { get; init; } = "";
        public bool IsLua { get; init; }
        public List<int> SourceLineIndices { get; init; } = new();
        public StackPanel? _uiContainer { get; set; }
        public List<YamlTableColumn> Columns { get; init; } = new();
        public List<YamlTableRowState> Rows { get; init; } = new();
    }

    private sealed class YamlTableColumn
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public string Type { get; init; } = "text";
        public List<string> Values { get; init; } = new();
    }

    private sealed class YamlTableRowState
    {
        public Dictionary<string, FrameworkElement> Editors { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public CheckBox? Toggle { get; set; }
    }
    private const string NexusMetadataFileName = "mod_metadata.json";

    private sealed record NexusDescriptionCache
    {
        public int SchemaVersion { get; init; } = 1;
        public string Name { get; init; } = "";
        public string Game { get; init; } = "";
        public int ModId { get; init; }
        public string NexusUrl { get; init; } = "";
        public string Version { get; init; } = "";
        public string Description { get; init; } = "";
        public DateTime CachedAtUtc { get; init; } = DateTime.UtcNow;
    }








    private sealed class ModManagerImportReport
    {
        public int PakMods { get; set; }
        public int Ue4ssMods { get; set; }
        public int Downloads { get; set; }
        public int Metadata { get; set; }
        public int Skipped { get; set; }
        public List<string> Notes { get; } = new();

        public override string ToString()
        {
            var lines = new List<string>
            {
                LStatic("Imported mods successfully."),
                "",
                LStatic("PAK mods: {0}", PakMods),
                LStatic("UE4SS mods: {0}", Ue4ssMods),
                LStatic("Downloads copied: {0}", Downloads),
                LStatic("Metadata imported: {0}", Metadata),
                LStatic("Skipped: {0}", Skipped)
            };
            lines.AddRange(Notes);
            return string.Join(Environment.NewLine, lines);
        }

        private static string LStatic(string text, params object[] args) =>
            args.Length == 0 ? text : string.Format(text, args);
    }













































    private Button ActiveActionButton =>
        _mode switch
        {
            "export" => ExportActionButton,
            "import" => ImportActionButton,
            _ => TransferActionButton
        };

























    private static readonly HashSet<string> NonGameFullscreenProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "shellexperiencehost", "searchhost", "searchapp",
        "applicationframehost", "textinputhost", "startmenuexperiencehost",
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi",
        "vlc", "wmplayer", "mpv", "potplayer", "discord", "teams",
        "wallpaper32", "wallpaper64", "wallpaper_engine", "wallpaperengine",
        "dsx", "dsxservice",
        "steam", "steamwebhelper", "epicgameslauncher", "goggalaxy",
        "battle.net", "eadesktop", "ea", "ubisoftconnect", "obs64",
        "obs32", "photos", "powerpnt", "winword", "excel", "acrobat",
        "spotify", "slack", "zoom", "code", "devenv"
    };












    private static readonly string[] StoreUpgradeNames = { "A", "B", "C", "D", "E", "F" };

    private async Task<(bool Ok, Dictionary<string, bool> Rooms, string Error)> ReadSaveRoomUnlocks(string path)
    {
        try
        {
            var result = await RunEngine($"room_unlocks {Q(path)}");
            if (result.code != 0)
                return (false, new Dictionary<string, bool>(), result.stderr);

            using var doc = JsonDocument.Parse(result.stdout);
            var root = doc.RootElement;
            if (!root.TryGetProperty("room_unlocks", out var rooms) ||
                rooms.ValueKind != JsonValueKind.Object)
                return (false, new Dictionary<string, bool>(), "Save is missing room unlock data.");

            var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in StoreUpgradeNames)
            {
                if (!rooms.TryGetProperty(name, out var value) ||
                    (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
                    return (false, dict, $"Room unlock data is missing room '{name}'.");
                dict[name] = value.GetBoolean();
            }
            return (true, dict, "");
        }
        catch (Exception ex)
        {
            return (false, new Dictionary<string, bool>(), ex.Message);
        }
    }



    private (bool Ok, string Error) CheckRoomCompatibility(
        Dictionary<string, bool> sourceRooms,
        Dictionary<string, bool> targetRooms,
        string sourceLabel,
        string targetLabel)
    {
        var missing = StoreUpgradeNames
            .Where(n => sourceRooms.TryGetValue(n, out var sourceUnlocked) && sourceUnlocked &&
                        (!targetRooms.TryGetValue(n, out var targetUnlocked) || !targetUnlocked))
            .ToList();

        if (missing.Count == 0)
            return (true, "");

        return (false,
            $"Room compatibility check failed.\n\n" +
            $"{sourceLabel}: {FormatRoomList(UnlockedRoomNames(sourceRooms))}\n" +
            $"{targetLabel}: {FormatRoomList(UnlockedRoomNames(targetRooms))}\n\n" +
            $"Missing from target: {FormatRoomList(missing)}");
    }

    private static (bool Ok, Dictionary<string, bool> Rooms, string Error) ReadBlueprintRoomUnlocks(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            if (!root.TryGetProperty("room_unlocks", out var rooms) ||
                rooms.ValueKind != JsonValueKind.Object)
                return (false, new Dictionary<string, bool>(), "Blueprint is missing room unlock data.");

            var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in StoreUpgradeNames)
            {
                if (!rooms.TryGetProperty(name, out var value) ||
                    (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
                    return (false, dict, $"Blueprint room unlock data is missing room '{name}'.");
                dict[name] = value.GetBoolean();
            }
            return (true, dict, "");
        }
        catch (Exception ex)
        {
            return (false, new Dictionary<string, bool>(), ex.Message);
        }
    }












    private Dictionary<string, VideoReplacement> LoadVideoReplacements()
    {
        try
        {
            if (!File.Exists(VideoReplacementsFile))
                return new(StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Deserialize<Dictionary<string, VideoReplacement>>(File.ReadAllText(VideoReplacementsFile))
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }
































    // User-installed tools live with the ModHub documents data, not beside the
    // application executable. This keeps upgrades/uninstalls from removing them
    // and avoids writing into protected installation directories.
    private string ToolsDirectory => Path.Combine(DefaultModhubFolder, "Tools");

    /// <summary>
    /// One-time migration for installations that previously kept video tools beside
    /// the application. Move/copy the existing Tools folder into Documents so an
    /// update does not strand the already-installed FFmpeg/yt-dlp/LibVLC files.
    /// The source is never deleted unless the destination has been successfully
    /// populated.
    /// </summary>

    private static string GetProductVersionFromExe()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) path = Assembly.GetExecutingAssembly().Location;
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = info.ProductVersion;
            if (!string.IsNullOrWhiteSpace(version))
            {
                var plus = version.IndexOf('+');
                if (plus > 0) version = version[..plus];
                return version.Trim();
            }
        }
        catch { }
        return "Unknown";
    }

    private void ApplyProductVersionToUi()
    {
        var version = GetProductVersionFromExe();
        if (ProductVersionTitleText != null) ProductVersionTitleText.Text = $"  v{version} · Blueprint 2";
        if (ProductVersionSettingsText != null) ProductVersionSettingsText.Text = version;
        if (ProductVersionFooterText != null) ProductVersionFooterText.Text = GetUe4ssFooterText();
    }

    private string GetUe4ssFooterText()
    {
        try
        {
            var state = GetUe4ssIntegrityState();
            if (state.MissingFiles || string.IsNullOrWhiteSpace(state.InstalledVersion))
                return "UE4SS Not Installed";

            var version = state.InstalledVersion.Trim();
            var plus = version.IndexOf('+');
            if (plus > 0) version = version[..plus].Trim();
            return $"UE4SS v{version}";
        }
        catch
        {
            return "UE4SS Not Installed";
        }
    }

    private void UpdateUe4ssFooter()
    {
        if (ProductVersionFooterText != null)
            ProductVersionFooterText.Text = GetUe4ssFooterText();
    }

    private void MigrateLegacyToolsFolder()
    {
        try
        {
            var legacy = Path.Combine(AppContext.BaseDirectory, "Tools");
            if (!Directory.Exists(legacy))
                return;

            if (string.Equals(
                    Path.GetFullPath(legacy).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(ToolsDirectory).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                return;

            Directory.CreateDirectory(ToolsDirectory);

            foreach (var source in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(legacy, source);
                var destination = Path.Combine(ToolsDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                // Preserve an existing working user copy in Documents.
                if (!File.Exists(destination))
                    File.Copy(source, destination, false);
            }

            // Only remove the old folder after its files have been copied.
            // Failure leaves the legacy folder intact, so playback keeps working.
            Directory.Delete(legacy, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tools migration failed; leaving legacy Tools in place: {ex}");
        }
    }
    private const string FfmpegDownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string FfmpegChecksumUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256";
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string TexconvDownloadUrl = "https://github.com/microsoft/DirectXTex/releases/download/may2026/texconv.exe";
    private const string YtDlpChecksumUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS";
    private const string RealEsrganDownloadUrl = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip";
    private const string RepakDownloadUrl = "https://github.com/trumank/repak/releases/download/v0.2.3/repak_cli-x86_64-pc-windows-msvc.zip";


























    private const string LibVlcVersion = "3.0.23.1";
    // NuGet currently exposes the package through the V2 package endpoint as well as the flat-container CDN.
    // Use the V2 endpoint first because some networks return 404 for api.nuget.org's flat-container URL.
    // NuGet's package page currently redirects its download to the official global CDN.
    // Use the CDN directly so installations do not depend on the API/flat-container endpoints.
    private const string LibVlcPackageUrl = "https://globalcdn.nuget.org/packages/videolan.libvlc.windows.3.0.23.1.nupkg";

    private string LibVlcToolsDirectory => Path.Combine(ToolsDirectory, "LibVLC", "win-x64");








































































    private Dictionary<string, NexusModMetadata> LoadNexusMetadata()
    {
        try
        {
            var path = GetModMetadataPath();
            if (!File.Exists(path)) return new Dictionary<string, NexusModMetadata>(StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Deserialize<Dictionary<string, NexusModMetadata>>(File.ReadAllText(path))
                   ?? new Dictionary<string, NexusModMetadata>(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, NexusModMetadata>(StringComparer.OrdinalIgnoreCase); }
    }


















    private static Dictionary<string, string> ParseMo2MetaFields(string metaPath)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(metaPath)) return fields;
        foreach (var raw in File.ReadLines(metaPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("[") || !line.Contains('=')) continue;
            var split = line.IndexOf('=');
            if (split <= 0) continue;
            var key = line[..split].Trim();
            if (key.Length == 0) continue;
            fields[key] = UnquoteMo2Value(line[(split + 1)..]);
        }
        return fields;
    }



















    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T match) return match;
            current = current is Visual || current is Visual3D
                ? VisualTreeHelper.GetParent(current)
                : null;
        }
        return null;
    }











    // Protected entries from the supplied original UE4SS mods.txt.
    // These entries are never removed by ModHub.
    private static readonly HashSet<string> Ue4ssDefaultModNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CheatManagerEnablerMod",
        "ConsoleCommandsMod",
        "ConsoleEnablerMod",
        "SplitScreenMod",
        "LineTraceMod",
        "BPML_GenericFunctions",
        "BPModLoaderMod",
        "Keybinds"
    };

    private static readonly string[] Ue4ssProtectedModsTxtLines =
    {
        "CheatManagerEnablerMod : 1",
        "ConsoleCommandsMod : 1",
        "ConsoleEnablerMod : 1",
        "SplitScreenMod : 0",
        "LineTraceMod : 0",
        "BPML_GenericFunctions : 1",
        "BPModLoaderMod : 1",
        "",
        "",
        "",
        "; Built-in keybinds, do not move up!",
        "Keybinds : 1"
    };












































    private const string DownloadStateFileName = "download_state.json";


























    




















































    private readonly record struct NexusHsl(double H, double S, double L);







    private enum PakInstallChoice
    {
        Automatic,
        UpdateExisting,
        AddAsNew,
        Cancel
    }











































    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }















    private sealed class NexusModInfo
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string Description { get; init; } = "";
        public int FilesCount { get; init; } = -1;
        public string Author { get; init; } = "";
    }




    private static Dictionary<string, string> ParseNexusQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2);
            if (parts.Length != 2) continue;
            result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
        }
        return result;
    }




    private async Task<(int code,string stdout,string stderr)> RunEngine(string args)
    {
        var bundled=Path.Combine(AppContext.BaseDirectory,"RetroRewindModHub_Data","Engine","engine.exe");
        var psi=new ProcessStartInfo();
        if(File.Exists(bundled))
        {
            psi.FileName=bundled;
            psi.Arguments=args;
        }
        else
        {
            if(!File.Exists(EnginePath))
                throw new Exception("Engine\\engine.py is missing from the published application.");

            // Use the Python launcher when available, with python.exe as a fallback.
            // This also works when the app was published as a single-file .exe:
            // engine.py is copied beside it under Engine\\.
            psi.FileName="py";
            psi.Arguments="-3 "+Q(EnginePath)+" "+args;

            try
            {
                using var probe = new Process();
                probe.StartInfo = new ProcessStartInfo
                {
                    FileName = "py",
                    Arguments = "-3 --version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                probe.Start();
                probe.WaitForExit(3000);
                if(probe.ExitCode != 0)
                    throw new InvalidOperationException();
            }
            catch
            {
                psi.FileName="python";
                psi.Arguments=Q(EnginePath)+" "+args;
            }
        }
        psi.WorkingDirectory=AppContext.BaseDirectory;
        psi.UseShellExecute=false; psi.RedirectStandardOutput=true; psi.RedirectStandardError=true; psi.CreateNoWindow=true; psi.StandardOutputEncoding=System.Text.Encoding.UTF8; psi.StandardErrorEncoding=System.Text.Encoding.UTF8;
        using var p=Process.Start(psi)??throw new Exception("Could not start Python. Install Python 3.10+ or package the engine as engine.exe.");
        var stdout=await p.StandardOutput.ReadToEndAsync(); var stderr=await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(); return(p.ExitCode,stdout,stderr);
    }

    // RRModHub: Shared/Scripts folder buttons










    // Rename the individual item that owns the Change Name command.
    // Grouped items must not redirect a rename to the parent group.

    // RRModHub: individual grouped-item Change Name target
    // When Change Name is invoked from a child button inside a group, use that
    // child's model/item as the rename target. Do not rename the parent group.


    // Bulk link operations must not invoke a separate UAC request for every mod.
    // This scope is used by Enable All / Disable All so the existing link creation
    // path can suppress repeated prompts while the batch is active.

    private sealed class BulkSymbolicLinkScope : IDisposable
    {
        private readonly Action _onDispose;
        public BulkSymbolicLinkScope(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose?.Invoke();
    }

}
