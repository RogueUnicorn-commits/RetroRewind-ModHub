using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;

namespace RogueUnicorn.StoreTransfer;

public partial class MainWindow
{
    private readonly List<PerformanceCounter> _gpuUsageCounters = new();
    private DateTime _gpuCounterRefreshUtc = DateTime.MinValue;

    private void StartResourceUsageMonitoring()
    {
        try { _resourceUsageTimer?.Stop(); } catch { }
        try { _resourceUsageProcess?.Dispose(); } catch { }
        DisposeResourceUsageCounters();

        try
        {
            _resourceUsageProcess = Process.GetCurrentProcess();
            _resourceUsageProcess.Refresh();
            _resourceUsageLastCpu = _resourceUsageProcess.TotalProcessorTime;
            _resourceUsageLastSampleUtc = DateTime.UtcNow;

            UpdateResourceUsageDisplay(0, _resourceUsageProcess.WorkingSet64 / (1024.0 * 1024.0), 0);

            _resourceUsageTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _resourceUsageTimer.Tick += (_, _) => SampleResourceUsage();
            _resourceUsageTimer.Start();
        }
        catch
        {
            UpdateResourceUsageDisplay(0, 0, 0);
        }
    }

    private void SampleResourceUsage()
    {
        try
        {
            if (_resourceUsageProcess == null || _resourceUsageProcess.HasExited)
                _resourceUsageProcess = Process.GetCurrentProcess();

            _resourceUsageProcess.Refresh();
            var now = DateTime.UtcNow;
            var cpu = _resourceUsageProcess.TotalProcessorTime;
            var elapsed = (now - _resourceUsageLastSampleUtc).TotalSeconds;
            var cpuSeconds = (cpu - _resourceUsageLastCpu).TotalSeconds;
            var cpuPercent = elapsed > 0
                ? cpuSeconds / (elapsed * Math.Max(1, Environment.ProcessorCount)) * 100.0
                : 0;

            _resourceUsageLastCpu = cpu;
            _resourceUsageLastSampleUtc = now;

            var ramMb = _resourceUsageProcess.WorkingSet64 / (1024.0 * 1024.0);
            var gpuPercent = GetCurrentProcessGpuUsage();
            // Windows GPU Engine counters can legitimately report 0 for a process
            // that is using CUDA/NVENC/NVDEC because those engines are exposed as
            // separate engine nodes. Fall back to the NVIDIA driver telemetry so
            // the title bar does not falsely imply that the GPU is idle.
            if (gpuPercent <= 0.01)
            {
                var nvidia = TryGetNvidiaOverallGpuUsage();
                if (nvidia.HasValue) gpuPercent = nvidia.Value;
            }
            UpdateResourceUsageDisplay(cpuPercent, ramMb, gpuPercent);
        }
        catch
        {
            // Keep the last useful display rather than replacing real values with
            // zeros if a transient performance-counter sample fails.
        }
    }

    private void UpdateResourceUsageDisplay(double cpuPercent, double ramMb, double gpuPercent)
    {
        if (ResourceUsageTitleBarText == null) return;
        ResourceUsageTitleBarText.Text =
            $"High Performance | GPU: {Math.Clamp(gpuPercent, 0, 100):0}% | CPU: {Math.Clamp(cpuPercent, 0, 100):0}% | RAM: {Math.Max(0, ramMb):0} MB";
    }

    private double GetCurrentProcessGpuUsage()
    {
        try
        {
            if (_gpuUsageCounters.Count == 0 || DateTime.UtcNow - _gpuCounterRefreshUtc > TimeSpan.FromSeconds(5))
                RefreshGpuUsageCounters();

            double maxUsage = 0;
            foreach (var counter in _gpuUsageCounters)
            {
                try
                {
                    var value = counter.NextValue();
                    if (!float.IsNaN(value) && !float.IsInfinity(value))
                        maxUsage = Math.Max(maxUsage, value);
                }
                catch { }
            }

            return maxUsage;
        }
        catch
        {
            return 0;
        }
    }

    private void RefreshGpuUsageCounters()
    {
        DisposeResourceUsageCounters();
        _gpuCounterRefreshUtc = DateTime.UtcNow;

        try
        {
            var pid = Process.GetCurrentProcess().Id;
            var prefix = $"process_{pid}_";
            var category = new PerformanceCounterCategory("GPU Engine");

            foreach (var instance in category.GetInstanceNames().Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                    counter.NextValue();
                    _gpuUsageCounters.Add(counter);
                }
                catch { }
            }
        }
        catch { }
    }


    private double? TryGetNvidiaOverallGpuUsage()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(750);
            if (process.ExitCode != 0) return null;

            var values = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => double.TryParse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : -1)
                .Where(value => value >= 0)
                .ToList();

            return values.Count == 0 ? null : values.Max();
        }
        catch
        {
            return null;
        }
    }

    private void DisposeResourceUsageCounters()
    {
        foreach (var counter in _gpuUsageCounters)
        {
            try { counter.Dispose(); } catch { }
        }
        _gpuUsageCounters.Clear();
    }
}
