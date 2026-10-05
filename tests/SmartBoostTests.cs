using System;
using System.IO;
using Mknk.LaptopFanChecker;

internal static class SmartBoostTests
{
    public static void Run(Action<bool, string> assert)
    {
        DateTime now = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        SmartBoostPolicy usageAlone = new SmartBoostPolicy(null);
        for (int second = 0; second <= 35; second += 5)
            assert(!usageAlone.ShouldRun(now.AddSeconds(second), false, true, false), "high usage alone must not run without a Windows low-memory notification");
        assert(!usageAlone.LastAttemptUtc.HasValue, "normal Windows memory state does not persist an attempt");

        SmartBoostPolicy unknown = new SmartBoostPolicy(null);
        for (int second = 0; second <= 40; second += 5)
            assert(!unknown.ShouldRun(now.AddSeconds(second), null, true, false), "unknown Windows memory state does not trigger an automatic run");
        assert(!unknown.LastAttemptUtc.HasValue, "failed notification queries do not record an attempt");
        DateTime previousAttempt = now.AddMinutes(-31);
        SmartBoostPolicy unknownAfterAttempt = new SmartBoostPolicy(previousAttempt);
        assert(!unknownAfterAttempt.ShouldRun(now, null, true, false), "unknown state cannot run after cooldown expires");
        assert(unknownAfterAttempt.LastAttemptUtc == previousAttempt, "unknown state preserves the previous attempt time");

        SmartBoostPolicy policy = new SmartBoostPolicy(null);
        for (int second = 0; second < 30; second += 5)
            assert(!policy.ShouldRun(now.AddSeconds(second), true, true, false), "boost waits for a sustained Windows low-memory notification");
        assert(!policy.ShouldRun(now.AddSeconds(29), true, true, false), "29 seconds of low memory is too short");
        assert(policy.ShouldRun(now.AddSeconds(30), true, true, false), "boost triggers at 30 seconds of Windows low memory");
        assert(policy.LastAttemptUtc == now.AddSeconds(30), "boost attempt is persisted before execution");
        assert(!policy.ShouldRun(now.AddSeconds(35), true, true, false), "boost cooldown suppresses repeated work");
        SmartBoostPolicy restarted = new SmartBoostPolicy(policy.LastAttemptUtc);
        for (int second = 40; second <= 80; second += 5)
            assert(!restarted.ShouldRun(now.AddSeconds(second), true, true, false), "restart preserves cooldown");
        for (int second = 1800; second < 1830; second += 5)
            assert(!restarted.ShouldRun(now.AddSeconds(second), true, true, false), "cooldown and continuity both required");
        assert(restarted.ShouldRun(now.AddSeconds(1830), true, true, false), "boost resumes after cooldown");

        SmartBoostPolicy cooldownBoundary = new SmartBoostPolicy(now);
        for (int second = 1760; second <= 1790; second += 5)
            assert(!cooldownBoundary.ShouldRun(now.AddSeconds(second), true, true, false), "sustained low memory still waits for the full cooldown");
        assert(!cooldownBoundary.ShouldRun(now.AddSeconds(1799), true, true, false), "cooldown suppresses execution one second before expiry");
        assert(cooldownBoundary.LastAttemptUtc == now, "cooldown polling does not advance the previous attempt");
        assert(cooldownBoundary.ShouldRun(now.AddSeconds(1800), true, true, false), "sustained low memory runs at the exact 30-minute cooldown boundary");

        foreach (bool? reset in new bool?[] { false, null })
        {
            SmartBoostPolicy interrupted = new SmartBoostPolicy(null);
            for (int second = 0; second <= 20; second += 5)
                assert(!interrupted.ShouldRun(now.AddSeconds(second), true, true, false), "partial low-memory interval does not run");
            string interruption = reset.HasValue ? "recovery" : "failed query";
            assert(!interrupted.ShouldRun(now.AddSeconds(25), reset, true, false), interruption + " interrupts the low-memory interval");
            for (int second = 30; second < 60; second += 5)
                assert(!interrupted.ShouldRun(now.AddSeconds(second), true, true, false), interruption + " requires a fresh continuous interval");
            assert(!interrupted.LastAttemptUtc.HasValue, interruption + " does not record an attempt before the new interval completes");
            assert(interrupted.ShouldRun(now.AddSeconds(60), true, true, false), "30 seconds of low memory after " + interruption + " permits execution");
        }

        SmartBoostPolicy disabled = new SmartBoostPolicy(null);
        for (int second = 0; second <= 40; second += 5)
            assert(!disabled.ShouldRun(now.AddSeconds(second), true, false, false), "disabled boost is read-only");
        assert(!disabled.LastAttemptUtc.HasValue, "disabled boost does not record an attempt");
        for (int second = 45; second < 75; second += 5)
            assert(!disabled.ShouldRun(now.AddSeconds(second), true, true, false), "disabled samples do not count toward a later automatic interval");
        assert(disabled.ShouldRun(now.AddSeconds(75), true, true, false), "later enabled polling completes its own low-memory interval");

        SmartBoostPolicy testing = new SmartBoostPolicy(null);
        for (int second = 0; second <= 40; second += 5)
            assert(!testing.ShouldRun(now.AddSeconds(second), true, true, true), "test and demo suppress boost");
        assert(!testing.LastAttemptUtc.HasValue, "test and demo blocks do not record an attempt");
        for (int second = 45; second < 75; second += 5)
            assert(!testing.ShouldRun(now.AddSeconds(second), true, true, false), "unblocking starts a new low-memory interval");
        assert(testing.ShouldRun(now.AddSeconds(75), true, true, false), "automatic execution resumes after a fresh unblocked interval");

        SmartBoostPolicy blockedPartway = new SmartBoostPolicy(null);
        for (int second = 0; second <= 20; second += 5)
            assert(!blockedPartway.ShouldRun(now.AddSeconds(second), true, true, false), "partially accumulated low memory does not run");
        assert(!blockedPartway.ShouldRun(now.AddSeconds(25), true, true, true), "blocking interrupts a partially accumulated interval");
        for (int second = 30; second < 60; second += 5)
            assert(!blockedPartway.ShouldRun(now.AddSeconds(second), true, true, false), "partial interval before a block is not reused");
        assert(blockedPartway.ShouldRun(now.AddSeconds(60), true, true, false), "blocked partial interval requires 30 fresh unblocked seconds");

        SmartBoostPolicy allowedGap = new SmartBoostPolicy(null);
        assert(!allowedGap.ShouldRun(now, true, true, false), "first notification starts the interval");
        assert(!allowedGap.ShouldRun(now.AddSeconds(15), true, true, false), "15-second sample gap preserves a partial interval");
        assert(allowedGap.ShouldRun(now.AddSeconds(30), true, true, false), "15-second sample gaps preserve the complete interval");

        SmartBoostPolicy gap = new SmartBoostPolicy(null);
        assert(!gap.ShouldRun(now, true, true, false), "sampling-gap case starts without an attempt");
        assert(!gap.ShouldRun(now.AddMinutes(2), true, true, false), "sleep or sampling gap does not trigger boost");
        assert(!gap.LastAttemptUtc.HasValue, "sleep or sampling gap does not record an attempt");
        for (int second = 125; second < 150; second += 5)
            assert(!gap.ShouldRun(now.AddSeconds(second), true, true, false), "sampling gap starts a fresh interval");
        assert(gap.ShouldRun(now.AddSeconds(150), true, true, false), "fresh interval after a sampling gap permits execution");

        SmartBoostPolicy excessiveGap = new SmartBoostPolicy(null);
        assert(!excessiveGap.ShouldRun(now, true, true, false), "excessive-gap case starts a low-memory interval");
        assert(!excessiveGap.ShouldRun(now.AddSeconds(15), true, true, false), "excessive-gap case accumulates 15 seconds");
        assert(!excessiveGap.ShouldRun(now.AddSeconds(31), true, true, false), "a 16-second sample gap resets an otherwise complete interval");
        for (int second = 36; second < 61; second += 5)
            assert(!excessiveGap.ShouldRun(now.AddSeconds(second), true, true, false), "gap beyond 15 seconds requires fresh sustained low memory");
        assert(excessiveGap.ShouldRun(now.AddSeconds(61), true, true, false), "30 fresh seconds after an excessive gap permits execution");

        SmartBoostPolicy backwardClock = new SmartBoostPolicy(null);
        for (int second = 0; second <= 20; second += 10)
            assert(!backwardClock.ShouldRun(now.AddSeconds(second), true, true, false), "backward-clock case starts a partial interval");
        assert(!backwardClock.ShouldRun(now.AddSeconds(15), true, true, false), "backward clock resets the low-memory interval");
        for (int second = 20; second < 45; second += 5)
            assert(!backwardClock.ShouldRun(now.AddSeconds(second), true, true, false), "backward clock requires a fresh interval");
        assert(backwardClock.ShouldRun(now.AddSeconds(45), true, true, false), "automatic execution resumes 30 seconds after the backward clock sample");

        SmartBoostPolicy enabledNow = new SmartBoostPolicy(now.AddMinutes(-1));
        enabledNow.OnEnabledChanged(true);
        assert(enabledNow.ShouldRun(now, false, true, false), "switching on runs immediately with normal memory and during cooldown");
        assert(enabledNow.LastAttemptUtc == now, "immediate run becomes the new cooldown origin");
        assert(!enabledNow.ShouldRun(now.AddSeconds(1), false, true, false), "switching on requests exactly one immediate run");
        for (int second = 5; second <= 40; second += 5)
            assert(!enabledNow.ShouldRun(now.AddSeconds(second), true, true, false), "automatic execution respects cooldown after immediate run");
        enabledNow.OnEnabledChanged(false);
        enabledNow.OnEnabledChanged(true);
        assert(enabledNow.ShouldRun(now.AddSeconds(45), null, true, false), "explicit off-on runs again without requiring memory reading");
        assert(enabledNow.LastAttemptUtc == now.AddSeconds(45), "immediate run with an unknown notification restarts cooldown");
        assert(!enabledNow.ShouldRun(now.AddSeconds(46), null, true, false), "unknown notification does not repeat the immediate run");

        SmartBoostPolicy deferred = new SmartBoostPolicy(null);
        deferred.OnEnabledChanged(true);
        assert(!deferred.ShouldRun(now, null, true, true), "immediate request waits while test or boost is active");
        assert(!deferred.LastAttemptUtc.HasValue, "deferred immediate request does not record an attempt while blocked");
        assert(deferred.ShouldRun(now.AddSeconds(1), false, true, false), "deferred request runs once the block clears");
        assert(!deferred.ShouldRun(now.AddSeconds(2), false, true, false), "deferred request runs only once");
        deferred.OnEnabledChanged(true);
        deferred.OnEnabledChanged(false);
        assert(!deferred.ShouldRun(now.AddSeconds(3), false, false, false), "off cancels deferred request");
        assert(!deferred.ShouldRun(now.AddSeconds(4), false, true, false), "cancelled request does not survive later polling");

        SmartBoostPolicy savedOn = new SmartBoostPolicy(null);
        for (int second = 0; second < 30; second += 5)
            assert(!savedOn.ShouldRun(now.AddSeconds(second), true, true, false), "loading saved on state waits for sustained low memory");
        assert(savedOn.ShouldRun(now.AddSeconds(30), true, true, false), "saved on state permits automatic execution after the interval");
        assert(!new SmartBoostPolicy(null).ShouldRun(now, false, true, false), "loading saved on state does not request an immediate run");

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
