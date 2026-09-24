using System;
using System.Collections.Generic;
using System.IO;
using Mknk.LaptopFanChecker;

internal static class EvaluatorTests
{
    private static int _passed;

    private static int Main()
    {
        try
        {
            DirectRpmNormal();
            ZeroRpmHotIsSuspect();
            IndirectCoolingIsNormalTrend();
            SafetyStopIsSuspect();
            AbnormalNoiseIsSuspect();
            AbortedTestIsUnknown();
            AbsoluteHeatAlert();
            RepeatedAbsoluteAlertIsSuppressed();
            SustainedHeatAlert();
            IdleHighAlert();
            RapidRiseAlert();
            CpuBaseSpeedFormatting();
            CpuClockSelection();
            CpuTopologyCounts();
            SmartBoostTests.Run(Assert);
            CoolingCompatibilityTests.Run(Assert);
            DesktopCoolingEvaluation();
            StartupRunCommandUsesNativeLauncher();
            StartupTaskUsesElevatedManagedMonitor();
            MissingTemperatureDriverDiagnostics();
            ReportFilesAreGenerated();
            Console.WriteLine("PASS: " + _passed + " tests");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
    }

    private static void DirectRpmNormal()
    {
        List<TelemetrySample> samples = BuildTest(true, 50, 82, 52, 0, 3400);
        EvaluationResult result = FanTestEvaluator.Evaluate(samples, 92, true, false, TestPhase.Complete);
        Assert(result.Level == VerdictLevel.Normal, "direct RPM normal");
    }

    private static void ZeroRpmHotIsSuspect()
    {
        List<TelemetrySample> samples = BuildTest(true, 48, 88, 86, 0, 0);
        EvaluationResult result = FanTestEvaluator.Evaluate(samples, 95, false, false, TestPhase.Complete);
        Assert(result.Level == VerdictLevel.Suspect, "zero RPM hot");
    }

    private static void IndirectCoolingIsNormalTrend()
    {
        List<TelemetrySample> samples = BuildTest(false, 48, 82, 60, null, null);
        EvaluationResult result = FanTestEvaluator.Evaluate(samples, 92, true, false, TestPhase.Complete);
        Assert(result.Level == VerdictLevel.Normal && !result.DirectRpmAssessment, "indirect cooling");
    }

    private static void SafetyStopIsSuspect()
    {
        List<TelemetrySample> samples = BuildTest(false, 50, 93, 91, null, null);
        EvaluationResult result = FanTestEvaluator.Evaluate(samples, 92, false, false, TestPhase.SafetyStop);
        Assert(result.Level == VerdictLevel.Suspect, "safety stop");
    }

    private static void AbnormalNoiseIsSuspect()
    {
        List<TelemetrySample> samples = BuildTest(true, 49, 78, 56, 500, 3100);
        EvaluationResult result = FanTestEvaluator.Evaluate(samples, 92, true, true, TestPhase.Complete);
        Assert(result.Level == VerdictLevel.Suspect, "abnormal noise");
    }

    private static void AbortedTestIsUnknown()
    {
        List<TelemetrySample> samples = BuildTest(true, 49, 78, 56, 500, 3100);
        EvaluationResult result = FanTestEvaluator.Evaluate(samples, 92, true, false, TestPhase.Aborted);
        Assert(result.Level == VerdictLevel.Unknown, "aborted test");
    }

    private static void AbsoluteHeatAlert()
    {
        AppSettings settings = new AppSettings();
        HeatAnomalyDetector detector = new HeatAnomalyDetector();
        SensorSnapshot current = Snapshot(DateTime.Now, 93, 20, 3000);
        HeatAlert alert = detector.Check(new List<TelemetrySample>(), current, settings);
        Assert(alert != null && alert.Kind == HeatAlertKind.AbsoluteHigh && alert.Critical, "absolute heat alert");
    }

    private static void RepeatedAbsoluteAlertIsSuppressed()
    {
        AppSettings settings = new AppSettings();
        HeatAnomalyDetector detector = new HeatAnomalyDetector();
        DateTime now = DateTime.Now;
        HeatAlert first = detector.Check(new List<TelemetrySample>(), Snapshot(now, 94, 20, 3200), settings);
        HeatAlert second = detector.Check(new List<TelemetrySample>(), Snapshot(now.AddSeconds(1), 94, 20, 3200), settings);
        Assert(first != null && second == null, "absolute alert suppression");
    }

    private static void SustainedHeatAlert()
    {
        AppSettings settings = new AppSettings { AbsoluteAlertC = 100 };
        HeatAnomalyDetector detector = new HeatAnomalyDetector();
        DateTime start = DateTime.Now.AddSeconds(-35);
        List<TelemetrySample> history = new List<TelemetrySample>();
        for (int i = 0; i <= 35; i++) history.Add(MonitorSample(start.AddSeconds(i), 87, 65));
        SensorSnapshot current = Snapshot(start.AddSeconds(35), 87, 65, 3500);
        HeatAlert alert = detector.Check(history, current, settings);
        Assert(alert != null && alert.Kind == HeatAlertKind.SustainedHigh, "sustained heat alert");
    }

    private static void IdleHighAlert()
    {
        AppSettings settings = new AppSettings { AbsoluteAlertC = 100, SustainedAlertC = 95 };
        HeatAnomalyDetector detector = new HeatAnomalyDetector();
        DateTime start = DateTime.Now.AddSeconds(-65);
        List<TelemetrySample> history = new List<TelemetrySample>();
        for (int i = 0; i <= 65; i++) history.Add(MonitorSample(start.AddSeconds(i), 82, 8));
        SensorSnapshot current = Snapshot(start.AddSeconds(65), 82, 8, 0);
        HeatAlert alert = detector.Check(history, current, settings);
        Assert(alert != null && alert.Kind == HeatAlertKind.IdleHigh, "idle high alert");
    }

    private static void RapidRiseAlert()
    {
        AppSettings settings = new AppSettings { AbsoluteAlertC = 100, SustainedAlertC = 95, IdleHighAlertC = 95 };
        HeatAnomalyDetector detector = new HeatAnomalyDetector();
        DateTime start = DateTime.Now.AddSeconds(-30);
        List<TelemetrySample> history = new List<TelemetrySample>();
        for (int i = 0; i <= 30; i++) history.Add(MonitorSample(start.AddSeconds(i), 58 + i * 0.7, 70));
        SensorSnapshot current = Snapshot(start.AddSeconds(30), 79, 70, 2800);
        HeatAlert alert = detector.Check(history, current, settings);
        Assert(alert != null && alert.Kind == HeatAlertKind.RapidRise, "rapid rise alert");
    }

    private static void ReportFilesAreGenerated()
    {
        List<TelemetrySample> samples = BuildTest(true, 49, 80, 55, 400, 3200);
        samples[0].CpuClockGHz = 0.8;
        samples[1].CpuClockGHz = 3.456;
        EvaluationResult result = FanTestEvaluator.Evaluate(samples, 92, true, false, TestPhase.Complete);
        SystemProfile profile = new SystemProfile { Manufacturer = "MKNK", Model = "Test Laptop", CpuName = "Test CPU", CpuBaseSpeedMHz = 2800.0,
            CpuSocketCount = 2, CpuCoreCount = 24, CpuLogicalProcessorCount = 48 };
        SavedReport saved = ReportWriter.Save(profile, samples, result, true, false, "CPU Package / CPU Fan");
        bool valid = File.Exists(saved.HtmlPath) && File.Exists(saved.CsvPath) &&
            File.ReadAllText(saved.HtmlPath).Contains("正常傾向") &&
            File.ReadAllText(saved.HtmlPath).Contains("2.80 GHz") &&
            File.ReadAllText(saved.CsvPath).Contains("cpu_temperature_c") &&
            File.ReadAllText(saved.CsvPath).Contains("cpu_clock_ghz") &&
            File.ReadAllText(saved.HtmlPath).Contains("CPU実働速度") &&
            File.ReadAllText(saved.HtmlPath).Contains("0.80 GHz") &&
            File.ReadAllText(saved.HtmlPath).Contains("3.46 GHz") &&
            File.ReadAllText(saved.HtmlPath).Contains("取得不可") &&
            File.ReadAllText(saved.HtmlPath).Contains("ソケット</b><span>2</span>") &&
            File.ReadAllText(saved.HtmlPath).Contains("コア</b><span>24</span>") &&
            File.ReadAllText(saved.HtmlPath).Contains("論理プロセッサ数</b><span>48</span>");
        string[] csv = File.ReadAllLines(saved.CsvPath);
        valid = valid && csv[1].EndsWith(",\"0.80\"") && csv[2].EndsWith(",\"3.46\"") && csv[3].EndsWith(",\"\"");
        try { File.Delete(saved.HtmlPath); File.Delete(saved.CsvPath); }
        catch { }
        Assert(valid, "report generation");
    }

    private static void CpuBaseSpeedFormatting()
    {
        SystemProfile ghz = new SystemProfile { CpuBaseSpeedMHz = 2800.0 };
        SystemProfile mhz = new SystemProfile { CpuBaseSpeedMHz = 800.0 };
        SystemProfile unavailable = new SystemProfile();
        Assert(ghz.CpuBaseSpeedText == "2.80 GHz" &&
            mhz.CpuBaseSpeedText == "800 MHz" &&
            unavailable.CpuBaseSpeedText == "取得不可", "CPU base speed formatting");
    }

    private static void DesktopCoolingEvaluation()
    {
        List<TelemetrySample> quiet = BuildTest(true, 25, 40, 26, 250, 250);
        Assert(FanTestEvaluator.Evaluate(quiet, 92, false, false, TestPhase.Complete).Level == VerdictLevel.Normal, "quiet desktop fan below 300 RPM is rotating");
        List<TelemetrySample> passive = BuildTest(true, 25, 40, 26, 0, 0);
        Assert(FanTestEvaluator.Evaluate(passive, 92, false, false, TestPhase.Complete).Level == VerdictLevel.Caution, "low-temperature zero RPM is inconclusive, not failure");
        foreach (TelemetrySample sample in quiet)
            if (sample.Phase == TestPhase.Load) { sample.FanRpm = null; sample.FanSensorAvailable = false; }
        Assert(!FanTestEvaluator.Evaluate(quiet, 92, false, false, TestPhase.Complete).DirectRpmAssessment, "missing load RPM cannot become zero RPM");
        Assert(FanTestEvaluator.Evaluate(quiet, 92, false, false, TestPhase.SensorLost).Level == VerdictLevel.Unknown, "temperature loss is a stopped, inconclusive test");
        Assert(!new AppSettings().SmartBoostEnabled, "memory boost defaults off for old and new settings");
    }

    private static void CpuClockSelection()
    {
        List<SensorRecord> clocks = new List<SensorRecord>
        {
            Clock("CPU Core #1 (P-Core)", 4200), Clock("CPU Core #2 (E-Core)", 1800),
            Clock("Bus Speed", 100), Clock("Ring/LLC Clock", 3900),
            Clock("Core #1 (Effective)", 450), Clock("Cores (Average Effective)", 300),
            Clock("Cores (Average)", 2700), Clock("CPU Core #3", null),
            Clock("CPU Core #4", Double.NaN), Clock("CPU Core #5", Double.PositiveInfinity),
            Clock("CPU Core #6", -10), Clock("CPU Core #7", 0), Clock("CPU Core #8", 10001),
            new SensorRecord { HardwareType = "GpuIntel", SensorType = "Clock", SensorName = "CPU Core #1", Value = 900 },
            new SensorRecord { HardwareType = "Cpu", SensorType = "Load", SensorName = "CPU Core #1", Value = 80 }
        };
        Assert(CpuClockMetrics.SelectGHz(clocks) == 3.0, "hybrid CPU average excludes unrelated and invalid clocks");
        Assert(CpuClockMetrics.SelectGHz(new[] { Clock("P-Core #1", 4200), Clock("E-Core #2", 1800) }) == 3.0, "LibreHardwareMonitor hybrid Intel sensor names");
        Assert(CpuClockMetrics.SelectGHz(new[] { Clock("Core #1", 3600), Clock("Core #2", 4000) }) == 3.8, "AMD core clock average");
        Assert(CpuClockMetrics.SelectGHz(new[] { Clock("CPU Core", 800) }) == 0.8, "single core sub-GHz speed");
        Assert(CpuClockMetrics.SelectGHz(new[] { Clock("Cores (Average)", 2800), Clock("Bus Speed", 100) }) == 2.8, "aggregate clock fallback");
        Assert(!CpuClockMetrics.SelectGHz(new[] { Clock("Bus Speed", 100), Clock("Core #1 (Effective)", 500) }).HasValue &&
            !CpuClockMetrics.SelectGHz(new SensorRecord[0]).HasValue, "missing core clock remains unavailable");
        Assert(CpuClockMetrics.FormatGHz(0.8) == "0.80 GHz" && CpuClockMetrics.FormatGHz(null) == "取得不可", "live speed GHz formatting");
    }

    private static SensorRecord Clock(string name, double? mhz)
    {
        return new SensorRecord { HardwareType = "Cpu", HardwareName = "Test CPU", Path = "Test CPU", SensorType = "Clock", SensorName = name, Value = mhz };
    }

    private static void CpuTopologyCounts()
    {
        byte[] smtCore = TopologyRecord(0, 48);
        smtCore[8] = 1; // LTP_PC_SMT: two logical processors still represent one core.
        smtCore[30] = 1; // GroupCount.
        smtCore[32] = 3; // Affinity mask for two logical processors.
        byte[] efficiencyCore = TopologyRecord(0, 48);
        efficiencyCore[9] = 1; // Different EfficiencyClass, still one physical core.
        List<byte> records = new List<byte>();
        records.AddRange(TopologyRecord(3, 64)); // One socket spans two groups.
        records.AddRange(smtCore);
        records.AddRange(efficiencyCore);
        records.AddRange(TopologyRecord(3, 48));
        records.AddRange(TopologyRecord(2, 80)); // Cache records do not count as cores.
        Assert(CpuTopologyReader.CountRecords(records.ToArray(), 3) == 2, "socket count uses packages, not groups");
        Assert(CpuTopologyReader.CountRecords(records.ToArray(), 0) == 2, "SMT and hybrid cores count once each");
        records.Clear();
        for (int i = 0; i < 144; i++) records.AddRange(TopologyRecord(0, 48));
        Assert(CpuTopologyReader.CountRecords(records.ToArray(), 0) == 144, "core count is not limited to 64");
        Assert(!CpuTopologyReader.CountRecords(new byte[0], 0).HasValue &&
            !CpuTopologyReader.CountRecords(TopologyRecord(2, 80), 0).HasValue, "missing topology is unavailable");
        Assert(!CpuTopologyReader.CountRecords(new byte[7], 0).HasValue &&
            !CpuTopologyReader.CountRecords(new byte[8], 0).HasValue, "truncated and zero-sized topology records rejected");
        byte[] oversized = TopologyRecord(0, 48);
        Array.Copy(BitConverter.GetBytes(UInt32.MaxValue), 0, oversized, 4, 4);
        Assert(!CpuTopologyReader.CountRecords(oversized, 0).HasValue, "oversized topology record rejected");
        SystemProfile profile = new SystemProfile { CpuSocketCount = 2, CpuCoreCount = 24, CpuLogicalProcessorCount = 48 };
        Assert(profile.CpuTopologyText == "ソケット: 2    コア: 24    論理プロセッサ数: 48", "topology labels keep physical and logical counts distinct");
        Assert(new SystemProfile().CpuSocketCountText == "取得不可" &&
            new SystemProfile { CpuCoreCount = 0 }.CpuCoreCountText == "取得不可", "unknown counts do not invent a value");
    }

    private static byte[] TopologyRecord(int relationship, int size)
    {
        byte[] data = new byte[size];
        Array.Copy(BitConverter.GetBytes(relationship), 0, data, 0, 4);
        Array.Copy(BitConverter.GetBytes(size), 0, data, 4, 4);
        return data;
    }

    private static void StartupRunCommandUsesNativeLauncher()
    {
        string root = Path.Combine(Path.GetTempPath(), "ネコシステム & PC " + Guid.NewGuid().ToString("N"));
        string runtime = Path.Combine(root, "runtime");
        string nativeLauncher = Path.Combine(root, "mknkLaptopFanChecker.exe");
        string managedExecutable = Path.Combine(runtime, "mknkLaptopFanChecker.exe");
        try
        {
            Directory.CreateDirectory(runtime);
            File.WriteAllText(nativeLauncher, String.Empty);
            File.WriteAllText(managedExecutable, String.Empty);
            string resolved = StartupTaskManager.ResolveStartupExecutablePath(managedExecutable);
            string command = StartupTaskManager.BuildRunCommand(resolved);
            Assert(resolved == nativeLauncher &&
                command == "\"" + nativeLauncher + "\" --start-monitor-task" &&
                command.Contains("ネコシステム & PC"), "startup Run command");
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch { }
        }
    }

    private static void StartupTaskUsesElevatedManagedMonitor()
    {
        string executable = Path.Combine(Path.GetTempPath(), "ネコシステム & PC", "runtime", "mknkLaptopFanChecker.exe");
        string xml = StartupTaskManager.BuildTaskXml(executable, "S-1-5-21-3939");
        Assert(xml.Contains("HighestAvailable") &&
            xml.Contains("--monitor") &&
            xml.Contains("ExecutionTimeLimit") &&
            xml.Contains("ネコシステム &amp; PC") &&
            xml.Contains("InteractiveToken"), "elevated startup task XML");
    }

    private static void MissingTemperatureDriverDiagnostics()
    {
        Version required = new Version(2, 2, 0, 0);
        Assert(SensorDiagnostics.ExplainMissingTemperature(null, required, false, true, 0).Contains("未導入"), "missing PawnIO diagnosis");
        Assert(SensorDiagnostics.ExplainMissingTemperature(new Version(2, 1, 0, 0), required, false, true, 0).Contains("古い"), "old PawnIO diagnosis");
        Assert(SensorDiagnostics.ExplainMissingTemperature(required, required, false, false, 0).Contains("管理者権限"), "non-elevated diagnosis");
        Assert(SensorDiagnostics.ExplainMissingTemperature(required, required, false, true, 0).Contains("再起動"), "unloaded PawnIO diagnosis");
        Assert(SensorDiagnostics.ExplainMissingTemperature(required, required, true, true, 2).Contains("値を読めません"), "unreadable CPU sensor diagnosis");
        Assert(SensorDiagnostics.ExplainMissingTemperature(required, required, true, true, 0).Contains("列挙できません"), "unsupported CPU sensor diagnosis");
    }

    private static List<TelemetrySample> BuildTest(bool fanAvailable, double idleTemp, double maxTemp, double coolEnd, double? idleFan, double? maxFan)
    {
        List<TelemetrySample> samples = new List<TelemetrySample>();
        DateTime start = DateTime.Now;
        int second = 0;
        for (int i = 0; i < 20; i++, second++)
            samples.Add(TestSample(start.AddSeconds(second), second, TestPhase.Idle, idleTemp, 8, idleFan, fanAvailable));
        for (int i = 0; i < 60; i++, second++)
        {
            double ratio = i / 59.0;
            double temp = idleTemp + (maxTemp - idleTemp) * ratio;
            double? fan = idleFan.HasValue && maxFan.HasValue ? idleFan.Value + (maxFan.Value - idleFan.Value) * ratio : maxFan;
            samples.Add(TestSample(start.AddSeconds(second), second, TestPhase.Load, temp, 82, fan, fanAvailable));
        }
        for (int i = 0; i < 60; i++, second++)
        {
            double ratio = i / 59.0;
            double temp = maxTemp + (coolEnd - maxTemp) * ratio;
            double? fan = maxFan.HasValue ? (double?)Math.Max(0, maxFan.Value * (1.0 - ratio * 0.8)) : null;
            samples.Add(TestSample(start.AddSeconds(second), second, TestPhase.Cooldown, temp, 7, fan, fanAvailable));
        }
        return samples;
    }

    private static TelemetrySample TestSample(DateTime time, double elapsed, TestPhase phase, double temp, double load, double? fan, bool available)
    {
        return new TelemetrySample { Timestamp = time, ElapsedSeconds = elapsed, Phase = phase, TemperatureC = temp, CpuLoadPercent = load, FanRpm = fan, FanSensorAvailable = available };
    }

    private static TelemetrySample MonitorSample(DateTime time, double temp, double load)
    {
        return new TelemetrySample { Timestamp = time, ElapsedSeconds = 0, Phase = TestPhase.Monitoring, TemperatureC = temp, CpuLoadPercent = load };
    }

    private static SensorSnapshot Snapshot(DateTime time, double temp, double load, double fan)
    {
        return new SensorSnapshot { Timestamp = time, TemperatureC = temp, CpuLoadPercent = load, FanRpm = fan, FanSensorAvailable = true };
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _passed++;
    }
}
