from pathlib import Path
p=Path('/mnt/data/patch106/MainWindow.xaml')
s=p.read_text(encoding='utf-8')
# Remove unwanted toggles from Zoom, Center, Fill, Rotate cards only.
s=s.replace('<Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><TextBlock Text="Zoom" FontWeight="SemiBold"/><CheckBox x:Name="PosterImageEditorZoomEnable" Grid.Column="1" Style="{DynamicResource VideoEditorToggleStyle}" IsChecked="True" Checked="PosterImageEditorPositionEnable_Changed" Unchecked="PosterImageEditorPositionEnable_Changed"/></Grid>', '<Grid.ColumnDefinitions><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Text="Zoom" FontWeight="SemiBold"/></Grid>')
s=s.replace('<Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><TextBlock Text="Center" FontWeight="SemiBold"/><CheckBox x:Name="PosterImageEditorCenterEnable" Grid.Column="1" Style="{DynamicResource VideoEditorToggleStyle}" IsChecked="True" Checked="PosterImageEditorPositionEnable_Changed" Unchecked="PosterImageEditorPositionEnable_Changed"/></Grid>', '<Grid.ColumnDefinitions><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Text="Center" FontWeight="SemiBold"/></Grid>')
s=s.replace('<Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><TextBlock Text="Fill" FontWeight="SemiBold"/><CheckBox x:Name="PosterImageEditorFillEnable" Grid.Column="1" Style="{DynamicResource VideoEditorToggleStyle}" IsChecked="True" Checked="PosterImageEditorPositionEnable_Changed" Unchecked="PosterImageEditorPositionEnable_Changed"/></Grid>', '<Grid.ColumnDefinitions><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Text="Fill" FontWeight="SemiBold"/></Grid>')
s=s.replace('<Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><TextBlock Text="Rotate" FontWeight="SemiBold"/><CheckBox x:Name="PosterImageEditorRotateEnable" Grid.Column="1" Style="{DynamicResource VideoEditorToggleStyle}" IsChecked="False" Checked="PosterImageEditorEffectEnable_Changed" Unchecked="PosterImageEditorEffectEnable_Changed"/></Grid>', '<Grid.ColumnDefinitions><ColumnDefinition/></Grid.ColumnDefinitions><TextBlock Text="Rotate" FontWeight="SemiBold"/></Grid>')
# Remove the old embedded profiles panel.
start='   <Border x:Name="PosterImageEditorProfilesPanel"'
i=s.find(start)
if i>=0:
    j=s.find('\n   </Border>', i)
    if j<0: raise SystemExit('profile border close not found')
    j += len('\n   </Border>')
    s=s[:i]+s[j:]
# Add no-op? profile controls are now fully dynamic.
p.write_text(s,encoding='utf-8')

p=Path('/mnt/data/patch106/MainWindow_ModManagerOperations.cs')
s=p.read_text(encoding='utf-8')
# Resolution labels and parser normalization.
s=s.replace('var selected = PosterImageEditorResolutionCombo.SelectedItem?.ToString() ?? "1024 × 2048";\n        var parts = selected.Split(\'×\'', 'var selected = PosterImageEditorResolutionCombo.SelectedItem?.ToString() ?? "1024x2048 (Recommended)";\n        var normalizedResolution = selected.Split(\' (\', 2)[0].Replace("×", "x").Replace(" ", "", StringComparison.Ordinal);\n        var parts = normalizedResolution.Split(\'x\'')
s=s.replace('var options = new List<string> { "1024 × 2048" };', 'var options = new List<string> { "1024x2048 (Recommended)" };')
s=s.replace('options.Add("2048 × 4096");', 'options.Add("2048x4096");')
s=s.replace('if (IsPosterImageAiUpscaleEnabled()) options.Add("4096 × 8192");', 'if (IsPosterImageAiUpscaleEnabled()) options.Add("4096x8192 (Not Recommended)");')
# Normalize profile resolution matching: replace selected item assignment block.
old='if (r.TryGetProperty("Resolution", out var res) && PosterImageEditorResolutionCombo != null) PosterImageEditorResolutionCombo.SelectedItem = res.GetString();'
new='if (r.TryGetProperty("Resolution", out var res) && PosterImageEditorResolutionCombo != null) { var savedResolution = res.GetString(); var targetResolution = savedResolution?.Split(" (", 2)[0].Replace("×", "x").Replace(" ", "", StringComparison.Ordinal); PosterImageEditorResolutionCombo.SelectedItem = PosterImageEditorResolutionCombo.Items.Cast<object>().FirstOrDefault(item => item.ToString()?.Split(" (", 2)[0].Replace("×", "x").Replace(" ", "", StringComparison.Ordinal).Equals(targetResolution, StringComparison.OrdinalIgnoreCase)); }'
s=s.replace(old,new)
# Remove old profile methods and replace with dynamic slide-out implementation.
old_start='    private void PosterImageEditorProfilesButton_Click(sender object' # won't match; locate exact line below
marker='    private void PosterImageEditorProfilesButton_Click(object sender, RoutedEventArgs e)'
i=s.find(marker)
if i<0: raise SystemExit('profile methods marker not found')
end=s.find('\n    private bool IsPosterImageAiUpscaleEnabled()', i)
if end<0: raise SystemExit('profile methods end not found')
new_methods=r'''    private async void PosterImageEditorProfilesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowPosterImageEditorProfilesPanelAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Poster image editor profiles panel failed: {ex}");
        }
    }

    private async Task ShowPosterImageEditorProfilesPanelAsync()
    {
        var directory = PosterImageEditorProfilesDirectory();
        Directory.CreateDirectory(directory);

        var dialog = new OverlayDialogHost(this, SlidePanelMode.Right)
        {
            Background = (Brush)Resources["CardBrush"],
            Foreground = (Brush)Resources["ForegroundBrush"]
        };

        var outer = new Grid { Margin = new Thickness(14, 18, 14, 18) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Profiles",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["ForegroundBrush"]
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = directory,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var closeButton = new Button
        {
            Content = "×",
            Width = 34,
            Height = 34,
            Style = (Style)Resources["BrowseButtonStyle"],
            ToolTip = "Close"
        };
        closeButton.Click += (_, _) => dialog.DialogResult = false;
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);
        Grid.SetRow(header, 0);
        outer.Children.Add(header);

        var saveButton = new Button
        {
            Content = "Save Current Profile",
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 10),
            Style = (Style)Resources["BrowseButtonStyle"]
        };
        saveButton.Click += (_, _) =>
        {
            try
            {
                var name = "Profile_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(CapturePosterImageEditorState(), new JsonSerializerOptions { WriteIndented = true }));
                _ = PopulateAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Save Profile", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        Grid.SetRow(saveButton, 1);
        outer.Children.Add(saveButton);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = true
        };
        var list = new StackPanel();
        scroll.Content = list;
        Grid.SetRow(scroll, 2);
        outer.Children.Add(scroll);

        async Task PopulateAsync()
        {
            list.Children.Clear();
            var files = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            if (files.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "No saved profiles found.",
                    Foreground = (Brush)Resources["SecondaryBrush"],
                    Margin = new Thickness(8, 18, 8, 18),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var file in files)
            {
                var row = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    SnapsToDevicePixels = true
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

                var load = new Button
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinHeight = 58,
                    Padding = new Thickness(10, 8, 10, 8),
                    Style = (Style)Resources["BrowseButtonStyle"],
                    ToolTip = "Load this profile"
                };
                var details = new StackPanel();
                details.Children.Add(new TextBlock
                {
                    Text = Path.GetFileNameWithoutExtension(file),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Resources["ForegroundBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                details.Children.Add(new TextBlock
                {
                    Text = File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm"),
                    Margin = new Thickness(0, 4, 0, 0),
                    FontSize = 11,
                    Foreground = (Brush)Resources["SecondaryBrush"]
                });
                load.Content = details;
                load.Click += (_, _) =>
                {
                    try
                    {
                        CapturePosterImageEditorHistory();
                        ApplyPosterImageEditorState(File.ReadAllText(file));
                        CapturePosterImageEditorHistory(true);
                        dialog.DialogResult = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "Load Profile", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                Grid.SetColumn(load, 0);
                row.Children.Add(load);

                var delete = new Button
                {
                    Width = 38,
                    Height = 38,
                    Margin = new Thickness(6, 10, 0, 10),
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Resources["VideoEditorIconButtonStyle"],
                    ToolTip = "Delete profile",
                    Tag = file
                };
                var deleteIcon = new Border
                {
                    Width = 18,
                    Height = 18,
                    Background = (Brush)delete.Foreground
                };
                deleteIcon.OpacityMask = new ImageBrush(LoadModIcon("delete.png")) { Stretch = Stretch.Uniform };
                delete.Content = deleteIcon;
                delete.Click += async (_, _) =>
                {
                    var result = MessageBox.Show(this,
                        $"Delete this profile permanently?\n\n{Path.GetFileNameWithoutExtension(file)}",
                        "Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (result != MessageBoxResult.Yes) return;
                    try
                    {
                        File.Delete(file);
                        await PopulateAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "Delete Profile", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                Grid.SetColumn(delete, 1);
                row.Children.Add(delete);
                list.Children.Add(row);
            }
        }

        dialog.OnEscapeClose = () => dialog.DialogResult = false;
        dialog.OnBackdropClose = () => dialog.DialogResult = false;
        dialog.Content = outer;
        _ = PopulateAsync();
        await dialog.ShowAsync();
    }

'''
s=s[:i]+new_methods+s[end:]
# Remove stale profile reset references and make rotate unconditional.
s=s.replace('        if (sender == PosterImageEditorZoomEnable && PosterImageEditorZoomSlider != null) PosterImageEditorZoomSlider.IsEnabled = enabled;\n', '')
s=s.replace('        if (sender == PosterImageEditorCenterEnable) { }\n        if (sender == PosterImageEditorFillEnable) { }\n', '')
s=s.replace('        if (PosterImageEditorRotateEnable?.IsChecked != true || _posterImageEditorSource == null) return;', '        if (_posterImageEditorSource == null) return;')
# Reset stale controls
for line in [
'            if (PosterImageEditorZoomEnable != null) PosterImageEditorZoomEnable.IsChecked = true;\n',
'            if (PosterImageEditorCenterEnable != null) PosterImageEditorCenterEnable.IsChecked = true;\n',
'            if (PosterImageEditorFillEnable != null) PosterImageEditorFillEnable.IsChecked = true;\n',
'            if (PosterImageEditorRotateEnable != null) PosterImageEditorRotateEnable.IsChecked = false;\n',
'        if (PosterImageEditorRotateEnable != null) PosterImageEditorRotateEnable.IsChecked = false;\n']:
    s=s.replace(line,'')
# Update profile state rotation check: remove stale rotate enable set.
s=s.replace(' SetCheck(PosterImageEditorFlipHorizontalEnable,_posterImageEditorFlipHorizontal); SetCheck(PosterImageEditorFlipVerticalEnable,_posterImageEditorFlipVertical); SetCheck(PosterImageEditorRotateEnable,_posterImageEditorRotation!=0);', ' SetCheck(PosterImageEditorFlipHorizontalEnable,_posterImageEditorFlipHorizontal); SetCheck(PosterImageEditorFlipVerticalEnable,_posterImageEditorFlipVertical);')
p.write_text(s,encoding='utf-8')
