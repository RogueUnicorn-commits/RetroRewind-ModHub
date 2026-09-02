using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RogueUnicorn.StoreTransfer;

internal sealed class StartupSplashWindow : Window
{
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;

    private static readonly Dictionary<string, (string Background, string Accent, string Foreground, string Secondary)> Palettes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["80s Synthwave"] = ("#0B0820", "#FF2FB3", "#FFF0FF", "#B8A9D6"),
            ["Arcade Neon"] = ("#081018", "#FF4D6D", "#FFF1D6", "#A7BBC2"),
            ["Sunset Drive"] = ("#1A0F16", "#FF8C42", "#FFF2D5", "#C6A99C"),
            ["Forest Terminal"] = ("#08130E", "#57D68D", "#E8FFE9", "#9CB8A4"),
            ["60s Mod"] = ("#F4EBD0", "#D65A31", "#3A3026", "#756A5B"),
            ["70s Psychedelic"] = ("#24131F", "#F2B134", "#FFE8B6", "#C7A98A"),
            ["90s Arcade"] = ("#11102B", "#7CFF00", "#F8F7FF", "#A8A7C4"),
            ["Retro Rewind"] = ("#0A0E17", "#125F6F", "#FEE1B5", "#FED18B")
        };

    private static readonly HashSet<string> SupportedFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Gillius ADF",
        "Universalis ADF Std",
        "Gillius ADF No2",
        "Berenis ADF Pro",
        "Accanthis ADF Std"
    };

    public StartupSplashWindow()
    {
        var theme = LoadSavedTheme();

        Title = "Retro Rewind ModHub";
        Width = 560;
        Height = 340;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Owner = null;

        // Keep the splash as a normal, non-topmost application window. The main WPF
        // window is hidden during startup, so there is no black surface behind this card.
        Background = new SolidColorBrush(ParseColor(theme.Background));
        FontFamily = CreateFontFamily(theme.Font);

        var foreground = new SolidColorBrush(ParseColor(theme.Foreground));
        var secondary = new SolidColorBrush(ParseColor(theme.Secondary));
        var accent = new SolidColorBrush(ParseColor(theme.Accent));

        var panel = new StackPanel
        {
            Margin = new Thickness(48),
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new TextBlock
        {
            Text = "RETRO REWIND",
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = "MODHUB",
            FontSize = 18,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 26)
        });

        _status = new TextBlock
        {
            Text = "Starting ModHub…",
            FontSize = 16,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(_status);

        _progress = new ProgressBar
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Value = 5,
            Foreground = accent,
            Background = secondary
        };
        panel.Children.Add(_progress);

        var card = new Border
        {
            Width = 520,
            Height = 300,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(ParseColor(theme.Background)),
            BorderBrush = new SolidColorBrush(ParseColor(theme.Secondary)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Child = panel
        };

        Content = card;
    }

    public void SetStatus(string status, double progress)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _status.Text = status;
            _progress.Value = Math.Clamp(progress, 0, 100);
        }), DispatcherPriority.Normal);
    }

    private static (string Background, string Accent, string Foreground, string Secondary, string Font) LoadSavedTheme()
    {
        var palette = "60s Mod";
        var font = "Gillius ADF";

        try
        {
            var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Retro Rewind Modhub");
            var path = Path.Combine(defaultFolder, "RetroRewindModhub.json");
            if (!File.Exists(path))
            {
                var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroRewind", "RetroRewindModhub.json");
                if (File.Exists(fallback)) path = fallback;
            }

            if (File.Exists(path))
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(path));
                palette = values?.GetValueOrDefault("settings.palette") ?? palette;
                font = values?.GetValueOrDefault("settings.font") ?? font;
            }
        }
        catch { }

        if (!Palettes.TryGetValue(palette, out var colors))
            colors = Palettes["60s Mod"];
        if (!SupportedFonts.Contains(font))
            font = "Gillius ADF";

        return (colors.Background, colors.Accent, colors.Foreground, colors.Secondary, font);
    }

    private static Color ParseColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value)!; }
        catch { return Colors.Black; }
    }

    private static FontFamily CreateFontFamily(string name)
    {
        var familyReference = name switch
        {
            "Gillius ADF No2" => "./Assets/Fonts/#Gillius ADF No2 Cond",
            _ => $"./Assets/Fonts/#{name}"
        };

        try
        {
            return new FontFamily(new Uri("pack://application:,,,/", UriKind.Absolute), familyReference);
        }
        catch
        {
            return new FontFamily("Segoe UI");
        }
    }
}
