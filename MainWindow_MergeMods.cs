using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace RogueUnicorn.StoreTransfer;

public partial class MainWindow
{
    private sealed class MergePakAsset
    {
        public string PakPath { get; init; } = "";
        public string ArchivePath { get; init; } = "";
        public string DisplayName => Path.GetFileNameWithoutExtension(ArchivePath);
        public ToggleButton? Toggle { get; set; }
    }

    private sealed class MergePakGroup
    {
        public string PakPath { get; init; } = "";
        public string PakName => Path.GetFileName(PakPath);
        public List<MergePakAsset> Assets { get; init; } = new();
        public bool IsExpanded { get; set; } = true;
    }

    private readonly List<MergePakGroup> _mergePakGroups = new();
    private bool _mergeModsUpdatingSelection;

    private async void MergeModsSelectPaksButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;

        string initialDirectory;
        try
        {
            // Merge Mods works from ModHub's configured Mod Folder. The PAK
            // workspace is always the PAK subfolder directly beneath it.
            initialDirectory = GetPakVirtualRoot();
            Directory.CreateDirectory(initialDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Merge Mods", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select PAKs To Merge",
            Filter = "Unreal PAK files (*.pak)|*.pak|All files (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = initialDirectory,
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog(this) != true) return;

        var paths = _mergePakGroups.Select(g => Path.GetFullPath(g.PakPath))
            .Concat(dialog.FileNames.Select(Path.GetFullPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        try
        {
            SetOperationBusy(true, "Reading selected PAKs…", null, "Indexing archive assets");
            var groups = await Task.Run(() => ReadMergePakGroups(paths));
            _mergePakGroups.Clear();
            _mergePakGroups.AddRange(groups);
            RebuildMergeModsList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Merge Mods", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetOperationBusy(false);
            UpdateMergeModsButtons();
        }
    }

    private void MergeModsResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;
        _mergePakGroups.Clear();
        RebuildMergeModsList();
    }

    private async void MergeModsBuildButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;

        var selected = _mergePakGroups.SelectMany(g => g.Assets)
            .Where(a => a.Toggle?.IsChecked == true)
            .ToList();
        if (selected.Count == 0) return;

        var name = ShowMergeModNameDialog();
        if (string.IsNullOrWhiteSpace(name)) return;

        name = SanitizePakFolderName(name.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Please enter a valid mod name.", "Merge Mods",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var gameRoot = GetVerifiedGameRoot();
            EnsurePakVirtualStore(gameRoot);

            var familyFolder = Path.Combine(GetPakVirtualRoot(), name);
            var outputPak = Path.Combine(familyFolder, name + "_p.pak");

            if (File.Exists(outputPak))
            {
                var answer = MessageBox.Show(this,
                    $"The mod \"{name}\" already exists.\n\nReplace its PAK?",
                    "Merge Mods", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }

            SetOperationBusy(true, "Building merged PAK…", 0, $"Preparing {selected.Count} selected asset(s)");

            await Task.Run(() => BuildMergedPak(selected, outputPak,
                (status, percent, detail) => SetOperationBusy(true, status, percent, detail)));

            var fullOutput = Path.GetFullPath(outputPak);
            var order = LoadPakLoadOrder()
                .Where(p => !string.Equals(Path.GetFullPath(p), fullOutput, StringComparison.OrdinalIgnoreCase))
                .ToList();
            order.Add(fullOutput);
            SavePakLoadOrder(order);

            var enabled = GetEnabledPakSources(gameRoot).Where(File.Exists)
                .Where(p => !string.Equals(Path.GetFullPath(p), fullOutput, StringComparison.OrdinalIgnoreCase))
                .ToList();
            enabled.Add(fullOutput);
            RebuildPakLinks(gameRoot, enabled, forceSingleElevation: true);

            MessageBox.Show(this,
                $"Merged PAK installed successfully.\n\n{Path.GetFileName(outputPak)}",
                "Merge Mods", MessageBoxButton.OK, MessageBoxImage.Information);

            RefreshModManager();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Merge Mods", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetOperationBusy(false);
            UpdateMergeModsButtons();
        }
    }

    private List<MergePakGroup> ReadMergePakGroups(IEnumerable<string> paths)
    {
        var result = new List<MergePakGroup>();
        foreach (var pak in paths)
        {
            if (!File.Exists(pak)) continue;

            using var stream = File.OpenRead(pak);
            var reader = CreateAssetPakReader(stream);
            var assets = GetReaderFiles(reader)
                .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                .Select(p => new MergePakAsset
                {
                    PakPath = pak,
                    ArchivePath = p.Replace('\\', '/')
                })
                .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.Add(new MergePakGroup { PakPath = pak, Assets = assets });
        }

        return result.OrderBy(g => g.PakName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void RebuildMergeModsList()
    {
        if (MergeModsList == null) return;

        _mergeModsUpdatingSelection = true;
        try
        {
            MergeModsList.Items.Clear();

            foreach (var group in _mergePakGroups)
            {
                // This deliberately follows the installed PAK Mods list structure:
                // group header + indented child rows, with the same BrowseButtonStyle,
                // row heights, spacing and chevron treatment. The merger removes only
                // the PAK manager-specific drag/enable/context controls and adds the
                // requested selection toggle to each asset row.
                var outer = new Grid
                {
                    Margin = new Thickness(0, 3, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinWidth = 0
                };
                outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var groupRow = new Grid
                {
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinWidth = 0
                };
                groupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28), MinWidth = 28 });
                groupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
                groupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170), MinWidth = 120 });

                var expanded = group.IsExpanded;
                var chevron = new TextBlock
                {
                    Text = expanded ? "⌄" : "›",
                    FontSize = 22,
                    Width = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Resources["SecondaryBrush"]
                };
                Grid.SetColumn(chevron, 0);
                groupRow.Children.Add(chevron);

                var nameButton = new Button
                {
                    Content = new TextBlock
                    {
                        Text = group.PakName,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    Height = 36,
                    MinWidth = 0,
                    Style = (Style)Resources["BrowseButtonStyle"],
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ToolTip = group.PakPath
                };
                Grid.SetColumn(nameButton, 1);
                groupRow.Children.Add(nameButton);

                var countText = new TextBlock
                {
                    Text = $"{group.Assets.Count:N0} assets",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(8, 0, 8, 0),
                    Foreground = (Brush)Resources["SecondaryBrush"]
                };
                Grid.SetColumn(countText, 2);
                groupRow.Children.Add(countText);

                var children = new StackPanel
                {
                    Visibility = expanded ? Visibility.Visible : Visibility.Collapsed,
                    Margin = new Thickness(18, 2, 0, 0)
                };

                foreach (var asset in group.Assets)
                {
                    var row = new Grid
                    {
                        Margin = new Thickness(0, 2, 0, 2),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MinWidth = 0,
                        Background = Brushes.Transparent
                    };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40), MinWidth = 40 });

                    var button = new Button
                    {
                        Content = asset.DisplayName,
                        Style = (Style)Resources["BrowseButtonStyle"],
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Height = 36,
                        Tag = asset,
                        ToolTip = asset.ArchivePath
                    };
                    button.Click += async (_, _) => await ShowMergeAssetInfoAsync(asset);
                    Grid.SetColumn(button, 0);
                    row.Children.Add(button);

                    var toggle = new ToggleButton
                    {
                        Width = 34,
                        Height = 34,
                        Margin = new Thickness(6, 0, 0, 0),
                        Style = (Style)Resources["MergeAssetToggleStyle"],
                        Tag = asset,
                        ToolTip = "Select asset for merged PAK"
                    };
                    toggle.Checked += MergeAssetToggle_Checked;
                    toggle.Unchecked += MergeAssetToggle_Unchecked;
                    asset.Toggle = toggle;
                    Grid.SetColumn(toggle, 1);
                    row.Children.Add(toggle);
                    children.Children.Add(row);
                }

                Grid.SetRow(groupRow, 0);
                Grid.SetRow(children, 1);
                outer.Children.Add(groupRow);
                outer.Children.Add(children);

                nameButton.Click += (_, _) =>
                {
                    group.IsExpanded = !group.IsExpanded;
                    children.Visibility = group.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
                    chevron.Text = group.IsExpanded ? "⌄" : "›";
                };

                MergeModsList.Items.Add(outer);
            }
        }
        finally
        {
            _mergeModsUpdatingSelection = false;
        }

        MergeModsStatus.Text = _mergePakGroups.Count == 0
            ? "Select one or more PAK files from the game's ~mods folder."
            : $"{_mergePakGroups.Count} PAK(s) loaded • {_mergePakGroups.Sum(g => g.Assets.Count):N0} assets indexed";

        UpdateMergeModsButtons();
    }

    private async Task ShowMergeAssetInfoAsync(MergePakAsset asset)
    {
        if (asset == null) return;

        MergeModsInfoTitle.Text = asset.DisplayName;
        MergeModsInfoPath.Text = asset.ArchivePath;
        MergeModsInfoTexturePreview.Source = null;
        MergeModsInfoTexturePreview.Visibility = Visibility.Collapsed;
        MergeModsInfoPlaceholder.Visibility = Visibility.Visible;
        MergeModsInfoPlaceholder.Text = "Loading preview…";

        if (!asset.ArchivePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
        {
            MergeModsInfoPlaceholder.Text = "Select a texture asset to preview it.";
            return;
        }

        try
        {
            var previewPath = await Task.Run(() => DecodeTextureAssetPreview(asset.PakPath, asset.ArchivePath));
            if (!string.IsNullOrWhiteSpace(previewPath) && File.Exists(previewPath))
            {
                var bytes = await File.ReadAllBytesAsync(previewPath);
                using var ms = new MemoryStream(bytes);
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                MergeModsInfoTexturePreview.Source = image;
                MergeModsInfoTexturePreview.Visibility = Visibility.Visible;
                MergeModsInfoPlaceholder.Visibility = Visibility.Collapsed;
                try { File.Delete(previewPath); } catch { }
            }
            else
            {
                MergeModsInfoPlaceholder.Text = "No texture preview is available for this asset.";
            }
        }
        catch (Exception ex)
        {
            MergeModsInfoPlaceholder.Text = $"Texture preview failed: {ex.Message}";
        }
    }

    private void MergeAssetToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle || toggle.Tag is not MergePakAsset asset) return;
        if (MergeModsInfoTitle == null) return;
        MergeModsInfoTitle.Text = asset.DisplayName;
        MergeModsInfoPath.Text = asset.ArchivePath;
        MergeModsInfoPlaceholder.Visibility = Visibility.Visible;
        MergeModsInfoTexturePreview.Visibility = Visibility.Collapsed;
    }

    private void MergeAssetToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_mergeModsUpdatingSelection) return;
        if (sender is not ToggleButton toggle || toggle.Tag is not MergePakAsset asset) return;

        var key = NormalizeMergeAssetPath(asset.ArchivePath);
        _mergeModsUpdatingSelection = true;
        try
        {
            foreach (var other in _mergePakGroups.SelectMany(g => g.Assets))
            {
                if (ReferenceEquals(other, asset)) continue;
                if (!string.Equals(NormalizeMergeAssetPath(other.ArchivePath), key,
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (other.Toggle != null)
                {
                    other.Toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                    other.Toggle.IsEnabled = false;
                }
            }
        }
        finally
        {
            _mergeModsUpdatingSelection = false;
        }
        UpdateMergeModsButtons();
    }

    private void MergeAssetToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle && toggle.Tag is MergePakAsset asset)
        {
            var key = NormalizeMergeAssetPath(asset.ArchivePath);
            foreach (var other in _mergePakGroups.SelectMany(g => g.Assets))
            {
                if (string.Equals(NormalizeMergeAssetPath(other.ArchivePath), key, StringComparison.OrdinalIgnoreCase))
                    other.Toggle?.SetCurrentValue(UIElement.IsEnabledProperty, true);
            }
        }
        if (!_mergeModsUpdatingSelection) UpdateMergeModsButtons();
    }

    private void UpdateMergeModsButtons()
    {
        if (MergeModsBuildButton == null) return;
        var hasPaks = _mergePakGroups.Count > 0;
        var hasSelected = _mergePakGroups.SelectMany(g => g.Assets)
            .Any(a => a.Toggle?.IsChecked == true);

        MergeModsBuildButton.IsEnabled = hasSelected && !_operationBusy;
        MergeModsResetButton.IsEnabled = hasPaks && !_operationBusy;
        MergeModsSelectPaksButton.IsEnabled = !_operationBusy;
    }

    private static string NormalizeMergeAssetPath(string path)
    {
        try
        {
            var relative = GetContentRelativeAssetPath(path);
            if (!string.IsNullOrWhiteSpace(relative))
                path = relative;
        }
        catch { }

        return path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
    }

    private static void BuildMergedPak(
        IReadOnlyList<MergePakAsset> selected,
        string outputPak,
        Action<string, double?, string?>? progress)
    {
        var repak = FindRepakExecutable();
        if (string.IsNullOrWhiteSpace(repak))
            throw new InvalidOperationException("repak.exe was not found in the game installation folder (the folder containing Engine and Localization).");

        var workRoot = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "MergeMods",
            Guid.NewGuid().ToString("N"));
        var stageRoot = Path.Combine(workRoot, "stage");
        Directory.CreateDirectory(stageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPak)!);

        try
        {
            var completed = 0;
            foreach (var pakGroup in selected.GroupBy(a => a.PakPath, StringComparer.OrdinalIgnoreCase))
            {
                using var pakStream = File.OpenRead(pakGroup.Key);
                var reader = CreateAssetPakReader(pakStream);
                var allFiles = GetReaderFiles(reader);

                foreach (var asset in pakGroup)
                {
                    var packageFiles = new[] { asset.ArchivePath }
                        .Concat(FindAssetCompanionFiles(asset.ArchivePath, allFiles))
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    foreach (var archivePath in packageFiles)
                    {
                        var relative = GetContentRelativeAssetPath(archivePath);
                        if (string.IsNullOrWhiteSpace(relative))
                            throw new InvalidOperationException($"Could not resolve {archivePath}.");

                        var destination = Path.GetFullPath(Path.Combine(
                            stageRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                        var root = Path.GetFullPath(stageRoot).TrimEnd(Path.DirectorySeparatorChar)
                                   + Path.DirectorySeparatorChar;
                        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("A selected asset resolved outside the staging directory.");

                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        ExtractAssetEntry(reader, pakStream, archivePath, destination);
                    }

                    completed++;
                    progress?.Invoke($"Preparing {asset.DisplayName}…",
                        completed * 90.0 / selected.Count,
                        $"{completed} / {selected.Count} assets staged");
                }
            }

            if (!Directory.EnumerateFiles(stageRoot, "*", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException("No cooked assets were staged.");

            progress?.Invoke("Packaging merged PAK…", 95,
                $"{selected.Count} asset package(s)");

            if (!RunAssetWorkshopRepakPack(repak, stageRoot, outputPak, out var error))
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error) ? "repak failed while creating the merged PAK." : error);

            if (!File.Exists(outputPak) || new FileInfo(outputPak).Length == 0)
                throw new InvalidOperationException("The merged PAK was not created.");

            progress?.Invoke("Merged PAK complete", 100,
                $"{Path.GetFileName(outputPak)} • {new FileInfo(outputPak).Length:N0} bytes");
        }
        finally
        {
            try { Directory.Delete(workRoot, true); } catch { }
        }
    }

    private string? ShowMergeModNameDialog()
    {
        var dialog = new Window
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 430,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Title = "Build Merged PAK",
            ShowInTaskbar = false,
            Background = (Brush)FindResource("WindowBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Name Your Mod",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("ForegroundBrush")
        });

        var nameBox = new TextBox
        {
            MinHeight = 34,
            Padding = new Thickness(10, 6, 10, 6),
            Background = (Brush)FindResource("InputBackgroundBrush"),
            Foreground = (Brush)FindResource("ForegroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush")
        };
        Grid.SetRow(nameBox, 2);
        root.Children.Add(nameBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 7, 14, 7),
            Style = (Style)FindResource("BrowseButtonStyle")
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;

        var build = new Button
        {
            Content = "Build",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(14, 7, 14, 7),
            Style = (Style)FindResource("AccentButtonStyle")
        };
        build.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                nameBox.Focus();
                return;
            }
            dialog.Tag = nameBox.Text.Trim();
            dialog.DialogResult = true;
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(build);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.Loaded += (_, _) => nameBox.Focus();
        return dialog.ShowDialog() == true ? dialog.Tag as string : null;
    }
}
