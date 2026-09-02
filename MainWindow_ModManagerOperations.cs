using System.Diagnostics;
using System.Windows.Media.Imaging;
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
    private async Task VerifyDownloadedChecksumAsync(string filePath, string checksumUrl, string expectedFileName)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var text = await client.GetStringAsync(checksumUrl);
            var expected = ParseChecksum(text, expectedFileName);
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidOperationException(L("The checksum for {0} could not be found.", expectedFileName));

            var actual = await ComputeSha256WithRetryAsync(filePath);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L("The downloaded {0} failed its SHA-256 verification.", expectedFileName));
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(L("Could not retrieve the official checksum for {0}. The download was not installed.", expectedFileName), ex);
        }
    }

    private static string? ParseChecksum(string text, string fileName)
    {
        foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 64 && line.All(Uri.IsHexDigit))
                return line;

            if (line.Length < 64) continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            if (line.Contains(fileName, StringComparison.OrdinalIgnoreCase)) return hash;
        }
        return null;
    }

    private static async Task<bool> VerifyExecutableAsync(string executable, string argument)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = executable, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private async Task<string> EnsureLibVlcAsync()
    {
        var nativeDir = LibVlcToolsDirectory;
        if (File.Exists(Path.Combine(nativeDir, "libvlc.dll")) && File.Exists(Path.Combine(nativeDir, "libvlccore.dll")) && Directory.Exists(Path.Combine(nativeDir, "plugins")))
            return nativeDir;

        Directory.CreateDirectory(ToolsDirectory);
        var workRoot = Path.Combine(ToolsDirectory, ".download_libvlc");
        var packagePath = Path.Combine(workRoot, $"videolan.libvlc.windows.{LibVlcVersion}.nupkg");
        var extractRoot = Path.Combine(workRoot, "extract");
        try
        {
            Directory.CreateDirectory(workRoot);
            SetOperationBusy(true, L("Downloading LibVLC video playback engine…"));
            SetRequiredFileCardState("LibVLC", L("Downloading LibVLC…"), L("Downloading…"), false, true);
            await DownloadToolFileAsync(LibVlcPackageUrl, packagePath, "LibVLC");

            // Do not query the flat-container .sha512 endpoint here. Some networks/CDNs
            // return 404 for that metadata URL even though the official package itself is
            // available. Validate the downloaded NuGet package locally instead.
            SetRequiredFileCardState("LibVLC", L("Verifying LibVLC download…"), L("Verifying…"), false, false);
            await ValidateLibVlcPackageAsync(packagePath);

            SetOperationBusy(true, L("Installing LibVLC video playback engine…"));
            SetRequiredFileCardState("LibVLC", L("Installing LibVLC video playback engine…"), L("Installing…"), false, false);
            if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true);
            ZipFile.ExtractToDirectory(packagePath, extractRoot, true);

            var nativeSource = Directory.EnumerateFiles(extractRoot, "libvlc.dll", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(dir => !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "libvlccore.dll")) && Directory.Exists(Path.Combine(dir, "plugins")));
            if (string.IsNullOrWhiteSpace(nativeSource))
                throw new InvalidOperationException(L("LibVLC was downloaded, but the Windows x64 playback engine was not found in the package."));

            if (Directory.Exists(nativeDir)) Directory.Delete(nativeDir, true);
            Directory.CreateDirectory(nativeDir);
            CopyDirectory(nativeSource, nativeDir);

            if (!File.Exists(Path.Combine(nativeDir, "libvlccore.dll")) || !Directory.Exists(Path.Combine(nativeDir, "plugins")))
                throw new InvalidOperationException(L("LibVLC was installed, but required playback files are missing."));

            return nativeDir;
        }
        finally
        {
            try { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, true); } catch { }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
            CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
    }

    private static async Task ValidateLibVlcPackageAsync(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length < 100_000_000)
            throw new InvalidDataException("The LibVLC package download is incomplete or invalid.");

        // Opening the archive verifies that it is a readable ZIP/NuGet package.
        using var archive = ZipFile.OpenRead(filePath);
        if (archive.Entries.Count == 0)
            throw new InvalidDataException("The LibVLC package is empty or invalid.");

        // Validate by the actual native payload rather than assuming a particular
        // NuGet folder layout. VideoLAN has used more than one layout across
        // package revisions (for example libvlc/win-x64 and runtimes/win-x64/native).
        // The installer below locates the DLLs recursively after extraction.
        var hasLibVlc = archive.Entries.Any(e =>
            string.Equals(Path.GetFileName(e.FullName), "libvlc.dll", StringComparison.OrdinalIgnoreCase));
        var hasLibVlcCore = archive.Entries.Any(e =>
            string.Equals(Path.GetFileName(e.FullName), "libvlccore.dll", StringComparison.OrdinalIgnoreCase));
        if (!hasLibVlc || !hasLibVlcCore)
            throw new InvalidDataException("The LibVLC package does not contain the Windows x64 playback engine.");

        await Task.CompletedTask;
    }

    private string RequireFfmpegForVideoEditor()
    {
        var path = FindFfmpeg();
        if (path != null) return path;
        throw new InvalidOperationException(L("FFmpeg is required for the Video Editor. Open Required Files and download it first."));
    }

    private string RequireYtDlpForVideoEditor()
    {
        var path = FindYtDlp();
        if (path != null) return path;
        throw new InvalidOperationException(L("yt-dlp is required for video URL downloads. Open Required Files and download it first."));
    }

    private string RequireLibVlcForVideoEditor()
    {
        if (File.Exists(Path.Combine(LibVlcToolsDirectory, "libvlc.dll")) &&
            File.Exists(Path.Combine(LibVlcToolsDirectory, "libvlccore.dll")) &&
            Directory.Exists(Path.Combine(LibVlcToolsDirectory, "plugins")))
            return LibVlcToolsDirectory;
        throw new InvalidOperationException(L("LibVLC is required for the Video Editor preview. Open Required Files and download it first."));
    }

    private async Task EnsureVideoEditorPlayerAsync()
    {
        if (_videoEditorLibVlcReady && _videoEditorMediaPlayer != null) return;
        var libDir = RequireLibVlcForVideoEditor();
        await Dispatcher.InvokeAsync(() =>
        {
            if (_videoEditorLibVlc == null)
            {
                Core.Initialize(libDir);
                _videoEditorLibVlc = new LibVLC(false, true, "--no-video-title-show", "--no-osd");
            }
            // Start with software decoding for maximum compatibility. Some MP4
            // codecs/drivers report a successful LibVLC open but produce a blank
            // WPF surface when hardware decoding is enabled.
            _videoEditorMediaPlayer = new VlcMediaPlayer(_videoEditorLibVlc)
            {
                EnableHardwareDecoding = false,
                Mute = false
            };
            _videoEditorMediaPlayer.EndReached += VideoEditorLibVlc_EndReached;
            _videoEditorMediaPlayer.EncounteredError += VideoEditorLibVlc_EncounteredError;
            // Do not attach the player here. On the very first video load the WPF
            // VideoView may not have created its native drawable yet. Attaching a
            // MediaPlayer before that happens can make LibVLC create a separate
            // native video window. The player is attached only after the preview
            // surface has been made visible and rendered in PrepareVideoEditorPreviewAsync.
            _videoEditorLibVlcReady = true;
        });
    }

    private void VideoEditorLibVlc_EndReached(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_mode != "videoeditor" || _videoEditorSourceDuration <= TimeSpan.Zero)
                return;

            var next = _videoEditorLiveSegmentStart + _videoEditorLiveSegmentDuration;
            if (next >= _videoEditorSourceDuration - TimeSpan.FromMilliseconds(100))
            {
                _videoEditorPreviewTimer.Stop();
                _videoEditorTimelineUpdating = true;
                VideoEditorTimelineSlider.Value = _videoEditorSourceDuration.TotalSeconds;
                _videoEditorTimelineUpdating = false;
                UpdateVideoEditorTimelineText(_videoEditorSourceDuration);
                return;
            }

            QueueVideoEditorLivePreviewRender(next);
        }));
    }

    private void VideoEditorLibVlc_EncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_videoEditorUsingFallbackMediaElement || string.IsNullOrWhiteSpace(_videoEditorInputFile))
                return;

            Debug.WriteLine("Video Editor: audio clock reported an error. The FFmpeg render engine remains the video path.");
            VideoEditorOutputText.Text = "Audio clock unavailable — video render remains active.";
        }));
    }

    private async Task ActivateVideoEditorFallbackAsync(string file)
    {
        if (_mode != "videoeditor" || !File.Exists(file))
            return;

        try
        {
            _videoEditorUsingFallbackMediaElement = true;
            _videoEditorPreviewError = false;
            _videoEditorPreviewPreparing = true;
            _videoEditorPreviewLoaded = false;
            VideoEditorOutputText.Text = "Preparing compatible audio/video preview…";
            SetOperationBusy(true, L("Preparing video playback…"), null, Path.GetFileName(file));
            RefreshVideoEditorUi();

            // Do not feed the original file directly to WPF MediaElement. Its
            // Windows Media Foundation codec support varies by Windows install,
            // and this was the source of the "Media Foundation could not open"
            // error. FFmpeg is already a required Video Editor dependency, so make
            // a standard H.264/AAC/yuv420p MP4 specifically for playback.
            var ffmpeg = RequireFfmpegForVideoEditor();
            Directory.CreateDirectory(VideoEditorTempRoot);

            var fallbackFile = Path.Combine(
                VideoEditorTempRoot,
                $"playback_compat_{Guid.NewGuid():N}.mp4");

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(Path.GetFullPath(file));
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:v:0");
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:a:0?");
            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add("libx264");
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add("ultrafast");
            psi.ArgumentList.Add("-crf");
            psi.ArgumentList.Add("23");
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv420p");
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("aac");
            psi.ArgumentList.Add("-b:a");
            psi.ArgumentList.Add("128k");
            psi.ArgumentList.Add("-movflags");
            psi.ArgumentList.Add("+faststart");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add(fallbackFile);

            using var process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException(L("Could not start FFmpeg compatibility conversion."));

            var token = _videoEditorPreviewCts?.Token ?? CancellationToken.None;
            using var registration = token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            });

            var stderrTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            var stderr = await stderrTask;
            token.ThrowIfCancellationRequested();

            if (process.ExitCode != 0 || !File.Exists(fallbackFile) || new FileInfo(fallbackFile).Length < 1024)
            {
                Debug.WriteLine($"Video compatibility FFmpeg error: {stderr}");
                try { if (File.Exists(fallbackFile)) File.Delete(fallbackFile); } catch { }
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(stderr)
                        ? L("FFmpeg could not create a compatible playback video.")
                        : $"FFmpeg could not create a compatible playback video: {stderr.Trim()}");
            }

            // Never delete the user's source. This is a generated playback copy.
            _videoEditorFallbackFile = fallbackFile;

            await Dispatcher.InvokeAsync(() =>
            {
                if (_mode != "videoeditor" || !File.Exists(fallbackFile))
                    return;

                if (_videoEditorFallbackMediaElement == null)
                {
                    _videoEditorFallbackMediaElement = new MediaElement
                    {
                        LoadedBehavior = MediaState.Manual,
                        UnloadedBehavior = MediaState.Manual,
                        Stretch = Stretch.Uniform,
                        Volume = 1.0,
                        IsMuted = false
                    };
                    _videoEditorFallbackMediaElement.MediaOpened += VideoEditorFallback_MediaOpened;
                    _videoEditorFallbackMediaElement.MediaFailed += VideoEditorFallback_MediaFailed;
                    _videoEditorFallbackMediaElement.MediaEnded += VideoEditorFallback_MediaEnded;
                    _videoEditorFallbackMediaElement.MediaEnded += VideoEditorFallback_MediaEnded;
                }

                try { _videoEditorMediaPlayer?.Stop(); } catch { }

                _videoEditorFallbackMediaElement.Stop();
                _videoEditorFallbackMediaElement.Source = null;

                VideoEditorPreviewHost.Content = _videoEditorFallbackMediaElement;
                VideoEditorPreviewBorder.Visibility = Visibility.Visible;
                VideoEditorDropBorder.Visibility = Visibility.Collapsed;
                VideoEditorPreviewHost.UpdateLayout();

                _videoEditorFallbackMediaElement.Source =
                    new Uri(Path.GetFullPath(fallbackFile), UriKind.Absolute);
                _videoEditorFallbackMediaElement.Play();
                VideoEditorOutputText.Text = "Preparing compatible audio preview…";
                RefreshVideoEditorUi();
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _videoEditorPreviewLoaded = false;
            _videoEditorPreviewPreparing = false;
            _videoEditorPreviewError = true;
            _videoEditorIsPlaying = false;
            UpdateVideoEditorTransportButton();
            SetOperationBusy(false);
            VideoEditorOutputText.Text = "Preview unavailable. " + ex.Message;
            RefreshVideoEditorUi();
            Debug.WriteLine($"Video Editor compatibility fallback failed: {ex}");
        }
    }

    private void VideoEditorFallback_MediaOpened(object? sender, RoutedEventArgs e)
    {
        if (_videoEditorFallbackMediaElement == null)
            return;

        if (!_videoEditorUsingFallbackMediaElement)
        {
            try
            {
                _videoEditorFallbackMediaElement.Position = _videoEditorPendingAudioPosition;
                if (_videoEditorAudioWantedPlaying)
                    _videoEditorFallbackMediaElement.Play();
                else
                    _videoEditorFallbackMediaElement.Pause();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Video Editor audio clock open failed: {ex}");
            }
            return;
        }

        var duration = _videoEditorFallbackMediaElement.NaturalDuration.HasTimeSpan
            ? _videoEditorFallbackMediaElement.NaturalDuration.TimeSpan
            : TimeSpan.Zero;

        // Rendered effect previews are short segments of the original source.
        // Keep the source duration so the editor timeline doesn't collapse to the
        // segment length after an effect is changed.
        if (_videoEditorSourceDuration <= TimeSpan.Zero)
            _videoEditorSourceDuration = duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(1);
        _videoEditorPreviewDuration = _videoEditorSourceDuration;
        _videoEditorLiveSegmentStart = TimeSpan.Zero;
        _videoEditorPreviewLoaded = true;
        _videoEditorPreviewPreparing = false;
        _videoEditorPreviewError = false;
        _videoEditorTimelineUpdating = true;
        VideoEditorTimelineSlider.Minimum = 0;
        VideoEditorTimelineSlider.Maximum = Math.Max(0.001, _videoEditorSourceDuration.TotalSeconds);
        VideoEditorTimelineSlider.Value = 0;
        _videoEditorTimelineUpdating = false;
        UpdateVideoEditorTimelineText(TimeSpan.Zero);
        _videoEditorPreviewTimer.Start();
        SetOperationBusy(false);
        HideVideoEditorEffectsOverlay();
        RefreshVideoEditorUi();

        try
        {
            MountVideoEditorRealtimeSurface();
            _ = StartVideoEditorRealtimePreviewAsync(_videoEditorLiveSegmentStart, true);
            _videoEditorFallbackMediaElement.Play();
            _videoEditorIsPlaying = true;
            UpdateVideoEditorTransportButton();
        }
        catch { }
    }

    private void VideoEditorFallback_MediaEnded(object? sender, RoutedEventArgs e)
    {
        if (!_videoEditorUsingFallbackMediaElement)
            return;

        _videoEditorIsPlaying = false;
        _videoEditorPreviewTimer.Stop();
        UpdateVideoEditorTransportButton();
    }

    private void VideoEditorFallback_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        // In the normal editor path MediaElement is audio-only. Its inability to
        // decode the picture must never invalidate the FFmpeg video preview.
        if (!_videoEditorUsingFallbackMediaElement)
        {
            Debug.WriteLine($"Video Editor audio clock unavailable: {e.ErrorException}");
            return;
        }

        _videoEditorPreviewLoaded = false;
        _videoEditorPreviewPreparing = false;
        _videoEditorPreviewError = true;
        _videoEditorUsingFallbackMediaElement = true;
        SetOperationBusy(false);
        VideoEditorOutputText.Text = "Audio preview unavailable. The video preview can still be used.";
        RefreshVideoEditorUi();
        Debug.WriteLine($"Video Editor MediaElement MediaFailed: {e.ErrorException}");
    }

    private async Task PrepareVideoEditorPreviewAsync(string file)
    {
        if (_videoEditorPreviewPreparing || !File.Exists(file) || _mode != "videoeditor") return;

        _videoEditorPreviewCts?.Cancel();
        _videoEditorPreviewCts?.Dispose();
        _videoEditorPreviewCts = new CancellationTokenSource();
        var token = _videoEditorPreviewCts.Token;

        _videoEditorPreviewPreparing = true;
        _videoEditorPreviewLoaded = false;
        _videoEditorPreviewError = false;
        _videoEditorPreviewDuration = TimeSpan.Zero;

        try
        {
            _videoEditorPreviewTimer.Stop();
            _videoEditorTimelineUpdating = true;
            VideoEditorTimelineSlider.Minimum = 0;
            VideoEditorTimelineSlider.Maximum = 1;
            VideoEditorTimelineSlider.Value = 0;
            _videoEditorTimelineUpdating = false;
            VideoEditorOutputText.Text = "Loading video and preparing audio…";
            SetOperationBusy(true, L("Loading video…"), null, Path.GetFileName(file));

            // The editor no longer waits for LibVLC/native video initialization.
            // FFmpeg is the authoritative decoder, renderer and duration source.
            var ffmpeg = RequireFfmpegForVideoEditor();
            var durationTask = ProbeVideoDurationAsync(ffmpeg, file, token);
            var frameRateTask = ProbeVideoFrameRateAsync(ffmpeg, file, token);
            var audioClockTask = PrepareVideoEditorAudioClockAsync(ffmpeg, file, token);
            await Task.WhenAll(durationTask, frameRateTask, audioClockTask);
            var duration = await durationTask;
            _videoEditorFrameRate = await frameRateTask;
            _videoEditorFrameRate = Math.Clamp(_videoEditorFrameRate, 1.0, 120.0);
            await audioClockTask;

            token.ThrowIfCancellationRequested();
            if (_mode != "videoeditor") throw new OperationCanceledException(token);

            _videoEditorPreviewDuration = duration > TimeSpan.Zero
                ? duration
                : TimeSpan.FromSeconds(1);
            _videoEditorSourceDuration = _videoEditorPreviewDuration;
            _videoEditorLiveSegmentStart = TimeSpan.Zero;

            await Dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();

                VideoEditorPreviewBorder.Visibility = Visibility.Visible;
                VideoEditorDropBorder.Visibility = Visibility.Collapsed;

                _videoEditorTimelineUpdating = true;
                VideoEditorTimelineSlider.Minimum = 0;
                VideoEditorTimelineSlider.Maximum =
                    Math.Max(0.001, _videoEditorPreviewDuration.TotalSeconds);
                VideoEditorTimelineSlider.Value = 0;
                _videoEditorTimelineUpdating = false;

                UpdateVideoEditorTimelineText(TimeSpan.Zero);
                MountVideoEditorRealtimeSurface();

                _videoEditorPreviewLoaded = true;
                _videoEditorPreviewPreparing = false;
                _videoEditorPreviewError = false;
                _videoEditorIsPlaying = true;
                SetOperationBusy(false);
                RefreshVideoEditorUi();
                UpdateVideoEditorTransportButton();
            }, System.Windows.Threading.DispatcherPriority.Render);

            // Start the actual picture pipeline. There is deliberately no LibVLC
            // video surface and no WPF effect overlay involved.
            await StartVideoEditorRealtimePreviewAsync(TimeSpan.Zero, true);
        }
        catch (OperationCanceledException)
        {
            if (_mode == "videoeditor")
                SetOperationBusy(false);
            _videoEditorPreviewPreparing = false;
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            _videoEditorPreviewLoaded = false;
            _videoEditorPreviewPreparing = false;
            _videoEditorPreviewError = true;
            _videoEditorIsPlaying = false;
            UpdateVideoEditorTransportButton();
            Debug.WriteLine($"Video Editor FFmpeg initialization error: {ex}");
            try { Debug.WriteLine($"FFmpeg path: {RequireFfmpegForVideoEditor()}"); } catch { }
            try { Debug.WriteLine($"Video source: {file}"); } catch { }
            VideoEditorOutputText.Text = "Preview error: " + ex.Message;
        }
    }

    private async Task<TimeSpan> ProbeVideoDurationAsync(
        string ffmpeg,
        string file,
        CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(Path.GetFullPath(file));

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("FFmpeg could not be started.");

        var stderrTask = process.StandardError.ReadToEndAsync(token);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);

        await process.WaitForExitAsync(token);

        var stderr = await stderrTask;
        _ = await stdoutTask;

        // ffmpeg writes the input duration to stderr, e.g.
        // "Duration: 00:01:23.45, start: ..."
        var match = Regex.Match(
            stderr,
            @"Duration:\s*(\d{2}):(\d{2}):(\d{2}(?:\.\d+)?)",
            RegexOptions.CultureInvariant);

        if (!match.Success)
            throw new InvalidOperationException(
                "FFmpeg could not determine the video's duration. " +
                "Check that the selected file is a supported video.");

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        return TimeSpan.FromHours(hours) +
               TimeSpan.FromMinutes(minutes) +
               TimeSpan.FromSeconds(seconds);
    }

    private async Task<double> ProbeVideoFrameRateAsync(
        string ffmpeg,
        string file,
        CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(Path.GetFullPath(file));

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("FFmpeg could not be started.");
        var stderrTask = process.StandardError.ReadToEndAsync(token);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var stderr = await stderrTask;
        _ = await stdoutTask;

        // Prefer the source's average frame rate. Fall back to tbr for files where
        // FFmpeg does not report avg_frame_rate cleanly.
        var matches = Regex.Matches(
            stderr,
            @"(?:\s|,)(\d+(?:\.\d+)?)\s+(?:fps|tbr)(?:\s|,)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (Match match in matches)
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var fps) && fps > 0.1)
                return fps;
        }

        return 30.0;
    }

    private async Task PrepareVideoEditorAudioClockAsync(
        string ffmpeg,
        string file,
        CancellationToken token)
    {
        // MediaElement is used only as a hidden audio clock. Transcode the source
        // audio to a broadly supported AAC/MP4 stream so videos with AC-3, Opus,
        // unusual AAC profiles, etc. do not silently lose audio in the editor.
        try
        {
            Directory.CreateDirectory(VideoEditorTempRoot);
            var audioFile = Path.Combine(
                VideoEditorTempRoot,
                $"audio_clock_{Guid.NewGuid():N}.mp4");

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(Path.GetFullPath(file));
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:a:0");
            psi.ArgumentList.Add("-vn");
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("aac");
            psi.ArgumentList.Add("-b:a");
            psi.ArgumentList.Add("192k");
            psi.ArgumentList.Add("-ar");
            psi.ArgumentList.Add("48000");
            psi.ArgumentList.Add("-ac");
            psi.ArgumentList.Add("2");
            psi.ArgumentList.Add("-af");
            psi.ArgumentList.Add("aresample=async=1:first_pts=0");
            psi.ArgumentList.Add("-avoid_negative_ts");
            psi.ArgumentList.Add("make_zero");
            psi.ArgumentList.Add("-movflags");
            psi.ArgumentList.Add("+faststart");
            psi.ArgumentList.Add(audioFile);

            using var process = Process.Start(psi);
            if (process == null) return;
            var stderrTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            var stderr = await stderrTask;
            token.ThrowIfCancellationRequested();

            if (process.ExitCode == 0 && File.Exists(audioFile) && new FileInfo(audioFile).Length > 1024)
            {
                _videoEditorAudioClockFile = audioFile;
                return;
            }

            try { if (File.Exists(audioFile)) File.Delete(audioFile); } catch { }
            Debug.WriteLine($"Video Editor: source has no usable audio stream. {stderr.Trim()}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Audio is optional; the video editor must still open silent videos.
            Debug.WriteLine($"Video Editor audio clock preparation failed: {ex}");
            _videoEditorAudioClockFile = null;
        }
    }

    private void LoadVideoEditorPreview(string file)
    {
        _ = PrepareVideoEditorPreviewAsync(file);
    }

    private void ApplyVideoEditorPlayerAspect()
    {
        if (_videoEditorMediaPlayer == null) return;
        var aspect = GetVideoEditorAspect();
        _videoEditorMediaPlayer.AspectRatio = aspect == "original" ? null : aspect;
    }

    private long _videoEditorLastAudioResyncTimestamp;

    private void VideoEditorPreviewTimer_Tick(object? sender, EventArgs e)
    {
        // FFmpeg is the authoritative video clock. Only correct the hidden audio
        // clock when it has drifted materially; never seek audio on every tick.
        if (!_videoEditorPreviewLoaded || !_videoEditorRealtimeActive ||
            !_videoEditorIsPlaying || _videoEditorFallbackMediaElement == null ||
            VideoEditorPlayAudioCheckBox?.IsChecked != true ||
            string.IsNullOrWhiteSpace(_videoEditorAudioClockFile))
            return;

        var now = Stopwatch.GetTimestamp();
        if (now - _videoEditorLastAudioResyncTimestamp < Stopwatch.Frequency / 2)
            return;
        _videoEditorLastAudioResyncTimestamp = now;

        try
        {
            var target = TimeSpan.FromSeconds(Math.Clamp(
                VideoEditorTimelineSlider?.Value ?? 0,
                0, _videoEditorSourceDuration.TotalSeconds));
            var actual = _videoEditorFallbackMediaElement.Position;
            if (Math.Abs((actual - target).TotalMilliseconds) > 180)
            {
                _videoEditorFallbackMediaElement.Position = target;
                _videoEditorFallbackMediaElement.Play();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Video Editor audio drift correction failed: {ex}");
        }
    }

    private void UpdateVideoEditorTimelineText(TimeSpan position)
    {
        if (VideoEditorTimelineText == null) return;
        var duration = _videoEditorPreviewDuration;
        VideoEditorTimelineText.Text = $"{FormatVideoEditorTime(position)} / {FormatVideoEditorTime(duration)}";
    }

    private void UpdateVideoEditorAccelerationStatus()
    {
        if (VideoEditorOutputText == null) return;
        var engine = _videoEditorRenderEngine;
        if (engine == null)
        {
            VideoEditorOutputText.Text = "NVIDIA CUDA/NVDEC live preview • NVENC export when available";
            return;
        }

        VideoEditorOutputText.Text = engine.CudaDecodeEnabled
            ? (engine.NvencEncodeEnabled
                ? "NVIDIA CUDA/NVDEC live preview • NVENC export"
                : "NVIDIA CUDA/NVDEC live preview")
            : (engine.HardwareDecodeEnabled
                ? "NVIDIA/D3D11VA hardware decode live preview"
                : "CPU live preview • NVIDIA hardware acceleration unavailable");
    }

    private static string FormatVideoEditorTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }


    private void EnsureVideoEditorRealtimeSurface()
    {
        if (_videoEditorRealtimeImage != null) return;

        _videoEditorRealtimeBitmap = new WriteableBitmap(
            VideoEditorRealtimeWidth,
            VideoEditorRealtimeHeight,
            96, 96,
            PixelFormats.Bgra32,
            null);

        _videoEditorRealtimeImage = new Image
        {
            Source = _videoEditorRealtimeBitmap,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            IsHitTestVisible = false
        };
    }

    private void MountVideoEditorRealtimeSurface()
    {
        EnsureVideoEditorRealtimeSurface();
        if (_videoEditorRealtimeImage == null) return;

        // The render engine owns the picture. There is no LibVLC video surface,
        // MediaElement video surface, or WPF effect overlay underneath it.
        // MountVideoEditorRealtimeSurface can be called repeatedly (load, seek,
        // effect change, play/pause). WPF does not allow an element to have two
        // logical/visual parents, so explicitly detach the Image from its previous
        // Grid before putting it into the new host.
        if (VisualTreeHelper.GetParent(_videoEditorRealtimeImage) is Panel previousPanel)
            previousPanel.Children.Remove(_videoEditorRealtimeImage);

        var root = new Grid { Background = Brushes.Black };

        // Keep WPF MediaElement in the visual tree only as the audio clock.
        // Its picture is completely transparent; the visible picture always comes
        // from the FFmpeg render engine.
        if (_videoEditorFallbackMediaElement == null)
        {
            _videoEditorFallbackMediaElement = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.None,
                Width = 1,
                Height = 1,
                Opacity = 0,
                IsHitTestVisible = false,
                Volume = 1.0,
                IsMuted = false
            };
            _videoEditorFallbackMediaElement.MediaOpened += VideoEditorFallback_MediaOpened;
            _videoEditorFallbackMediaElement.MediaFailed += VideoEditorFallback_MediaFailed;
            _videoEditorFallbackMediaElement.MediaEnded += VideoEditorFallback_MediaEnded;
        }

        _videoEditorFallbackMediaElement.HorizontalAlignment = HorizontalAlignment.Left;
        _videoEditorFallbackMediaElement.VerticalAlignment = VerticalAlignment.Top;

        if (VisualTreeHelper.GetParent(_videoEditorFallbackMediaElement) is Panel previousAudioPanel)
            previousAudioPanel.Children.Remove(_videoEditorFallbackMediaElement);

        root.Children.Add(_videoEditorRealtimeImage);
        root.Children.Add(_videoEditorFallbackMediaElement);

        VideoEditorPreviewHost.Content = root;
        VideoEditorPreviewBorder.Visibility = Visibility.Visible;
        VideoEditorDropBorder.Visibility = Visibility.Collapsed;
    }

    private void QueueVideoEditorLivePreviewRender(TimeSpan? start = null)
    {
        var position = start ?? TimeSpan.FromSeconds(
            Math.Max(0, VideoEditorTimelineSlider?.Value ?? 0));
        _ = StartVideoEditorRealtimePreviewAsync(position, _videoEditorIsPlaying);
    }

    private void SyncVideoEditorAudio(TimeSpan position, bool playing)
    {
        // Audio is an explicit opt-in for the editor preview. This keeps the
        // render engine free to concentrate on the picture unless the user asks
        // for sound, and avoids a hidden audio clock fighting the video clock.
        var playAudio = VideoEditorPlayAudioCheckBox?.IsChecked == true;
        if (_videoEditorFallbackMediaElement == null ||
            string.IsNullOrWhiteSpace(_videoEditorInputFile) ||
            !File.Exists(_videoEditorInputFile))
            return;

        _videoEditorPendingAudioPosition = position;
        _videoEditorAudioWantedPlaying = playAudio && playing;

        if (!playAudio)
        {
            try
            {
                _videoEditorFallbackMediaElement.Pause();
                _videoEditorFallbackMediaElement.Position = position;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Video Editor audio mute sync failed: {ex}");
            }
            return;
        }

        try
        {
            var audioSource = !string.IsNullOrWhiteSpace(_videoEditorAudioClockFile) &&
                              File.Exists(_videoEditorAudioClockFile)
                ? _videoEditorAudioClockFile
                : null;
            if (audioSource == null) return;

            var sourcePath = Path.GetFullPath(audioSource);
            var current = _videoEditorFallbackMediaElement.Source?.LocalPath;
            if (!string.Equals(current, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                _videoEditorFallbackMediaElement.Stop();
                _videoEditorFallbackMediaElement.Source = new Uri(sourcePath, UriKind.Absolute);

                // MediaElement opens asynchronously. The MediaOpened handler applies
                // the pending position and play state.
                return;
            }

            _videoEditorFallbackMediaElement.Position = position;
            if (playing)
                _videoEditorFallbackMediaElement.Play();
            else
                _videoEditorFallbackMediaElement.Pause();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Video Editor audio clock sync failed: {ex}");
        }
    }

    private (int Width, int Height, double FrameRate) GetVideoEditorRealtimeRenderProfile()
    {
        // Tape Tear and Horizontal Glitch use animated pixel remapping. Running
        // those filters at the full 960x540/source-FPS preview resolution can
        // make the preview renderer fall behind realtime. Keep the actual filter
        // graph intact, but render the heavy realtime preview at a smaller frame
        // size and cap it at 30 FPS. The export path is unchanged and remains full
        // resolution/source quality.
        var heavyRealtimeEffect =
            VideoEditorTearCheck?.IsChecked == true ||
            VideoEditorGlitchCheck?.IsChecked == true;

        if (heavyRealtimeEffect)
            return (480, 270, Math.Min(_videoEditorFrameRate, 30.0));

        return (VideoEditorRealtimeWidth, VideoEditorRealtimeHeight, _videoEditorFrameRate);
    }

    private async Task StartVideoEditorRealtimePreviewAsync(TimeSpan position, bool realtimePlayback = true)
    {
        var input = _videoEditorInputFile;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) return;

        var ffmpeg = RequireFfmpegForVideoEditor();

        try
        {
            if (_videoEditorRenderEngine == null)
            {
                _videoEditorRenderEngine = new VideoEditorRenderEngine();
                _videoEditorRenderEngine.FrameReady += OnVideoEditorRenderFrame;
                _videoEditorRenderEngine.Error += message =>
                    Debug.WriteLine($"Video Editor render engine: {message}");
                await _videoEditorRenderEngine.DetectHardwareAccelerationAsync(ffmpeg, CancellationToken.None);
            }

            _videoEditorRealtimeActive = true;

            EnsureVideoEditorRealtimeSurface();
            MountVideoEditorRealtimeSurface();
            SyncVideoEditorAudio(position, realtimePlayback);

            // The filter string is the same one used by final conversion.
            // No WPF overlay participates in rendering. Heavy animated effects
            // use a smaller realtime render profile so the preview stays close to
            // realtime instead of entering slow motion.
            var profile = GetVideoEditorRealtimeRenderProfile();
            var filter = GetVideoEditorFilter(realtimePreview: true, realtimeWidth: profile.Width, realtimeHeight: profile.Height);

            await _videoEditorRenderEngine.StartAsync(
                ffmpeg,
                input,
                filter,
                position,
                profile.Width,
                profile.Height,
                realtimePlayback,
                realtimePlayback ? profile.FrameRate : _videoEditorFrameRate,
                CancellationToken.None);

            UpdateVideoEditorAccelerationStatus();
        }
        catch (Exception ex)
        {
            _videoEditorRealtimeActive = false;
            Debug.WriteLine($"Video Editor render engine start failed: {ex}");
            VideoEditorOutputText.Text = "Preview unavailable. " + ex.Message;
        }
    }

    private void OnVideoEditorRenderFrame(byte[] pixels, int width, int height, TimeSpan position)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!_videoEditorRealtimeActive || _videoEditorRealtimeBitmap == null) return;

            try
            {
                if (_videoEditorRealtimeBitmap.PixelWidth != width ||
                    _videoEditorRealtimeBitmap.PixelHeight != height)
                {
                    _videoEditorRealtimeBitmap = new WriteableBitmap(
                        width, height, 96, 96, PixelFormats.Bgra32, null);
                    if (_videoEditorRealtimeImage != null)
                        _videoEditorRealtimeImage.Source = _videoEditorRealtimeBitmap;
                }

                _videoEditorRealtimeBitmap.WritePixels(
                    new Int32Rect(0, 0, width, height),
                    pixels,
                    width * 4,
                    0);

                if (_videoEditorIsPlaying && !_videoEditorTimelineUpdating)
                {
                    var seconds = Math.Clamp(
                        position.TotalSeconds,
                        0,
                        _videoEditorSourceDuration.TotalSeconds);

                    _videoEditorTimelineUpdating = true;
                    VideoEditorTimelineSlider.Value = seconds;
                    _videoEditorTimelineUpdating = false;
                    UpdateVideoEditorTimelineText(TimeSpan.FromSeconds(seconds));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Video Editor frame presentation failed: {ex}");
            }
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private async Task StopVideoEditorRealtimePreviewAsync(bool keepAudio = false)
    {
        _videoEditorRealtimeActive = false;
        if (_videoEditorRenderEngine != null)
        {
            try { await _videoEditorRenderEngine.StopAsync(); } catch { }
        }

        // Legacy process/CTS are deliberately stopped as well so an old preview
        // generation can never write into the new editor surface.
        try { _videoEditorRealtimeCts?.Cancel(); } catch { }
        try { _videoEditorRealtimeProcess?.Kill(entireProcessTree: true); } catch { }
        try { _videoEditorRealtimeProcess?.Dispose(); } catch { }
        _videoEditorRealtimeProcess = null;
        try { _videoEditorRealtimeCts?.Dispose(); } catch { }
        _videoEditorRealtimeCts = null;

        if (!keepAudio)
        {
            try { _videoEditorFallbackMediaElement?.Pause(); } catch { }
        }
    }

    private void CreateVideoEditorEffectsOverlay()
    {
        _videoEditorEffectsOverlayRoot = new Grid { Background = Brushes.Transparent, IsHitTestVisible = false };

        _videoEditorEffectsVignette = new WpfRectangle { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
        _videoEditorEffectsHue = new WpfRectangle { Fill = GetThemeBrush("AccentBrush", Brushes.Magenta), Opacity = 0, IsHitTestVisible = false };
        _videoEditorEffectsFlicker = new WpfRectangle { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };

        _videoEditorEffectsChroma = new Grid { Opacity = 0, IsHitTestVisible = false };
        var left = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = 12, Background = GetThemeBrush("AccentBrush", Brushes.Cyan), Opacity = 0.8 };
        var right = new Border { HorizontalAlignment = HorizontalAlignment.Right, Width = 12, Background = GetThemeBrush("AccentBrush", Brushes.Cyan), Opacity = 0.8 };
        _videoEditorEffectsChroma.Children.Add(left);
        _videoEditorEffectsChroma.Children.Add(right);

        _videoEditorEffectsScanlines = new Grid { Opacity = 0, IsHitTestVisible = false };
        for (var i = 0; i < 32; i++)
        {
            _videoEditorEffectsScanlines.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var line = new Border { BorderBrush = GetThemeBrush("AccentBrush", Brushes.White), BorderThickness = new Thickness(0, 0, 0, 1), Opacity = 0.55 };
            Grid.SetRow(line, i);
            _videoEditorEffectsScanlines.Children.Add(line);
        }

        _videoEditorEffectsTear = new Grid { Opacity = 0, IsHitTestVisible = false };
        foreach (var margin in new[] { new Thickness(0, 18, 0, 0), new Thickness(0, -12, 0, 0), new Thickness(0, 0, 0, 22) })
        {
            var line = new Border { Height = 2, Background = GetThemeBrush("AccentBrush", Brushes.White), Margin = margin, VerticalAlignment = margin.Top != 0 ? VerticalAlignment.Top : (margin.Bottom != 0 ? VerticalAlignment.Bottom : VerticalAlignment.Center), Opacity = 0.8 };
            _videoEditorEffectsTear.Children.Add(line);
        }

        _videoEditorEffectsOverlayRoot.Children.Add(_videoEditorEffectsHue);
        _videoEditorEffectsOverlayRoot.Children.Add(_videoEditorEffectsChroma);
        _videoEditorEffectsOverlayRoot.Children.Add(_videoEditorEffectsVignette);
        _videoEditorEffectsOverlayRoot.Children.Add(_videoEditorEffectsScanlines);
        _videoEditorEffectsOverlayRoot.Children.Add(_videoEditorEffectsTear);
        _videoEditorEffectsOverlayRoot.Children.Add(_videoEditorEffectsFlicker);

        _videoEditorEffectsOverlayWindow = new Window
        {
            // Do not set Owner here: WPF requires an Owner window to have
            // been shown first. The overlay is positioned manually over the
            // preview and follows the main window.
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            Topmost = false,
            Content = _videoEditorEffectsOverlayRoot
        };
    }

    private Brush GetThemeBrush(string key, Brush fallback)
    {
        try { return (Brush)(FindResource(key) ?? fallback); } catch { return fallback; }
    }

    private void PositionVideoEditorEffectsOverlay()
    {
        if (_videoEditorEffectsOverlayWindow == null || VideoEditorPreviewHost == null) return;
        if (_mode != "videoeditor" || VideoEditorPreviewBorder.Visibility != Visibility.Visible || VideoEditorPreviewHost.ActualWidth <= 1 || VideoEditorPreviewHost.ActualHeight <= 1)
        {
            _videoEditorEffectsOverlayWindow.Hide();
            return;
        }

        try
        {
            // PointToScreen can be device-pixel based on a per-monitor-DPI setup,
            // while Window.Left/Top are device-independent pixels. Convert through
            // the current presentation source so the overlay stays exactly over the
            // LibVLC video instead of drifting down/right (or onto the controls).
            var source = PresentationSource.FromVisual(this);
            var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var screenPx = VideoEditorPreviewHost.PointToScreen(new Point(0, 0));
            var screenDip = fromDevice.Transform(screenPx);

            _videoEditorEffectsOverlayWindow.Left = screenDip.X;
            _videoEditorEffectsOverlayWindow.Top = screenDip.Y;
            _videoEditorEffectsOverlayWindow.Width = VideoEditorPreviewHost.ActualWidth;
            _videoEditorEffectsOverlayWindow.Height = VideoEditorPreviewHost.ActualHeight;
        }
        catch { }
    }

    private void ShowVideoEditorEffectsOverlay()
    {
        if (_videoEditorEffectsOverlayWindow == null || _mode != "videoeditor" || !_videoEditorPreviewLoaded) return;
        try
        {
            // Once the main window has been shown it is safe to make the effects
            // window owned by it. This keeps the overlay above the LibVLC airspace
            // control and makes it minimize/restore with the editor window.
            if (_videoEditorEffectsOverlayWindow.Owner == null && IsLoaded)
                _videoEditorEffectsOverlayWindow.Owner = this;

            PositionVideoEditorEffectsOverlay();
            if (!_videoEditorEffectsOverlayWindow.IsVisible) _videoEditorEffectsOverlayWindow.Show();
        }
        catch { }
    }

    private void HideVideoEditorEffectsOverlay()
    {
        try { _videoEditorEffectsOverlayWindow?.Hide(); } catch { }
    }

    private void StopAndReleaseVideoEditorPreview(bool disposePlayer = false)
    {
        // Cancel any asynchronous preview preparation first. Without this, a
        // navigation/source change could race with LibVLC and recreate the media
        // after it was already released.
        try { _videoEditorPreviewCts?.Cancel(); } catch { }
        try { _videoEditorLiveRenderCts?.Cancel(); } catch { }
        try { _videoEditorLiveRenderCts?.Dispose(); } catch { }
        _videoEditorLiveRenderCts = null;

        // Do not tear down LibVLCSharp's native VideoView/MediaPlayer just because
        // the user changed pages. Destroying the native player while the WPF
        // VideoView is still in the visual tree can deadlock or crash the process.
        // Navigation therefore only stops playback; full disposal is reserved for
        // window shutdown.
        try { _videoEditorPreviewCts?.Dispose(); } catch { }
        _videoEditorPreviewCts = null;
        try { _videoEditorPreviewTimer.Stop(); } catch { }
        HideVideoEditorEffectsOverlay();
        try { _videoEditorMediaPlayer?.Stop(); } catch { }
        try { _videoEditorFallbackMediaElement?.Stop(); } catch { }
        try { _videoEditorFallbackMediaElement!.Source = null; } catch { }
        _videoEditorUsingFallbackMediaElement = false;
        if (!string.IsNullOrWhiteSpace(_videoEditorFallbackFile) && File.Exists(_videoEditorFallbackFile))
        {
            try { File.Delete(_videoEditorFallbackFile); } catch { }
        }
        _videoEditorFallbackFile = null;
        if (!string.IsNullOrWhiteSpace(_videoEditorAudioClockFile) && File.Exists(_videoEditorAudioClockFile))
        {
            try { File.Delete(_videoEditorAudioClockFile); } catch { }
        }
        _videoEditorAudioClockFile = null;
        _videoEditorFrameRate = 30.0;

        _videoEditorPreviewLoaded = false;
        _videoEditorPreviewPreparing = false;
        _videoEditorPreviewError = false;
        try { if (VideoEditorPreviewBorder != null) VideoEditorPreviewBorder.Visibility = Visibility.Collapsed; } catch { }
        var releasedPreviewFile = _videoEditorPreviewFile;
        _videoEditorPreviewFile = null;
        _videoEditorPreviewDuration = TimeSpan.Zero;
        _videoEditorSourceDuration = TimeSpan.Zero;
        _videoEditorLiveSegmentStart = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(releasedPreviewFile) &&
            releasedPreviewFile.Contains("live_preview_", StringComparison.OrdinalIgnoreCase))
        {
            try { if (File.Exists(releasedPreviewFile)) File.Delete(releasedPreviewFile); } catch { }
        }
        _videoEditorTimelineUpdating = false;

        // Always detach and dispose the current Media when leaving/changing video.
        // Also detach the WPF VideoView surface. Keeping a stale native video surface
        // attached across page navigation can cause LibVLC to lose its HWND and open
        // the next video in a separate native window. The managed player itself can
        // remain alive and will be re-attached when the Video Editor is opened again.
        ReleaseVideoEditorMedia();

        if (!disposePlayer)
            return;

        try { _videoEditorMediaPlayer?.Stop(); } catch { }
        try { if (_videoEditorMediaPlayer != null) _videoEditorMediaPlayer.EndReached -= VideoEditorLibVlc_EndReached; } catch { }
        try { if (_videoEditorMediaPlayer != null) _videoEditorMediaPlayer.EncounteredError -= VideoEditorLibVlc_EncounteredError; } catch { }
        try { _videoEditorMediaPlayer?.Dispose(); } catch { }
        try { _videoEditorMedia?.Dispose(); } catch { }
        _videoEditorMedia = null;
        _videoEditorMediaPlayer = null;
        _videoEditorLibVlcReady = false;

        try { _videoEditorLibVlc?.Dispose(); } catch { }
        _videoEditorLibVlc = null;
    }

    private void ReleaseVideoEditorMedia()
    {
        var oldMedia = _videoEditorMedia;
        _videoEditorMedia = null;

        // Detach the media from the player before disposing the managed wrapper.
        // This avoids disposing a Media object that LibVLC may still be using.
        try { if (_videoEditorMediaPlayer != null) _videoEditorMediaPlayer.Media = null; } catch { }
        try { oldMedia?.Dispose(); } catch { }
    }

    private void VideoEditorGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
    }

    private void VideoEditorGrid_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        var file = files.FirstOrDefault(f => Path.GetExtension(f).Equals(".mp4", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(file)) SetVideoEditorInput(file);
    }

    private void SetVideoEditorInput(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
        if (!VideoEditorDownloadExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Please choose a supported video file.", "Video Editor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            try { _videoEditorPreviewCts?.Cancel(); } catch { }
            try { _videoEditorPreviewCts?.Dispose(); } catch { }
            _videoEditorPreviewCts = null;
            _videoEditorPreviewTimer.Stop();
            _videoEditorPreviewLoaded = false;
            _videoEditorPreviewPreparing = false;
            _videoEditorPreviewError = false;
            _videoEditorPreviewDuration = TimeSpan.Zero;
            _videoEditorTimelineUpdating = true;
            if (VideoEditorTimelineSlider != null)
            {
                VideoEditorTimelineSlider.Minimum = 0;
                VideoEditorTimelineSlider.Maximum = 1;
                VideoEditorTimelineSlider.Value = 0;
            }
            _videoEditorTimelineUpdating = false;

            // Selecting a new source closes the current video first.
            StopAndReleaseVideoEditorPreview();
            _videoEditorPreviewFile = null;

            _videoEditorInputFile = Path.GetFullPath(file);
            UpdateVideoEditorTimelineText(TimeSpan.Zero);
            RefreshVideoEditorUi();

            // SetVideoEditorInput only changes the selected source; actually start
            // the LibVLC load here so Browse/drag-and-drop both enter playback.
            LoadVideoEditorPreview(_videoEditorInputFile);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Video input error: {ex}");
        }
    }

    private void UpdateVideoEditorTransportButton()
    {
        if (VideoEditorPlayButton == null) return;

        var playing = _videoEditorIsPlaying;
        VideoEditorPlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        VideoEditorPauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        VideoEditorPlayButton.ToolTip = playing ? "Pause" : "Play";
        System.Windows.Automation.AutomationProperties.SetName(
            VideoEditorPlayButton,
            playing ? "Pause" : "Play");
    }

    private void VideoEditorPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_videoEditorPreviewLoaded || string.IsNullOrWhiteSpace(_videoEditorInputFile)) return;
        if (_videoEditorIsPlaying)
        {
            VideoEditorPauseButton_Click(sender, e);
            return;
        }

        try
        {
            var position = TimeSpan.FromSeconds(Math.Clamp(
                VideoEditorTimelineSlider?.Value ?? 0, 0,
                _videoEditorSourceDuration.TotalSeconds));

            SyncVideoEditorAudio(position, true);
            MountVideoEditorRealtimeSurface();
            _ = StartVideoEditorRealtimePreviewAsync(position, true);
            _videoEditorIsPlaying = true;
            _videoEditorPreviewTimer.Start();
            UpdateVideoEditorTransportButton();
        }
        catch (Exception ex) { Debug.WriteLine($"Video Editor play failed: {ex}"); }
    }

    private void VideoEditorPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_videoEditorPreviewLoaded) return;
        try
        {
            var position = TimeSpan.FromSeconds(Math.Clamp(
                VideoEditorTimelineSlider?.Value ?? 0, 0,
                _videoEditorSourceDuration.TotalSeconds));

            SyncVideoEditorAudio(position, false);
            _ = StopVideoEditorRealtimePreviewAsync(keepAudio: true);
            _videoEditorIsPlaying = false;
            _videoEditorPreviewTimer.Stop();
            _ = StartVideoEditorRealtimePreviewAsync(position, false);
            UpdateVideoEditorTransportButton();
            UpdateVideoEditorTimelineText(position);
        }
        catch (Exception ex) { Debug.WriteLine($"Video Editor pause failed: {ex}"); }
    }

    private void VideoEditorStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_videoEditorPreviewLoaded) return;
        try
        {
            SyncVideoEditorAudio(TimeSpan.Zero, false);
            _ = StopVideoEditorRealtimePreviewAsync(keepAudio: false);
            _videoEditorIsPlaying = false;
            _videoEditorPreviewTimer.Stop();

            _videoEditorTimelineUpdating = true;
            VideoEditorTimelineSlider.Value = 0;
            _videoEditorTimelineUpdating = false;
            UpdateVideoEditorTimelineText(TimeSpan.Zero);
            UpdateVideoEditorTransportButton();
            _ = StartVideoEditorRealtimePreviewAsync(TimeSpan.Zero, false);
        }
        catch (Exception ex)
        {
            _videoEditorTimelineUpdating = false;
            Debug.WriteLine($"Video Editor stop failed: {ex}");
        }
    }

    private void VideoEditorTimeline_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_videoEditorTimelineUpdating || !_videoEditorPreviewLoaded ||
            _videoEditorSourceDuration <= TimeSpan.Zero) return;

        try
        {
            var target = TimeSpan.FromSeconds(Math.Clamp(
                e.NewValue, 0, _videoEditorSourceDuration.TotalSeconds));

            SyncVideoEditorAudio(target, _videoEditorIsPlaying);
            _ = StartVideoEditorRealtimePreviewAsync(target, _videoEditorIsPlaying);
            UpdateVideoEditorTimelineText(target);
        }
        catch (Exception ex) { Debug.WriteLine($"Video Editor seek failed: {ex}"); }
    }

    private async void VideoEditorDownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = await ShowVideoEditorDownloadsPanelAsync();
            if (!string.IsNullOrWhiteSpace(selected) && File.Exists(selected))
            {
                StopAndReleaseVideoEditorPreview();
                SetVideoEditorInput(selected);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Video download library panel failed: {ex}");
        }
    }

    private async Task<string?> ShowVideoEditorDownloadsPanelAsync()
    {
        var directory = GetVideoEditorDownloadsDirectory();
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
            Text = "Downloaded Videos",
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

        var refreshButton = new Button
        {
            Content = "Refresh",
            Width = 90,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
            Style = (Style)Resources["BrowseButtonStyle"]
        };
        Grid.SetRow(refreshButton, 1);
        outer.Children.Add(refreshButton);

        var list = new StackPanel();

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = true
        };
        scroll.Content = list;
        Grid.SetRow(scroll, 2);
        outer.Children.Add(scroll);

        async Task PopulateAsync()
        {
            list.Children.Clear();

            // Clean up files created by older builds. Downloads are user-facing
            // files, so the temporary download_ prefix should never remain.
            foreach (var legacyFile in Directory.EnumerateFiles(directory, "download_*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var name = Path.GetFileName(legacyFile);
                    var cleanName = name["download_".Length..];
                    if (!string.IsNullOrWhiteSpace(cleanName))
                    {
                        var cleanPath = GetUniqueVideoEditorDownloadPath(Path.Combine(directory, cleanName));
                        if (!string.Equals(legacyFile, cleanPath, StringComparison.OrdinalIgnoreCase))
                            File.Move(legacyFile, cleanPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Video download filename migration failed: {ex.Message}");
                }
            }

            var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => VideoEditorDownloadExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            if (files.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "No downloaded videos found.",
                    Foreground = (Brush)Resources["SecondaryBrush"],
                    Margin = new Thickness(8, 18, 8, 18),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            // IMPORTANT: never probe every video concurrently. FFmpeg is a
            // heavyweight process and the old Task.WhenAll implementation could
            // start dozens of ffmpeg.exe instances at once, making the whole
            // machine unresponsive. Probe one file at a time and let the dialog
            // render between probes.
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                var metadata = await Task.Run(() => ProbeVideoEditorFileAsync(file));
                var row = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    SnapsToDevicePixels = true
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

                var open = new Button
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinHeight = 70,
                    Padding = new Thickness(10, 8, 10, 8),
                    Style = (Style)Resources["BrowseButtonStyle"],
                    ToolTip = "Load this video"
                };
                var details = new Grid();
                details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = Path.GetFileNameWithoutExtension(file),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Resources["ForegroundBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var ext = new TextBlock
                {
                    Text = Path.GetExtension(file).TrimStart('.').ToUpperInvariant(),
                    Margin = new Thickness(8, 0, 0, 0),
                    Foreground = (Brush)Resources["AccentBrush"]
                };
                Grid.SetColumn(ext, 1);
                details.Children.Add(name);
                details.Children.Add(ext);

                var metaText = new TextBlock
                {
                    Text = $"{FormatDownloadSize(info.Length)}  •  {info.LastWriteTime:yyyy-MM-dd HH:mm}  •  {FormatVideoDuration(metadata.Duration)}  •  {metadata.Width}×{metadata.Height}",
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = 11,
                    Foreground = (Brush)Resources["SecondaryBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetRow(metaText, 1);
                Grid.SetColumnSpan(metaText, 2);
                details.Children.Add(metaText);
                open.Content = details;
                open.Click += (_, _) =>
                {
                    dialog.SelectedValue = file;
                    dialog.DialogResult = true;
                };
                Grid.SetColumn(open, 0);
                row.Children.Add(open);

                var delete = new Button
                {
                    Width = 38,
                    Height = 38,
                    Margin = new Thickness(6, 16, 0, 16),
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Resources["VideoEditorIconButtonStyle"],
                    ToolTip = "Delete permanently",
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
                    var result = MessageBox.Show(
                        this,
                        L("Deleting this video is permanent and cannot be undone.\n\n{0}\n\nDelete it?", Path.GetFileName(file)),
                        L("Delete Video"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);
                    if (result != MessageBoxResult.Yes) return;

                    try
                    {
                        if (string.Equals(Path.GetFullPath(_videoEditorInputFile ?? ""), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
                            ClearVideoEditorInput();
                        File.Delete(file);
                        await PopulateAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, L("Delete Video"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                Grid.SetColumn(delete, 1);
                row.Children.Add(delete);
                list.Children.Add(row);
            }
        }

        refreshButton.Click += async (_, _) => await PopulateAsync();
        dialog.OnEscapeClose = () => dialog.DialogResult = false;
        dialog.OnBackdropClose = () => dialog.DialogResult = false;
        dialog.Content = outer;
        _ = PopulateAsync();
        dialog.ShowDialog();
        return dialog.DialogResult == true ? dialog.SelectedValue as string : null;
    }

    private sealed record VideoEditorFileMetadata(TimeSpan Duration, int Width, int Height);

    private async Task<VideoEditorFileMetadata> ProbeVideoEditorFileAsync(string file)
    {
        Process? process = null;
        try
        {
            var ffmpeg = FindFfmpeg();
            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
                return new VideoEditorFileMetadata(TimeSpan.Zero, 0, 0);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                // stdout is intentionally not redirected: the null muxer does
                // not need it, and an unused redirected pipe can deadlock a
                // child process if it ever writes enough data.
                RedirectStandardOutput = false
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-threads");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(file);
            // We only need enough decoding to validate the stream and obtain
            // the dimensions. Never let the library scan/decode the whole file.
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-an");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("null");
            psi.ArgumentList.Add("-");

            process = Process.Start(psi);
            if (process == null) return new VideoEditorFileMetadata(TimeSpan.Zero, 0, 0);

            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch { }
                try { await process.WaitForExitAsync(); } catch { }
            }

            var stderr = await stderrTask;
            if (!process.HasExited) return new VideoEditorFileMetadata(TimeSpan.Zero, 0, 0);

            var duration = TimeSpan.Zero;
            var durationMatch = Regex.Match(stderr, @"Duration:\s*(\d{2}):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (durationMatch.Success && double.TryParse(durationMatch.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                duration = new TimeSpan(0, int.Parse(durationMatch.Groups[1].Value), int.Parse(durationMatch.Groups[2].Value), (int)seconds, (int)((seconds - Math.Truncate(seconds)) * 1000));

            var dimensions = Regex.Match(stderr, @"Video:[^\r\n]*?(\d{2,5})x(\d{2,5})", RegexOptions.IgnoreCase);
            var width = dimensions.Success ? int.Parse(dimensions.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var height = dimensions.Success ? int.Parse(dimensions.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            return new VideoEditorFileMetadata(duration, width, height);
        }
        catch
        {
            try
            {
                if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
            }
            catch { }
            return new VideoEditorFileMetadata(TimeSpan.Zero, 0, 0);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string FormatVideoDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return "—";
        return duration.TotalHours >= 1 ? duration.ToString(@"hh\:mm\:ss") : duration.ToString(@"mm\:ss");
    }

    private void VideoEditorBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        StopAndReleaseVideoEditorPreview();

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose MP4",
            Filter = "MP4 Video (*.mp4)|*.mp4|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            SetVideoEditorInput(dialog.FileName);
    }

    private void QueueVideoEditorEffectRender()
    {
        _videoEditorEffectRenderCts?.Cancel();
        _videoEditorEffectRenderCts?.Dispose();
        _videoEditorEffectRenderCts = new CancellationTokenSource();
        var token = _videoEditorEffectRenderCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // Coalesce rapid slider movement into one FFmpeg graph rebuild.
                await Task.Delay(120, token);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || !_videoEditorPreviewLoaded) return;

                    var position = TimeSpan.FromSeconds(Math.Clamp(
                        VideoEditorTimelineSlider?.Value ?? 0,
                        0,
                        _videoEditorSourceDuration.TotalSeconds));

                    SyncVideoEditorAudio(position, _videoEditorIsPlaying);
                    _ = StartVideoEditorRealtimePreviewAsync(
                        position,
                        _videoEditorIsPlaying);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Video Editor effect render queue failed: {ex}");
            }
        }, token);
    }

    private void VideoEditorOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        try
        {
            UpdateVideoEditorPreviewEffects();
            QueueVideoEditorEffectRender();
        }
        catch (Exception ex) { Debug.WriteLine($"Video option change error: {ex}"); }
    }

    private void VideoEditorSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        try
        {
            if (ReferenceEquals(sender, VideoEditorScanlineSlider) && VideoEditorScanlineValue != null)
                VideoEditorScanlineValue.Text = $"{VideoEditorScanlineSlider.Value:0}%";
            else if (ReferenceEquals(sender, VideoEditorVignetteSlider) && VideoEditorVignetteValue != null)
                VideoEditorVignetteValue.Text = $"{VideoEditorVignetteSlider.Value:0}%";
            else if (ReferenceEquals(sender, VideoEditorChromaSlider) && VideoEditorChromaValue != null)
                VideoEditorChromaValue.Text = $"{VideoEditorChromaSlider.Value:0}px";
            else if (ReferenceEquals(sender, VideoEditorFlickerSlider) && VideoEditorFlickerValue != null)
                VideoEditorFlickerValue.Text = $"{VideoEditorFlickerSlider.Value:0}%";
            else if (ReferenceEquals(sender, VideoEditorTearSlider) && VideoEditorTearValue != null)
                VideoEditorTearValue.Text = $"{VideoEditorTearSlider.Value:0}%";
            else if (ReferenceEquals(sender, VideoEditorHueSlider) && VideoEditorHueValue != null)
                VideoEditorHueValue.Text = $"{VideoEditorHueSlider.Value:0}°";
            else if (ReferenceEquals(sender, VideoEditorTapeNoiseSlider) && VideoEditorTapeNoiseValue != null)
                VideoEditorTapeNoiseValue.Text = $"{VideoEditorTapeNoiseSlider.Value:0}%";
            else if (ReferenceEquals(sender, VideoEditorRgbSplitSlider) && VideoEditorRgbSplitValue != null)
                VideoEditorRgbSplitValue.Text = $"{VideoEditorRgbSplitSlider.Value:0}px";
            else if (ReferenceEquals(sender, VideoEditorGlitchSlider) && VideoEditorGlitchValue != null)
                VideoEditorGlitchValue.Text = $"{VideoEditorGlitchSlider.Value:0}%";
            else if (ReferenceEquals(sender, VideoEditorSaturationSlider) && VideoEditorSaturationValue != null)
                VideoEditorSaturationValue.Text = $"{VideoEditorSaturationSlider.Value:0}%";
            else if (ReferenceEquals(sender, VideoEditorContrastSlider) && VideoEditorContrastValue != null)
                VideoEditorContrastValue.Text = $"{VideoEditorContrastSlider.Value:0}%";

            QueueVideoEditorEffectRender();
        }
        catch (Exception ex) { Debug.WriteLine($"Video slider change error: {ex}"); }
    }

    private void UpdateVideoEditorPreviewEffects()
    {
        // Intentionally empty. There is no WPF effect overlay anymore.
        // The render engine is the only source of the visible picture.
    }

    private string GetVideoEditorAspect()
    {
        return VideoEditorAspectComboBox?.SelectedValue?.ToString() ?? "1:1";
    }

    private string GetVideoEditorCropMode()
    {
        return VideoEditorCropModeComboBox?.SelectedValue?.ToString() ?? "center";
    }

    private string GetVideoEditorFilter(bool realtimePreview = false, int? realtimeWidth = null, int? realtimeHeight = null)
    {
        var filters = new List<string>();

        // Single source of truth: the same real FFmpeg filter chain is used by
        // the live renderer and the final exported video.
        var aspect = GetVideoEditorAspect();
        if (aspect == "1:1")
            filters.Add("scale=1080:1080:force_original_aspect_ratio=increase,crop=1080:1080");
        else if (aspect == "16:9")
            filters.Add("scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080");
        else if (aspect == "4:3")
            filters.Add("scale=1440:1080:force_original_aspect_ratio=increase,crop=1440:1080");
        else
            filters.Add("scale=1920:1080:force_original_aspect_ratio=decrease,pad=ceil(iw/2)*2:ceil(ih/2)*2:(ow-iw)/2:(oh-ih)/2");

        // CRT
        if (VideoEditorCrtCheckBox?.IsChecked == true)
            filters.Add("eq=contrast=1.05:saturation=0.85:brightness=-0.02");

        if (VideoEditorScanlinesCheck?.IsChecked == true)
        {
            var amount = Math.Clamp(VideoEditorScanlineSlider?.Value ?? 25, 0, 100) / 100.0;
            var alpha = (0.08 + amount * 0.62).ToString("0.###", CultureInfo.InvariantCulture);
            filters.Add($"drawgrid=w=iw:h=4:t=1:c=black@{alpha}");
        }

        if (VideoEditorVignetteCheck?.IsChecked == true)
        {
            var amount = Math.Clamp(VideoEditorVignetteSlider?.Value ?? 5, 0, 100) / 100.0;
            if (amount > 0)
            {
                var angle = Math.Max(2.0, 7.0 - amount * 5.0);
                filters.Add($"vignette=PI/{angle.ToString("0.###", CultureInfo.InvariantCulture)}");
            }
        }

        // VHS
        if (VideoEditorChromaCheck?.IsChecked == true)
        {
            var shift = Math.Clamp(VideoEditorChromaSlider?.Value ?? 4, 0, 12);
            if (shift > 0)
                filters.Add($"chromashift=cbh={shift}:crh=-{shift}");
        }

        if (VideoEditorTearCheck?.IsChecked == true)
        {
            var amount = Math.Clamp(VideoEditorTearSlider?.Value ?? 10, 0, 100) / 100.0;
            if (amount > 0)
            {
                var offset = Math.Max(1, (int)Math.Round(amount * 45));
                var band = Math.Max(2, (int)Math.Round(2 + amount * 8));
                filters.Add(
                    $"geq=r='if(lt(mod(Y+floor(T*31),180),{band}),r(X+{offset}*sin(T*42),Y),r(X,Y))':" +
                    $"g='if(lt(mod(Y+floor(T*31),180),{band}),g(X+{offset}*sin(T*42),Y),g(X,Y))':" +
                    $"b='if(lt(mod(Y+floor(T*31),180),{band}),b(X+{offset}*sin(T*42),Y),b(X,Y))'");
            }
        }

        if (VideoEditorFlickerCheck?.IsChecked == true)
        {
            var amount = Math.Clamp(VideoEditorFlickerSlider?.Value ?? 15, 0, 100) / 100.0;
            if (amount > 0)
            {
                var strength = (amount * 0.18).ToString("0.####", CultureInfo.InvariantCulture);
                filters.Add($"eq=brightness='sin(2*PI*8*t)*{strength}'");
            }
        }

        if (VideoEditorTapeNoiseCheck?.IsChecked == true)
        {
            var amount = Math.Clamp(VideoEditorTapeNoiseSlider?.Value ?? 12, 0, 100);
            if (amount > 0)
            {
                var strength = Math.Max(1, (int)Math.Round(amount * 0.35));
                filters.Add($"noise=alls={strength}:allf=t+u");
            }
        }

        // Glitch
        if (VideoEditorRgbSplitCheck?.IsChecked == true)
        {
            var shift = Math.Clamp(VideoEditorRgbSplitSlider?.Value ?? 4, 0, 20);
            if (shift > 0)
                filters.Add($"rgbashift=rh={shift}:bh=-{shift}:gh=0");
        }

        if (VideoEditorGlitchCheck?.IsChecked == true)
        {
            var amount = Math.Clamp(VideoEditorGlitchSlider?.Value ?? 12, 0, 100) / 100.0;
            if (amount > 0)
            {
                var offset = Math.Max(1, (int)Math.Round(amount * 60));
                var band = Math.Max(2, (int)Math.Round(2 + amount * 12));
                filters.Add(
                    $"geq=r='if(lt(mod(Y+floor(T*47),220),{band}),r(X+{offset}*sin(T*73),Y),r(X,Y))':" +
                    $"g='if(lt(mod(Y+floor(T*47),220),{band}),g(X+{offset}*sin(T*73),Y),g(X,Y))':" +
                    $"b='if(lt(mod(Y+floor(T*47),220),{band}),b(X+{offset}*sin(T*73),Y),b(X,Y))'");
            }
        }

        // Color
        if (VideoEditorHueCheck?.IsChecked == true)
        {
            var hue = Math.Clamp(VideoEditorHueSlider?.Value ?? 0, -180, 180);
            if (Math.Abs(hue) > 0.01)
                filters.Add($"hue=h={hue.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        if (VideoEditorSaturationCheck?.IsChecked == true)
        {
            var saturation = Math.Clamp(VideoEditorSaturationSlider?.Value ?? 100, 0, 200) / 100.0;
            filters.Add($"eq=saturation={saturation.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        if (VideoEditorContrastCheck?.IsChecked == true)
        {
            var contrast = Math.Clamp(VideoEditorContrastSlider?.Value ?? 100, 50, 150) / 100.0;
            filters.Add($"eq=contrast={contrast.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        // Custom frei0r plugin. The plugin must actually be installed in the
        // FFmpeg/frei0r runtime; no fake overlay is used.
        if (VideoEditorFrei0rCheck?.IsChecked == true)
        {
            var plugin = VideoEditorFrei0rComboBox?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(plugin))
            {
                var parameters = VideoEditorFrei0rParameters?.Text?.Trim();
                filters.Add(string.IsNullOrWhiteSpace(parameters)
                    ? $"frei0r={plugin}"
                    : $"frei0r={plugin}:{parameters}");
            }
        }

        if (realtimePreview)
        {
            var previewWidth = realtimeWidth ?? VideoEditorRealtimeWidth;
            var previewHeight = realtimeHeight ?? VideoEditorRealtimeHeight;
            filters.Add($"scale={previewWidth}:{previewHeight}:force_original_aspect_ratio=decrease,pad={previewWidth}:{previewHeight}:(ow-iw)/2:(oh-ih)/2");
        }

        return string.Join(',', filters);
    }

    private sealed record VideoEditorRealtimeEffectState(
        bool Crt, bool Scanlines, double ScanlineAmount,
        bool Vignette, double VignetteAmount,
        bool Chroma, int ChromaAmount,
        bool Hue, double HueDegrees,
        bool Flicker, double FlickerAmount,
        bool Tear, double TearAmount);

    private VideoEditorRealtimeEffectState CaptureVideoEditorRealtimeEffectState()
    {
        return new VideoEditorRealtimeEffectState(
            VideoEditorCrtCheckBox?.IsChecked == true,
            VideoEditorScanlinesCheck?.IsChecked == true,
            Math.Clamp(VideoEditorScanlineSlider?.Value ?? 25, 0, 100) / 100.0,
            VideoEditorVignetteCheck?.IsChecked == true,
            Math.Clamp(VideoEditorVignetteSlider?.Value ?? 5, 0, 100) / 100.0,
            VideoEditorChromaCheck?.IsChecked == true,
            (int)Math.Clamp(VideoEditorChromaSlider?.Value ?? 4, 0, 12),
            VideoEditorHueCheck?.IsChecked == true,
            Math.Clamp(VideoEditorHueSlider?.Value ?? 0, -180, 180),
            VideoEditorFlickerCheck?.IsChecked == true,
            Math.Clamp(VideoEditorFlickerSlider?.Value ?? 15, 0, 100) / 100.0,
            VideoEditorTearCheck?.IsChecked == true,
            Math.Clamp(VideoEditorTearSlider?.Value ?? 10, 0, 100) / 100.0);
    }

    private static byte ClampByte(double v) =>
        (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rf=r/255.0, gf=g/255.0, bf=b/255.0;
        double max=Math.Max(rf,Math.Max(gf,bf)), min=Math.Min(rf,Math.Min(gf,bf)), d=max-min;
        h=0;
        if (d>1e-9)
        {
            if (max==rf) h=60*((gf-bf)/d%6);
            else if (max==gf) h=60*((bf-rf)/d+2);
            else h=60*((rf-gf)/d+4);
        }
        if(h<0) h+=360;
        s=max<=1e-9?0:d/max; v=max;
    }

    private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        h=((h%360)+360)%360; double c=v*s, x=c*(1-Math.Abs((h/60)%2-1)), m=v-c;
        double rr=0,gg=0,bb=0;
        if(h<60){rr=c;gg=x;} else if(h<120){rr=x;gg=c;} else if(h<180){gg=c;bb=x;}
        else if(h<240){gg=x;bb=c;} else if(h<300){rr=x;bb=c;} else {rr=c;bb=x;}
        r=ClampByte((rr+m)*255); g=ClampByte((gg+m)*255); b=ClampByte((bb+m)*255);
    }

    private static void ApplyVideoEditorRealtimeEffects(byte[] pixels, VideoEditorRealtimeEffectState s, long frame)
    {
        int w=VideoEditorRealtimeWidth, h=VideoEditorRealtimeHeight;
        double flicker = s.Flicker ? Math.Sin((frame / 30.0) * Math.PI * 18.0) * s.FlickerAmount * 18.0 : 0;
        int chroma=s.Chroma ? s.ChromaAmount : 0;
        byte[] original = chroma>0 ? (byte[])pixels.Clone() : pixels;

        for(int y=0;y<h;y++)
        {
            double ny=(y/(double)(h-1))*2-1;
            for(int x=0;x<w;x++)
            {
                int i=(y*w+x)*4;
                byte b=original[i], g=original[i+1], r=original[i+2];

                if(chroma>0)
                {
                    int rx=Math.Clamp(x+chroma,0,w-1)*4 + y*w*4;
                    int bx=Math.Clamp(x-chroma,0,w-1)*4 + y*w*4;
                    r=original[rx+2]; b=original[bx];
                }

                if(s.Crt)
                {
                    r=ClampByte((r-128)*1.05+128-5);
                    g=ClampByte((g-128)*1.05+128-5);
                    b=ClampByte((b-128)*1.05+128-5);
                    double lum=(0.2126*r+0.7152*g+0.0722*b);
                    r=ClampByte(lum+(r-lum)*0.85); g=ClampByte(lum+(g-lum)*0.85); b=ClampByte(lum+(b-lum)*0.85);
                }

                if(s.Scanlines && (y%2==1))
                {
                    double k=1.0-s.ScanlineAmount*0.45;
                    r=ClampByte(r*k); g=ClampByte(g*k); b=ClampByte(b*k);
                }

                if(s.Vignette)
                {
                    double dist=Math.Sqrt(ny*ny+(((x/(double)(w-1))*2-1)*((x/(double)(w-1))*2-1)));
                    double k=1.0-Math.Clamp((dist-0.35)/0.8,0,1)*s.VignetteAmount*0.75;
                    r=ClampByte(r*k); g=ClampByte(g*k); b=ClampByte(b*k);
                }

                if(s.Hue)
                {
                    RgbToHsv(r,g,b,out var hh,out var ss,out var vv);
                    HsvToRgb(hh+s.HueDegrees,ss,vv,out r,out g,out b);
                }

                if(Math.Abs(flicker)>0)
                {
                    r=ClampByte(r+flicker); g=ClampByte(g+flicker); b=ClampByte(b+flicker);
                }

                pixels[i]=b; pixels[i+1]=g; pixels[i+2]=r;
            }
        }

        if(s.Tear && s.TearAmount>0)
        {
            int bands=Math.Max(1,(int)(s.TearAmount*8));
            for(int band=0;band<bands;band++)
            {
                int y=(int)((frame*7+band*97)%h);
                int offset=(int)(Math.Sin(frame*0.31+band)*s.TearAmount*30);
                var row=new byte[w*4];
                Buffer.BlockCopy(pixels,y*w*4,row,0,row.Length);
                for(int x=0;x<w;x++)
                {
                    int sx=((x-offset)%w+w)%w;
                    Buffer.BlockCopy(row,sx*4,pixels,(y*w+x)*4,4);
                }
            }
        }
    }

    private string GetUniqueVideoEditorOutput(string? originalName)
    {
        Directory.CreateDirectory(VideoEditorRoot);
        var baseName = SanitizeVideoBaseName(originalName ?? "");
        if (string.IsNullOrWhiteSpace(baseName))
        {
            int n = 1; string p;
            do { p = Path.Combine(VideoEditorRoot, $"modhub_{n}.mp4"); n++; } while (File.Exists(p));
            return p;
        }
        var first = Path.Combine(VideoEditorRoot, baseName + ".mp4");
        if (!File.Exists(first)) return first;
        int i = 2;
        while (true) { var p = Path.Combine(VideoEditorRoot, $"{baseName}_{i}.mp4"); if (!File.Exists(p)) return p; i++; }
    }

    private async void VideoEditorConvertButton_Click(object sender, RoutedEventArgs e)
    {
        var input = _videoEditorInputFile;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) return;
        var original = _videoEditorOriginalName;
        var output = GetUniqueVideoEditorOutput(original);
        try
        {
            // The source preview is no longer needed while FFmpeg works. Release the
            // media first so the file is not held open and the native video surface
            // does not continue consuming resources during conversion.
            StopAndReleaseVideoEditorPreview();

            Directory.CreateDirectory(VideoEditorRoot);
            var ffmpeg = RequireFfmpegForVideoEditor();
            // Capture every UI-dependent option on the WPF thread before moving the
            // actual FFmpeg process to a worker thread. Reading controls from Task.Run
            // causes the cross-thread exception shown by the previous build.
            var filter = GetVideoEditorFilter();
            SetOperationBusy(true, L("Converting video…"), 0, Path.GetFileName(input));
            await Task.Yield();
            var ok = await RunVideoEditorFfmpegAsync(ffmpeg, input, output, filter, (percent, status) =>
            {
                SetOperationBusy(true, L("Converting video…"), percent, status);
            });
            if (!ok) { try { if (File.Exists(output)) File.Delete(output); } catch { } throw new InvalidOperationException(L("FFmpeg could not convert the video. Check that the source MP4 is valid.")); }
            SetOperationBusy(true, L("Updating Videos library…"), null, Path.GetFileName(output)); await Task.Yield();
            RefreshVideosPage();
            SetOperationBusy(false);
            MessageBox.Show(this, L("Video added to the ModHub Videos folder:\n\n{0}", Path.GetFileName(output)), L("Video Editor"), MessageBoxButton.OK, MessageBoxImage.Information);
            ClearVideoEditorInput();
        }
        catch (Exception ex) { SetOperationBusy(false); MessageBox.Show(this, ex.Message, L("Video Editor"), MessageBoxButton.OK, MessageBoxImage.Error); RefreshVideoEditorUi(); }
    }

    private void ClearVideoEditorInput()
    {
        StopAndReleaseVideoEditorPreview();
        try { if (!string.IsNullOrWhiteSpace(_videoEditorTempFile) && File.Exists(_videoEditorTempFile)) File.Delete(_videoEditorTempFile); } catch { }
        _videoEditorPreviewFile = null; _videoEditorPreviewPreparing = false; _videoEditorPreviewFallbackAttempted = false;
        _videoEditorTempFile = null; _videoEditorInputFile = null; _videoEditorOriginalName = null; _videoEditorPreviewLoaded = false; _videoEditorPreviewDuration = TimeSpan.Zero;
        _videoEditorTimelineUpdating = true;
        if (VideoEditorTimelineSlider != null) { VideoEditorTimelineSlider.Value = 0; VideoEditorTimelineSlider.Maximum = 1; }
        if (VideoEditorTimelineText != null) VideoEditorTimelineText.Text = "00:00 / 00:00";
        _videoEditorTimelineUpdating = false;
        if (VideoEditorPlayAudioCheckBox != null)
            VideoEditorPlayAudioCheckBox.IsChecked = false;
        RefreshVideoEditorUi();
    }

    private void VideoEditorPlayAudio_Changed(object sender, RoutedEventArgs e)
    {
        if (VideoEditorPlayAudioCheckBox == null)
            return;

        var position = TimeSpan.FromSeconds(Math.Clamp(
            VideoEditorTimelineSlider?.Value ?? 0,
            0,
            _videoEditorSourceDuration.TotalSeconds));

        // Changing the toggle should not restart the picture pipeline. It only
        // starts/stops the hidden audio clock at the current video position.
        SyncVideoEditorAudio(position, _videoEditorIsPlaying);
    }

    private void VideoEditorClearButton_Click(object sender, RoutedEventArgs e) => ClearVideoEditorInput();

    private void SetVideoEditorDownloadProgress(bool visible, double percent = 0, string status = "")
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => SetVideoEditorDownloadProgress(visible, percent, status)));
            return;
        }

        if (VideoEditorDownloadProgressBorder == null) return;
        VideoEditorDownloadProgressBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        VideoEditorDownloadProgressBar.Value = Math.Clamp(percent, 0, 100);
        if (!string.IsNullOrWhiteSpace(status)) VideoEditorDownloadProgressText.Text = status;
    }

    private static string FormatDownloadSize(long bytes)
    {
        if (bytes < 1024L * 1024L) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / 1024d / 1024d:0.00} MB";
        return $"{bytes / 1024d / 1024d / 1024d:0.00} GB";
    }

    private static string FormatDownloadSpeed(double? bytesPerSecond)
    {
        if (bytesPerSecond is null || double.IsNaN(bytesPerSecond.Value) || double.IsInfinity(bytesPerSecond.Value) || bytesPerSecond.Value <= 0) return "—";
        return FormatDownloadSize((long)bytesPerSecond.Value) + "/s";
    }

    private async void VideoEditorUrlButton_Click(object sender, RoutedEventArgs e)
    {
        // Starting a URL download replaces the current source, so release the
        // preview before validation/download work begins.
        StopAndReleaseVideoEditorPreview();

        var url = VideoEditorUrlTextBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) { MessageBox.Show(this, L("Enter a valid video URL."), L("Video Editor"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        try
        {
            Directory.CreateDirectory(VideoEditorTempRoot);
            Directory.CreateDirectory(GetVideoEditorDownloadsDirectory());
            // Keep the user-facing title clean. The file lives in the temporary
            // directory while yt-dlp works, so there is no need for a download_
            // prefix that would otherwise leak into the final library name.
            var template = Path.Combine(VideoEditorTempRoot, "%(title)s.%(ext)s");
            var ytdlp = RequireYtDlpForVideoEditor();
            SetOperationBusy(true, L("Downloading video…"));
            SetVideoEditorDownloadProgress(true, 0, L("Downloaded 0 MB | Total — | —/s"));
            await Task.Yield();
            var downloaded = await DownloadVideoUrlAsync(ytdlp, url, template);
            if (string.IsNullOrWhiteSpace(downloaded) || !File.Exists(downloaded)) throw new InvalidOperationException(L("The video could not be downloaded from that URL."));
            var ext = Path.GetExtension(downloaded);
            if (!ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                var mp4 = Path.Combine(VideoEditorTempRoot, Path.GetFileNameWithoutExtension(downloaded) + ".mp4");
                var ffmpeg = RequireFfmpegForVideoEditor();
                var filter = GetVideoEditorCompatibilityFilter();
                SetOperationBusy(true, L("Preparing downloaded video…"));
                SetOperationBusy(true, L("Preparing downloaded video…"), 0, Path.GetFileName(downloaded));
                if (!await RunVideoEditorFfmpegAsync(ffmpeg, downloaded, mp4, filter, (percent, status) => SetOperationBusy(true, L("Preparing downloaded video…"), percent, status))) throw new InvalidOperationException(L("The downloaded video could not be prepared as MP4."));
                try { File.Delete(downloaded); } catch { }
                downloaded = mp4;
            }
            var permanentName = Path.GetFileName(downloaded);
            if (permanentName.StartsWith("download_", StringComparison.OrdinalIgnoreCase))
                permanentName = permanentName["download_".Length..];
            var permanentPath = GetUniqueVideoEditorDownloadPath(Path.Combine(GetVideoEditorDownloadsDirectory(), permanentName));
            File.Move(downloaded, permanentPath);
            _videoEditorTempFile = null;
            _videoEditorInputFile = permanentPath;
            _videoEditorOriginalName = Path.GetFileNameWithoutExtension(permanentPath);
            _videoEditorPreviewLoaded = false; _videoEditorPreviewPreparing = false; _videoEditorPreviewFallbackAttempted = false; _videoEditorPreviewFile = null;
            SetVideoEditorDownloadProgress(false); SetOperationBusy(false); RefreshVideoEditorUi();
        }
        catch (Exception ex) { SetVideoEditorDownloadProgress(false); SetOperationBusy(false); MessageBox.Show(this, ex.Message, L("Video Editor"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task<string?> DownloadVideoUrlAsync(string ytdlp, string url, string template)
    {
        var psi = new ProcessStartInfo { FileName = ytdlp, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--newline");
        psi.ArgumentList.Add("--quiet");
        psi.ArgumentList.Add("--no-warnings");
        psi.ArgumentList.Add("--progress-template");
        psi.ArgumentList.Add("download:%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("bv*+ba/b");
        psi.ArgumentList.Add("--merge-output-format"); psi.ArgumentList.Add("mp4");
        psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(template); psi.ArgumentList.Add(url);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException(L("Could not start yt-dlp."));
        var errorLines = new List<string>();

        async Task ConsumeOutputAsync(StreamReader reader, bool collectErrors)
        {
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                if (line.StartsWith("download:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Substring("download:".Length).Split('|');
                    if (parts.Length >= 6)
                    {
                        if (!long.TryParse(parts[1], out var downloadedBytes)) downloadedBytes = 0;
                        long totalBytes = 0;
                        if (!long.TryParse(parts[2], out totalBytes)) long.TryParse(parts[3], out totalBytes);
                        double? speed = null;
                        if (double.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speedValue)) speed = speedValue;
                        var percent = totalBytes > 0 ? downloadedBytes * 100d / totalBytes : 0;
                        var totalText = totalBytes > 0 ? FormatDownloadSize(totalBytes) : "—";
                        var status = L("Downloaded {0} | Total {1} | {2}", FormatDownloadSize(downloadedBytes), totalText, FormatDownloadSpeed(speed));
                        await Dispatcher.InvokeAsync(() => SetVideoEditorDownloadProgress(true, percent, status));
                    }
                }
                else if (collectErrors && !string.IsNullOrWhiteSpace(line))
                {
                    errorLines.Add(line);
                }
            }
        }

        var stdoutTask = ConsumeOutputAsync(process.StandardOutput, false);
        var stderrTask = ConsumeOutputAsync(process.StandardError, true);
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var details = string.Join(Environment.NewLine, errorLines.TakeLast(8));
            if (!string.IsNullOrWhiteSpace(details)) throw new InvalidOperationException(L("yt-dlp could not download the video.\n\n{0}", details));
            return null;
        }

        var dir = Path.GetDirectoryName(template)!;
        var candidates = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                        VideoEditorDownloadExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        // yt-dlp can create the final file with a slightly different extension
        // or title after merging. Prefer the newest completed video rather than
        // assuming the temporary template name is the final path.
        return candidates.FirstOrDefault();
    }

    private string GetVideoEditorCompatibilityFilter()
    {
        return GetVideoEditorAspect() switch
        {
            "1:1" => "scale=1080:1080:force_original_aspect_ratio=increase,crop=1080:1080",
            "16:9" => "scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080",
            "4:3" => "scale=1440:1080:force_original_aspect_ratio=increase,crop=1440:1080",
            _ => "scale=1920:1080:force_original_aspect_ratio=decrease,pad=ceil(iw/2)*2:ceil(ih/2)*2:(ow-iw)/2:(oh-ih)/2"
        };
    }

    private async Task<bool> CanUseVideoEditorNvencAsync(string ffmpeg)
    {
        try
        {
            if (_videoEditorRenderEngine == null)
            {
                _videoEditorRenderEngine = new VideoEditorRenderEngine();
                _videoEditorRenderEngine.FrameReady += OnVideoEditorRenderFrame;
                _videoEditorRenderEngine.Error += message =>
                    Debug.WriteLine($"Video Editor render engine: {message}");
            }

            await _videoEditorRenderEngine.DetectHardwareAccelerationAsync(
                ffmpeg, CancellationToken.None);

            return _videoEditorRenderEngine.NvencEncodeEnabled;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Video Editor NVENC detection failed: {ex}");
            return false;
        }
    }

    private async Task<bool> RunVideoEditorFfmpegAsync(
        string ffmpeg,
        string input,
        string output,
        string filter,
        Action<double?, string> progress,
        string preset = "medium",
        string crf = "20")
    {
        var useNvenc = await CanUseVideoEditorNvencAsync(ffmpeg);
        var useCudaFilters = useNvenc &&
            _videoEditorRenderEngine?.CudaFiltersEnabled == true;
        var useCudaDecode = useNvenc &&
            _videoEditorRenderEngine?.CudaDecodeEnabled == true;

        var exportFilter = filter;

        // Keep all of the user's effects on the CPU side exactly as defined by
        // GetVideoEditorFilter. Only the final upload/format conversion is moved
        // to CUDA. This avoids silently replacing an effect with a different
        // implementation and keeps preview/export appearance aligned.
        if (useCudaFilters)
            exportFilter += ",hwupload_cuda,scale_cuda=format=yuv420p";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        psi.ArgumentList.Add("-y");

        if (useCudaDecode)
        {
            psi.ArgumentList.Add("-hwaccel");
            psi.ArgumentList.Add("cuda");
        }

        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(input);
        psi.ArgumentList.Add("-vf");
        psi.ArgumentList.Add(exportFilter);

        if (useNvenc)
        {
            // NVENC is NVIDIA's hardware H.264 encoder. NVENC uses its own
            // quality/preset controls rather than libx264's CRF scale.
            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add("h264_nvenc");
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add("p4");
            psi.ArgumentList.Add("-rc");
            psi.ArgumentList.Add("vbr");
            psi.ArgumentList.Add("-cq");
            psi.ArgumentList.Add(crf);
            psi.ArgumentList.Add("-b:v");
            psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("-tune");
            psi.ArgumentList.Add("hq");
        }
        else
        {
            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add("libx264");
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add(preset);
            psi.ArgumentList.Add("-crf");
            psi.ArgumentList.Add(crf);
        }

        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add("160k");
        psi.ArgumentList.Add("-movflags");
        psi.ArgumentList.Add("+faststart");
        psi.ArgumentList.Add("-progress");
        psi.ArgumentList.Add("pipe:1");
        psi.ArgumentList.Add("-nostats");
        psi.ArgumentList.Add(output);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(L("Could not start FFmpeg."));

        var errorLines = new List<string>();
        double? durationSeconds = null;
        long lastOutTimeMs = 0;

        async Task ReadProgressAsync()
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line == null) break;

                if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(
                        line.Substring(12),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var outMs))
                {
                    lastOutTimeMs = Math.Max(0, outMs);

                    if (durationSeconds.HasValue && durationSeconds.Value > 0)
                    {
                        var percent = Math.Clamp(
                            lastOutTimeMs / 1000000d / durationSeconds.Value * 100d,
                            0,
                            99.9);

                        progress(
                            percent,
                            useNvenc
                                ? L("GPU encoding {0:0.0}%", percent)
                                : L("Encoding {0:0.0}%", percent));
                    }
                }
                else if (line.Equals("progress=end", StringComparison.OrdinalIgnoreCase))
                {
                    progress(
                        100,
                        useNvenc
                            ? L("GPU encoding complete")
                            : L("Encoding complete"));
                }
            }
        }

        async Task ReadErrorAsync()
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line == null) break;

                if (line.Contains("Duration:", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(
                        line,
                        @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");

                    if (match.Success &&
                        double.TryParse(
                            match.Groups[1].Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var h) &&
                        double.TryParse(
                            match.Groups[2].Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var m) &&
                        double.TryParse(
                            match.Groups[3].Value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var sec))
                    {
                        durationSeconds = h * 3600d + m * 60d + sec;

                        if (durationSeconds > 0 && lastOutTimeMs > 0)
                        {
                            var percent = Math.Clamp(
                                lastOutTimeMs / 1000000d / durationSeconds.Value * 100d,
                                0,
                                99.9);

                            progress(
                                percent,
                                useNvenc
                                    ? L("GPU encoding {0:0.0}%", percent)
                                    : L("Encoding {0:0.0}%", percent));
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(line) &&
                    (line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("No such", StringComparison.OrdinalIgnoreCase)))
                {
                    errorLines.Add(line);
                }
            }
        }

        progress(
            0,
            useNvenc
                ? L("Starting NVIDIA GPU video conversion…")
                : L("Starting video conversion…"));

        var stdoutTask = ReadProgressAsync();
        var stderrTask = ReadErrorAsync();

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var details = string.Join(Environment.NewLine, errorLines.TakeLast(8));

            if (!string.IsNullOrWhiteSpace(details))
                Debug.WriteLine(details);

            return false;
        }

        progress(
            100,
            useNvenc
                ? L("NVIDIA GPU conversion complete")
                : L("Conversion complete"));

        return true;
    }



    private void VideosGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void VideosGrid_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        try
        {
            SetOperationBusy(true, L("Loading dropped video files…"));
            await Task.Yield();
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                SetOperationBusy(true, ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) ? L("Installing {0}…", Path.GetFileName(file)) : L("Copying {0}…", Path.GetFileName(file)));
                await Task.Yield();
                await Task.Run(() => ImportVideoFile(file));
            }
            SetOperationBusy(true, L("Loading installed videos…"));
            await Task.Yield();
            InvalidateVideoLibraryCache();
            RefreshVideosPage();
            SetOperationBusy(false);
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, ex.Message, L("Videos"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void VideosInstallButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Video files (*.mp4;*.zip)|*.mp4;*.zip|MP4 files (*.mp4)|*.mp4|ZIP files (*.zip)|*.zip", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SetOperationBusy(true, L("Loading video files…"));
            await Task.Yield();
            foreach (var file in dialog.FileNames)
            {
                var ext = Path.GetExtension(file);
                SetOperationBusy(true, ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) ? L("Installing {0}…", Path.GetFileName(file)) : L("Copying {0}…", Path.GetFileName(file)));
                await Task.Yield();
                await Task.Run(() => ImportVideoFile(file));
            }
            SetOperationBusy(true, L("Loading installed videos…"));
            await Task.Yield();
            InvalidateVideoLibraryCache();
            RefreshVideosPage();
            SetOperationBusy(false);
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, ex.Message, L("Videos"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void VideosNexusButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { InitialDirectory = GetDownloadsDirectory(), Filter = "Nexus downloads (*.zip;*.mp4)|*.zip;*.mp4|All files (*.*)|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        try { foreach (var file in dialog.FileNames) ImportVideoFile(file); InvalidateVideoLibraryCache(); RefreshVideosPage(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L("Videos"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void VideosRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetOperationBusy(true, L("Loading installed videos…"));
            await Task.Yield();
            InvalidateVideoLibraryCache();
            RefreshVideosPage();
        }
        finally
        {
            SetOperationBusy(false);
        }
    }

    private void ModManagerGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ModManagerGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        var downloadDir = GetDownloadsDirectory();
        Directory.CreateDirectory(downloadDir);
        foreach (var file in files.Where(IsSupportedModArchive))
        {
            try
            {
                var destination = Path.Combine(downloadDir, Path.GetFileName(file));
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    continue;
                destination = GetUniqueDownloadPath(destination);
                File.Copy(file, destination, false);
                var sourceMeta = GetMo2MetaPath(file);
                if (File.Exists(sourceMeta)) File.Copy(sourceMeta, GetMo2MetaPath(destination), true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        InvalidateDownloadsCache();
        RefreshDownloadsPage();
        RefreshModManager();
        await Task.CompletedTask;
    }

    private string GetVerifiedGameRoot()
    {
        var result = SteamVerification.Verify();
        if (!result.Verified || string.IsNullOrWhiteSpace(result.InstallPath))
            throw new InvalidOperationException(L("Retro Rewind could not be verified as a Steam installation."));
        return result.InstallPath;
    }

    private string GetGameProjectRoot(string gameRoot)
    {
        var candidates = new[] { gameRoot, Path.Combine(gameRoot, "RetroRewind") };
        return candidates.FirstOrDefault(root =>
                   Directory.Exists(Path.Combine(root, "Content", "Paks")) ||
                   Directory.Exists(Path.Combine(root, "Binaries", "Win64")))
               ?? gameRoot;
    }

    private string GetPakModsRoot(string gameRoot) =>
        Path.Combine(GetGameProjectRoot(gameRoot), "Content", "Paks", "~mods");

    private string GetPakVirtualRoot() => PakVirtualRoot;

    private string PakMetadataKey(string path) => "pak:" + Path.GetRelativePath(GetPakVirtualRoot(), path).Replace('\\', '/');

    private void EnsurePakVirtualStore(string gameRoot)
    {
        var store = GetPakVirtualRoot();
        Directory.CreateDirectory(store);
        var gameMods = GetPakModsRoot(gameRoot);
        if (!Directory.Exists(gameMods)) return;

        var metadata = LoadNexusMetadata();
        var metadataChanged = false;
        var migrationChangedFiles = false;
        const string disabledSuffix = ".RRModHub.DISABLED";
        var enabledSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingOrder = LoadPakLoadOrder();

        foreach (var file in Directory.EnumerateFiles(gameMods, "*", SearchOption.TopDirectoryOnly).ToList())
        {
            var name = Path.GetFileName(file);

            if (IsSymbolicLink(file))
            {
                try
                {
                    var info = new FileInfo(file);
                    var linkTargetPath = info.LinkTarget == null
                        ? null
                        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, info.LinkTarget));
                    if (linkTargetPath != null && linkTargetPath.StartsWith(Path.GetFullPath(store) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(linkTargetPath))
                        enabledSources.Add(linkTargetPath);
                    else if (name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !IsPakOrderLinkName(name))
                        enabledSources.Add(Path.Combine(store, name));
                }
                catch { }
                continue;
            }

            var isDisabled = name.EndsWith(disabledSuffix, StringComparison.OrdinalIgnoreCase);
            if (!name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !isDisabled) continue;
            var baseName = isDisabled ? name[..^disabledSuffix.Length] : name;
            if (!baseName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) continue;

            // New numbered manager copies are positional references, not new source PAKs.
            // Resolve them through the persisted global order before removing them.
            if (!isDisabled && TryGetPakOrderIndex(baseName, out var numberedIndex) && numberedIndex > 0 && numberedIndex <= existingOrder.Count)
            {
                var numberedSource = Path.GetFullPath(existingOrder[numberedIndex - 1]);
                if (File.Exists(numberedSource)) enabledSources.Add(numberedSource);
                File.Delete(file);
                migrationChangedFiles = true;
                continue;
            }

            var target = Path.Combine(store, baseName);
            if (!File.Exists(target))
            {
                File.Copy(file, target, false);
                migrationChangedFiles = true;
            }
            var oldKey = MetadataKey(gameRoot, file);
            if (metadata.TryGetValue(oldKey, out var meta))
            {
                metadata.Remove(oldKey);
                metadata[PakMetadataKey(target)] = meta;
                metadataChanged = true;
            }
            if (!isDisabled) enabledSources.Add(Path.GetFullPath(target));
            File.Delete(file);
            migrationChangedFiles = true;
        }
        if (metadataChanged) SaveNexusMetadata(metadata);

        // Normal Mod Manager refreshes must NEVER recreate symbolic links.
        // Existing numbered links are already the source of truth. Rebuild only
        // when this pass actually migrated legacy files into the virtual store.
        if (migrationChangedFiles && enabledSources.Count > 0)
            RebuildPakLinks(gameRoot, enabledSources, forceSingleElevation: true);
    }

    private string GetUe4ssModsRoot(string gameRoot) =>
        Path.Combine(GetGameProjectRoot(gameRoot), "Binaries", "Win64", "ue4ss", "Mods");

    private string GetModMetadataPath() => Path.Combine(GetDownloadsDirectory(), NexusMetadataFileName);

    private string GetNexusDescriptionCacheDirectory() =>
        Path.Combine(ModsRoot, "NexusCache");

    private string GetNexusDescriptionCachePath(string game, int modId)
    {
        var safeGame = string.Concat((game ?? "unknown").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safeGame)) safeGame = "unknown";
        return Path.Combine(GetNexusDescriptionCacheDirectory(), $"{safeGame}_{modId}.json");
    }

    private void SaveNexusDescriptionCache(string game, int modId, string name, string version, string description)
    {
        if (modId <= 0 || string.IsNullOrWhiteSpace(game) || string.IsNullOrWhiteSpace(description))
            return;

        try
        {
            var directory = GetNexusDescriptionCacheDirectory();
            Directory.CreateDirectory(directory);
            var cache = new NexusDescriptionCache
            {
                Name = name ?? "",
                Game = game,
                ModId = modId,
                NexusUrl = $"https://www.nexusmods.com/{Uri.EscapeDataString(game)}/mods/{modId}",
                Version = version ?? "",
                Description = description,
                CachedAtUtc = DateTime.UtcNow
            };
            var path = GetNexusDescriptionCachePath(game, modId);
            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("NexusDescriptionCacheSave", ex);
        }
    }

    private bool TryLoadNexusDescriptionCache(string game, int modId, out NexusDescriptionCache cache)
    {
        cache = new NexusDescriptionCache();
        if (modId <= 0 || string.IsNullOrWhiteSpace(game)) return false;

        try
        {
            var path = GetNexusDescriptionCachePath(game, modId);
            if (!File.Exists(path)) return false;
            var loaded = JsonSerializer.Deserialize<NexusDescriptionCache>(File.ReadAllText(path));
            if (loaded == null || string.IsNullOrWhiteSpace(loaded.Description)) return false;
            cache = loaded;
            return true;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("NexusDescriptionCacheLoad", ex);
            return false;
        }
    }

    private void SaveNexusMetadata(Dictionary<string, NexusModMetadata> data)
    {
        try
        {
            Directory.CreateDirectory(GetDownloadsDirectory());
            File.WriteAllText(GetModMetadataPath(), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static string MetadataKey(string gameRoot, string path) => Path.GetRelativePath(gameRoot, path).Replace('\\', '/');

    private string GetDisplayName(string gameRoot, string path, string fallback, Dictionary<string, NexusModMetadata>? dataOverride = null)
    {
        var data = dataOverride ?? LoadNexusMetadata();
        if (!string.IsNullOrWhiteSpace(path) && data.TryGetValue(PakMetadataKey(path), out var pakMeta))
        {
            if (!string.IsNullOrWhiteSpace(pakMeta.DisplayName)) return pakMeta.DisplayName;
            if (!string.IsNullOrWhiteSpace(pakMeta.Name)) return pakMeta.Name;
        }

        // Conflict scans do not have a game-root path. Do not call
        // Path.GetRelativePath with an empty relativeTo value.
        if (!string.IsNullOrWhiteSpace(gameRoot) && !string.IsNullOrWhiteSpace(path) &&
            data.TryGetValue(MetadataKey(gameRoot, path), out var meta))
        {
            if (!string.IsNullOrWhiteSpace(meta.DisplayName)) return meta.DisplayName;
            if (!string.IsNullOrWhiteSpace(meta.Name)) return meta.Name;
        }
        return fallback;
    }

    private static bool IsPakVersionPath(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, "_versions", StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizePakFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((name ?? "mod").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().Trim('.');
        cleaned = string.Join("_", cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "mod";
        return cleaned.ToLowerInvariant().Replace(" ", "_");
    }

    private static string SanitizePakVersionPart(string version)
    {
        var cleaned = SanitizePakFolderName(string.IsNullOrWhiteSpace(version) ? "unknown" : version);
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }

    private static string GetPakVersionTimestamp(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss");

    private string? FindExistingPakModFamilyFolder(string modName, string? nexusGame, int nexusModId, Dictionary<string, NexusModMetadata> metadata)
    {
        var root = GetPakVirtualRoot();
        if (!Directory.Exists(root)) return null;
        foreach (var family in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsUe4ssSpecialFolderName(Path.GetFileName(family))) continue;
            if (string.Equals(Path.GetFileName(family), "_versions", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var active in Directory.EnumerateFiles(family, "*.pak", SearchOption.AllDirectories))
            {
                if (IsPakVersionPath(active)) continue;
                if (TryLoadPakVersionManifest(GetJsonForPak(active), out var manifest))
                {
                    if (nexusModId > 0 && manifest.NexusModId == nexusModId &&
                        string.Equals(manifest.NexusGame, nexusGame ?? "", StringComparison.OrdinalIgnoreCase)) return family;
                    if (string.Equals(manifest.ModName, modName, StringComparison.OrdinalIgnoreCase)) return family;
                }
                var meta = metadata.GetValueOrDefault(PakMetadataKey(active));
                if (meta != null)
                {
                    if (nexusModId > 0 && meta.ModId == nexusModId && string.Equals(meta.Game, nexusGame ?? "", StringComparison.OrdinalIgnoreCase)) return family;
                    if (string.Equals(meta.Name, modName, StringComparison.OrdinalIgnoreCase) || string.Equals(meta.DisplayName, modName, StringComparison.OrdinalIgnoreCase)) return family;
                }
            }
        }
        return null;
    }

    private string? FindExistingPakPackageFolder(string familyFolder, string? nexusGame, int nexusModId, int nexusFileId, Dictionary<string, NexusModMetadata> metadata)
    {
        if (!Directory.Exists(familyFolder)) return null;
        foreach (var active in Directory.EnumerateFiles(familyFolder, "*.pak", SearchOption.AllDirectories))
        {
            if (IsPakVersionPath(active)) continue;
            if (TryLoadPakVersionManifest(GetJsonForPak(active), out var manifest))
            {
                if (nexusFileId > 0 && manifest.NexusFileId == nexusFileId &&
                    (nexusModId <= 0 || manifest.NexusModId == nexusModId) &&
                    (string.IsNullOrWhiteSpace(nexusGame) || string.Equals(manifest.NexusGame, nexusGame, StringComparison.OrdinalIgnoreCase)))
                    return Path.GetDirectoryName(active);
            }
            var meta = metadata.GetValueOrDefault(PakMetadataKey(active));
            if (meta != null && nexusFileId > 0 && meta.FileId == nexusFileId &&
                (nexusModId <= 0 || meta.ModId == nexusModId) &&
                (string.IsNullOrWhiteSpace(nexusGame) || string.Equals(meta.Game, nexusGame, StringComparison.OrdinalIgnoreCase)))
                return Path.GetDirectoryName(active);
        }
        return null;
    }

    private void EnsurePakFamilyPackageLayout(string familyFolder, string familyName, Dictionary<string, NexusModMetadata> metadata)
    {
        if (!Directory.Exists(familyFolder)) return;
        var directActive = Directory.EnumerateFiles(familyFolder, "*.pak", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (directActive == null) return;
        var directJson = GetJsonForPak(directActive);
        string packageName = "main_mod";
        if (TryLoadPakVersionManifest(directJson, out var manifest) && !string.IsNullOrWhiteSpace(manifest.OriginalPakName))
            packageName = SanitizePakFolderName(Path.GetFileNameWithoutExtension(manifest.OriginalPakName));
        else
            packageName = SanitizePakFolderName(Path.GetFileNameWithoutExtension(directActive));
        if (string.IsNullOrWhiteSpace(packageName) || packageName.Equals("_versions", StringComparison.OrdinalIgnoreCase)) packageName = "main_mod";
        var packageFolder = Path.Combine(familyFolder, packageName);
        if (Directory.Exists(packageFolder))
            packageFolder = Path.Combine(familyFolder, packageName + "_main");
        Directory.CreateDirectory(packageFolder);
        var targetPak = Path.Combine(packageFolder, Path.GetFileName(directActive));
        var targetJson = Path.Combine(packageFolder, Path.GetFileName(directJson));
        var oldVersions = Path.Combine(familyFolder, "_versions");
        var newVersions = Path.Combine(packageFolder, "_versions");
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var enabledTarget = Path.Combine(GetPakModsRoot(gameRoot), Path.GetFileName(directActive));
            var disabledTarget = enabledTarget + ".RRModHub.DISABLED";
            var wasEnabled = File.Exists(enabledTarget) || IsSymbolicLink(enabledTarget);
            if (File.Exists(enabledTarget) || IsSymbolicLink(enabledTarget)) File.Delete(enabledTarget);
            if (File.Exists(disabledTarget)) File.Delete(disabledTarget);
            CopyOrMoveFile(directActive, targetPak);
            if (File.Exists(directJson)) CopyOrMoveFile(directJson, targetJson);
            if (Directory.Exists(oldVersions)) Directory.Move(oldVersions, newVersions);
            metadata.Remove(PakMetadataKey(directActive));
            if (TryLoadPakVersionManifest(targetJson, out var movedManifest))
            {
                var movedMeta = new NexusModMetadata(
                    movedManifest.NexusName.Length > 0 ? movedManifest.NexusName : familyName,
                    movedManifest.NexusGame ?? "", movedManifest.NexusModId, movedManifest.NexusFileId, movedManifest.OriginalPakName)
                {
                    DisplayName = movedManifest.DisplayName,
                    InstalledVersion = movedManifest.Version,
                    LatestVersion = movedManifest.LatestVersion
                };
                metadata[PakMetadataKey(targetPak)] = movedMeta;
            }
            InvalidatePakConflictIndexForPath(directActive);
            SetPakPathEnabled(gameRoot, targetPak, wasEnabled);
        }
        catch
        {
            // Best-effort migration; the original active file remains the source of truth if a move fails.
        }
    }

    private static string GetPakModFolderForPath(string pakPath)
    {
        return Path.GetDirectoryName(pakPath) ?? "";
    }

    private string GetPakModFamilyFolderForPath(string pakPath)
    {
        var directory = Path.GetDirectoryName(pakPath) ?? "";
        var root = PakVirtualRoot;
        if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase)) return directory;
        var current = directory;
        while (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)) return current;
            current = parent ?? "";
        }
        return directory;
    }

    private static string? GetActivePakInModFolder(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder, "*.pak", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }

    private static string GetJsonForPak(string pakPath) => Path.Combine(Path.GetDirectoryName(pakPath) ?? "", Path.GetFileNameWithoutExtension(pakPath) + ".json");

    private static string ComputeMd5(string path)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    private static void CopyOrMoveFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(source, destination);
    }

    private static string GetMo2MetaPath(string downloadPath) => downloadPath + ".meta";

    private static string UnquoteMo2Value(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        return value.Replace("\\\"", "\"");
    }

    private static bool TryImportMo2Meta(string downloadPath, NexusModMetadata? existing, out NexusModMetadata? imported, bool computeHashes = true)
    {
        imported = existing;
        var metaPath = GetMo2MetaPath(downloadPath);
        if (!File.Exists(metaPath)) return false;
        try
        {
            var fields = ParseMo2MetaFields(metaPath);
            if (fields.Count == 0) return false;
            _ = int.TryParse(fields.GetValueOrDefault("modID"), out var modId);
            _ = int.TryParse(fields.GetValueOrDefault("fileID"), out var fileId);
            _ = int.TryParse(fields.GetValueOrDefault("category"), out var category);
            var repository = fields.GetValueOrDefault("repository") ?? "";
            var name = fields.GetValueOrDefault("name") ?? existing?.Name ?? Path.GetFileNameWithoutExtension(downloadPath);
            var game = existing?.Game ?? "";
            var modName = fields.GetValueOrDefault("modName") ?? "";
            var version = fields.GetValueOrDefault("version") ?? "";
            var newest = fields.GetValueOrDefault("newestVersion") ?? "";
            var url = fields.GetValueOrDefault("url") ?? "";
            var fileTime = fields.GetValueOrDefault("fileTime") ?? "";
            var author = fields.GetValueOrDefault("author") ?? "";
            var uploader = fields.GetValueOrDefault("uploader") ?? "";
            var uploaderUrl = fields.GetValueOrDefault("uploaderUrl") ?? "";
            var fileSize = new FileInfo(downloadPath).Length;
            var fileMd5 = computeHashes ? ComputeMd5(downloadPath) : (existing?.FileMd5 ?? "");
            var fileSha256 = computeHashes ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(downloadPath))).ToLowerInvariant() : (existing?.FileSha256 ?? "");
            if (string.IsNullOrWhiteSpace(game) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) game = parts[0];
            }
            var nexusUrl = modId > 0 && !string.IsNullOrWhiteSpace(game)
                ? $"https://www.nexusmods.com/{Uri.EscapeDataString(game)}/mods/{modId}"
                : (existing?.NexusUrl ?? "");
            imported = new NexusModMetadata(
                string.IsNullOrWhiteSpace(name) ? (existing?.Name ?? "") : name,
                game,
                modId > 0 ? modId : existing?.ModId ?? 0,
                fileId > 0 ? fileId : existing?.FileId ?? 0,
                Path.GetFileName(downloadPath))
            {
                DisplayName = existing?.DisplayName ?? "",
                InstalledVersion = existing?.InstalledVersion ?? "",
                LatestVersion = string.IsNullOrWhiteSpace(version) ? (existing?.LatestVersion ?? "") : version,
                Description = fields.GetValueOrDefault("description") ?? existing?.Description ?? "",
                DownloadedAtUtc = existing?.DownloadedAtUtc ?? File.GetLastWriteTimeUtc(downloadPath),
                Endorsed = existing?.Endorsed,
                Tracked = existing?.Tracked,
                FilesCount = existing?.FilesCount ?? -1,
                NexusCurrentFileCount = existing?.NexusCurrentFileCount ?? -1,
                Author = string.IsNullOrWhiteSpace(author) ? (existing?.Author ?? "") : author,
                Repository = string.IsNullOrWhiteSpace(repository) ? (existing?.Repository ?? "") : repository,
                NexusUrl = nexusUrl,
                ModName = string.IsNullOrWhiteSpace(modName) ? (existing?.ModName ?? "") : modName,
                Uploader = string.IsNullOrWhiteSpace(uploader) ? (existing?.Uploader ?? "") : uploader,
                UploaderUrl = string.IsNullOrWhiteSpace(uploaderUrl) ? (existing?.UploaderUrl ?? "") : uploaderUrl,
                NewestVersion = string.IsNullOrWhiteSpace(newest) ? (existing?.NewestVersion ?? "") : newest,
                FileTime = fileTime,
                FileMd5 = fileMd5,
                FileSha256 = fileSha256,
                FileSize = fileSize,
                Category = fields.ContainsKey("category") ? category : (existing?.Category ?? -1),
                Mo2MetaFields = fields
            };
            return true;
        }
        catch { imported = existing; return false; }
    }

    private NexusModMetadata? ImportMo2MetaForDownload(string downloadPath, bool save = true, bool computeHashes = true)
    {
        var data = LoadNexusMetadata();
        var key = "_download:" + Path.GetFileName(downloadPath);
        var existing = data.GetValueOrDefault(key) ?? data.Values.FirstOrDefault(m => string.Equals(m.ArchivePath, Path.GetFileName(downloadPath), StringComparison.OrdinalIgnoreCase));
        if (!TryImportMo2Meta(downloadPath, existing, out var imported, computeHashes) || imported == null) return existing;
        data[key] = imported;
        if (save) SaveNexusMetadata(data);
        return imported;
    }

    private PakVersionManifest BuildPakVersionManifest(string pakPath, string modName, string version, NexusModMetadata? meta, string? originalPakName = null)
    {
        var installedUtc = DateTime.UtcNow;
        var manifest = new PakVersionManifest
        {
            ModName = modName,
            Version = string.IsNullOrWhiteSpace(version) ? "Unknown" : version,
            OriginalPakName = originalPakName ?? Path.GetFileName(pakPath),
            PakFileName = Path.GetFileName(pakPath),
            InstalledAtUtc = installedUtc,
            InstalledAtLocal = installedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            Md5 = ComputeMd5(pakPath),
            NexusGame = meta?.Game,
            NexusModId = meta?.ModId ?? 0,
            NexusFileId = meta?.FileId ?? 0,
            NexusCurrentFileCount = meta?.NexusCurrentFileCount ?? -1,
            NexusName = meta?.Name ?? modName,
            DisplayName = meta?.DisplayName ?? "",
            LatestVersion = meta?.LatestVersion ?? version,
            Repository = meta?.Repository ?? "",
            NexusUrl = meta?.NexusUrl ?? "",
            NexusModName = meta?.ModName ?? "",
            Description = meta?.Description ?? "",
            Author = meta?.Author ?? "",
            Uploader = meta?.Uploader ?? "",
            UploaderUrl = meta?.UploaderUrl ?? "",
            NewestVersion = meta?.NewestVersion ?? "",
            FileTime = meta?.FileTime ?? "",
            FileMd5 = meta?.FileMd5 ?? "",
            FileSha256 = meta?.FileSha256 ?? "",
            FileSize = meta?.FileSize > 0 ? meta.FileSize : new FileInfo(pakPath).Length,
            Category = meta?.Category ?? -1,
            Mo2MetaFields = meta?.Mo2MetaFields != null ? new Dictionary<string, string>(meta.Mo2MetaFields, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase)
        };

        if (TryReadPakWithBuiltInReader(pakPath, out var files, out var hashes, out _))
            manifest.ConflictFiles = files.Select(f => new PakConflictFile(f, hashes.GetValueOrDefault(f, ""))).ToList();
        return manifest;
    }

    private static void SavePakVersionManifest(string jsonPath, PakVersionManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool TryLoadPakVersionManifest(string jsonPath, out PakVersionManifest manifest)
    {
        manifest = new PakVersionManifest();
        try
        {
            if (!File.Exists(jsonPath)) return false;
            manifest = JsonSerializer.Deserialize<PakVersionManifest>(File.ReadAllText(jsonPath)) ?? new PakVersionManifest();
            return true;
        }
        catch { return false; }
    }

    private List<PakVersionInfo> GetOtherPakVersions(ModEntry mod)
    {
        var folder = GetPakModFolderForPath(mod.Path);
        var versions = Path.Combine(folder, "_versions");
        if (!Directory.Exists(versions)) return new List<PakVersionInfo>();
        var result = new List<PakVersionInfo>();
        foreach (var pak in Directory.EnumerateFiles(versions, "*.pak", SearchOption.TopDirectoryOnly))
        {
            var json = GetJsonForPak(pak);
            if (TryLoadPakVersionManifest(json, out var manifest))
                result.Add(new PakVersionInfo(manifest.ModName, manifest.Version, manifest.InstalledAtLocal, pak, json));
            else
                result.Add(new PakVersionInfo(Path.GetFileNameWithoutExtension(pak), "Unknown", File.GetLastWriteTime(pak).ToString("yyyy-MM-dd HH:mm:ss"), pak, json));
        }
        return result.OrderByDescending(v => v.Date, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void WriteActivePakManifest(string pakPath, NexusModMetadata? meta, string modName, string version, string? originalPakName = null)
    {
        var manifest = BuildPakVersionManifest(pakPath, modName, version, meta, originalPakName);
        SavePakVersionManifest(GetJsonForPak(pakPath), manifest);
    }

    private void EnsurePakJsonManifests()
    {
        var root = GetPakVirtualRoot();
        if (!Directory.Exists(root)) return;
        var metadata = LoadNexusMetadata();
        var changed = false;
        foreach (var pak in Directory.EnumerateFiles(root, "*.pak", SearchOption.AllDirectories))
        {
            var json = GetJsonForPak(pak);
            if (File.Exists(json)) continue;
            try
            {
                var meta = metadata.GetValueOrDefault(PakMetadataKey(pak));
                var fallbackName = meta?.Name;
                if (string.IsNullOrWhiteSpace(fallbackName))
                {
                    var manifest = TryLoadPakVersionManifest(json, out var existing) ? existing : null;
                    fallbackName = manifest?.ModName;
                }
                if (string.IsNullOrWhiteSpace(fallbackName))
                    fallbackName = Path.GetFileNameWithoutExtension(pak);
                var version = meta?.InstalledVersion;
                if (string.IsNullOrWhiteSpace(version)) version = "Unknown";
                WriteActivePakManifest(pak, meta, fallbackName!, version!);
                changed = true;
            }
            catch (Exception ex)
            {
                CrashLogger.Write("EnsurePakJsonManifest", ex);
            }
        }
        if (changed) SaveNexusMetadata(metadata);
    }

    private List<string> LoadPakLoadOrder()
    {
        try
        {
            if (!File.Exists(PakLoadOrderFile)) return new List<string>();
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(PakLoadOrderFile))?
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).ToList() ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    private void SavePakLoadOrder(IEnumerable<string> paths)
    {
        try { Directory.CreateDirectory(ModsRoot); File.WriteAllText(PakLoadOrderFile, JsonSerializer.Serialize(paths.ToList(), new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { CrashLogger.Write("SavePakLoadOrder", ex); }
    }

    private List<string> GetOrderedPakPaths()
    {
        var store = GetPakVirtualRoot(); if (!Directory.Exists(store)) return new List<string>();
        var all = Directory.EnumerateFiles(store, "*.pak", SearchOption.AllDirectories).Where(p => !IsPakVersionPath(p)).Select(Path.GetFullPath).ToList();
        var saved = LoadPakLoadOrder(); var result = new List<string>();
        foreach (var p in saved) if (all.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
        foreach (var p in all.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)) if (!result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
        if (!saved.SequenceEqual(result, StringComparer.OrdinalIgnoreCase)) SavePakLoadOrder(result);
        return result;
    }

    private static bool TryGetPakOrderIndex(string name, out int index)
    {
        index = 0;
        var match = Regex.Match(name, @"^RRModHub_(\d+)_p\.pak$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out index) && index > 0;
    }

    private bool IsPakSourceEnabled(string gameMods, string source)
    {
        if (!Directory.Exists(gameMods)) return false;
        source = Path.GetFullPath(source);
        var order = GetOrderedPakPaths();
        foreach (var file in Directory.EnumerateFiles(gameMods, "*.pak", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var name = Path.GetFileName(file);
                if (TryGetPakOrderIndex(name, out var index) && index <= order.Count && string.Equals(order[index - 1], source, StringComparison.OrdinalIgnoreCase))
                    return true;

                var info = new FileInfo(file);
                if (info.LinkTarget != null && string.Equals(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, info.LinkTarget)), source, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
        }
        var legacy = Path.Combine(gameMods, Path.GetFileName(source));
        return File.Exists(legacy) && !IsSymbolicLink(legacy);
    }

    private HashSet<string> GetEnabledPakSources(string gameMods)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(gameMods)) return result;
        var store = Path.GetFullPath(GetPakVirtualRoot()) + Path.DirectorySeparatorChar;
        var order = GetOrderedPakPaths();
        foreach (var file in Directory.EnumerateFiles(gameMods, "*.pak", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var name = Path.GetFileName(file);
                if (TryGetPakOrderIndex(name, out var index) && index <= order.Count)
                {
                    var positionalSource = Path.GetFullPath(order[index - 1]);
                    if (File.Exists(positionalSource)) result.Add(positionalSource);
                    continue;
                }

                var info = new FileInfo(file);
                string? target = info.LinkTarget == null ? null : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, info.LinkTarget));
                if (target != null && target.StartsWith(store, StringComparison.OrdinalIgnoreCase) && File.Exists(target)) result.Add(target);
                else if (target == null)
                {
                    var candidate = Path.Combine(GetPakVirtualRoot(), name);
                    if (File.Exists(candidate)) result.Add(candidate);
                }
            }
            catch { }
        }
        return result;
    }

    private static bool IsPakOrderLinkName(string name) => TryGetPakOrderIndex(name, out _);

    private static bool CreateSymbolicLinkWithElevation(string source, string target)
    {
        source = Path.GetFullPath(source);
        target = Path.GetFullPath(target);
        if (!File.Exists(source))
            throw new FileNotFoundException("The symbolic-link source could not be found.", source);

        // Deleting/renaming existing managed links does NOT require elevation.
        // Do that in the normal process and fail normally if Windows refuses it.
        if (File.Exists(target) || Directory.Exists(target))
            File.Delete(target);

        // Try creating normally first. This allows systems with Developer Mode
        // or SeCreateSymbolicLinkPrivilege to avoid UAC entirely. Only a failed
        // link CREATION is allowed to trigger the elevated helper.
        try
        {
            File.CreateSymbolicLink(target, source);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Fall through to the tiny elevated helper. The main UI process
            // remains unelevated, which keeps nxm:// protocol handling working.
        }
        catch (IOException)
        {
            // Windows can report privilege-related link-creation failures as
            // IOException on some configurations. Try the elevated helper.
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            throw new InvalidOperationException("Could not locate ModHub to request administrator permission for the symbolic link.");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"--rr-create-link {QuoteProcessArgument(source)} {QuoteProcessArgument(target)}",
            WorkingDirectory = AppContext.BaseDirectory
        };
        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the administrator link helper.");
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new UnauthorizedAccessException("Administrator permission was cancelled or symbolic-link creation failed.");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new UnauthorizedAccessException("Administrator permission was cancelled. The symbolic link was not created.", ex);
        }
    }

    private static string QuoteProcessArgument(string value)
        => "\"" + value.Replace("\"", "\\\"") + "\"";

    private void RebuildPakLinks(string gameRoot, IEnumerable<string> enabledSources, bool forceSingleElevation = false)
    {
        var gameMods = GetPakModsRoot(gameRoot);
        Directory.CreateDirectory(gameMods);
        var store = Path.GetFullPath(GetPakVirtualRoot()) + Path.DirectorySeparatorChar;
        var enabled = enabledSources.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = GetOrderedPakPaths();

        // Remove every manager-owned file first. Deletion never requires UAC.
        foreach (var file in Directory.EnumerateFiles(gameMods, "*.pak", SearchOption.TopDirectoryOnly).ToList())
        {
            try
            {
                var info = new FileInfo(file);
                var managed = IsPakOrderLinkName(Path.GetFileName(file));
                if (info.LinkTarget != null)
                {
                    var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, info.LinkTarget));
                    managed |= target.StartsWith(store, StringComparison.OrdinalIgnoreCase);
                }
                if (managed) File.Delete(file);
            }
            catch { }
        }

        var links = new List<(string Source, string Target)>();
        for (var position = 0; position < order.Count; position++)
        {
            var source = order[position];
            if (!enabled.Contains(source) || !File.Exists(source)) continue;

            var target = Path.Combine(gameMods, $"RRModHub_{position + 1:000}_p.pak");
            links.Add((source, target));
        }

        if (links.Count == 0)
            return;

        // Bulk operations intentionally bypass the per-link creation path.
        // This guarantees exactly one elevation request for the entire batch.
        if (forceSingleElevation)
        {
            CreateSymbolicLinksWithSingleElevationPrompt(links);
            return;
        }

        // Normal single-mod operations may create without elevation first and
        // only request elevation when Windows actually requires it.
        var linksNeedingElevation = new List<(string Source, string Target)>();
        foreach (var link in links)
        {
            try
            {
                File.CreateSymbolicLink(link.Target, link.Source);
            }
            catch (UnauthorizedAccessException)
            {
                linksNeedingElevation.Add(link);
            }
            catch (IOException)
            {
                linksNeedingElevation.Add(link);
            }
        }

        if (linksNeedingElevation.Count > 0)
            CreateSymbolicLinksWithSingleElevationPrompt(linksNeedingElevation);
    }

    private void CreateSymbolicLinksWithSingleElevationPrompt(IEnumerable<(string Source, string Target)> links)
    {
        var list = links.ToList();
        if (list.Count == 0) return;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            throw new InvalidOperationException("Could not locate ModHub to request administrator permission for symbolic-link creation.");

        var batchFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RetroRewindModHub",
            $"link-batch-{Guid.NewGuid():N}.json");

        Directory.CreateDirectory(Path.GetDirectoryName(batchFile)!);
        var payload = list.Select(x => new { Source = x.Source, Target = x.Target }).ToList();
        File.WriteAllText(batchFile, JsonSerializer.Serialize(payload));

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"--rr-create-links-batch {QuoteProcessArgument(batchFile)}",
            WorkingDirectory = AppContext.BaseDirectory
        };

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start the administrator link helper.");

            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new UnauthorizedAccessException(
                    "Administrator permission was cancelled or symbolic-link creation failed.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            try { File.Delete(batchFile); } catch { }
            throw new UnauthorizedAccessException(
                "Administrator permission was cancelled. The symbolic links were not created.", ex);
        }
        catch
        {
            try { File.Delete(batchFile); } catch { }
            throw;
        }
    }

    private void ReorderPakLoadOrder(string source, string target)
    {
        ReorderPakDragPaths(new[] { source }, target, false);
    }

    private void ReorderPakDragPaths(IEnumerable<string> sourcePaths, string target, bool insertAfter = false)
    {
        try
        {
            var order = GetOrderedPakPaths();
            var root = GetVerifiedGameRoot();
            // Capture enabled sources BEFORE changing the order. Positional filenames
            // such as RRModHub_002_p.pak refer to the old order until the rebuild runs.
            var enabledSources = GetEnabledPakSources(GetPakModsRoot(root));
            var source = sourcePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(p => order.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToList();
            target = Path.GetFullPath(target);
            if (source.Count == 0 || !order.Contains(target, StringComparer.OrdinalIgnoreCase)) return;

            var sourceGroup = source.Count == 1 ? GetPakGroupPathsForPath(source[0]) : new List<string>();
            var targetGroup = GetPakGroupPathsForPath(target);

            // A single PAK dragged within its own group changes only the group's
            // internal order. It never escapes the group or moves the group anchor.
            if (source.Count == 1 && sourceGroup.Count > 1 && targetGroup.Count > 1 &&
                sourceGroup.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(source[0], target, StringComparison.OrdinalIgnoreCase)) return;
                order.Remove(source[0]);
                var internalTargetIndex = order.FindIndex(p => string.Equals(p, target, StringComparison.OrdinalIgnoreCase));
                if (internalTargetIndex < 0) return;
                if (insertAfter) internalTargetIndex++;
                order.Insert(Math.Clamp(internalTargetIndex, 0, order.Count), source[0]);
            }
            else
            {
                // A grouped mod is a single global load-order unit when its group
                // handle is dragged. A child PAK from a group may not be dropped
                // outside that group.
                if (source.Count == 1 && sourceGroup.Count > 1 &&
                    (targetGroup.Count == 0 || !targetGroup.Contains(source[0], StringComparer.OrdinalIgnoreCase)))
                    return;

                var targetUnit = targetGroup.Count == 0 ? new List<string> { target } : targetGroup;
                if (source.Any(p => targetUnit.Contains(p, StringComparer.OrdinalIgnoreCase))) return;

                order.RemoveAll(p => source.Contains(p, StringComparer.OrdinalIgnoreCase));
                var insertAt = order.FindIndex(p => targetUnit.Contains(p, StringComparer.OrdinalIgnoreCase));
                if (insertAt < 0) return;
                if (insertAfter)
                    insertAt = order.FindLastIndex(p => targetUnit.Contains(p, StringComparer.OrdinalIgnoreCase)) + 1;
                order.InsertRange(insertAt, source);
            }

            SavePakLoadOrder(order);
            RebuildPakLinks(root, enabledSources);
            RefreshModManager();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<string> GetPakGroupPathsForPath(string path)
    {
        path = Path.GetFullPath(path);
        try
        {
            var metadata = LoadNexusMetadata();
            var match = metadata.GetValueOrDefault(PakMetadataKey(path));
            if (match == null || match.ModId <= 0 || string.IsNullOrWhiteSpace(match.Game))
                return new List<string>();

            var key = $"{match.Game.ToLowerInvariant()}:{match.ModId}";
            var result = GetOrderedPakPaths()
                .Where(p =>
                {
                    var meta = metadata.GetValueOrDefault(PakMetadataKey(p));
                    return meta != null && meta.ModId > 0 && !string.IsNullOrWhiteSpace(meta.Game) &&
                           string.Equals($"{meta.Game.ToLowerInvariant()}:{meta.ModId}", key, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            return result.Count > 1 ? result : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string? FindUe4ssDragName(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is FrameworkElement element &&
                element.Tag is string name &&
                !string.IsNullOrWhiteSpace(name))
                return name;
            current = current is Visual || current is Visual3D
                ? VisualTreeHelper.GetParent(current)
                : null;
        }
        return null;
    }

    private void AttachUe4ssDragHandler(FrameworkElement handle, string modName)
    {
        handle.Tag = modName;
        handle.Cursor = Cursors.SizeAll;
    }

    private void Ue4ssReorder_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            return;

        var source = e.OriginalSource as DependencyObject;
        var name = FindUe4ssDragName(source);
        if (string.IsNullOrWhiteSpace(name) ||
            Ue4ssDefaultModNames.Contains(name) ||
            name.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
            return;

        var row = FindPakDragRow(source);
        var list = row == null ? null : FindVisualAncestor<ListBox>(row);
        if (row == null || list == null)
            return;

        _ue4ssDragRow = row;
        _ue4ssDragList = list;
        _ue4ssDragStartPoint = e.GetPosition(this);
        _ue4ssDragSourceName = name;
        _ue4ssDragTargetName = null;
        _ue4ssDragInsertAfter = false;
        _ue4ssDragArmed = true;
        _ue4ssDragActive = false;
    }

    private void Ue4ssReorder_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_ue4ssDragArmed || _ue4ssDragRow == null || _ue4ssDragList == null ||
            e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(this);
        var dx = Math.Abs(current.X - _ue4ssDragStartPoint.X);
        var dy = Math.Abs(current.Y - _ue4ssDragStartPoint.Y);
        if (!_ue4ssDragActive &&
            dx < SystemParameters.MinimumHorizontalDragDistance &&
            dy < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (!_ue4ssDragActive)
        {
            _ue4ssDragActive = true;
            _ue4ssDragRow.Opacity = 0.45;
            Mouse.OverrideCursor = Cursors.SizeAll;
        }

        var hit = InputHitTest(current) as DependencyObject;
        var target = FindUe4ssDragName(hit);
        var targetRow = FindPakDragRow(hit);
        if (!string.IsNullOrWhiteSpace(target) && targetRow != null &&
            !string.Equals(target, _ue4ssDragSourceName, StringComparison.OrdinalIgnoreCase) &&
            !Ue4ssDefaultModNames.Contains(target) &&
            !target.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
        {
            _ue4ssDragTargetName = target;
            var pointInTarget = e.GetPosition(targetRow);
            _ue4ssDragInsertAfter = pointInTarget.Y >= targetRow.ActualHeight / 2.0;
        }
    }

    private void Ue4ssReorder_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_ue4ssDragArmed)
            return;

        var wasDragging = _ue4ssDragActive;
        var source = _ue4ssDragSourceName;
        var target = _ue4ssDragTargetName;
        var insertAfter = _ue4ssDragInsertAfter;
        var row = _ue4ssDragRow;

        _ue4ssDragArmed = false;
        _ue4ssDragActive = false;
        _ue4ssDragRow = null;
        _ue4ssDragList = null;
        _ue4ssDragSourceName = null;
        _ue4ssDragTargetName = null;
        _ue4ssDragInsertAfter = false;
        Mouse.OverrideCursor = null;
        if (row != null) row.Opacity = 1.0;

        if (!wasDragging || string.IsNullOrWhiteSpace(source))
            return;

        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(target))
            ReorderUe4ssDrag(source, target, insertAfter);
    }

    private void ReorderUe4ssDrag(string sourceName, string targetName, bool insertAfter)
    {
        if (Ue4ssDefaultModNames.Contains(sourceName) ||
            Ue4ssDefaultModNames.Contains(targetName) ||
            sourceName.Equals("Keybinds", StringComparison.OrdinalIgnoreCase) ||
            targetName.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var root = GetUe4ssModsRoot(gameRoot);
            EnsureUe4ssModsTxtMatchesInstalledMods(root, gameRoot);

            var order = ReadUe4ssModsTxtOrder(root)
                .Where(n => !Ue4ssDefaultModNames.Contains(n) &&
                            !n.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var sourceIndex = order.FindIndex(n => string.Equals(n, sourceName, StringComparison.OrdinalIgnoreCase));
            var targetIndex = order.FindIndex(n => string.Equals(n, targetName, StringComparison.OrdinalIgnoreCase));
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

            var item = order[sourceIndex];
            order.RemoveAt(sourceIndex);
            targetIndex = order.FindIndex(n => string.Equals(n, targetName, StringComparison.OrdinalIgnoreCase));
            var insertIndex = Math.Clamp(insertAfter ? targetIndex + 1 : targetIndex, 0, order.Count);
            order.Insert(insertIndex, item);

            WriteUe4ssModsTxtOrder(root, order);
            RefreshModManagerAfterStateChange();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("UE4SS Load Order"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AttachPakDragHandlers(FrameworkElement handle, string path, IEnumerable<string>? groupPaths = null)
    {
        var paths = groupPaths?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths is not { Length: > 0 })
            paths = new[] { Path.GetFullPath(path) };

        // Only the six-dot handle is a drag source. The mouse handlers are still
        // registered at Window level, but a drag payload is discoverable only from
        // this handle, preventing accidental reordering when clicking the row.
        handle.Tag = new PakDragInfo(paths);
        handle.Cursor = Cursors.SizeAll;
    }

    private static PakDragInfo? FindPakDragInfo(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is FrameworkElement element && element.Tag is PakDragInfo info)
                return info;
            current = current is Visual || current is Visual3D
                ? VisualTreeHelper.GetParent(current)
                : null;
        }
        return null;
    }

    private static string? FindPakDragTargetPath(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is FrameworkElement element && element.Tag is string path && !string.IsNullOrWhiteSpace(path))
                return path;
            current = current is Visual || current is Visual3D
                ? VisualTreeHelper.GetParent(current)
                : null;
        }
        return null;
    }

    private static Grid? FindPakDragRow(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is Grid grid)
                return grid;
            current = current is Visual || current is Visual3D
                ? VisualTreeHelper.GetParent(current)
                : null;
        }
        return null;
    }

    private void PakReorder_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            return;

        var source = e.OriginalSource as DependencyObject;
        var info = FindPakDragInfo(source);
        if (info == null || info.Paths.Length == 0)
            return;

        var row = FindPakDragRow(source);
        if (row == null)
            return;

        var list = FindVisualAncestor<ListBox>(row);
        if (list == null)
            return;

        _pakDragRow = row;
        _pakDragList = list;
        _pakDragStartPoint = e.GetPosition(this);
        _pakDragPaths = info.Paths;
        _pakDragTargetPath = null;
        _pakDragInsertAfter = false;
        _pakDragArmed = true;
        _pakDragActive = false;
    }

    private void PakReorder_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pakDragArmed || _pakDragRow == null || _pakDragList == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(this);
        var dx = Math.Abs(current.X - _pakDragStartPoint.X);
        var dy = Math.Abs(current.Y - _pakDragStartPoint.Y);
        if (!_pakDragActive &&
            dx < SystemParameters.MinimumHorizontalDragDistance &&
            dy < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (!_pakDragActive)
        {
            _pakDragActive = true;
            _pakDragRow.Opacity = 0.45;
            Mouse.OverrideCursor = Cursors.SizeAll;
        }

        // Determine the row currently underneath the pointer. We deliberately do
        // not reorder the visual tree while dragging; the underlying load order is
        // committed once on mouse-up, which keeps the gesture stable and avoids
        // destroying the element that owns the mouse capture.
        var hit = InputHitTest(current) as DependencyObject;
        var target = FindPakDragTargetPath(hit);
        var targetRow = FindPakDragRow(hit);
        if (!string.IsNullOrWhiteSpace(target) && targetRow != null)
        {
            if (!string.IsNullOrWhiteSpace(target) &&
                !_pakDragPaths.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                _pakDragTargetPath = Path.GetFullPath(target);
                var pointInTarget = e.GetPosition(targetRow);
                _pakDragInsertAfter = pointInTarget.Y >= targetRow.ActualHeight / 2.0;
            }
        }
    }

    private void PakReorder_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_pakDragArmed)
            return;

        var wasDragging = _pakDragActive;
        var sourcePaths = _pakDragPaths;
        var target = _pakDragTargetPath;
        var insertAfter = _pakDragInsertAfter;
        var row = _pakDragRow;

        _pakDragArmed = false;
        _pakDragActive = false;
        _pakDragRow = null;
        _pakDragList = null;
        _pakDragTargetPath = null;
        _pakDragInsertAfter = false;
        _pakDragPaths = Array.Empty<string>();
        Mouse.OverrideCursor = null;
        if (row != null) row.Opacity = 1.0;

        if (!wasDragging)
            return;

        // We handled a genuine drag, so prevent the child Button from interpreting
        // the release as a normal click.
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(target))
            ReorderPakDragPaths(sourcePaths, target, insertAfter);
    }

    private static string? GetPakDragTargetPath(object sender)
    {
        if (sender is not FrameworkElement element) return null;
        if (element.Tag is string path && !string.IsNullOrWhiteSpace(path)) return path;
        if (element.Tag is ModEntry mod && !string.IsNullOrWhiteSpace(mod.Path)) return mod.Path;
        return null;
    }

    private void PakOrderHandle_DragOver(object sender, DragEventArgs e)
    {
        var target = GetPakDragTargetPath(sender);
        var hasSingleSource = e.Data.GetDataPresent("RRModHub.PakPath") && e.Data.GetData("RRModHub.PakPath") is string;
        var hasGroupSource = e.Data.GetDataPresent("RRModHub.PakGroupPaths") && e.Data.GetData("RRModHub.PakGroupPaths") is IEnumerable<string>;
        e.Effects = !string.IsNullOrWhiteSpace(target) && (hasSingleSource || hasGroupSource) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PakOrderHandle_Drop(object sender, DragEventArgs e)
    {
        var target = GetPakDragTargetPath(sender);
        if (!string.IsNullOrWhiteSpace(target))
        {
            if (e.Data.GetData("RRModHub.PakGroupPaths") is IEnumerable<string> groupPaths)
                ReorderPakDragPaths(groupPaths, target);
            else if (e.Data.GetData("RRModHub.PakPath") is string source)
                ReorderPakLoadOrder(source, target);
        }
        e.Handled = true;
    }

    private List<ModEntry> GetPakMods(string gameRoot)
    {
        EnsurePakVirtualStore(gameRoot);
        EnsurePakJsonManifests();
        var store = GetPakVirtualRoot();
        var gameMods = GetPakModsRoot(gameRoot);
        var result = new List<ModEntry>();
        var metadata = LoadNexusMetadata();
        const string disabledSuffix = ".RRModHub.DISABLED";
        foreach (var path in Directory.EnumerateFiles(store, "*.pak", SearchOption.AllDirectories))
        {
            if (IsPakVersionPath(path)) continue;
            var fileName = Path.GetFileName(path);
            var enabledPath = Path.Combine(gameMods, fileName);
            var disabledPath = enabledPath + disabledSuffix;
            var enabled = IsPakSourceEnabled(gameMods, path);
            // The active link is the source of truth. A stale manager-owned
            // .RRModHub.DISABLED marker must never override an actually enabled PAK.
            if (!enabled && File.Exists(disabledPath)) enabled = false;
            var fallback = Path.GetFileNameWithoutExtension(fileName);
            if (TryLoadPakVersionManifest(GetJsonForPak(path), out var manifest) && !string.IsNullOrWhiteSpace(manifest.ModName))
                fallback = manifest.ModName;
            result.Add(new ModEntry(GetDisplayName(gameRoot, path, fallback, metadata), path, enabled, true));
        }
        var order = GetOrderedPakPaths();
        return result.OrderBy(m => { var i = order.FindIndex(p => string.Equals(p, m.Path, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetUe4ssModsTxtPathForOrder(string modsRoot) =>
        Path.Combine(modsRoot, "mods.txt");

    private static List<string> ReadUe4ssModsTxtOrder(string modsRoot)
    {
        var path = GetUe4ssModsTxtPathForOrder(modsRoot);
        if (!File.Exists(path)) return new List<string>();

        var result = new List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (!TryParseUe4ssModsTxtLine(line, out var name, out _)) continue;
            if (!result.Contains(name, StringComparer.OrdinalIgnoreCase))
                result.Add(name);
        }
        return result;
    }

    private static void WriteUe4ssModsTxtOrder(string modsRoot, IReadOnlyList<string> orderedNames)
    {
        var path = GetUe4ssModsTxtPathForOrder(modsRoot);
        if (!File.Exists(path)) return;

        var lines = File.ReadAllLines(path).ToList();
        var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (TryParseUe4ssModsTxtLine(line, out var name, out var enabled))
                states.TryAdd(name, enabled ? "1" : "0");
        }

        var protectedNames = Ue4ssDefaultModNames;
        var userNames = orderedNames
            .Where(n => !protectedNames.Contains(n) && !n.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Preserve the supplied default entries in their original order, then
        // user mods in the requested order, and Keybinds absolutely last.
        var output = new List<string>();
        var original = File.ReadAllLines(path).ToList();

        foreach (var line in original)
        {
            if (TryParseUe4ssModsTxtLine(line, out var name, out _) &&
                protectedNames.Contains(name) &&
                !name.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
                output.Add($"{name} : {(states.TryGetValue(name, out var s) ? s : "1")}");
            else if (!TryParseUe4ssModsTxtLine(line, out _, out _))
                output.Add(line);
        }

        foreach (var name in userNames)
            output.Add($"{name} : {(states.TryGetValue(name, out var s) ? s : "0")}");

        if (states.TryGetValue("Keybinds", out var keybindState))
            output.Add($"Keybinds : {keybindState}");
        else
            output.Add("Keybinds : 1");

        File.WriteAllLines(path, output);
    }

    private void MoveUe4ssMod(string modName, int delta)
    {
        if (Ue4ssDefaultModNames.Contains(modName) ||
            modName.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
            return;

        var gameRoot = GetVerifiedGameRoot();
        var root = GetUe4ssModsRoot(gameRoot);
        EnsureUe4ssModsTxtMatchesInstalledMods(root, gameRoot);
        var order = ReadUe4ssModsTxtOrder(root);

        var users = order
            .Where(n => !Ue4ssDefaultModNames.Contains(n) &&
                        !n.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var index = users.FindIndex(n => n.Equals(modName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        var target = Math.Clamp(index + delta, 0, users.Count - 1);
        if (target == index) return;

        var item = users[index];
        users.RemoveAt(index);
        users.Insert(target, item);

        WriteUe4ssModsTxtOrder(root, users);
        RefreshModManager();
    }

    private List<ModEntry> GetUe4ssMods(string gameRoot)
    {
        var root = GetUe4ssModsRoot(gameRoot);
        if (!Directory.Exists(root)) return new List<ModEntry>();

        var result = new List<ModEntry>();
        var metadata = LoadNexusMetadata();
        foreach (var path in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsUe4ssSpecialFolderName(Path.GetFileName(path))) continue;
            var name = Path.GetFileName(path);
            if (!_showUe4ssDefaultMods && Ue4ssDefaultModNames.Contains(name))
                continue;
            var isDefault = Ue4ssDefaultModNames.Contains(name);
            var enabled = ReadUe4ssModsTxtEnabled(root, name, defaultValue: isDefault ? true : false);
            result.Add(new ModEntry(GetDisplayName(gameRoot, path, name, metadata), path, enabled, false, isDefault));
        }
        var txtOrder = ReadUe4ssModsTxtOrder(root);
        var userOrder = txtOrder
            .Where(n => !Ue4ssDefaultModNames.Contains(n) &&
                        !n.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var defaultOrder = Ue4ssProtectedModsTxtLines
            .Select(line => TryParseUe4ssModsTxtLine(line, out var name, out _) ? name : null)
            .Where(n => n != null && Ue4ssDefaultModNames.Contains(n!))
            .ToList();

        return result
            .OrderBy(m => m.IsUe4ssDefault ? 0 : 1)
            .ThenBy(m => m.IsUe4ssDefault
                ? Math.Max(0, defaultOrder.FindIndex(n => string.Equals(n, m.Name, StringComparison.OrdinalIgnoreCase)))
                : Math.Max(0, userOrder.FindIndex(n => string.Equals(n, Path.GetFileName(m.Path), StringComparison.OrdinalIgnoreCase))))
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RefreshUe4ssListImmediately()
    {
        UpdateUe4ssSpecialFoldersButtons();
        if (_mode != "mods" || !IsLoaded) return;
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var ue = GetUe4ssMods(gameRoot);
            _cachedUe4ssMods = ue;
            if (_cachedPakMods != null && _cachedPendingMods != null)
                ApplyModManagerSnapshot(_cachedPakMods, ue, _cachedPendingMods);
            else
            {
                PopulateModList(Ue4ssModsList, ue, false);
                Ue4ssModsStatus.Text = ue.Count == 0
                    ? L("No UE4SS mods installed | 0 Enabled")
                    : L("{0} UE4SS mods installed | {1} Enabled", ue.Count, ue.Count(m => m.Enabled));
            }
        }
        catch { }
    }

    private string GetPakConflictIndexPath() => Path.Combine(ModsRoot, PakConflictIndexFileName);

    private static object? GetReflectedMember(object instance, string name)
    {
        var type = instance.GetType();
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null) return property.GetValue(instance);
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(instance);
    }

    private static string GetReflectedString(object instance, string name)
    {
        return GetReflectedMember(instance, name)?.ToString() ?? "";
    }

    private static bool TryReadPakWithBuiltInReader(string pakPath, out List<string> files, out Dictionary<string, string> hashes, out string error)
    {
        files = new List<string>();
        hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = "";

        try
        {
            // Unpaker is a managed library bundled through NuGet. It reads the PAK
            // index directly, so users do not need UnrealPak.exe or Unreal Engine.
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "Unpaker", StringComparison.OrdinalIgnoreCase))
                ?? Assembly.Load("Unpaker");
            var readerType = assembly.GetType("Unpaker.PakReader", throwOnError: true)!;
            var create = readerType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 2);
            if (create == null)
                throw new MissingMethodException("The bundled PAK reader does not expose its reader factory.");

            using var stream = File.OpenRead(pakPath);
            var reader = create.Invoke(null, new object?[] { stream, null });
            if (reader == null) throw new InvalidOperationException("The bundled PAK reader could not open the archive.");

            var encrypted = readerType.GetProperty("EncryptedIndex", BindingFlags.Public | BindingFlags.Instance)?.GetValue(reader) as bool?;
            if (encrypted == true)
                throw new InvalidOperationException("The PAK index is encrypted. ModHub cannot inspect encrypted PAK indexes without the game's encryption key.");

            var filesProperty = readerType.GetProperty("Files", BindingFlags.Public | BindingFlags.Instance);
            if (filesProperty?.GetValue(reader) is IEnumerable<string> listedFiles)
            {
                files = listedFiles
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => f.Replace('\\', '/'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // The library stores the Unreal FPakEntry SHA-1 in the index. Read that
            // metadata through reflection so conflict scans do not have to extract or
            // decompress every asset just to determine whether two entries are identical.
            var pak = GetReflectedMember(reader, "_pak");
            var index = pak == null ? null : GetReflectedMember(pak, "Index");
            var entries = index == null ? null : GetReflectedMember(index, "Entries");
            if (entries is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item == null) continue;
                    var key = GetReflectedMember(item, "Key")?.ToString();
                    var entry = GetReflectedMember(item, "Value");
                    if (string.IsNullOrWhiteSpace(key) || entry == null) continue;
                    var hashValue = GetReflectedMember(entry, "Hash");
                    if (hashValue is byte[] bytes && bytes.Length > 0)
                    {
                        hashes[key.Replace('\\', '/')] = Convert.ToHexString(bytes);
                    }
                    else if (hashValue is System.Collections.IEnumerable hashEnumerable)
                    {
                        var bytesList = new List<byte>();
                        foreach (var value in hashEnumerable)
                            if (value is byte b) bytesList.Add(b);
                        if (bytesList.Count > 0)
                            hashes[key.Replace('\\', '/')] = Convert.ToHexString(bytesList.ToArray());
                    }
                }
            }

            return files.Count > 0;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            error = ex.InnerException.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void InvalidatePakConflictIndexForPath(string path)
    {
        try
        {
            var index = LoadPakConflictIndex();
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var prefix = full + Path.DirectorySeparatorChar;
            index.RemoveAll(x => string.Equals(x.PakPath, full, StringComparison.OrdinalIgnoreCase) ||
                                 x.PakPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            SavePakConflictIndex(index);
            _conflictIndex = index;
        }
        catch { }
    }

    private List<PakConflictIndexEntry> LoadPakConflictIndex()
    {
        try
        {
            var path = GetPakConflictIndexPath();
            if (!File.Exists(path)) return new List<PakConflictIndexEntry>();
            var loaded = JsonSerializer.Deserialize<List<PakConflictIndexEntry>>(File.ReadAllText(path)) ?? new List<PakConflictIndexEntry>();
            // v1.0.5 changed the stored hash meaning from extracted SHA-256 to the
            // Unreal entry SHA-1 kept inside the PAK index. Discard the old schema
            // so the next scan rebuilds it with the built-in reader.
            if (loaded.Any(x => x.Files.Any(f => f.ContentHash == null)))
                return new List<PakConflictIndexEntry>();
            return loaded;
        }
        catch { return new List<PakConflictIndexEntry>(); }
    }

    private void SavePakConflictIndex(List<PakConflictIndexEntry> index)
    {
        try
        {
            Directory.CreateDirectory(ModsRoot);
            File.WriteAllText(GetPakConflictIndexPath(), JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { CrashLogger.Write("PakConflictIndexSave", ex); }
    }

    private string GetConflictDisplayName(string pakPath, NexusModMetadata? meta, Dictionary<string, NexusModMetadata> metadata)
    {
        var individual = !string.IsNullOrWhiteSpace(meta?.DisplayName)
            ? meta!.DisplayName
            : (!string.IsNullOrWhiteSpace(meta?.Name)
                ? meta!.Name
                : Path.GetFileNameWithoutExtension(pakPath));

        var group = meta?.GroupDisplayName;
        if (!string.IsNullOrWhiteSpace(group) &&
            !string.Equals(group.Trim(), individual.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return $"{group.Trim()} | {individual.Trim()}";
        }

        return individual;
    }

    private List<PakConflictIndexEntry> ScanInstalledPakConflicts(CancellationToken token, IProgress<string>? progress = null)
    {
        var store = GetPakVirtualRoot();
        if (!Directory.Exists(store)) return new List<PakConflictIndexEntry>();
        var metadata = LoadNexusMetadata();
        var paks = Directory.EnumerateFiles(store, "*.pak", SearchOption.AllDirectories).Where(p => !IsPakVersionPath(p)).ToList();
        var result = new List<PakConflictIndexEntry>();
        var previous = LoadPakConflictIndex().ToDictionary(x => x.PakPath, StringComparer.OrdinalIgnoreCase);

        foreach (var pak in paks)
        {
            token.ThrowIfCancellationRequested();
            var info = new FileInfo(pak);
            var key = Path.GetFullPath(pak);
            if (previous.TryGetValue(key, out var cached) && cached.Length == info.Length && cached.LastWriteUtc == info.LastWriteTimeUtc && cached.Files.Count > 0)
            {
                result.Add(cached);
                progress?.Report($"Using cached index: {Path.GetFileName(pak)}");
                continue;
            }

            progress?.Report($"Reading PAK index: {Path.GetFileName(pak)}");
            if (!TryReadPakWithBuiltInReader(pak, out var files, out var hashes, out var error))
                throw new InvalidOperationException($"Could not read '{Path.GetFileName(pak)}'.\n\n{error}");

            var meta = metadata.GetValueOrDefault(PakMetadataKey(pak));
            var fileRecords = files.Select(f => new PakConflictFile(f, hashes.GetValueOrDefault(f, ""))).ToList();
            result.Add(new PakConflictIndexEntry(
                key,
                Path.GetFileName(pak),
                GetConflictDisplayName(pak, meta, metadata),
                meta?.Game,
                meta?.ModId ?? 0,
                info.Length,
                info.LastWriteTimeUtc,
                fileRecords));
        }

        return result.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Conflict-checker exception: PAKs belonging to the Rogue Unicorn RealMovies
    // mod are allowed to overlap with other RealMovies PAKs. They should still
    // be compared against PAKs from other mods. The token is checked against the
    // PAK paths, not the internal asset paths, because the internal assets often
    // have generic names that do not contain the mod name.
    private const string ConflictExcludedFileToken = "rogueunicorn-realmovies";

    private static bool IsExcludedConflictPakPair(string pakPathA, string pakPathB)
    {
        return pakPathA.Contains(ConflictExcludedFileToken, StringComparison.OrdinalIgnoreCase) &&
               pakPathB.Contains(ConflictExcludedFileToken, StringComparison.OrdinalIgnoreCase);
    }

    private List<PakConflictPair> BuildPakConflictPairs(List<PakConflictIndexEntry> index)
    {
        var pairs = new List<PakConflictPair>();
        for (var i = 0; i < index.Count; i++)
        {
            var a = index[i];
            var mapA = a.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            for (var j = i + 1; j < index.Count; j++)
            {
                var b = index[j];
                var mapB = b.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                // Skip the entire comparison when both PAKs are RealMovies PAKs.
                // RealMovies PAKs must still be compared normally against every
                // non-RealMovies PAK.
                if (IsExcludedConflictPakPair(a.PakPath, b.PakPath))
                    continue;

                var overlaps = mapA.Keys.Intersect(mapB.Keys, StringComparer.OrdinalIgnoreCase)
                    .Select(path => new PakConflictFilePair(path, mapA[path].ContentHash, mapB[path].ContentHash))
                    .ToList();
                if (overlaps.Count > 0)
                    pairs.Add(new PakConflictPair(a.PakPath, a.DisplayName, b.PakPath, b.DisplayName, overlaps));
            }
        }
        return pairs;
    }

    private void ConflictCheckScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.ScrollableHeight <= 0) return;

        // WPF's default mouse-wheel handling moves by several logical lines at a
        // time. Use a pixel target and ease toward it instead.
        var wheelPixels = -e.Delta * 0.55;
        if (!_conflictSmoothScrollRunning)
            _conflictSmoothScrollTarget = viewer.VerticalOffset;

        _conflictSmoothScrollTarget = Math.Clamp(
            _conflictSmoothScrollTarget + wheelPixels,
            0,
            viewer.ScrollableHeight);

        e.Handled = true;
        if (_conflictSmoothScrollRunning) return;

        _conflictSmoothScrollRunning = true;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const double durationMs = 140.0;
        EventHandler? render = null;

        render = (_, _) =>
        {
            if (!viewer.IsLoaded)
            {
                CompositionTarget.Rendering -= render;
                _conflictSmoothScrollRunning = false;
                return;
            }

            // Re-read the target every frame so additional wheel ticks while the
            // animation is running are smoothly accumulated instead of ignored.
            var current = viewer.VerticalOffset;
            var target = Math.Clamp(_conflictSmoothScrollTarget, 0, viewer.ScrollableHeight);
            var distance = target - current;
            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / durationMs, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            var next = current + distance * eased;

            if (Math.Abs(distance) < 0.5 || progress >= 1)
            {
                viewer.ScrollToVerticalOffset(target);
                if (Math.Abs(target - _conflictSmoothScrollTarget) < 0.5)
                {
                    CompositionTarget.Rendering -= render;
                    _conflictSmoothScrollRunning = false;
                    _conflictSmoothScrollTarget = viewer.VerticalOffset;
                }
                else
                {
                    stopwatch.Restart();
                }
            }
            else
            {
                viewer.ScrollToVerticalOffset(next);
            }
        };

        CompositionTarget.Rendering += render;
    }

    private void RefreshConflictCheckPage()
    {
        if (_mode != "conflicts" || !IsLoaded) return;
        _conflictIndex = LoadPakConflictIndex();
        RenderConflictCheckResults(_conflictIndex);
    }

    private void RenderConflictCheckResults(List<PakConflictIndexEntry> index)
    {
        if (ConflictCheckListPanel == null) return;
        ConflictCheckListPanel.Children.Clear();
        var pairs = BuildPakConflictPairs(index);
        ConflictCheckSummary.Text = index.Count == 0
            ? "No indexed PAK files. Scan your installed PAKs to build the conflict index."
            : $"{index.Count} PAK files indexed • {pairs.Count} conflicting pairs detected.";

        if (index.Count == 0) return;
        if (pairs.Count == 0)
        {
            ConflictCheckListPanel.Children.Add(new TextBlock { Text = "✓ No overlapping packaged files were detected.", Foreground = Brushes.LightGreen, Margin = new Thickness(0, 8, 0, 0) });
            return;
        }
        foreach (var pair in pairs)
        {
            var border = new Border { Style = (Style)Resources["CardStyle"], Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10) };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = $"⚠ {pair.DisplayA}", FontWeight = FontWeights.SemiBold, FontSize = 16 });
            panel.Children.Add(new TextBlock { Text = $"{Path.GetFileName(pair.PakA)}  ↔  {pair.DisplayB} ({Path.GetFileName(pair.PakB)})", Foreground = (Brush)FindResource("SecondaryBrush"), Margin = new Thickness(0, 3, 0, 8), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = $"{pair.Files.Count} overlapping file(s)", Foreground = (Brush)FindResource("AccentBrush") });
            var files = new Expander { Header = "Show conflicting files", Margin = new Thickness(0, 8, 0, 0) };
            var list = new ItemsControl();
            foreach (var file in pair.Files.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Take(250))
            {
                var known = !string.IsNullOrWhiteSpace(file.HashA) && !string.IsNullOrWhiteSpace(file.HashB);
                var same = known && string.Equals(file.HashA, file.HashB, StringComparison.OrdinalIgnoreCase);
                var prefix = known ? (same ? "= " : "≠ ") : "? ";
                var foreground = !known ? Brushes.Goldenrod : (same ? Brushes.LightGreen : Brushes.Orange);
                list.Items.Add(new TextBlock { Text = prefix + file.Path, Margin = new Thickness(0, 2, 0, 0), Foreground = foreground });
            }
            files.Content = new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
            panel.Children.Add(files);
            border.Child = panel;
            ConflictCheckListPanel.Children.Add(border);
        }
    }

    private void UpdateConflictCheckToolButton()
    {
        if (ConflictCheckScanButton == null) return;
        ConflictCheckScanButton.Content = "Scan Installed PAKs";
        ConflictCheckScanButton.ToolTip = "Scan installed PAK files using ModHub's built-in PAK reader. No Unreal Engine installation is required.";
    }

    private async void ConflictCheckScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_conflictScanInProgress) return;
        if (_gameActive)
        {
            MessageBox.Show(this, "Conflict scanning is disabled while Retro Rewind is running.", "Conflict Checker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            _conflictScanInProgress = true;
            ConflictCheckScanButton.IsEnabled = false;
            ConflictCheckToolStatus.Text = "Starting PAK scan…";
            var cts = new CancellationTokenSource();
            _conflictScanCts = cts;
            var progress = new Progress<string>(text => ConflictCheckToolStatus.Text = text);
            var index = await Task.Run(() => ScanInstalledPakConflicts(cts.Token, progress), cts.Token);
            _conflictIndex = index;
            SavePakConflictIndex(index);
            RenderConflictCheckResults(index);
            ConflictCheckToolStatus.Text = $"Scan complete. {index.Count} PAK files indexed using the built-in reader.";
        }
        catch (OperationCanceledException)
        {
            ConflictCheckToolStatus.Text = "Conflict scan cancelled.";
        }
        catch (Exception ex)
        {
            ConflictCheckToolStatus.Text = "Conflict scan failed: " + ex.Message;
            CrashLogger.Write("PakConflictScan", ex);
        }
        finally
        {
            _conflictScanInProgress = false;
            _conflictScanCts?.Dispose();
            _conflictScanCts = null;
            ConflictCheckScanButton.IsEnabled = true;
        }
    }

    private void DownloadsRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        InvalidateDownloadsCache();
        RefreshDownloadsPage();
    }

    private void RefreshDownloadsPage()
    {
        if (DownloadsList == null) return;
        BeginDownloadsRefresh();
        RenderDownloadsList(_cachedDownloads ?? new List<DownloadEntry>());
        UpdateDownloadsQueryAllState();
    }

    private void BeginDownloadsRefresh(bool force = false)
    {
        if (_gameActive || _downloadsRefreshInProgress) return;
        if (!force && _cachedDownloads != null && DateTime.UtcNow - _downloadsCacheUpdatedUtc < TimeSpan.FromSeconds(15)) return;
        _downloadsRefreshInProgress = true;
        try { _downloadsRefreshCts?.Cancel(); } catch { }
        var cts = new CancellationTokenSource();
        _downloadsRefreshCts = cts;
        _ = Task.Run(() => BuildDownloadsSnapshot(cts.Token), cts.Token).ContinueWith(t =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _downloadsRefreshInProgress = false;
                if (t.IsCanceled || t.IsFaulted || cts.IsCancellationRequested || _gameActive) return;
                _cachedDownloads = t.Result;
                _downloadsCacheUpdatedUtc = DateTime.UtcNow;
                if (_mode == "downloads") { RenderDownloadsList(_cachedDownloads); UpdateDownloadsQueryAllState(); }
            }));
        }, TaskScheduler.Default);
    }

    private List<DownloadEntry> BuildDownloadsSnapshot(CancellationToken token)
    {
        var result = new List<DownloadEntry>();
        var dir = GetDownloadsDirectory();
        if (!Directory.Exists(dir)) return result;
        var metadata = LoadNexusMetadata();
        var hidden = LoadHiddenDownloadState();
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFileName(file), NexusMetadataFileName, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(file), DownloadStateFileName, StringComparison.OrdinalIgnoreCase)) continue;
            var ext = Path.GetExtension(file);
            string type;
            if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)) type = "Video";
            else if (IsSupportedModArchive(file))
            {
                try
                {
                    var pak = DetectZipModType(file);
                    if (pak == null) continue;
                    type = pak == true ? "PAK" : "UE4SS";
                }
                catch { continue; }
            }
            else continue;

            var fileName = Path.GetFileName(file);
            var importedMeta = ImportMo2MetaForDownload(file, save: true, computeHashes: false);
            if (importedMeta != null) metadata = LoadNexusMetadata();
            var downloadMeta = metadata.GetValueOrDefault("_download:" + fileName)
                ?? metadata.Values.FirstOrDefault(m => string.Equals(m.ArchivePath, fileName, StringComparison.OrdinalIgnoreCase));
            var name = downloadMeta?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(file);
            var version = downloadMeta?.LatestVersion;
            if (string.IsNullOrWhiteSpace(version)) version = downloadMeta?.InstalledVersion;
            if (string.IsNullOrWhiteSpace(version)) version = "Unknown";
            var installed = metadata.Any(kv => !kv.Key.StartsWith("_download:", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(kv.Value.ArchivePath, fileName, StringComparison.OrdinalIgnoreCase));
            var nexusGame = downloadMeta?.Game;
            var nexusModId = downloadMeta?.ModId ?? 0;
            var nexusFileId = downloadMeta?.FileId ?? 0;
            var previouslyInstalled = installed || WasNexusFilePreviouslyInstalled(metadata, nexusGame, nexusModId, nexusFileId, fileName);
            var downloadedAt = downloadMeta?.DownloadedAtUtc ?? File.GetLastWriteTimeUtc(file);
            result.Add(new DownloadEntry(name!, version!, type, installed, previouslyInstalled, file, downloadedAt,
                nexusGame, nexusModId, nexusFileId,
                hidden.Contains(Path.GetFileName(file)), string.IsNullOrWhiteSpace(downloadMeta?.Author) ? null : downloadMeta!.Author));
        }
        return result
            .OrderBy(x => x.Installed ? 2 : x.PreviouslyInstalled ? 1 : 0)
            .ThenByDescending(x => x.DownloadedAtUtc)
            .ToList();
    }

    private bool WasNexusFilePreviouslyInstalled(Dictionary<string, NexusModMetadata> metadata, string? game, int modId, int fileId, string archiveFileName)
    {
        if (modId <= 0) return false;
        foreach (var kv in metadata)
        {
            if (kv.Key.StartsWith("_download:", StringComparison.OrdinalIgnoreCase)) continue;
            var meta = kv.Value;
            if (meta.ModId != modId) continue;
            if (!string.IsNullOrWhiteSpace(game) && !string.Equals(meta.Game, game, StringComparison.OrdinalIgnoreCase)) continue;
            if (fileId > 0 && meta.FileId > 0 && meta.FileId != fileId) continue;
            return true;
        }

        // Installed PAK versions retain their manifests even after the version is
        // no longer active, so they provide a reliable history signal.
        try
        {
            var root = GetPakVirtualRoot();
            if (!Directory.Exists(root)) return false;
            foreach (var json in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            {
                if (!TryLoadPakVersionManifest(json, out var manifest)) continue;
                if (manifest.NexusModId != modId) continue;
                if (!string.IsNullOrWhiteSpace(game) && !string.Equals(manifest.NexusGame, game, StringComparison.OrdinalIgnoreCase)) continue;
                if (fileId > 0 && manifest.NexusFileId > 0 && manifest.NexusFileId != fileId) continue;
                return true;
            }
        }
        catch { }
        return false;
    }

    private void StartDownloadUiTimer()
    {
        _downloadUiTimer?.Stop();
        _downloadUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _downloadUiTimer.Tick += (_, _) =>
        {
            if (_mode == "downloads") RefreshDownloadsPage();
        };
        _downloadUiTimer.Start();
    }

    private List<ActiveDownloadState> GetActiveDownloadsSnapshot()
    {
        lock (_activeDownloadsSync)
            return _activeDownloads.Values.ToList();
    }

    private bool IsNexusDownloadActive(string game, int modId, int fileId)
    {
        var id = $"{game}:{modId}:{fileId}";
        lock (_activeDownloadsSync)
            return _activeDownloads.Keys.Any(k => k.StartsWith(id + ":", StringComparison.OrdinalIgnoreCase));
    }

    private void RenderDownloadsList(IEnumerable<DownloadEntry> entries)
    {
        if (DownloadsList == null) return;
        DownloadsList.Items.Clear();
        var list = entries.Where(x => _showHiddenDownloads || !x.Hidden)
            .OrderBy(x => x.Installed ? 2 : x.PreviouslyInstalled ? 1 : 0)
            .ThenByDescending(x => x.DownloadedAtUtc)
            .ToList();
        var active = GetActiveDownloadsSnapshot();

        foreach (var download in active.OrderByDescending(x => x.StartedUtc))
            DownloadsList.Items.Add(CreateActiveDownloadRow(download));
        foreach (var entry in list)
            DownloadsList.Items.Add(CreateDownloadRow(entry));
    }

    private Grid CreateActiveDownloadRow(ActiveDownloadState entry)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var body = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        body.Children.Add(new TextBlock
        {
            Text = entry.NexusModName,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = entry.NexusModName
        });
        body.Children.Add(new TextBlock
        {
            Text = entry.FileName,
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = entry.FileName
        });
        var bar = new ProgressBar { Height = 8, Margin = new Thickness(0, 6, 0, 0), Minimum = 0, Maximum = 100 };
        var percent = entry.TotalBytes > 0 ? entry.DownloadedBytes * 100.0 / entry.TotalBytes : 0;
        bar.Value = Math.Clamp(percent, 0, 100);
        body.Children.Add(bar);
        Grid.SetColumn(body, 0); row.Children.Add(body);

        var sizeText = new TextBlock
        {
            Text = entry.TotalBytes > 0
                ? $"{FormatDownloadSize(entry.DownloadedBytes)} / {FormatDownloadSize(entry.TotalBytes)}"
                : FormatDownloadSize(entry.DownloadedBytes),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["SecondaryBrush"]
        };
        Grid.SetColumn(sizeText, 1); row.Children.Add(sizeText);

        var speedText = new TextBlock
        {
            Text = FormatDownloadSpeed(entry.BytesPerSecond),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["SecondaryBrush"]
        };
        Grid.SetColumn(speedText, 2); row.Children.Add(speedText);

        var elapsed = DateTime.UtcNow - entry.StartedUtc;
        var eta = entry.TotalBytes > 0 && entry.BytesPerSecond > 0
            ? TimeSpan.FromSeconds(Math.Max(0, (entry.TotalBytes - entry.DownloadedBytes) / entry.BytesPerSecond))
            : TimeSpan.Zero;
        var etaText = entry.TotalBytes > 0 && entry.BytesPerSecond > 0 ? FormatDuration(eta) : "—";
        var details = new TextBlock
        {
            Text = $"{percent:0.0}%  •  {FormatDuration(elapsed)} elapsed  •  {etaText} left  •  {entry.PremiumStatus}",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(details, 3); row.Children.Add(details);

        var type = new TextBlock
        {
            Text = entry.Type,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["SecondaryBrush"],
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(type, 4); row.Children.Add(type);

        var status = new TextBlock
        {
            Text = "Downloading…",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["AccentBrush"]
        };
        Grid.SetColumn(status, 5); row.Children.Add(status);
        return row;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private Grid CreateDownloadRow(DownloadEntry entry)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), Cursor = Cursors.Hand, ToolTip = entry.Path, Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var namePanel = new StackPanel();
        var name = new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var file = new TextBlock { Text = Path.GetFileName(entry.Path) + (entry.Hidden ? "  •  Hidden" : ""), FontSize = 12, Foreground = (Brush)Resources["SecondaryBrush"], TextTrimming = TextTrimming.CharacterEllipsis };
        namePanel.Children.Add(name); namePanel.Children.Add(file);
        Grid.SetColumn(namePanel, 0); row.Children.Add(namePanel);
        var version = new TextBlock { Text = entry.Version, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Resources["SecondaryBrush"], Margin = new Thickness(8,0,4,0) };
        Grid.SetColumn(version, 1); row.Children.Add(version);
        var type = new TextBlock { Text = entry.Type, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Resources["SecondaryBrush"] };
        Grid.SetColumn(type, 2); row.Children.Add(type);
        var statusText = entry.Installed ? "Installed" : entry.PreviouslyInstalled ? "Previously Installed" : "New";
        var statusBrush = entry.Installed || entry.PreviouslyInstalled ? (Brush)Resources["AccentBrush"] : (Brush)Resources["ForegroundBrush"];
        var status = new TextBlock { Text = statusText, VerticalAlignment = VerticalAlignment.Center, Foreground = statusBrush, FontWeight = entry.PreviouslyInstalled || !entry.Installed ? FontWeights.SemiBold : FontWeights.Normal };
        Grid.SetColumn(status, 3); row.Children.Add(status);

        var installIcon = new Border { Width = 18, Height = 18, Background = (Brush)Resources["ForegroundBrush"], OpacityMask = new ImageBrush(LoadModIcon("Install.png")) { Stretch = Stretch.Uniform } };
        var install = new Button { Content = installIcon, Width = 34, Height = 34, Margin = new Thickness(6,0,0,0), Style = (Style)Resources["ModIconButtonStyle"], ToolTip = "Install" };
        install.Tag = entry; install.Click += DownloadInstall_Click; Grid.SetColumn(install, 4); row.Children.Add(install);

        var deleteIcon = new Border { Width = 18, Height = 18, Background = (Brush)Resources["ForegroundBrush"], OpacityMask = new ImageBrush(LoadModIcon("delete.png")) { Stretch = Stretch.Uniform } };
        var delete = new Button { Content = deleteIcon, Width = 34, Height = 34, Margin = new Thickness(6,0,0,0), Style = (Style)Resources["ModIconButtonStyle"], ToolTip = "Delete" };
        delete.Tag = entry; delete.Click += DownloadDelete_Click; Grid.SetColumn(delete, 5); row.Children.Add(delete);

        row.MouseEnter += (_, _) => row.Background = (Brush)Resources["ButtonBackgroundBrush"];
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is Button || e.OriginalSource is Image) return; ShowDownloadSlidePanel(entry); };
        return row;
    }

    private void ShowDownloadSlidePanel(DownloadEntry entry)
    {
        var dialog = new OverlayDialogHost(this, SlidePanelMode.Right)
        {
            Background = (Brush)Resources["CardBrush"],
            Foreground = (Brush)Resources["ForegroundBrush"]
        };
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(24) };
        var close = new Button { Content = "×", Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)Resources["BrowseButtonStyle"] };
        close.Click += (_, _) => dialog.DialogResult = false;
        DockPanel.SetDock(close, Dock.Top); root.Children.Add(close);
        var title = new TextBlock { Text = entry.Name, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var subtitle = new TextBlock { Text = Path.GetFileName(entry.Path), Foreground = (Brush)Resources["SecondaryBrush"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(subtitle, Dock.Top); root.Children.Add(subtitle);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel();
        void AddAction(string text, Action action, bool primary = false)
        {
            var b = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 6), Style = (Style)Resources[primary ? "AccentButtonStyle" : "BrowseButtonStyle"] };
            b.Click += (_, _) => { action(); if (_activeSlidePanel == dialog) dialog.Close(); };
            stack.Children.Add(b);
        }
        var linked = entry.NexusModId > 0;
        if (!linked) AddAction("Query Info", () => _ = QueryDownloadInfoAsync(entry));
        AddAction("Install", () => _ = InstallDownloadEntryAsync(entry), !entry.Installed);
        if (linked)
        {
            AddAction("Visit on Nexus", () => OpenUrl($"https://www.nexusmods.com/retrorewindvideostoresimulator/mods/{entry.NexusModId}"));
            if (!string.IsNullOrWhiteSpace(entry.Author))
                AddAction("Visit the uploader's profile", () => OpenUrl($"https://www.nexusmods.com/profile/{Uri.EscapeDataString(entry.Author)}"));
        }
        AddAction("Open File", () => OpenFile(entry.Path));
        var metaPath = GetDownloadMetaFilePath(entry);
        AddAction("Open Meta File", () => { EnsureDownloadMetaFile(entry); OpenFile(metaPath); });
        AddAction("Reveal in Explorer", () => RevealInExplorer(entry.Path));
        stack.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 10) });
        AddAction("Delete...", () =>
        {
            if (MessageBox.Show(this, L("Delete downloaded file '{0}'?", entry.Name), L("Delete Download"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                DeleteDownloadFile(entry);
        });
        AddAction(entry.Hidden ? "Unhide" : "Hide", () => SetDownloadHidden(entry, !entry.Hidden));
        stack.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 10) });
        AddAction("Delete Installed Downloads...", () => DeleteDownloadsByFilter(true));
        AddAction("Delete Uninstalled Downloads...", () => DeleteDownloadsByFilter(false));
        AddAction("Delete All Downloads...", () => DeleteDownloadsByFilter(null));
        stack.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 10) });
        AddAction("Hide Installed...", () => HideDownloadsByFilter(true));
        AddAction("Hide Uninstalled...", () => HideDownloadsByFilter(false));
        AddAction("Hide All...", () => HideDownloadsByFilter(null));
        scroll.Content = stack; root.Children.Add(scroll); dialog.Content = root; dialog.ShowDialog();
    }

    private string GetDownloadMetaFilePath(DownloadEntry entry) => entry.Path + ".json";

    private void EnsureDownloadMetaFile(DownloadEntry entry)
    {
        try
        {
            var data = LoadNexusMetadata();
            var meta = data.GetValueOrDefault("_download:" + Path.GetFileName(entry.Path));
            var payload = new
            {
                entry.Name, entry.Version, entry.Type, entry.Installed,
                FileName = Path.GetFileName(entry.Path),
                entry.DownloadedAtUtc,
                NexusGame = meta?.Game ?? entry.NexusGame,
                NexusModId = meta?.ModId ?? entry.NexusModId,
                NexusFileId = meta?.FileId ?? entry.NexusFileId,
                NexusName = meta?.Name ?? entry.Name,
                NexusVersion = meta?.LatestVersion ?? entry.Version,
                Author = meta?.Author ?? entry.Author,
                Description = meta?.Description ?? "",
                Repository = meta?.Repository ?? "",
                NexusUrl = meta?.NexusUrl ?? "",
                ModName = meta?.ModName ?? "",
                Uploader = meta?.Uploader ?? "",
                UploaderUrl = meta?.UploaderUrl ?? "",
                NewestVersion = meta?.NewestVersion ?? "",
                FileTime = meta?.FileTime ?? "",
                FileMd5 = meta?.FileMd5 ?? "",
                FileSha256 = meta?.FileSha256 ?? "",
                FileSize = meta?.FileSize ?? 0,
                Category = meta?.Category ?? -1,
                Mo2MetaFields = meta?.Mo2MetaFields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
            File.WriteAllText(GetDownloadMetaFilePath(entry), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private async Task InstallDownloadEntryAsync(DownloadEntry entry)
    {
        try { await InstallDownloadEntryCoreAsync(entry); } catch (Exception ex) { MessageBox.Show(this, ex.Message, L("Downloads"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private (bool MissingFiles, string InstalledVersion) GetUe4ssIntegrityState()
    {
        try
        {
            var projectRoot = GetGameProjectRoot(GetVerifiedGameRoot());
            var win64 = Path.Combine(projectRoot, "Binaries", "Win64");
            var ueRoot = Path.Combine(win64, "ue4ss");
            var dll = Path.Combine(ueRoot, "UE4SS.dll");
            var proxy = Path.Combine(win64, "dwmapi.dll");
            var missing = !File.Exists(dll) || !File.Exists(proxy);
            var version = string.Empty;
            if (File.Exists(dll))
            {
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(dll);
                    version = info.ProductVersion ?? info.FileVersion ?? string.Empty;
                }
                catch { }
            }
            return (missing, version);
        }
        catch
        {
            return (true, string.Empty);
        }
    }

    private static int[] ParseUe4ssVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<int>();
        var matches = Regex.Matches(value, @"\d+");
        return matches.Cast<Match>().Select(m => int.TryParse(m.Value, out var n) ? n : 0).Take(6).ToArray();
    }

    private static int CompareUe4ssVersions(string? installed, string? latest)
    {
        var a = ParseUe4ssVersion(installed);
        var b = ParseUe4ssVersion(latest);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }

    private async Task CheckUe4ssHealthAsync()
    {
        var state = GetUe4ssIntegrityState();
        _ue4ssIntegrityMissing = state.MissingFiles;
        _ue4ssUpdateAvailable = false;
        _ue4ssLatestVersion = string.Empty;

        try
        {
            // A remote check is only possible when Nexus credentials are configured.
            if (!string.IsNullOrWhiteSpace(_nexusApiKey) || !string.IsNullOrWhiteSpace(NexusSecretStore.Load()))
            {
                var request = await GetUe4ssMainFileRequestAsync();
                _ue4ssLatestVersion = request.Version ?? string.Empty;
                _ue4ssUpdateAvailable = CompareUe4ssVersions(state.InstalledVersion, request.Version) < 0;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UE4SS update check failed: {ex.Message}");
        }

        // If there is no update but UE4SS is missing files, silently repair only
        // the missing files when a valid local archive is already available.
        if (!_ue4ssUpdateAvailable && _ue4ssIntegrityMissing)
        {
            var localArchive = FindValidLocalUe4ssArchive();
            if (!string.IsNullOrWhiteSpace(localArchive) && File.Exists(localArchive))
            {
                try
                {
                    SetOperationBusy(true, L("Fixing UE4SS…"), null, L("Restoring missing UE4SS files from the existing local archive."));
                    await InstallUe4ssFrameworkZipAsync(localArchive, onlyMissingFiles: true);
                    _ue4ssIntegrityMissing = GetUe4ssIntegrityState().MissingFiles;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UE4SS automatic repair failed: {ex.Message}");
                }
                finally
                {
                    if (!_gameActive) SetOperationBusy(false);
                }
            }
        }

        await Dispatcher.InvokeAsync(ApplyUe4ssHealthButtons);
    }

    private void ApplyUe4ssHealthButtons()
    {
        // Home no longer contains UE4SS status controls. Keep the health state
        // available for the Mods > UE4SS panel only.
        UpdateUe4ssFooter();
    }

    private async Task UpdateUe4ssAsync()
    {
        SetOperationBusy(true, L("Updating UE4SS…"), null, L("Downloading the latest UE4SS package."));
        try
        {
            var request = await GetUe4ssMainFileRequestAsync();
            await DownloadNexusFileAsync(request);
            var downloaded = FindNexusDownloadedFile(request.Game, request.ModId, request.FileId);
            if (string.IsNullOrWhiteSpace(downloaded) || !File.Exists(downloaded))
                throw new InvalidOperationException(L("UE4SS was downloaded, but the downloaded archive could not be located."));
            SetOperationBusy(true, L("Installing UE4SS…"), null, Path.GetFileName(downloaded));
            await InstallUe4ssFrameworkZipAsync(downloaded);
            _modCacheUpdatedUtc = DateTime.MinValue;
            _modStateRefreshVersion++;
            BeginModManagerRefresh(force: true);
            await WaitForModManagerRefreshAsync();
            RefreshModManager();
            await CheckUe4ssHealthAsync();
        }
        finally
        {
            if (!_gameActive) SetOperationBusy(false);
        }
    }

    private async Task FixUe4ssAsync()
    {
        SetOperationBusy(true, L("Fixing UE4SS…"), null, L("Restoring the missing UE4SS files."));
        try
        {
            var local = FindValidLocalUe4ssArchive();
            if (string.IsNullOrWhiteSpace(local))
            {
                var request = await GetUe4ssMainFileRequestAsync();
                await DownloadNexusFileAsync(request);
                local = FindNexusDownloadedFile(request.Game, request.ModId, request.FileId);
            }
            if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
                throw new InvalidOperationException(L("A valid UE4SS archive could not be found."));
            await InstallUe4ssFrameworkZipAsync(local);
            _modCacheUpdatedUtc = DateTime.MinValue;
            _modStateRefreshVersion++;
            BeginModManagerRefresh(force: true);
            await WaitForModManagerRefreshAsync();
            RefreshModManager();
            await CheckUe4ssHealthAsync();
        }
        finally
        {
            if (!_gameActive) SetOperationBusy(false);
        }
    }

    private async void Ue4ssUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy || _gameActive) return;
        try { await UpdateUe4ssAsync(); }
        catch (Exception ex) { SetOperationBusy(false); MessageBox.Show(this, ex.Message, L("UE4SS"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Ue4ssFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy || _gameActive) return;
        try { await FixUe4ssAsync(); }
        catch (Exception ex) { SetOperationBusy(false); MessageBox.Show(this, ex.Message, L("UE4SS"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private bool IsUe4ssInstalled()
    {
        try
        {
            var projectRoot = GetGameProjectRoot(GetVerifiedGameRoot());
            return File.Exists(Path.Combine(projectRoot, "Binaries", "Win64", "ue4ss", "UE4SS.dll"));
        }
        catch { return false; }
    }

    private async Task<NexusFileDownloadRequest?> GetUe4ssMainFileRequestAsync()
    {
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(L("Connect to Nexus in Settings before installing UE4SS."));
        const string game = "retrorewindvideostoresimulator";
        const int modId = 52;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Retro Rewind ModHub/1.0.12");
        client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);
        using var response = await client.GetAsync($"https://api.nexusmods.com/v1/games/{game}/mods/{modId}/files.json");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(L("Nexus could not provide the UE4SS file list (HTTP {0}).", (int)response.StatusCode));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var files = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList()
            : root.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Array ? f.EnumerateArray().ToList()
            : new List<JsonElement>();
        var main = files.FirstOrDefault(x =>
        {
            var cat = JsonString(x, "category_name", "category", "categoryName");
            var name = JsonString(x, "name", "file_name", "fileName");
            return cat.Equals("main", StringComparison.OrdinalIgnoreCase) &&
                   !name.Contains("zDEV", StringComparison.OrdinalIgnoreCase) &&
                   !name.Contains("dev", StringComparison.OrdinalIgnoreCase);
        });
        if (main.ValueKind != JsonValueKind.Object)
            main = files.FirstOrDefault(x =>
            {
                var cat = JsonString(x, "category_name", "category", "categoryName");
                var name = JsonString(x, "name", "file_name", "fileName");
                return !name.Contains("zDEV", StringComparison.OrdinalIgnoreCase) &&
                       (cat.Equals("main", StringComparison.OrdinalIgnoreCase) || name.Contains("UE4SS", StringComparison.OrdinalIgnoreCase));
            });
        if (main.ValueKind != JsonValueKind.Object || !main.TryGetProperty("file_id", out var fid) || !fid.TryGetInt32(out var fileId) || fileId <= 0)
            throw new InvalidOperationException(L("Nexus did not provide the UE4SS main file."));
        var fileName = JsonString(main, "name", "file_name", "fileName");
        var version = JsonString(main, "version", "versionString");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "UE4SS.zip";
        return new NexusFileDownloadRequest(game, modId, fileId, fileName, version, 1);
    }

    private async Task EnsureUe4ssInstalledAsync()
    {
        if (IsUe4ssInstalled()) return;
        SetOperationBusy(true, L("Preparing UE4SS…"), null, L("Checking for an existing valid UE4SS archive before downloading."));
        try
        {
            var local = FindValidLocalUe4ssArchive();
            string downloaded;
            if (!string.IsNullOrWhiteSpace(local))
            {
                downloaded = local;
                SetOperationBusy(true, L("Using existing UE4SS archive…"), null, Path.GetFileName(downloaded));
            }
            else
            {
                var request = await GetUe4ssMainFileRequestAsync();
                SetOperationBusy(true, L("Downloading UE4SS…"), null, request.FileName);
                await DownloadNexusFileAsync(request);
                downloaded = FindNexusDownloadedFile(request.Game, request.ModId, request.FileId) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(downloaded) || !File.Exists(downloaded))
                    throw new InvalidOperationException(L("UE4SS downloaded successfully, but ModHub could not locate the downloaded file."));
            }

            SetOperationBusy(true, L("Installing UE4SS…"), null, Path.GetFileName(downloaded));
            await InstallUe4ssFrameworkZipAsync(downloaded);
            try
            {
                var gameRoot = GetVerifiedGameRoot();
                EnsureUe4ssModsTxtMatchesInstalledMods(GetUe4ssModsRoot(gameRoot), gameRoot);
            }
            catch { }
            if (!IsUe4ssInstalled())
                throw new InvalidOperationException(L("UE4SS installation completed, but ue4ss\\UE4SS.dll was not detected in the expected Win64 folder."));
        }
        finally
        {
            if (!_operationBusy || IsUe4ssInstalled()) SetOperationBusy(false);
        }
    }

    private string? FindValidLocalUe4ssArchive()
    {
        try
        {
            var dir = GetDownloadsDirectory();
            if (!Directory.Exists(dir)) return null;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal)) continue;
                if (file.EndsWith(".download", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var archive = OpenSupportedArchive(file);
                    var entries = GetSupportedArchiveEntries(archive);
                    var hasUe4ss = entries.Any(e => string.Equals(Path.GetFileName(e.Key.Replace('\\','/')), "UE4SS.dll", StringComparison.OrdinalIgnoreCase));
                    var hasDwmapi = entries.Any(e => string.Equals(Path.GetFileName(e.Key.Replace('\\','/')), "dwmapi.dll", StringComparison.OrdinalIgnoreCase));
                    if (hasUe4ss && hasDwmapi) return file;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    private async Task InstallUe4ssFrameworkZipAsync(string zipPath, bool onlyMissingFiles = false)
    {
        var projectRoot = GetGameProjectRoot(GetVerifiedGameRoot());
        var win64Root = Path.Combine(projectRoot, "Binaries", "Win64");
        var ue4ssRoot = Path.Combine(win64Root, "ue4ss");
        Directory.CreateDirectory(win64Root);
        Directory.CreateDirectory(ue4ssRoot);

        await Task.Run(() =>
        {
            using var archive = OpenSupportedArchive(zipPath);
            var files = GetSupportedArchiveEntries(archive);
            var hasNestedUe4ss = files.Any(e => e.Key.Replace('\\', '/').StartsWith("ue4ss/", StringComparison.OrdinalIgnoreCase));
            foreach (var entry in files)
            {
                var relative = entry.Key.Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrWhiteSpace(relative)) continue;
                if (relative.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;
                if (hasNestedUe4ss && relative.StartsWith("ue4ss/", StringComparison.OrdinalIgnoreCase))
                    relative = relative.Substring("ue4ss/".Length);

                string target;
                var isLoader = string.Equals(relative, "dwmapi.dll", StringComparison.OrdinalIgnoreCase);
                if (isLoader)
                    target = Path.Combine(win64Root, "dwmapi.dll");
                else
                    target = Path.Combine(ue4ssRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                target = Path.GetFullPath(target);
                var allowedRoot = Path.GetFullPath((isLoader ? win64Root : ue4ssRoot) + Path.DirectorySeparatorChar);
                if (!target.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The UE4SS archive contains an unsafe path.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (onlyMissingFiles && File.Exists(target))
                    continue;
                ExtractSupportedArchiveEntry(entry, target, true);
            }
        });
    }

    private async void Ue4ssInstallNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy || _gameActive || IsUe4ssInstalled()) return;
        try
        {
            await EnsureUe4ssInstalledAsync();
            _modCacheUpdatedUtc = DateTime.MinValue;
            _modStateRefreshVersion++;
            BeginModManagerRefresh(force: true);
            await WaitForModManagerRefreshAsync();
            RefreshModManager();
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, ex.Message, L("UE4SS"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!_gameActive) SetOperationBusy(false);
        }
    }

    private async Task InstallDownloadEntryCoreAsync(DownloadEntry entry, bool ensureUe4ssPrerequisite = true)
    {
        SetOperationBusy(true, L("Installing {0}…", entry.Name));
        try
        {
            if (entry.Type.Equals("Video", StringComparison.OrdinalIgnoreCase)) await Task.Run(() => ImportVideoFile(entry.Path));
            else
            {
                if (ensureUe4ssPrerequisite && entry.Type.Contains("UE4SS", StringComparison.OrdinalIgnoreCase) && !IsUe4ssInstalled())
                    await EnsureUe4ssInstalledAsync();
                var importedMeta = ImportMo2MetaForDownload(entry.Path);
                var metadata = LoadNexusMetadata();
                var meta = importedMeta ?? metadata.GetValueOrDefault("_download:" + Path.GetFileName(entry.Path));
                await InstallModZipAsync(entry.Path, meta?.Name ?? entry.Name, meta?.Game ?? entry.NexusGame, meta?.ModId ?? entry.NexusModId, meta?.FileId ?? entry.NexusFileId);
            }
            InvalidateDownloadsCache(); RefreshDownloadsPage(); RefreshModManager(); RefreshVideosPage();
        }
        finally { SetOperationBusy(false); }
    }

    private void DeleteDownloadFile(DownloadEntry entry)
    {
        try
        {
            File.Delete(entry.Path);
            try { var mo2Meta = GetMo2MetaPath(entry.Path); if (File.Exists(mo2Meta)) File.Delete(mo2Meta); } catch { }
            var data = LoadNexusMetadata(); data.Remove("_download:" + Path.GetFileName(entry.Path)); SaveNexusMetadata(data);
            var hidden = LoadHiddenDownloadState(); hidden.Remove(Path.GetFileName(entry.Path)); SaveHiddenDownloadState(hidden);
            InvalidateDownloadsCache(); RefreshDownloadsPage();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L("Downloads"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SetDownloadHidden(DownloadEntry entry, bool hidden)
    {
        var state = LoadHiddenDownloadState(); var name = Path.GetFileName(entry.Path);
        if (hidden) state.Add(name); else state.Remove(name);
        SaveHiddenDownloadState(state); InvalidateDownloadsCache(); RefreshDownloadsPage();
    }

    private void DeleteDownloadsByFilter(bool? installed)
    {
        var entries = BuildDownloadsSnapshot(CancellationToken.None).Where(x => installed == null || x.Installed == installed.Value).ToList();
        if (entries.Count == 0) return;
        if (MessageBox.Show(this, L("Delete {0} downloaded file(s)?", entries.Count), L("Downloads"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var entry in entries) DeleteDownloadFileSilent(entry);
        InvalidateDownloadsCache(); RefreshDownloadsPage();
    }

    private void DeleteDownloadFileSilent(DownloadEntry entry)
    {
        try { File.Delete(entry.Path); } catch { }
        try { var mo2Meta = GetMo2MetaPath(entry.Path); if (File.Exists(mo2Meta)) File.Delete(mo2Meta); } catch { }
        try { var data = LoadNexusMetadata(); data.Remove("_download:" + Path.GetFileName(entry.Path)); SaveNexusMetadata(data); } catch { }
        try { var hidden = LoadHiddenDownloadState(); hidden.Remove(Path.GetFileName(entry.Path)); SaveHiddenDownloadState(hidden); } catch { }
    }

    private void HideDownloadsByFilter(bool? installed)
    {
        var state = LoadHiddenDownloadState();
        foreach (var entry in BuildDownloadsSnapshot(CancellationToken.None).Where(x => installed == null || x.Installed == installed.Value)) state.Add(Path.GetFileName(entry.Path));
        SaveHiddenDownloadState(state); InvalidateDownloadsCache(); RefreshDownloadsPage();
    }

    private HashSet<string> LoadHiddenDownloadState()
    {
        try
        {
            var path = Path.Combine(GetDownloadsDirectory(), DownloadStateFileName);
            if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new List<string>();
            return list.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    private void SaveHiddenDownloadState(HashSet<string> state)
    {
        try
        {
            var dir = GetDownloadsDirectory(); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, DownloadStateFileName), JsonSerializer.Serialize(state.OrderBy(x => x).ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void DownloadsHiddenToggle_Click(object sender, RoutedEventArgs e)
    {
        _showHiddenDownloads = DownloadsHiddenToggle.IsChecked == true;
        RefreshDownloadsPage();
    }

    private async void DownloadsQueryAllButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadsQueryAllButton.IsEnabled = false;
        try { await QueryAllUnlinkedDownloadsAsync(); }
        finally { UpdateDownloadsQueryAllState(); }
    }

    private void UpdateDownloadsQueryAllState()
    {
        if (DownloadsQueryAllButton == null) return;
        var count = (_cachedDownloads ?? new List<DownloadEntry>()).Count(x => x.NexusModId <= 0 && File.Exists(x.Path));
        DownloadsQueryAllButton.IsEnabled = count > 0 && !_downloadsRefreshInProgress && !_gameActive;
        DownloadsQueryAllButton.ToolTip = count > 0
            ? L("Query Info for {0} unlinked download{1}", count, count == 1 ? "" : "s")
            : L("All downloads are linked");
    }

    private async void DownloadInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DownloadEntry entry) return;
        await InstallDownloadEntryAsync(entry);
    }

    private void DownloadDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DownloadEntry entry) return;
        if (MessageBox.Show(this, L("Delete downloaded file '{0}'?\n\nThis will not delete the installed mod or video.", entry.Name), L("Delete Download"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        DeleteDownloadFile(entry);
    }

    private async Task QueryDownloadInfoAsync(DownloadEntry entry)
    {
        if (entry.NexusModId > 0) return;
        if (!File.Exists(entry.Path)) return;
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(this, L("Connect to Nexus in Settings before using Query Info."), L("Query Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            SetOperationBusy(true, L("Fetching data: {0}…", Path.GetFileName(entry.Path)));

            // Prefer MO2 metadata when available. It contains the authoritative Nexus
            // Mod ID/File ID pair and avoids filename guessing.
            var imported = ImportMo2MetaForDownload(entry.Path);
            NexusModMetadata? resolved = imported;

            // If there is no usable MO2 identity, use Nexus' official MD5 lookup.
            // This is the same identity mechanism used by MO2/Vortex-style managers:
            // hash the actual downloaded file, then ask Nexus which mod/file owns it.
            if (resolved == null || resolved.ModId <= 0 || resolved.FileId <= 0)
            {
                var md5 = ComputeMd5(entry.Path);
                resolved = await QueryNexusByMd5Async(md5);
            }

            if (resolved == null || resolved.ModId <= 0 || resolved.FileId <= 0)
            {
                MessageBox.Show(this, L("Query Info could not identify this file from its metadata or MD5 hash.\n\nThe file has not been linked."), L("Query Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var info = await FetchNexusModInfoAsync(resolved.Game, resolved.ModId);
                if (info != null) resolved = ApplyNexusInfo(resolved, info);
            }
            catch { }

            var data = LoadNexusMetadata();
            var key = "_download:" + Path.GetFileName(entry.Path);
            data[key] = resolved with
            {
                ArchivePath = Path.GetFileName(entry.Path),
                FileMd5 = string.IsNullOrWhiteSpace(resolved.FileMd5) ? ComputeMd5(entry.Path) : resolved.FileMd5,
                FileSize = resolved.FileSize > 0 ? resolved.FileSize : new FileInfo(entry.Path).Length,
                DownloadedAtUtc = resolved.DownloadedAtUtc ?? File.GetLastWriteTimeUtc(entry.Path)
            };
            SaveNexusMetadata(data);
            EnsureDownloadMetaFile(entry);
            InvalidateDownloadsCache();
            RefreshDownloadsPage();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Query Info failed:\n\n{0}", ex.Message), L("Query Info"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetOperationBusy(false); }
    }

    private async Task<NexusModMetadata?> QueryNexusByMd5Async(string md5)
    {
        if (string.IsNullOrWhiteSpace(md5)) return null;
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        const string game = "retrorewindvideostoresimulator";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Retro Rewind ModHub/1.0.12");
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        var url = $"https://api.nexusmods.com/v1/games/{Uri.EscapeDataString(game)}/mods/md5_search/{Uri.EscapeDataString(md5)}.json";
        using var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var first = root.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
                root = first;
            else
                return null;
        }
        if (root.ValueKind != JsonValueKind.Object) return null;

        var modId = JsonInt(root, "mod_id", "modId", "modID");
        var fileId = JsonInt(root, "file_id", "fileId", "fileID");
        if (modId <= 0 || fileId <= 0) return null;

        var fileName = JsonString(root, "file_name", "name", "fileName");
        var version = JsonString(root, "version", "mod_version", "modVersion");
        return new NexusModMetadata(
            string.IsNullOrWhiteSpace(fileName) ? Path.GetFileNameWithoutExtension(md5) : fileName,
            game,
            modId,
            fileId,
            fileName)
        {
            LatestVersion = version,
            InstalledVersion = version,
            FileMd5 = md5
        };
    }

    private async Task QueryAllUnlinkedDownloadsAsync()
    {
        var entries = (_cachedDownloads ?? BuildDownloadsSnapshot(CancellationToken.None))
            .Where(x => x.NexusModId <= 0 && File.Exists(x.Path))
            .ToList();
        if (entries.Count == 0) return;

        try
        {
            SetOperationBusy(true, L("Querying {0} download{1}…", entries.Count, entries.Count == 1 ? "" : "s"));
            var completed = 0;
            foreach (var entry in entries)
            {
                try
                {
                    await QueryDownloadInfoAsync(entry);
                }
                catch { }
                completed++;
                SetOperationBusy(true, L("Querying {0}/{1}…", completed, entries.Count));
            }
        }
        finally
        {
            SetOperationBusy(false);
            InvalidateDownloadsCache();
            RefreshDownloadsPage();
        }
    }

    private void InvalidateDownloadsCache()
    {
        _cachedDownloads = null;
        _downloadsCacheUpdatedUtc = DateTime.MinValue;
    }

    private string GetDownloadsDirectory() => Path.Combine(ModsRoot, "_downloads");

    private string GetVideoEditorDownloadsDirectory() => Path.Combine(VideoEditorRoot, "_downloads");

    private static string GetUniqueVideoEditorDownloadPath(string desiredPath)
    {
        if (!File.Exists(desiredPath)) return desiredPath;
        var directory = Path.GetDirectoryName(desiredPath)!;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static readonly string[] VideoEditorDownloadExtensions =
    { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".mpeg", ".mpg" };

    private static string GetUniqueDownloadPath(string desiredPath)
    {
        if (!File.Exists(desiredPath)) return desiredPath;
        var directory = Path.GetDirectoryName(desiredPath)!;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private List<PendingModEntry> GetPendingMods()
    {
        var result = new List<PendingModEntry>();
        var dir = GetDownloadsDirectory();
        if (!Directory.Exists(dir)) return result;
        var metadata = LoadNexusMetadata();
        foreach (var zip in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly).Where(IsSupportedModArchive))
        {
            try
            {
                var detected = DetectZipModType(zip);
                if (detected == null) continue;
                var name = Path.GetFileNameWithoutExtension(zip);
                var fileName = Path.GetFileName(zip);
                var installed = metadata.Any(kv => !kv.Key.StartsWith("_download:", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(kv.Value.ArchivePath, fileName, StringComparison.OrdinalIgnoreCase));
                if (installed) continue;
                var nexus = metadata.GetValueOrDefault("_download:" + fileName);
                if (nexus == null) nexus = metadata.Values.FirstOrDefault(m => string.Equals(m.ArchivePath, fileName, StringComparison.OrdinalIgnoreCase));
                if (nexus != null && !string.IsNullOrWhiteSpace(nexus.Name)) name = nexus.Name;
                result.Add(new PendingModEntry(name, zip, detected.Value, nexus?.Game, nexus?.ModId ?? 0, nexus?.FileId ?? 0));
            }
            catch { }
        }
        return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool? DetectZipModType(string zipPath)
    {
        using var archive = OpenSupportedArchive(zipPath);
        var entries = GetSupportedArchiveEntries(archive);
        if (entries.Count == 0) return null;
        foreach (var entry in entries) ValidateZipEntry(entry.Key);
        if (entries.Any(e => e.Key.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))) return true;
        var hasUe4ss = entries.Any(e =>
            e.Key.EndsWith("enabled.txt", StringComparison.OrdinalIgnoreCase) ||
            e.Key.EndsWith("config.lua", StringComparison.OrdinalIgnoreCase) ||
            e.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            e.Key.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
            e.Key.Contains("scripts/", StringComparison.OrdinalIgnoreCase) ||
            e.Key.Contains("mods.json", StringComparison.OrdinalIgnoreCase) ||
            e.Key.Contains("ue4ss", StringComparison.OrdinalIgnoreCase));
        return hasUe4ss ? false : null;
    }

    private void PakEnableAll_Click(object sender, RoutedEventArgs e) => SetAllPakModsEnabled(true);

    private void PakDisableAll_Click(object sender, RoutedEventArgs e) => SetAllPakModsEnabled(false);

    private void Ue4ssEnableAll_Click(object sender, RoutedEventArgs e) => SetAllUe4ssModsEnabled(true);

    private void Ue4ssDisableAll_Click(object sender, RoutedEventArgs e) => SetAllUe4ssModsEnabled(false);

    private void SetAllPakModsEnabled(bool enabled)
    {
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var mods = GetPakMods(gameRoot);

            // Do not call SetPakModEnabledWithoutRefresh for every mod here.
            // That method rebuilds the entire PAK link set, which would cause
            // one UAC request per mod. Change the desired states first, then
            // rebuild the symbolic links exactly once.
            if (!enabled)
            {
                // Disable All is deletion-only and therefore never needs UAC.
                foreach (var mod in mods)
                    SetPakPathEnabled(gameRoot, mod.Path, false);
            }
            else
            {
                // Enable All is one atomic link batch and may show one UAC prompt.
                var enabledSources = mods.Select(m => Path.GetFullPath(m.Path))
                    .Where(File.Exists)
                    .ToList();
                RebuildPakLinks(gameRoot, enabledSources, forceSingleElevation: true);
            }

            RefreshModManagerAfterStateChange();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetAllUe4ssModsEnabled(bool enabled)
    {
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var mods = GetUe4ssMods(gameRoot);
            var protectedDefaults = !enabled ? mods.Where(m => m.IsUe4ssDefault).ToList() : new List<ModEntry>();
            if (protectedDefaults.Count > 0)
            {
                var result = MessageBox.Show(this,
                    L("Warning — this will disable {0} protected UE4SS default mod(s). Disabling default UE4SS mods may affect UE4SS or game functionality.\n\nDo you want to continue?", protectedDefaults.Count),
                    L("Disable UE4SS Default Mods"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }
            foreach (var mod in mods) SetUe4ssModEnabled(mod, enabled);
            RefreshModManagerAfterStateChange();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetAllModsEnabled(bool enabled)
    {
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var ue4ssDefaults = !enabled ? GetUe4ssMods(gameRoot).Where(m => m.IsUe4ssDefault).ToList() : new List<ModEntry>();
            if (ue4ssDefaults.Count > 0)
            {
                var result = MessageBox.Show(this,
                    L("Warning — this will disable {0} protected UE4SS default mod(s). Disabling default UE4SS mods may affect UE4SS or game functionality.\n\nDo you want to continue?", ue4ssDefaults.Count),
                    L("Disable UE4SS Default Mods"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }
            var pak = GetPakMods(gameRoot);

            // Bulk PAK state changes are rebuilt once so symbolic-link elevation
            // can be requested once for the whole batch.
            if (!enabled)
            {
                foreach (var mod in pak)
                    SetPakPathEnabled(gameRoot, mod.Path, false);
            }
            else
            {
                var enabledPakSources = pak.Select(m => Path.GetFullPath(m.Path))
                    .Where(File.Exists)
                    .ToList();
                RebuildPakLinks(gameRoot, enabledPakSources, forceSingleElevation: true);
            }

            var ue = GetUe4ssMods(gameRoot);
            foreach (var mod in ue)
                SetUe4ssModEnabled(mod, enabled);

            RefreshModManagerAfterStateChange();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshModManager()
    {
        if (_mode != "mods" || !IsLoaded) return;

        // The visual Mod Manager is intentionally persistent. Once a snapshot has
        // been rendered, navigating away and back must not rebuild hundreds of WPF
        // controls or reread metadata from disk. Only a new snapshot invalidates it.
        if (_cachedPakMods != null && _cachedUe4ssMods != null && _cachedPendingMods != null)
        {
            if (_modUiAppliedVersion != _modSnapshotVersion)
                ApplyModManagerSnapshot(_cachedPakMods, _cachedUe4ssMods, _cachedPendingMods);
        }
        else
        {
            PakModsStatus.Text = L("Loading installed mods…");
            Ue4ssModsStatus.Text = L("Loading installed mods…");
        }

        // Do not start another filesystem scan merely because the page was selected.
        // Startup/state-change code is responsible for updating the cache; F5 can
        // explicitly request a refresh.
    }

    private void ApplyModManagerSnapshot(List<ModEntry> pak, List<ModEntry> ue, List<PendingModEntry> pending)
    {
        if (_mode != "mods" || !IsLoaded) return;
        _modListMetadataCache ??= LoadNexusMetadata();
        PopulatePakModList(PakModsList, pak, _modListMetadataCache);
        PopulateModList(Ue4ssModsList, ue, false, _modListMetadataCache);
        _modUiAppliedVersion = _modSnapshotVersion;
        PakModsStatus.Text = pak.Count == 0 ? L("No PAK mods installed | 0 Enabled") : L("{0} PAK mods installed | {1} Enabled", pak.Count, pak.Count(m => m.Enabled));
        UpdatePakBatchLinkButton();
        var ue4ssInstalled = false;
        try
        {
            var projectRoot = GetGameProjectRoot(GetVerifiedGameRoot());
            ue4ssInstalled = File.Exists(Path.Combine(projectRoot, "Binaries", "Win64", "ue4ss", "UE4SS.dll"));
        }
        catch { }
        if (Ue4ssInstallPromptPanel != null && Ue4ssModsStatusNormal != null)
        {
            Ue4ssInstallPromptPanel.Visibility = ue4ssInstalled ? Visibility.Collapsed : Visibility.Visible;
            Ue4ssModsStatusNormal.Visibility = ue4ssInstalled ? Visibility.Visible : Visibility.Collapsed;
            Ue4ssModsList.Visibility = ue4ssInstalled ? Visibility.Visible : Visibility.Collapsed;
        }
        if (Ue4ssModsStatus != null)
            Ue4ssModsStatus.Text = ue4ssInstalled
                ? (ue.Count == 0 ? L("No UE4SS mods installed | 0 Enabled") : L("{0} UE4SS mods installed | {1} Enabled", ue.Count, ue.Count(m => m.Enabled)))
                : L("UE4SS Not Installed");
        if (Ue4ssInstallNowButton != null) Ue4ssInstallNowButton.IsEnabled = !ue4ssInstalled && !_operationBusy && !_gameActive;
        if (Ue4ssFixButton != null)
        {
            var showFix = _ue4ssIntegrityMissing && !_ue4ssUpdateAvailable && ue4ssInstalled;
            Ue4ssFixButton.Visibility = showFix ? Visibility.Visible : Visibility.Collapsed;
            Ue4ssFixButton.IsEnabled = showFix && !_operationBusy && !_gameActive;
        }
        if (Ue4ssEnableAllButton != null) Ue4ssEnableAllButton.IsEnabled = ue4ssInstalled && ue.Count > 0 && ue.Any(m => !m.Enabled);
        if (Ue4ssDisableAllButton != null) Ue4ssDisableAllButton.IsEnabled = ue4ssInstalled && ue.Count > 0 && ue.Any(m => m.Enabled);
        if (PakEnableAllButton != null) PakEnableAllButton.IsEnabled = pak.Count > 0 && pak.Any(m => !m.Enabled);
        if (PakDisableAllButton != null) PakDisableAllButton.IsEnabled = pak.Count > 0 && pak.Any(m => m.Enabled);
        if (Ue4ssEnableAllButton != null) Ue4ssEnableAllButton.IsEnabled = ue4ssInstalled && ue.Count > 0 && ue.Any(m => !m.Enabled);
        if (Ue4ssDisableAllButton != null) Ue4ssDisableAllButton.IsEnabled = ue4ssInstalled && ue.Count > 0 && ue.Any(m => m.Enabled);
    }

    private void BeginModManagerRefresh(bool force = false)
    {
        if (_gameActive || _modRefreshInProgress || DateTime.UtcNow - _lastPakSelectionUtc < TimeSpan.FromSeconds(3)) return;
        if (!force && _modCacheUpdatedUtc != DateTime.MinValue && DateTime.UtcNow - _modCacheUpdatedUtc < TimeSpan.FromSeconds(20))
            return;

        _modRefreshInProgress = true;
        try { _modRefreshCts?.Cancel(); } catch { }
        var cts = new CancellationTokenSource();
        _modRefreshCts = cts;
        _ = Task.Run(() =>
        {
            cts.Token.ThrowIfCancellationRequested();
            var gameRoot = GetVerifiedGameRoot();
            var pak = GetPakMods(gameRoot);
            cts.Token.ThrowIfCancellationRequested();
            var ue = GetUe4ssMods(gameRoot);
            cts.Token.ThrowIfCancellationRequested();
            var pending = GetPendingMods();
            return (pak, ue, pending);
        }, cts.Token).ContinueWith(t =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _modRefreshInProgress = false;
                if (t.IsCanceled || t.IsFaulted || cts.IsCancellationRequested || _gameActive || _mode != "mods") return;
                _cachedPakMods = t.Result.pak;
                _cachedUe4ssMods = t.Result.ue;
                _cachedPendingMods = t.Result.pending;
                _modCacheUpdatedUtc = DateTime.UtcNow;
                _modSnapshotVersion++;
                SaveModListCache();
                ApplyModManagerSnapshot(_cachedPakMods, _cachedUe4ssMods, _cachedPendingMods);
                _ = RefreshLinkedNexusMetadataIfDueAsync();
            }));
        }, TaskScheduler.Default);
    }

    private static ImageSource LoadModIcon(string fileName)
    {
        var uri = new Uri($"pack://application:,,,/RetroRewindModhub;component/Assets/{fileName}", UriKind.Absolute);
        return new BitmapImage(uri);
    }

    private void PopulatePakModList(ListBox list, IEnumerable<ModEntry> mods, Dictionary<string, NexusModMetadata>? metadataCache = null)
    {
        list.Items.Clear();
        var all = mods.ToList();
        var byPath = all.ToDictionary(m => Path.GetFullPath(m.Path), StringComparer.OrdinalIgnoreCase);
        var metadata = metadataCache ?? LoadNexusMetadata();
        var groups = all.Where(m => m.IsPak)
            .Select(m => new
            {
                Mod = m,
                Meta = metadata.GetValueOrDefault(PakMetadataKey(m.Path))
            })
            .Where(x => x.Meta != null && x.Meta.ModId > 0 && !string.IsNullOrWhiteSpace(x.Meta.Game))
            .GroupBy(x => $"{x.Meta!.Game.ToLowerInvariant()}:{x.Meta.ModId}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var groupedPaths = groups.Values
            .SelectMany(g => g.Select(x => Path.GetFullPath(x.Mod.Path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loadOrder = GetOrderedPakPaths();
        var addedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Render the list in the same order used by the actual PAK load-order file.
        // A group occupies the position of its first child, while its children retain
        // their existing internal order. This makes dragging a group visibly change
        // the same load order that is written to disk.
        foreach (var path in loadOrder)
        {
            if (groupedPaths.Contains(path))
            {
                var groupEntry = groups.FirstOrDefault(g => g.Value.Any(x => string.Equals(x.Mod.Path, path, StringComparison.OrdinalIgnoreCase)));
                if (groupEntry.Value == null || !addedGroups.Add(groupEntry.Key)) continue;

                var meta = groupEntry.Value[0].Meta!;
                var order = loadOrder;
                var groupMods = groupEntry.Value
                    .Select(x => x.Mod)
                    .OrderBy(m => { var i = order.FindIndex(p => string.Equals(p, m.Path, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; })
                    .ThenBy(m => Path.GetFileName(m.Path), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var installedVersions = groupEntry.Value.Select(x => x.Meta!.InstalledVersion).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var latest = groupEntry.Value.Select(x => x.Meta!.LatestVersion).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
                var installed = installedVersions.Count == 1 ? installedVersions[0] : (installedVersions.Count > 1 ? installedVersions.OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase).First() : "");
                var versionText = GetModVersionStatusText(installed, latest);
                var groupName = !string.IsNullOrWhiteSpace(meta.GroupDisplayName) ? meta.GroupDisplayName : meta.Name;
                list.Items.Add(CreatePakGroupRow(new PakModGroup(groupName, meta.Game, meta.ModId, versionText, groupMods)));
                foreach (var mod in groupMods) addedPaths.Add(Path.GetFullPath(mod.Path));
            }
            else if (byPath.TryGetValue(Path.GetFullPath(path), out var mod) && !addedPaths.Contains(Path.GetFullPath(mod.Path)))
            {
                list.Items.Add(CreateModRow(mod, metadata));
                addedPaths.Add(Path.GetFullPath(mod.Path));
            }
        }

        // Include anything that was not present in the persisted order, preserving the
        // previous fallback behavior for newly discovered files.
        foreach (var groupEntry in groups)
        {
            if (!addedGroups.Add(groupEntry.Key)) continue;
            var meta = groupEntry.Value[0].Meta!;
            var groupMods = groupEntry.Value.Select(x => x.Mod).OrderBy(m => Path.GetFileName(m.Path), StringComparer.OrdinalIgnoreCase).ToList();
            var installedVersions = groupEntry.Value.Select(x => x.Meta!.InstalledVersion).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var latest = groupEntry.Value.Select(x => x.Meta!.LatestVersion).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
            var installed = installedVersions.Count == 1 ? installedVersions[0] : (installedVersions.Count > 1 ? installedVersions.OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase).First() : "");
            var versionText = GetModVersionStatusText(installed, latest);
            var groupName = !string.IsNullOrWhiteSpace(meta.GroupDisplayName) ? meta.GroupDisplayName : meta.Name;
            list.Items.Add(CreatePakGroupRow(new PakModGroup(groupName, meta.Game, meta.ModId, versionText, groupMods)));
            foreach (var mod in groupMods) addedPaths.Add(Path.GetFullPath(mod.Path));
        }

        foreach (var mod in all.Where(m => !groupedPaths.Contains(Path.GetFullPath(m.Path)) && !addedPaths.Contains(Path.GetFullPath(m.Path))))
            list.Items.Add(CreateModRow(mod, metadata));
    }

    private static string PakGroupStateKey(PakModGroup group)
    {
        return $"{group.Game.ToLowerInvariant()}:{group.ModId}";
    }

private Grid CreatePakGroupRow(PakModGroup group)
    {
        var outer = new Grid { Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var row = new Grid { Background = Brushes.Transparent, Tag = Path.GetFullPath(group.Mods[0].Path) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28), MinWidth = 28 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170), MinWidth = 120 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40), MinWidth = 40 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40), MinWidth = 40 });

        // Groups are load-order units too, so give the group the same drag affordance as
        // individual PAK rows. The group handle carries every child path as its payload.
        var groupHandle = new TextBlock
        {
            Text = "⋮⋮",
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = L("Drag to change load order"),
            DataContext = group.Mods.Select(m => m.Path).ToArray()
        };
        AttachPakDragHandlers(groupHandle, group.Mods[0].Path, group.Mods.Select(m => m.Path));
        Grid.SetColumn(groupHandle, 0);
        row.Children.Add(groupHandle);

        var groupStateKey = PakGroupStateKey(group);
        var groupIsExpanded = _expandedPakGroups.Contains(groupStateKey);

        var namePanel = new Grid { MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        namePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        namePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chevron = new TextBlock
        {
            Text = groupIsExpanded ? "⌄" : "›",
            FontSize = 22,
            Width = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["SecondaryBrush"]
        };
        var title = new TextBlock
        {
            Text = group.Name,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        namePanel.Children.Add(chevron);
        namePanel.Children.Add(title);
        Grid.SetColumn(title, 1);

        // Keep the group selector on the exact same BrowseButtonStyle as normal mod rows.
        var expand = new Button
        {
            Content = namePanel,
            MinWidth = 0,
            Height = 36,
            Style = (Style)Resources["BrowseButtonStyle"],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = group.Mods[0].Path
        };
        Grid.SetColumn(expand, 1);
        row.Children.Add(expand);

        var version = new TextBlock
        {
            Text = group.VersionText,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 4, 0),
            Foreground = (Brush)Resources[group.VersionText.Contains("Outdated", StringComparison.OrdinalIgnoreCase) ? "AccentBrush" : "SecondaryBrush"]
        };
        Grid.SetColumn(version, 2);
        row.Children.Add(version);

        var allEnabled = group.Mods.All(m => m.Enabled);
        var toggleIconSource = LoadModIcon(allEnabled ? "Disable.png" : "Enable.png");
        var toggleIcon = new Border
        {
            Width = 18, Height = 18,
            Background = (Brush)Resources["ForegroundBrush"],
            OpacityMask = new ImageBrush(toggleIconSource) { Stretch = Stretch.Uniform }
        };
        var toggle = new Button
        {
            Content = toggleIcon, Width = 34, Height = 34, Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)Resources["ModIconButtonStyle"],
            ToolTip = allEnabled ? L("Disable mod") : L("Enable mod")
        };
        toggle.Click += (_, _) => SetPakGroupEnabled(group, !allEnabled);
        Grid.SetColumn(toggle, 3);
        row.Children.Add(toggle);

        var menu = new Button
        {
            Content = "⋮", Width = 34, Height = 34, Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)Resources["ModIconButtonStyle"], ToolTip = L("Mod options")
        };
        menu.Click += (_, _) => ShowPakGroupContextMenu(group, menu);
        Grid.SetColumn(menu, 4);
        row.Children.Add(menu);

        var children = new StackPanel
        {
            Visibility = groupIsExpanded ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(18, 2, 0, 0)
        };
        foreach (var mod in group.Mods)
            children.Children.Add(CreatePakChildRow(mod, _modListMetadataCache));
        Grid.SetRow(children, 1);
        outer.Children.Add(row);
        outer.Children.Add(children);

        expand.Click += (_, _) =>
        {
            var open = children.Visibility != Visibility.Visible;
            children.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = open ? "⌄" : "›";

            if (open)
                _expandedPakGroups.Add(groupStateKey);
            else
                _expandedPakGroups.Remove(groupStateKey);
        };
        return outer;
    }

    private Grid CreatePakChildRow(ModEntry mod, Dictionary<string, NexusModMetadata>? metadataCache = null)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Background = _selectedPakModPaths.Contains(mod.Path) ? new SolidColorBrush(((SolidColorBrush)Resources["AccentBrush"]).Color) { Opacity = 0.22 } : Brushes.Transparent, Tag = Path.GetFullPath(mod.Path) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28), MinWidth = 28 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40), MinWidth = 40 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40), MinWidth = 40 });
        var handle = new TextBlock { Text = "⋮⋮", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = L("Drag to change load order") };
        AttachPakDragHandlers(handle, mod.Path);
        Grid.SetColumn(handle, 0); row.Children.Add(handle);
        var childMeta = (metadataCache ?? _modListMetadataCache ?? LoadNexusMetadata()).GetValueOrDefault(PakMetadataKey(mod.Path));
        var childDisplayName = !string.IsNullOrWhiteSpace(childMeta?.DisplayName)
            ? childMeta.DisplayName
            : Path.GetFileNameWithoutExtension(mod.Path);

        var button = new Button
        {
            Content = childDisplayName,
            Style = (Style)Resources["BrowseButtonStyle"],
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = mod,
            ToolTip = Path.GetFileName(mod.Path)
        };
        button.Click += (_, _) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                TogglePakMultiSelection(mod.Path, row);
                return;
            }
            OpenModNexusSlideout(mod);
        };
        Grid.SetColumn(button, 1); row.Children.Add(button);

        var toggleIconSource = LoadModIcon(mod.Enabled ? "Disable.png" : "Enable.png");
        var toggleIcon = new Border { Width = 18, Height = 18, Background = (Brush)Resources["ForegroundBrush"], OpacityMask = new ImageBrush(toggleIconSource) { Stretch = Stretch.Uniform } };
        var toggle = new Button { Content = toggleIcon, Width = 34, Height = 34, Margin = new Thickness(6,0,0,0), Style = (Style)Resources["ModIconButtonStyle"], Tag = mod, ToolTip = mod.Enabled ? L("Disable mod") : L("Enable mod") };
        toggle.Click += ModToggle_Click;
        Grid.SetColumn(toggle, 2); row.Children.Add(toggle);

        var menu = new Button { Content = "⋮", Width = 34, Height = 34, Margin = new Thickness(6,0,0,0), Style = (Style)Resources["ModIconButtonStyle"], Tag = mod, ToolTip = L("Mod options") };
        menu.Click += ModContextButton_Click;
        Grid.SetColumn(menu, 3); row.Children.Add(menu);
        return row;
    }

    private void SetPakGroupEnabled(PakModGroup group, bool enabled)
    {
        try
        {
            foreach (var mod in group.Mods) SetPakModEnabledWithoutRefresh(mod, enabled);
            RefreshModManagerAfterStateChange();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetPakPathEnabled(string gameRoot, string source, bool enabled)
    {
        source = Path.GetFullPath(source);
        if (!File.Exists(source)) return;

        var gameMods = GetPakModsRoot(gameRoot);
        Directory.CreateDirectory(gameMods);

        var order = GetOrderedPakPaths();
        var index = order.FindIndex(p => string.Equals(Path.GetFullPath(p), source, StringComparison.OrdinalIgnoreCase));

        // A source that is not in the persisted load order cannot be assigned a
        // positional RRModHub link safely. Keep it disabled until the order is fixed.
        if (index < 0) return;

        var target = Path.Combine(gameMods, $"RRModHub_{index + 1:000}_p.pak");

        if (!enabled)
        {
            // Disable = delete only. Deleting a symbolic link never needs UAC.
            try
            {
                if (File.Exists(target) || Directory.Exists(target))
                    File.Delete(target);
            }
            catch { }

            // Remove any old/legacy representation of this exact source too.
            foreach (var file in Directory.EnumerateFiles(gameMods, "*", SearchOption.TopDirectoryOnly).ToList())
            {
                try
                {
                    var info = new FileInfo(file);
                    var linkTarget = info.LinkTarget == null
                        ? null
                        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, info.LinkTarget));

                    if (string.Equals(linkTarget, source, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(file), Path.GetFileName(source), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(file), Path.GetFileName(source) + ".RRModHub.DISABLED", StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                    }
                }
                catch { }
            }
            return;
        }

        // Enable = create exactly one positional link. Existing links are left
        // alone when they already point to the correct source.
        try
        {
            var info = new FileInfo(target);
            if (info.LinkTarget != null)
            {
                var currentTarget = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(target)!, info.LinkTarget));
                if (string.Equals(currentTarget, source, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }
        catch { }

        try
        {
            if (File.Exists(target) || Directory.Exists(target))
                File.Delete(target);
        }
        catch { }

        CreateSymbolicLinkWithElevation(source, target);
    }

    private void SetPakModEnabledWithoutRefresh(ModEntry mod, bool enabled)
    {
        SetPakPathEnabled(GetVerifiedGameRoot(), mod.Path, enabled);
    }

    private void ChangePakGroupName(PakModGroup group)
    {
        var first = group.Mods[0];
        var data = LoadNexusMetadata();
        var existing = data.GetValueOrDefault(PakMetadataKey(first.Path));
        var currentGroupName = !string.IsNullOrWhiteSpace(existing?.GroupDisplayName)
            ? existing!.GroupDisplayName
            : (!string.IsNullOrWhiteSpace(existing?.DisplayName) ? existing.DisplayName : group.Name);

        var input = ShowTextInputDialog(
            L("Enter the display name for this mod group:"),
            L("Change Name"),
            currentGroupName);

        if (string.IsNullOrWhiteSpace(input)) return;

        foreach (var mod in group.Mods)
        {
            var key = PakMetadataKey(mod.Path);
            var meta = data.GetValueOrDefault(key);
            if (meta == null)
                meta = new NexusModMetadata(group.Name, group.Game, group.ModId, 0, "");

            data[key] = meta with { GroupDisplayName = input.Trim() };
        }

        SaveNexusMetadata(data);
        RefreshModManager();
    }

    private void ShowPakGroupContextMenu(PakModGroup group, Button owner)
    {
        var menu = new ContextMenu();
        var allEnabled = group.Mods.All(m => m.Enabled);
        menu.Items.Add(MenuItem(allEnabled ? L("Disable") : L("Enable"), (_, _) => SetPakGroupEnabled(group, !allEnabled)));
        menu.Items.Add(MenuItem(L("Delete"), (_, _) => DeletePakGroup(group)));
        menu.Items.Add(MenuItem(L("Change Name"), (_, _) => ChangePakGroupName(group)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(L("Open Nexus Page"), (_, _) => OpenUrl($"https://www.nexusmods.com/{group.Game}/mods/{group.ModId}")));
        menu.Items.Add(MenuItem(L("Unlink Nexus"), (_, _) => UnlinkPakGroup(group)));
        menu.IsOpen = true;
    }

    private void DeletePakGroup(PakModGroup group)
    {
        if (MessageBox.Show(L("Delete mod '{0}' and all of its files?\n\nThis cannot be undone.", group.Name), L("Delete Mod"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var data = LoadNexusMetadata();
            foreach (var mod in group.Mods)
            {
                File.Delete(mod.Path);
                data.Remove(PakMetadataKey(mod.Path));
            }
            SaveNexusMetadata(data);
            RefreshModManager();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void UnlinkPakGroup(PakModGroup group)
    {
        var data = LoadNexusMetadata();
        foreach (var mod in group.Mods) data.Remove(PakMetadataKey(mod.Path));
        SaveNexusMetadata(data);
        RefreshModManager();
    }

    private void PopulateModList(ListBox list, IEnumerable<ModEntry> mods, bool isPak, Dictionary<string, NexusModMetadata>? metadataCache = null)
    {
        list.Items.Clear();
        var metadata = metadataCache ?? _modListMetadataCache ?? LoadNexusMetadata();
        foreach (var mod in mods)
            list.Items.Add(CreateModRow(mod, metadata));
    }

    private void PopulatePendingModList(ListBox list, IEnumerable<PendingModEntry> mods)
    {
        foreach (var mod in mods)
            list.Items.Add(CreatePendingModRow(mod));
    }

    private string GetModVersionStatusText(string installed, string latest)
    {
        if (string.IsNullOrWhiteSpace(installed) && string.IsNullOrWhiteSpace(latest))
            return L("Version Unknown");

        // Do not append "- Unknown", "- Latest", or "- Outdated".
        // If there is no installed version, the known latest version is still
        // useful and should be displayed plainly.
        if (string.IsNullOrWhiteSpace(installed))
            return L("Version {0}", latest);

        return L("Version {0}", installed);
    }

    private sealed class PosterSearchResult
    {
        public required string SourceUrl { get; init; }
        public required string LocalPath { get; init; }
        public string? SourcePage { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public double Aspect => Width > 0 ? (double)Height / Width : 0;
    }

    private readonly List<PosterSearchResult> _posterSearchResults = new();
    private CancellationTokenSource? _posterSearchCts;
    private string? _posterSearchCacheDirectory;
    private WebView2? _posterSearchWebView;

    private ModEntry? _posterBrowserMod;
    private string? _posterBrowserDirectory;
    private List<string> _posterBrowserFiles = new();
    private readonly Dictionary<string, Image> _posterBrowserImageControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _posterBrowserLoading = new(StringComparer.OrdinalIgnoreCase);
    private string? _posterBrowserSelectedFile;
    private bool _showInvalidPosterImages;
    private CancellationTokenSource? _posterBrowserSelectedLoadCts;
    private readonly List<string> _posterAutoAddFiles = new();
    private ModEntry? _posterAutoAddMod;
    private string? _posterImageEditorSourceFile;
    private BitmapSource? _posterImageEditorSource;
    private BitmapSource? _posterImageEditorAdjustedSource;
    private bool _posterImageEditorUpdatingControls;
    private Point _posterImageEditorDragStart;
    private Point _posterImageEditorOffset;
    private Point _posterImageEditorStartOffset;
    private bool _posterImageEditorDragging;
    private int _posterImageEditorOriginalWidth;
    private int _posterImageEditorOriginalHeight;
    private TaskCompletionSource<bool>? _posterUpscaleComparisonTcs;
    private BitmapSource? _posterImageEditorPreUpscaleSource;
    private string? _posterImageEditorOriginalFile;
    private bool _posterImageEditorFlipHorizontal;
    private bool _posterImageEditorFlipVertical;
    private int _posterImageEditorRotation;
    private readonly Dictionary<string, double> _posterImageEditorEffectValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _posterImageEditorEffectEnabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<string> _posterImageEditorUndo = new();
    private readonly Stack<string> _posterImageEditorRedo = new();
    private bool _posterImageEditorHistoryApplying;
    private DateTime _posterImageEditorLastHistoryCapture = DateTime.MinValue;
    private string _posterImageEditorEffectsCategory = "Basic";
    private static readonly Dictionary<string, string[]> PosterImageEditorEffectCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Basic"] = new[] { "Clarity", "Denoise", "Blur", "Vignette", "Grain", "Fade", "Softness" },
        ["CRT / Old-TV"] = new[] { "Scanlines", "Phosphor", "CRT Glow", "Screen Curvature", "Chromatic Aberration", "Flicker", "Interlacing", "CRT Noise" },
        ["Retro Console"] = new[] { "Pixelation", "Palette Reduction", "Color Banding", "Dithering", "Pixel Grid", "Color Bleed" },
        ["VHS / Analogue"] = new[] { "VHS Noise", "Tracking Distortion", "Horizontal Tearing", "Tape Grain", "Color Bleed", "Chromatic Offset", "Scanline Jitter", "Image Warping" },
        ["Print / Physical Media"] = new[] { "Halftone", "Print Dots", "Ink Bleed", "Paper Grain", "Misregistration", "Faded Ink", "Poster Wear", "Scratches" },
        ["Old Film"] = new[] { "Film Grain", "Dust", "Film Scratches", "Film Flicker", "Film Fade", "Sepia", "Light Leaks", "Gate Weave" }
    };

    private sealed class PosterRules
    {
        public string? PosterDirectory { get; init; }
        public int? MaxFiles { get; init; }
        public bool? UseSubDirectories { get; init; }
        public HashSet<string> SupportedExtensions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public double? MinAspect { get; init; }
        public double? MaxAspect { get; init; }
        public bool? RequiresPortrait { get; init; }
    }

    private static string GetPosterValidationTemplate()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/RetroRewindModhub;component/invalid.txt", UriKind.Absolute));
            if (resource != null)
            {
                using var reader = new StreamReader(resource.Stream);
                return reader.ReadToEnd().Trim();
            }
        }
        catch { }

        return "{Mod_Name_Here} Requires portrait images.\n\nInvalid Resolution ({The_Images_Resolution}) Detected!\n\nInvalid Aspect Ratio ({The_Images_Aspect_Ratio}) Detected!\nExpected Aspect Ratio: {Mods_Minimum} / {Mods_maximum}\n\nInvalid File Format ({The_Images_Extension}) Detected!\nExpected File Format: {Mods_Excepted_Extensions}";
    }

    private static string FormatPosterValidationReason(ModEntry? mod, PosterRules rules, string path, int width, int height, double aspect, bool invalidResolution, bool invalidAspect, bool invalidFormat, bool invalidOrientation)
    {
        var template = GetPosterValidationTemplate();
        var modName = mod?.Name ?? "This mod";
        var resolution = width > 0 && height > 0 ? $"{width}x{height}" : "Unavailable";
        var aspectText = width > 0 && height > 0 ? aspect.ToString("0.###", CultureInfo.InvariantCulture) : "Unavailable";
        var minText = rules.MinAspect?.ToString("0.###", CultureInfo.InvariantCulture) ?? "Not specified";
        var maxText = rules.MaxAspect?.ToString("0.###", CultureInfo.InvariantCulture) ?? "Not specified";
        var extensions = rules.SupportedExtensions.Count > 0
            ? string.Join(", ", rules.SupportedExtensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            : "Not specified";

        var lines = template.Replace("{Mod_Name_Here}", modName, StringComparison.Ordinal)
            .Replace("{The_Images_Resolution}", resolution, StringComparison.Ordinal)
            .Replace("{The_Images_Aspect_Ratio}", aspectText, StringComparison.Ordinal)
            .Replace("{Mods_Minimum}", minText, StringComparison.Ordinal)
            .Replace("{Mods_maximum}", maxText, StringComparison.Ordinal)
            .Replace("{The_Images_Extension}", Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("{Mods_Excepted_Extensions}", extensions, StringComparison.Ordinal);

        var sourceLines = lines.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var output = new List<string>();
        foreach (var line in sourceLines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Invalid Resolution", StringComparison.OrdinalIgnoreCase) && !invalidResolution) continue;
            if (trimmed.StartsWith("Invalid Aspect Ratio", StringComparison.OrdinalIgnoreCase) && !invalidAspect) continue;
            if (trimmed.StartsWith("Expected Aspect Ratio", StringComparison.OrdinalIgnoreCase) && !invalidAspect) continue;
            if (trimmed.StartsWith("Invalid File Format", StringComparison.OrdinalIgnoreCase) && !invalidFormat) continue;
            if (trimmed.StartsWith("Expected File Format", StringComparison.OrdinalIgnoreCase) && !invalidFormat) continue;
            if (string.IsNullOrWhiteSpace(trimmed) && output.Count > 0 && string.IsNullOrWhiteSpace(output[^1])) continue;
            output.Add(line);
        }

        // The supplied template's first line describes the portrait requirement.
        // Keep it only when the mod actually declares that orientation rule.
        if (!invalidOrientation)
        {
            output.RemoveAll(line => line.Trim().Contains("Requires portrait images", StringComparison.OrdinalIgnoreCase));
        }

        return string.Join(Environment.NewLine, output).Trim();
    }

    private PosterRules ReadPosterRules(ModEntry mod)
    {
        var rules = new PosterRules();
        try
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.Path) || !Directory.Exists(mod.Path))
                return rules;

            var config = FindModConfig(mod.Path);
            var configText = config != null && File.Exists(config.Value.path)
                ? File.ReadAllText(config.Value.path)
                : string.Empty;

            string? GetConfigString(string key)
            {
                var m = Regex.Match(configText, $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*([""'])(.*?)\1\s*,?\s*(?:--.*)?$");
                return m.Success ? m.Groups[2].Value.Trim().Replace("\\\\", "\\") : null;
            }

            int? GetConfigInt(string key)
            {
                var m = Regex.Match(configText, $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*(-?\d+)");
                return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? Math.Max(0, v) : null;
            }

            bool? GetConfigBool(string key)
            {
                var m = Regex.Match(configText, $@"(?mi)^\s*{Regex.Escape(key)}\s*=\s*(true|false)\b");
                return m.Success && bool.TryParse(m.Groups[1].Value, out var v) ? v : null;
            }

            var posterDirectory = GetConfigString("PosterDirectory");
            var maxFiles = GetConfigInt("MaxPosterFiles");
            var useSubdirectories = GetConfigBool("UseSubDirectories");

            string mainLua = string.Empty;
            var mainCandidates = new[]
            {
                Path.Combine(mod.Path, "Scripts", "main.lua"),
                Path.Combine(mod.Path, "main.lua")
            };
            var mainPath = mainCandidates.FirstOrDefault(File.Exists);
            if (mainPath != null)
                mainLua = File.ReadAllText(mainPath);

            // Poster-specific values are discovered from the mod's own Lua code.
            // No poster dimensions or extensions are stored in ModHub.
            double? GetLuaNumber(string name)
            {
                var m = Regex.Match(mainLua, $@"(?m)^\s*(?:local\s+)?{Regex.Escape(name)}\s*=\s*(-?(?:\d+(?:\.\d*)?|\.\d+))\b");
                return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            }

            var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Extract extension tests from the mod's own supported-poster function.
            var extMatches = Regex.Matches(mainLua, @"(?i)match\(\s*[""']%\.([a-z0-9]+)\$[""']\s*\)");
            foreach (Match m in extMatches)
            {
                var ext = "." + m.Groups[1].Value;
                if (!ext.Equals(".dds", StringComparison.OrdinalIgnoreCase)) supported.Add(ext);
            }
            supported.Remove(".dds");
            supported.Add(".png");

            bool? portrait = null;
            var validationFunction = Regex.Match(mainLua, @"(?s)function\s+is_vanilla_poster_aspect\s*\(.*?\nend");
            if (validationFunction.Success)
            {
                // Derive orientation from the actual comparison used by the mod.
                if (Regex.IsMatch(validationFunction.Value, @"\bh\s*>\s*w\b")) portrait = true;
                else if (Regex.IsMatch(validationFunction.Value, @"\bw\s*>\s*h\b")) portrait = false;
            }

            return new PosterRules
            {
                PosterDirectory = posterDirectory,
                MaxFiles = maxFiles,
                UseSubDirectories = useSubdirectories,
                SupportedExtensions = supported,
                MinAspect = GetLuaNumber("POSTER_MIN_ASPECT"),
                MaxAspect = GetLuaNumber("POSTER_MAX_ASPECT"),
                RequiresPortrait = portrait
            };
        }
        catch
        {
            return rules;
        }
    }

    private static string GetPosterSourceDirectory(ModEntry mod)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var safeName = SanitizeFileName(mod.Name);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Mod";
        return Path.Combine(documents, "Retro Rewind Modhub", "Images", safeName);
    }

    private async Task MigrateLegacyPosterOriginalsAsync(ModEntry mod)
    {
        var sourceDir = GetPosterSourceDirectory(mod);
        Directory.CreateDirectory(sourceDir);

        // Older builds stored originals in _Backup/_Buckup. Fold those files
        // into the single canonical Images\{Mod} folder, then remove the
        // obsolete folders. An existing canonical original always wins.
        var legacyDirs = new[]
        {
            Path.Combine(sourceDir, "_Backup"),
            Path.Combine(sourceDir, "_Buckup")
        };

        foreach (var legacyDir in legacyDirs)
        {
            if (!Directory.Exists(legacyDir)) continue;
            foreach (var file in Directory.EnumerateFiles(legacyDir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var destination = Path.Combine(sourceDir, Path.GetFileName(file));
                try
                {
                    if (!File.Exists(destination))
                        File.Move(file, destination);
                    else
                        TryDeleteFile(file);
                }
                catch { }
            }
            TryDeleteDirectory(legacyDir);
        }

        await Task.CompletedTask;
    }

    private bool IsSourcePosterFile(string file)
        => _posterBrowserMod != null && file.StartsWith(GetPosterSourceDirectory(_posterBrowserMod) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private async Task<string> EnsurePosterOriginalInWorkspaceAsync(string sourceFile)
    {
        if (_posterBrowserMod == null) throw new InvalidOperationException("No mod is selected.");
        if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
            throw new FileNotFoundException("The source image could not be found.", sourceFile);

        var mod = _posterBrowserMod;
        var sourceDir = GetPosterSourceDirectory(mod);
        Directory.CreateDirectory(sourceDir);
        await MigrateLegacyPosterOriginalsAsync(mod);

        var fileName = Path.GetFileName(sourceFile);
        var canonicalSource = Path.Combine(sourceDir, fileName);

        // The Images\{Mod} folder is the only original store. If it already
        // contains this filename, never create another copy or overwrite it.
        if (!File.Exists(canonicalSource))
        {
            if (string.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(canonicalSource), StringComparison.OrdinalIgnoreCase))
                return canonicalSource;
            await Task.Run(() => File.Copy(sourceFile, canonicalSource, false));
        }

        return canonicalSource;
    }

    private async Task<int> MoveInvalidPosterImagesToSourceAsync(ModEntry mod, string directory, PosterRules rules, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        var sourceDir = GetPosterSourceDirectory(mod);
        Directory.CreateDirectory(sourceDir);
        await MigrateLegacyPosterOriginalsAsync(mod);

        var option = rules.UseSubDirectories == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(directory, "*.*", option)
            .Where(IsEditableImageFile)
            .ToList();

        var moved = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsValidPosterImage(file, rules, out _, mod))
                continue;

            var destination = Path.Combine(sourceDir, Path.GetFileName(file));
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                // Invalid poster files are recovered into the original workspace.
                // The requested behavior is an overwrite when that filename already
                // exists there, so the workspace receives the latest invalid source.
                File.Move(file, destination, true);
                moved++;
            }
            catch
            {
                // Leave files that cannot be moved in place; they remain visible as invalid.
            }

            if ((moved & 7) == 0)
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }

        return moved;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value.Trim();
    }

    private static string? DetectImageExtension(byte[] bytes, string? mediaType, string url)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        var ext = Path.GetExtension(url.Split('?')[0]).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg") return ext == ".jpeg" ? ".jpg" : ext;
        return mediaType?.ToLowerInvariant() switch { "image/png" => ".png", "image/jpeg" => ".jpg", _ => null };
    }

    private static bool IsEditableImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> EnsureRealEsrganAsync()
    {
        var tools = Path.Combine(DefaultModhubFolder, "Tools", "RealESRGAN");
        Directory.CreateDirectory(tools);
        var exe = Path.Combine(tools, "realesrgan-ncnn-vulkan.exe");
        var modelDir = Path.Combine(tools, "models");
        if (File.Exists(exe) && new FileInfo(exe).Length > 1_000_000 && Directory.Exists(modelDir) && Directory.EnumerateFiles(modelDir, "*.*", SearchOption.TopDirectoryOnly).Any())
            return exe;

        Directory.CreateDirectory(tools);
        var workRoot = Path.Combine(ToolsDirectory, ".download_realesrgan");
        var zipPath = Path.Combine(workRoot, "realesrgan-windows.zip");
        var extractRoot = Path.Combine(workRoot, "extract");
        try
        {
            Directory.CreateDirectory(workRoot);
            SetOperationBusy(true, L("Downloading Real-ESRGAN…"));
            SetRequiredFileCardState("Real-ESRGAN", L("Downloading Real-ESRGAN…"), L("Downloading…"), false, true);
            await DownloadToolFileAsync(RealEsrganDownloadUrl, zipPath, "Real-ESRGAN");
            SetRequiredFileCardState("Real-ESRGAN", L("Installing Real-ESRGAN…"), L("Installing…"), false, false);
            if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true);
            ZipFile.ExtractToDirectory(zipPath, extractRoot, true);
            var candidate = Directory.EnumerateFiles(extractRoot, "realesrgan-ncnn-vulkan.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (candidate == null) throw new InvalidDataException(L("The Real-ESRGAN package did not contain the expected executable."));
            File.Copy(candidate, exe, true);
            var sourceModels = Directory.EnumerateDirectories(extractRoot, "models", SearchOption.AllDirectories).FirstOrDefault();
            if (sourceModels == null) throw new InvalidDataException(L("The Real-ESRGAN package did not contain its models."));
            if (Directory.Exists(modelDir)) Directory.Delete(modelDir, true);
            CopyDirectory(sourceModels, modelDir);
            if (!File.Exists(exe) || !Directory.EnumerateFiles(modelDir, "*.*", SearchOption.TopDirectoryOnly).Any())
                throw new InvalidDataException(L("Real-ESRGAN was installed, but its executable or models are missing."));
            return exe;
        }
        finally
        {
            try { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, true); } catch { }
        }
    }

    private async Task<string> EnsureTexconvAsync()
    {
        Directory.CreateDirectory(ToolsDirectory);
        var exe = Path.Combine(ToolsDirectory, "texconv.exe");
        if (File.Exists(exe) && new FileInfo(exe).Length > 100_000)
            return exe;
        var workRoot = Path.Combine(ToolsDirectory, ".download_texconv");
        var downloadPath = Path.Combine(workRoot, "texconv.exe");
        try
        {
            Directory.CreateDirectory(workRoot);
            SetOperationBusy(true, L("Downloading texconv…"));
            SetRequiredFileCardState("texconv", L("Downloading texconv…"), L("Downloading…"), false, true);
            await DownloadToolFileAsync(TexconvDownloadUrl, downloadPath, "texconv");
            SetOperationBusy(true, L("Installing texconv…"));
            var staged = exe + ".new";
            await CopyFileWithRetryAsync(downloadPath, staged, false);
            await ReplaceFileWithRetryAsync(staged, exe);
            if (!File.Exists(exe) || new FileInfo(exe).Length < 100_000)
                throw new InvalidDataException(L("texconv was downloaded, but the executable could not be verified."));
            return exe;
        }
        finally
        {
            try { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, true); } catch { }
        }
    }

    private async Task<string> EnsureRepakAsync()
    {
        Directory.CreateDirectory(ToolsDirectory);
        var exe = Path.Combine(ToolsDirectory, "repak.exe");
        if (File.Exists(exe) && new FileInfo(exe).Length > 100_000 && await VerifyExecutableAsync(exe, "--version"))
            return exe;

        var workRoot = Path.Combine(ToolsDirectory, ".download_repak");
        var zipPath = Path.Combine(workRoot, "repak.zip");
        var extractRoot = Path.Combine(workRoot, "extract");
        try
        {
            Directory.CreateDirectory(workRoot);
            SetOperationBusy(true, L("Downloading repak…"));
            SetRequiredFileCardState("repak", L("Downloading repak…"), L("Downloading…"), false, true);
            await DownloadToolFileAsync(RepakDownloadUrl, zipPath, "repak");
            SetRequiredFileCardState("repak", L("Installing repak…"), L("Installing…"), false, false);
            if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true);
            ZipFile.ExtractToDirectory(zipPath, extractRoot, true);
            var candidate = Directory.EnumerateFiles(extractRoot, "repak.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (candidate == null) throw new InvalidDataException(L("The repak package did not contain repak.exe."));
            var staged = exe + ".new";
            await CopyFileWithRetryAsync(candidate, staged, false);
            await ReplaceFileWithRetryAsync(staged, exe);
            if (!await VerifyExecutableAsync(exe, "--version")) throw new InvalidDataException(L("repak was installed, but could not be verified."));
            return exe;
        }
        finally
        {
            try { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, true); } catch { }
        }
    }

    private string? GetConfiguredPosterDirectory(ModEntry mod)
    {
        var rules = ReadPosterRules(mod);
        if (string.IsNullOrWhiteSpace(rules.PosterDirectory))
            return null;
        try
        {
            return Path.IsPathRooted(rules.PosterDirectory)
                ? Path.GetFullPath(rules.PosterDirectory)
                : Path.GetFullPath(Path.Combine(mod.Path, rules.PosterDirectory));
        }
        catch { return null; }
    }

    private static bool TryGetPosterDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return false;
                width = decoder.Frames[0].PixelWidth;
                height = decoder.Frames[0].PixelHeight;
                return width > 0 && height > 0;
            }

        }
        catch { }
        return false;
    }

    private static bool TryGetPosterDimensions(string path, out int width, out int height, out double aspect)
    {
        aspect = 0;
        if (!TryGetPosterDimensions(path, out width, out height))
            return false;
        aspect = width > 0 ? (double)height / width : 0;
        return width > 0 && height > 0;
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            return true;
        }
        catch { return false; }
    }

    private static bool IsSupportedPosterExtension(string path, PosterRules rules)
        => rules.SupportedExtensions.Count > 0 && rules.SupportedExtensions.Contains(Path.GetExtension(path));

    private bool IsValidPosterImage(string path, PosterRules rules, out string reason, ModEntry? mod = null)
    {
        reason = string.Empty;
        var extensionValid = IsSupportedPosterExtension(path, rules);
        var dimensionsReadable = TryGetPosterDimensions(path, out var width, out var height);
        var aspect = width > 0 ? (double)height / width : 0;
        var invalidFormat = !extensionValid;
        var invalidResolution = !dimensionsReadable;
        var invalidOrientation = rules.RequiresPortrait == true && dimensionsReadable && height <= width
            || rules.RequiresPortrait == false && dimensionsReadable && width <= height;
        var invalidAspect = dimensionsReadable &&
            ((rules.MinAspect.HasValue && aspect < rules.MinAspect.Value) ||
             (rules.MaxAspect.HasValue && aspect > rules.MaxAspect.Value));

        if (invalidFormat || invalidResolution || invalidOrientation || invalidAspect)
        {
            reason = FormatPosterValidationReason(mod, rules, path, width, height, aspect, invalidResolution, invalidAspect, invalidFormat, invalidOrientation);
            return false;
        }

        return true;
    }

    private int CountValidPosters(string directory, PosterRules rules, ModEntry? mod = null)
    {
        if (!Directory.Exists(directory)) return 0;
        var option = rules.UseSubDirectories == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", option))
                if (IsValidPosterImage(file, rules, out _, mod)) count++;
        }
        catch { }
        return count;
    }

    private static string BuildPosterOpenFileFilter(PosterRules rules)
    {
        if (rules.SupportedExtensions.Count == 0)
            return "All files|*.*";
        var patterns = string.Join(";", rules.SupportedExtensions.OrderBy(x => x).Select(x => "*" + x));
        return $"Supported poster images|{patterns}|All files|*.*";
    }

    private void OpenConfiguredPosterDirectory(ModEntry mod)
    {
        var directory = GetConfiguredPosterDirectory(mod);
        if (string.IsNullOrWhiteSpace(directory))
        {
            MessageBox.Show(this,
                L("This mod does not have a valid PosterDirectory configured."),
                L("Open Posters"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            OpenFolder(directory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                L("Could not open the Posters folder.\n\n{0}", ex.Message),
                L("Open Posters"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task AddPosterImagesAsync(ModEntry mod)
    {
        var staging = GetPosterSourceDirectory(mod);
        try
        {
            Directory.CreateDirectory(staging);
            var picturesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var dialog = new OpenFileDialog
            {
                Title = L("Add Poster Images"),
                Filter = L("Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|All Files|*.*"),
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true,
                InitialDirectory = !string.IsNullOrWhiteSpace(picturesDirectory) && Directory.Exists(picturesDirectory) ? picturesDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dialog.ShowDialog(this) != true || dialog.FileNames.Length == 0) return;
            SetOperationBusy(true, L("Adding images…"), null, L("Copying {0} source image(s) into the mod's image workspace.", dialog.FileNames.Length));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var added = 0; var skipped = 0;
            foreach (var source in dialog.FileNames)
            {
                var ext = Path.GetExtension(source).ToLowerInvariant();
                if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp" and not ".gif" and not ".webp" and not ".tif" and not ".tiff") { skipped++; continue; }
                var dest = Path.Combine(staging, Path.GetFileName(source));
                if (File.Exists(dest)) dest = Path.Combine(staging, Path.GetFileNameWithoutExtension(source) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture) + ext);
                File.Copy(source, dest, false); added++;
            }
            SetOperationBusy(false);
            await ViewPosterImagesAsync(mod);
            MessageBox.Show(this, L("Added {0} source image(s). {1} file(s) were skipped. Invalid/source images can now be edited into posters.", added, skipped), L("Add Images"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, L("Could not add images.\n\n{0}", ex.Message), L("Add Images"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool _posterBrowserFullscreenPreviewVisible;

    private sealed class PosterBrowserCard
    {
        public required string FilePath { get; init; }
        public required Border Card { get; init; }
        public required Image Preview { get; init; }
    }

    private void PosterBrowserSearchButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_posterBrowserMod == null)
            return;

        OpenPosterSearch(_posterBrowserMod);
    }

    private void OpenPosterSearch(ModEntry mod)
    {
        _posterSearchCts?.Cancel();
        _posterSearchCts = new CancellationTokenSource();
        _posterSearchResults.Clear();

        if (PosterSearchResultsPanel != null)
            PosterSearchResultsPanel.Children.Clear();

        if (PosterSearchQueryTextBox != null)
            PosterSearchQueryTextBox.Text = mod.Name;

        _mode = "poster_search";
        _posterBrowserMod = mod;
        _posterBrowserDirectory = GetConfiguredPosterDirectory(mod);
        UpdateMode();
    }

    private async void PosterSearchGo_Click(object? sender, RoutedEventArgs e)
    {
        await RunPosterSearchAsync();
    }

    private async void PosterSearchQueryTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunPosterSearchAsync();
        }
    }

    private static string? DetectPosterImageExtension(byte[] data, string? mediaType, string url)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return ".png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";
        var type = mediaType?.ToLowerInvariant();
        if (type == "image/png") return ".png";
        if (type == "image/jpeg" || type == "image/jpg") return ".jpg";
        var ext = Path.GetExtension(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url).ToLowerInvariant();
        return ext == ".jpeg" ? ".jpg" : (ext is ".png" or ".jpg" ? ext : null);
    }

    private async Task RunPosterSearchAsync()
    {
        var mod = _posterBrowserMod;
        if (mod == null || PosterSearchQueryTextBox == null || PosterSearchResultsPanel == null)
            return;

        var query = PosterSearchQueryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return;

        // Search terms are normalized so equivalent casing produces identical requests/cache keys.
        query = string.Join(" ", query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

        var rules = ReadPosterRules(mod);
        _posterSearchCts?.Cancel();
        _posterSearchCts = new CancellationTokenSource();
        var token = _posterSearchCts.Token;

        PosterSearchResultsPanel.Children.Clear();
        PosterSearchStatus.Text = L("Searching Google Images. Results are saved as source images so they can be edited into posters.");

        SetOperationBusy(true, L("Searching posters…"), null, L("Finding images and saving source candidates for editing."));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        try
        {
            _posterSearchCacheDirectory = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "PosterSearch", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_posterSearchCacheDirectory);

            if (_posterSearchWebView == null)
            {
                _posterSearchWebView = new WebView2
                {
                    Visibility = Visibility.Collapsed,
                    Width = 1,
                    Height = 1
                };
                _posterSearchWebView.NavigationCompleted += PosterSearchWebView_NavigationCompleted;
                if (PosterSearchHost != null)
                    PosterSearchHost.Children.Add(_posterSearchWebView);
            }

            await _posterSearchWebView.EnsureCoreWebView2Async();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(e.IsSuccess);
            _posterSearchWebView.NavigationCompleted += Handler;

            var url = "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(query);
            _posterSearchWebView.CoreWebView2.Navigate(url);
            await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15), token));
            _posterSearchWebView.NavigationCompleted -= Handler;

            if (token.IsCancellationRequested)
                return;

            var script = """
                (() => {
                    const out = [];
                    const seen = new Set();
                    const add = (value) => {
                        if (!value) return;
                        try {
                            value = new URL(value, location.href).href;
                            value = value.replace(/\\u003d/gi, '=').replace(/\\u0026/gi, '&').replace(/\\u002f/gi, '/');
                        } catch { return; }
                        if (!/^https?:/i.test(value) || seen.has(value)) return;
                        seen.add(value);
                        out.push(value);
                    };

                    // Current Google Images layouts do not always expose /imgres links.
                    // Collect original URLs from links, image data attributes, and the
                    // embedded result JSON used to build the page.
                    for (const a of document.querySelectorAll('a[href]')) {
                        try {
                            const u = new URL(a.href, location.href);
                            const original = u.searchParams.get('imgurl');
                            if (original) add(original);
                        } catch {}
                        if (out.length >= 120) break;
                    }

                    if (out.length < 120) {
                        for (const img of document.images) {
                            for (const value of [
                                img.getAttribute('data-iurl'),
                                img.getAttribute('data-original'),
                                img.getAttribute('data-fullres'),
                                img.getAttribute('data-src'),
                                img.getAttribute('data-url')
                            ]) add(value);
                            if (out.length >= 120) break;
                        }
                    }

                    if (out.length < 120) {
                        const decode = (v) => v
                            .replace(/\\u003d/gi, '=')
                            .replace(/\\u0026/gi, '&')
                            .replace(/\\u002f/gi, '/')
                            .replace(/\\u003f/gi, '?')
                            .replace(/\\u0025/gi, '%');
                        for (const script of document.scripts) {
                            const text = script.textContent || '';
                            const matches = text.match(/https?:\/\/[^\s"'<>\\]+/g) || [];
                            for (const value of matches) {
                                const decoded = decode(value);
                                // Prefer external image hosts, but allow Google-hosted
                                // originals when their URL looks like an actual image.
                                if (!/(google|gstatic)\.com/i.test(decoded) || /\.(?:png|jpe?g)(?:[?#]|$)/i.test(decoded))
                                    add(decoded);
                                if (out.length >= 120) break;
                            }
                            if (out.length >= 120) break;
                        }
                    }

                    return out;
                })()
                """;

            var raw = await _posterSearchWebView.ExecuteScriptAsync(script);
            var candidates = JsonSerializer.Deserialize<List<string>>(raw ?? "null") ?? new List<string>();

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 RetroRewindModHub");

            var accepted = 0;
            var attempted = 0;
            var staging = GetPosterSourceDirectory(mod);
            Directory.CreateDirectory(staging);

            foreach (var imageUrl in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(80))
            {
                token.ThrowIfCancellationRequested();
                attempted++;
                try
                {
                    using var response = await client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, token);
                    if (!response.IsSuccessStatusCode) continue;
                    var bytes = await response.Content.ReadAsByteArrayAsync(token);
                    var extension = DetectImageExtension(bytes, response.Content.Headers.ContentType?.MediaType, imageUrl);
                    if (extension == null) continue;
                    var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(new Uri(imageUrl).AbsolutePath));
                    if (string.IsNullOrWhiteSpace(safeName)) safeName = "Downloaded_Image";
                    var destination = Path.Combine(staging, safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture) + extension);
                    await File.WriteAllBytesAsync(destination, bytes, token);
                    accepted++;
                    AddPosterSearchResultCard(new PosterSearchResult { SourceUrl = imageUrl, LocalPath = destination, Width = 0, Height = 0 }, mod);
                }
                catch { }
                if (accepted >= 40) break;
            }
            PosterSearchStatus.Text = L("{0} image(s) added to the image workspace from {1} candidate(s). Select one in the Poster Browser to edit it.", accepted, attempted);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PosterSearchStatus.Text = L("Search failed: {0}", ex.Message);
        }
        finally
        {
            SetOperationBusy(false);
        }
    }

    private void AddPosterSearchResultCard(PosterSearchResult result, ModEntry mod)
    {
        if (PosterSearchResultsPanel == null)
            return;

        var card = new Border
        {
            Width = 190,
            Height = 270,
            Margin = new Thickness(6),
            Padding = new Thickness(8),
            Background = (Brush)Resources["CardBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Cursor = Cursors.Hand
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var image = new Image { Stretch = Stretch.Uniform, Source = LoadPosterThumbnail(result.LocalPath) };
        Grid.SetRow(image, 0);
        grid.Children.Add(image);

        var info = new TextBlock
        {
            Text = $"{result.Width} × {result.Height}",
            Foreground = (Brush)Resources["SecondaryBrush"],
            Margin = new Thickness(0, 6, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(info, 1);
        grid.Children.Add(info);

        var add = new Button
        {
            Content = L("Add to Images"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Resources["AccentButtonStyle"]
        };
        add.Click += async (_, _) => { await Dispatcher.InvokeAsync(async () => await ViewPosterImagesAsync(mod)); };
        Grid.SetRow(add, 2);
        grid.Children.Add(add);

        card.Child = grid;
        PosterSearchResultsPanel.Children.Add(card);
    }

    private async Task AddPosterSearchResultAsync(PosterSearchResult result, ModEntry mod)
    {
        var directory = GetConfiguredPosterDirectory(mod);
        if (string.IsNullOrWhiteSpace(directory) || !File.Exists(result.LocalPath))
            return;

        try
        {
            SetOperationBusy(true, L("Adding poster…"), null, L("Copying the validated image into the mod's Posters folder."));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

            var rules = ReadPosterRules(mod);
            var currentCount = CountValidPosters(directory, rules, mod);
            if (rules.MaxFiles.HasValue && rules.MaxFiles.Value > 0 && currentCount >= rules.MaxFiles.Value)
            {
                MessageBox.Show(this, L("The mod's maximum poster limit has been reached."), L("Add Poster"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var extension = Path.GetExtension(result.LocalPath);
            var name = "Poster_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture) + extension;
            var destination = Path.Combine(directory, name);
            File.Copy(result.LocalPath, destination, false);

            MessageBox.Show(this, L("Poster added successfully."), L("Add Poster"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Could not add the poster.\n\n{0}", ex.Message), L("Add Poster"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetOperationBusy(false);
        }
    }

    private static ImageSource? LoadPosterThumbnail(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.StreamSource = ms;
            source.EndInit();
            source.Freeze();
            return source;
        }
        catch { return null; }
    }

    private void PosterSearchBackButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_posterBrowserMod != null)
            _ = ViewPosterImagesAsync(_posterBrowserMod);
        else
            ClosePosterBrowser();
    }

    private void PosterSearchWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) { }

    private async Task ViewPosterImagesAsync(ModEntry mod)
    {
        var directory = GetConfiguredPosterDirectory(mod);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        _posterBrowserMod = mod;
        _posterBrowserDirectory = directory;
        _posterBrowserFiles.Clear();
        _posterBrowserImageControls.Clear();
        _posterBrowserLoading.Clear();
        _posterBrowserSelectedFile = null;
        _posterBrowserSelectedLoadCts?.Cancel();
        SetPosterBrowserListInteractivity(true);
        if (PosterBrowserInvalidToggle != null) PosterBrowserInvalidToggle.IsChecked = _showInvalidPosterImages;

        try
        {
            Directory.CreateDirectory(directory);
            var rules = ReadPosterRules(mod);

            _mode = "poster_browser";
            UpdateMode();
            SetOperationBusy(true, L("Loading poster browser…"), null,
                L("Reading the Posters folder. Images are loaded only when visible."));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Yield();

            var option = rules.UseSubDirectories == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            // Show all image files in Posters, including unsupported/invalid formats, so they can be edited.
            var sourceDir = GetPosterSourceDirectory(mod);
            await MigrateLegacyPosterOriginalsAsync(mod);
            await MoveInvalidPosterImagesToSourceAsync(mod, directory, rules);

            // Re-read after recovery so moved invalid files no longer remain in Posters.
            var posterFiles = Directory.EnumerateFiles(directory, "*.*", option)
                .Where(IsEditableImageFile)
                .ToList();
            var sourceFiles = Directory.Exists(sourceDir)
                ? Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsEditableImageFile)
                    .ToList()
                : new List<string>();
            _posterBrowserFiles = posterFiles.Concat(sourceFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(GetPosterBrowserSortTimestampUtc)
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            BuildPosterBrowserCards(rules);
            
            SetOperationBusy(false);
            await Dispatcher.InvokeAsync(() => LoadVisiblePosterImages(), DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, L("Could not open the poster browser.\n\n{0}", ex.Message),
                L("View Images"), MessageBoxButton.OK, MessageBoxImage.Error);
            _mode = "mods";
            UpdateMode();
        }
    }

    private static DateTime GetPosterBrowserSortTimestampUtc(string file)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private void BuildPosterBrowserCards(PosterRules rules)
    {
        if (PosterBrowserItemsPanel == null || PosterBrowserScrollViewer == null)
            return;

        PosterBrowserItemsPanel.Children.Clear();
        _posterBrowserImageControls.Clear();

        var classified = _posterBrowserFiles
            .Select(file =>
            {
                string reason = string.Empty;
                bool valid;
                if (IsSourcePosterFile(file))
                {
                    valid = false;
                    reason = L("Source image. Edit it to create a valid poster.");
                }
                else
                {
                    valid = IsValidPosterImage(file, rules, out reason, _posterBrowserMod);
                }
                return new { File = file, Valid = valid, Reason = reason };
            })
            .OrderBy(x => x.Valid)
            .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allClassified = classified
            .OrderByDescending(x => GetPosterBrowserSortTimestampUtc(x.File))
            .ThenBy(x => Path.GetFileName(x.File), StringComparer.OrdinalIgnoreCase)
            .ToList();
        classified = _showInvalidPosterImages
            ? allClassified.Where(x => !x.Valid).ToList()
            : allClassified.Where(x => x.Valid && !IsSourcePosterFile(x.File)).ToList();

        var validCount = allClassified.Count(x => x.Valid && !IsSourcePosterFile(x.File));
        var invalidCount = allClassified.Count(x => !x.Valid);
        _posterBrowserFiles = classified.Select(x => x.File).ToList();

        foreach (var item in classified)
        {
            var file = item.File;
            var card = new Border
            {
                Width = 188,
                Height = 304,
                Margin = new Thickness(7),
                Padding = new Thickness(8),
                Background = (Brush)Resources["ButtonBackgroundBrush"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Tag = file,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = L("Select {0}", Path.GetFileName(file))
            };

            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var previewHost = new Border
            {
                Background = (Brush)Resources["InputBackgroundBrush"],
                CornerRadius = new CornerRadius(5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var preview = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 164,
                Height = 206,
                Tag = file,
                IsHitTestVisible = false
            };
            previewHost.Child = preview;
            panel.Children.Add(previewHost);

            var name = new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(file),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Resources["ForegroundBrush"],
                Margin = new Thickness(2, 7, 2, 0),
                ToolTip = file
            };
            Grid.SetRow(name, 1);
            panel.Children.Add(name);

            var statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2, 5, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusIconSource = LoadModIcon(item.Valid ? "valid.png" : "Invalid.png");
            var statusIcon = new Border
            {
                Width = 18,
                Height = 18,
                Background = (Brush)Resources["AccentBrush"],
                OpacityMask = new ImageBrush(statusIconSource) { Stretch = Stretch.Uniform },
                ToolTip = item.Valid ? L("Valid poster") : L("Invalid poster")
            };
            statusPanel.Children.Add(statusIcon);
            panel.Children.Add(statusPanel);

            var selectHint = new TextBlock
            {
                Text = L("Click for details"),
                Foreground = (Brush)Resources["SecondaryBrush"],
                FontSize = 10,
                Margin = new Thickness(2, 4, 2, 0)
            };
            Grid.SetRow(selectHint, 2);
            panel.Children.Add(selectHint);
            Grid.SetRow(statusPanel, 3);

            card.Child = panel;
            card.MouseEnter += (_, _) =>
            {
                card.Background = (Brush)Resources["AccentHoverBrush"];
                card.BorderBrush = (Brush)Resources["AccentBrush"];
                card.RenderTransform = new TranslateTransform(0, -1);
            };
            card.MouseLeave += (_, _) =>
            {
                card.Background = (Brush)Resources["ButtonBackgroundBrush"];
                card.BorderBrush = (Brush)Resources["BorderBrush"];
                card.RenderTransform = null;
            };
            card.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left)
                    return;

                if (_posterBrowserSelectedFile != null && PosterBrowserSelectedPanel?.Visibility == Visibility.Visible)
                {
                    CollapseSelectedPosterPanel();
                    e.Handled = true;
                    return;
                }

                SelectPosterBrowserImage(file);
                e.Handled = true;
            }), true);

            _posterBrowserImageControls[file] = preview;
            PosterBrowserItemsPanel.Children.Add(card);
        }

        PosterBrowserInfo.Text = _showInvalidPosterImages
            ? L("{0} invalid/unused image(s) • {1} valid poster(s) • Maximum: {2}", _posterBrowserFiles.Count, validCount, rules.MaxFiles?.ToString() ?? L("Not specified"))
            : L("{0} valid poster(s) • {1} invalid/unused image(s) hidden • Maximum: {2}", _posterBrowserFiles.Count, invalidCount, rules.MaxFiles?.ToString() ?? L("Not specified"));
        PosterBrowserScrollViewer.ScrollToHome();
    }

    private void LoadVisiblePosterImages()
    {
        if (PosterBrowserScrollViewer == null || _posterBrowserFiles.Count == 0) return;

        // Cards are fixed-height; load a generous viewport window around the current scroll position.
        var columns = Math.Max(1, (int)(PosterBrowserScrollViewer.ViewportWidth / 202));
        var rowHeight = 308d;
        var firstRow = Math.Max(0, (int)(PosterBrowserScrollViewer.VerticalOffset / rowHeight) - 1);
        var lastRow = Math.Min((int)Math.Ceiling((double)_posterBrowserFiles.Count / columns),
            firstRow + (int)Math.Ceiling(PosterBrowserScrollViewer.ViewportHeight / rowHeight) + 2);
        var start = firstRow * columns;
        var end = Math.Min(_posterBrowserFiles.Count, lastRow * columns);

        for (var i = start; i < end; i++)
        {
            var file = _posterBrowserFiles[i];
            if (_posterBrowserImageControls.TryGetValue(file, out var image) && image.Source == null)
                _ = LoadPosterThumbnailAsync(file, image);
        }
    }

    private async Task LoadPosterThumbnailAsync(string file, Image image)
    {
        if (!_posterBrowserLoading.Add(file)) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(file);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!File.Exists(file)) return;
                using var stream = new MemoryStream(bytes, false);
                var source = new BitmapImage();
                source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.StreamSource = stream; source.EndInit(); source.Freeze();
                image.Source = CropPosterPreviewToUsableArea(source);
            }, DispatcherPriority.Background);
        }
        catch { }
        finally { _posterBrowserLoading.Remove(file); }
    }

    private async Task ReloadPosterBrowserAsync()
    {
        if (_posterBrowserMod == null) return;
        await ViewPosterImagesAsync(_posterBrowserMod);
    }

    private void PosterBrowserScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        LoadVisiblePosterImages();
    }

    private void SelectPosterBrowserImage(string file)
    {
        if (!File.Exists(file))
            return;

        _posterBrowserSelectedFile = file;
        PosterBrowserSelectedPanel.Visibility = Visibility.Visible;
        SetPosterBrowserListInteractivity(false);
        PosterBrowserSelectedPanel.Opacity = 0;
        PosterBrowserSelectedPanel.MaxHeight = 0;

        PopulateSelectedPosterDetails(file);

        var maxHeight = 340d;
        var animation = new DoubleAnimation(0, maxHeight, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        PosterBrowserSelectedPanel.BeginAnimation(MaxHeightProperty, animation);
        PosterBrowserSelectedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        _posterBrowserSelectedLoadCts?.Cancel();
        _posterBrowserSelectedLoadCts = new CancellationTokenSource();
        _ = LoadSelectedPosterPreviewAsync(file, _posterBrowserSelectedLoadCts.Token);
    }

    private void PopulateSelectedPosterDetails(string file)
    {
        var rules = _posterBrowserMod != null ? ReadPosterRules(_posterBrowserMod) : new PosterRules();
        string reason = string.Empty;
        bool valid;
        if (IsSourcePosterFile(file))
        {
            valid = false;
            reason = L("Source image. Use Edit to crop, resize and convert it to the mod's poster format.");
        }
        else
        {
            valid = IsValidPosterImage(file, rules, out reason, _posterBrowserMod);
        }
        TryGetPosterDimensions(file, out var width, out var height);
        var aspect = width > 0 ? (double)height / width : 0;
        var info = new FileInfo(file);
        var directory = Path.GetDirectoryName(file) ?? string.Empty;
        var relativeDirectory = _posterBrowserDirectory != null
            ? Path.GetDirectoryName(Path.GetRelativePath(_posterBrowserDirectory, file))
            : string.Empty;

        PosterBrowserSelectedFileName.Text = Path.GetFileNameWithoutExtension(file);
        PosterBrowserSelectedPath.Text = directory;
        PosterBrowserSelectedType.Text = Path.GetExtension(file).TrimStart('.').ToUpperInvariant();
        PosterBrowserSelectedSize.Text = FormatPosterFileSize(info.Exists ? info.Length : 0);
        PosterBrowserSelectedDimensions.Text = width > 0 && height > 0
            ? $"{width} × {height}"
            : L("Unavailable");
        PosterBrowserSelectedAspect.Text = width > 0 ? aspect.ToString("0.###", CultureInfo.InvariantCulture) : L("Unavailable");
        PosterBrowserSelectedRelative.Text = string.IsNullOrWhiteSpace(relativeDirectory) || relativeDirectory == "."
            ? L("Root poster folder")
            : relativeDirectory;
        PosterBrowserSelectedModified.Text = info.Exists ? info.LastWriteTime.ToString("g") : L("Unavailable");
        PosterBrowserSelectedStatus.Text = valid ? L("Valid poster") : L("Invalid poster");
        PosterBrowserSelectedStatus.Foreground = valid
            ? (Brush)Resources["SecondaryBrush"]
            : (Brush)Resources["CoreUiRedBrush"];
        PosterBrowserSelectedReason.Text = valid
            ? L("This image meets the poster requirements.")
            : reason;
        // Images that already match the poster template need no editor action.
        // Anything else remains editable, including valid-by-rule images whose texture
        // geometry is not actually compatible with the poster template.
        PosterBrowserSelectedEditButton.Visibility = IsPosterTemplateCompatible(file)
            ? Visibility.Collapsed
            : Visibility.Visible;
        var selectedStatusIconSource = LoadModIcon(valid ? "valid.png" : "Invalid.png");
        PosterBrowserSelectedStatusIcon.Background = (Brush)Resources["AccentBrush"];
        PosterBrowserSelectedStatusIcon.OpacityMask = new ImageBrush(selectedStatusIconSource) { Stretch = Stretch.Uniform };
        PosterBrowserSelectedPreview.Source = null;
        PosterBrowserSelectedPreview.ToolTip = Path.GetFileName(file);
    }

    private void PosterBrowserSelectedPreview_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (PosterBrowserSelectedPreview?.Source is not ImageSource source)
            return;

        e.Handled = true;
        OpenPosterBrowserFullscreenPreview(source);
    }

    private void OpenPosterBrowserFullscreenPreview(ImageSource source)
    {
        if (PosterBrowserFullscreenPreviewOverlay == null || PosterBrowserFullscreenPreviewImage == null)
            return;

        PosterBrowserFullscreenPreviewImage.Source = source;
        _posterBrowserFullscreenPreviewVisible = true;
        PosterBrowserFullscreenPreviewOverlay.BeginAnimation(OpacityProperty, null);
        PosterBrowserFullscreenPreviewOverlay.Visibility = Visibility.Visible;
        PosterBrowserFullscreenPreviewOverlay.Opacity = 0;
        PosterBrowserFullscreenPreviewOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        Keyboard.Focus(PosterBrowserFullscreenPreviewOverlay);
    }

    private void ClosePosterBrowserFullscreenPreview()
    {
        if (!_posterBrowserFullscreenPreviewVisible || PosterBrowserFullscreenPreviewOverlay == null)
            return;

        _posterBrowserFullscreenPreviewVisible = false;
        PosterBrowserFullscreenPreviewOverlay.BeginAnimation(OpacityProperty, null);
        var animation = new DoubleAnimation(PosterBrowserFullscreenPreviewOverlay.Opacity, 0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            PosterBrowserFullscreenPreviewOverlay.Visibility = Visibility.Collapsed;
            PosterBrowserFullscreenPreviewOverlay.BeginAnimation(OpacityProperty, null);
            PosterBrowserFullscreenPreviewOverlay.Opacity = 0;
            PosterBrowserFullscreenPreviewImage.Source = null;
        };
        PosterBrowserFullscreenPreviewOverlay.BeginAnimation(OpacityProperty, animation);
    }

    private void PosterBrowserFullscreenPreviewOverlay_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (_posterBrowserFullscreenPreviewVisible)
        {
            e.Handled = true;
            ClosePosterBrowserFullscreenPreview();
        }
    }

    private async Task LoadSelectedPosterPreviewAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.InvokeAsync(() =>
            {
                if (_posterBrowserSelectedFile == null || !string.Equals(_posterBrowserSelectedFile, file, StringComparison.OrdinalIgnoreCase)) return;
                using var stream = new MemoryStream(bytes, writable: false);
                var source = new BitmapImage();
                source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.StreamSource = stream; source.EndInit(); source.Freeze();
                PosterBrowserSelectedPreview.Source = CropPosterPreviewToUsableArea(source);
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException) { }
        catch { await Dispatcher.InvokeAsync(() => PosterBrowserSelectedPreview.ToolTip = L("Preview unavailable.")); }
    }

    private static BitmapSource CropPosterPreviewToUsableArea(BitmapSource source)
    {
        if (source == null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
            return source;

        // The poster template is defined in normalized coordinates. Scaling the
        // template geometry to the source dimensions means the same preview rule
        // works for valid textures and arbitrary source images alike.
        var g = GetPosterTemplateGeometry(source.PixelWidth, source.PixelHeight);
        var x = Math.Clamp((int)Math.Round(g.ContentX), 0, source.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Round(g.ContentY), 0, source.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Round(g.ContentX + g.ContentWidth), x + 1, source.PixelWidth);
        var bottom = Math.Clamp((int)Math.Round(g.ContentY + g.ContentHeight), y + 1, source.PixelHeight);
        var crop = new Int32Rect(x, y, right - x, bottom - y);

        try
        {
            var cropped = new CroppedBitmap(source, crop);
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            // Never make an otherwise viewable image disappear because a preview
            // crop could not be constructed.
            return source;
        }
    }

    private static string FormatPosterFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.##} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024d):0.##} MB";
        return $"{bytes / (1024d * 1024d * 1024d):0.##} GB";
    }

    private async void PosterBrowserSelectedDelete_Click(object? sender, RoutedEventArgs e)
    {
        var file = _posterBrowserSelectedFile;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            return;

        var confirm = MessageBox.Show(this,
            L("Delete this image permanently?\n\n{0}", Path.GetFileName(file)),
            L("Delete Poster"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            SetOperationBusy(true, L("Deleting poster…"), null, Path.GetFileName(file));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Yield();
            _posterBrowserSelectedLoadCts?.Cancel();
            File.Delete(file);
            _posterBrowserSelectedFile = null;
            CollapseSelectedPosterPanel();
            await ReloadPosterBrowserAsync();
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, L("Could not delete the poster.\n\n{0}", ex.Message),
                L("Delete Poster"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PosterBrowserSelectedGoTo_Click(object? sender, RoutedEventArgs e)
    {
        var file = _posterBrowserSelectedFile;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{file.Replace("\"", "") }\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Could not open the file location.\n\n{0}", ex.Message),
                L("Go To File"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetPosterBrowserListInteractivity(bool isInteractive)
    {
        if (PosterBrowserScrollViewer != null)
        {
            // The poster list must be completely static while details are open:
            // no scrolling, hover, focus, keyboard navigation, or card clicks.
            PosterBrowserScrollViewer.IsHitTestVisible = isInteractive;
            PosterBrowserScrollViewer.Focusable = isInteractive;
            KeyboardNavigation.SetIsTabStop(PosterBrowserScrollViewer, isInteractive);
            PosterBrowserScrollViewer.PanningMode = isInteractive
                ? PanningMode.VerticalOnly
                : PanningMode.None;
        }

        if (PosterBrowserItemsPanel != null)
            PosterBrowserItemsPanel.IsHitTestVisible = isInteractive;
    }

    private void CloseSelectedPosterPanelImmediately()
    {
        if (PosterBrowserSelectedPanel != null)
        {
            PosterBrowserSelectedPanel.BeginAnimation(MaxHeightProperty, null);
            PosterBrowserSelectedPanel.BeginAnimation(OpacityProperty, null);
            PosterBrowserSelectedPanel.Visibility = Visibility.Collapsed;
            PosterBrowserSelectedPanel.Opacity = 1;
        }

        _posterBrowserSelectedLoadCts?.Cancel();
        _posterBrowserSelectedFile = null;
        SetPosterBrowserListInteractivity(true);
    }

    private void CollapseSelectedPosterPanel()
    {
        if (PosterBrowserSelectedPanel == null)
        {
            SetPosterBrowserListInteractivity(true);
            return;
        }

        _posterBrowserSelectedLoadCts?.Cancel();
        _posterBrowserSelectedFile = null;

        if (PosterBrowserSelectedPanel.Visibility != Visibility.Visible)
        {
            PosterBrowserSelectedPanel.Visibility = Visibility.Collapsed;
            PosterBrowserSelectedPanel.BeginAnimation(MaxHeightProperty, null);
            PosterBrowserSelectedPanel.BeginAnimation(OpacityProperty, null);
            SetPosterBrowserListInteractivity(true);
            return;
        }

        var animation = new DoubleAnimation(PosterBrowserSelectedPanel.MaxHeight, 0, TimeSpan.FromMilliseconds(180));
        animation.Completed += (_, _) =>
        {
            PosterBrowserSelectedPanel.Visibility = Visibility.Collapsed;
            PosterBrowserSelectedPanel.BeginAnimation(MaxHeightProperty, null);
            PosterBrowserSelectedPanel.BeginAnimation(OpacityProperty, null);
            SetPosterBrowserListInteractivity(true);
        };
        PosterBrowserSelectedPanel.BeginAnimation(MaxHeightProperty, animation);
        PosterBrowserSelectedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(PosterBrowserSelectedPanel.Opacity, 0, TimeSpan.FromMilliseconds(140)));
    }

    private void PosterBrowserSelectedEdit_Click(object? sender, RoutedEventArgs e)
    {
        var file = _posterBrowserSelectedFile;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file) || _posterBrowserMod == null)
            return;

        OpenPosterImageEditor(file);
    }

    private async void PosterBrowserSelectedClose_Click(object? sender, RoutedEventArgs e)
    {
        CollapseSelectedPosterPanel();
        await Task.CompletedTask;
    }

    private bool IsPosterTemplateCompatible(string file)
    {
        if (!TryGetPosterDimensions(file, out var width, out var height))
            return false;

        var supported = (width == 1024 && height == 2048)
                     || (width == 2048 && height == 4096)
                     || (width == 4096 && height == 8192);
        if (!supported) return false;

        try
        {
            using var stream = File.OpenRead(file);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return false;
            var frame = decoder.Frames[0];
            var g = GetPosterTemplateGeometry(width, height);
            var pixels = new byte[Math.Max(1, frame.PixelWidth * frame.PixelHeight * 4)];
            if (pixels.Length > 64 * 1024 * 1024) return true; // geometry is sufficient for very large sources
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);

            bool IsBlack(int x, int y)
            {
                var i = (y * converted.PixelWidth + x) * 4;
                return pixels[i] <= 12 && pixels[i + 1] <= 12 && pixels[i + 2] <= 12;
            }

            // Sample the unused top/left/right/bottom margins. A small tolerance keeps
            // compression/encoding noise from incorrectly classifying an exact template.
            var samples = new List<(int X, int Y)>();
            var margin = Math.Max(1, (int)Math.Round(width / 256.0));
            var gx = (int)Math.Round(g.ContentX);
            var gy = (int)Math.Round(g.ContentY);
            var gr = Math.Min(width - 1, (int)Math.Round(g.ContentX + g.ContentWidth) - 1);
            var gb = Math.Min(height - 1, (int)Math.Round(g.ContentY + g.ContentHeight) - 1);
            for (int x = gx; x <= gr; x += Math.Max(1, width / 32))
            {
                samples.Add((x, Math.Max(0, gy - margin)));
                samples.Add((x, Math.Min(height - 1, gb + margin)));
            }
            for (int y = gy; y <= gb; y += Math.Max(1, height / 32))
            {
                samples.Add((Math.Max(0, gx - margin), y));
                samples.Add((Math.Min(width - 1, gr + margin), y));
            }
            return samples.Count == 0 || samples.Count(p => IsBlack(p.X, p.Y)) >= samples.Count * 0.9;
        }
        catch { return false; }
    }

    private void OpenPosterImageEditor(string file)
    {
        if (_posterBrowserMod == null || !File.Exists(file)) return;

        CloseSelectedPosterPanelImmediately();

        try
        {
            using var stream = File.OpenRead(file);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            _posterImageEditorSourceFile = file;
            _posterImageEditorSource = bitmap;
            _posterImageEditorOriginalWidth = bitmap.PixelWidth;
            _posterImageEditorOriginalHeight = bitmap.PixelHeight;
            _posterImageEditorOffset = new Point(0, 0);
            _posterImageEditorStartOffset = _posterImageEditorOffset;
            if (PosterImageEditorXEnable != null) PosterImageEditorXEnable.IsChecked = true;
            if (PosterImageEditorYEnable != null) PosterImageEditorYEnable.IsChecked = false;
            ResetPosterImageEditorAdjustments();
            PosterImageEditorPositionEnable_Changed(PosterImageEditorYEnable, new RoutedEventArgs());
            PosterImageEditorPositionEnable_Changed(PosterImageEditorXEnable, new RoutedEventArgs());
            PosterImageEditorSourceName.Text = Path.GetFileName(file);

            PosterImageEditorUpscaleCombo.ItemsSource = GetPosterImageEditorUpscaleOptions();
            PosterImageEditorUpscaleCombo.SelectedIndex = 0;
            PopulatePosterImageEditorEffectsCategories();
            BuildPosterImageEditorEffectControls();
            _posterImageEditorUndo.Clear();
            _posterImageEditorRedo.Clear();
            CapturePosterImageEditorHistory(true);
            UpdatePosterImageEditorResolutionOptions();
            PosterImageEditorZoomSlider.Value = 1;
            _posterImageEditorAdjustedSource = bitmap;
            PosterImageEditorImage.Source = bitmap;
            _mode = "poster_image_editor";
            UpdateMode();
            UpdatePosterImageEditorTransform();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Could not open the image editor.\n\n{0}", ex.Message), L("Image Editor"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private IEnumerable<string> GetPosterImageEditorUpscaleOptions()
    {
        var options = new List<string> { "Off", "Real-ESRGAN 2×", "Real-ESRGAN 4×" };
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Retro Rewind Modhub", "AI Models");
            if (Directory.Exists(folder))
            {
                foreach (var param in Directory.EnumerateFiles(folder, "*.param", SearchOption.TopDirectoryOnly))
                {
                    var baseName = Path.GetFileNameWithoutExtension(param);
                    if (File.Exists(Path.Combine(folder, baseName + ".bin"))) options.Add("Custom: " + baseName);
                }
            }
        }
        catch { }
        return options.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void PopulatePosterImageEditorEffectsCategories()
    {
        if (PosterImageEditorEffectsCategoryCombo == null) return;
        PosterImageEditorEffectsCategoryCombo.ItemsSource = PosterImageEditorEffectCategories.Keys.ToList();
        PosterImageEditorEffectsCategoryCombo.SelectedItem = _posterImageEditorEffectsCategory;
    }

    private static string EffectDescription(string effect) => effect switch
    {
        "Clarity" => "Enhances local contrast and definition.", "Denoise" => "Reduces small-scale image noise.", "Blur" => "Softens fine image detail.", "Vignette" => "Darkens the edges for a classic look.", "Grain" => "Adds subtle film-like grain.", "Fade" => "Fades the image toward a washed print.", "Softness" => "Adds gentle overall softness.",
        "Scanlines" => "Adds horizontal CRT scan lines.", "Phosphor" => "Simulates subtle CRT phosphor response.", "CRT Glow" => "Adds a soft glow to bright pixels.", "Screen Curvature" => "Adds a subtle curved-screen impression.", "Chromatic Aberration" => "Offsets color channels slightly.", "Flicker" => "Adds a restrained analogue flicker.", "Interlacing" => "Alternates brightness like interlaced video.", "CRT Noise" => "Adds light CRT signal noise.",
        "Pixelation" => "Reduces the image into larger pixel blocks.", "Palette Reduction" => "Limits the number of displayed colors.", "Color Banding" => "Creates stepped retro color levels.", "Dithering" => "Adds a patterned low-color dither.", "Pixel Grid" => "Overlays a subtle pixel grid.", "Color Bleed" => "Softens colors into neighboring pixels.",
        "VHS Noise" => "Adds soft analogue tape noise.", "Tracking Distortion" => "Adds subtle horizontal tracking errors.", "Horizontal Tearing" => "Creates small analogue horizontal shifts.", "Tape Grain" => "Adds VHS-style tape texture.", "Chromatic Offset" => "Separates color channels like tape playback.", "Scanline Jitter" => "Adds unstable scanline movement.", "Image Warping" => "Adds subtle analogue warping.",
        "Halftone" => "Simulates printed halftone dots.", "Print Dots" => "Adds a fine print-dot texture.", "Ink Bleed" => "Softens edges like ink on paper.", "Paper Grain" => "Adds subtle paper texture.", "Misregistration" => "Slightly offsets printed color layers.", "Faded Ink" => "Reduces saturation like aged print.", "Poster Wear" => "Adds subtle worn print variation.", "Scratches" => "Adds restrained print scratches.",
        "Film Grain" => "Adds photographic film grain.", "Dust" => "Adds tiny film dust marks.", "Film Scratches" => "Adds faint film scratches.", "Film Flicker" => "Adds old-film brightness variation.", "Film Fade" => "Fades color like aged film.", "Sepia" => "Adds a warm aged-film tone.", "Light Leaks" => "Adds subtle analogue light leaks.", "Gate Weave" => "Adds gentle old-film frame movement.", _ => "Applies a retro visual treatment."
    };

    private void BuildPosterImageEditorEffectControls()
    {
        if (PosterImageEditorEffectsPanel == null) return;
        PosterImageEditorEffectsPanel.Children.Clear();
        if (!PosterImageEditorEffectCategories.TryGetValue(_posterImageEditorEffectsCategory, out var effects)) return;
        foreach (var effect in effects)
        {
            var key = _posterImageEditorEffectsCategory + ":" + effect;
            if (!_posterImageEditorEffectValues.ContainsKey(key)) _posterImageEditorEffectValues[key] = 0;
            if (!_posterImageEditorEffectEnabled.ContainsKey(key)) _posterImageEditorEffectEnabled[key] = false;
            var card = new Border { Padding = new Thickness(12), Margin = new Thickness(0,0,0,8), Background = (Brush)FindResource("SecondaryCardBrush"), BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
            var stack = new StackPanel();
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = effect, FontWeight = FontWeights.SemiBold });
            var check = new CheckBox { IsChecked = _posterImageEditorEffectEnabled[key], Style = (Style)FindResource("VideoEditorToggleStyle"), Tag = key, HorizontalAlignment = HorizontalAlignment.Right };
            check.Checked += PosterImageEditorDynamicEffectChanged; check.Unchecked += PosterImageEditorDynamicEffectChanged; Grid.SetColumn(check,1); header.Children.Add(check);
            stack.Children.Add(header);
            stack.Children.Add(new TextBlock { Text = EffectDescription(effect), Foreground = (Brush)FindResource("SecondaryBrush"), Margin = new Thickness(0,5,0,8), TextWrapping = TextWrapping.Wrap });
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = _posterImageEditorEffectValues[key], Style = (Style)FindResource("VideoEditorSliderStyle"), Tag = key };
            slider.ValueChanged += PosterImageEditorDynamicEffectChanged;
            stack.Children.Add(slider); card.Child = stack; PosterImageEditorEffectsPanel.Children.Add(card);
        }
    }

    private void PosterImageEditorEffectsCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PosterImageEditorEffectsCategoryCombo?.SelectedItem is string value) { _posterImageEditorEffectsCategory = value; BuildPosterImageEditorEffectControls(); CapturePosterImageEditorHistory(); ApplyPosterImageEditorPreview(); }
    }

    private void PosterImageEditorDynamicEffectChanged(object sender, RoutedEventArgs e)
    {
        if (_posterImageEditorUpdatingControls) return;
        var key = (sender as FrameworkElement)?.Tag?.ToString(); if (string.IsNullOrWhiteSpace(key)) return;
        if (sender is CheckBox cb) _posterImageEditorEffectEnabled[key] = cb.IsChecked == true;
        if (sender is Slider sl) _posterImageEditorEffectValues[key] = sl.Value;
        CapturePosterImageEditorHistory(); ApplyPosterImageEditorPreview();
    }

    private void ApplyPosterImageEditorPreview()
    {
        if (_posterImageEditorSource == null || PosterImageEditorImage == null) return;
        _posterImageEditorAdjustedSource = ApplyPosterImageEditorAdjustments(_posterImageEditorSource);
        PosterImageEditorImage.Source = _posterImageEditorAdjustedSource;
        UpdatePosterImageEditorTransform();
    }

    private string PosterImageEditorProfilesDirectory()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Retro Rewind Modhub", "Profiles"); Directory.CreateDirectory(folder); return folder;
    }

    private object CapturePosterImageEditorState()
    {
        return new { X = _posterImageEditorOffset.X, Y = _posterImageEditorOffset.Y, Zoom = PosterImageEditorZoomSlider?.Value ?? 1, Rotation = _posterImageEditorRotation, FlipH = _posterImageEditorFlipHorizontal, FlipV = _posterImageEditorFlipVertical, XEnable = PosterImageEditorXEnable?.IsChecked != false, YEnable = PosterImageEditorYEnable?.IsChecked == true, Resolution = PosterImageEditorResolutionCombo?.SelectedItem?.ToString(), Upscale = PosterImageEditorUpscaleCombo?.SelectedItem?.ToString(), Category = _posterImageEditorEffectsCategory, Effects = _posterImageEditorEffectValues, Enabled = _posterImageEditorEffectEnabled, Brightness = PosterImageEditorBrightnessSlider?.Value ?? 0, Contrast = PosterImageEditorContrastSlider?.Value ?? 0, Saturation = PosterImageEditorSaturationSlider?.Value ?? 0, Gamma = PosterImageEditorGammaSlider?.Value ?? 1, Hue = PosterImageEditorHueSlider?.Value ?? 0, Temperature = PosterImageEditorTemperatureSlider?.Value ?? 0, Tint = PosterImageEditorTintSlider?.Value ?? 0, Sharpness = PosterImageEditorSharpnessSlider?.Value ?? 0 };
    }

    private void CapturePosterImageEditorHistory(bool force = false)
    {
        if (_posterImageEditorHistoryApplying || _posterImageEditorSource == null) return;
        if (!force && (DateTime.UtcNow - _posterImageEditorLastHistoryCapture).TotalMilliseconds < 150) return;
        _posterImageEditorLastHistoryCapture = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(CapturePosterImageEditorState());
        if (_posterImageEditorUndo.Count == 0 || _posterImageEditorUndo.Peek() != json) _posterImageEditorUndo.Push(json);
        _posterImageEditorRedo.Clear(); while (_posterImageEditorUndo.Count > 50) { var arr = _posterImageEditorUndo.ToArray(); _posterImageEditorUndo.Clear(); foreach (var x in arr.Take(50).Reverse()) _posterImageEditorUndo.Push(x); }
    }

    private void ApplyPosterImageEditorState(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var r = doc.RootElement;
            _posterImageEditorUpdatingControls = true;
            if (r.TryGetProperty("X", out var x)) _posterImageEditorOffset.X = x.GetDouble();
            if (r.TryGetProperty("Y", out var y)) _posterImageEditorOffset.Y = y.GetDouble();
            if (PosterImageEditorZoomSlider != null && r.TryGetProperty("Zoom", out var z)) PosterImageEditorZoomSlider.Value = z.GetDouble();
            _posterImageEditorRotation = r.TryGetProperty("Rotation", out var rot) ? rot.GetInt32() : 0;
            _posterImageEditorFlipHorizontal = r.TryGetProperty("FlipH", out var fh) && fh.GetBoolean(); _posterImageEditorFlipVertical = r.TryGetProperty("FlipV", out var fv) && fv.GetBoolean();
            SetTextBoxSilently(PosterImageEditorXTextBox, _posterImageEditorOffset.X.ToString("0.##", CultureInfo.InvariantCulture)); SetTextBoxSilently(PosterImageEditorYTextBox, _posterImageEditorOffset.Y.ToString("0.##", CultureInfo.InvariantCulture));
            SetCheck(PosterImageEditorFlipHorizontalEnable,_posterImageEditorFlipHorizontal); SetCheck(PosterImageEditorFlipVerticalEnable,_posterImageEditorFlipVertical); SetCheck(PosterImageEditorXEnable, !r.TryGetProperty("XEnable", out var xe) || xe.GetBoolean()); SetCheck(PosterImageEditorYEnable, r.TryGetProperty("YEnable", out var ye) && ye.GetBoolean());
            SetSlider(PosterImageEditorBrightnessSlider,r,"Brightness"); SetSlider(PosterImageEditorContrastSlider,r,"Contrast"); SetSlider(PosterImageEditorSaturationSlider,r,"Saturation"); SetSlider(PosterImageEditorGammaSlider,r,"Gamma"); SetSlider(PosterImageEditorHueSlider,r,"Hue"); SetSlider(PosterImageEditorTemperatureSlider,r,"Temperature"); SetSlider(PosterImageEditorTintSlider,r,"Tint"); SetSlider(PosterImageEditorSharpnessSlider,r,"Sharpness");
            if (r.TryGetProperty("Resolution", out var res) && PosterImageEditorResolutionCombo != null) { var savedResolution = res.GetString(); var targetResolution = savedResolution?.Split(" (", 2)[0].Replace("×", "x").Replace(" ", "", StringComparison.Ordinal); PosterImageEditorResolutionCombo.SelectedItem = PosterImageEditorResolutionCombo.Items.Cast<object>().FirstOrDefault(item => { var text = item?.ToString(); if (string.IsNullOrEmpty(text)) return false; var normalized = text.Split(" (", 2)[0].Replace("×", "x").Replace(" ", "", StringComparison.Ordinal); return normalized.Equals(targetResolution, StringComparison.OrdinalIgnoreCase); }); }
            if (r.TryGetProperty("Upscale", out var up) && PosterImageEditorUpscaleCombo != null) PosterImageEditorUpscaleCombo.SelectedItem = up.GetString();
            if (r.TryGetProperty("Category", out var cat)) { _posterImageEditorEffectsCategory = cat.GetString() ?? "Basic"; PosterImageEditorEffectsCategoryCombo.SelectedItem = _posterImageEditorEffectsCategory; }
            if (r.TryGetProperty("Effects", out var effects)) foreach (var prop in effects.EnumerateObject()) _posterImageEditorEffectValues[prop.Name] = prop.Value.GetDouble();
            if (r.TryGetProperty("Enabled", out var enabled)) foreach (var prop in enabled.EnumerateObject()) _posterImageEditorEffectEnabled[prop.Name] = prop.Value.GetBoolean();
        }
        finally { _posterImageEditorUpdatingControls = false; BuildPosterImageEditorEffectControls(); ApplyPosterImageEditorPreview(); }
    }

    private static void SetTextBoxSilently(TextBox? box, string value) { if (box != null) box.Text = value; }
    private static void SetCheck(CheckBox? box, bool value) { if (box != null) box.IsChecked = value; }
    private static void SetSlider(Slider? slider, JsonElement root, string prop) { if (slider != null && root.TryGetProperty(prop, out var p)) slider.Value = p.GetDouble(); }

    private async void PosterImageEditorProfilesButton_Click(object sender, RoutedEventArgs e)
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

        var list = new StackPanel();

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
        dialog.ShowDialog();
    }


    private bool IsPosterImageAiUpscaleEnabled()
        => (PosterImageEditorUpscaleCombo?.SelectedItem?.ToString() ?? "Off") != "Off";

    private void UpdatePosterImageEditorResolutionOptions()
    {
        if (PosterImageEditorResolutionCombo == null) return;
        var options = new List<string> { "1024x2048 (Recommended)" };
        if (IsPosterImageAiUpscaleEnabled() || _posterImageEditorOriginalHeight >= 4096)
        {
            options.Add("2048x4096");
            if (IsPosterImageAiUpscaleEnabled()) options.Add("4096x8192 (Not Recommended)");
        }

        var previous = PosterImageEditorResolutionCombo.SelectedItem?.ToString();
        PosterImageEditorResolutionCombo.ItemsSource = options;
        PosterImageEditorResolutionCombo.SelectedItem = options.Contains(previous ?? "") ? previous : options[0];
    }

    private string BuildPosterEsrganArguments(string input, string output, int scale, string? selectedModel, string modelDirectory)
    {
        var modelName = "realesrgan-x4plus-anime";
        var args = $"-i {QuoteProcessArgument(input)} -o {QuoteProcessArgument(output)} -m {QuoteProcessArgument(modelDirectory)} -n {QuoteProcessArgument(modelName)} -s {scale}";
        if (!string.IsNullOrWhiteSpace(selectedModel) && selectedModel.StartsWith("Custom: ", StringComparison.OrdinalIgnoreCase))
        {
            var name = selectedModel.Substring(8).Trim();
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Retro Rewind Modhub", "AI Models");
            if (File.Exists(Path.Combine(folder, name + ".param")) && File.Exists(Path.Combine(folder, name + ".bin")))
                args = $"-i {QuoteProcessArgument(input)} -o {QuoteProcessArgument(output)} -m {QuoteProcessArgument(folder)} -n {QuoteProcessArgument(name)} -s {scale}";
        }
        return args;
    }

    private async Task<BitmapSource> PreparePosterEditorSourceAsync(BitmapSource source, int upscaleFactor, string? selectedModel = null)
    {
        if (upscaleFactor <= 1) return source;

        var realEsrgan = await EnsureRealEsrganAsync();
        var tempDir = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "RealESRGAN");
        Directory.CreateDirectory(tempDir);
        var id = Guid.NewGuid().ToString("N");
        var input = Path.Combine(tempDir, id + ".png");
        var output = Path.Combine(tempDir, id + "_out.png");
        var modelDirectory = Path.Combine(Path.GetDirectoryName(realEsrgan)!, "models");

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var fs = File.Create(input)) encoder.Save(fs);

            // The portable NCNN build has a known corruption/mis-stitching problem
            // when realesrgan-x4plus is asked to run at -s 2. For a real 2x result,
            // always run the model at its native 4x scale and downsample the clean
            // result to 2x inside WPF. This avoids the tiled-square output shown by
            // the broken 2x path while retaining the AI pass.
            var isCustomModel = !string.IsNullOrWhiteSpace(selectedModel) && selectedModel.StartsWith("Custom: ", StringComparison.OrdinalIgnoreCase);
            var nativeScale = (!isCustomModel && upscaleFactor == 2) ? 4 : upscaleFactor;
            var psi = new ProcessStartInfo
            {
                FileName = realEsrgan,
                Arguments = BuildPosterEsrganArguments(input, output, nativeScale, selectedModel, modelDirectory),
                WorkingDirectory = Path.GetDirectoryName(realEsrgan),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Real-ESRGAN.");
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stderr = await stderrTask;
            var stdout = await stdoutTask;
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);

            var result = LoadBitmapFromFile(output);
            var expectedNativeWidth = source.PixelWidth * nativeScale;
            var expectedNativeHeight = source.PixelHeight * nativeScale;
            if (result.PixelWidth != expectedNativeWidth || result.PixelHeight != expectedNativeHeight)
                throw new InvalidOperationException(
                    $"Real-ESRGAN returned {result.PixelWidth}×{result.PixelHeight}, expected {expectedNativeWidth}×{expectedNativeHeight}.");

            if (upscaleFactor == 2)
            {
                var targetWidth = Math.Max(1, source.PixelWidth * 2);
                var targetHeight = Math.Max(1, source.PixelHeight * 2);
                var resized = new TransformedBitmap(result, new ScaleTransform(
                    targetWidth / (double)result.PixelWidth,
                    targetHeight / (double)result.PixelHeight));
                resized.Freeze();

                var render = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                using (var dc = visual.RenderOpen())
                    dc.DrawImage(resized, new Rect(0, 0, targetWidth, targetHeight));
                render.Render(visual);
                render.Freeze();
                return render;
            }

            return result;
        }
        finally
        {
            TryDeleteFile(input);
            TryDeleteFile(output);
        }
    }

    // Geometry from the supplied poster-compatible 1024x2048 texture.
    // The white/custom-image area occupies X=22..1001 and Y=21..1685.
    // The remaining pixels are unused space and stay black in the final poster.
    private const double PosterTemplateWidth = 1024.0;
    private const double PosterTemplateHeight = 2048.0;
    // The supplied poster template defines the usable image rectangle. Store the
    // geometry once at the template resolution and scale it proportionally for
    // every supported output resolution instead of maintaining separate pixel
    // coordinates for 2048x4096 and 4096x8192.
    private const double PosterContentX = 22.0;
    private const double PosterContentY = 21.0;
    private const double PosterContentWidth = 980.0;
    private const double PosterContentHeight = 1665.0;

    private readonly record struct PosterTemplateGeometry(
        double CanvasWidth, double CanvasHeight,
        double ContentX, double ContentY,
        double ContentWidth, double ContentHeight);

    private static PosterTemplateGeometry GetPosterTemplateGeometry(int width, int height)
    {
        var scaleX = width / PosterTemplateWidth;
        var scaleY = height / PosterTemplateHeight;
        return new PosterTemplateGeometry(
            width, height,
            PosterContentX * scaleX,
            PosterContentY * scaleY,
            PosterContentWidth * scaleX,
            PosterContentHeight * scaleY);
    }

    private void UpdatePosterImageEditorTransform()
    {
        if (PosterImageEditorImage == null || _posterImageEditorSource == null ||
            PosterImageEditorCanvas == null || PosterImageEditorClipViewport == null ||
            PosterImageEditorImageCanvas == null) return;

        var stageWidth = PosterImageEditorCanvas.ActualWidth > 1 ? PosterImageEditorCanvas.ActualWidth : 680.0;
        var stageHeight = PosterImageEditorCanvas.ActualHeight > 1 ? PosterImageEditorCanvas.ActualHeight : 820.0;

        // The editor displays ONLY the usable image area from the real poster template.
        // The unused black portion is deliberately omitted from the editor UI.
        var contentScale = Math.Min(stageWidth / PosterContentWidth, stageHeight / PosterContentHeight);
        if (contentScale <= 0) return;

        var frameWidth = PosterContentWidth * contentScale;
        var frameHeight = PosterContentHeight * contentScale;
        var frameLeft = (stageWidth - frameWidth) / 2.0;
        var frameTop = (stageHeight - frameHeight) / 2.0;

        var transformSource = _posterImageEditorAdjustedSource ?? _posterImageEditorSource;
        var sw = Math.Max(1, transformSource.PixelWidth);
        var sh = Math.Max(1, transformSource.PixelHeight);
        var zoom = Math.Max(1.0, PosterImageEditorZoomSlider.Value);

        // Fit the complete source image to cover the usable frame. It is never cropped
        // when opened; the clipping viewport merely hides pixels outside the usable area.
        var fitScale = Math.Max(frameWidth / sw, frameHeight / sh);
        var displayScale = fitScale * zoom;
        var displayWidth = sw * displayScale;
        var displayHeight = sh * displayScale;
        var imageLeft = frameLeft + (frameWidth - displayWidth) / 2.0 + _posterImageEditorOffset.X;
        var imageTop = frameTop + (frameHeight - displayHeight) / 2.0 + _posterImageEditorOffset.Y;

        // No full-template/black background is rendered in the editor.
        PosterImageEditorTemplateBackground.Visibility = Visibility.Collapsed;

        PosterImageEditorClipViewport.Width = frameWidth;
        PosterImageEditorClipViewport.Height = frameHeight;
        PosterImageEditorClipViewport.HorizontalAlignment = HorizontalAlignment.Left;
        PosterImageEditorClipViewport.VerticalAlignment = VerticalAlignment.Top;
        PosterImageEditorClipViewport.Margin = new Thickness(frameLeft, frameTop, 0, 0);

        PosterImageEditorImageCanvas.Width = frameWidth;
        PosterImageEditorImageCanvas.Height = frameHeight;
        PosterImageEditorImage.Width = displayWidth;
        PosterImageEditorImage.Height = displayHeight;
        PosterImageEditorImage.Margin = new Thickness(0);
        PosterImageEditorImage.RenderTransform = null;
        Canvas.SetLeft(PosterImageEditorImage, imageLeft - frameLeft);
        Canvas.SetTop(PosterImageEditorImage, imageTop - frameTop);

        PosterImageEditorFrame.Width = frameWidth;
        PosterImageEditorFrame.Height = frameHeight;
        PosterImageEditorFrame.HorizontalAlignment = HorizontalAlignment.Left;
        PosterImageEditorFrame.VerticalAlignment = VerticalAlignment.Top;
        PosterImageEditorFrame.Margin = new Thickness(frameLeft, frameTop, 0, 0);
        PosterImageEditorFrameLabel.Margin = new Thickness(frameLeft + 6, frameTop + 8, 0, 0);
    }

    private void PosterImageEditorCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_posterImageEditorSource != null)
            UpdatePosterImageEditorTransform();
    }

    private void PosterImageEditorImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _posterImageEditorDragging = true;
        _posterImageEditorDragStart = e.GetPosition(PosterImageEditorCanvas);
        _posterImageEditorStartOffset = _posterImageEditorOffset;
        PosterImageEditorImage.CaptureMouse();
        e.Handled = true;
    }

    private void PosterImageEditorImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_posterImageEditorDragging) return;
        var p = e.GetPosition(PosterImageEditorCanvas);
        var d = p - _posterImageEditorDragStart;
        var allowX = PosterImageEditorXEnable?.IsChecked != false;
        var allowY = PosterImageEditorYEnable?.IsChecked != false;
        _posterImageEditorOffset = new Point(allowX ? _posterImageEditorStartOffset.X + d.X : _posterImageEditorStartOffset.X, allowY ? _posterImageEditorStartOffset.Y + d.Y : _posterImageEditorStartOffset.Y);
        CapturePosterImageEditorHistory();
        _posterImageEditorUpdatingControls = true;
        try { PosterImageEditorXTextBox.Text = _posterImageEditorOffset.X.ToString("0"); PosterImageEditorYTextBox.Text = _posterImageEditorOffset.Y.ToString("0"); } finally { _posterImageEditorUpdatingControls = false; }
        UpdatePosterImageEditorTransform();
    }

    private void PosterImageEditorImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _posterImageEditorDragging = false;
        if (PosterImageEditorImage.IsMouseCaptured) PosterImageEditorImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ResetPosterImageEditorAdjustments()
    {
        _posterImageEditorUpdatingControls = true;
        try
        {
            PosterImageEditorBrightnessSlider.Value = 0;
            PosterImageEditorContrastSlider.Value = 0;
            PosterImageEditorSaturationSlider.Value = 0;
            PosterImageEditorGammaSlider.Value = 1;
            PosterImageEditorHueSlider.Value = 0;
            PosterImageEditorTemperatureSlider.Value = 0;
            PosterImageEditorTintSlider.Value = 0;
            PosterImageEditorSharpnessSlider.Value = 0;
            if (PosterImageEditorBrightnessEnable != null) PosterImageEditorBrightnessEnable.IsChecked = true;
            if (PosterImageEditorContrastEnable != null) PosterImageEditorContrastEnable.IsChecked = true;
            if (PosterImageEditorSaturationEnable != null) PosterImageEditorSaturationEnable.IsChecked = true;
            if (PosterImageEditorGammaEnable != null) PosterImageEditorGammaEnable.IsChecked = true;
            if (PosterImageEditorHueEnable != null) PosterImageEditorHueEnable.IsChecked = true;
            if (PosterImageEditorTemperatureEnable != null) PosterImageEditorTemperatureEnable.IsChecked = true;
            if (PosterImageEditorTintEnable != null) PosterImageEditorTintEnable.IsChecked = true;
            if (PosterImageEditorSharpnessEnable != null) PosterImageEditorSharpnessEnable.IsChecked = true;
            _posterImageEditorFlipHorizontal = false;
            _posterImageEditorFlipVertical = false;
            _posterImageEditorRotation = 0;
            PosterImageEditorXTextBox.Text = "0";
            PosterImageEditorYTextBox.Text = "0";
        }
        finally { _posterImageEditorUpdatingControls = false; }
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);

    private static void RgbToHsv(double r, double g, double b, out double h, out double s, out double v)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 1e-9) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * (((b - r) / d) + 2);
        else h = 60 * (((r - g) / d) + 4);
        if (h < 0) h += 360;
    }

    private static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        var m = v - c;
        if (h < 60) (r,g,b) = (c,x,0);
        else if (h < 120) (r,g,b) = (x,c,0);
        else if (h < 180) (r,g,b) = (0,c,x);
        else if (h < 240) (r,g,b) = (0,x,c);
        else if (h < 300) (r,g,b) = (x,0,c);
        else (r,g,b) = (c,0,x);
        r += m; g += m; b += m;
    }

    private BitmapSource ApplyPosterImageEditorEffects(BitmapSource source)
    {
        if (!_posterImageEditorFlipHorizontal && !_posterImageEditorFlipVertical && _posterImageEditorRotation == 0)
            return source;

        var group = new TransformGroup();
        if (_posterImageEditorRotation != 0)
            group.Children.Add(new RotateTransform(_posterImageEditorRotation));
        if (_posterImageEditorFlipHorizontal || _posterImageEditorFlipVertical)
        {
            group.Children.Add(new ScaleTransform(_posterImageEditorFlipHorizontal ? -1 : 1, _posterImageEditorFlipVertical ? -1 : 1, 0.5, 0.5));
        }
        var transformed = new TransformedBitmap(source, group);
        transformed.Freeze();
        return transformed;
    }

    private void PosterImageEditorPositionEnable_Changed(object sender, RoutedEventArgs e)
    {
        if (_posterImageEditorUpdatingControls) return;
        var enabled = sender is CheckBox cb && cb.IsChecked == true;
                if (sender == PosterImageEditorXEnable && PosterImageEditorXTextBox != null) PosterImageEditorXTextBox.IsEnabled = enabled;
        if (sender == PosterImageEditorYEnable && PosterImageEditorYTextBox != null) PosterImageEditorYTextBox.IsEnabled = enabled;
        UpdatePosterImageEditorTransform();
    }

    private void PosterImageEditorEffectEnable_Changed(object sender, RoutedEventArgs e)
    {
        if (_posterImageEditorUpdatingControls || _posterImageEditorSource == null) return;
        _posterImageEditorFlipHorizontal = PosterImageEditorFlipHorizontalEnable?.IsChecked == true;
        _posterImageEditorFlipVertical = PosterImageEditorFlipVerticalEnable?.IsChecked == true;
        _posterImageEditorAdjustedSource = ApplyPosterImageEditorAdjustments(_posterImageEditorSource);
        if (PosterImageEditorImage != null) PosterImageEditorImage.Source = _posterImageEditorAdjustedSource;
        UpdatePosterImageEditorTransform();
    }

    private void PosterImageEditorRotateLeft_Click(object sender, RoutedEventArgs e)
    {
        if (_posterImageEditorSource == null) return;
        _posterImageEditorRotation = (_posterImageEditorRotation + 270) % 360;
        _posterImageEditorAdjustedSource = ApplyPosterImageEditorAdjustments(_posterImageEditorSource);
        PosterImageEditorImage.Source = _posterImageEditorAdjustedSource;
        UpdatePosterImageEditorTransform();
    }

    private void PosterImageEditorRotateRight_Click(object sender, RoutedEventArgs e)
    {
        if (_posterImageEditorSource == null) return;
        _posterImageEditorRotation = (_posterImageEditorRotation + 90) % 360;
        _posterImageEditorAdjustedSource = ApplyPosterImageEditorAdjustments(_posterImageEditorSource);
        PosterImageEditorImage.Source = _posterImageEditorAdjustedSource;
        UpdatePosterImageEditorTransform();
    }

    private void PosterImageEditorEffectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || string.IsNullOrWhiteSpace(button.Tag?.ToString())) return;
        switch (button.Tag.ToString())
        {
            case "flipH": _posterImageEditorFlipHorizontal = !_posterImageEditorFlipHorizontal; break;
            case "flipV": _posterImageEditorFlipVertical = !_posterImageEditorFlipVertical; break;
            case "rotateL": _posterImageEditorRotation = (_posterImageEditorRotation + 270) % 360; break;
            case "rotateR": _posterImageEditorRotation = (_posterImageEditorRotation + 90) % 360; break;
        }
        PosterImageEditorAdjustment_ValueChanged(sender, new RoutedPropertyChangedEventArgs<double>(0, 0));
    }

    private void PosterImageEditorColorEnable_Changed(object sender, RoutedEventArgs e)
    {
        if (_posterImageEditorUpdatingControls) return;
        if (sender is CheckBox cb)
        {
            var name = cb.Name ?? string.Empty;
            var slider = name switch
            {
                "PosterImageEditorBrightnessEnable" => PosterImageEditorBrightnessSlider,
                "PosterImageEditorContrastEnable" => PosterImageEditorContrastSlider,
                "PosterImageEditorSaturationEnable" => PosterImageEditorSaturationSlider,
                "PosterImageEditorGammaEnable" => PosterImageEditorGammaSlider,
                "PosterImageEditorHueEnable" => PosterImageEditorHueSlider,
                "PosterImageEditorTemperatureEnable" => PosterImageEditorTemperatureSlider,
                "PosterImageEditorTintEnable" => PosterImageEditorTintSlider,
                "PosterImageEditorSharpnessEnable" => PosterImageEditorSharpnessSlider,
                _ => null
            };
            if (slider != null) slider.IsEnabled = cb.IsChecked == true;
        }
        PosterImageEditorAdjustment_ValueChanged(sender, new RoutedPropertyChangedEventArgs<double>(0, 0));
    }

    private void ApplyPosterImageEditorDynamicEffects(byte[] pixels, int w, int h)
    {
        foreach (var pair in _posterImageEditorEffectEnabled.ToArray())
        {
            if (!pair.Value) continue;
            var parts = pair.Key.Split(':', 2); if (parts.Length != 2) continue;
            var effect = parts[1]; var amount = Math.Clamp(_posterImageEditorEffectValues.TryGetValue(pair.Key, out var v) ? v / 100.0 : 0, 0, 1);
            if (amount <= 0.001) continue;
            if (effect is "Grain" or "Film Grain" or "Tape Grain" or "CRT Noise" or "VHS Noise" or "Paper Grain")
            { var rng = new Random(1337); for (int y=0;y<h;y++) for(int x=0;x<w;x++){ int i=(y*w+x)*4; int n=(int)Math.Round((rng.NextDouble()-.5)*55*amount); pixels[i]=(byte)Math.Clamp(pixels[i]+n,0,255); pixels[i+1]=(byte)Math.Clamp(pixels[i+1]+n,0,255); pixels[i+2]=(byte)Math.Clamp(pixels[i+2]+n,0,255);} }
            else if (effect is "Vignette")
            { for(int y=0;y<h;y++) for(int x=0;x<w;x++){ double dx=(x-w/2.0)/(w/2.0), dy=(y-h/2.0)/(h/2.0); double f=1-Math.Clamp((Math.Sqrt(dx*dx+dy*dy)-.35)*amount*1.5,0,.8); int i=(y*w+x)*4; pixels[i]=(byte)(pixels[i]*f); pixels[i+1]=(byte)(pixels[i+1]*f); pixels[i+2]=(byte)(pixels[i+2]*f);} }
            else if (effect is "Fade" or "Faded Ink" or "Film Fade")
            { for(int i=0;i<pixels.Length;i+=4){ pixels[i]=(byte)(pixels[i]*(.75+.25*(1-amount))); pixels[i+1]=(byte)(pixels[i+1]*(.75+.25*(1-amount))); pixels[i+2]=(byte)(pixels[i+2]*(.75+.25*(1-amount))); pixels[i]=(byte)Math.Clamp(pixels[i]+20*amount,0,255); pixels[i+1]=(byte)Math.Clamp(pixels[i+1]+18*amount,0,255); pixels[i+2]=(byte)Math.Clamp(pixels[i+2]+15*amount,0,255);} }
            else if (effect is "Sepia")
            { for(int i=0;i<pixels.Length;i+=4){ double r=pixels[i+2],g=pixels[i+1],b=pixels[i]; double nr=.393*r+.769*g+.189*b, ng=.349*r+.686*g+.168*b, nb=.272*r+.534*g+.131*b; pixels[i+2]=(byte)Math.Clamp(r+(nr-r)*amount,0,255); pixels[i+1]=(byte)Math.Clamp(g+(ng-g)*amount,0,255); pixels[i]=(byte)Math.Clamp(b+(nb-b)*amount,0,255);} }
            else if (effect is "Palette Reduction" or "Color Banding")
            { int levels=Math.Max(2,(int)Math.Round(16-14*amount)); for(int i=0;i<pixels.Length;i+=4) for(int c=0;c<3;c++){ var q=(int)Math.Round(pixels[i+c]/255.0*(levels-1)); pixels[i+c]=(byte)Math.Round(q*255.0/(levels-1)); } }
            else if (effect is "Scanlines" or "Interlacing")
            { for(int y=0;y<h;y+=2) for(int x=0;x<w;x++){ int i=(y*w+x)*4; double f=1-.35*amount; pixels[i]=(byte)(pixels[i]*f); pixels[i+1]=(byte)(pixels[i+1]*f); pixels[i+2]=(byte)(pixels[i+2]*f);} }
            else if (effect is "Pixelation" or "Pixel Grid")
            { int block=Math.Max(2,2+(int)Math.Round(10*amount)); for(int y=0;y<h;y+=block) for(int x=0;x<w;x+=block){ int sx=Math.Min(x,w-1), sy=Math.Min(y,h-1), si=(sy*w+sx)*4; for(int yy=y;yy<Math.Min(y+block,h);yy++) for(int xx=x;xx<Math.Min(x+block,w);xx++){ int i=(yy*w+xx)*4; pixels[i]=pixels[si]; pixels[i+1]=pixels[si+1]; pixels[i+2]=pixels[si+2]; }} }
            else if (effect is "Softness" or "Blur" or "Ink Bleed" or "Color Bleed")
            { var copy=(byte[])pixels.Clone(); int radius=Math.Max(1,(int)Math.Round(1+amount*2)); for(int y=radius;y<h-radius;y++) for(int x=radius;x<w-radius;x++){ int i=(y*w+x)*4; for(int c=0;c<3;c++){ int sum=0,count=0; for(int yy=y-radius;yy<=y+radius;yy++) for(int xx=x-radius;xx<=x+radius;xx++){sum+=copy[(yy*w+xx)*4+c];count++;} pixels[i+c]=(byte)Math.Round(sum/(double)count); } } }
            else if (effect is "Clarity")
            { for(int i=0;i<pixels.Length;i+=4){ for(int c=0;c<3;c++){ double n=pixels[i+c]/255.0; n=(n-.5)*(1+.8*amount)+.5; pixels[i+c]=(byte)Math.Clamp(Math.Round(n*255),0,255); } } }
            else if (effect is "Dithering" or "Halftone" or "Print Dots" or "Pixel Grid")
            { int step=Math.Max(2,3+(int)Math.Round(6*amount)); for(int y=0;y<h;y+=step) for(int x=0;x<w;x+=step){ int i=(y*w+x)*4; double lum=(pixels[i]+pixels[i+1]+pixels[i+2])/765.0; if(lum<.5) { pixels[i]=pixels[i+1]=pixels[i+2]=(byte)(pixels[i+2]*.7); } } }
            else if (effect is "Chromatic Aberration" or "Chromatic Offset" or "Misregistration")
            { var copy=(byte[])pixels.Clone(); int shift=Math.Max(1,(int)Math.Round(1+amount*4)); for(int y=0;y<h;y++) for(int x=shift;x<w-shift;x++){int i=(y*w+x)*4; pixels[i+2]=copy[(y*w+x-shift)*4+2]; pixels[i]=copy[(y*w+x+shift)*4];} }
        }
    }

    private BitmapSource ApplyPosterImageEditorAdjustments(BitmapSource source)
    {
        source = ApplyPosterImageEditorEffects(source);
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        int w = converted.PixelWidth, h = converted.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);

        double brightness = PosterImageEditorBrightnessEnable?.IsChecked == true ? (PosterImageEditorBrightnessSlider?.Value ?? 0) : 0;
        double contrast = PosterImageEditorContrastEnable?.IsChecked == true ? (PosterImageEditorContrastSlider?.Value ?? 0) : 0;
        double saturation = PosterImageEditorSaturationEnable?.IsChecked == true ? (PosterImageEditorSaturationSlider?.Value ?? 0) : 0;
        double gamma = PosterImageEditorGammaEnable?.IsChecked == true ? Math.Max(0.1, PosterImageEditorGammaSlider?.Value ?? 1) : 1;
        double hue = PosterImageEditorHueEnable?.IsChecked == true ? (PosterImageEditorHueSlider?.Value ?? 0) : 0;
        double temp = PosterImageEditorTemperatureEnable?.IsChecked == true ? (PosterImageEditorTemperatureSlider?.Value ?? 0) : 0;
        double tint = PosterImageEditorTintEnable?.IsChecked == true ? (PosterImageEditorTintSlider?.Value ?? 0) : 0;
        double sharpness = PosterImageEditorSharpnessEnable?.IsChecked == true ? Math.Max(0, PosterImageEditorSharpnessSlider?.Value ?? 0) / 100.0 : 0;
        double contrastFactor = (100.0 + contrast) / 100.0;
        contrastFactor *= contrastFactor;
        double brightnessOffset = brightness / 100.0;
        double satFactor = (100.0 + saturation) / 100.0;
        double tempShift = temp / 100.0 * 0.08;
        double tintShift = tint / 100.0 * 0.06;

        for (int y=0; y<h; y++)
        for (int x=0; x<w; x++)
        {
            int i=(y*w+x)*4;
            double b=pixels[i]/255.0, g=pixels[i+1]/255.0, r=pixels[i+2]/255.0;
            r = (r - .5) * contrastFactor + .5 + brightnessOffset;
            g = (g - .5) * contrastFactor + .5 + brightnessOffset;
            b = (b - .5) * contrastFactor + .5 + brightnessOffset;
            r = Math.Pow(Clamp01(r), 1.0/gamma);
            g = Math.Pow(Clamp01(g), 1.0/gamma);
            b = Math.Pow(Clamp01(b), 1.0/gamma);
            RgbToHsv(r,g,b,out var hh,out var ss,out var vv);
            ss = Clamp01(ss * satFactor);
            HsvToRgb(hh + hue, ss, vv, out r,out g,out b);
            r = Clamp01(r + tempShift + tintShift);
            g = Clamp01(g - tintShift * 0.5);
            b = Clamp01(b - tempShift + tintShift);
            pixels[i]=(byte)Math.Round(b*255); pixels[i+1]=(byte)Math.Round(g*255); pixels[i+2]=(byte)Math.Round(r*255);
        }

        ApplyPosterImageEditorDynamicEffects(pixels, w, h);

        if (sharpness > 0.001 && w > 2 && h > 2)
        {
            var original = (byte[])pixels.Clone();
            for (int y=1; y<h-1; y++)
            for (int x=1; x<w-1; x++)
            {
                int i=(y*w+x)*4;
                for(int c=0;c<3;c++)
                {
                    double center=original[i+c]*5.0;
                    double neigh=original[((y-1)*w+x)*4+c]+original[((y+1)*w+x)*4+c]+original[(y*w+x-1)*4+c]+original[(y*w+x+1)*4+c];
                    pixels[i+c]=(byte)Math.Clamp(Math.Round(original[i+c] + sharpness*(center-neigh)),0,255);
                }
            }
        }

        var wb = new WriteableBitmap(w,h,96,96,PixelFormats.Bgra32,null);
        wb.WritePixels(new Int32Rect(0,0,w,h), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    private void PosterImageEditorAdjustment_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_posterImageEditorUpdatingControls || _posterImageEditorSource == null || PosterImageEditorImage == null) return;
        try
        {
            CapturePosterImageEditorHistory();
            _posterImageEditorAdjustedSource = ApplyPosterImageEditorAdjustments(_posterImageEditorSource);
            PosterImageEditorImage.Source = _posterImageEditorAdjustedSource;
        }
        catch { }
    }

    private void PosterImageEditorPosition_TextChanged(object sender, TextChangedEventArgs e)
    {
        // TextChanged fires while MainWindow XAML is still being constructed.
        // Do not touch editor state until both position controls and the editor
        // surface have been initialized.
        if (_posterImageEditorUpdatingControls ||
            PosterImageEditorXTextBox == null ||
            PosterImageEditorYTextBox == null ||
            PosterImageEditorImage == null ||
            _posterImageEditorSource == null) return;

        if (double.TryParse(PosterImageEditorXTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(PosterImageEditorYTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            var allowX = PosterImageEditorXEnable?.IsChecked != false;
            var allowY = PosterImageEditorYEnable?.IsChecked != false;
            _posterImageEditorOffset = new Point(allowX ? x : _posterImageEditorOffset.X, allowY ? y : _posterImageEditorOffset.Y);
            UpdatePosterImageEditorTransform();
        }
    }

    private void PosterImageEditorCenterButton_Click(object sender, RoutedEventArgs e)
    {
        _posterImageEditorOffset = new Point(0,0);
        _posterImageEditorUpdatingControls = true;
        try { PosterImageEditorXTextBox.Text="0"; PosterImageEditorYTextBox.Text="0"; } finally { _posterImageEditorUpdatingControls=false; }
        UpdatePosterImageEditorTransform();
    }

    private void PosterImageEditorFillButton_Click(object sender, RoutedEventArgs e)
    {
        PosterImageEditorZoomSlider.Value = 1;
        PosterImageEditorCenterButton_Click(sender,e);
    }

    private void PosterImageEditorZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePosterImageEditorTransform();
    }

    private void PosterImageEditorUpscaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdatePosterImageEditorResolutionOptions();
    }

    private void PosterImageEditorResetButton_Click(object sender, RoutedEventArgs e)
    {
        _posterImageEditorOffset = new Point(0, 0);
        _posterImageEditorUpdatingControls = true;
        try { PosterImageEditorXTextBox.Text = "0"; PosterImageEditorYTextBox.Text = "0"; } finally { _posterImageEditorUpdatingControls = false; }
        PosterImageEditorZoomSlider.Value = 1;
        _posterImageEditorUpdatingControls = true;
        try
        {
                        if (PosterImageEditorXEnable != null) PosterImageEditorXEnable.IsChecked = true;
            if (PosterImageEditorYEnable != null) PosterImageEditorYEnable.IsChecked = false;
            if (PosterImageEditorBrightnessEnable != null) PosterImageEditorBrightnessEnable.IsChecked = true;
            if (PosterImageEditorContrastEnable != null) PosterImageEditorContrastEnable.IsChecked = true;
            if (PosterImageEditorSaturationEnable != null) PosterImageEditorSaturationEnable.IsChecked = true;
            if (PosterImageEditorGammaEnable != null) PosterImageEditorGammaEnable.IsChecked = true;
            if (PosterImageEditorHueEnable != null) PosterImageEditorHueEnable.IsChecked = true;
            if (PosterImageEditorTemperatureEnable != null) PosterImageEditorTemperatureEnable.IsChecked = true;
            if (PosterImageEditorTintEnable != null) PosterImageEditorTintEnable.IsChecked = true;
            if (PosterImageEditorSharpnessEnable != null) PosterImageEditorSharpnessEnable.IsChecked = true;
            if (PosterImageEditorFlipHorizontalEnable != null) PosterImageEditorFlipHorizontalEnable.IsChecked = false;
            if (PosterImageEditorFlipVerticalEnable != null) PosterImageEditorFlipVerticalEnable.IsChecked = false;
        }
        finally { _posterImageEditorUpdatingControls = false; }
        _posterImageEditorFlipHorizontal = false;
        _posterImageEditorFlipVertical = false;
        _posterImageEditorRotation = 0;
        _posterImageEditorOffset = new Point(0, 0);
        if (PosterImageEditorFlipHorizontalEnable != null) PosterImageEditorFlipHorizontalEnable.IsChecked = false;
        if (PosterImageEditorFlipVerticalEnable != null) PosterImageEditorFlipVerticalEnable.IsChecked = false;
        _posterImageEditorAdjustedSource = _posterImageEditorSource;
        if (PosterImageEditorImage != null)
            PosterImageEditorImage.Source = _posterImageEditorSource;
        UpdatePosterImageEditorTransform();
    }

    private void PosterImageEditorBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_posterBrowserMod != null) _ = ViewPosterImagesAsync(_posterBrowserMod);
    }

    private static BitmapSource LoadBitmapFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private Task<bool> ShowPosterUpscaleComparisonAsync(BitmapSource original, BitmapSource processed)
    {
        if (PosterUpscaleComparisonGrid == null) return Task.FromResult(true);
        PosterUpscaleComparisonOriginal.Source = original;
        PosterUpscaleComparisonUpscaled.Source = processed;
        PosterUpscaleComparisonGrid.Visibility = Visibility.Visible;
        _posterUpscaleComparisonTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _posterUpscaleComparisonTcs.Task;
    }

    private void PosterUpscaleComparisonContinue_Click(object sender, RoutedEventArgs e)
    {
        PosterUpscaleComparisonGrid.Visibility = Visibility.Collapsed;
        _posterUpscaleComparisonTcs?.TrySetResult(true);
        _posterUpscaleComparisonTcs = null;
    }

    private void PosterUpscaleComparisonUndo_Click(object sender, RoutedEventArgs e)
    {
        PosterUpscaleComparisonGrid.Visibility = Visibility.Collapsed;
        if (_posterImageEditorPreUpscaleSource != null)
        {
            _posterImageEditorAdjustedSource = _posterImageEditorPreUpscaleSource;
            PosterImageEditorImage.Source = _posterImageEditorAdjustedSource;
            UpdatePosterImageEditorTransform();
        }
        _posterUpscaleComparisonTcs?.TrySetResult(false);
        _posterUpscaleComparisonTcs = null;
        _posterImageEditorPreUpscaleSource = null;
    }

    private async void PosterImageEditorSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_posterBrowserMod == null || _posterImageEditorSource == null || string.IsNullOrWhiteSpace(_posterImageEditorSourceFile)) return;
        var selected = PosterImageEditorResolutionCombo.SelectedItem?.ToString() ?? "1024x2048 (Recommended)";
        var normalizedResolution = selected.Split(new[] { " (" }, 2, StringSplitOptions.None)[0].Replace("×", "x").Replace(" ", "", StringComparison.Ordinal);
        var parts = normalizedResolution.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var outW) || !int.TryParse(parts[1], out var outH)) return;

        try
        {
            SetOperationBusy(true, L("Creating poster…"), null, L("Cropping, resizing, compressing to BC7 and generating mipmaps."));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

            var source = _posterImageEditorAdjustedSource ?? _posterImageEditorSource;
            // Preserve the exact original in the canonical Images\{Mod} workspace before any poster write.
            _posterImageEditorOriginalFile = await EnsurePosterOriginalInWorkspaceAsync(_posterImageEditorSourceFile);
            var upscaleText = PosterImageEditorUpscaleCombo?.SelectedItem?.ToString() ?? "Off";
            var upscaleFactor = upscaleText.Contains("4×", StringComparison.Ordinal) ? 4 : (upscaleText.Contains("2×", StringComparison.Ordinal) ? 2 : 1);
            if (upscaleFactor > 1)
            {
                // Keep the edited image as the rollback point. The comparison must be
                // original -> fully processed, never backup -> an unrelated intermediate.
                _posterImageEditorPreUpscaleSource = source;
                var comparisonOriginal = _posterImageEditorSource;
                SetOperationBusy(true, L("Working…"), null, L("Running Real-ESRGAN {0}× AI upscaling…", upscaleFactor));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                source = await PreparePosterEditorSourceAsync(source, upscaleFactor, upscaleText);
                SetOperationBusy(false);
                var accepted = await Dispatcher.InvokeAsync(() => ShowPosterUpscaleComparisonAsync(comparisonOriginal, source), DispatcherPriority.Render);
                var continueWithUpscale = await accepted;
                if (!continueWithUpscale)
                {
                    PosterImageEditorStatus.Text = L("Upscale undone. Your image effects were kept.");
                    return;
                }
                _posterImageEditorAdjustedSource = source;
                PosterImageEditorImage.Source = source;
                UpdatePosterImageEditorTransform();
                _posterImageEditorPreUpscaleSource = null;
                SetOperationBusy(true, L("Creating poster…"), null, L("Applying the selected crop, resolution and BC7 compression."));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            }

            var stageWidth = PosterImageEditorCanvas.ActualWidth > 1 ? PosterImageEditorCanvas.ActualWidth : 680.0;
            var stageHeight = PosterImageEditorCanvas.ActualHeight > 1 ? PosterImageEditorCanvas.ActualHeight : 820.0;

            // Use the same usable-area geometry shown in the editor. The editor omits
            // the unused black template area, but the final poster is still rebuilt as
            // the complete 1024x2048 template before being resized and encoded.
            var contentScale = Math.Min(stageWidth / PosterContentWidth, stageHeight / PosterContentHeight);
            var frameWidth = PosterContentWidth * contentScale;
            var frameHeight = PosterContentHeight * contentScale;
            var frameLeft = (stageWidth - frameWidth) / 2.0;
            var frameTop = (stageHeight - frameHeight) / 2.0;

            var sw = Math.Max(1, source.PixelWidth);
            var sh = Math.Max(1, source.PixelHeight);
            var zoom = Math.Max(1.0, PosterImageEditorZoomSlider.Value);
            var fitScale = Math.Max(frameWidth / sw, frameHeight / sh);
            var displayScale = fitScale * zoom;
            var displayWidth = sw * displayScale;
            var displayHeight = sh * displayScale;
            var imageLeft = frameLeft + (frameWidth - displayWidth) / 2.0 + _posterImageEditorOffset.X;
            var imageTop = frameTop + (frameHeight - displayHeight) / 2.0 + _posterImageEditorOffset.Y;

            // Convert the visible frame rectangle back into source-image pixel coordinates.
            var cropX = (frameLeft - imageLeft) / displayScale;
            var cropY = (frameTop - imageTop) / displayScale;
            var cropW = frameWidth / displayScale;
            var cropH = frameHeight / displayScale;

            // The source must cover the complete editable region. Nothing is cropped until Save.
            if (cropW > sw + 0.01 || cropH > sh + 0.01)
                throw new InvalidOperationException("The image does not cover the poster frame. Zoom in until the entire frame is covered, then save again.");

            if (cropX < -0.01 || cropY < -0.01 || cropX + cropW > sw + 0.01 || cropY + cropH > sh + 0.01)
                throw new InvalidOperationException("The image does not fully cover the poster frame. Reposition or zoom the image so no part of the frame is empty.");

            var crop = new Int32Rect(
                Math.Clamp((int)Math.Floor(cropX), 0, Math.Max(0, sw - 1)),
                Math.Clamp((int)Math.Floor(cropY), 0, Math.Max(0, sh - 1)),
                Math.Max(1, Math.Min(sw, (int)Math.Ceiling(cropW))),
                Math.Max(1, Math.Min(sh, (int)Math.Ceiling(cropH))));

            var cropped = new CroppedBitmap(source, crop);
            cropped.Freeze();

            // Build the complete poster-compatible texture directly at the selected
            // resolution. The supplied 1024x2048 template geometry scales uniformly,
            // so the usable region stays in exactly the same relative position for
            // 2048x4096 and 4096x8192. The unused region remains black.
            var template = GetPosterTemplateGeometry(outW, outH);
            var posterVisual = new DrawingVisual();
            using (var dc = posterVisual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, template.CanvasWidth, template.CanvasHeight));
                dc.DrawImage(cropped, new Rect(template.ContentX, template.ContentY, template.ContentWidth, template.ContentHeight));
            }

            var posterBitmap = new RenderTargetBitmap(
                (int)template.CanvasWidth, (int)template.CanvasHeight, 96, 96, PixelFormats.Pbgra32);
            posterBitmap.Render(posterVisual);
            posterBitmap.Freeze();

            var tempDir = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "PosterEditor");
            Directory.CreateDirectory(tempDir);
            var tempPng = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(posterBitmap));
            using (var fs = File.Create(tempPng)) encoder.Save(fs);

            var outputDir = GetConfiguredPosterDirectory(_posterBrowserMod);
            if (string.IsNullOrWhiteSpace(outputDir)) throw new InvalidOperationException("PosterDirectory is not configured.");
            Directory.CreateDirectory(outputDir);

            // Route the final texture conversion through the single application-wide texconv tool.
            var texconv = await EnsureTexconvAsync();
            var texconvRoot = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "PosterEditor", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(texconvRoot);
            await RunTexconvPngAsync(texconv, tempPng, texconvRoot, "poster");
            var convertedPng = Path.Combine(texconvRoot, "poster.png");
            if (!File.Exists(convertedPng)) throw new InvalidDataException("texconv.exe did not produce the expected PNG poster.");
            var baseName = Path.GetFileNameWithoutExtension(_posterImageEditorSourceFile) + "_Poster";
            var desired = Path.Combine(outputDir, SanitizeFileName(baseName) + ".png");
            await CopyFileWithRetryAsync(convertedPng, desired, true);
            TryDeleteFile(convertedPng);
            TryDeleteDirectory(texconvRoot);

            // The Images\{Mod} workspace is the immutable original-image store. Never modify it after preservation.

            TryDeleteFile(tempPng);
            PosterImageEditorStatus.Text = L("Saved {0} × {1} PNG poster.", outW, outH);
            await ViewPosterImagesAsync(_posterBrowserMod);
        }
        catch (Exception ex)
        {
            PosterImageEditorStatus.Text = L("Could not create poster: {0}", ex.Message);
            MessageBox.Show(this, PosterImageEditorStatus.Text, L("Image Editor"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetOperationBusy(false); }
    }

    private void PosterBrowserInvalidToggle_Click(object? sender, RoutedEventArgs e)
    {
        _showInvalidPosterImages = PosterBrowserInvalidToggle?.IsChecked == true;
        if (_posterBrowserMod != null)
            _ = ViewPosterImagesAsync(_posterBrowserMod);
    }

    private void PosterBrowserBackButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePosterBrowser();
    }

    private void PosterBrowserAutoAddButton_Click(object sender, RoutedEventArgs e) => _ = OpenPosterAutoAddAsync();

    private async Task OpenPosterAutoAddAsync()
    {
        if (_posterBrowserMod == null) return;
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = L("Select a folder containing poster images."),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;

        var selectedFolder = dialog.SelectedPath;
        var mod = _posterBrowserMod;
        var rules = ReadPosterRules(mod);

        try
        {
            SetOperationBusy(true, L("Scanning poster folder…"), 0, L("Finding compatible poster images."));
            // Let WPF paint the Working… overlay before the potentially large
            // directory enumeration begins.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            await Task.Delay(50);

            var compatible = await Task.Run(() =>
            {
                var found = new List<string>();
                var candidates = new List<string>();
                try
                {
                    candidates = Directory.EnumerateFiles(selectedFolder, "*", SearchOption.AllDirectories).ToList();
                }
                catch (Exception ex)
                {
                    throw new IOException(L("Could not enumerate the selected folder."), ex);
                }

                for (var i = 0; i < candidates.Count; i++)
                {
                    var index = i;
                    var percent = candidates.Count == 0 ? 100 : index * 100.0 / candidates.Count;
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                        SetOperationBusy(true, L("Scanning poster folder…"), percent,
                            L("Checking {0} of {1}", index + 1, candidates.Count))));

                    // Auto Add deliberately ignores filename-extension and poster-count
                    // restrictions, but it still applies the mod's actual poster geometry
                    // rules (readable image, orientation and aspect ratio).
                    if (TryLoadCompatiblePosterImage(candidates[i], rules))
                        found.Add(candidates[i]);
                }
                return found;
            });

            if (compatible.Count == 0) return;

            _posterAutoAddMod = mod;
            _posterAutoAddFiles.Clear();
            _posterAutoAddFiles.AddRange(compatible.Distinct(StringComparer.OrdinalIgnoreCase));
            PosterAutoAddScaleCombo.SelectedIndex = 0;
            PosterAutoAddUpscaleCombo.SelectedIndex = 0;
            PosterAutoAddResolutionCombo.SelectedIndex = 0;
            BuildPosterAutoAddCards();
            _mode = "poster_auto_add";
            UpdateMode();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Could not scan the selected folder.\n\n{0}", ex.Message), L("Auto Add Posters"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetOperationBusy(false);
        }
    }

    private static bool TryLoadCompatiblePosterImage(string path, PosterRules rules)
    {
        try
        {
            if (Directory.Exists(path)) return false;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0) return false;

            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return false;
            var frame = decoder.Frames[0];
            var width = frame.PixelWidth;
            var height = frame.PixelHeight;
            if (width <= 0 || height <= 0) return false;

            // Extension is intentionally NOT checked here. Auto Add accepts any
            // image format WPF can decode, while still enforcing the mod's geometry.
            var aspect = (double)height / width;
            // Auto Add is for the game's portrait poster slot.  The normal
            // extension/count rules are intentionally bypassed, but landscape
            // and square images are never compatible poster sources.
            if (height <= width) return false;
            if (rules.RequiresPortrait == true && height <= width) return false;
            if (rules.RequiresPortrait == false && width <= height) return false;
            if (rules.MinAspect.HasValue && aspect < rules.MinAspect.Value) return false;
            if (rules.MaxAspect.HasValue && aspect > rules.MaxAspect.Value) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void BuildPosterAutoAddCards()
    {
        if (PosterAutoAddItemsPanel == null) return;
        PosterAutoAddItemsPanel.Children.Clear(); PosterAutoAddCountText.Text = L("{0} compatible image(s)", _posterAutoAddFiles.Count);
        foreach (var file in _posterAutoAddFiles)
        {
            var card = new Border { Width = 188, Height = 304, Margin = new Thickness(7), Padding = new Thickness(8), Background = (Brush)Resources["ButtonBackgroundBrush"], BorderBrush = (Brush)Resources["BorderBrush"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ToolTip = file };
            var panel = new Grid(); panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) }); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var preview = new Image { Width = 164, Height = 206, Stretch = Stretch.Uniform }; try { preview.Source = LoadBitmapFromFile(file); } catch { } panel.Children.Add(preview);
            var name = new TextBlock { Text = Path.GetFileName(file), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)Resources["ForegroundBrush"], Margin = new Thickness(2, 7, 2, 0), ToolTip = file }; Grid.SetRow(name, 1); panel.Children.Add(name);
            var hint = new TextBlock { Text = L("Ready to convert"), Foreground = (Brush)Resources["SecondaryBrush"], FontSize = 10, Margin = new Thickness(2, 4, 2, 0) }; Grid.SetRow(hint, 2); panel.Children.Add(hint);
            card.Child = panel; PosterAutoAddItemsPanel.Children.Add(card);
        }
    }

    private void PosterAutoAddBackButton_Click(object sender, RoutedEventArgs e) { _mode = "poster_browser"; UpdateMode(); }

    private async void PosterAutoAddConvertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_posterAutoAddMod == null || _posterAutoAddFiles.Count == 0) return;
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            // The Steam verification path can be either the game project root or the
            // Steam common folder. Always resolve the actual RetroRewind project root
            // before writing generated posters.
            var gameProjectRoot = GetGameProjectRoot(gameRoot);
            var outputDir = Path.Combine(gameProjectRoot, "Binaries", "Win64", "ue4ss", "Mods", "RogueUnicorn_CustomPosters", "Posters", "_RRModHub");
            var tmpRoot = Path.Combine(DefaultModhubFolder, "Images", "_tmp"); Directory.CreateDirectory(tmpRoot); Directory.CreateDirectory(outputDir);
            var texconv = await EnsureTexconvAsync();
            var scaleMode = (PosterAutoAddScaleCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Fill";
            var upscaleText = (PosterAutoAddUpscaleCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Off";
            var resolution = (PosterAutoAddResolutionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "1024×2048";
            var r = resolution.Split('×'); if (r.Length != 2 || !int.TryParse(r[0], out var targetWidth) || !int.TryParse(r[1], out var targetHeight)) throw new InvalidOperationException(L("The selected resolution is invalid."));
            var upscaleFactor = upscaleText.StartsWith("4", StringComparison.Ordinal) ? 4 : upscaleText.StartsWith("2", StringComparison.Ordinal) ? 2 : 1;
            SetOperationBusy(true, L("Converting posters…"), 0, L("Preparing {0} image(s).", _posterAutoAddFiles.Count));
            for (var i = 0; i < _posterAutoAddFiles.Count; i++)
            {
                var sourceFile = _posterAutoAddFiles[i]; var id = Guid.NewGuid().ToString("N"); var tmpInput = Path.Combine(tmpRoot, id + Path.GetExtension(sourceFile)); var processed = Path.Combine(tmpRoot, id + "_processed.png"); var texOutput = Path.Combine(tmpRoot, id + "_texconv.png");
                try
                {
                    SetOperationBusy(true, L("Copying poster {0} of {1}…", i + 1, _posterAutoAddFiles.Count), i * 100.0 / _posterAutoAddFiles.Count, Path.GetFileName(sourceFile));
                    await Task.Run(() => File.Copy(sourceFile, tmpInput, false));
                    var source = LoadBitmapFromFile(tmpInput);
                    if (upscaleFactor > 1) { SetOperationBusy(true, L("AI upscaling poster {0} of {1}…", i + 1, _posterAutoAddFiles.Count), i * 100.0 / _posterAutoAddFiles.Count, L("Running Real-ESRGAN {0}×", upscaleFactor)); source = await PreparePosterEditorSourceAsync(source, upscaleFactor); }
                    var scaled = RenderPosterAutoAddImage(source, targetWidth, targetHeight, scaleMode); SaveBitmapPng(scaled, processed);
                    SetOperationBusy(true, L("Converting texture {0} of {1}…", i + 1, _posterAutoAddFiles.Count), i * 100.0 / _posterAutoAddFiles.Count, "texconv.exe");
                    await RunTexconvPngAsync(texconv, processed, tmpRoot, id + "_texconv");
                    if (!File.Exists(texOutput)) throw new InvalidDataException(L("texconv.exe did not produce the expected PNG output."));
                    var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourceFile)); if (string.IsNullOrWhiteSpace(baseName)) baseName = "Poster";
                    await CopyFileWithRetryAsync(texOutput, Path.Combine(outputDir, baseName + ".png"), true);
                    SetOperationBusy(true, L("Converted {0} of {1}", i + 1, _posterAutoAddFiles.Count), (i + 1) * 100.0 / _posterAutoAddFiles.Count, Path.GetFileName(sourceFile));
                }
                finally { TryDeleteFile(tmpInput); TryDeleteFile(processed); TryDeleteFile(texOutput); }
            }
            var convertedCount = _posterAutoAddFiles.Count;
            // Conversion is complete: leave the batch page and return to Poster Images.
            // Do this before showing the completion message so the page is closed as soon
            // as the final output has been written.
            _posterAutoAddFiles.Clear();
            _mode = "poster_browser";
            UpdateMode();
            if (_posterBrowserMod != null)
                await ViewPosterImagesAsync(_posterBrowserMod);
            MessageBox.Show(this, L("Converted {0} poster(s) to game-ready PNG files.", convertedCount), L("Auto Add Posters"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, L("Poster conversion failed.\n\n{0}", ex.Message), L("Auto Add Posters"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SetOperationBusy(false); }
    }

    private static BitmapSource RenderPosterAutoAddImage(BitmapSource source, int width, int height, string mode)
    {
        // Auto Add must produce the same game-ready poster canvas as the normal
        // Poster Image Editor: the image occupies the usable poster rectangle and
        // the surrounding frame remains solid black.
        var template = GetPosterTemplateGeometry(width, height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(
                Brushes.Black,
                null,
                new Rect(0, 0, template.CanvasWidth, template.CanvasHeight));

            var sw = Math.Max(1, source.PixelWidth);
            var sh = Math.Max(1, source.PixelHeight);
            var contentWidth = template.ContentWidth;
            var contentHeight = template.ContentHeight;
            var sx = contentWidth / sw;
            var sy = contentHeight / sh;

            double scale = mode.Equals("Fit", StringComparison.OrdinalIgnoreCase)
                ? Math.Min(sx, sy)
                : mode.Equals("Stretch", StringComparison.OrdinalIgnoreCase)
                    ? 1.0
                    : Math.Max(sx, sy);

            var dw = mode.Equals("Stretch", StringComparison.OrdinalIgnoreCase)
                ? contentWidth
                : sw * scale;
            var dh = mode.Equals("Stretch", StringComparison.OrdinalIgnoreCase)
                ? contentHeight
                : sh * scale;

            var x = template.ContentX + (contentWidth - dw) / 2.0;
            var y = template.ContentY + (contentHeight - dh) / 2.0;

            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(source, new Rect(x, y, dw, dh));
        }

        var result = new RenderTargetBitmap(
            (int)template.CanvasWidth,
            (int)template.CanvasHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static void SaveBitmapPng(BitmapSource bitmap, string path) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var fs = File.Create(path); encoder.Save(fs); }

    private async Task RunTexconvPngAsync(string texconv, string input, string outputDirectory, string outputBaseName)
    {
        var psi = new ProcessStartInfo { FileName = texconv, WorkingDirectory = outputDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; psi.ArgumentList.Add("-y"); psi.ArgumentList.Add("-ft"); psi.ArgumentList.Add("png"); psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outputDirectory); psi.ArgumentList.Add(input);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException(L("Could not start texconv.exe.")); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); var expected = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(input) + ".png"); if (process.ExitCode != 0 || !File.Exists(expected)) throw new InvalidOperationException(string.IsNullOrWhiteSpace(await stderr) ? await stdout : await stderr); var desired = Path.Combine(outputDirectory, outputBaseName + ".png"); if (!string.Equals(expected, desired, StringComparison.OrdinalIgnoreCase)) { TryDeleteFile(desired); File.Move(expected, desired); }
    }

    private void PosterBrowserAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_posterBrowserMod != null)
            _ = AddPosterImagesAsync(_posterBrowserMod);
    }

    private void PosterBrowserOpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_posterBrowserMod != null)
            OpenConfiguredPosterDirectory(_posterBrowserMod);
    }

    private void PosterBrowserOperationCancel_Click(object? sender, RoutedEventArgs e)
    {
        try { _posterBrowserSelectedLoadCts?.Cancel(); } catch { }
        try { _posterSearchCts?.Cancel(); } catch { }
        SetOperationBusy(false);
        if (PosterBrowserOperationCancelButton != null) PosterBrowserOperationCancelButton.Visibility = Visibility.Collapsed;
    }

    private void ClosePosterBrowser()
    {
        _posterBrowserMod = null;
        _posterBrowserDirectory = null;
        _posterBrowserFiles.Clear();
        _posterBrowserImageControls.Clear();
        _posterBrowserLoading.Clear();
        _posterBrowserSelectedLoadCts?.Cancel();
        _posterSearchCts?.Cancel();
        _posterSearchResults.Clear();
        if (!string.IsNullOrWhiteSpace(_posterSearchCacheDirectory))
            TryDeleteDirectory(_posterSearchCacheDirectory);
        _posterSearchCacheDirectory = null;
        _posterBrowserSelectedFile = null;
        
        if (PosterBrowserSelectedPanel != null)
            PosterBrowserSelectedPanel.Visibility = Visibility.Collapsed;
        SetPosterBrowserListInteractivity(true);
        _mode = "mods";
        UpdateMode();
    }

    private Grid CreateModRow(ModEntry mod, Dictionary<string, NexusModMetadata>? metadataCache = null)
    {
        var isUe4ssOrderedMod = !mod.IsPak && !mod.IsUe4ssDefault &&
            !Path.GetFileName(mod.Path).Equals("Keybinds", StringComparison.OrdinalIgnoreCase);

        var row = new Grid
        {
            Margin = new Thickness(0, 3, 0, 3),
            Background = mod.IsPak && _selectedPakModPaths.Contains(mod.Path)
                ? new SolidColorBrush(((SolidColorBrush)Resources["AccentBrush"]).Color) { Opacity = 0.22 }
                : Brushes.Transparent,
            Tag = mod.IsPak ? Path.GetFullPath(mod.Path) : null
        };

        // PAK rows: [drag][name/version][enable][context]
        // UE4SS ordered rows: [6-dot drag][name/version][enable][context]
        // Default UE4SS/Keybinds: [name/version][enable][context]
        // Poster mods with a PosterDirectory setting additionally get [Open Posters].
        if (mod.IsPak || isUe4ssOrderedMod)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28), MinWidth = 28 }); // drag
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // main
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // toggle
        var posterDirectory = GetConfiguredPosterDirectory(mod);
        var hasPosterDirectory = !string.IsNullOrWhiteSpace(posterDirectory);
        if (hasPosterDirectory)
        {
            // The UE4SS Mods list only exposes the poster browser. Add Posters and
            // Open Poster Folder are available in the poster browser toolbar.
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // view images
        }
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // context

        var metaKey = mod.IsPak ? PakMetadataKey(mod.Path) : MetadataKey(GetVerifiedGameRoot(), mod.Path);
        var meta = (metadataCache ?? _modListMetadataCache ?? LoadNexusMetadata()).GetValueOrDefault(metaKey);
        var versionText = mod.IsUe4ssDefault
            ? L("Default UE4SS Plugin")
            : GetModVersionStatusText(meta?.InstalledVersion ?? "", meta?.LatestVersion ?? "");

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new TextBlock
        {
            Text = mod.Name + (mod.Enabled ? "" : " (disabled)"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = mod.IsUe4ssDefault ? FontWeights.Bold : FontWeights.Normal
        };
        var version = new TextBlock
        {
            Text = versionText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 4, 0),
            Foreground = (Brush)Resources["SecondaryBrush"]
        };
        content.Children.Add(nameText);
        Grid.SetColumn(nameText, 0);
        content.Children.Add(version);
        Grid.SetColumn(version, 1);

        var mainColumn = (mod.IsPak || isUe4ssOrderedMod) ? 1 : 0;
        var toggleColumn = mainColumn + 1;
        var posterColumn = hasPosterDirectory ? mainColumn + 2 : -1;
        var menuColumn = mainColumn + (hasPosterDirectory ? 3 : 2);

        if (mod.IsPak)
        {
            var handle = new TextBlock
            {
                Text = "⋮⋮",
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = L("Drag to change load order")
            };
            AttachPakDragHandlers(handle, mod.Path);
            Grid.SetColumn(handle, 0);
            row.Children.Add(handle);
        }
        else if (isUe4ssOrderedMod)
        {
            var handle = new TextBlock
            {
                Text = "⋮⋮",
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = L("Drag to change UE4SS load order")
            };
            AttachUe4ssDragHandler(handle, Path.GetFileName(mod.Path));
            Grid.SetColumn(handle, 0);
            row.Children.Add(handle);
        }

        var name = new Button
        {
            Content = content,
            Style = (Style)Resources["BrowseButtonStyle"],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = mod,
            ToolTip = mod.Name
        };
        name.Click += (_, _) =>
        {
            if (mod.IsPak && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                TogglePakMultiSelection(mod.Path, row);
                return;
            }
            OpenModNexusSlideout(mod);
        };
        Grid.SetColumn(name, mainColumn);
        row.Children.Add(name);

        var toggleIconSource = LoadModIcon(mod.Enabled ? "Disable.png" : "Enable.png");
        var toggleIcon = new Border
        {
            Width = 18,
            Height = 18,
            Background = (Brush)Resources["ForegroundBrush"],
            OpacityMask = new ImageBrush(toggleIconSource) { Stretch = Stretch.Uniform }
        };
        var toggle = new Button
        {
            Content = toggleIcon,
            Width = 34,
            Height = 34,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)Resources["ModIconButtonStyle"],
            Tag = mod,
            ToolTip = mod.Enabled ? L("Disable mod") : L("Enable mod")
        };
        toggle.Click += ModToggle_Click;
        Grid.SetColumn(toggle, toggleColumn);
        row.Children.Add(toggle);

        if (hasPosterDirectory)
        {
            // The UE4SS Mods list keeps only the poster-browser entry point.
            // Adding posters and opening the folder are handled by the poster
            // browser toolbar to avoid duplicate actions on the mod row.
            var posterButtons = new[]
            {
                (File: "View_Images.png", Tip: L("View Images"), Action: (Action)(() => _ = ViewPosterImagesAsync(mod)))
            };

            var posterButtonColumn = posterColumn;
            foreach (var posterButton in posterButtons)
            {
                var icon = new Border
                {
                    Width = 19,
                    Height = 19,
                    Background = (Brush)Resources["ForegroundBrush"],
                    OpacityMask = new ImageBrush(LoadModIcon(posterButton.File)) { Stretch = Stretch.Uniform }
                };
                var button = new Button
                {
                    Content = icon,
                    Width = 34,
                    Height = 34,
                    Margin = new Thickness(6, 0, 0, 0),
                    Style = (Style)Resources["ModIconButtonStyle"],
                    Tag = mod,
                    ToolTip = posterButton.Tip
                };
                button.Click += (_, _) => posterButton.Action();
                Grid.SetColumn(button, posterButtonColumn);
                row.Children.Add(button);
                posterButtonColumn++;
            }
        }

        var menu = new Button
        {
            Content = "⋮",
            Width = 34,
            Height = 34,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)Resources["ModIconButtonStyle"],
            Tag = mod,
            ToolTip = L("Mod options")
        };
        menu.Click += ModContextButton_Click;
        Grid.SetColumn(menu, menuColumn);
        row.Children.Add(menu);

        return row;
    }

    private Grid CreatePendingModRow(PendingModEntry mod)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var install = new Button
        {
            Content = L("Install: {0}", mod.Name),
            Style = (Style)Resources["AccentButtonStyle"],
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = mod
        };
        install.Click += PendingInstall_Click;
        Grid.SetColumn(install, 0);
        row.Children.Add(install);

        var menu = new Button
        {
            Content = "⋮", Width = 34, Height = 34, Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)Resources["ModIconButtonStyle"], Tag = mod, ToolTip = L("Download options")
        };
        menu.Click += PendingContextButton_Click;
        Grid.SetColumn(menu, 1);
        row.Children.Add(menu);
        return row;
    }

    private async void OpenModNexusSlideout(ModEntry mod)
    {
        var root = GetVerifiedGameRoot();
        var metaKey = mod.IsPak ? PakMetadataKey(mod.Path) : MetadataKey(root, mod.Path);
        var meta = LoadNexusMetadata().GetValueOrDefault(metaKey);
        if (meta == null || meta.ModId <= 0 || string.IsNullOrWhiteSpace(meta.Game))
        {
            MessageBox.Show(this, L("This mod is not linked to Nexus Mods yet."), L("Nexus"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var host = new OverlayDialogHost(this, SlidePanelMode.Bottom)
        {
            Background = (Brush)Resources["CardBrush"],
            Foreground = (Brush)Resources["ForegroundBrush"]
        };

        var panel = new Grid { Margin = new Thickness(22) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = meta.Name,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["ForegroundBrush"]
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = GetModVersionStatusText(meta.InstalledVersion, meta.LatestVersion),
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)Resources["SecondaryBrush"]
        });
        header.Children.Add(titleStack);
        var close = new Button
        {
            Content = "×", Width = 36, Height = 36,
            Style = (Style)Resources["ModIconButtonStyle"],
            ToolTip = L("Close")
        };
        close.Click += (_, _) => host.Close();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        panel.Children.Add(header);

        var navRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        navRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nav = new WrapPanel { VerticalAlignment = VerticalAlignment.Top };
        var actionPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var actionButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var nexusStatus = new TextBlock
        {
            Text = L("Nexus: Checking…"),
            Foreground = (Brush)Resources["SecondaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 3, 2, 0),
            FontSize = 12
        };
        actionPanel.Children.Add(actionButtons);
        actionPanel.Tag = nexusStatus;
        Grid.SetColumn(nav, 0);
        Grid.SetColumn(actionPanel, 1);
        navRow.Children.Add(nav);
        navRow.Children.Add(actionPanel);

        var content = new Border
        {
            Background = (Brush)Resources["CardBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14)
        };
        var contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = true,
            IsHitTestVisible = true,
            Tag = "NexusDescriptionScroll",
            Content = new TextBlock { Text = L("Loading Nexus information…"), Foreground = (Brush)Resources["SecondaryBrush"] }
        };
        content.Child = contentScroll;
        contentScroll.DataContext = nexusStatus;

        AddNexusNativeButton(nav, host, meta, contentScroll, L("Description"), "description", true);
        AddNexusNativeButton(nav, host, meta, contentScroll, L("Files {0}", meta.FilesCount < 0 ? "" : meta.FilesCount.ToString()), "files", meta.FilesCount != 0);

        AddNexusActionButtons(actionButtons, meta, contentScroll);
        AddNexusOpenBrowserButton(actionButtons, meta);
        actionPanel.Children.Add(nexusStatus);

        Grid.SetRow(navRow, 1);
        panel.Children.Add(navRow);
        Grid.SetRow(content, 2);
        panel.Children.Add(content);

        host.Content = panel;
        host.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is UIElement element && element.Focusable)
                element.Focus();
        };
        host.OnBackdropClose = host.Close;
        host.OnEscapeClose = host.Close;

        // The panel is shown immediately; only the selected native content is loaded
        // asynchronously so a slow Nexus API cannot prevent the panel from appearing.
        host.ShowDialog();
    }
}


internal sealed class VideoEditorRenderEngine : IAsyncDisposable
{
    private Process? _process;
    private CancellationTokenSource? _cts;
    public event Action<byte[], int, int, TimeSpan>? FrameReady;
    public event Action<string>? Error;

    private bool _hardwareDecode;
    private bool _cudaDecode;
    private bool _nvencEncode;
    private bool _cudaFilters;

    public bool HardwareDecodeEnabled => _hardwareDecode || _cudaDecode;
    public bool CudaDecodeEnabled => _cudaDecode;
    public bool NvencEncodeEnabled => _nvencEncode;
    public bool CudaFiltersEnabled => _cudaFilters;

    public async Task DetectHardwareAccelerationAsync(string ffmpeg, CancellationToken token)
    {
        _hardwareDecode = false;
        _cudaDecode = false;
        _nvencEncode = false;
        _cudaFilters = false;

        try
        {
            var hw = await RunFfmpegProbeAsync(
                ffmpeg,
                new[] { "-hide_banner", "-hwaccels" },
                token);

            _hardwareDecode = hw.IndexOf("d3d11va", StringComparison.OrdinalIgnoreCase) >= 0;

            var encoders = await RunFfmpegProbeAsync(
                ffmpeg,
                new[] { "-hide_banner", "-encoders" },
                token);
            _nvencEncode = Regex.IsMatch(
                encoders,
                @"(?m)^\s*[A-Z\.]{6}\s+h264_nvenc\b",
                RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                encoders,
                @"(?m)^\s*[A-Z\.]{6}\s+hevc_nvenc\b",
                RegexOptions.CultureInvariant);

            var filters = await RunFfmpegProbeAsync(
                ffmpeg,
                new[] { "-hide_banner", "-filters" },
                token);
            _cudaFilters =
                filters.IndexOf("hwupload_cuda", StringComparison.OrdinalIgnoreCase) >= 0 &&
                filters.IndexOf("scale_cuda", StringComparison.OrdinalIgnoreCase) >= 0;

            // A compiled CUDA FFmpeg is not enough: verify that the installed
            // NVIDIA driver can actually create a CUDA device.
            if (hw.IndexOf("cuda", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var cudaProbe = await RunFfmpegProbeAsync(
                    ffmpeg,
                    new[]
                    {
                        "-hide_banner",
                        "-loglevel", "error",
                        "-init_hw_device", "cuda=retrorewind_cuda:0",
                        "-f", "lavfi",
                        "-i", "color=c=black:s=16x16:r=1",
                        "-frames:v", "1",
                        "-f", "null", "-"
                    },
                    token);

                // A successful probe returns no fatal CUDA error. The helper
                // exposes the process exit code via its marker.
                _cudaDecode = cudaProbe.StartsWith("__RETROREWIND_CUDA_OK__",
                    StringComparison.Ordinal);
            }

            // If CUDA is usable, prefer it over D3D11VA for decoding.
            if (_cudaDecode)
                _hardwareDecode = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Video Editor GPU capability detection failed: {ex}");
        }
    }

    private static async Task<string> RunFfmpegProbeAsync(
        string ffmpeg,
        IEnumerable<string> arguments,
        CancellationToken token)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (var arg in arguments)
            p.StartInfo.ArgumentList.Add(arg);

        if (!p.Start())
            return string.Empty;

        var stdoutTask = p.StandardOutput.ReadToEndAsync(token);
        var stderrTask = p.StandardError.ReadToEndAsync(token);
        await p.WaitForExitAsync(token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var text = stdout + "\n" + stderr;
        return p.ExitCode == 0 ? "__RETROREWIND_CUDA_OK__\n" + text : text;
    }

    public async Task StartAsync(
        string ffmpeg,
        string input,
        string filter,
        TimeSpan position,
        int width,
        int height,
        bool playing,
        double frameRate,
        CancellationToken externalToken)
    {
        // Start the new render process without waiting for the previous one to
        // finish. This keeps effect changes responsive; the old process is stopped
        // as soon as the new stream has produced its first frame.
        var oldProcess = _process;
        var oldCts = _cts;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var token = cts.Token;
        _cts = cts;

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        // Let FFmpeg use all available worker threads. The old preview path was
        // effectively constrained by the default filter threading behaviour,
        // which is especially noticeable with the pixel-remapping effects.
        psi.ArgumentList.Add("-threads");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-filter_threads");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-filter_complex_threads");
        psi.ArgumentList.Add("0");

        if (_cudaDecode)
        {
            // NVDEC keeps decode on the NVIDIA video engine. FFmpeg will
            // download frames when the current effect graph requires CPU-side
            // filters; the editor still benefits from hardware decode.
            psi.ArgumentList.Add("-hwaccel");
            psi.ArgumentList.Add("cuda");
        }
        else if (_hardwareDecode)
        {
            psi.ArgumentList.Add("-hwaccel");
            psi.ArgumentList.Add("d3d11va");
        }

        var seconds = Math.Max(0, position.TotalSeconds)
            .ToString("0.###", CultureInfo.InvariantCulture);

        if (playing)
        {
            psi.ArgumentList.Add("-fflags");
            psi.ArgumentList.Add("+nobuffer");
            psi.ArgumentList.Add("-flags");
            psi.ArgumentList.Add("low_delay");
        }

        // While playing, seek before opening the input so effect changes and
        // timeline seeks do not spend seconds decoding the entire prefix.
        if (playing)
        {
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(seconds);
        }

        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(Path.GetFullPath(input));

        // When paused, use accurate post-input seeking for the exact frame.
        if (!playing)
        {
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(seconds);
        }

        psi.ArgumentList.Add("-vf");
        psi.ArgumentList.Add(filter);

        if (playing)
        {
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add(frameRate.ToString("0.###", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-fps_mode");
            psi.ArgumentList.Add("cfr");
        }
        else
        {
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
        }

        psi.ArgumentList.Add("-an");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("rawvideo");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("bgra");
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add($"{width}x{height}");
        psi.ArgumentList.Add("pipe:1");

        try
        {
            var process = Process.Start(psi);
            if (process == null)
            {
                try { cts.Cancel(); } catch { }
                throw new InvalidOperationException("FFmpeg could not be started.");
            }

            _process = process;

            _ = Task.Run(async () =>
            {
                try
                {
                    var stderrTask = process.StandardError.ReadToEndAsync(token);
                    var frameBytes = checked(width * height * 4);
                    var buffer = new byte[frameBytes];
                    long frame = 0;
                    var safeFrameRate = Math.Clamp(frameRate, 1.0, 120.0);
                    var frameDuration = TimeSpan.FromSeconds(1.0 / safeFrameRate);
                    var wallClock = Stopwatch.GetTimestamp();

                    while (!token.IsCancellationRequested)
                    {
                        var offset = 0;
                        while (offset < frameBytes && !token.IsCancellationRequested)
                        {
                            var n = await process.StandardOutput.BaseStream.ReadAsync(
                                buffer.AsMemory(offset, frameBytes - offset), token);
                            if (n <= 0) break;
                            offset += n;
                        }

                        if (offset != frameBytes || token.IsCancellationRequested)
                            break;

                        // FFmpeg can decode considerably faster than realtime.
                        // Pace presentation explicitly so the WPF preview runs at
                        // the same 30 fps cadence as the exported preview stream.
                        if (playing)
                        {
                            var targetTicks = (long)Math.Round(
                                frame * (double)Stopwatch.Frequency / safeFrameRate);
                            var elapsedTicks = Stopwatch.GetTimestamp() - wallClock;
                            var waitTicks = targetTicks - elapsedTicks;
                            if (waitTicks > 0)
                            {
                                var waitMs = (int)Math.Min(
                                    1000,
                                    Math.Max(1,
                                        Math.Round(waitTicks * 1000.0 / Stopwatch.Frequency)));
                                await Task.Delay(waitMs, token);
                            }
                        }

                        var copy = new byte[frameBytes];
                        Buffer.BlockCopy(buffer, 0, copy, 0, frameBytes);
                        FrameReady?.Invoke(
                            copy,
                            width,
                            height,
                            position + TimeSpan.FromTicks(frameDuration.Ticks * frame));
                        frame++;
                    }

                    var error = await stderrTask;
                    if (!string.IsNullOrWhiteSpace(error) && !token.IsCancellationRequested)
                        Error?.Invoke(error.Trim());
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        Error?.Invoke(ex.Message);
                }
                finally
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    try { process.Dispose(); } catch { }
                }
            }, token);

            // Give the new stream a chance to initialize. The old process remains
            // available as the audio clock and is stopped by the next explicit
            // Start/Stop operation; this avoids blocking the UI on filter startup.
            try { oldCts?.Cancel(); } catch { }
            try { if (oldProcess != null && !oldProcess.HasExited) oldProcess.Kill(true); } catch { }
            try { oldProcess?.Dispose(); } catch { }
        }
        catch (Exception ex)
        {
            try { cts.Cancel(); } catch { }
            Error?.Invoke(ex.Message);
            throw;
        }
    }

    public async Task StopAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { if (_process != null && !_process.HasExited) _process.Kill(true); } catch { }
        try { _process?.Dispose(); } catch { }
        _process = null;
        if (_cts != null)
        {
            try { _cts.Dispose(); } catch { }
            _cts = null;
        }
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}


