using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Mknk.LaptopFanChecker;

// Reads notification state and disposes only handles created by this test or monitor.
// It never creates memory pressure or runs a memory boost.
internal static class MemoryPressureWindowsTests
{
    private static int assertions;

    private sealed class ReferenceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ReferenceHandle() : base(true) { }
        protected override bool ReleaseHandle() { return CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ReferenceHandle CreateMemoryResourceNotification(int notificationType);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryMemoryResourceNotification(ReferenceHandle notification,
        [MarshalAs(UnmanagedType.Bool)] out bool state);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private static int Main()
    {
        try
        {
            ConstructionDoesNotCreateNotification();
            ReadMatchesIndependentWindowsQuery();
            ClosedNotificationFailsClosedThenRecovers();
            InvalidNotificationFailsClosedThenRecovers();
            RepeatedCreateReadDisposeClosesEveryOwnedHandle();
            Console.WriteLine("PASS: Windows low-memory notification; " + assertions + " assertions (read-only)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }

    private static void ConstructionDoesNotCreateNotification()
    {
        MemoryPressureMonitor monitor = new MemoryPressureMonitor();
        try
        {
            Check(GetOwnedHandle(monitor) == null, "construction must not allocate a notification handle");
            monitor.Dispose();
            monitor.Dispose();
            Check(!monitor.ReadLowMemory().HasValue, "disposing before first read returns unavailable");
            Check(GetOwnedHandle(monitor) == null, "reads after disposal must not create a notification");
        }
        finally { monitor.Dispose(); }
    }

    private static void ReadMatchesIndependentWindowsQuery()
    {
        using (ReferenceHandle reference = CreateMemoryResourceNotification(0))
        using (MemoryPressureMonitor monitor = new MemoryPressureMonitor())
        {
            Check(reference != null && !reference.IsInvalid, "independent low-memory notification can be created");
            bool before;
            bool after;
            Check(QueryMemoryResourceNotification(reference, out before), "independent query before monitor succeeds");
            bool? actual = monitor.ReadLowMemory();
            Check(QueryMemoryResourceNotification(reference, out after), "independent query after monitor succeeds");
            Check(actual.HasValue, "monitor supplies an available Windows reading");
            // The system-wide state can change between these nonblocking calls.
            // When stable, the independently queried value must match exactly.
            if (before == after)
                Check(actual.Value == before, "monitor agrees with independent Windows low-memory state");
            else
                Console.WriteLine("INFO: Windows low-memory state changed during the comparison");
            SafeHandle first = GetOwnedHandle(monitor);
            Check(first != null && !first.IsInvalid && !first.IsClosed, "reading owns a live notification handle");
            Check(monitor.ReadLowMemory().HasValue, "second notification reading is available");
            Check(Object.ReferenceEquals(first, GetOwnedHandle(monitor)), "repeated reads reuse the owned notification handle");
            Console.WriteLine("INFO: Windows low-memory notification = " + actual.Value);
        }
    }

    private static void ClosedNotificationFailsClosedThenRecovers()
    {
        using (MemoryPressureMonitor monitor = new MemoryPressureMonitor())
        {
            Check(monitor.ReadLowMemory().HasValue, "closed-handle recovery starts with a successful reading");
            // Test-only invalidation exercises the fail-closed path without exhausting resources.
            SafeHandle notification = GetOwnedHandle(monitor);
            notification.Dispose();
            Check(!monitor.ReadLowMemory().HasValue, "a closed notification returns unavailable without throwing");
            Check(monitor.ReadLowMemory().HasValue, "sample after a closed-notification failure recreates the notification");
            Check(!Object.ReferenceEquals(notification, GetOwnedHandle(monitor)), "recovery owns a fresh notification");
        }
    }

    private static void InvalidNotificationFailsClosedThenRecovers()
    {
        using (MemoryPressureMonitor monitor = new MemoryPressureMonitor())
        {
            Check(monitor.ReadLowMemory().HasValue, "invalid-handle recovery starts with a successful reading");
            SafeHandle original = GetOwnedHandle(monitor);
            original.Dispose();
            // Zero is a documented invalid notification handle; no native resource is created here.
            SafeHandle invalid = (SafeHandle)Activator.CreateInstance(original.GetType(), true);
            try
            {
                GetHandleField().SetValue(monitor, invalid);
                Check(!monitor.ReadLowMemory().HasValue, "an invalid notification returns unavailable");
                Check(invalid.IsClosed, "failed notification handle is disposed");
                Check(monitor.ReadLowMemory().HasValue, "sample after an invalid-notification failure retries creation");
            }
            finally { invalid.Dispose(); }
        }
    }

    private static void RepeatedCreateReadDisposeClosesEveryOwnedHandle()
    {
        for (int iteration = 0; iteration < 128; iteration++)
        {
            MemoryPressureMonitor monitor = new MemoryPressureMonitor();
            try
            {
                Check(monitor.ReadLowMemory().HasValue, "repeated lifecycle supplies a reading");
                SafeHandle notification = GetOwnedHandle(monitor);
                IntPtr nativeHandle = notification.DangerousGetHandle();
                monitor.Dispose();
                Check(notification.IsClosed, "dispose closes each owned SafeHandle");
                uint flags;
                Check(!GetHandleInformation(nativeHandle, out flags), "dispose releases each exact Windows handle");
                monitor.Dispose();
                Check(!monitor.ReadLowMemory().HasValue, "read after repeated disposal remains unavailable");
            }
            finally { monitor.Dispose(); }
        }
    }

    // Handle introspection stays in the test and verifies resource ownership without
    // depending on noisy process-wide handle counts or exposing a production test API.
    private static FieldInfo GetHandleField()
    {
        foreach (FieldInfo field in typeof(MemoryPressureMonitor).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            if (typeof(SafeHandle).IsAssignableFrom(field.FieldType)) return field;
        throw new Exception("monitor must own its notification through a SafeHandle");
    }

    private static SafeHandle GetOwnedHandle(MemoryPressureMonitor monitor)
    {
        return (SafeHandle)GetHandleField().GetValue(monitor);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
}
