using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace RogueUnicorn.StoreTransfer;

internal static class RunLaunchHelper
{
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_EXECUTE = 0x0008;
    private const uint INFINITE = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string? applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr address, UIntPtr size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr attrs, UIntPtr stackSize, IntPtr startAddress, IntPtr parameter, uint flags, IntPtr threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr address, UIntPtr size, uint freeType);

    private const uint MEM_RELEASE = 0x8000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    public static Process Launch(string exe, string arguments, IEnumerable<string> libraries)
    {
        if (!File.Exists(exe))
            throw new FileNotFoundException("Retro Rewind executable was not found.", exe);

        var exeDirectory = Path.GetDirectoryName(exe)!;
        var libs = libraries
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var command = $"\"{exe}\" {arguments}".Trim();
        var si = new STARTUPINFO { cb = (uint)Marshal.SizeOf<STARTUPINFO>() };

        if (!CreateProcess(exe, new StringBuilder(command), IntPtr.Zero, IntPtr.Zero, false,
            CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero,
            exeDirectory, ref si, out var pi))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            foreach (var library in libs)
            {
                var full = ResolveLibrary(exe, library);
                if (full == null)
                    throw new FileNotFoundException(
                        $"The selected library '{library}' was not found beside the game executable.",
                        Path.Combine(exeDirectory, library));

                LoadRemoteLibrary(pi.hProcess, full);
            }

            // UE4SS's documented proxy installation uses dwmapi.dll beside the
            // game executable. If the proxy itself does not initialize UE4SS in
            // this launch path, explicitly load UE4SS.dll from the same UE4SS
            // installation before resuming the game. This is the documented
            // manual-injection fallback and guarantees UE4SS is in the process.
            if (libs.Any(x => x.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase)))
            {
                var ue4ss = Path.Combine(exeDirectory, "ue4ss", "UE4SS.dll");
                if (File.Exists(ue4ss))
                    LoadRemoteLibrary(pi.hProcess, ue4ss);
            }

            var resume = ResumeThread(pi.hThread);
            if (resume == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);

            return Process.GetProcessById((int)pi.dwProcessId);
        }
        catch
        {
            try { CloseHandle(pi.hThread); } catch { }
            try { CloseHandle(pi.hProcess); } catch { }
            throw;
        }
    }

    private static void LoadRemoteLibrary(IntPtr process, string fullPath)
    {
        var bytes = Encoding.Unicode.GetBytes(Path.GetFullPath(fullPath) + "\0");
        var remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)bytes.Length,
            MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

        if (remote == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            if (!WriteProcessMemory(process, remote, bytes, (UIntPtr)bytes.Length, out var written) ||
                written.ToUInt64() != (ulong)bytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var kernel32 = GetModuleHandle("kernel32.dll");
            if (kernel32 == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var thread = CreateRemoteThread(
                process, IntPtr.Zero, UIntPtr.Zero, loadLibrary, remote, 0, IntPtr.Zero);

            if (thread == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                if (WaitForSingleObject(thread, INFINITE) == 0xFFFFFFFF)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                if (!GetExitCodeThread(thread, out var moduleHandle) || moduleHandle == 0)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        $"Windows could not load '{Path.GetFileName(fullPath)}' into the game process.");
                }
            }
            finally
            {
                CloseHandle(thread);
            }
        }
        finally
        {
            VirtualFreeEx(process, remote, UIntPtr.Zero, MEM_RELEASE);
        }
    }

    private static string? ResolveLibrary(string exe, string library)
    {
        if (Path.IsPathRooted(library) && File.Exists(library))
            return Path.GetFullPath(library);

        var gameDir = Path.GetDirectoryName(exe)!;
        var candidates = new[]
        {
            Path.Combine(gameDir, library),
            Path.Combine(gameDir, "Binaries", "Win64", library),
            Path.Combine(gameDir, "RetroRewind", "Binaries", "Win64", library),
            Path.Combine(gameDir, "RetroRewind", library)
        };

        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }
}
