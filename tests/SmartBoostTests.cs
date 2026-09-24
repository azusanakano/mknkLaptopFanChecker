using System;
using System.IO;
using Mknk.LaptopFanChecker;

internal static class SmartBoostTests
{
    public static void Run(Action<bool, string> assert)
    {
        DateTime now = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        SmartBoostPolicy policy = new SmartBoostPolicy(null);
        for (int second = 0; second < 30; second += 5)
            assert(!policy.ShouldRun(now.AddSeconds(second), 85, true, false), "boost waits for sustained pressure");
        assert(policy.ShouldRun(now.AddSeconds(30), 85, true, false), "boost triggers at 30 seconds");
        assert(policy.LastAttemptUtc == now.AddSeconds(30), "boost attempt is persisted before execution");
        assert(!policy.ShouldRun(now.AddSeconds(35), 99, true, false), "boost cooldown suppresses repeated work");
        SmartBoostPolicy restarted = new SmartBoostPolicy(policy.LastAttemptUtc);
        for (int second = 40; second <= 80; second += 5)
            assert(!restarted.ShouldRun(now.AddSeconds(second), 99, true, false), "restart preserves cooldown");
        for (int second = 1800; second < 1830; second += 5)
            assert(!restarted.ShouldRun(now.AddSeconds(second), 90, true, false), "cooldown and continuity both required");
        assert(restarted.ShouldRun(now.AddSeconds(1830), 90, true, false), "boost resumes after cooldown");
        foreach (double? reset in new double?[] { 84.9, null, Double.NaN, Double.PositiveInfinity, 101 })
        {
            SmartBoostPolicy interrupted = new SmartBoostPolicy(null);
            interrupted.ShouldRun(now, 90, true, false);
            interrupted.ShouldRun(now.AddSeconds(5), reset, true, false);
            assert(!interrupted.ShouldRun(now.AddSeconds(30), 90, true, false), "invalid or low memory resets continuity");
        }
        SmartBoostPolicy disabled = new SmartBoostPolicy(null);
        for (int second = 0; second <= 40; second += 5)
            assert(!disabled.ShouldRun(now.AddSeconds(second), 90, false, false), "disabled boost is read-only");
        SmartBoostPolicy testing = new SmartBoostPolicy(null);
        for (int second = 0; second <= 40; second += 5)
            assert(!testing.ShouldRun(now.AddSeconds(second), 90, true, true), "test and demo suppress boost");
        assert(!testing.ShouldRun(now.AddSeconds(45), 90, true, false), "unblocking starts a new pressure interval");
        SmartBoostPolicy gap = new SmartBoostPolicy(null);
        gap.ShouldRun(now, 90, true, false);
        assert(!gap.ShouldRun(now.AddMinutes(2), 90, true, false), "sleep or sampling gap does not trigger boost");

        SmartBoostPolicy enabledNow = new SmartBoostPolicy(now.AddMinutes(-1));
        enabledNow.OnEnabledChanged(true);
        assert(enabledNow.ShouldRun(now, 40, true, false), "switching on runs immediately below threshold and during cooldown");
        assert(enabledNow.LastAttemptUtc == now, "immediate run becomes the new cooldown origin");
        assert(!enabledNow.ShouldRun(now.AddSeconds(1), 40, true, false), "switching on requests exactly one immediate run");
        for (int second = 5; second <= 40; second += 5)
            assert(!enabledNow.ShouldRun(now.AddSeconds(second), 99, true, false), "automatic execution respects cooldown after immediate run");
        enabledNow.OnEnabledChanged(false);
        enabledNow.OnEnabledChanged(true);
        assert(enabledNow.ShouldRun(now.AddSeconds(45), null, true, false), "explicit off-on runs again without requiring memory reading");

        SmartBoostPolicy deferred = new SmartBoostPolicy(null);
        deferred.OnEnabledChanged(true);
        assert(!deferred.ShouldRun(now, 40, true, true), "immediate request waits while test or boost is active");
        assert(deferred.ShouldRun(now.AddSeconds(1), 40, true, false), "deferred request runs once the block clears");
        deferred.OnEnabledChanged(true);
        deferred.OnEnabledChanged(false);
        assert(!deferred.ShouldRun(now.AddSeconds(2), 40, false, false), "off cancels deferred request");
        assert(!deferred.ShouldRun(now.AddSeconds(3), 40, true, false), "cancelled request does not survive later polling");
        assert(!new SmartBoostPolicy(null).ShouldRun(now, 40, true, false), "loading saved on state does not request an immediate run");

        string path = Path.Combine(Path.GetTempPath(), "smart-boost-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, "HeatMonitoringEnabled=False\r\nAbsoluteAlertC=95\r\n");
            AppSettings legacy = AppSettings.Load(path);
            assert(!legacy.SmartBoostEnabled && !legacy.LastSmartBoostAttemptUtc.HasValue && legacy.AbsoluteAlertC == 95, "old settings keep boost off and preserve temperature settings");
            legacy.SmartBoostEnabled = true;
            legacy.LastSmartBoostAttemptUtc = now;
            legacy.Save(path);
            AppSettings restored = AppSettings.Load(path);
            assert(restored.SmartBoostEnabled && restored.LastSmartBoostAttemptUtc == now && !restored.HeatMonitoringEnabled, "boost enablement and UTC cooldown survive settings round trip");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
