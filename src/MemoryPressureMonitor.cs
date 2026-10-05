using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mknk.LaptopFanChecker
{
    internal sealed class MemoryPressureMonitor : IDisposable
    {
        private readonly object sync = new object();
        private NotificationHandle notification;
        private bool disposed;

        private sealed class NotificationHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public NotificationHandle() : base(true) { }
            protected override bool ReleaseHandle() { return CloseHandle(handle); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern NotificationHandle CreateMemoryResourceNotification(int notificationType);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryMemoryResourceNotification(NotificationHandle notification,
            [MarshalAs(UnmanagedType.Bool)] out bool state);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        // true: Windows signals low physical memory; false: no such notification;
        // null: unavailable. QueryMemoryResourceNotification never waits.
        public bool? ReadLowMemory()
        {
            lock (sync)
            {
                if (disposed) return null;
                try
                {
                    // Defer native allocation until a real sample is requested.
                    if (notification == null)
                        notification = CreateMemoryResourceNotification(0); // LowMemoryResourceNotification
                    bool lowMemory;
                    if (notification == null || notification.IsInvalid ||
                        !QueryMemoryResourceNotification(notification, out lowMemory))
                    {
                        CloseNotification();
                        return null;
                    }
                    return lowMemory;
                }
                catch
                {
                    // A failed sample cannot trigger automatic work. The next sample retries.
                    CloseNotification();
                    return null;
                }
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                disposed = true;
                CloseNotification();
            }
        }

        private void CloseNotification()
        {
            NotificationHandle owned = notification;
            notification = null;
            if (owned != null) owned.Dispose();
        }
    }
}
