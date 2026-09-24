using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Mknk.LaptopFanChecker;

// This executable only trims the child process it creates, never other applications.
internal static class MemoryBoostWindowsTests
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--child")
        {
            byte[] allocation = new byte[128 * 1024 * 1024];
            for (int index = 0; index < allocation.Length; index += 4096) allocation[index] = 1;
            File.WriteAllText(args[1] + ".ready", "ready");
            Stopwatch deadline = Stopwatch.StartNew();
            while (!File.Exists(args[1] + ".stop") && deadline.Elapsed.TotalSeconds < 30) Thread.Sleep(50);
            GC.KeepAlive(allocation);
            return 0;
        }
        string signal = Path.Combine(Path.GetTempPath(), "boost-test-" + Guid.NewGuid().ToString("N"));
        Process child = null;
        try
        {
            MemoryReading memory = MemoryBoost.ReadMemory();
            Check(memory != null && memory.UsedPercent >= 0 && memory.UsedPercent <= 100, "physical memory reading");
            using (Process current = Process.GetCurrentProcess())
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                Check(!MemoryBoost.TryTrimBackgroundProcess(current, current.Id, current.SessionId, identity.User.Value, CancellationToken.None), "self excluded");
                child = Process.Start(new ProcessStartInfo(current.MainModule.FileName, "--child \"" + signal + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                Stopwatch wait = Stopwatch.StartNew();
                while (!File.Exists(signal + ".ready") && !child.HasExited && wait.Elapsed.TotalSeconds < 10) Thread.Sleep(50);
                Check(File.Exists(signal + ".ready"), "owned child ready");
                Check(!MemoryBoost.TryTrimBackgroundProcess(child, current.Id, current.SessionId, "S-1-0-0", CancellationToken.None), "different user excluded");
                Check(!MemoryBoost.TryTrimBackgroundProcess(child, current.Id, current.SessionId + 1, identity.User.Value, CancellationToken.None), "different session excluded");
                Check(!MemoryBoost.TryTrimBackgroundProcess(child, current.Id, current.SessionId, identity.User.Value, new CancellationToken(true)), "cancelled operation excluded");
                child.Refresh();
                long before = child.WorkingSet64;
                Check(MemoryBoost.TryTrimBackgroundProcess(child, current.Id, current.SessionId, identity.User.Value, CancellationToken.None), "owned background child trimmed");
                child.Refresh();
                Check(!child.HasExited && child.WorkingSet64 < before, "child remains alive with lower working set");
                Console.WriteLine("PASS: Windows memory API and exclusions; owned child " + before + " -> " + child.WorkingSet64 + " bytes");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally
        {
            File.WriteAllText(signal + ".stop", "stop");
            if (child != null)
            {
                if (!child.WaitForExit(3000)) { child.Kill(); child.WaitForExit(); }
                child.Dispose();
            }
            foreach (string suffix in new[] { ".ready", ".stop" })
                if (File.Exists(signal + suffix)) File.Delete(signal + suffix);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
