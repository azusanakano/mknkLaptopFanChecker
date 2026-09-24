using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Mknk.LaptopFanChecker
{
    internal sealed class MemoryReading
    {
        public ulong TotalBytes { get; set; }
        public ulong AvailableBytes { get; set; }
        public double UsedPercent { get { return 100.0 * (TotalBytes - AvailableBytes) / TotalBytes; } }
    }

    internal sealed class MemoryBoostResult
    {
        public MemoryReading Before { get; set; }
        public MemoryReading After { get; set; }
        public int TrimmedProcesses { get; set; }
        public bool Cancelled { get; set; }
    }

    internal static class MemoryBoost
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length, Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
            public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        internal sealed class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public NativeHandle() : base(true) { }
            protected override bool ReleaseHandle() { return CloseHandle(handle); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern NativeHandle OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsProcessCritical(NativeHandle process, out bool critical);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(NativeHandle process, int flags, StringBuilder name, ref int size);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(NativeHandle process, uint access, out NativeHandle token);
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(NativeHandle process);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        public static MemoryReading ReadMemory()
        {
            try
            {
                MemoryStatus status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
                if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0 || status.AvailablePhysical > status.TotalPhysical)
                    return null;
                return new MemoryReading { TotalBytes = status.TotalPhysical, AvailableBytes = status.AvailablePhysical };
            }
            catch { return null; }
        }

        public static MemoryBoostResult Run(CancellationToken cancellation)
        {
            MemoryBoostResult result = new MemoryBoostResult { Before = ReadMemory() };
            using (Process current = Process.GetCurrentProcess())
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (identity.User == null)
                    return result;
                Process[] processes = Process.GetProcesses();
                Stopwatch duration = Stopwatch.StartNew();
                try
                {
                    foreach (Process process in processes)
                    {
                        if (cancellation.IsCancellationRequested || duration.Elapsed > TimeSpan.FromSeconds(10))
                            break;
                        if (TryTrimBackgroundProcess(process, current.Id, current.SessionId, identity.User.Value, cancellation))
                            result.TrimmedProcesses++;
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                        process.Dispose();
                }
            }
            result.After = ReadMemory();
            result.Cancelled = cancellation.IsCancellationRequested;
            return result;
        }

        internal static bool TryTrimBackgroundProcess(Process process, int currentId, int sessionId, string userSid, CancellationToken cancellation)
        {
            try
            {
                if (cancellation.IsCancellationRequested || process.Id == currentId || process.SessionId != sessionId ||
                    process.SessionId == 0 || process.MainWindowHandle != IntPtr.Zero || process.WorkingSet64 < 50L * 1024 * 1024)
                    return false;
                // Request only query/working-set rights; never terminate or alter priorities.
                using (NativeHandle handle = OpenProcess(0x1000 | 0x0100, false, process.Id))
                {
                    bool critical;
                    if (handle.IsInvalid || !IsProcessCritical(handle, out critical) || critical)
                        return false;
                    StringBuilder path = new StringBuilder(32768);
                    int length = path.Capacity;
                    if (!QueryFullProcessImageName(handle, 0, path, ref length))
                        return false;
                    string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    if (String.IsNullOrWhiteSpace(windows))
                        return false;
                    windows = windows.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (path.ToString().StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                        return false;
                    NativeHandle token;
                    if (!OpenProcessToken(handle, 0x0008, out token))
                        return false;
                    using (token)
                    using (WindowsIdentity owner = new WindowsIdentity(token.DangerousGetHandle()))
                    {
                        if (owner.User == null || owner.User.Value != userSid)
                            return false;
                    }
                    uint foreground;
                    GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
                    if (foreground == process.Id || cancellation.IsCancellationRequested)
                        return false;
                    // Also protect renderers/helpers using the foreground application's executable.
                    if (foreground != 0)
                    {
                        using (NativeHandle foregroundHandle = OpenProcess(0x1000, false, (int)foreground))
                        {
                            StringBuilder foregroundPath = new StringBuilder(32768);
                            int foregroundLength = foregroundPath.Capacity;
                            if (foregroundHandle.IsInvalid || !QueryFullProcessImageName(foregroundHandle, 0, foregroundPath, ref foregroundLength))
                                return false;
                            if (String.Equals(path.ToString(), foregroundPath.ToString(), StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                    }
                    return EmptyWorkingSet(handle);
                }
            }
            catch { return false; } // Exited, inaccessible and protected processes are skipped.
        }
    }
}
