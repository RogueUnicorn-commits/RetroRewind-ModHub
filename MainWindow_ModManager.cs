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
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialogOriginalFont = _selectedFont;
        var dialogOriginalPalette = _selectedPalette;
        var dialogOriginalSaveFolder = _saveFolderPath;
        var dialogOriginalModsFolder = _modsFolderPath;
        var dialogOriginalNexusKey = _nexusApiKey;
        var dialogOriginalShowUe4ssDefaults = _showUe4ssDefaultMods;
        var dialogOriginalPowerSaveMode = _powerSaveMode;
        var dialogOriginalAutoStart = _autoStartWithWindowsLogin;
        var dialogOriginalWindowsNotifications = _enableWindowsNotifications;
        var dialogOriginalCloseToTaskbar = _closeToTaskbar;
        var dialogOriginalModManagerPath = _modManagerPath;
        var dialogOriginalModManagerType = _modManagerType;
        var dialogOriginalRunLibraries = _runForceLoadLibraries.ToList();
        var dialogOriginalRunExecutables = _runLaunchExecutables.ToList();
        var dialogOriginalRunArguments = _runArguments;
        var dialog = new OverlayDialogHost(this, SlidePanelMode.Bottom)
        {
            Background = (Brush)Resources["CardBrush"],
            Foreground = (Brush)Resources["ForegroundBrush"],
            FontFamily = CreateFontFamily(_selectedFont)
        };

        void RevertSettingsChanges()
        {
            _selectedPalette = dialogOriginalPalette;
            _selectedFont = dialogOriginalFont;
            _saveFolderPath = dialogOriginalSaveFolder;
            _modsFolderPath = dialogOriginalModsFolder;
            _nexusApiKey = dialogOriginalNexusKey;
            _showUe4ssDefaultMods = dialogOriginalShowUe4ssDefaults;
            UpdateUe4ssSpecialFoldersButtons();
            _powerSaveMode = dialogOriginalPowerSaveMode;
            RefreshUe4ssListImmediately();
            _runAsAdmin = false;
            _autoStartWithWindowsLogin = dialogOriginalAutoStart;
            UpdateAdminModeFooter();
            _enableWindowsNotifications = dialogOriginalWindowsNotifications;
            _closeToTaskbar = dialogOriginalCloseToTaskbar;
            _modManagerPath = dialogOriginalModManagerPath;
            _modManagerType = dialogOriginalModManagerType;
            _runForceLoadLibraries = dialogOriginalRunLibraries.ToList();
            _runLaunchExecutables = dialogOriginalRunExecutables.ToList();
            _runArguments = dialogOriginalRunArguments;
            ApplySelectedPalette();
            ApplyFontSelection();
            ApplyLanguage();
        }

        dialog.OnBackdropClose = RevertSettingsChanges;
        dialog.OnEscapeClose = RevertSettingsChanges;

        var rootGrid = new Grid { Margin = new Thickness(24) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = L("SETTINGS"),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["ForegroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18)
        };
        Grid.SetRow(title, 0);
        rootGrid.Children.Add(title);

        var tabs = new TabControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = (Brush)Resources["WindowBackgroundBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            Style = (Style)Resources["SettingsTabControlStyle"],
            ItemContainerStyle = (Style)Resources["SettingsTabItemStyle"]
        };
        Grid.SetRow(tabs, 1);
        rootGrid.Children.Add(tabs);

        // Give each tab a real layout width. The previous auto-sized tabs could be
        // measured right against the TabPanel/ScrollViewer edge, clipping the last
        // few pixels of the border on narrow settings overlays.
        var mainTab = new TabItem { Header = L("Main"), Width = 78, MinWidth = 78 };
        var modTab = new TabItem { Header = L("Mod"), Width = 78, MinWidth = 78 };
        var themeTab = new TabItem { Header = L("Theme"), Width = 86, MinWidth = 86 };
        var apiTab = new TabItem { Header = L("API"), Width = 74, MinWidth = 74 };
        var modManagerTab = new TabItem { Header = L("Vortex\\MO2"), Width = 126, MinWidth = 126 };
        var runTab = new TabItem { Header = L("Run"), Width = 74, MinWidth = 74 };
        tabs.Items.Add(mainTab);
        tabs.Items.Add(modTab);
        tabs.Items.Add(themeTab);
        tabs.Items.Add(apiTab);
        tabs.Items.Add(modManagerTab);
        tabs.Items.Add(runTab);

        var labelBrush = (Brush)Resources["ForegroundBrush"];
        var inputBrush = (Brush)Resources["InputBackgroundBrush"];
        var borderBrush = (Brush)Resources["BorderBrush"];
        var buttonBrush = (Brush)Resources["ButtonBackgroundBrush"];
        var accentBrush = (Brush)Resources["AccentBrush"];
        var accentForeground = (Brush)Resources["AccentForegroundBrush"];

        TextBlock MakeSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = L(text),
                Foreground = labelBrush,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        // MAIN TAB
        var mainPanel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };
        mainPanel.Children.Add(MakeSectionLabel("Game Save Folder"));
        var saveRow = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var saveText = new TextBox
        {
            Text = _saveFolderPath,
            Height = 34,
            IsReadOnly = true,
            Background = inputBrush,
            Foreground = labelBrush,
            BorderBrush = borderBrush,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(saveText, 0); saveRow.Children.Add(saveText);
        var saveBrowse = new Button
        {
            Content = L("Browse"), Width = 90, Height = 34,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)Resources["SettingsButtonStyle"]
        };
        saveBrowse.Click += (_, _) =>
        {
            var folder = PickFolder(this, saveText.Text);
            if (folder != null) saveText.Text = folder;
        };
        ApplySettingsButtonFeedback(saveBrowse, false);
        Grid.SetColumn(saveBrowse, 1); saveRow.Children.Add(saveBrowse);
        mainPanel.Children.Add(saveRow);

        mainPanel.Children.Add(MakeSectionLabel("Power Save Options"));
        var powerSavePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        var powerSaveAuto = new RadioButton { Content = L("Auto"), GroupName = "PowerSaveMode", Margin = new Thickness(0, 0, 18, 0), Foreground = labelBrush, IsChecked = _powerSaveMode == "auto", Style = (Style)Resources["VideoEditorRadioStyle"] };
        var powerSaveSaving = new RadioButton { Content = L("Power Saving"), GroupName = "PowerSaveMode", Margin = new Thickness(0, 0, 18, 0), Foreground = labelBrush, IsChecked = _powerSaveMode == "powersaving", Style = (Style)Resources["VideoEditorRadioStyle"] };
        var powerSavePerformance = new RadioButton { Content = L("Performance"), GroupName = "PowerSaveMode", Foreground = labelBrush, IsChecked = _powerSaveMode == "performance", Style = (Style)Resources["VideoEditorRadioStyle"] };
        powerSavePanel.Children.Add(powerSaveAuto);
        powerSavePanel.Children.Add(powerSaveSaving);
        powerSavePanel.Children.Add(powerSavePerformance);
        mainPanel.Children.Add(powerSavePanel);

        mainPanel.Children.Add(new TextBlock
        {
            Text = L("Administrator permission is requested only when ModHub needs to create a symbolic link. The main application always runs normally."),
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        });
        var autoStartWithWindowsLogin = new CheckBox
        {
            Content = L("Auto Start With Windows Login"),
            IsChecked = _autoStartWithWindowsLogin,
            Margin = new Thickness(0, 18, 0, 0),
            Style = (Style)Resources["TransferToggleStyle"],
            Foreground = labelBrush
        };
        mainPanel.Children.Add(autoStartWithWindowsLogin);
        mainPanel.Children.Add(new TextBlock
        {
            Text = L("When enabled, ModHub starts in the background and stays in the system tray when you sign in to Windows."),
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(28, 4, 0, 0)
        });

        var enableWindowsNotifications = new CheckBox
        {
            Content = L("Enable Windows Notifications"),
            IsChecked = _enableWindowsNotifications,
            Margin = new Thickness(0, 18, 0, 0),
            Style = (Style)Resources["TransferToggleStyle"],
            Foreground = labelBrush
        };
        mainPanel.Children.Add(enableWindowsNotifications);
        mainPanel.Children.Add(new TextBlock
        {
            Text = L("Show native Windows notifications when required files have available updates."),
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(28, 4, 0, 0)
        });

        var closeToTaskbar = new CheckBox
        {
            Content = L("Close to Taskbar"),
            IsChecked = _closeToTaskbar,
            Margin = new Thickness(0, 18, 0, 0),
            Style = (Style)Resources["TransferToggleStyle"],
            Foreground = labelBrush
        };
        mainPanel.Children.Add(closeToTaskbar);
        mainPanel.Children.Add(new TextBlock
        {
            Text = L("Clicking X hides ModHub to the Windows taskbar tray instead of closing the app."),
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(28, 4, 0, 0)
        });

        var requiredFilesButton = new Button
        {
            Content = L("Required Files"), Width = 150, Height = 36,
            Margin = new Thickness(0, 18, 0, 0),
            Style = (Style)Resources["SettingsButtonStyle"]
        };
        ApplySettingsButtonFeedback(requiredFilesButton, false);
        requiredFilesButton.Click += (_, _) =>
        {
            RevertSettingsChanges();
            dialog.Close();
            _mode = "requiredfiles";
            UpdateMode();
            RefreshRequiredFilesPage();
        };
        mainPanel.Children.Add(requiredFilesButton);
        mainTab.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = mainPanel };

        // MOD TAB
        var modPanel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };
        modPanel.Children.Add(MakeSectionLabel("Mods Folder"));
        var modTabRow = new Grid();
        modTabRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modTabRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modTabText = new TextBox
        {
            Text = ModsRoot, Height = 34, IsReadOnly = true, Background = inputBrush, Foreground = labelBrush,
            BorderBrush = borderBrush, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(modTabText, 0); modTabRow.Children.Add(modTabText);
        var modTabBrowse = new Button { Content = L("Browse"), Width = 90, Height = 34, Margin = new Thickness(8, 0, 0, 0), Style = (Style)Resources["SettingsButtonStyle"] };
        modTabBrowse.Click += (_, _) => { var folder = PickFolder(this, modTabText.Text); if (folder != null) modTabText.Text = folder; };
        ApplySettingsButtonFeedback(modTabBrowse, false);
        Grid.SetColumn(modTabBrowse, 1); modTabRow.Children.Add(modTabBrowse);
        modPanel.Children.Add(modTabRow);
        var modShowUe4ssDefaults = new CheckBox
        { Content = L("Show UE4SS Default Mods — Advanced Users Only / Unsafe"), IsChecked = _showUe4ssDefaultMods, Margin = new Thickness(0, 18, 0, 0),
          Style = (Style)Resources["TransferToggleStyle"], Foreground = labelBrush,
          ToolTip = L("Advanced Users Only / Unsafe: these are core UE4SS default mods. Disabling them may affect UE4SS or game functionality. They cannot be deleted from ModHub.") };
        modPanel.Children.Add(modShowUe4ssDefaults);
        modShowUe4ssDefaults.Checked += (_, _) =>
        {
            var result = MessageBox.Show(this,
                L("Warning — Advanced Users Only / Unsafe\n\nUE4SS default mods are core/system components. Showing them exposes controls that can affect UE4SS or game functionality. Default mods can be disabled, but they cannot be deleted by ModHub.\n\nDo you want to continue?"),
                L("UE4SS Default Mods — Unsafe"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                modShowUe4ssDefaults.IsChecked = false;
                return;
            }

            _showUe4ssDefaultMods = true;
            UpdateUe4ssSpecialFoldersButtons();
            RefreshUe4ssListImmediately();
        };
        modShowUe4ssDefaults.Unchecked += (_, _) =>
        {
            _showUe4ssDefaultMods = false;
            UpdateUe4ssSpecialFoldersButtons();
            RefreshUe4ssListImmediately();
        };
        modTab.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = modPanel };

        // MO2 IMPORT TAB
        var modManagerPanel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };
        modManagerPanel.Children.Add(MakeSectionLabel("Mod Organizer 2 Import"));
        modManagerPanel.Children.Add(new TextBlock
        {
            Text = L("Select an MO2 Mods folder or an individual mod folder, then choose Import."),
            Foreground = (Brush)Resources["SecondaryBrush"], TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        var modManagerPathBox = new TextBox
        { Text = _modManagerPath, Height = 34, IsReadOnly = true, Background = inputBrush, Foreground = labelBrush,
          BorderBrush = borderBrush, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center };
        modManagerPanel.Children.Add(modManagerPathBox);
        var modManagerStatusText = new TextBlock
        { Text = string.IsNullOrWhiteSpace(_modManagerPath) ? L("No folder selected.") : L("Ready to import."),
          Foreground = (Brush)Resources["SecondaryBrush"], Margin = new Thickness(0, 8, 0, 14), TextWrapping = TextWrapping.Wrap };
        modManagerPanel.Children.Add(modManagerStatusText);
        var modManagerButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var browseModManager = new Button { Content = L("Browse"), Width = 100, Height = 36, Margin = new Thickness(0, 0, 8, 0), Style = (Style)Resources["SettingsButtonStyle"] };
        var importModManager = new Button { Content = L("Import"), Width = 100, Height = 36, Style = (Style)Resources["SettingsAccentButtonStyle"], IsEnabled = !string.IsNullOrWhiteSpace(_modManagerPath) && Directory.Exists(_modManagerPath) };
        ApplySettingsButtonFeedback(browseModManager, false); ApplySettingsButtonFeedback(importModManager, true);
        void SetSelectedModFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
            _modManagerPath = Path.GetFullPath(folder); _modManagerType = "Mod Organizer 2";
            modManagerPathBox.Text = _modManagerPath; modManagerStatusText.Text = L("Ready to import: {0}", _modManagerPath); importModManager.IsEnabled = true;
        }
        browseModManager.Click += (_, _) => { var folder = PickFolder(this, modManagerPathBox.Text); if (folder != null) SetSelectedModFolder(folder); };
        async Task ImportSelectedMo2Async(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(L("The selected folder no longer exists."));
                dialog.IsEnabled = false; modManagerStatusText.Text = L("Importing mods…"); SetOperationBusy(true, L("Importing Mod Organizer 2 mods…"));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                var report = await Task.Run(() => ImportMo2SelectedFolder(folder));
                InvalidateDownloadsCache(); RefreshDownloadsPage(); RefreshModManager(); RefreshVideosPage();
                modManagerStatusText.Text = report.ToString();
                MessageBox.Show(this, report.ToString(), L("MO2 Import"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                modManagerStatusText.Text = L("Import failed: {0}", ex.Message);
                MessageBox.Show(this, ex.Message, L("MO2 Import"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { SetOperationBusy(false); dialog.IsEnabled = true; importModManager.IsEnabled = Directory.Exists(_modManagerPath); }
        }
        importModManager.Click += async (_, _) => { if (Directory.Exists(_modManagerPath)) await ImportSelectedMo2Async(_modManagerPath); };

        modManagerButtons.Children.Add(browseModManager);
        modManagerButtons.Children.Add(importModManager);
        modManagerPanel.Children.Add(modManagerButtons);
        modManagerPanel.Children.Add(new TextBlock
        {
            Text = L("The folder name becomes the ModHub mod name. Installed meta.ini data and available MO2 metadata are imported automatically."),
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        });
        modManagerTab.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = modManagerPanel };

        // RUN TAB
        var runPanel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };

        runPanel.Children.Add(MakeSectionLabel("Force Load Libraries"));
        runPanel.Children.Add(new TextBlock
        {
            Text = L("Choose the game executable and a non-vanilla DLL to force-load. The default is RetroRewind-Win64-Shipping.exe + dwmapi.dll."),
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var availableExes = new[] { "RetroRewind.exe", "RetroRewind-Win64-Shipping.exe" };

        string[] GetNonVanillaDlls()
        {
            var gameRoot = "";
            try { gameRoot = GetVerifiedGameRoot(); } catch { }

            // The Library dropdown is intentionally restricted to:
            // RetroRewind\Binaries\Win64\
            // Do not recurse into D3D12 or ue4ss.
            var win64 = string.IsNullOrWhiteSpace(gameRoot)
                ? ""
                : Path.Combine(gameRoot, "RetroRewind", "Binaries", "Win64");

            if (!Directory.Exists(win64))
                return new[] { "dwmapi.dll" };

            var excludedDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "OpenColorIO_2_3.dll",
                "OpenImageDenoise.dll",
                "tbb.dll",
                "tbb12.dll",
                "tbbmalloc.dll"
            };

            // The uploaded exclusion list also contains paths under D3D12 and
            // ue4ss; those folders are excluded wholesale, so those entries
            // never enter the dropdown.
            var dlls = Directory.EnumerateFiles(win64, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(file =>
                {
                    var name = Path.GetFileName(file);
                    return !excludedDlls.Contains(name);
                })
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // The default selection must always be available.
            if (!dlls.Contains("dwmapi.dll", StringComparer.OrdinalIgnoreCase))
                dlls.Insert(0, "dwmapi.dll");

            return dlls.ToArray();
        }

        var libraryNames = GetNonVanillaDlls();

        var runPairsPanel = new StackPanel();

        void RefreshRunPairRows()
        {
            runPairsPanel.Children.Clear();

            while (_runLaunchExecutables.Count < _runForceLoadLibraries.Count)
                _runLaunchExecutables.Add("RetroRewind-Win64-Shipping.exe");
            while (_runForceLoadLibraries.Count < _runLaunchExecutables.Count)
                _runForceLoadLibraries.Add("dwmapi.dll");

            for (var pairIndex = 0; pairIndex < _runForceLoadLibraries.Count; pairIndex++)
            {
                var index = pairIndex;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var exeBox = new ComboBox
                {
                    ItemsSource = availableExes,
                    SelectedItem = availableExes.Contains(_runLaunchExecutables[index], StringComparer.OrdinalIgnoreCase)
                        ? availableExes.First(x => x.Equals(_runLaunchExecutables[index], StringComparison.OrdinalIgnoreCase))
                        : availableExes[1],
                    Height = 36,
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = inputBrush,
                    Foreground = labelBrush,
                    BorderBrush = borderBrush,
                    Padding = new Thickness(8, 5, 8, 5),
                    ToolTip = "EXE",
                    Style = (Style)Resources["SettingsComboBoxStyle"],
                    ItemContainerStyle = (Style)Resources["SettingsComboBoxItemStyle"]
                };
                exeBox.SelectionChanged += (_, _) =>
                {
                    if (exeBox.SelectedItem is string value)
                        _runLaunchExecutables[index] = value;
                };
                Grid.SetColumn(exeBox, 0);
                row.Children.Add(exeBox);

                var libBox = new ComboBox
                {
                    ItemsSource = libraryNames,
                    SelectedItem = libraryNames.FirstOrDefault(x =>
                        x.Equals(_runForceLoadLibraries[index], StringComparison.OrdinalIgnoreCase))
                        ?? "dwmapi.dll",
                    Height = 36,
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = inputBrush,
                    Foreground = labelBrush,
                    BorderBrush = borderBrush,
                    Padding = new Thickness(8, 5, 8, 5),
                    ToolTip = "Library",
                    Style = (Style)Resources["SettingsComboBoxStyle"],
                    ItemContainerStyle = (Style)Resources["SettingsComboBoxItemStyle"]
                };
                libBox.SelectionChanged += (_, _) =>
                {
                    if (libBox.SelectedItem is string value)
                        _runForceLoadLibraries[index] = value;
                };
                Grid.SetColumn(libBox, 1);
                row.Children.Add(libBox);

                var remove = new Button
                {
                    Content = L("Remove"),
                    Width = 82,
                    Height = 36,
                    Style = (Style)Resources["SettingsButtonStyle"]
                };
                remove.Click += (_, _) =>
                {
                    if (_runForceLoadLibraries.Count > 1)
                    {
                        _runForceLoadLibraries.RemoveAt(index);
                        _runLaunchExecutables.RemoveAt(index);
                        RefreshRunPairRows();
                    }
                };
                ApplySettingsButtonFeedback(remove, false);
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);

                runPairsPanel.Children.Add(row);
            }
        }

        runPanel.Children.Add(runPairsPanel);
        RefreshRunPairRows();

        var addRunPair = new Button
        {
            Content = L("Add"),
            Width = 90,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 20),
            Style = (Style)Resources["SettingsButtonStyle"]
        };
        addRunPair.Click += (_, _) =>
        {
            _runLaunchExecutables.Add("RetroRewind-Win64-Shipping.exe");
            _runForceLoadLibraries.Add(libraryNames.Contains("dwmapi.dll") ? "dwmapi.dll" : libraryNames.FirstOrDefault() ?? "dwmapi.dll");
            RefreshRunPairRows();
        };
        ApplySettingsButtonFeedback(addRunPair, false);
        runPanel.Children.Add(addRunPair);

        runPanel.Children.Add(MakeSectionLabel("Arguments"));
        runPanel.Children.Add(new TextBlock
        {
            Text = L("Optional command-line arguments passed to the game executable."),
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var runArgumentsBox = new TextBox
        {
            Text = _runArguments,
            Height = 100,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = inputBrush,
            Foreground = labelBrush,
            BorderBrush = borderBrush,
            Padding = new Thickness(8, 7, 8, 7)
        };
        runPanel.Children.Add(runArgumentsBox);
        runArgumentsBox.TextChanged += (_, _) => _runArguments = runArgumentsBox.Text;

        runPanel.Children.Add(new TextBlock
        {
            Text = L("Changes are saved when you press Apply."),
            Foreground = (Brush)Resources["SecondaryBrush"],
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        runTab.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = runPanel };

        // THEME TAB
        var themePanel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };
        themePanel.Children.Add(MakeSectionLabel("Color palette"));
        var palettePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        var paletteButtons = new List<Button>();
        Button MakePaletteButton(string name)
        {
            var button = new Button
            {
                Content = name, Tag = name, Height = 34, Width = 155,
                Margin = new Thickness(0, 0, 8, 8), FocusVisualStyle = null,
                Style = (Style)Resources["PaletteButtonStyle"]
            };
            ApplySettingsButtonFeedback(button, false);
            paletteButtons.Add(button);
            palettePanel.Children.Add(button);
            return button;
        }
        var mod60PaletteButton = MakePaletteButton("60s Mod");
        var retroRewindPaletteButton = MakePaletteButton("Retro Rewind");
        var synthPaletteButton = MakePaletteButton("80s Synthwave");
        var arcadePaletteButton = MakePaletteButton("Arcade Neon");
        var sunsetPaletteButton = MakePaletteButton("Sunset Drive");
        var forestPaletteButton = MakePaletteButton("Forest Terminal");
        var psychedelic70PaletteButton = MakePaletteButton("70s Psychedelic");
        var arcade90PaletteButton = MakePaletteButton("90s Arcade");
        themePanel.Children.Add(palettePanel);

        themePanel.Children.Add(MakeSectionLabel("Font"));
        var fontPanel = new WrapPanel();
        var fontButtons = new List<Button>();
        void ApplyFontButtonStates()
        {
            foreach (var b in fontButtons)
            {
                bool selected = string.Equals(b.Tag?.ToString(), _selectedFont, StringComparison.OrdinalIgnoreCase);
                b.IsEnabled = !selected;
                b.Background = selected ? (Brush)Resources["AccentBrush"] : (Brush)Resources["ButtonBackgroundBrush"];
                b.Foreground = selected ? (Brush)Resources["AccentForegroundBrush"] : (Brush)Resources["ForegroundBrush"];
                b.BorderBrush = selected ? (Brush)Resources["AccentBrush"] : (Brush)Resources["BorderBrush"];
            }
        }
        foreach (var fontName in SupportedFonts)
        {
            var button = new Button
            {
                Content = fontName, Tag = fontName, Height = 34, Width = 155,
                Margin = new Thickness(0, 0, 8, 8), FocusVisualStyle = null,
                Style = (Style)Resources["PaletteButtonStyle"],
                FontFamily = CreateFontFamily(fontName),
                ToolTip = L("Bundled with RetroRewind Store Transfer")
            };
            button.Click += (_, _) =>
            {
                _selectedFont = fontName;
                ApplyFontSelection();
                dialog.FontFamily = CreateFontFamily(_selectedFont);
                ApplyFontToVisualTree(dialog, dialog.FontFamily);
                ApplyFontButtonStates();
            };
            fontButtons.Add(button); fontPanel.Children.Add(button);
        }
        ApplyFontButtonStates();
        themePanel.Children.Add(fontPanel);

        themeTab.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = themePanel };

        // NEXUS TAB
        var nexusPanel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };
        nexusPanel.Children.Add(MakeSectionLabel("Nexus API"));
        var nexusKey = new PasswordBox
        {
            Password = _nexusApiKey,
            Height = 34,
            Background = inputBrush,
            Foreground = labelBrush,
            BorderBrush = borderBrush,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        nexusPanel.Children.Add(nexusKey);
        var nexusHelp = new TextBlock
        {
            Text = L("Your Nexus Mods API key is stored securely on this PC."),
            Foreground = labelBrush,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 18)
        };
        nexusPanel.Children.Add(nexusHelp);

        var nexusConnectionButton = new Button
        {
            Content = string.IsNullOrWhiteSpace(_nexusApiKey) ? L("Connect to Nexus") : L("Disconnect from Nexus"),
            Width = 230,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            Style = (Style)Resources["SettingsButtonStyle"]
        };
        nexusConnectionButton.Click += async (_, _) =>
        {
            nexusConnectionButton.IsEnabled = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(_nexusApiKey))
                {
                    _nexusApiKey = string.Empty;
                    nexusKey.Password = string.Empty;
                    NexusSecretStore.Save(null);
                    nexusConnectionButton.Content = L("Connect to Nexus");
                    nexusHelp.Text = L("Disconnected from Nexus Mods. Your stored API key has been removed from this PC.");
                    ClearNexusHomeAccountUi(L("Not connected"));
                }
                else
                {
                    const string nexusApiKeysUrl = "https://www.nexusmods.com/settings/api-keys";
                    var result = MessageBox.Show(
                        this,
                        L("To connect Retro Rewind: ModHub to Nexus Mods:\n\n" +
                          "1. Click Yes to open your Nexus Mods API Keys page.\n" +
                          "2. Scroll to the bottom of the page.\n" +
                          "3. Find your **Personal API Key** and copy it.\n" +
                          "4. Paste the key into the Nexus API box here.\n" +
                          "5. Select Save Settings.\n\n" +
                          "Do you want to open the Nexus Mods API Keys page now?"),
                        L("Nexus API Key"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = nexusApiKeysUrl,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, L("Could not open the Nexus Mods API Keys page:\n\n{0}", ex.Message), L("Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                nexusHelp.Text = L("Nexus connection failed: {0}", ex.Message);
                MessageBox.Show(this, nexusHelp.Text, L("Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                nexusConnectionButton.IsEnabled = true;
            }
        };
        ApplySettingsButtonFeedback(nexusConnectionButton, false);
        nexusPanel.Children.Add(nexusConnectionButton);

        var associateLinks = new Button
        {
            Content = L("Associate Mod Download Links"),
            Width = 230,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Resources["SettingsButtonStyle"]
        };
        associateLinks.Click += (_, _) =>
        {
            try
            {
                App.RegisterNxmProtocol();
                MessageBox.Show(this, L("Nexus Mod download links are now associated with Retro Rewind: ModHub."),
                    L("Nexus"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L("Could not associate Nexus Mod download links:\n\n{0}", ex.Message),
                    L("Nexus"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        ApplySettingsButtonFeedback(associateLinks, false);
        nexusPanel.Children.Add(associateLinks);
        // STEAM API BOX
        var steamPanel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };
        steamPanel.Children.Add(MakeSectionLabel("Steam Web API"));
        var steamKey = new PasswordBox
        {
            Password = _steamApiKey,
            Height = 34,
            Background = inputBrush,
            Foreground = labelBrush,
            BorderBrush = borderBrush,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        steamPanel.Children.Add(steamKey);
        steamPanel.Children.Add(new TextBlock
        {
            Text = L("Enter your Steam Web API key. It is stored securely on this PC and is used only to load your Steam library and achievements."),
            Foreground = labelBrush,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 14)
        });
        var steamHelpButton = new Button
        {
            Content = L("Get a Steam Web API Key"),
            Width = 230, Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Resources["SettingsButtonStyle"]
        };
        steamHelpButton.Click += (_, _) => OpenUrl("https://steamcommunity.com/dev/apikey");
        ApplySettingsButtonFeedback(steamHelpButton, false);
        steamPanel.Children.Add(steamHelpButton);
        // API TAB
        var apiPanel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };

        Border MakeApiBox(string titleText, UIElement content)
        {
            var box = new Border
            {
                Background = (Brush)Resources["SecondaryCardBrush"],
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var inner = new StackPanel();
            inner.Children.Add(new TextBlock
            {
                Text = L(titleText),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = labelBrush,
                Margin = new Thickness(14, 10, 14, 0)
            });
            inner.Children.Add(content);
            box.Child = inner;
            return box;
        }

        apiPanel.Children.Add(MakeApiBox("Nexus", nexusPanel));
        apiPanel.Children.Add(MakeApiBox("Steam", steamPanel));
        apiTab.Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = apiPanel
        };



        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Grid.SetRow(buttons, 2);
        rootGrid.Children.Add(buttons);

        var cancel = new Button
        {
            Content = L("Cancel"), Width = 100, Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Resources["SettingsButtonStyle"]
        };
        ApplySettingsButtonFeedback(cancel, false);
        cancel.Click += (_, _) => { RevertSettingsChanges(); dialog.Close(); };
        buttons.Children.Add(cancel);

        var apply = new Button
        {
            Content = L("Save Settings"), Width = 130, Height = 36,
            Style = (Style)Resources["SettingsAccentButtonStyle"]
        };
        ApplySettingsButtonFeedback(apply, true);
        apply.Click += (_, _) =>
        {
            var selectedSave = saveText.Text.Trim();
            var selectedMods = modTabText.Text.Trim();
            var selectedNexusKey = nexusKey.Password.Trim();
            var selectedSteamKey = steamKey.Password.Trim();
            if (string.IsNullOrWhiteSpace(selectedSave) || !Directory.Exists(selectedSave))
            {
                MessageBox.Show(this, L("Please select an existing save folder."), L("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(selectedMods))
            {
                MessageBox.Show(this, L("Please select a Mods folder."), L("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                Directory.CreateDirectory(selectedMods);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L("The Mods folder could not be created:\n\n{0}", ex.Message), L("Settings"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var currentMods = ModsRoot;
            if (!string.Equals(Path.GetFullPath(currentMods).TrimEnd(Path.DirectorySeparatorChar),
                               Path.GetFullPath(selectedMods).TrimEnd(Path.DirectorySeparatorChar),
                               StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    MergeDirectoryContents(currentMods, selectedMods);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L("The Mods folder could not be moved:\n\n{0}", ex.Message), L("Settings"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            _saveFolderPath = selectedSave;
            _modsFolderPath = selectedMods;
            NexusSecretStore.Configure(ModsRoot);
            SteamSecretStore.Configure(ModsRoot);
            _nexusApiKey = selectedNexusKey;
            _steamApiKey = selectedSteamKey;
            _showUe4ssDefaultMods = modShowUe4ssDefaults.IsChecked == true;
            // The main application is never elevated. Keep the old config key false
            // for backwards compatibility; symbolic-link operations request UAC only
            // when they actually need it.
            var requestedRunAsAdmin = false;
            var requestedAutoStart = autoStartWithWindowsLogin.IsChecked == true;
            var requestedWindowsNotifications = enableWindowsNotifications.IsChecked == true;
            var requestedCloseToTaskbar = closeToTaskbar.IsChecked == true;
            NexusSecretStore.Save(_nexusApiKey);
            SteamSecretStore.Save(_steamApiKey);
            var values = LoadConfig();
            values["settings.palette"] = _selectedPalette;
            values["settings.font"] = _selectedFont;
            values["settings.saveFolder"] = _saveFolderPath;
            values["settings.modsFolder"] = _modsFolderPath;
            values.Remove("settings.modhubFolder");
            values["settings.showUe4ssDefaultMods"] = _showUe4ssDefaultMods ? "true" : "false";
            _powerSaveMode = powerSavePerformance.IsChecked == true ? "performance" : powerSaveSaving.IsChecked == true ? "powersaving" : "auto";
            values["settings.powerSaveMode"] = _powerSaveMode;
            values["settings.runAsAdmin"] = requestedRunAsAdmin ? "true" : "false";
            values["settings.autoStartWithWindowsLogin"] = requestedAutoStart ? "true" : "false";
            values["settings.enableWindowsNotifications"] = requestedWindowsNotifications ? "true" : "false";
            values["settings.closeToTaskbar"] = requestedCloseToTaskbar ? "true" : "false";
            _runForceLoadLibraries = _runForceLoadLibraries
                .Select(x => x?.Trim() ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_runForceLoadLibraries.Count == 0)
                _runForceLoadLibraries.Add("dwmapi.dll");
            var runPairCount = Math.Min(_runLaunchExecutables.Count, _runForceLoadLibraries.Count);
            values["settings.runPairs"] = string.Join(";", Enumerable.Range(0, runPairCount)
                .Select(i => $"{_runLaunchExecutables[i]}|{_runForceLoadLibraries[i]}"));
            values["settings.runForceLoadLibraries"] = string.Join("|", _runForceLoadLibraries);
            values["settings.runArguments"] = _runArguments ?? "";
            values["settings.modManagerPath"] = _modManagerPath;
            values["settings.modManagerType"] = _modManagerType;
            values.Remove("settings.backupFolder");
            SaveConfig(values);
            UpdateGameActivityState();

            _runAsAdmin = false;
            _autoStartWithWindowsLogin = requestedAutoStart;
            _enableWindowsNotifications = requestedWindowsNotifications;
            _closeToTaskbar = requestedCloseToTaskbar;
            UpdateWindowsAutoStartRegistration(_autoStartWithWindowsLogin);
            UpdateAdminModeFooter();
            ApplySelectedPalette();
            ApplyFontSelection();
            ApplyLanguage();
            RestoreRememberedPaths();
            dialog.Close();
        };
        buttons.Children.Add(apply);

        void SelectPalette(string paletteName)
        {
            _selectedPalette = paletteName;
            ApplySelectedPalette();
            ApplySettingsDialogPalette(dialog, title, Array.Empty<TextBlock>(), paletteButtons,
                saveText, modTabText, saveBrowse, modTabBrowse, cancel, apply);
            ApplyFontButtonStates();
        }
        retroRewindPaletteButton.Click += (_, _) => SelectPalette("Retro Rewind");
        synthPaletteButton.Click += (_, _) => SelectPalette("80s Synthwave");
        arcadePaletteButton.Click += (_, _) => SelectPalette("Arcade Neon");
        sunsetPaletteButton.Click += (_, _) => SelectPalette("Sunset Drive");
        forestPaletteButton.Click += (_, _) => SelectPalette("Forest Terminal");
        mod60PaletteButton.Click += (_, _) => SelectPalette("60s Mod");
        psychedelic70PaletteButton.Click += (_, _) => SelectPalette("70s Psychedelic");
        arcade90PaletteButton.Click += (_, _) => SelectPalette("90s Arcade");

        dialog.Content = rootGrid;
        ApplySettingsDialogPalette(dialog, title, Array.Empty<TextBlock>(), paletteButtons,
            saveText, modTabText, saveBrowse, modTabBrowse, cancel, apply);
        ApplyFontButtonStates();
        dialog.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Escape)
            {
                keyArgs.Handled = true;
                RevertSettingsChanges();
                dialog.Close();
            }
        };
        dialog.ShowDialog();
    }

    private sealed record ModEntry(string Name, string Path, bool Enabled, bool IsPak, bool IsUe4ssDefault = false);

    private sealed record PakConflictFile(string Path, string ContentHash);

    private sealed record PakConflictIndexEntry(string PakPath, string PakName, string DisplayName, string? NexusGame, int NexusModId, long Length, DateTime LastWriteUtc, List<PakConflictFile> Files);

    private sealed record PakConflictPair(string PakA, string DisplayA, string PakB, string DisplayB, List<PakConflictFilePair> Files);

    private sealed record PakConflictFilePair(string Path, string HashA, string HashB);

    private sealed record PakModGroup(string Name, string Game, int ModId, string VersionText, List<ModEntry> Mods);

    private sealed record PakVersionInfo(string Name, string Version, string Date, string PakPath, string JsonPath);

    private sealed record PendingModEntry(string Name, string ZipPath, bool IsPak, string? NexusGame = null, int NexusModId = 0, int NexusFileId = 0);

    private sealed record DownloadEntry(string Name, string Version, string Type, bool Installed, bool PreviouslyInstalled, string Path, DateTime DownloadedAtUtc, string? NexusGame = null, int NexusModId = 0, int NexusFileId = 0, bool Hidden = false, string? Author = null);

    private sealed record ModDefaultRecord(string ModPath, string ConfigPath, string ConfigType, Dictionary<string, string> Defaults, Dictionary<string, string> Custom);

    private sealed record NexusModMetadata(string Name, string Game, int ModId, int FileId, string ArchivePath)
    {
        // Optional Store Transfer display name. The Nexus name remains intact so
        // multiple downloads from one Nexus mod can each have a useful local name.
        public string DisplayName { get; init; } = "";
        // Display name for a multi-file PAK group. Kept separate from an
        // individual file's DisplayName so renaming one child never renames
        // the group header.
        public string GroupDisplayName { get; init; } = "";
        public string InstalledVersion { get; init; } = "";
        public string LatestVersion { get; init; } = "";
        public string Description { get; init; } = "";
        public DateTime? DownloadedAtUtc { get; init; }
        public bool? Endorsed { get; init; }
        public bool? Tracked { get; init; }
        public int FilesCount { get; init; } = -1;
        public int NexusCurrentFileCount { get; init; } = -1;
        public string Author { get; init; } = "";
        public string Repository { get; init; } = "";
        public string NexusUrl { get; init; } = "";
        public string ModName { get; init; } = "";
        public string Uploader { get; init; } = "";
        public string UploaderUrl { get; init; } = "";
        public string NewestVersion { get; init; } = "";
        public string FileTime { get; init; } = "";
        public string FileMd5 { get; init; } = "";
        public string FileSha256 { get; init; } = "";
        public long FileSize { get; init; }
        public int Category { get; init; } = -1;
        public Dictionary<string, string> Mo2MetaFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateAdminModeFooter()
    {
        if (AdminModeFooterText == null) return;

        AdminModeFooterText.Text = IsRunningAsAdministrator()
            ? "Admin: Unexpectedly elevated"
            : "Admin: Off (UAC only for symbolic links)";
    }

    private static bool IsSymbolicLink(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return false;

            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private bool TryRestartAsAdministrator()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return false;
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = HasTrayStartupArgument() ? "--tray" : "",
                WorkingDirectory = AppContext.BaseDirectory
            };
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch { return false; }
    }

    private bool TryRestartNormally()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return false;
            // Starting through the normal Explorer shell drops the elevated token
            // when the current process was launched with UAC elevation.
            var args = HasTrayStartupArgument() ? " --tray" : "";
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exePath}\"{args}")
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
            return true;
        }
        catch { return false; }
    }

    private void RememberCurrentPaths()
    {
        try
        {
            var values = LoadConfig();

            if (!string.IsNullOrWhiteSpace(TransferSourcePath)) values["transfer.source"] = TransferSourcePath;
            if (!string.IsNullOrWhiteSpace(TransferTargetPath)) values["transfer.target"] = TransferTargetPath;
            if (!string.IsNullOrWhiteSpace(ExportSourcePath)) values["export.source"] = ExportSourcePath;
            if (!string.IsNullOrWhiteSpace(ImportBlueprintPath)) values["import.blueprint"] = ImportBlueprintPath;
            if (!string.IsNullOrWhiteSpace(ImportTargetPath)) values["import.target"] = ImportTargetPath;
            if (!string.IsNullOrWhiteSpace(InfoSavePath)) values["info.save"] = InfoSavePath;

            values["settings.saveFolder"] = _saveFolderPath;
            values["settings.modsFolder"] = _modsFolderPath;
            values.Remove("settings.modhubFolder");
            values.Remove("settings.blueprintFolder");
            SaveConfig(values);
        }
        catch
        {
            // Remembered paths are optional; never block normal application use.
        }
    }

    private sealed record ModManagerCandidate(string Type, string Path);

    private ModManagerImportReport ImportMo2SelectedFolder(string selectedFolder)
    {
        // If the user selected an actual MO2 instance, resolve its configured Mods folder.
        var ini = FindModOrganizerIni(selectedFolder);
        if (ini != null)
        {
            var paths = ResolveMo2Paths(selectedFolder);
            if (!string.IsNullOrWhiteSpace(paths.ModsRoot) && Directory.Exists(paths.ModsRoot))
            {
                var report = new ModManagerImportReport();
                var gameRoot = GetVerifiedGameRoot();
                var pakRoot = GetPakVirtualRoot();
                var ueRoot = GetUe4ssModsRoot(gameRoot);
                var downloadRoot = GetDownloadsDirectory();
                Directory.CreateDirectory(pakRoot); Directory.CreateDirectory(ueRoot); Directory.CreateDirectory(downloadRoot);
                ImportMo2(paths.ModsRoot!, gameRoot, pakRoot, ueRoot, downloadRoot, report);
                return report;
            }
        }

        var dirs = Directory.EnumerateDirectories(selectedFolder, "*", SearchOption.TopDirectoryOnly).ToList();
        // A folder containing meta.ini is an individual MO2 mod folder.
        if (File.Exists(Path.Combine(selectedFolder, "meta.ini")))
            return ImportMo2SelectedFolders(new[] { selectedFolder });

        // Otherwise treat immediate child folders as individual MO2 mods.
        return ImportMo2SelectedFolders(dirs);
    }

    private ModManagerImportReport ImportMo2SelectedFolders(IEnumerable<string> selectedFolders)
    {
        var report = new ModManagerImportReport();
        var gameRoot = GetVerifiedGameRoot();
        var pakRoot = GetPakVirtualRoot();
        var ueRoot = GetUe4ssModsRoot(gameRoot);
        var downloadRoot = GetDownloadsDirectory();
        Directory.CreateDirectory(pakRoot); Directory.CreateDirectory(ueRoot); Directory.CreateDirectory(downloadRoot);

        foreach (var folder in selectedFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { ImportMo2ModDirectory(folder, gameRoot, pakRoot, report); }
            catch (Exception ex) { report.Skipped++; report.Notes.Add(L("{0}: {1}", Path.GetFileName(folder), ex.Message)); }
        }
        return report;
    }

    private ModManagerImportReport ImportFromModManager(string managerType, string sourceRoot)
    {
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException(L("The selected mod manager folder no longer exists."));

        var report = new ModManagerImportReport();
        var gameRoot = GetVerifiedGameRoot();
        var pakRoot = GetPakVirtualRoot();
        var ueRoot = GetUe4ssModsRoot(gameRoot);
        var downloadRoot = GetDownloadsDirectory();
        Directory.CreateDirectory(pakRoot);
        Directory.CreateDirectory(ueRoot);
        Directory.CreateDirectory(downloadRoot);

        if (managerType.Equals("Mod Organizer 2", StringComparison.OrdinalIgnoreCase))
            ImportMo2(sourceRoot, gameRoot, pakRoot, ueRoot, downloadRoot, report);
        else
            ImportVortex(sourceRoot, gameRoot, pakRoot, ueRoot, downloadRoot, report);

        ImportGenericModArchives(sourceRoot, downloadRoot, report);
        if (!managerType.Equals("Mod Organizer 2", StringComparison.OrdinalIgnoreCase))
            ImportLooseNexusMetadata(sourceRoot, gameRoot, report);
        return report;
    }

    private void ImportMo2(string sourceRoot, string gameRoot, string pakRoot, string ueRoot, string downloadRoot, ModManagerImportReport report)
    {
        var paths = ResolveMo2Paths(sourceRoot);
        var modsRoot = paths.ModsRoot;
        if (modsRoot != null && Directory.Exists(modsRoot))
        {
            foreach (var modDir in Directory.EnumerateDirectories(modsRoot, "*", SearchOption.TopDirectoryOnly))
                ImportMo2ModDirectory(modDir, gameRoot, pakRoot, report);
        }

        var downloads = paths.DownloadRoot;
        if (downloads != null)
        {
            CopyZipDownloads(downloads, downloadRoot, report);
            // Import the copied MO2 download metadata immediately so the Downloads
            // page and the installed-mod records have the same Nexus association.
            foreach (var zip in Directory.Exists(downloadRoot)
                ? Directory.EnumerateFiles(downloadRoot, "*", SearchOption.TopDirectoryOnly).Where(IsSupportedModArchive)
                : Array.Empty<string>())
            {
                try
                {
                    if (File.Exists(GetMo2MetaPath(zip)))
                    {
                        ImportMo2MetaForDownload(zip, save: true, computeHashes: true);
                        report.Metadata++;
                    }
                }
                catch { report.Skipped++; }
            }
        }
    }

    private void ImportMo2ModDirectory(string modDir, string gameRoot, string pakRoot, ModManagerImportReport report)
    {
        var modFolderName = Path.GetFileName(modDir);
        if (string.IsNullOrWhiteSpace(modFolderName)) return;

        var metaIni = Path.Combine(modDir, "meta.ini");
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(metaIni))
        {
            try { fields = ParseMo2MetaFields(metaIni); }
            catch { }
        }

        var game = fields.GetValueOrDefault("gameName") ?? "retrorewindvideostoresimulator";
        var modId = ParseMo2Int(fields, "modid");
        var version = fields.GetValueOrDefault("version") ?? fields.GetValueOrDefault("newestVersion") ?? "Unknown";
        var newestVersion = fields.GetValueOrDefault("newestVersion") ?? version;
        var fileId = 0;
        foreach (var pair in fields)
        {
            if (pair.Key.EndsWith("\\fileid", StringComparison.OrdinalIgnoreCase) && int.TryParse(pair.Value, out var candidate) && candidate > 0)
            {
                fileId = candidate;
                break;
            }
        }
        if (fileId <= 0) fileId = ParseMo2Int(fields, "fileid");

        var author = fields.GetValueOrDefault("author") ?? "";
        var uploader = fields.GetValueOrDefault("uploader") ?? "";
        var uploaderUrl = fields.GetValueOrDefault("uploaderUrl") ?? "";
        var repository = fields.GetValueOrDefault("repository") ?? "Nexus";
        var installationFile = fields.GetValueOrDefault("installationFile") ?? "";
        var nexusUrl = modId > 0 ? $"https://www.nexusmods.com/{game}/mods/{modId}" : "";

        var metadata = LoadNexusMetadata();
        var sourcePaks = SafeEnumerateFiles(modDir, "*.pak", 8).ToList();
        if (sourcePaks.Count == 0)
        {
            // Keep non-PAK MO2 content handled by the existing importer.
            var ueRoot = GetUe4ssModsRoot(gameRoot);
            var isUe4ss = Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories)
                .Any(p => (p.EndsWith("enabled.txt", StringComparison.OrdinalIgnoreCase) || p.EndsWith("enabled.txt.RRModHub.CONTROLLED", StringComparison.OrdinalIgnoreCase)) ||
                          p.EndsWith("config.lua", StringComparison.OrdinalIgnoreCase) ||
                          p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                          p.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
            if (isUe4ss)
            {
                var destination = GetUniqueDirectoryPath(Path.Combine(ueRoot, modFolderName));
                CopyDirectoryContents(modDir, destination);
                NormalizeEnabledMarkersForImportedMod(destination);
                report.Ue4ssMods++;
            }
            return;
        }

        // The MO2 folder name is intentionally the ModHub display name. Do not
        // replace it with the Nexus page title or the downloaded archive name.
        var familyFolder = Path.Combine(pakRoot, SanitizePakFolderName(modFolderName));
        Directory.CreateDirectory(familyFolder);

        foreach (var sourcePak in sourcePaks)
        {
            var targetName = Path.GetFileName(sourcePak);
            var target = Path.Combine(familyFolder, targetName);
            if (File.Exists(target))
            {
                report.Skipped++;
                continue;
            }

            try
            {
                File.Copy(sourcePak, target, false);
                var meta = new NexusModMetadata(modFolderName, game, modId, fileId, installationFile)
                {
                    DisplayName = modFolderName,
                    LatestVersion = newestVersion,
                    InstalledVersion = version,
                    Repository = repository,
                    NexusUrl = nexusUrl,
                    Author = author,
                    Uploader = uploader,
                    UploaderUrl = uploaderUrl,
                    Mo2MetaFields = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase)
                };
                metadata[PakMetadataKey(target)] = meta;
                WriteActivePakManifest(target, meta, modFolderName, version, targetName);
                report.PakMods++;
                report.Metadata++;
            }
            catch
            {
                report.Skipped++;
            }
        }

        SaveNexusMetadata(metadata);
    }

    private static int ParseMo2Int(Dictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private void ImportVortex(string sourceRoot, string gameRoot, string pakRoot, string ueRoot, string downloadRoot, ModManagerImportReport report)
    {
        // Vortex can use a separate staging directory. Look for likely game/mod roots first.
        var candidateDirs = new List<string>();
        foreach (var dirName in new[] { "mods", "staging", "staging_mods" })
            candidateDirs.AddRange(FindDirectories(sourceRoot, dirName, 4));

        foreach (var dir in candidateDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var modDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                ImportModDirectory(modDir, gameRoot, pakRoot, ueRoot, report, null);
        }

        foreach (var downloads in FindDirectories(sourceRoot, "downloads", 4).Distinct(StringComparer.OrdinalIgnoreCase))
            CopyZipDownloads(downloads, downloadRoot, report);

        // Vortex profiles/collections are not assumed to be directly compatible with Store Transfer.
        // We preserve JSON metadata that explicitly references Retro Rewind without importing Vortex's DB wholesale.
        foreach (var json in SafeEnumerateFiles(sourceRoot, "*.json", 5))
        {
            try
            {
                var text = File.ReadAllText(json);
                if (text.Contains("retrorewindvideostoresimulator", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Retro Rewind", StringComparison.OrdinalIgnoreCase))
                {
                    var destination = Path.Combine(downloadRoot, "Imported", "Vortex", Path.GetFileName(json));
                    CopyUniqueFile(json, destination);
                    report.Metadata++;
                }
            }
            catch { report.Skipped++; }
        }
    }

    private void ImportModDirectory(string modDir, string gameRoot, string pakRoot, string ueRoot, ModManagerImportReport report, bool? forcedEnabled)
    {
        var name = Path.GetFileName(modDir);
        if (string.IsNullOrWhiteSpace(name)) return;

        var pakFiles = SafeEnumerateFiles(modDir, "*.pak", 8).ToList();
        var isUe4ss = Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories)
            .Any(p => (p.EndsWith("enabled.txt", StringComparison.OrdinalIgnoreCase) || p.EndsWith("enabled.txt.RRModHub.CONTROLLED", StringComparison.OrdinalIgnoreCase)) ||
                      p.EndsWith("config.lua", StringComparison.OrdinalIgnoreCase) ||
                      p.EndsWith("disable.txt.RRModHub.DISABLED", StringComparison.OrdinalIgnoreCase) ||
                      p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                      p.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));

        if (pakFiles.Count > 0)
        {
            foreach (var pak in pakFiles)
            {
                var destination = Path.Combine(pakRoot, Path.GetFileName(pak));
                if (File.Exists(destination)) { report.Skipped++; continue; }
                File.Copy(pak, destination, false);
                report.PakMods++;
            }
        }
        else if (isUe4ss)
        {
            var destination = GetUniqueDirectoryPath(Path.Combine(ueRoot, name));
            CopyDirectoryContents(modDir, destination);
            var enabled = forcedEnabled ?? true;
            var marker = Path.Combine(destination, "enabled.txt.RRModHub.CONTROLLED");
            var legacyMarker = Path.Combine(destination, "enabled.txt");
            var disabled = Path.Combine(destination, "disable.txt.RRModHub.DISABLED");
            if (!enabled && (File.Exists(marker) || File.Exists(legacyMarker))) File.Move(File.Exists(marker) ? marker : legacyMarker, disabled);
            report.Ue4ssMods++;
        }
    }

    private static void NormalizeEnabledMarkersForImportedMod(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "enabled.txt", SearchOption.AllDirectories).ToList())
        {
            var target = file + ".RRModHub.CONTROLLED";
            try
            {
                if (File.Exists(target)) File.Delete(file);
                else File.Move(file, target);
            }
            catch { }
        }
    }

    private void CopyZipDownloads(string sourceDir, string downloadRoot, ModManagerImportReport report)
    {
        foreach (var zip in SafeEnumerateFiles(sourceDir, "*", 2).Where(IsSupportedModArchive))
        {
            try
            {
                var destination = GetUniqueDownloadPath(Path.Combine(downloadRoot, Path.GetFileName(zip)));
                File.Copy(zip, destination, false);
                report.Downloads++;
            }
            catch { report.Skipped++; }
        }
    }

    private void ImportGenericModArchives(string sourceRoot, string downloadRoot, ModManagerImportReport report)
    {
        // Only import archives that look like mods. Avoid copying arbitrary application archives.
        foreach (var zip in SafeEnumerateFiles(sourceRoot, "*", 5).Where(IsSupportedModArchive))
        {
            try
            {
                if (zip.StartsWith(downloadRoot, StringComparison.OrdinalIgnoreCase)) continue;
                var type = DetectZipModType(zip);
                if (type == null) continue;
                var destination = GetUniqueDownloadPath(Path.Combine(downloadRoot, Path.GetFileName(zip)));
                File.Copy(zip, destination, false);
                report.Downloads++;
            }
            catch { report.Skipped++; }
        }
    }

    private static string CleanImportedNexusName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return rawName;
        var cleaned = rawName.Trim();
        // MO2 can store the downloaded archive title rather than the Nexus page title.
        // Strip the common "mod id + version + timestamp + archive token" suffix when present.
        cleaned = Regex.Replace(cleaned, @"\s+\d{2,8}\s+\d+(?:\.\d+)+\s+\d{4}-\d{2}-\d{2}T[^\s]+(?:\s+[A-Za-z0-9_-]{6,})?$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+\d{2,8}\s+\d+(?:\.\d+)+\s+\d{4}-\d{2}-\d{2}.*$", "", RegexOptions.IgnoreCase);
        return string.IsNullOrWhiteSpace(cleaned) ? rawName.Trim() : cleaned.Trim();
    }

    private void ImportMo2NexusMetadata(string modsRoot, string pakRoot, string gameRoot, ModManagerImportReport report)
    {
        var metadata = LoadNexusMetadata();
        foreach (var metaIni in SafeEnumerateFiles(modsRoot, "meta.ini", 3))
        {
            try
            {
                var lines = File.ReadAllLines(metaIni);
                var idLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("modid=", StringComparison.OrdinalIgnoreCase));
                if (idLine == null || !int.TryParse(idLine.Split('=', 2)[1].Trim(), out var modId) || modId <= 0) continue;
                var rawName = lines.FirstOrDefault(l => l.TrimStart().StartsWith("name=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2).ElementAtOrDefault(1)?.Trim() ?? Path.GetFileName(Path.GetDirectoryName(metaIni) ?? "");
                var name = CleanImportedNexusName(rawName);
                try
                {
                    var apiInfo = FetchNexusModBasicInfoAsync("retrorewindvideostoresimulator", modId).GetAwaiter().GetResult();
                    if (apiInfo != null && !string.IsNullOrWhiteSpace(apiInfo.Name)) name = apiInfo.Name;
                }
                catch { }
                var modDir = Path.GetDirectoryName(metaIni) ?? modsRoot;
                foreach (var pak in SafeEnumerateFiles(modDir, "*.pak", 8))
                {
                    var target = Path.Combine(pakRoot, Path.GetFileName(pak));
                    if (!File.Exists(target)) continue;
                    metadata[PakMetadataKey(target)] = new NexusModMetadata(name, "retrorewindvideostoresimulator", modId, 0, "") { InstalledVersion = "" };
                    report.Metadata++;
                }
            }
            catch { report.Skipped++; }
        }
        SaveNexusMetadata(metadata);
    }

    private void ImportLooseNexusMetadata(string sourceRoot, string gameRoot, ModManagerImportReport report)
    {
        var metadata = LoadNexusMetadata();
        foreach (var metaIni in SafeEnumerateFiles(sourceRoot, "meta.ini", 8))
        {
            try
            {
                var lines = File.ReadAllLines(metaIni);
                var idLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("modid=", StringComparison.OrdinalIgnoreCase));
                var nameLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("name=", StringComparison.OrdinalIgnoreCase));
                if (idLine == null) continue;
                if (!int.TryParse(idLine.Split('=', 2)[1].Trim(), out var modId) || modId <= 0) continue;
                var rawName = nameLine?.Split('=', 2).ElementAtOrDefault(1)?.Trim() ?? Path.GetFileName(Path.GetDirectoryName(metaIni) ?? "");
                var name = CleanImportedNexusName(rawName);
                try
                {
                    var apiInfo = FetchNexusModBasicInfoAsync("retrorewindvideostoresimulator", modId).GetAwaiter().GetResult();
                    if (apiInfo != null && !string.IsNullOrWhiteSpace(apiInfo.Name)) name = apiInfo.Name;
                }
                catch { }
                var modDir = Path.GetDirectoryName(metaIni) ?? sourceRoot;
                var pak = SafeEnumerateFiles(modDir, "*.pak", 8).FirstOrDefault();
                if (pak == null) continue;
                var virtualPak = Path.Combine(GetPakVirtualRoot(), Path.GetFileName(pak));
                var key = PakMetadataKey(virtualPak);
                metadata[key] = new NexusModMetadata(name, "retrorewindvideostoresimulator", modId, 0, "") { InstalledVersion = "" };
                report.Metadata++;
            }
            catch { report.Skipped++; }
        }
        SaveNexusMetadata(metadata);
    }

    private static IEnumerable<string> FindDirectories(string root, string name, int maxDepth)
    {
        var results = new List<string>();
        void Walk(string dir, int depth)
        {
            if (depth > maxDepth) return;
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
            if (IsUe4ssSpecialFolderName(Path.GetFileName(d))) continue;
                    if (Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase)) results.Add(d);
                    if (depth < maxDepth) Walk(d, depth + 1);
                }
            }
            catch { }
        }
        Walk(root, 0);
        return results;
    }

    private static string? FindDirectory(string root, string name, int maxDepth) => FindDirectories(root, name, maxDepth).FirstOrDefault();

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, int maxDepth)
    {
        var results = new List<string>();
        void Walk(string dir, int depth)
        {
            if (depth > maxDepth) return;
            try
            {
                results.AddRange(Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly));
                if (depth < maxDepth)
                    foreach (var d in Directory.EnumerateDirectories(dir)) Walk(d, depth + 1);
            }
            catch { }
        }
        Walk(root, 0);
        return results;
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(file, target, false);
        }
    }

    private static string GetUniqueDirectoryPath(string desired)
    {
        if (!Directory.Exists(desired)) return desired;
        var parent = Path.GetDirectoryName(desired)!;
        var name = Path.GetFileName(desired);
        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(parent, $"{name} ({i})");
            if (!Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("Unable to create a unique mod directory.");
    }

    private static void CopyUniqueFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!File.Exists(destination)) { File.Copy(source, destination, false); return; }
        var dir = Path.GetDirectoryName(destination)!;
        var stem = Path.GetFileNameWithoutExtension(destination);
        var ext = Path.GetExtension(destination);
        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) { File.Copy(source, candidate, false); return; }
        }
        throw new IOException("Unable to create a unique imported metadata file.");
    }

    private static string DetectModManagerType(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Unknown";
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (FindModOrganizerIni(full) != null) return "Mod Organizer 2";
            var name = new DirectoryInfo(full).Name;
            if (name.Equals("Vortex", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.Combine(full, "state.v2")) ||
                Directory.Exists(Path.Combine(full, "games")))
                return "Vortex";
            if (name.Equals("ModOrganizer", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Mod Organizer 2", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.Combine(full, "ModOrganizer.exe")))
                return "Mod Organizer 2";
        }
        catch { }
        return "Unknown";
    }

    private static string? FindModOrganizerIni(string root)
    {
        try
        {
            var direct = Path.Combine(root, "ModOrganizer.ini");
            if (File.Exists(direct)) return direct;
            foreach (var file in SafeEnumerateFiles(root, "ModOrganizer.ini", 3))
                return file;
        }
        catch { }
        return null;
    }

    private sealed record Mo2Paths(string? ModsRoot, string? DownloadRoot, string? ProfilesRoot, string IniPath);

    private static Mo2Paths ResolveMo2Paths(string sourceRoot)
    {
        var ini = FindModOrganizerIni(sourceRoot);
        if (ini == null)
            throw new InvalidOperationException("ModOrganizer.ini could not be found in the selected MO2 location.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(ini))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            values[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }

        string? Resolve(string key)
        {
            if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim().Trim('"');
            value = value.Replace("@ByteArray(", "").TrimEnd(')');
            if (!Path.IsPathRooted(value)) value = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ini)!, value));
            return value;
        }

        var baseDir = Resolve("base_directory");
        var downloads = Resolve("download_directory");
        var profiles = baseDir == null ? null : Path.Combine(baseDir, "profiles");
        var mods = baseDir == null ? null : Path.Combine(baseDir, "mods");
        return new Mo2Paths(mods, downloads, profiles, ini);
    }

    private static List<ModManagerCandidate> FindModManagerInstallations()
    {
        var results = new List<ModManagerCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string type, string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (seen.Add(full)) results.Add(new ModManagerCandidate(type, full));
            }
            catch { }
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        foreach (var p in new[] { Path.Combine(appData, "Vortex"), Path.Combine(localAppData, "Vortex") })
            if (DetectModManagerType(p) == "Vortex") Add("Vortex", p);

        foreach (var p in new[] { Path.Combine(localAppData, "ModOrganizer"), Path.Combine(appData, "ModOrganizer"), Path.Combine(programFiles, "Mod Organizer 2"), Path.Combine(programFilesX86, "Mod Organizer 2"), Path.Combine(documents, "Mod Organizer 2") })
            if (DetectModManagerType(p) == "Mod Organizer 2") Add("Mod Organizer 2", p);

        // MO2 instances can live outside the normal install folders. Search likely modding roots and drive roots for ModOrganizer.ini.
        var roots = new List<string> { Environment.CurrentDirectory, documents, Path.Combine(documents, "My Games"), Path.Combine(documents, "Modding") };
        try { roots.AddRange(Environment.GetLogicalDrives()); } catch { }
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var ini in SafeEnumerateFiles(root, "ModOrganizer.ini", 4).Take(20))
                Add("Mod Organizer 2", Path.GetDirectoryName(ini)!);
        }
        return results;
    }

    private static string? PickFolder(Window owner, string? initialPath)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select folder",
                Multiselect = false,
                InitialDirectory = !string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath)
                    ? initialPath
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
        }
        catch
        {
            return null;
        }
    }

    private void RestoreRememberedPaths()
    {
        try
        {
            var values = LoadConfig();
            if (values.Count == 0) return;

            TransferSourcePath = null;
            TransferTargetPath = null;
            ExportSourcePath = null;
            ImportBlueprintPath = null;
            ImportTargetPath = null;
            InfoSavePath = values.GetValueOrDefault("info.save");
            HealthSavePath = null;

            // Operational pages always start clean. Only Info restores its last save.
            if (_mode == "info")
                InspectorSaveBox.Text = ExistingPath(InfoSavePath);
        }
        catch
        {
            // Ignore corrupt/missing remembered-path data.
        }
    }

    private static string ExistingPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : "";

    private void SetActiveModeButton()
    {
        if (FindName("TransferTab") is Button transfer)
            transfer.SetValue(FrameworkElement.TagProperty, "transfer");
        if (FindName("ExportTab") is Button export)
            export.SetValue(FrameworkElement.TagProperty, "export");
        if (FindName("ImportTab") is Button import)
            import.SetValue(FrameworkElement.TagProperty, "import");

        // The style trigger reads the separate IsDefault visual state.
        if (FindName("TransferTab") is Button t)
            t.SetCurrentValue(Button.IsDefaultProperty, _mode == "transfer");
        if (FindName("ExportTab") is Button ex)
            ex.SetCurrentValue(Button.IsDefaultProperty, _mode == "export");
        if (FindName("ImportTab") is Button im)
            im.SetCurrentValue(Button.IsDefaultProperty, _mode == "import");
        if (FindName("AboutButton") is Button about)
            about.SetCurrentValue(Button.IsDefaultProperty, _mode == "about");
        if (FindName("InfoTab") is Button info)
            info.SetCurrentValue(Button.IsDefaultProperty, _mode == "info");
        if (FindName("HealthCheckTab") is Button health)
            health.SetCurrentValue(Button.IsDefaultProperty, _mode == "health");
        if (FindName("StoreManagementTab") is Button manage)
            manage.SetCurrentValue(Button.IsDefaultProperty, _mode == "manage");
        if (FindName("ModManagerTab") is Button mods)
            mods.SetCurrentValue(Button.IsDefaultProperty, _mode == "mods");
        if (FindName("ConfigureModsTab") is Button configureMods)
            configureMods.SetCurrentValue(Button.IsDefaultProperty, _mode == "configuremods");
        if (FindName("ConflictCheckTab") is Button conflicts)
            conflicts.SetCurrentValue(Button.IsDefaultProperty, _mode == "conflicts");
        if (FindName("AssetWorkshopTab") is Button assets)
            assets.SetCurrentValue(Button.IsDefaultProperty, _mode == "assets");
        if (FindName("HomeButton") is Button home)
            home.SetCurrentValue(Button.IsDefaultProperty, _mode == "home");
    }

    private void SetOperationBusy(bool busy, string status = "Preparing…", double? percent = null, string? detail = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => SetOperationBusy(busy, status, percent, detail)));
            return;
        }

        _operationBusy = busy;
        OperationOverlayStatus.Text = status;
        if (OperationOverlayDetail != null)
            OperationOverlayDetail.Text = detail ?? string.Empty;

        if (OperationOverlayProgress != null)
        {
            OperationOverlayProgress.IsIndeterminate = !percent.HasValue;
            if (percent.HasValue)
                OperationOverlayProgress.Value = Math.Clamp(percent.Value, 0, 100);
        }

        // Required Files has its own per-file progress/status UI. Do not cover it with the global overlay.
        var isRequiredFilesPage = string.Equals(_mode, "requiredfiles", StringComparison.OrdinalIgnoreCase);
        OperationOverlay.Visibility = busy && !isRequiredFilesPage
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (busy)
        {
            ActiveActionButton.IsEnabled = false;
            HealthRunButton.IsEnabled = false;
            InspectorHealthCheckButton.IsEnabled = false;
        }
        else
        {
            UpdateActionButtons();
        }
    }

    private void RestoreOperationToggleStates()
    {
        var values = LoadConfig();

        TransferFurnitureBox.IsChecked = values.GetValueOrDefault("transfer.furniture") != "false";
        TransferStoreStyleBox.IsChecked = values.GetValueOrDefault("transfer.storeStyle") != "false";

        ImportFurnitureBox.IsChecked = values.GetValueOrDefault("import.furniture") != "false";
        ImportStoreStyleBox.IsChecked = values.GetValueOrDefault("import.storeStyle") != "false";

        ManagementFurnitureBox.IsChecked = values.GetValueOrDefault("management.furniture") != "false";
        ManagementStoreStyleBox.IsChecked = values.GetValueOrDefault("management.storeStyle") != "false";
    }

    private void SaveOperationToggleStates()
    {
        var values = LoadConfig();
        values["transfer.furniture"] = (TransferFurnitureBox.IsChecked == true).ToString().ToLowerInvariant();
        values["transfer.storeStyle"] = (TransferStoreStyleBox.IsChecked == true).ToString().ToLowerInvariant();
        values["import.furniture"] = (ImportFurnitureBox.IsChecked == true).ToString().ToLowerInvariant();
        values["import.storeStyle"] = (ImportStoreStyleBox.IsChecked == true).ToString().ToLowerInvariant();
        values["management.furniture"] = (ManagementFurnitureBox.IsChecked == true).ToString().ToLowerInvariant();
        values["management.storeStyle"] = (ManagementStoreStyleBox.IsChecked == true).ToString().ToLowerInvariant();
        SaveConfig(values);
    }

    private void OperationToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SaveOperationToggleStates();
        UpdateActionButtons();
    }

    private void ManagementToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SaveOperationToggleStates();
        UpdateActionButtons();
    }

    private void ResetTransferState()
    {
        _transferSourceHealthOk = false;
        _transferTargetHealthOk = false;

        SourceBox.Text = "";
        TargetBox.Text = "";
        TransferSourcePath = null;
        TransferTargetPath = null;

        SourceStoreName.Text = "No save selected";
        TargetStoreName.Text = "No save selected";
        SourceInfo.Text = "Select a .sav file.";
        TargetInfo.Text = "Select a target save.";

        SourceCount.Text = SourceMovies.Text = SourceLevel.Text =
            SourceMoney.Text = SourceGameDate.Text = SourceLastPlayed.Text = "—";
        TargetCount.Text = TargetMovies.Text = TargetLevel.Text =
            TargetMoney.Text = TargetGameDate.Text = TargetLastPlayed.Text = "—";

        SourceHealthStatus.Text = "Select a source save to run a health check.";
        TransferActionButton.Visibility = Visibility.Collapsed;
        ResetTransferButton.Visibility = Visibility.Collapsed;
        ResetTransferButton.IsEnabled = false;
        SourceSelectSaveButton.Visibility = Visibility.Visible;
        SourceSelectSaveButton.IsEnabled = true;
        UpdateActionButtons();
        RememberCurrentPaths();
    }

    private void ResetTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;
        ResetTransferState();
    }

    private async Task<bool> CheckTransferSaveHealth(
        string path, TextBlock statusBlock, string label)
    {
        statusBlock.Text = "Checking…";
        var result = await CheckSingleSaveHealth(path);

        if (result.Passed)
        {
            statusBlock.Text = $"Passed • {result.Objects} objects";
            return true;
        }

        statusBlock.Text = "Warning";
        MessageBox.Show(
            this,
            $"{label} failed the health check.\n\n" +
            (string.IsNullOrWhiteSpace(result.Log)
                ? "No additional diagnostic information was returned."
                : result.Log),
            "Save Health Check Failed",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private void ResetImportState()
    {
        _importBlueprintHealthOk = false;
        _importTargetHealthOk = false;
        ImportBlueprintPath = null;
        ImportTargetPath = null;
        SourceBox.Text = "";
        TargetBox.Text = "";

        ImportBlueprintStoreName.Text = "No blueprint selected";
        ImportBlueprintInfo.Text = "Select a .rrblueprint file.";
        ImportTargetStoreName.Text = "No save selected";
        ImportTargetInfo.Text = "Select a .sav file.";

        ImportTargetCount.Text = ImportTargetMovies.Text = ImportTargetLevel.Text =
            ImportTargetMoney.Text = ImportTargetGameDate.Text = ImportTargetLastPlayed.Text = "—";

        ImportBlueprintHealthStatus.Text = "Select a blueprint to run its health check.";
        ImportActionButton.Visibility = Visibility.Collapsed;
        ImportSelectBlueprintButton.Visibility = Visibility.Visible;
        // A cancelled target-save selection resets the entire import workflow,
        // so the blueprint selector must become usable again.
        ImportSelectBlueprintButton.IsEnabled = true;
        ImportTargetSelectSaveButton.Visibility = Visibility.Collapsed;
        ResetImportButton.Visibility = Visibility.Collapsed;
        ResetImportButton.IsEnabled = false;
        UpdateActionButtons();
        RememberCurrentPaths();
    }

    private void ResetImport_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;
        ResetImportState();
    }

    private void BlueprintNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateActionButtons();
    }

    private static string BuildBlueprintFileName(string typedName)
    {
        var name = typedName.Trim();
        if (name.EndsWith(".rrblueprint", StringComparison.OrdinalIgnoreCase))
            name = name[..^".rrblueprint".Length];
        else if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name = name[..^".json".Length];

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Enter a blueprint name.");

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The blueprint name contains characters that are not valid in a file name.");

        return name + ".rrblueprint";
    }

    private void ResetExportState()
    {
        _exportSourceHealthOk = false;
        ExportSourcePath = null;
        SourceBox.Text = "";
        SourceStoreName.Text = "No save selected";
        SourceInfo.Text = "Select a .sav file.";
        SourceCount.Text = SourceMovies.Text = SourceLevel.Text =
            SourceMoney.Text = SourceGameDate.Text = SourceLastPlayed.Text = "—";
        SourceHealthStatus.Text = "Select a save to run a health check.";
        SourceHealthStatus.Text = "Select a save to run a health check.";
        BlueprintNameBox.Text = "";
        BlueprintNameBox.Visibility = Visibility.Collapsed;
        BlueprintNameBox.IsEnabled = false;
        BlueprintExportStatus.Text = "Enter a blueprint name to enable export.";
        BlueprintExportStatus.Foreground = (Brush)Resources["SecondaryBrush"];
        BlueprintExportStatus.Visibility = Visibility.Visible;

        ExportActionButton.Visibility = Visibility.Collapsed;
        SourceSelectSaveButton.Visibility = Visibility.Visible;
        SourceSelectSaveButton.IsEnabled = true;
        ResetExportButton.Visibility = Visibility.Collapsed;
        ResetExportButton.IsEnabled = false;
        UpdateActionButtons();
        RememberCurrentPaths();
    }

    private void ResetExport_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;
        ResetExportState();
    }

    private void UpdateActionButtons()
    {
        bool sourceSaveSelected = _mode switch
        {
            "transfer" => File.Exists(TransferSourcePath ?? SourceBox.Text),
            "export" => File.Exists(ExportSourcePath ?? SourceBox.Text),
            _ => false
        };

        bool managementSaveSelected = _mode == "manage" && File.Exists(SourceBox.Text);
        bool anyManagementOptionEnabled =
            ManagementFurnitureBox.IsChecked == true ||
            ManagementStoreStyleBox.IsChecked == true;
        ResetShopButton.IsEnabled =
            _mode == "manage" &&
            managementSaveSelected &&
            anyManagementOptionEnabled &&
            !_operationBusy;
        ResetShopButton.Visibility = _mode == "manage" ? Visibility.Visible : Visibility.Collapsed;

        bool targetSaveSelected = _mode switch
        {
            "transfer" => File.Exists(TransferTargetPath ?? TargetBox.Text),
            "import" => File.Exists(ImportTargetPath ?? TargetBox.Text),
            _ => false
        };

        bool blueprintSelected = _mode == "import" &&
                                 File.Exists(ImportBlueprintPath ?? SourceBox.Text);

        bool anyTransferOptionEnabled =
            TransferFurnitureBox.IsChecked == true ||
            TransferStoreStyleBox.IsChecked == true;

        bool anyImportOptionEnabled =
            ImportFurnitureBox.IsChecked == true ||
            ImportStoreStyleBox.IsChecked == true;

        TransferActionButton.IsEnabled =
            _mode == "transfer" &&
            anyTransferOptionEnabled &&
            sourceSaveSelected &&
            targetSaveSelected &&
            _transferSourceHealthOk &&
            _transferTargetHealthOk &&
            !_operationBusy;

        ResetTransferButton.IsEnabled =
            _mode == "transfer" &&
            (sourceSaveSelected || targetSaveSelected) &&
            !_operationBusy;

        bool blueprintNameEntered = _mode == "export" &&
                                     !string.IsNullOrWhiteSpace(BlueprintNameBox.Text);

        ExportActionButton.IsEnabled =
            _mode == "export" &&
            sourceSaveSelected &&
            _exportSourceHealthOk &&
            blueprintNameEntered &&
            !_operationBusy;

        BlueprintNameBox.IsEnabled =
            _mode == "export" &&
            _exportSourceHealthOk &&
            !_operationBusy;

        ResetExportButton.IsEnabled =
            _mode == "export" &&
            sourceSaveSelected &&
            !_operationBusy;

        ImportActionButton.IsEnabled =
            _mode == "import" &&
            blueprintSelected &&
            _importBlueprintHealthOk &&
            _importTargetHealthOk &&
            targetSaveSelected &&
            anyImportOptionEnabled &&
            !_operationBusy;

        ResetImportButton.IsEnabled =
            _mode == "import" &&
            (blueprintSelected || targetSaveSelected) &&
            !_operationBusy;
    }

    private string _previousModeForCleanup = "";

    private void UpdateMode()
    {
        var previousMode = _previousModeForCleanup;
        bool transfer = _mode == "transfer";
        bool exportMode = _mode == "export";
        bool importMode = _mode == "import";
        bool infoMode = _mode == "info";
        bool healthMode = _mode == "health";
        bool manageMode = _mode == "manage";
        bool modsMode = _mode == "mods";
        bool posterBrowserMode = _mode == "poster_browser";
        bool posterSearchMode = _mode == "poster_search";
        bool posterAutoAddMode = _mode == "poster_auto_add";
        bool posterImageEditorMode = _mode == "poster_image_editor";
        bool mergeModsMode = _mode == "mergemods";
        bool configureModsMode = _mode == "configuremods";
        bool conflictsMode = _mode == "conflicts";
        bool videosMode = _mode == "videos";
        bool videoEditorMode = _mode == "videoeditor";
        bool requiredFilesMode = _mode == "requiredfiles";
        bool homeMode = _mode == "home";
        bool downloadsMode = _mode == "downloads";
        bool aboutMode = _mode == "about";
        bool assetsMode = _mode == "assets";
        bool assetTextureMode = _mode == "asset_texture";
        bool assetStaticMeshMode = _mode == "asset_staticmesh";
        bool assetSkeletalMeshMode = _mode == "asset_skeletalmesh";
        bool assetMaterialMode = _mode == "asset_material";
        bool assetAnimationMode = _mode == "asset_animation";
        bool assetAudioMode = _mode == "asset_audio";
        bool assetBlueprintMode = _mode == "asset_blueprint";
        bool assetNiagaraMode = _mode == "asset_niagara";
        bool assetParticleMode = _mode == "asset_particle";
        bool assetWidgetMode = _mode == "asset_widget";
        bool assetWorldMode = _mode == "asset_world";
        bool assetOtherMode = _mode == "asset_other";
        bool anyAssetPageMode = assetsMode || assetTextureMode || assetStaticMeshMode || assetSkeletalMeshMode ||
                                assetMaterialMode || assetAnimationMode || assetAudioMode || assetBlueprintMode ||
                                assetNiagaraMode || assetParticleMode || assetWidgetMode || assetWorldMode || assetOtherMode;

        // Leaving the Video Editor must fully unload the current preview. Do this
        // from the navigation state itself rather than relying on the previous
        // visual state of VideoEditorGrid, which can already be collapsed by
        // another navigation/update pass. Keep the selected source path, but do
        // not leave a loaded LibVLC media/player surface behind.
        if (!videoEditorMode)
        {
            StopAndReleaseVideoEditorPreview();
            _videoEditorPreviewFile = null;

            // A page change is also an explicit editor exit. Do not let the
            // previous source silently auto-load when the user returns to the
            // editor; that would recreate the playback panel and native media
            // surface they just left. The temporary downloaded source is safe to
            // discard here because it is only an editor workspace file.
            try
            {
                if (!string.IsNullOrWhiteSpace(_videoEditorTempFile) && File.Exists(_videoEditorTempFile))
                    File.Delete(_videoEditorTempFile);
            }
            catch { }
            _videoEditorTempFile = null;
            _videoEditorInputFile = null;
            _videoEditorOriginalName = null;
            _videoEditorPreviewFallbackAttempted = false;
            _videoEditorPreviewError = false;
            UpdateVideoEditorTimelineText(TimeSpan.Zero);
        }

        SourceLabel.Text = transfer ? L("SOURCE SAVE") : L("SAVE");
        SourceFurnitureLabel.Text = transfer ? L("Source Furniture") : L("Furniture");

        SaveGrid.Visibility = (infoMode || healthMode || modsMode || posterBrowserMode || posterSearchMode || posterAutoAddMode || mergeModsMode || configureModsMode || conflictsMode || videosMode || videoEditorMode || requiredFilesMode || homeMode || downloadsMode || aboutMode || anyAssetPageMode) ? Visibility.Collapsed : Visibility.Visible;
        HomeGrid.Visibility = homeMode ? Visibility.Visible : Visibility.Collapsed;
        ConfigureModsGrid.Visibility = configureModsMode ? Visibility.Visible : Visibility.Collapsed;
        ModManagerGrid.Visibility = modsMode ? Visibility.Visible : Visibility.Collapsed;
        PosterBrowserGrid.Visibility = posterBrowserMode ? Visibility.Visible : Visibility.Collapsed;
        if (PosterBrowserInvalidTogglePanel != null)
            PosterBrowserInvalidTogglePanel.Visibility = posterBrowserMode ? Visibility.Visible : Visibility.Collapsed;
        PosterSearchGrid.Visibility = posterSearchMode ? Visibility.Visible : Visibility.Collapsed;
        PosterAutoAddGrid.Visibility = posterAutoAddMode ? Visibility.Visible : Visibility.Collapsed;
        PosterImageEditorGrid.Visibility = posterImageEditorMode ? Visibility.Visible : Visibility.Collapsed;
        if (!posterBrowserMode) SetPosterBrowserListInteractivity(true);
        MergeModsGrid.Visibility = mergeModsMode ? Visibility.Visible : Visibility.Collapsed;
        ConflictCheckGrid.Visibility = conflictsMode ? Visibility.Visible : Visibility.Collapsed;
        VideosGrid.Visibility = videosMode ? Visibility.Visible : Visibility.Collapsed;
        VideoEditorGrid.Visibility = videoEditorMode ? Visibility.Visible : Visibility.Collapsed;
        RequiredFilesGrid.Visibility = requiredFilesMode ? Visibility.Visible : Visibility.Collapsed;
        DownloadsGrid.Visibility = downloadsMode ? Visibility.Visible : Visibility.Collapsed;
        AboutGrid.Visibility = aboutMode ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopGrid.Visibility = assetsMode ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopLegacyTextureGrid.Visibility = Visibility.Collapsed;
        AssetWorkshopTexturePageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Texture", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopStaticMeshPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Static Mesh", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopSkeletalMeshPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Skeletal Mesh", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopMaterialPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Material", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopAnimationPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Animation", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopAudioPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Audio", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopBlueprintPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Blueprint", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopNiagaraPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Niagara", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopParticlePageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Particle", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopWidgetPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Widget", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopWorldPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "World", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopOtherPageGrid.Visibility = string.Equals(_assetWorkshopActiveType, "Other", StringComparison.OrdinalIgnoreCase) && IsAssetWorkshopCategoryMode(_mode) ? Visibility.Visible : Visibility.Collapsed;
        InspectorGrid.Visibility = infoMode ? Visibility.Visible : Visibility.Collapsed;
        HealthGrid.Visibility = healthMode ? Visibility.Visible : Visibility.Collapsed;

        ImportCard.Visibility = importMode ? Visibility.Visible : Visibility.Collapsed;
        SourceCard.Visibility = importMode || infoMode || modsMode || mergeModsMode || configureModsMode || conflictsMode || videosMode || videoEditorMode || requiredFilesMode || homeMode || downloadsMode || aboutMode || assetsMode ? Visibility.Collapsed : Visibility.Visible;
        TargetCard.Visibility = transfer ? Visibility.Visible : Visibility.Collapsed;

        SaveGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        SaveGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        TransferArrow.Visibility = transfer ? Visibility.Visible : Visibility.Collapsed;

        if (exportMode || manageMode)
        {
            Grid.SetColumn(SourceCard, 0);
            Grid.SetColumnSpan(SourceCard, 3);
        }
        else
        {
            Grid.SetColumn(SourceCard, 0);
            Grid.SetColumnSpan(SourceCard, 1);
            Grid.SetColumn(TargetCard, 2);
            Grid.SetColumnSpan(TargetCard, 1);
        }

        OptionsPanel.Visibility = transfer ? Visibility.Visible : Visibility.Collapsed;
        ImportOptionsPanel.Visibility = importMode ? Visibility.Visible : Visibility.Collapsed;
        StoreManagementOptionsPanel.Visibility = manageMode ? Visibility.Visible : Visibility.Collapsed;
        ResetShopButton.Visibility = manageMode ? Visibility.Visible : Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Collapsed;

        TransferActionButton.Visibility =
            transfer && _transferSourceHealthOk && _transferTargetHealthOk
                ? Visibility.Visible
                : Visibility.Collapsed;
        ResetTransferButton.Visibility =
            transfer && _transferSourceHealthOk && _transferTargetHealthOk
                ? Visibility.Visible
                : Visibility.Collapsed;
        BlueprintNameBox.Visibility =
            exportMode && _exportSourceHealthOk
                ? Visibility.Visible
                : Visibility.Collapsed;
        BlueprintExportStatus.Visibility = exportMode ? Visibility.Visible : Visibility.Collapsed;
        ExportActionButton.Visibility =
            exportMode && _exportSourceHealthOk
                ? Visibility.Visible
                : Visibility.Collapsed;
        SourceSelectSaveButton.Visibility =
            exportMode && !_exportSourceHealthOk
                ? Visibility.Visible
                : Visibility.Collapsed;
        ResetExportButton.Visibility =
            exportMode && _exportSourceHealthOk
                ? Visibility.Visible
                : Visibility.Collapsed;
        ImportActionButton.Visibility =
            importMode && _importTargetHealthOk
                ? Visibility.Visible
                : Visibility.Collapsed;
        ImportSelectBlueprintButton.Visibility =
            importMode && !_importBlueprintHealthOk
                ? Visibility.Visible
                : Visibility.Collapsed;
        ImportTargetSelectSaveButton.Visibility =
            importMode && _importBlueprintHealthOk && !_importTargetHealthOk
                ? Visibility.Visible
                : Visibility.Collapsed;
        ImportBlueprintHealthCard.Visibility = importMode ? Visibility.Visible : Visibility.Collapsed;

        if (!importMode)
        {
            _importBlueprintHealthOk = false;
            ImportBlueprintHealthStatus.Text = "Select a blueprint to run its health check.";
        }

        UpdateActionButtons();
        if (previousMode == "conflicts" && !conflictsMode)
            ClearConflictCheckResults();
        if (modsMode || configureModsMode) RefreshModManager();
        if (configureModsMode) RefreshModConfigurationPanels();
        if (videosMode) RefreshVideosPage();
        if (videoEditorMode) RefreshVideoEditorUi();
        if (requiredFilesMode) RefreshRequiredFilesPage();
        if (homeMode)
        {
            LoadSteamHomeProfile();
            _ = RefreshNexusHomeAccountAsync();
            _ = RefreshHomeNewsAsync();
        }
        if (downloadsMode) RefreshDownloadsPage();
        if (assetsMode) RefreshAssetWorkshopPage();

        WindowTitle.Text = _mode switch
        {
            "export" => "Store Blueprint",
            "import" => "Store Import",
            "info" => "Save Information",
            "health" => "Health Check",
            "manage" => "Store Demolition",
            "mods" => "Mod Manager",
            "poster_browser" => _posterBrowserMod?.Name ?? "Poster Browser",
            "poster_search" => _posterBrowserMod?.Name ?? "Poster Search",
            "poster_auto_add" => _posterAutoAddMod?.Name ?? "Auto Add Posters",
            "poster_image_editor" => _posterBrowserMod?.Name ?? "Image Editor",
            "mergemods" => "Merge Mods",
            "configuremods" => "Mod Configurator",
            "conflicts" => "Conflict Checker",
            "videos" => "Videos",
            "videoeditor" => "Video Editor",
            "requiredfiles" => "Required Files",
            "home" => "Retro Rewind Modhub",
            "downloads" => "Downloads",
            "about" => "About Retro Rewind ModHub",
            "assets" => "Asset Workshop",
            "asset_texture" => "Asset Workshop",
            "asset_staticmesh" => "Asset Workshop",
            "asset_skeletalmesh" => "Asset Workshop",
            "asset_material" => "Asset Workshop",
            "asset_animation" => "Asset Workshop",
            "asset_audio" => "Asset Workshop",
            "asset_blueprint" => "Asset Workshop",
            "asset_niagara" => "Asset Workshop",
            "asset_particle" => "Asset Workshop",
            "asset_widget" => "Asset Workshop",
            "asset_world" => "Asset Workshop",
            "asset_other" => "Asset Workshop",
            _ => "Store Transfer"
        };

        PageDescription.Text = _mode switch
        {
            "transfer" => "Save Your Design, Switch Your World.",
            "export" => "Build Now And Reuse Later.",
            "import" => "Apply A Layout Template To Your Store.",
            "info" => "View Your Journey So Far.",
            "health" => "Inspect Save Data Integrity",
            "manage" => "Strip It Down And Start Fresh",
            "mods" => "Your Game, Your Rules.",
            "mergemods" => "Combine PAK assets into one installed mod.",
            "configuremods" => "Personalize Your Installed Mods",
            "conflicts" => "Find overlapping PAK assets before they cause problems.",
            "videos" => "Be kind Rewind",
            "videoeditor" => "Make it look like tape.",
            "requiredfiles" => "Install the components ModHub needs.",
            "home" => "Your mods. Your library. Your way.",
            "downloads" => "Manage Your Mods Downloads",
            "about" => "About the application, tools and supporting libraries.",
            "assets" => "Browse and extract Unreal PAK assets",
            "asset_texture" => "Browse and replace Unreal textures",
            "asset_staticmesh" => "Browse Unreal static meshes",
            "asset_skeletalmesh" => "Browse Unreal skeletal meshes",
            "asset_material" => "Browse Unreal materials",
            "asset_animation" => "Browse Unreal animations",
            "asset_audio" => "Browse Unreal audio",
            "asset_blueprint" => "Browse Unreal blueprints",
            "asset_niagara" => "Browse Unreal Niagara assets",
            "asset_particle" => "Browse Unreal particle assets",
            "asset_widget" => "Browse Unreal widgets",
            "asset_world" => "Browse Unreal worlds",
            "asset_other" => "Browse other Unreal assets",
            _ => "Save Your Design, Switch Your World."
        };

        bool isHomePage = _mode == "home";
        HomePageHeader.Visibility = isHomePage ? Visibility.Visible : Visibility.Collapsed;
        PageContextTitleBarText.Text = isHomePage
            ? string.Empty
            : WindowTitle.Text;
        PageContextTitleBarText.Visibility = isHomePage ? Visibility.Collapsed : Visibility.Visible;

        StatusText.Text = _mode switch
        {
            "export" => "Save → Blueprint",
            "import" => "Blueprint + save → new save",
            "info" => "Save inspection",
            "health" => "Save health check",
            "manage" => "Save management",
            "mods" => "Mod management",
            "configuremods" => "Mod configuration",
            "conflicts" => "PAK conflict analysis",
            "videos" => "Video replacement management",
            "videoeditor" => "MP4 conversion and CRT/VHS processing",
            "requiredfiles" => _requiredFilesInstallError ? "A required file could not be installed. Fix the error above and try again." : "Required files",
            "home" => "Steam news feed",
            "downloads" => "",
            "about" => "Application information and external tools",
            _ => string.Empty
        };

        if (DownloadsHiddenToggle != null)
        {
            DownloadsHiddenToggle.Visibility = downloadsMode ? Visibility.Visible : Visibility.Collapsed;
            if (downloadsMode) DownloadsHiddenToggle.IsChecked = _showHiddenDownloads;
        }

        SourceBox.Visibility = Visibility.Collapsed;
        SourceStoreName.Visibility = (transfer || exportMode || manageMode) ? Visibility.Visible : Visibility.Collapsed;
        SourceSelectSaveButton.Visibility = transfer || (exportMode && !_exportSourceHealthOk) || manageMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        SourceSelectSaveButton.IsEnabled = transfer
            ? !_transferSourceHealthOk
            : !exportMode || !_exportSourceHealthOk;
        SourceBrowseButton.Visibility = Visibility.Collapsed;
        SourceInfo.Visibility = (transfer || exportMode || manageMode) ? Visibility.Visible : Visibility.Collapsed;
        SourceDetails.Visibility = (transfer || manageMode) ? Visibility.Visible : Visibility.Collapsed;

        TargetBox.Visibility = Visibility.Collapsed;
        TargetStoreName.Visibility = transfer ? Visibility.Visible : Visibility.Collapsed;
        // Transfer uses one visible save-selection button: the source button.
        // The target selector is opened automatically after source validation.
        TargetSelectSaveButton.Visibility = Visibility.Collapsed;
        TargetBrowseButton.Visibility = Visibility.Collapsed;

        SourceHealthCard.Visibility = (transfer || exportMode) ? Visibility.Visible : Visibility.Collapsed;

        if (!transfer)
        {
            _transferSourceHealthOk = false;
            _transferTargetHealthOk = false;
            SourceHealthStatus.Text = "Select a save to run a health check.";
        }

        InspectorSaveBox.Visibility = Visibility.Collapsed;
        InspectorStoreName.Visibility = infoMode ? Visibility.Visible : Visibility.Collapsed;
        InspectorSelectSaveButton.Visibility = infoMode ? Visibility.Visible : Visibility.Collapsed;
        InspectorBrowseButton.Visibility = Visibility.Collapsed;
        InspectorHealthCheckButton.IsEnabled =
            infoMode &&
            !string.IsNullOrWhiteSpace(InfoSavePath) &&
            File.Exists(InfoSavePath);

        if (healthMode)
        {
            HealthResultsPanel.Children.Clear();
            HealthRunButton.IsEnabled = true;
        }

        if (exportMode)
        {
            SourceHealthStatus.Text = "Select a save to run a health check.";
        }
        else
        {
            _exportSourceHealthOk = false;
        }

        if (transfer)
        {
            SourceHealthStatus.Text = "Select a save to run a health check.";
        }

        if (importMode)
        {
            ResetImportState();
        }

        SetActiveModeButton();
        _previousModeForCleanup = _mode;
    }

    private sealed record HealthSaveResult(
        string FileName,
        string SaveName,
        string Objects,
        bool Passed,
        string Status,
        string Log);

    private async void InspectorHealthCheck_Click(object sender, RoutedEventArgs e)
    {
        var path = InfoSavePath;
        if (string.IsNullOrWhiteSpace(path))
            path = InspectorSaveBox.Text;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            InspectorHealthCheckButton.IsEnabled = false;
            InspectorHealthStatus.Text = "No active save selected.";
            return;
        }

        InspectorHealthCheckButton.IsEnabled = false;
        InspectorHealthStatus.Text = "Checking save…";

        try
        {
            var result = await CheckSingleSaveHealth(path);

            if (result.Passed)
            {
                InspectorHealthStatus.Text = $"Passed • {result.Objects} objects";
                MessageBox.Show(this,
                    "The selected save passed the health check.",
                    "Save Health Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                InspectorHealthStatus.Text = "Warning";
                MessageBox.Show(this,
                    string.IsNullOrWhiteSpace(result.Log)
                        ? "The selected save produced a health warning."
                        : result.Log,
                    "Save Health Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            InspectorHealthStatus.Text = "Warning";
            MessageBox.Show(this, ex.ToString(), "Save Health Check",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            InspectorHealthCheckButton.IsEnabled =
                !string.IsNullOrWhiteSpace(InfoSavePath) &&
                File.Exists(InfoSavePath);
        }
    }

    private async void HealthRun_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;

        HealthRunButton.IsEnabled = false;
        HealthResultsPanel.Children.Clear();
        SetOperationBusy(true, "Scanning save files…");

        try
        {
            if (!Directory.Exists(SaveFolderPath))
            {
                AddHealthMessage("No save folder was found.");
                return;
            }

            var files = Directory
                .EnumerateFiles(SaveFolderPath, "*.sav", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                AddHealthMessage("No .sav files were found in the Save folder.");
                return;
            }

            var results = new List<HealthSaveResult>();
            foreach (var path in files)
            {
                SetOperationBusy(true, $"Checking {Path.GetFileName(path)}…");
                results.Add(await CheckSingleSaveHealth(path));
            }

            SetOperationBusy(true, "Finalizing health results…");

            foreach (var result in results
                .OrderByDescending(r => !r.Passed)
                .ThenBy(r => r.FileName, StringComparer.OrdinalIgnoreCase))
            {
                AddHealthResultRow(result);
            }
        }
        catch (Exception ex)
        {
            AddHealthMessage($"Health check failed: {ex.Message}");
        }
        finally
        {
            HealthRunButton.IsEnabled = true;
            SetOperationBusy(false);
        }
    }

    private async Task<HealthSaveResult> CheckSingleSaveHealth(string path)
    {
        var fileName = Path.GetFileName(path);
        string saveName = "Unknown";
        string objects = "Unknown";
        var failures = new List<string>();
        var logParts = new List<string>();
        string metadataError = "";

        try
        {
            var metadata = await RunEngine($"metadata {Q(path)}");
            logParts.Add($"[metadata]\nstdout:\n{metadata.stdout}\nstderr:\n{metadata.stderr}");

            if (metadata.code == 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(metadata.stdout);
                    var root = doc.RootElement;

                    string Get(params string[] names)
                    {
                        foreach (var name in names)
                        {
                            if (root.TryGetProperty(name, out var value) &&
                                value.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(value.GetString()))
                                return value.GetString()!;
                        }
                        return "";
                    }

                    var name = Get("shop_name", "shopName", "store_name", "storeName", "name");
                    if (!string.IsNullOrWhiteSpace(name))
                        saveName = name;
                    else
                        failures.Add("Save name could not be read.");
                }
                catch (Exception ex)
                {
                    metadataError = $"Save name could not be read: {ex.Message}";
                    failures.Add(metadataError);
                }
            }
            else
            {
                metadataError = CleanEngineError(
                    !string.IsNullOrWhiteSpace(metadata.stderr)
                        ? metadata.stderr : metadata.stdout);
                failures.Add(metadataError);
            }
        }
        catch (Exception ex)
        {
            metadataError = $"Save name could not be read: {ex.Message}";
            logParts.Add($"[metadata exception]\n{ex}");
            failures.Add(metadataError);
        }

        try
        {
            var sanity = await CheckSaveSanity(path, "Save");

            if (sanity.Ok)
            {
                objects = sanity.ObjectCount.ToString();
            }
            else
            {
                var sanityError = sanity.Error
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();

                // If metadata and object validation report the same underlying
                // engine error, don't duplicate it in the tooltip.
                if (string.IsNullOrWhiteSpace(metadataError) ||
                    !sanityError.Contains(metadataError, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(sanityError);
                    logParts.Add("[object-data check]\n" + sanity.Error.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            var error = $"Object check failed: {ex.Message}";
            failures.Add(error);
            logParts.Add($"[object check exception]\n{ex}");
        }

        // Keep each failure only once.
        failures = failures
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var passed = failures.Count == 0;
        var status = passed ? "Passed" : "Warning";

        // The tooltip is the complete diagnostic log. Do not append a second
        // synthesized "health result" section; the visible status already says Warning.
        var fullLog = string.Join("\n\n",
            logParts.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (!string.IsNullOrWhiteSpace(metadataError) &&
            !fullLog.Contains(metadataError, StringComparison.OrdinalIgnoreCase))
        {
            fullLog += (fullLog.Length > 0 ? "\n\n" : "") +
                       "[health result]\n" + metadataError;
        }

        if (string.IsNullOrWhiteSpace(fullLog) && !passed)
            fullLog = string.Join("\n", failures);

        return new HealthSaveResult(fileName, saveName, objects, passed, status, fullLog);
    }

    private void AddHealthResultRow(HealthSaveResult result)
    {
        var row = new Border
        {
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(0),
            Background = (Brush)Resources["SecondaryCardBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };

        var grid = new Grid
        {
            MinHeight = 40
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });

        var passedCheck = new CheckBox
        {
            IsChecked = result.Passed,
            IsEnabled = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false
        };
        Grid.SetColumn(passedCheck, 0);
        grid.Children.Add(passedCheck);

        AddHealthCell(grid, 1, result.FileName);
        AddHealthCell(grid, 2, result.SaveName);
        AddHealthCell(grid, 3, result.Objects, true);
        AddHealthCell(grid, 4, result.Status, false, result.Log);

        row.Child = grid;
        HealthResultsPanel.Children.Add(row);
    }

    private void AddHealthCell(
        Grid grid,
        int column,
        string text,
        bool centered = false,
        string? tooltip = null)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = (Brush)Resources["ForegroundBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 6, 8, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            ToolTip = tooltip ?? text
        };

        if (centered)
            cell.TextAlignment = TextAlignment.Center;

        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void AddHealthMessage(string message)
    {
        var text = new TextBlock
        {
            Text = message,
            FontSize = 15,
            Foreground = (Brush)Resources["SecondaryBrush"],
            Margin = new Thickness(8, 12, 8, 12),
            TextWrapping = TextWrapping.Wrap
        };
        HealthResultsPanel.Children.Add(text);
    }

    private async void InspectorBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Retro Rewind saves (*.sav)|*.sav|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true)
        {
            InspectorSaveBox.Text = dlg.FileName;
            InfoSavePath = dlg.FileName;
            RememberCurrentPaths();
            await LoadInspector(dlg.FileName);
        }
    }

    private void StartInfoSaveWatcher()
    {
        _infoSaveWatcher?.Stop();
        _infoSaveWatcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _infoSaveWatcher.Tick += async (_, _) => await CheckInfoSaveForChanges();
        _infoSaveWatcher.Start();
    }

    private async Task CheckInfoSaveForChanges()
    {
        if (_mode != "info" || _infoRefreshBusy || string.IsNullOrWhiteSpace(InfoSavePath))
            return;

        var path = InfoSavePath;
        if (!File.Exists(path))
            return;

        try
        {
            var info = new FileInfo(path);
            var writeUtc = info.LastWriteTimeUtc;
            var length = info.Length;

            if (_infoLastWriteUtc == DateTime.MinValue)
            {
                _infoLastWriteUtc = writeUtc;
                _infoLastLength = length;
                return;
            }

            if (writeUtc == _infoLastWriteUtc && length == _infoLastLength)
                return;

            // Give the game a moment to finish writing/replacing the save.
            await Task.Delay(250);
            if (!File.Exists(path))
                return;

            var stable = new FileInfo(path);
            if (stable.LastWriteTimeUtc != writeUtc || stable.Length != length)
            {
                writeUtc = stable.LastWriteTimeUtc;
                length = stable.Length;
            }

            _infoRefreshBusy = true;
            await LoadInspector(path);
            _infoLastWriteUtc = writeUtc;
            _infoLastLength = length;
        }
        catch
        {
            // Ignore transient file-write/replace errors; the next timer tick retries.
        }
        finally
        {
            _infoRefreshBusy = false;
        }
    }

    private async Task LoadInspector(string path)
    {
        if (!File.Exists(path))
        {
            InspectorFileInfo.Text = "Select a .sav file.";
            InspectorStoreUpgrades.Text = "—";
            InspectorUnlockedRooms.Text = "—";
            InspectorHealthCheckButton.IsEnabled = false;
            InspectorHealthStatus.Text = "No active save selected.";
            return;
        }

        InspectorFileInfo.Text = "Reading save…";
        InspectorHealthCheckButton.IsEnabled = false;
        InspectorHealthStatus.Text = "Reading save…";
        _allObjects.Clear();
        try
        {
            var result = await RunEngine($"inspect {Q(path)}");
            if (result.code != 0)
            {
                InspectorFileInfo.Text = "Unable to read save.";
                InspectorStoreUpgrades.Text = "Unknown";
                InspectorUnlockedRooms.Text = "Unknown";
                InspectorHealthCheckButton.IsEnabled = false;
                InspectorHealthStatus.Text = "Save could not be loaded.";
                return;
            }

            using var doc = JsonDocument.Parse(result.stdout);
            var root = doc.RootElement;
            var meta = root.GetProperty("metadata");

            string Get(string name)
            {
                if (!meta.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
                    return "—";
                return v.ValueKind == JsonValueKind.Number ? v.ToString() : (v.GetString() ?? "—");
            }

            InspectorFileInfo.Text = $"{Get("file_name")} • {FormatBytes(meta.GetProperty("file_size").GetInt64())}";
            InspectorObjectCount.Text = $"{Get("object_count")} objects";
            InspectorMovies.Text = Get("movie_count");
            InspectorStoreValue.Text = GetCurrencySymbol() + Get("store_value");
            InspectorExperience.Text = Get("experience");
            InspectorLifetimeExperience.Text = Get("lifetime_experience");
            InspectorLevel.Text = Get("level");
            var money = Get("money64");
            InspectorMoney.Text = money == "—" ? "—" : GetCurrencySymbol() + money;
            InspectorGameDate.Text = Get("game_date");
            InspectorGameDay.Text = Get("game_day");
            InspectorDaysPassed.Text = Get("game_day");
            InspectorLastPlayed.Text = Get("last_played");
            InfoSavePath = path;
            InspectorHealthCheckButton.IsEnabled = true;
            InspectorHealthStatus.Text = "Ready to check this save.";

            InspectorGlobalFootsteps.Text = Get("global_footsteps");
            InspectorGlobalClientsServed.Text = Get("global_clients_served");
            InspectorDailyMovieReturns.Text = Get("daily_movie_returns");
            InspectorDailyXP.Text = Get("daily_xp");
            InspectorDailyStaffSpending.Text = GetCurrencySymbol() + Get("daily_staff_spending");

            if (meta.TryGetProperty("store_upgrades", out var upgrades) &&
                upgrades.ValueKind == JsonValueKind.Array)
            {
                var names = upgrades.EnumerateArray()
                    .Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();
                InspectorStoreUpgrades.Text = names.Count == 0 ? "None" : string.Join(", ", names);
            }
            else
            {
                InspectorStoreUpgrades.Text = "Unknown";
            }

            if (meta.TryGetProperty("room_unlocks", out var rooms) &&
                rooms.ValueKind == JsonValueKind.Object)
            {
                var names = rooms.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.True)
                    .Select(p => p.Name)
                    .ToList();
                InspectorUnlockedRooms.Text = names.Count == 0 ? "None" : string.Join(", ", names);
            }
            else
            {
                InspectorUnlockedRooms.Text = "Unknown";
            }

            _allObjects.Clear();

            var groupedObjects = root.GetProperty("objects")
                .EnumerateArray()
                .Select(o =>
                {
                    var className = o.TryGetProperty("class", out var c) ? c.GetString() ?? "—" : "—";
                    var engineExcluded = o.TryGetProperty("excluded", out var ex) &&
                                         ex.ValueKind == JsonValueKind.True;
                    return new
                    {
                        ClassName = className,
                        DisplayName = GetObjectDisplayName(className),
                        Group = GetObjectGroup(className, engineExcluded),
                        Excluded = engineExcluded
                    };
                })
                .GroupBy(o => (o.Group, o.DisplayName))
                .OrderBy(g => g.Key.Group)
                .ThenBy(g => g.Key.DisplayName, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedObjects)
            {
                _allObjects.Add(new ObjectRow(
                    group.Key.DisplayName,
                    group.Count(),
                    IsIncluded: group.All(o => !o.Excluded),
                    Group: group.Key.Group));
            }

            SetObjectGroup(_selectedObjectGroup);

            try
            {
                var currentFile = new FileInfo(path);
                _infoLastWriteUtc = currentFile.LastWriteTimeUtc;
                _infoLastLength = currentFile.Length;
            }
            catch { }

            InspectorFileInfo.Text = $"{Get("file_name")} • {FormatBytes(meta.GetProperty("file_size").GetInt64())}";
        }
        catch (Exception ex)
        {
            InspectorFileInfo.Text = "Unable to read save: " + ex.Message;
        }
    }

    private static readonly (string DisplayName, string[] Classes)[] ObjectDisplayGroups =
    {
        // Decorations
        ("Frame", new[] { "PosterFrame_C" }),
        ("Small Frame", new[] { "PosterFrame_Small_C" }),
        ("Frame Stand", new[] { "PosterFrame_Standing_C" }),
        ("Customizable Sign", new[] { "TextSign_C" }),
        ("Door Mat", new[] { "Deco_DoorMat_BASE_C" }),
        ("UFO", new[] { "Deco_SCIFI_UFO_C" }),
        ("Jack O'Lantern", new[] { "Deco_Halloween_Jack_C" }),
        ("Coffin", new[] { "Deco_Horror_Coffin_A_C" }),
        ("Display Case - 4 Rows", new[] { "Shelf_Movie-Display_4Row_01_C" }),
        ("Display Case - 5 Rows", new[] { "Shelf_Movie-Display_5Row_01_C" }),
        ("Display Case - 6 Rows", new[] { "Shelf_Movie-Display_6Row_01_C" }),
        ("Display Case - Wall Mounted", new[] { "Shelf_Movie-Display_WallMounted_01_C" }),
        ("Neon Sign", new[] { "SignNeon_Big_C" }),
        ("Small Neon Sign", new[] { "SignNeon_Small_C" }),
        ("Balloon", new[] { "Deco_Balloon_Color_C", "Deco_Balloon_Heart_C", "Deco_Balloon_Smiley_C" }),
        ("Ceiling Sign", new[] { "CeillingSign_C" }),
        ("Chair", new[] { "Couch_C" }),
        ("Gifts", new[] { "Deco_xMas_Gifts_C" }),
        ("Nutcracker", new[] { "Deco_xMas_NutCracker_C" }),
        ("Display Cabinet", new[] { "Shelf_Movie-Shelf_MovieDisplay_Cabinet_01_C" }),
        ("Display Unit", new[] { "Shelf_Movie-Shelf_MovieDisplay_Unit_01_C" }),
        ("Armor", new[] { "Deco_Medieval_Armor_A_01_C" }),
        ("Banner", new[] { "Deco_Medieval_Flag_A_01_C" }),
        ("Sword & Shield", new[] { "Deco_Medieval_SwordShield_C" }),
        ("Color Ceiling Fan", new[] { "Deco_CeilingFan_Colors_C" }),
        ("Neon Ceiling Fan", new[] { "Deco_CeilingFan_Neon_C" }),
        ("Wood Ceiling Fan", new[] { "Deco_CeilingFan_Wood_C" }),
        ("Camera", new[] { "Deco_Camera_C" }),
        ("Film Wheel", new[] { "Deco_FilmWheel_C" }),
        ("Theatre Masks", new[] { "Deco_TheatreMask_C" }),
        ("Spotlight", new[] { "Deco_Light-Cinema_C" }),
        ("Ceiling Spotlight", new[] { "Deco_Ceilling-Light_Color_A_01_C" }),
        ("Robot", new[] { "Deco_Robot_BASE_C" }),
        ("LED Disco Ball", new[] { "Deco_LightBall_Ball_A_01_C" }),
        ("Disco Ball", new[] { "Deco_LightBall_Ball_A_02_C" }),
        ("Cactus", new[] { "Deco_Western_Cactus_A_C" }),
        ("VHS Collection", new[] { "Deco_VHSCollection_C" }),
        ("Rug", new[] { "Deco_Rug_BASE_C" }),

        // Equipment
        ("Snack Shelf", new[] { "SnackShelf_Shelf_A_01_C" }),
        ("Television", new[] { "BP_Television_Cart_C" }),
        ("Television Base", new[] { "BP_Television_BASE_C" }),
        ("Clearance Bin", new[] { "ClearanceBin_Base_C" }),
        ("Refrigerator", new[] { "Fridge_Base_C" }),
        ("Candy Dispense", new[] { "CandyDispense_01_C" }),
        ("Arcade Maze Munch", new[] { "Arcade_A_C" }),
        ("Arcade Orc Invaders", new[] { "Arcade_B_C" }),
        ("Arcade Rampant Rage", new[] { "Arcade_C_C" }),
        ("VHS Player(s)", new[] { "VHSPlayer_C" }),
        ("Toy Shelf", new[] { "ToysShelf_Base_C" }),
        ("Pinball Machine", new[] { "Pinball-Machine_A_C" }),
        ("Packaged Concessions Shelf", new[] { "ConcessionsShelf_Base_C" }),

        // Shelves
        ("Thin Movie Shelf 4 Rows", new[] { "Shelf_Movie_4Row_01_C" }),
        ("Standard Movie Shelf 4 Rows", new[] { "Shelf_Movie_4Row_02_C" }),
        ("Thin Movie Shelf 5 Rows", new[] { "Shelf_Movie_5Row_01_C" }),
        ("Standard Movie Shelf 5 Rows", new[] { "Shelf_Movie_5Row_02_C" }),
        ("Thin Movie Shelf 6 Rows", new[] { "Shelf_Movie_6Row_01_C" }),
        ("Standard Movie Shelf 6 Rows", new[] { "Shelf_Movie_6Row_02_C" }),
        ("New Releases Movie Shelf 4 Rows", new[] { "Shelf_NewMovie_4Row_02_C" }),
        ("New Releases Movie Shelf 5 Rows", new[] { "Shelf_NewMovie_5Row_02_C" }),
        ("New Releases Movie Shelf 6 Rows", new[] { "Shelf_NewMovie_6Row_02_C" }),

        // Excluded
        ("Video Tape(s)", new[] { "videotape_C" }),
        ("Storage Box", new[] { "Storage_Box_C" }),
        ("VHS Player(s)", new[] { "VHSPlayer_C" }),
        ("Standee(s)", new[] { "__STANDEE_GROUP__" })
    };

    private static string GetObjectGroup(string className, bool engineExcluded)
    {
        if (className.StartsWith("Standees_", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "videotape_C", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "Storage_Box_C", StringComparison.OrdinalIgnoreCase))
            return "Excluded";

        var equipment = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SnackShelf_Shelf_A_01_C", "BP_Television_Cart_C", "BP_Television_BASE_C", "ClearanceBin_Base_C",
            "VHSPlayer_C", "Fridge_Base_C", "CandyDispense_01_C", "Arcade_A_C", "Arcade_B_C",
            "Arcade_C_C", "ToysShelf_Base_C", "Pinball-Machine_A_C",
            "ConcessionsShelf_Base_C"
        };
        if (equipment.Contains(className))
            return "Equipment";

        var shelves = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shelf_Movie_4Row_01_C", "Shelf_Movie_4Row_02_C",
            "Shelf_Movie_5Row_01_C", "Shelf_Movie_5Row_02_C",
            "Shelf_Movie_6Row_01_C", "Shelf_Movie_6Row_02_C",
            "Shelf_NewMovie_4Row_02_C", "Shelf_NewMovie_5Row_02_C",
            "Shelf_NewMovie_6Row_02_C"
        };
        if (shelves.Contains(className))
            return "Shelves";

        // Everything explicitly named in the supplied Decorations list belongs there.
        if (ObjectDisplayGroups.Any(g =>
            g.DisplayName != "Standee(s)" &&
            g.DisplayName != "Television Base" &&
            g.Classes.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase))))
            return "Decorations";

        return engineExcluded ? "Excluded" : "Excluded";
    }

    private void ObjectGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string group)
            SetObjectGroup(group);
    }

    private void SetObjectGroup(string group)
    {
        _selectedObjectGroup = group;

        var rows = group.Equals("Excluded", StringComparison.OrdinalIgnoreCase)
            ? _allObjects.Where(o => !o.IsIncluded).ToList()
            : _allObjects.Where(o => string.Equals(o.Group, group, StringComparison.OrdinalIgnoreCase)).ToList();

        ObjectList.ItemsSource = rows;

        var buttons = new[] {
            ObjectsDecorationsButton,
            ObjectsEquipmentButton,
            ObjectsShelvesButton,
            ObjectsExcludedButton
        };

        foreach (var button in buttons)
        {
            var active = string.Equals(
                button.Tag?.ToString(),
                group,
                StringComparison.OrdinalIgnoreCase);

            // The active group is disabled so it has a clear selected state
            // and cannot be clicked again.
            button.IsEnabled = !active;
        }

        InspectorObjectCount.Text = $"{rows.Sum(o => o.Count)} objects";
    }

    private static string GetObjectDisplayName(string className)
    {
        if (className.StartsWith("Standees_", StringComparison.OrdinalIgnoreCase))
            return "Standee(s)";

        foreach (var group in ObjectDisplayGroups)
        {
            if (group.Classes.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase)))
                return group.DisplayName;
        }
        return className;
    }

    private void ObjectList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // The All Objects view is informational only. Keep wheel/scrollbar
        // interaction available, but prevent row selection/click behavior.
        e.Handled = true;
    }

    private void ObjectList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ObjectColumn == null || CountColumn == null || ObjectList == null) return;

        // Keep the checkbox and count columns compact, and let Object use the
        // remaining width. Account for the vertical scrollbar and small padding.
        const double statusWidth = 30;
        const double countWidth = 70;
        const double scrollbarAllowance = 24;
        const double minimumObjectWidth = 220;

        var available = ObjectList.ActualWidth - statusWidth - countWidth - scrollbarAllowance;
        ObjectColumn.Width = Math.Max(minimumObjectWidth, available);
        CountColumn.Width = countWidth;
    }

        private sealed record ObjectRow(string Class, int Count, string Asset = "", string Array = "", string Type = "", bool IsIncluded = true, string Group = "Decorations");

    private void RememberSourcePath()
    {
        if (_mode == "transfer") TransferSourcePath = SourceBox.Text;
        else if (_mode == "export") ExportSourcePath = SourceBox.Text;
        else if (_mode == "import") ImportBlueprintPath = SourceBox.Text;
        RememberCurrentPaths();
    }

    private void StartGameActivityMonitor()
    {
        _gameActivityTimer?.Stop();
        _gameActivityTimer = new DispatcherTimer { Interval = GameActivityPollInterval };
        _gameActivityTimer.Tick += (_, _) => UpdateGameActivityState();
        _gameActivityTimer.Start();
        UpdateGameActivityState();
    }

    private bool IsLikelyGameProcess(Process process, out string displayName)
    {
        displayName = process.ProcessName;
        if (process.Id == Environment.ProcessId) return false;
        if (NonGameFullscreenProcesses.Contains(process.ProcessName)) return false;

        // Do not treat Steam/Epic/GOG/Xbox/EA installation paths as proof that
        // an application is a game. A number of utilities and creative tools
        // are legitimately distributed through those stores.
        //
        // Instead, a generic process must present a visible window that is
        // actually covering almost the entire monitor. This avoids putting
        // Steam utilities into Power Saving merely because they live under
        // steamapps\\common.
        var hwnd = process.MainWindowHandle;
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out var rect))
            return false;

        var width = Math.Max(0, rect.Right - rect.Left);
        var height = Math.Max(0, rect.Bottom - rect.Top);
        if (width < 800 || height < 450) return false;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        var mw = info.rcMonitor.Right - info.rcMonitor.Left;
        var mh = info.rcMonitor.Bottom - info.rcMonitor.Top;
        var coversMonitor = width >= mw * 0.95 && height >= mh * 0.95;
        if (!coversMonitor) return false;

        displayName = process.ProcessName;
        return true;
    }

    private bool IsGameOrFullscreenAppActive(out string detectedName)
    {
        detectedName = string.Empty;
        try
        {
            // Only a real fullscreen/borderless window is sufficient to trigger
            // automatic game mode. Store membership alone is intentionally not.
            var foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && IsWindowVisible(foreground) &&
                GetWindowThreadProcessId(foreground, out var foregroundPid) != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)foregroundPid);
                    if (IsLikelyGameProcess(process, out detectedName)) return true;
                }
                catch { }
            }

            // A background fullscreen game is still detected, but ordinary
            // Steam-installed applications are not scanned as games merely
            // because their executable path contains steamapps\\common.
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (IsLikelyGameProcess(process, out detectedName)) return true;
                }
                catch { }
                finally { process.Dispose(); }
            }
        }
        catch { }

        return false;
    }

    private bool IsPowerSavingActive()
    {
        return _powerSaveMode switch
        {
            "powersaving" => true,
            "performance" => false,
            _ => _gameActive
        };
    }

    private void UpdateGameActivityState()
    {
        var active = IsGameOrFullscreenAppActive(out var detectedName);
        var wasPowerSaving = IsPowerSavingActive();
        var changed = active != _gameActive || !string.Equals(detectedName, _detectedGameName, StringComparison.OrdinalIgnoreCase);
        _detectedGameName = active ? detectedName : string.Empty;
        _gameActive = active;
        var powerSavingChanged = wasPowerSaving != IsPowerSavingActive();
        if (!changed && !powerSavingChanged) return;

        if (IsPowerSavingActive())
        {
            // Once the game is running, stop non-essential refresh work. The
            // monitor itself is only a 10-second lightweight process check.
            if (_mode == "videoeditor")
                StopAndReleaseVideoEditorPreview();
            try { _modRefreshCts?.Cancel(); } catch { }
            try { _videoLibraryRefreshCts?.Cancel(); } catch { }
            try { _nexusBackgroundCts?.Cancel(); } catch { }
            try { _homeNewsCts?.Cancel(); } catch { }
        }
        else
        {
            // Refresh only the page the user is actually viewing after gameplay.
            if (_mode == "mods") RefreshModManager();
            else if (_mode == "videos") RefreshVideosPage();
        }
    }

    private void RememberTargetPath()
    {
        if (_mode == "transfer") TransferTargetPath = TargetBox.Text;
        else if (_mode == "import") ImportTargetPath = TargetBox.Text;
        RememberCurrentPaths();
    }

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var exeName = _runLaunchExecutables.FirstOrDefault()
                ?? "RetroRewind-Win64-Shipping.exe";
            var library = _runForceLoadLibraries.FirstOrDefault()
                ?? "dwmapi.dll";
            var arguments = (_runArguments ?? "").Trim();

            var exeCandidates = new[]
            {
                Path.Combine(gameRoot, exeName),
                Path.Combine(gameRoot, "RetroRewind", "Binaries", "Win64", exeName),
                Path.Combine(gameRoot, "Binaries", "Win64", exeName)
            };
            var exe = exeCandidates.FirstOrDefault(File.Exists);
            if (exe == null)
                throw new FileNotFoundException($"The selected game executable '{exeName}' could not be found.");

            var libraries = string.IsNullOrWhiteSpace(library)
                ? Array.Empty<string>()
                : new[] { library };

            if (library.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase))
            {
                var proxy = Path.Combine(Path.GetDirectoryName(exe)!, "dwmapi.dll");
                var ue4ss = Path.Combine(Path.GetDirectoryName(exe)!, "ue4ss", "UE4SS.dll");
                if (!File.Exists(proxy))
                    throw new FileNotFoundException(
                        "Force Load Library is set to dwmapi.dll, but the UE4SS proxy was not found beside the selected game executable.",
                        proxy);
                if (!File.Exists(ue4ss))
                    throw new FileNotFoundException(
                        "The UE4SS proxy was found, but ue4ss\\UE4SS.dll is missing.",
                        ue4ss);
            }

            RunLaunchHelper.Launch(exe, arguments, libraries);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                L("Retro Rewind could not be launched.\n\n{0}", ex.Message),
                L("Launch Retro Rewind"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RememberCurrentPaths();

            // Retro Rewind's save location is fixed under LocalAppData.
            var folder = SaveFolderPath;

            if (!Directory.Exists(folder))
            {
                MessageBox.Show(
                    $"The Retro Rewind save folder could not be found:\n\n{folder}",
                    "Retro Rewind: ModHub",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open the save folder.\n\n{ex.Message}",
                "Retro Rewind: ModHub",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == "import")
        {
            await SelectBlueprintWorkflowAsync();
            return;
        }

        var d = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Retro Rewind saves (*.sav)|*.sav|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (d.ShowDialog() == true)
            SourceBox.Text = d.FileName;
    }

    private void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        Browse(TargetBox, "Retro Rewind saves (*.sav)|*.sav|All files (*.*)|*.*");
    }

    private void BrowseBlueprint_Click(object sender, RoutedEventArgs e) => Browse(BlueprintBox, "Retro Rewind blueprints (*.rrblueprint;*.json)|*.rrblueprint;*.json|All files (*.*)|*.*");

    private static void Browse(TextBox box, string filter) { var d = new Microsoft.Win32.OpenFileDialog { Filter = filter }; if (d.ShowDialog() == true) box.Text = d.FileName; }

    private bool ConfirmSaveModification(string operation)
    {
        string source = SourceBox.Text;
        string target = TargetBox.Text;

        string title;
        string message;

        if (operation == "import")
        {
            title = "Confirm Blueprint Import";
            message =
                "This will import the selected store blueprint into the selected target save.\n\n" +
                "What will happen:\n" +
                "• A backup of the current target save will be created.\n" +
                "• The selected import options will be applied to that save.\n" +
                "• The modified save will replace the original target save using the original filename, so the game can load it normally.\n\n" +
                $"Target save:\n{Path.GetFileName(target)}\n\n" +
                "Do you want to continue?";
        }
        else
        {
            title = "Confirm Furniture Transfer";
            message =
                "This will transfer the selected store furniture from the source save to the target save.\n\n" +
                "What will happen:\n" +
                "• Existing target furniture will be replaced where applicable.\n" +
                "• A backup of the current target save will be created.\n" +
                "• The existing target save will remain untouched; the modified save will be written as a new output file.\n" +
                "• The modified save will replace the original target save using the original filename, so the game can load it normally.\n\n" +
                $"Source save:\n{Path.GetFileName(source)}\n\n" +
                $"Target save:\n{Path.GetFileName(target)}\n\n" +
                "Do you want to continue?";
        }

        return MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private static string CleanEngineError(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "The save could not be validated.";

        text = text.Trim();

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(error.GetString()))
                return error.GetString()!;
        }
        catch (JsonException)
        {
            // Not JSON; keep the original engine text.
        }

        return text;
    }

    private async Task<SaveSanityResult> CheckSaveSanity(string path, string label, bool checkTransferArrayOrder = false)
    {
        if (!File.Exists(path))
            return new SaveSanityResult(false, 0, $"{label} does not exist.");

        try
        {
            var countTask = RunEngine($"count {Q(path)}");
            var metadataTask = RunEngine($"metadata {Q(path)}");
            var orderTask = checkTransferArrayOrder
                ? RunEngine($"array_order {Q(path)}")
                : Task.FromResult((code: 0, stdout: "", stderr: ""));
            var results = await Task.WhenAll(countTask, metadataTask, orderTask);

            if (results[0].code != 0)
                return new SaveSanityResult(false, 0,
                    $"{label} failed the object-data check.\n\n" +
                    CleanEngineError(!string.IsNullOrWhiteSpace(results[0].stderr)
                        ? results[0].stderr : results[0].stdout));

            if (results[1].code != 0)
                return new SaveSanityResult(false, 0,
                    $"{label} failed the metadata check.\n\n" +
                    CleanEngineError(!string.IsNullOrWhiteSpace(results[1].stderr)
                        ? results[1].stderr : results[1].stdout));

            if (checkTransferArrayOrder)
            {
                if (results[2].code != 0)
                    return new SaveSanityResult(false, 0,
                        $"{label} failed the transfer array-order check.\n\n" +
                        CleanEngineError(!string.IsNullOrWhiteSpace(results[2].stderr)
                            ? results[2].stderr : results[2].stdout));

                using var orderDoc = JsonDocument.Parse(results[2].stdout);
                var orderRoot = orderDoc.RootElement;
                if (!orderRoot.TryGetProperty("ok", out var orderOk) || !orderOk.GetBoolean())
                {
                    var orderError = orderRoot.TryGetProperty("error", out var orderErrorEl) &&
                                     orderErrorEl.ValueKind == JsonValueKind.String
                        ? orderErrorEl.GetString()
                        : "Transfer object arrays are in an invalid order.";
                    return new SaveSanityResult(false, 0,
                        $"{label} failed the transfer array-order check.\n\n{orderError}");
                }
            }

            using var doc = JsonDocument.Parse(results[0].stdout);
            if (!doc.RootElement.TryGetProperty("count", out var countElement) ||
                !countElement.TryGetInt32(out var count))
                return new SaveSanityResult(false, 0, $"{label} returned an invalid object count.");

            return new SaveSanityResult(true, count, "");
        }
        catch (Exception ex)
        {
            return new SaveSanityResult(false, 0, $"{label} could not be validated.\n\n{ex.Message}");
        }
    }

    private static List<string> UnlockedRoomNames(Dictionary<string, bool> rooms) =>
        StoreUpgradeNames.Where(n => rooms.TryGetValue(n, out var unlocked) && unlocked).ToList();

    private static string FormatRoomList(IEnumerable<string> rooms)
    {
        var list = rooms.ToList();
        return list.Count == 0 ? "None" : string.Join(", ", list);
    }

    private SaveSanityResult CheckBlueprintSanity(string path)
    {
        if (!File.Exists(path))
            return new SaveSanityResult(false, 0, "The blueprint file does not exist.");

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return new SaveSanityResult(false, 0, "The blueprint root is not a JSON object.");

            if (!root.TryGetProperty("format", out var format) ||
                format.ValueKind != JsonValueKind.String ||
                format.GetString() != "RetroRewindStoreBlueprint")
                return new SaveSanityResult(false, 0, "The file is not a Retro Rewind Store Blueprint.");

            if (!root.TryGetProperty("version", out var version) ||
                (version.ValueKind != JsonValueKind.String && version.ValueKind != JsonValueKind.Number))
                return new SaveSanityResult(false, 0, "The blueprint is missing a valid version.");

            var versionText = version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : version.GetRawText();
            if (string.IsNullOrWhiteSpace(versionText))
                return new SaveSanityResult(false, 0, "The blueprint is missing a valid version.");

            if (!root.TryGetProperty("furniture", out var furniture) ||
                furniture.ValueKind != JsonValueKind.Array)
                return new SaveSanityResult(false, 0, "The blueprint is missing its furniture data.");

            var roomData = ReadBlueprintRoomUnlocks(path);
            if (!roomData.Ok)
                return new SaveSanityResult(false, 0, roomData.Error);

            var declaredCount = root.TryGetProperty("count", out var countElement) &&
                                countElement.TryGetInt32(out var c) ? c : furniture.GetArrayLength();

            if (declaredCount != furniture.GetArrayLength())
                return new SaveSanityResult(false, 0,
                    $"Blueprint count mismatch: it declares {declaredCount} objects but contains {furniture.GetArrayLength()}.");

            if (!root.TryGetProperty("templates", out var templates) ||
                templates.ValueKind != JsonValueKind.Object)
                return new SaveSanityResult(false, 0, "The blueprint is missing its object templates.");

            if (!root.TryGetProperty("array_headers", out var headers) ||
                headers.ValueKind != JsonValueKind.Object)
                return new SaveSanityResult(false, 0, "The blueprint is missing its array headers.");

            foreach (var item in furniture.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    return new SaveSanityResult(false, 0, "The blueprint contains an invalid furniture entry.");

                if (!item.TryGetProperty("class", out var cls) ||
                    cls.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(cls.GetString()))
                    return new SaveSanityResult(false, 0, "A furniture entry is missing its class.");

                if (!item.TryGetProperty("asset", out var asset) ||
                    asset.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(asset.GetString()))
                    return new SaveSanityResult(false, 0,
                        $"Furniture entry '{cls.GetString()}' is missing its asset.");

                if (!item.TryGetProperty("array", out var array) ||
                    array.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(array.GetString()))
                    return new SaveSanityResult(false, 0,
                        $"Furniture entry '{cls.GetString()}' is missing its array.");

                if (!item.TryGetProperty("location", out var location) ||
                    location.ValueKind != JsonValueKind.Array ||
                    location.GetArrayLength() != 3)
                    return new SaveSanityResult(false, 0,
                        $"Furniture entry '{cls.GetString()}' has invalid location data.");

                foreach (var v in location.EnumerateArray())
                    if (v.ValueKind != JsonValueKind.Number)
                        return new SaveSanityResult(false, 0,
                            $"Furniture entry '{cls.GetString()}' has non-numeric location data.");

                if (item.TryGetProperty("scale", out var scale) &&
                    (scale.ValueKind != JsonValueKind.Array || scale.GetArrayLength() != 3))
                    return new SaveSanityResult(false, 0,
                        $"Furniture entry '{cls.GetString()}' has invalid scale data.");
            }

            // Validate every embedded template/header is actually valid base64.
            foreach (var property in templates.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    return new SaveSanityResult(false, 0,
                        $"Template '{property.Name}' is not a string.");

                try { Convert.FromBase64String(property.Value.GetString() ?? ""); }
                catch { return new SaveSanityResult(false, 0,
                    $"Template '{property.Name}' contains invalid base64 data."); }
            }

            foreach (var property in headers.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    return new SaveSanityResult(false, 0,
                        $"Array header '{property.Name}' is not a string.");

                try { Convert.FromBase64String(property.Value.GetString() ?? ""); }
                catch { return new SaveSanityResult(false, 0,
                    $"Array header '{property.Name}' contains invalid base64 data."); }
            }

            return new SaveSanityResult(true, furniture.GetArrayLength(), "");
        }
        catch (JsonException ex)
        {
            return new SaveSanityResult(false, 0, $"The blueprint JSON is corrupted or incomplete.\n\n{ex.Message}");
        }
        catch (Exception ex)
        {
            return new SaveSanityResult(false, 0, $"The blueprint could not be validated.\n\n{ex.Message}");
        }
    }

    private async Task RequireSaveSanity(string path, string label)
    {
        var check = await CheckSaveSanity(path, label);
        if (!check.Ok)
            throw new InvalidDataException(check.Error);
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void RestoreOriginalAfterFailedPostCheck(
        string originalSave, string backupPath, string modifiedPath)
    {
        try
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException(
                    "The verified backup required for restoration is missing.", backupPath);

            // Only delete the modified live save after confirming the verified backup exists.
            DeleteFileIfPresent(modifiedPath);
            File.Copy(backupPath, originalSave, false);
        }
        catch (Exception restoreEx)
        {
            throw new IOException(
                "POST-MODIFICATION SANITY CHECK FAILED and the original save could not be restored automatically.\n\n" +
                $"Original: {originalSave}\nBackup: {backupPath}\n\n{restoreEx.Message}",
                restoreEx);
        }
    }

    private sealed record SaveSanityResult(bool Ok, int ObjectCount, string Error);

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string output, args;
            if (_mode == "export")
            {
                if (!_exportSourceHealthOk)
                    throw new InvalidOperationException(
                        "Blueprint export cannot start until the selected save passes its health check.");

                Require(SourceBox.Text);
                var blueprintFileName = BuildBlueprintFileName(BlueprintNameBox.Text);
                Directory.CreateDirectory(BlueprintFolderPath);
                output = Path.Combine(BlueprintFolderPath, blueprintFileName);

                if (File.Exists(output))
                {
                    var overwrite = MessageBox.Show(
                        this,
                        $"A blueprint named \"{blueprintFileName}\" already exists.\n\nDo you want to replace it?",
                        "Blueprint Already Exists",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (overwrite != MessageBoxResult.Yes) return;
                }

                args=$"export {Q(SourceBox.Text)} {Q(output)}";
                SetOperationBusy(true, "Creating blueprint…");
                BlueprintExportStatus.Text = "Creating blueprint…";
            }
            else if (_mode == "import")
            {
                Require(SourceBox.Text); Require(TargetBox.Text);
                if (!_importBlueprintHealthOk || !_importTargetHealthOk)
                    throw new InvalidOperationException(
                        "Import cannot start until the blueprint and target save both pass their health checks.");
                var blueprintCheck = CheckBlueprintSanity(SourceBox.Text);
                if (!blueprintCheck.Ok)
                    throw new InvalidDataException(
                        "The blueprint failed its pre-import sanity check.\n\n" +
                        blueprintCheck.Error);
                if (!ConfirmSaveModification("import")) return;
                SetOperationBusy(true, "Preparing import…");

                var name=Path.GetFileNameWithoutExtension(TargetBox.Text)+" - Store Import.sav";
                output=Path.Combine(Path.GetDirectoryName(TargetBox.Text)!,name);

                // The selected .rrblueprint is not itself a save. Read its
                // source_save field directly so the importer always receives
                // the ORIGINAL save that supplied the furniture templates.
                string sourceSave="";
                try
                {
                    using var bpDoc=JsonDocument.Parse(File.ReadAllText(SourceBox.Text));
                    if (bpDoc.RootElement.TryGetProperty("source_save",out var sourceNameEl) &&
                        sourceNameEl.ValueKind==JsonValueKind.String)
                    {
                        var sourceName=sourceNameEl.GetString();
                        if (!string.IsNullOrWhiteSpace(sourceName))
                        {
                            var configured=Path.Combine(SaveFolderPath,sourceName);
                            var beside=Path.Combine(Path.GetDirectoryName(SourceBox.Text)!,sourceName);
                            if (File.Exists(configured)) sourceSave=configured;
                            else if (File.Exists(beside)) sourceSave=beside;
                        }
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(sourceSave))
                {
                    throw new FileNotFoundException(
                        "The original source save named by this blueprint could not be found. " +
                        "Place the source .sav in the configured Save folder or beside the blueprint.");
                }

                SetOperationBusy(true, "Validating source save…");
                await RequireSaveSanity(sourceSave, "The source save");
                SetOperationBusy(true, "Validating target save…");
                await RequireSaveSanity(TargetBox.Text, "The target save");

                args=$"import {Q(sourceSave)} {Q(TargetBox.Text)} {Q(SourceBox.Text)} {Q(output)}" +
                    (ImportFurnitureBox.IsChecked == true ? "" : " --no-furniture") +
                    (ImportStoreStyleBox.IsChecked == true ? "" : " --no-store-style");

            }
            else
            {
                Require(SourceBox.Text); Require(TargetBox.Text);
                if (!ConfirmSaveModification("transfer")) return;
                SetOperationBusy(true, "Validating source save…");
                await RequireSaveSanity(SourceBox.Text, "The source save");
                SetOperationBusy(true, "Validating target save…");
                await RequireSaveSanity(TargetBox.Text, "The target save");
                output=Path.Combine(Path.GetDirectoryName(TargetBox.Text)!,Path.GetFileNameWithoutExtension(TargetBox.Text)+" - Store Transfer.sav");
                args=$"transfer {Q(SourceBox.Text)} {Q(TargetBox.Text)} {Q(output)}" +
                    (TransferFurnitureBox.IsChecked == true ? "" : " --no-furniture") +
                    (TransferStoreStyleBox.IsChecked == true ? "" : " --no-store-style");

            }
            SetOperationBusy(true, "Running operation…");
            ActiveActionButton.IsEnabled=false; ActiveActionButton.Visibility=Visibility.Visible; Progress.IsIndeterminate=true; StatusText.Text="Working…";
            var result=await RunEngine(args);
            Progress.IsIndeterminate=false; ActiveActionButton.Visibility=Visibility.Visible; ActiveActionButton.IsEnabled=true;
            if(result.code!=0) throw new Exception(result.stderr.Length>0?result.stderr:result.stdout);

            if (_mode == "export")
            {
                // Automatically validate the newly created blueprint before reporting
                // the export as successful. The output is deleted if validation fails.
                SourceHealthStatus.Text = "Blueprint health check: Checking…";
                SetOperationBusy(true, "Checking blueprint…");

                var blueprintCheck = CheckBlueprintSanity(output);
                if (!blueprintCheck.Ok)
                {
                    SourceHealthStatus.Text =
                        "Blueprint health check: Warning — " + blueprintCheck.Error;

                    DeleteFileIfPresent(output);
                    SetOperationBusy(false);
                    BlueprintExportStatus.Visibility = Visibility.Visible;
                    BlueprintExportStatus.Text = "✕ Export failed: the blueprint failed its health check and was deleted. " + blueprintCheck.Error;
                    BlueprintExportStatus.Foreground = (Brush)Resources["CoreUiRedBrush"];
                    UpdateActionButtons();
                    return;
                }

                SourceHealthStatus.Text =
                    "Blueprint health check: Passed — Blueprint health check successful.";
                BlueprintExportStatus.Text = $"✓ Blueprint saved successfully: {Path.GetFileNameWithoutExtension(output)}";
                BlueprintExportStatus.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            }

            string? backupPath = null;
            if (_mode == "transfer" || _mode == "import")
            {
                // Validate the generated save before touching the original target.
                SetOperationBusy(true, "Validating generated save…");
                var generatedCheck = await CheckSaveSanity(output, "The generated save", true);
                if (!generatedCheck.Ok)
                {
                    DeleteFileIfPresent(output);
                    throw new InvalidDataException(
                        "The generated save failed the pre-replacement sanity check. " +
                        "The original target save was not modified.\n\n" +
                        generatedCheck.Error);
                }

                // Preserve the original, then put the generated save under the game's
                // expected filename.
                SetOperationBusy(true, "Creating verified backup…");
                backupPath = await BackupAndReplaceSave(TargetBox.Text, output);
                output = TargetBox.Text;

                // The live replacement must pass the same validation. If it does not,
                // delete the modified file and restore the exact original from backup.
                SetOperationBusy(true, "Running final health check…");
                var postCheck = await CheckSaveSanity(output, "The modified save", true);
                if (!postCheck.Ok)
                {
                    RestoreOriginalAfterFailedPostCheck(
                        TargetBox.Text, backupPath, TargetBox.Text);

                    output = TargetBox.Text;
                    backupPath = null;
                    throw new InvalidDataException(
                        "POST-MODIFICATION SANITY CHECK FAILED.\n\n" +
                        "The modified save was deleted and the original save was restored.\n\n" +
                        postCheck.Error);
                }
            }

            StatusText.Text="✓ Completed";
            SetOperationBusy(false);
            MessageBox.Show(Pretty(result.stdout,output,backupPath),"Retro Rewind: ModHub",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex)
        {
            Progress.IsIndeterminate=false;
            SetOperationBusy(false);
            ActiveActionButton.Visibility=Visibility.Visible;
            ActiveActionButton.IsEnabled=true;
            StatusText.Text="Operation failed";
            if (_mode == "export")
            {
                BlueprintExportStatus.Visibility = Visibility.Visible;
                BlueprintExportStatus.Text = "✕ Export failed: " + ex.Message;
                BlueprintExportStatus.Foreground = (Brush)Resources["CoreUiRedBrush"];
                UpdateActionButtons();
            }
            else
            {
                MessageBox.Show(ex.Message,"Retro Rewind: ModHub",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        }
    }

    private string? FindLatestStoreTransferBackup(string savePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(savePath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return null;

            var stem = Path.GetFileNameWithoutExtension(savePath);
            return Directory.GetFiles(dir, $"{stem}_StoreTransferBackup_*.sav")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private async Task RunStoreManagementOperationAsync(string operation)
    {
        if (_operationBusy || !File.Exists(SourceBox.Text))
            return;

        string selected = SourceBox.Text;
        bool removeFurniture = ManagementFurnitureBox.IsChecked == true;
        bool resetStyle = ManagementStoreStyleBox.IsChecked == true;
        if (!removeFurniture && !resetStyle)
            return;

        string output = Path.Combine(Path.GetDirectoryName(selected)!,
            Path.GetFileNameWithoutExtension(selected) + " - Store Management.sav");
        string working = output;
        string? intermediate = null;
        string? backupPath = null;

        try
        {
            string operationName = removeFurniture && resetStyle
                ? "Reset Shop"
                : removeFurniture
                    ? "Remove All Furniture"
                    : "Reset Shop Style";

            var warning = removeFurniture && resetStyle
                ? "Reset Shop will remove all detected store furniture and reset the shop walls, floors, and ceilings to the original vanilla Retro Rewind style."
                : removeFurniture
                    ? "Remove All Furniture will remove all detected store furniture objects from the selected save."
                    : "Reset Shop Style will reset the selected save's shop walls, floors, and ceilings to the original vanilla Retro Rewind style.";

            if (removeFurniture)
            {
                warning += "\n\nWARNING: If your store contains a large amount of movies, removing all furniture can drastically reduce FPS in-game.";
            }

            warning += "\n\nFurniture, shop style, or other save data will only be changed according to the selected Management Options.\n\n" +
                       "The original save will be backed up automatically before it is changed.\n\n" +
                       "Do you want to continue?";

            var answer = MessageBox.Show(
                this,
                warning,
                operationName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            SetOperationBusy(true, "Running shop reset…");

            // Build the requested changes as a chain of engine operations. Each
            // operation writes to a temporary save; the live save is untouched
            // until both requested changes have passed validation.
            if (removeFurniture && resetStyle)
            {
                intermediate = Path.Combine(Path.GetDirectoryName(selected)!,
                    Path.GetFileNameWithoutExtension(selected) + " - Store Management Furniture.sav");
                DeleteFileIfPresent(intermediate);
                var furnitureResult = await RunEngine($"remove-furniture {Q(selected)} {Q(intermediate)}");
                if (furnitureResult.code != 0)
                    throw new Exception(string.IsNullOrWhiteSpace(furnitureResult.stderr) ? furnitureResult.stdout : furnitureResult.stderr);

                var furnitureCheck = await CheckSaveSanity(intermediate, "The furniture-reset save", true);
                if (!furnitureCheck.Ok)
                    throw new InvalidDataException("The generated save failed validation. The original save was not modified.\n\n" + furnitureCheck.Error);

                var styleResult = await RunEngine($"restore-style {Q(intermediate)} {Q(working)}");
                if (styleResult.code != 0)
                    throw new Exception(string.IsNullOrWhiteSpace(styleResult.stderr) ? styleResult.stdout : styleResult.stderr);
            }
            else if (removeFurniture)
            {
                var result = await RunEngine($"remove-furniture {Q(selected)} {Q(working)}");
                if (result.code != 0)
                    throw new Exception(string.IsNullOrWhiteSpace(result.stderr) ? result.stdout : result.stderr);
            }
            else
            {
                var result = await RunEngine($"restore-style {Q(selected)} {Q(working)}");
                if (result.code != 0)
                    throw new Exception(string.IsNullOrWhiteSpace(result.stderr) ? result.stdout : result.stderr);
            }

            SetOperationBusy(true, "Validating generated save…");
            var generated = await CheckSaveSanity(working, "The generated save", removeFurniture);
            if (!generated.Ok)
            {
                DeleteFileIfPresent(working);
                throw new InvalidDataException("The generated save failed validation. The original save was not modified.\n\n" + generated.Error);
            }

            SetOperationBusy(true, "Creating verified backup…");
            backupPath = await BackupAndReplaceSave(selected, working);

            SetOperationBusy(true, "Running final health check…");
            var finalCheck = await CheckSaveSanity(selected, "The modified save", removeFurniture);
            if (!finalCheck.Ok)
            {
                RestoreOriginalAfterFailedPostCheck(selected, backupPath, selected);
                throw new InvalidDataException("POST-MODIFICATION SANITY CHECK FAILED.\n\nThe original save was restored.\n\n" + finalCheck.Error);
            }

            await UpdateSaveDetails(selected, SourceCount, SourceMovies, SourceLevel, SourceMoney, SourceGameDate, SourceLastPlayed);
            SourceInfo.Text = Info(selected, ".sav");
            SourceHealthStatus.Text = $"Passed • {finalCheck.ObjectCount} objects";
            MessageBox.Show(
                this,
                removeFurniture && resetStyle
                    ? $"Shop reset completed: all detected furniture was removed and the shop style was reset to the original vanilla Retro Rewind style.\n\nBackup: {Path.GetFileName(backupPath)}"
                    : removeFurniture
                        ? $"All detected furniture was removed.\n\nBackup: {Path.GetFileName(backupPath)}"
                        : $"Shop style was reset to the original vanilla Retro Rewind style.\n\nBackup: {Path.GetFileName(backupPath)}",
                "Store Management Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DeleteFileIfPresent(output);
            DeleteFileIfPresent(intermediate);
            MessageBox.Show(this, ex.Message, "Store Management Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetOperationBusy(false);
            UpdateActionButtons();
        }
    }

    private async void ResetShop_Click(object sender, RoutedEventArgs e)
    {
        await RunStoreManagementOperationAsync("reset-shop");
    }

    private async Task<string> BackupAndReplaceSave(string originalSave, string modifiedSave)
    {
        if (!File.Exists(originalSave))
            throw new FileNotFoundException("The original save could not be found.", originalSave);

        if (!File.Exists(modifiedSave))
            throw new FileNotFoundException("The modified save could not be created.", modifiedSave);

        var directory = SaveFolderPath;
        Directory.CreateDirectory(directory);

        var originalName = Path.GetFileNameWithoutExtension(originalSave);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backup = Path.Combine(directory,
            $"{originalName}_StoreTransferBackup_{stamp}.sav");

        if (File.Exists(backup))
        {
            int suffix = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(directory,
                    $"{originalName}_StoreTransferBackup_{stamp}_{suffix}.sav");
                suffix++;
            } while (File.Exists(candidate));
            backup = candidate;
        }

        // CRITICAL SAFETY RULE:
        // Never delete or move the original until a complete, independently
        // verified backup exists.
        File.Copy(originalSave, backup, false);

        try
        {
            var originalInfo = new FileInfo(originalSave);
            var backupInfo = new FileInfo(backup);

            if (!backupInfo.Exists || backupInfo.Length != originalInfo.Length)
                throw new IOException("The backup could not be verified: file size does not match the original.");

            var originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalSave)));
            var backupHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backup)));

            if (!string.Equals(originalHash, backupHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The backup could not be verified: its contents do not exactly match the original.");

            var backupCheck = await CheckSaveSanity(backup, "The backup");
            if (!backupCheck.Ok)
                throw new IOException("The backup could not be verified as a healthy save.\n\n" + backupCheck.Error);

            // Only now is deletion of the original permitted.
            File.Delete(originalSave);

            try
            {
                File.Move(modifiedSave, originalSave);
            }
            catch
            {
                // The backup is known-valid, so restore immediately if the replacement fails.
                DeleteFileIfPresent(originalSave);
                File.Copy(backup, originalSave, false);
                throw;
            }

            return backup;
        }
        catch
        {
            // Never delete the backup. If the original is missing, restore it from
            // the verified backup before propagating the error.
            if (!File.Exists(originalSave) && File.Exists(backup))
            {
                try { File.Copy(backup, originalSave, false); }
                catch { }
            }
            throw;
        }
    }

    private async Task UpdateSaveDetails(
        string path, TextBlock count, TextBlock movies, TextBlock level,
        TextBlock money, TextBlock gameDate, TextBlock lastPlayed)
    {
        if (!File.Exists(path))
        {
            count.Text = movies.Text = level.Text = money.Text = gameDate.Text = lastPlayed.Text = "—";
            return;
        }

        count.Text = movies.Text = level.Text = money.Text = gameDate.Text = lastPlayed.Text = "Reading…";
        try
        {
            var furnitureTask = RunEngine($"count {Q(path)}");
            var metadataTask = RunEngine($"metadata {Q(path)}");
            var results = await Task.WhenAll(furnitureTask, metadataTask);

            var furniture = results[0];
            var metadata = results[1];

            if (furniture.code == 0)
            {
                using var fd = JsonDocument.Parse(furniture.stdout);
                var n = fd.RootElement.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
                count.Text = n == 1 ? "1 object" : $"{n} objects";
            }
            else count.Text = "Read failed";

            if (metadata.code != 0)
            {
                movies.Text = level.Text = money.Text = gameDate.Text = lastPlayed.Text = "—";
                return;
            }

            using var md = JsonDocument.Parse(metadata.stdout);
            var r = md.RootElement;

            string Get(string name)
            {
                if (!r.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
                    return "—";
                return v.ValueKind == JsonValueKind.Number ? v.ToString() : (v.GetString() ?? "—");
            }

            movies.Text = Get("total_movies");
            level.Text = Get("level");
            var m = Get("money64");
            money.Text = m == "—" ? "—" : GetCurrencySymbol() + m;
            gameDate.Text = Get("game_date");
            lastPlayed.Text = Get("last_played");
        }
        catch
        {
            count.Text = "Read failed";
            movies.Text = level.Text = money.Text = gameDate.Text = lastPlayed.Text = "—";
        }
    }

    private void SaveVideoReplacements(Dictionary<string, VideoReplacement> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VideoReplacementsFile)!);
        File.WriteAllText(VideoReplacementsFile, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private IEnumerable<string> EnumerateVideoLibrary()
    {
        if (!Directory.Exists(VideoLibraryRoot)) yield break;
        foreach (var file in Directory.EnumerateFiles(VideoLibraryRoot, "*.mp4", SearchOption.AllDirectories))
        {
            // _tmp is the Video Editor workspace, not part of the installed
            // Videos library. Never expose temporary source/preview files here.
            var relative = Path.GetRelativePath(VideoLibraryRoot, file);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part.Equals("_tmp", StringComparison.OrdinalIgnoreCase) ||
                             part.Equals("_downloads", StringComparison.OrdinalIgnoreCase)))
                continue;
            yield return file;
        }
    }

    private void InvalidateVideoLibraryCache()
    {
        _cachedVideoLibrary = null;
        _videoLibraryCacheUpdatedUtc = DateTime.MinValue;
    }

    private void BeginVideoLibraryRefresh(bool force = false)
    {
        if (_gameActive || _videoLibraryRefreshInProgress) return;
        if (!force && _cachedVideoLibrary != null &&
            (DateTime.UtcNow - _videoLibraryCacheUpdatedUtc) < TimeSpan.FromSeconds(30)) return;

        _videoLibraryRefreshInProgress = true;
        try { _videoLibraryRefreshCts?.Cancel(); } catch { }
        var cts = new CancellationTokenSource();
        _videoLibraryRefreshCts = cts;
        _ = Task.Run(() => EnumerateVideoLibrary().OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList(), cts.Token)
            .ContinueWith(t =>
            {
                if (t.IsCanceled || t.IsFaulted || cts.IsCancellationRequested) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    _cachedVideoLibrary = t.Result;
                    _videoLibraryCacheUpdatedUtc = DateTime.UtcNow;
                    _videoLibraryRefreshInProgress = false;
                    if (_mode == "videos") RefreshVideosPageCore();
                }));
            }, TaskScheduler.Default);
    }

    private string VideoMetadataKey(string path) => "video:" + Path.GetRelativePath(VideoLibraryRoot, path).Replace('\\', '/');

    private static string SanitizeVideoBaseName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((value ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Video" : safe;
    }

    private NexusModMetadata? GetNexusMetadataForVideoSource(string source)
    {
        var fullSource = Path.GetFullPath(source);
        var fileName = Path.GetFileName(source);
        var data = LoadNexusMetadata();
        foreach (var pair in data)
        {
            if (!pair.Key.StartsWith("_download:", StringComparison.OrdinalIgnoreCase)) continue;
            var meta = pair.Value;
            if (meta.ModId <= 0 || string.IsNullOrWhiteSpace(meta.Game)) continue;
            if (!string.Equals(pair.Key[10..], fileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(meta.ArchivePath))
            {
                var archive = Path.Combine(GetDownloadsDirectory(), meta.ArchivePath);
                if (File.Exists(archive) && string.Equals(Path.GetFullPath(archive), fullSource, StringComparison.OrdinalIgnoreCase))
                    return meta;
            }
            else return meta;
        }
        return null;
    }

    private string GetUniqueVideoPath(string fileName, string? preferredBaseName = null)
    {
        Directory.CreateDirectory(VideoLibraryRoot);
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".mp4";
        var baseName = SanitizeVideoBaseName(preferredBaseName ?? Path.GetFileNameWithoutExtension(fileName));
        var target = Path.Combine(VideoLibraryRoot, baseName + ext);
        if (!File.Exists(target)) return target;
        for (var i = 2; ; i++)
        {
            target = Path.Combine(VideoLibraryRoot, $"{baseName}_{i}{ext}");
            if (!File.Exists(target)) return target;
        }
    }

    private static string GetNexusVideoBaseName(NexusModMetadata meta) =>
        SanitizeVideoBaseName($"{meta.Name}_{meta.ModId}");

    private static string GetDisabledMoviePath(string moviePath) => moviePath + ".RRModHub.BACKUP";

    private string? FindGameMoviePath(string fileName)
    {
        string gameRoot;
        try { gameRoot = GetVerifiedGameRoot(); } catch { return null; }
        var moviesRoot = Path.Combine(GetGameProjectRoot(gameRoot), "Content", "Movies");
        if (!Directory.Exists(moviesRoot)) return null;

        var direct = Path.Combine(moviesRoot, fileName);
        if (File.Exists(direct)) return direct;
        var directBackup = GetDisabledMoviePath(direct);
        if (File.Exists(directBackup)) return direct;

        var found = Directory.EnumerateFiles(moviesRoot, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(p => !p.EndsWith(".RRModHub.BACKUP", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(found)) return found;
        var backup = Directory.EnumerateFiles(moviesRoot, fileName + ".RRModHub.BACKUP", SearchOption.AllDirectories).FirstOrDefault();
        return string.IsNullOrWhiteSpace(backup) ? null : backup[..^".RRModHub.BACKUP".Length];
    }

    private string? FindGameMovieBackup(string fileName)
    {
        string gameRoot;
        try { gameRoot = GetVerifiedGameRoot(); } catch { return null; }
        var moviesRoot = Path.Combine(GetGameProjectRoot(gameRoot), "Content", "Movies");
        if (!Directory.Exists(moviesRoot)) return null;
        var backupName = fileName + ".RRModHub.BACKUP";
        var direct = Path.Combine(moviesRoot, backupName);
        if (File.Exists(direct)) return direct;
        return Directory.EnumerateFiles(moviesRoot, backupName, SearchOption.AllDirectories).FirstOrDefault();
    }

    private void ImportVideoFile(string source)
    {
        if (!File.Exists(source)) return;
        var ext = Path.GetExtension(source);
        var nexus = GetNexusMetadataForVideoSource(source);
        var nexusBase = nexus != null ? GetNexusVideoBaseName(nexus) : null;
        var metadata = LoadNexusMetadata();

        if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            var target = GetUniqueVideoPath(Path.GetFileName(source), nexusBase);
            File.Copy(source, target, false);
            if (nexus != null)
            {
                metadata[VideoMetadataKey(target)] = nexus with { ArchivePath = Path.GetFileName(source) };
                SaveNexusMetadata(metadata);
            }
            return;
        }
        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(source);
            var zipBase = Path.GetFileNameWithoutExtension(source);
            var preferredBase = nexusBase ?? zipBase;
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith("/", StringComparison.Ordinal)))
            {
                ValidateZipEntry(entry.FullName);
                var target = GetUniqueVideoPath(Path.GetFileName(entry.FullName), preferredBase);
                entry.ExtractToFile(target, false);
                if (nexus != null) metadata[VideoMetadataKey(target)] = nexus with { ArchivePath = Path.GetFileName(source) };
            }
            if (nexus != null)
            {
                SaveNexusMetadata(metadata);
            }
            return;
        }
        throw new InvalidOperationException(L("Only MP4 and ZIP video files are supported."));
    }

    private void RefreshVideosPage()
    {
        if (VideosSlotsPanel == null || VideosLibraryPanel == null) return;
        BeginVideoLibraryRefresh();
        RefreshVideosPageCore();
    }

    private void RefreshVideosPageCore()
    {
        if (VideosSlotsPanel == null || VideosLibraryPanel == null) return;
        _refreshingVideosUi = true;
        try
        {
            VideosLibraryPanel.Children.Clear();
            VideosSlotsPanel.Children.Clear();
            Directory.CreateDirectory(VideoLibraryRoot);
            var library = _cachedVideoLibrary ?? new List<string>();
            var replacements = LoadVideoReplacements();

            if (_selectedVideoLibraryFile != null && !library.Any(p => string.Equals(Path.GetFileName(p), _selectedVideoLibraryFile, StringComparison.OrdinalIgnoreCase)))
                _selectedVideoLibraryFile = null;

            foreach (var file in library)
                AddVideoLibraryRow(file);

            VideosFolderText.Text = L("Videos Folder: {0}", VideoLibraryRoot);
            VideosStatus.Text = library.Count == 0
                ? L("Drop MP4 or ZIP files here to build your video library.")
                : L("{0} installed video(s). Select one in a game slot on the right.", library.Count);

            foreach (var slot in VideoSlotNames)
            {
                var card = new Border
                {
                    Style = (Style)Resources["CardStyle"],
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var active = replacements.TryGetValue(slot, out var replacement) && !string.IsNullOrWhiteSpace(replacement.CustomFile);
                var selectedName = active ? replacement!.CustomFile : "";

                var slotText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                slotText.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(slot), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                slotText.Children.Add(new TextBlock { Text = slot, Foreground = (Brush)Resources["SecondaryBrush"], FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(slotText, 0);
                grid.Children.Add(slotText);

                var controls = new Grid { Margin = new Thickness(12, 0, 0, 0) };
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var check = new CheckBox
                {
                    IsChecked = active,
                    IsEnabled = active || library.Count > 0,
                    Style = (Style)Resources["TransferToggleStyle"],
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = L("Enable or disable this video replacement")
                };
                Grid.SetColumn(check, 0);
                controls.Children.Add(check);

                var combo = new ComboBox
                {
                    Height = 34,
                    Style = (Style)Resources["SettingsComboBoxStyle"],
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 180
                };
                combo.Items.Add(new ComboBoxItem { Content = L("None"), Tag = "" });
                foreach (var file in library)
                    combo.Items.Add(new ComboBoxItem { Content = Path.GetFileName(file), Tag = Path.GetFileName(file) });
                var selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(selectedName))
                {
                    for (var i = 1; i < combo.Items.Count; i++)
                    {
                        if (string.Equals(((ComboBoxItem)combo.Items[i]).Tag as string, selectedName, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }
                combo.SelectedIndex = selectedIndex;
                Grid.SetColumn(combo, 2);
                controls.Children.Add(combo);

                var ui = new VideoSlotUi(slot, check, combo);
                check.Tag = ui;
                combo.Tag = ui;
                check.Checked += VideoToggle_Changed;
                check.Unchecked += VideoToggle_Changed;
                combo.SelectionChanged += VideoSelection_Changed;

                Grid.SetColumn(controls, 1);
                grid.Children.Add(controls);
                card.Child = grid;
                VideosSlotsPanel.Children.Add(card);
            }
        }
        finally
        {
            _refreshingVideosUi = false;
        }
    }

    private void AddVideoLibraryRow(string file)
    {
        var name = Path.GetFileName(file);
        var row = new Border
        {
            Style = (Style)Resources["CardStyle"],
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(name), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = name, Foreground = (Brush)Resources["SecondaryBrush"], FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(stack, 0);
        grid.Children.Add(stack);
        var size = new FileInfo(file).Length;
        var sizeText = new TextBlock { Text = FormatBytes(size), Foreground = (Brush)Resources["SecondaryBrush"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        Grid.SetColumn(sizeText, 1);
        grid.Children.Add(sizeText);
        row.Child = grid;
        row.Tag = file;
        row.MouseLeftButtonUp += (_, _) =>
        {
            var meta = LoadNexusMetadata().GetValueOrDefault(VideoMetadataKey(file));
            if (meta != null && meta.ModId > 0 && !string.IsNullOrWhiteSpace(meta.Game))
                OpenUrl($"https://www.nexusmods.com/{meta.Game}/mods/{meta.ModId}");
        };
        var contextButton = new Button
        {
            Content = "⋮", Width = 34, Height = 34, Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)Resources["ModIconButtonStyle"], Tag = file, ToolTip = L("Mod options")
        };
        contextButton.Click += VideoContextButton_Click;
        Grid.SetColumn(contextButton, 2);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(contextButton);
        row.ContextMenu = null;
        VideosLibraryPanel.Children.Add(row);
    }

    private void VideoContextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string file) return;
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(L("Delete"), (_, _) => DeleteVideoFile(file)));
        menu.Items.Add(MenuItem(L("Change Name"), (_, _) => ChangeVideoName(file)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(L("Open Mod Folder"), (_, _) => OpenFolder(VideoLibraryRoot)));
        var nexus = LoadNexusMetadata().GetValueOrDefault(VideoMetadataKey(file));
        if (nexus != null && nexus.ModId > 0 && !string.IsNullOrWhiteSpace(nexus.Game))
        {
            menu.Items.Add(MenuItem(L("Open Nexus Page"), (_, _) => OpenUrl($"https://www.nexusmods.com/{nexus.Game}/mods/{nexus.ModId}")));
            menu.Items.Add(MenuItem(L("Unlink Nexus"), (_, _) => UnlinkVideoNexus(file)));
        }
        else
        {
            menu.Items.Add(MenuItem(L("Link to Nexus"), (_, _) => LinkVideoToNexus(file)));
        }
        menu.IsOpen = true;
    }

    private void ChangeVideoName(string file)
    {
        if (!File.Exists(file)) return;
        var oldName = Path.GetFileName(file);
        var input = ShowTextInputDialog(L("Enter the display name for this video:"), L("Change Name"), Path.GetFileNameWithoutExtension(oldName));
        if (string.IsNullOrWhiteSpace(input)) return;
        var newPath = GetUniqueVideoPath(oldName, input.Trim());
        try
        {
            File.Move(file, newPath);
            var metadata = LoadNexusMetadata();
            var oldKey = VideoMetadataKey(file);
            if (metadata.TryGetValue(oldKey, out var meta))
            {
                metadata.Remove(oldKey);
                metadata[VideoMetadataKey(newPath)] = meta;
            }
            var replacements = LoadVideoReplacements();
            foreach (var key in replacements.Keys.ToList())
            {
                var replacement = replacements[key];
                if (string.Equals(replacement.CustomFile, oldName, StringComparison.OrdinalIgnoreCase))
                    replacements[key] = replacement with { CustomFile = Path.GetFileName(newPath) };
            }
            SaveVideoReplacements(replacements);
            SaveNexusMetadata(metadata);
            InvalidateVideoLibraryCache();
        RefreshVideosPage();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("Videos"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UnlinkVideoNexus(string file)
    {
        var metadata = LoadNexusMetadata();
        if (metadata.Remove(VideoMetadataKey(file))) SaveNexusMetadata(metadata);
        InvalidateVideoLibraryCache();
        RefreshVideosPage();
    }

    private async void LinkVideoToNexus(string file)
    {
        var input = ShowTextInputDialog(L("Enter the Retro Rewind Nexus Mods page URL:"), L("Link to Nexus"), "https://www.nexusmods.com/retrorewindvideostoresimulator/mods/");
        if (string.IsNullOrWhiteSpace(input)) return;
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || !uri.Host.Equals("www.nexusmods.com", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, L("Please enter a valid Retro Rewind Nexus Mods URL."), L("Link to Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[^2].Equals("mods", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[^1], out var modId))
        {
            MessageBox.Show(this, L("Please enter a valid Retro Rewind Nexus mod page URL."), L("Link to Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var game = parts[0];
        var name = Path.GetFileNameWithoutExtension(file);
        var meta = new NexusModMetadata(name, game, modId, 0, "");
        try
        {
            var info = await FetchNexusModInfoAsync(game, modId);
            if (info != null) meta = ApplyNexusInfo(meta, info);
        }
        catch { }
        var metadata = LoadNexusMetadata();
        metadata[VideoMetadataKey(file)] = meta;
        SaveNexusMetadata(metadata);
        InvalidateVideoLibraryCache();
        RefreshVideosPage();
    }

    private void RefreshVideosLibrarySelectionVisuals()
    {
        foreach (var child in VideosLibraryPanel.Children.OfType<Border>())
        {
            var file = child.Tag as string;
            child.BorderBrush = string.Equals(Path.GetFileName(file), _selectedVideoLibraryFile, StringComparison.OrdinalIgnoreCase)
                ? (Brush)Resources["AccentBrush"]
                : (Brush)Resources["BorderBrush"];
            child.BorderThickness = new Thickness(1);
        }
    }

    private sealed record VideoSlotUi(string Slot, CheckBox Enabled, ComboBox Selection);

    private async void VideoSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingVideosUi || sender is not ComboBox combo || combo.Tag is not VideoSlotUi ui) return;
        try
        {
            var item = combo.SelectedItem as ComboBoxItem;
            var customFile = item?.Tag as string ?? "";
            if (string.IsNullOrWhiteSpace(customFile))
            {
                var replacements = LoadVideoReplacements();
                SetOperationBusy(true, L("Restoring vanilla video…"));
                await Task.Yield();
                await Task.Run(() => DisableVideoReplacement(ui.Slot, replacements));
                SaveVideoReplacements(replacements);
                ui.Enabled.IsChecked = false;
                ui.Enabled.IsEnabled = false;
                SetOperationBusy(true, L("Loading videos…"));
                await Task.Yield();
                InvalidateVideoLibraryCache();
        RefreshVideosPage();
                SetOperationBusy(false);
            }
            else
            {
                ui.Enabled.IsEnabled = true;
                var replacements = LoadVideoReplacements();
                ui.Enabled.IsChecked = replacements.TryGetValue(ui.Slot, out var current) && string.Equals(current.CustomFile, customFile, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, ex.Message, L("Videos"), MessageBoxButton.OK, MessageBoxImage.Error);
            InvalidateVideoLibraryCache();
        RefreshVideosPage();
        }
    }

    private async void VideoToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshingVideosUi || sender is not CheckBox check || check.Tag is not VideoSlotUi ui) return;
        try
        {
            var item = ui.Selection.SelectedItem as ComboBoxItem;
            var customFile = item?.Tag as string ?? "";
            var replacements = LoadVideoReplacements();
            SetOperationBusy(true, check.IsChecked == true ? L("Installing video replacement…") : L("Restoring vanilla video…"));
            await Task.Yield();
            if (check.IsChecked == true && !string.IsNullOrWhiteSpace(customFile))
            {
                var customPath = Path.Combine(VideoLibraryRoot, customFile);
                if (!File.Exists(customPath)) throw new FileNotFoundException(L("The selected custom video could not be found."), customPath);
                await Task.Run(() => ActivateVideoReplacement(ui.Slot, customFile, replacements));
                SaveVideoReplacements(replacements);
            }
            else
            {
                await Task.Run(() => DisableVideoReplacement(ui.Slot, replacements));
                SaveVideoReplacements(replacements);
                check.IsChecked = false;
            }
            SetOperationBusy(true, L("Loading videos…"));
            await Task.Yield();
            InvalidateVideoLibraryCache();
        RefreshVideosPage();
            SetOperationBusy(false);
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, ex.Message, L("Videos"), MessageBoxButton.OK, MessageBoxImage.Error);
            InvalidateVideoLibraryCache();
        RefreshVideosPage();
        }
    }

    private async void DeleteVideoFile(string file)
    {
        if (!File.Exists(file)) return;
        var name = Path.GetFileName(file);
        var replacements = LoadVideoReplacements();
        var activeSlot = replacements.FirstOrDefault(x => string.Equals(x.Value.CustomFile, name, StringComparison.OrdinalIgnoreCase)).Key;
        if (!string.IsNullOrWhiteSpace(activeSlot))
        {
            MessageBox.Show(this, L("This video is currently assigned to '{0}'. Select None for that slot before deleting it.", activeSlot), L("Videos"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show(this, L("Delete video '{0}'?\n\nThis cannot be undone.", name), L("Delete Video"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            SetOperationBusy(true, L("Deleting {0}…", name));
            await Task.Yield();
            await Task.Run(() => File.Delete(file));
            if (string.Equals(_selectedVideoLibraryFile, name, StringComparison.OrdinalIgnoreCase)) _selectedVideoLibraryFile = null;
            SetOperationBusy(true, L("Loading installed videos…"));
            await Task.Yield();
            InvalidateVideoLibraryCache();
        RefreshVideosPage();
            SetOperationBusy(false);
        }
        catch (Exception ex) { SetOperationBusy(false); MessageBox.Show(this, ex.Message, L("Videos"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void VideosOpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenFolder(VideoLibraryRoot);

    private void VideosDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedVideoLibraryFile)) return;
        DeleteVideoFile(Path.Combine(VideoLibraryRoot, _selectedVideoLibraryFile));
    }

    private void ActivateVideoReplacement(string slot, string customFile, Dictionary<string, VideoReplacement> replacements)
    {
        var customPath = Path.Combine(VideoLibraryRoot, customFile);
        if (!File.Exists(customPath)) throw new FileNotFoundException(L("The selected custom video could not be found."));

        var existing = replacements.GetValueOrDefault(slot);
        var gamePath = !string.IsNullOrWhiteSpace(existing?.GameRelativePath)
            ? ResolveGameRelativeMovie(existing!.GameRelativePath)
            : FindGameMoviePath(slot);
        if (string.IsNullOrWhiteSpace(gamePath))
            throw new InvalidOperationException(L("Could not find the vanilla movie '{0}' in the Retro Rewind installation.", slot));

        var backup = GetDisabledMoviePath(gamePath);
        if (!File.Exists(backup))
        {
            if (IsSymbolicLink(gamePath))
                throw new InvalidOperationException(L("The vanilla movie '{0}' has not been backed up and the active file is a symbolic link.", slot));
            if (File.Exists(gamePath))
                File.Move(gamePath, backup);
            else
                throw new FileNotFoundException(L("The vanilla movie '{0}' could not be found.", slot));
        }
        else if (File.Exists(gamePath) || IsSymbolicLink(gamePath))
        {
            File.Delete(gamePath);
        }

        CreateSymbolicLinkWithElevation(customPath, gamePath);

        var gameRoot = GetVerifiedGameRoot();
        var relative = Path.GetRelativePath(GetGameProjectRoot(gameRoot), gamePath);
        replacements[slot] = new VideoReplacement(customFile, relative);
    }

    private string? ResolveGameRelativeMovie(string relative)
    {
        try
        {
            var root = GetGameProjectRoot(GetVerifiedGameRoot());
            var path = Path.GetFullPath(Path.Combine(root, relative));
            var moviesRoot = Path.GetFullPath(Path.Combine(root, "Content", "Movies"));
            if (path.StartsWith(moviesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && (File.Exists(path) || IsSymbolicLink(path) || File.Exists(GetDisabledMoviePath(path))))
                return File.Exists(path) || IsSymbolicLink(path) ? path : GetDisabledMoviePath(path).Replace(".RRModHub.BACKUP", "");
        }
        catch { }
        return null;
    }

    private void DisableVideoReplacement(string slot, Dictionary<string, VideoReplacement> replacements)
    {
        var replacement = replacements.GetValueOrDefault(slot);
        var gamePath = !string.IsNullOrWhiteSpace(replacement?.GameRelativePath)
            ? ResolveGameRelativeMovie(replacement!.GameRelativePath)
            : FindGameMoviePath(slot);
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            var backupOnly = FindGameMovieBackup(slot);
            if (!string.IsNullOrWhiteSpace(backupOnly))
                File.Move(backupOnly, backupOnly[..^".RRModHub.BACKUP".Length], true);
            replacements.Remove(slot);
            return;
        }

        var backup = GetDisabledMoviePath(gamePath);
        if (File.Exists(gamePath) || IsSymbolicLink(gamePath)) File.Delete(gamePath);
        if (File.Exists(backup)) File.Move(backup, gamePath, true);
        replacements.Remove(slot);
    }

    private void RefreshVideoEditorUi()
    {
        if (VideoEditorInputText == null) return;
        var hasVideo = !string.IsNullOrWhiteSpace(_videoEditorInputFile) && File.Exists(_videoEditorInputFile);
        // Keep the preview surface mounted while LibVLC/MediaElement is
        // initializing. Collapsing the Border during MediaOpened causes WPF
        // MediaElement to unload and cancel playback, which looks like the video
        // briefly appearing and then disappearing.
        var showPreview = hasVideo && (_videoEditorPreviewLoaded ||
                                       _videoEditorPreviewPreparing ||
                                       _videoEditorUsingFallbackMediaElement);
        VideoEditorInputText.Text = hasVideo ? Path.GetFileName(_videoEditorInputFile) : L("No video selected");
        UpdateVideoEditorAccelerationStatus();
        VideoEditorConvertButton.IsEnabled = hasVideo && !_operationBusy;

        // The playback surface and transport controls only exist while a media
        // item is actually loaded. During source selection/loading the working
        // dialog is enough feedback; leaving the old preview surface visible
        // makes it look as if the old video is still alive.
        VideoEditorPreviewBorder.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        // Once a source has been selected, the drop target is gone immediately.
        // It must not remain visible while the real-time preview is preparing.
        VideoEditorDropBorder.Visibility = hasVideo ? Visibility.Collapsed : Visibility.Visible;
        VideoEditorClearButton.Visibility = hasVideo ? Visibility.Visible : Visibility.Collapsed;
        VideoEditorPlayButton.IsEnabled = showPreview && !_videoEditorPreviewPreparing;
        VideoEditorStopButton.IsEnabled = showPreview && !_videoEditorPreviewPreparing;
        UpdateVideoEditorTransportButton();
        VideoEditorTimelineSlider.IsEnabled = showPreview && !_videoEditorPreviewPreparing && _videoEditorPreviewDuration > TimeSpan.Zero;
        if (hasVideo && !_videoEditorPreviewLoaded && !_videoEditorPreviewPreparing && !_videoEditorPreviewError) _ = PrepareVideoEditorPreviewAsync(_videoEditorInputFile!);
        UpdateVideoEditorPreviewEffects();
        if (VideoEditorScanlineValue != null) VideoEditorScanlineValue.Text = $"{VideoEditorScanlineSlider.Value:0}%";
        if (VideoEditorVignetteValue != null) VideoEditorVignetteValue.Text = $"{VideoEditorVignetteSlider.Value:0}%";
        if (VideoEditorChromaValue != null) VideoEditorChromaValue.Text = $"{VideoEditorChromaSlider.Value:0}px";
        if (VideoEditorFlickerValue != null) VideoEditorFlickerValue.Text = $"{VideoEditorFlickerSlider.Value:0}%";
        if (VideoEditorTearValue != null) VideoEditorTearValue.Text = $"{VideoEditorTearSlider.Value:0}%";
        if (VideoEditorHueValue != null) VideoEditorHueValue.Text = $"{VideoEditorHueSlider.Value:0}°";
    }

    private string? FindFfmpeg()
    {
        var bundled = Path.Combine(ToolsDirectory, "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var candidate = Path.Combine(dir.Trim('"'), "ffmpeg.exe"); if (File.Exists(candidate)) return candidate; } catch { }
        }
        return null;
    }

    private string? FindYtDlp()
    {
        var names = new[] { Path.Combine(ToolsDirectory, "yt-dlp.exe"), Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe") };
        foreach (var n in names) if (File.Exists(n)) return n;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var candidate = Path.Combine(dir.Trim('"'), "yt-dlp.exe"); if (File.Exists(candidate)) return candidate; } catch { }
        }
        return null;
    }

    private async Task CheckRequiredFilesOnStartupAsync()
    {
        try
        {
            await EnsureAllExternalToolsAsync();
            var allRequiredFilesReady = AreRequiredVideoEditorFilesAvailable();
            await Dispatcher.InvokeAsync(() => UpdateVideoEditorNavigation(allRequiredFilesReady));
            if (!_enableWindowsNotifications) return;

            var latestLib = await GetLatestNuGetVersionAsync();
            if (File.Exists(Path.Combine(LibVlcToolsDirectory, "libvlc.dll")) && IsVersionNewer(LibVlcVersion, latestLib))
                ShowWindowsNotification(L("LibVLC update available"), L("Open Required Files to update the video playback engine."));

            var ytdlp = FindYtDlp();
            if (ytdlp != null)
            {
                var latestYt = await GetLatestGithubTagAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");
                var installedYt = await GetExecutableVersionAsync(ytdlp, "--version");
                if (IsVersionNewer(installedYt, latestYt))
                    ShowWindowsNotification(L("yt-dlp update available"), L("Open Required Files to update yt-dlp."));
            }
        }
        catch { }
    }

    private bool AreRequiredVideoEditorFilesAvailable()
    {
        return FindFfmpeg() != null && FindYtDlp() != null &&
               File.Exists(Path.Combine(LibVlcToolsDirectory, "libvlc.dll")) &&
               File.Exists(Path.Combine(LibVlcToolsDirectory, "libvlccore.dll")) &&
               Directory.Exists(Path.Combine(LibVlcToolsDirectory, "plugins"));
    }

    private void UpdateVideoEditorNavigation(bool allRequiredFilesReady)
    {
        if (RequiredFilesTab == null || VideoEditorTab == null) return;

        // Required Files is always available so users can inspect, repair, or
        // re-download any external dependency. Video Editor is enabled once its
        // own playback dependencies are installed.
        RequiredFilesTab.Visibility = Visibility.Visible;
        VideoEditorTab.Visibility = allRequiredFilesReady ? Visibility.Visible : Visibility.Collapsed;

        // Never leave the current mode pointing at a page whose sidebar entry
        // is no longer available after a dependency check completes.
        if (!allRequiredFilesReady && _mode == "videoeditor")
        {
            _mode = "requiredfiles";
            UpdateMode();
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d):0.0} MB";
        return $"{bytes / (1024d * 1024d * 1024d):0.00} GB";
    }

    private void UpdateRequiredFileCardProgress(string toolName, int percent, long downloaded, long? total, double bytesPerSecond)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateRequiredFileCardProgress(toolName, percent, downloaded, total, bytesPerSecond)));
            return;
        }

        ProgressBar? bar = toolName switch
        {
            "LibVLC" => RequiredLibVlcProgress,
            "FFmpeg" => RequiredFfmpegProgress,
            "yt-dlp" => RequiredYtDlpProgress,
            "Real-ESRGAN" => RequiredRealEsrganProgress,
            "repak" => RequiredRepakProgress,
            "texconv" => RequiredTexconvProgress,
            _ => null
        };
        TextBlock? status = toolName switch
        {
            "LibVLC" => RequiredLibVlcStatus,
            "FFmpeg" => RequiredFfmpegStatus,
            "yt-dlp" => RequiredYtDlpStatus,
            "Real-ESRGAN" => RequiredRealEsrganStatus,
            "repak" => RequiredRepakStatus,
            "texconv" => RequiredTexconvStatus,
            _ => null
        };
        if (bar == null || status == null) return;

        bar.Visibility = Visibility.Visible;
        bar.Value = percent;
        var totalText = total.HasValue ? $" / {FormatBytes(total.Value)}" : "";
        status.Text = L("Downloading… {0}% — {1}{2} — {3}/s", percent, FormatBytes(downloaded), totalText, FormatBytes((long)Math.Max(0, bytesPerSecond)));
        try
        {
            var toolProgress = percent / 100.0;
            var baseProgress = toolName switch { "FFmpeg" => 87, "yt-dlp" => 89, "LibVLC" => 91, "Real-ESRGAN" => 93, "repak" => 95, _ => 87 };
            App.GetStartupSplash()?.SetStatus($"Downloading {toolName}… {percent}%", baseProgress + toolProgress * 2);
        }
        catch { }
    }

    private void SetRequiredFileCardState(string toolName, string statusText, string buttonText, bool enabled, bool showProgress = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => SetRequiredFileCardState(toolName, statusText, buttonText, enabled, showProgress)));
            return;
        }
        TextBlock? status = toolName switch
        {
            "LibVLC" => RequiredLibVlcStatus,
            "FFmpeg" => RequiredFfmpegStatus,
            "yt-dlp" => RequiredYtDlpStatus,
            "Real-ESRGAN" => RequiredRealEsrganStatus,
            "repak" => RequiredRepakStatus,
            "texconv" => RequiredTexconvStatus,
            _ => null
        };
        ProgressBar? bar = toolName switch
        {
            "LibVLC" => RequiredLibVlcProgress,
            "FFmpeg" => RequiredFfmpegProgress,
            "yt-dlp" => RequiredYtDlpProgress,
            "Real-ESRGAN" => RequiredRealEsrganProgress,
            "repak" => RequiredRepakProgress,
            "texconv" => RequiredTexconvProgress,
            _ => null
        };
        Button? button = toolName switch
        {
            "LibVLC" => RequiredLibVlcButton,
            "FFmpeg" => RequiredFfmpegButton,
            "yt-dlp" => RequiredYtDlpButton,
            "Real-ESRGAN" => RequiredRealEsrganButton,
            "repak" => RequiredRepakButton,
            "texconv" => RequiredTexconvButton,
            _ => null
        };
        if (status != null) status.Text = statusText;
        try { App.GetStartupSplash()?.SetStatus(statusText, showProgress ? 92 : 98); } catch { }
        if (button != null) { button.Content = buttonText; button.IsEnabled = enabled && !_operationBusy; }
        if (bar != null)
        {
            bar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
            bar.IsIndeterminate = showProgress && (statusText.Contains("Installing", StringComparison.OrdinalIgnoreCase) || statusText.Contains("Verifying", StringComparison.OrdinalIgnoreCase) || statusText.Contains("Preparing", StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task<string> GetExecutableVersionAsync(string? executable, string argument)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return "Not installed";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            if (process == null) return "Unknown";
            var output = await process.StandardOutput.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(output)) output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (argument == "--version") return line.Trim();
            var match = Regex.Match(line, @"version\s+([^\s]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : line.Trim();
        }
        catch { return "Unknown"; }
    }

    private async Task<string?> GetLatestGithubTagAsync(string apiUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0");
            using var doc = JsonDocument.Parse(await client.GetStringAsync(apiUrl));
            return doc.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        }
        catch { return null; }
    }

    private async Task<string?> GetLatestNuGetVersionAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var doc = JsonDocument.Parse(await client.GetStringAsync("https://api.nuget.org/v3-flatcontainer/videolan.libvlc.windows/index.json"));
            if (!doc.RootElement.TryGetProperty("versions", out var versions)) return null;
            var values = versions.EnumerateArray().Select(v => v.GetString()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            return values.LastOrDefault();
        }
        catch { return null; }
    }

    private static bool IsVersionNewer(string? installed, string? latest)
    {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(latest)) return false;
        var clean = (string value) => Regex.Match(value, @"\d+(?:\.\d+){1,3}").Value;
        if (!Version.TryParse(clean(installed), out var a) || !Version.TryParse(clean(latest), out var b)) return false;
        return b > a;
    }

    private async Task RefreshRequiredFilesPage()
    {
        if (RequiredFilesGrid == null) return;
        var ffmpeg = FindFfmpeg();
        var ytdlp = FindYtDlp();
        var libvlcInstalled = File.Exists(Path.Combine(LibVlcToolsDirectory, "libvlc.dll")) &&
                              File.Exists(Path.Combine(LibVlcToolsDirectory, "libvlccore.dll")) &&
                              Directory.Exists(Path.Combine(LibVlcToolsDirectory, "plugins"));

        RequiredFfmpegStatus.Text = ffmpeg == null ? L("Status: Missing") : L("Status: Installed — Version: {0}", await GetExecutableVersionAsync(ffmpeg, "-version"));
        RequiredYtDlpStatus.Text = ytdlp == null ? L("Status: Missing") : L("Status: Installed — Version: {0}", await GetExecutableVersionAsync(ytdlp, "--version"));
        var realEsrganInstalled = File.Exists(Path.Combine(ToolsDirectory, "RealESRGAN", "realesrgan-ncnn-vulkan.exe"));
        var repakInstalled = File.Exists(Path.Combine(ToolsDirectory, "repak.exe"));
        var texconvInstalled = File.Exists(Path.Combine(ToolsDirectory, "texconv.exe"));
        RequiredLibVlcStatus.Text = libvlcInstalled ? L("Status: Installed — Version: {0}", LibVlcVersion) : L("Status: Missing");
        RequiredRealEsrganStatus.Text = realEsrganInstalled ? L("Status: Installed") : L("Status: Missing");
        RequiredRepakStatus.Text = repakInstalled ? L("Status: Installed — Version: 0.2.3") : L("Status: Missing");
        RequiredTexconvStatus.Text = texconvInstalled ? L("Status: Installed") : L("Status: Missing");
        RequiredFfmpegButton.Content = ffmpeg == null ? L("Download") : L("ReDownload");
        RequiredYtDlpButton.Content = ytdlp == null ? L("Download") : L("ReDownload");
        RequiredLibVlcButton.Content = libvlcInstalled ? L("ReDownload") : L("Download");
        RequiredRealEsrganButton.Content = realEsrganInstalled ? L("ReDownload") : L("Download");
        RequiredRepakButton.Content = repakInstalled ? L("ReDownload") : L("Download");
        RequiredTexconvButton.Content = texconvInstalled ? L("ReDownload") : L("Download");
        RequiredFfmpegButton.IsEnabled = !_operationBusy;
        RequiredYtDlpButton.IsEnabled = !_operationBusy;
        RequiredLibVlcButton.IsEnabled = !_operationBusy;
        RequiredRealEsrganButton.IsEnabled = !_operationBusy;
        RequiredRepakButton.IsEnabled = !_operationBusy;
        RequiredTexconvButton.IsEnabled = !_operationBusy;
        var allRequiredFilesReady = AreRequiredVideoEditorFilesAvailable();
        var allExternalToolsReady = allRequiredFilesReady && realEsrganInstalled && repakInstalled && texconvInstalled;
        UpdateVideoEditorNavigation(allRequiredFilesReady);
        if (allExternalToolsReady) _requiredFilesInstallError = false;
        RequiredFilesOverallStatus.Text = _requiredFilesInstallError
            ? L("A required file could not be installed. Fix the error above and try again.")
            : allExternalToolsReady
                ? L("All required external tools are installed and ready. Video Editor and Asset Workshop are available.")
                : L("One or more external tools are missing. ModHub will download missing tools automatically at startup.");
        StatusText.Text = _requiredFilesInstallError
            ? L("A required file could not be installed. Fix the error above and try again.")
            : L("Required files");
        if (_requiredFilesUpdateCheckInProgress) return;
        _requiredFilesUpdateCheckInProgress = true;
        try
        {
            var latestLib = await GetLatestNuGetVersionAsync();
            if (libvlcInstalled && IsVersionNewer(LibVlcVersion, latestLib))
            {
                RequiredLibVlcStatus.Text = L("Update available — Installed: {0} | Latest: {1}", LibVlcVersion, latestLib);
                RequiredLibVlcButton.Content = L("Update");
                if (_enableWindowsNotifications) ShowWindowsNotification(L("LibVLC update available"), L("A newer LibVLC playback engine is available in Required Files."));
            }

            var latestYt = await GetLatestGithubTagAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");
            if (ytdlp != null)
            {
                var installedYt = await GetExecutableVersionAsync(ytdlp, "--version");
                if (IsVersionNewer(installedYt, latestYt))
                {
                    RequiredYtDlpStatus.Text = L("Update available — Installed: {0} | Latest: {1}", installedYt, latestYt);
                    RequiredYtDlpButton.Content = L("Update");
                    if (_enableWindowsNotifications) ShowWindowsNotification(L("yt-dlp update available"), L("A newer yt-dlp version is available in Required Files."));
                }
            }
        }
        finally { _requiredFilesUpdateCheckInProgress = false; }
    }

    private void ShowWindowsNotification(string title, string message)
    {
        if (!_enableWindowsNotifications || _trayIcon == null) return;
        try
        {
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = message;
            _trayIcon.ShowBalloonTip(5000);
        }
        catch { }
    }

    private async Task EnsureAllExternalToolsAsync()
    {
        var tools = new (string Name, Func<Task<string>> Ensure)[]
        {
            ("FFmpeg", EnsureFfmpegAsync),
            ("yt-dlp", EnsureYtDlpAsync),
            ("LibVLC", EnsureLibVlcAsync),
            ("Real-ESRGAN", EnsureRealEsrganAsync),
            ("repak", EnsureRepakAsync),
            ("texconv", EnsureTexconvAsync)
        };
        for (var i = 0; i < tools.Length; i++)
        {
            var item = tools[i];
            SetStartupOverlayStatus("Checking external tools…", 87 + i * 2, $"{item.Name}: checking for an installed copy.");
            try
            {
                await item.Ensure();
                SetRequiredFileCardState(item.Name, L("Ready"), L("ReDownload"), true, false);
            }
            catch (Exception ex)
            {
                SetRequiredFileCardState(item.Name, L("Download failed: {0}", ex.Message), L("Retry"), true, false);
                Debug.WriteLine($"Required tool {item.Name} failed: {ex}");
            }
        }
        SetOperationBusy(false);
        await RefreshRequiredFilesPage();
    }

    private async Task DownloadRequiredFileAsync(string toolName, bool force)
    {
        if (_operationBusy) return;
        try
        {
            if (force)
            {
                if (toolName == "FFmpeg") { var p = FindFfmpeg(); if (p != null && p.StartsWith(ToolsDirectory, StringComparison.OrdinalIgnoreCase)) try { File.Delete(p); } catch { } }
                else if (toolName == "yt-dlp") { var p = FindYtDlp(); if (p != null && p.StartsWith(ToolsDirectory, StringComparison.OrdinalIgnoreCase)) try { File.Delete(p); } catch { } }
                else if (toolName == "Real-ESRGAN") { try { var p = Path.Combine(ToolsDirectory, "RealESRGAN", "realesrgan-ncnn-vulkan.exe"); if (File.Exists(p)) File.Delete(p); } catch { } }
                else if (toolName == "repak") { try { var p = Path.Combine(ToolsDirectory, "repak.exe"); if (File.Exists(p)) File.Delete(p); } catch { } }
                else if (toolName == "texconv") { try { var p = Path.Combine(ToolsDirectory, "texconv.exe"); if (File.Exists(p)) File.Delete(p); } catch { } }
                else if (toolName == "LibVLC") { try { _videoEditorMediaPlayer?.Stop(); _videoEditorMediaPlayer?.Dispose(); _videoEditorMedia?.Dispose(); _videoEditorLibVlc?.Dispose(); } catch { } _videoEditorMediaPlayer = null; _videoEditorMedia = null; _videoEditorLibVlc = null; _videoEditorLibVlcReady = false; try { if (Directory.Exists(LibVlcToolsDirectory)) Directory.Delete(LibVlcToolsDirectory, true); } catch { } }
            }
            SetRequiredFileCardState(toolName, L("Preparing download…"), L("Downloading…"), false, true);
            if (toolName == "FFmpeg") await EnsureFfmpegAsync();
            else if (toolName == "yt-dlp") await EnsureYtDlpAsync();
            else if (toolName == "LibVLC") await EnsureLibVlcAsync();
            else if (toolName == "Real-ESRGAN") await EnsureRealEsrganAsync();
            else if (toolName == "repak") await EnsureRepakAsync();
            else if (toolName == "texconv") await EnsureTexconvAsync();
            SetOperationBusy(false);
            _requiredFilesInstallError = false;
            RefreshRequiredFilesPage();
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            _requiredFilesInstallError = true;
            SetRequiredFileCardState(toolName, L("Download failed: {0}", ex.Message), L("Retry"), true, false);
            RequiredFilesOverallStatus.Text = L("A required file could not be installed. Fix the error above and try again.");
            StatusText.Text = L("A required file could not be installed. Fix the error above and try again.");
        }
    }

    private void RequiredLibVlcButton_Click(object sender, RoutedEventArgs e) => _ = DownloadRequiredFileAsync("LibVLC", RequiredLibVlcButton.Content?.ToString() is "ReDownload" or "Update");

    private void RequiredFfmpegButton_Click(object sender, RoutedEventArgs e) => _ = DownloadRequiredFileAsync("FFmpeg", RequiredFfmpegButton.Content?.ToString() is "ReDownload" or "Update");

    private void RequiredYtDlpButton_Click(object sender, RoutedEventArgs e) => _ = DownloadRequiredFileAsync("yt-dlp", RequiredYtDlpButton.Content?.ToString() is "ReDownload" or "Update");

    private void RequiredRealEsrganButton_Click(object sender, RoutedEventArgs e) => _ = DownloadRequiredFileAsync("Real-ESRGAN", RequiredRealEsrganButton.Content?.ToString() is "ReDownload" or "Update");

    private void RequiredRepakButton_Click(object sender, RoutedEventArgs e) => _ = DownloadRequiredFileAsync("repak", RequiredRepakButton.Content?.ToString() is "ReDownload" or "Update");
    private void RequiredTexconvButton_Click(object sender, RoutedEventArgs e) => _ = DownloadRequiredFileAsync("texconv", RequiredTexconvButton.Content?.ToString() is "ReDownload" or "Update");

    private void RequiredFilesRefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRequiredFilesPage();

    private async Task<string> EnsureFfmpegAsync()
    {
        var existing = FindFfmpeg();
        if (existing != null) return existing;

        Directory.CreateDirectory(ToolsDirectory);
        var workRoot = Path.Combine(ToolsDirectory, ".download_ffmpeg");
        var zipPath = Path.Combine(workRoot, "ffmpeg.zip");
        var extractRoot = Path.Combine(workRoot, "extract");
        try
        {
            Directory.CreateDirectory(workRoot);
            SetOperationBusy(true, L("Downloading FFmpeg…"));
            await DownloadToolFileAsync(FfmpegDownloadUrl, zipPath, "FFmpeg");

            SetOperationBusy(true, L("Verifying FFmpeg download…"));
            await VerifyDownloadedChecksumAsync(zipPath, FfmpegChecksumUrl, "ffmpeg-release-essentials.zip");

            SetOperationBusy(true, L("Installing FFmpeg…"));
            if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true);
            ZipFile.ExtractToDirectory(zipPath, extractRoot, true);
            var ffmpegCandidate = Directory.EnumerateFiles(extractRoot, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffmpegCandidate == null) throw new InvalidOperationException(L("FFmpeg was downloaded, but ffmpeg.exe was not found in the package."));

            var destination = Path.Combine(ToolsDirectory, "ffmpeg.exe");
            File.Copy(ffmpegCandidate, destination, true);
            if (!await VerifyExecutableAsync(destination, "-version"))
            {
                try { File.Delete(destination); } catch { }
                throw new InvalidOperationException(L("The downloaded FFmpeg executable could not be verified."));
            }
            return destination;
        }
        finally
        {
            try { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, true); } catch { }
        }
    }

    private async Task<string> EnsureYtDlpAsync()
    {
        var existing = FindYtDlp();
        if (existing != null) return existing;

        Directory.CreateDirectory(ToolsDirectory);
        var workRoot = Path.Combine(ToolsDirectory, ".download_ytdlp");
        var downloadPath = Path.Combine(workRoot, "yt-dlp.exe");
        try
        {
            Directory.CreateDirectory(workRoot);
            SetOperationBusy(true, L("Downloading yt-dlp…"));
            await DownloadToolFileAsync(YtDlpDownloadUrl, downloadPath, "yt-dlp");

            SetOperationBusy(true, L("Verifying yt-dlp download…"));
            await VerifyDownloadedChecksumAsync(downloadPath, YtDlpChecksumUrl, "yt-dlp.exe");

            SetOperationBusy(true, L("Installing yt-dlp…"));
            var destination = Path.Combine(ToolsDirectory, "yt-dlp.exe");
            var stagedDestination = Path.Combine(ToolsDirectory, $"yt-dlp.{Guid.NewGuid():N}.new");
            try
            {
                await CopyFileWithRetryAsync(downloadPath, stagedDestination, overwrite: false);
                if (!await VerifyExecutableAsync(stagedDestination, "--version"))
                    throw new InvalidOperationException(L("The downloaded yt-dlp executable could not be verified."));
                await ReplaceFileWithRetryAsync(stagedDestination, destination);
            }
            catch
            {
                try { if (File.Exists(stagedDestination)) File.Delete(stagedDestination); } catch { }
                throw;
            }
            return destination;
        }
        finally
        {
            try { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, true); } catch { }
        }
    }

    private async Task DownloadToolFileAsync(string url, string destination, string toolName)
    {
        var temp = destination + ".part";
        try
        {
            if (File.Exists(temp)) File.Delete(temp);
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync();
            {
                await using var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64, true);
                var buffer = new byte[1024 * 128];
                long read = 0;
                var stopwatch = Stopwatch.StartNew();
                int count;
                while ((count = await source.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count));
                    read += count;
                    var elapsedSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                    var speed = read / elapsedSeconds;
                    var percent = total.HasValue && total.Value > 0 ? (int)Math.Clamp(read * 100L / total.Value, 0, 100) : 0;
                    SetOperationBusy(true, L("Downloading {0}… {1}%", toolName, percent));
                    UpdateRequiredFileCardProgress(toolName, percent, read, total, speed);
                }
                stopwatch.Stop();
                await target.FlushAsync();
            } // Ensure the .part file handle is fully closed before Windows is asked to move it.
            await ReplaceFileWithRetryAsync(temp, destination);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    private static async Task<string> ComputeSha256WithRetryAsync(string filePath)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = await SHA256.HashDataAsync(stream);
                return Convert.ToHexString(hash);
            }
            catch (IOException ex) { last = ex; await Task.Delay(250 * (attempt + 1)); }
            catch (UnauthorizedAccessException ex) { last = ex; await Task.Delay(250 * (attempt + 1)); }
        }
        throw new IOException($"Could not read '{filePath}' because it is still in use.", last);
    }

    private static async Task CopyFileWithRetryAsync(string source, string destination, bool overwrite)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output);
                await output.FlushAsync();
                return;
            }
            catch (IOException ex) { last = ex; await Task.Delay(300 * (attempt + 1)); }
            catch (UnauthorizedAccessException ex) { last = ex; await Task.Delay(300 * (attempt + 1)); }
        }
        throw new IOException($"Could not copy '{source}' because it is still in use.", last);
    }

    private static async Task ReplaceFileWithRetryAsync(string source, string destination)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                {
                    try { File.Delete(destination); }
                    catch (IOException) { await Task.Delay(300 * (attempt + 1)); continue; }
                    catch (UnauthorizedAccessException) { await Task.Delay(300 * (attempt + 1)); continue; }
                }
                File.Move(source, destination);
                return;
            }
            catch (IOException ex) { last = ex; await Task.Delay(300 * (attempt + 1)); }
            catch (UnauthorizedAccessException ex) { last = ex; await Task.Delay(300 * (attempt + 1)); }
        }
        throw new IOException($"Could not install '{Path.GetFileName(source)}' because the destination is still in use.", last);
    }

    private void ClearConflictCheckResults()
    {
        _conflictIndex = new List<PakConflictIndexEntry>();
        if (ConflictCheckListPanel != null)
            ConflictCheckListPanel.Children.Clear();
        if (ConflictCheckSummary != null)
            ConflictCheckSummary.Text = "No conflict scan has been run. Press F5 to scan installed PAKs.";
    }
}