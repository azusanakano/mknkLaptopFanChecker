using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mknk.LaptopFanChecker
{
    public enum TestPhase
    {
        Monitoring,
        Idle,
        Load,
        Cooldown,
        Complete,
        Aborted,
        SafetyStop
    }

    public enum VerdictLevel
    {
        Unknown,
        Normal,
        Caution,
        Suspect
    }

    public sealed class SystemProfile
    {
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string CpuName { get; set; }
        public double? CpuBaseSpeedMHz { get; set; }
        public string OperatingSystem { get; set; }

        public SystemProfile()
        {
            Manufacturer = "不明";
            Model = "不明";
            CpuName = "不明";
            CpuBaseSpeedMHz = null;
            OperatingSystem = Environment.OSVersion.VersionString;
        }

        public string DisplayName
        {
            get { return (Manufacturer + " " + Model).Trim(); }
        }

        public string CpuBaseSpeedText
        {
            get
            {
                if (!CpuBaseSpeedMHz.HasValue || CpuBaseSpeedMHz.Value <= 0.0)
                    return "取得不可";
                if (CpuBaseSpeedMHz.Value >= 1000.0)
                    return (CpuBaseSpeedMHz.Value / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " GHz";
                return CpuBaseSpeedMHz.Value.ToString("0", CultureInfo.InvariantCulture) + " MHz";
            }
        }
    }

    public sealed class SensorSnapshot
    {
        public DateTime Timestamp { get; set; }
        public double? TemperatureC { get; set; }
        public double? CpuLoadPercent { get; set; }
        public double? FanRpm { get; set; }
        public double? FanControlPercent { get; set; }
        public bool FanSensorAvailable { get; set; }
        public bool TemperatureIsCpuDirect { get; set; }
        public string TemperatureSensorName { get; set; }
        public string FanSensorName { get; set; }
        public string SensorSource { get; set; }
        public string Error { get; set; }
        public bool LowLevelDriverInstalled { get; set; }
        public bool LowLevelDriverLoaded { get; set; }
        public bool ProcessElevated { get; set; }
        public string LowLevelDriverVersion { get; set; }
        public string TemperatureFailureReason { get; set; }

        public SensorSnapshot()
        {
            Timestamp = DateTime.Now;
            TemperatureSensorName = "未検出";
            FanSensorName = "未検出";
            SensorSource = "未検出";
            Error = String.Empty;
            LowLevelDriverVersion = "未導入";
            TemperatureFailureReason = String.Empty;
        }
    }

    public sealed class TelemetrySample
    {
        public DateTime Timestamp { get; set; }
        public double ElapsedSeconds { get; set; }
        public TestPhase Phase { get; set; }
        public double? TemperatureC { get; set; }
        public double? CpuLoadPercent { get; set; }
        public double? FanRpm { get; set; }
        public double? FanControlPercent { get; set; }
        public bool FanSensorAvailable { get; set; }
        public string TemperatureSensorName { get; set; }
        public string FanSensorName { get; set; }
        public string SensorSource { get; set; }
    }

    public sealed class TestDurations
    {
        public int IdleSeconds { get; set; }
        public int LoadSeconds { get; set; }
        public int CooldownSeconds { get; set; }

        public int TotalSeconds
        {
            get { return IdleSeconds + LoadSeconds + CooldownSeconds; }
        }

        public static TestDurations Quick()
        {
            return new TestDurations { IdleSeconds = 10, LoadSeconds = 30, CooldownSeconds = 30 };
        }

        public static TestDurations Standard()
        {
            return new TestDurations { IdleSeconds = 20, LoadSeconds = 60, CooldownSeconds = 60 };
        }

        public static TestDurations Thorough()
        {
            return new TestDurations { IdleSeconds = 30, LoadSeconds = 120, CooldownSeconds = 120 };
        }
    }

    public sealed class EvaluationResult
    {
        public VerdictLevel Level { get; set; }
        public string Verdict { get; set; }
        public string Basis { get; set; }
        public string Limitation { get; set; }
        public string RecommendedAction { get; set; }
        public bool DirectRpmAssessment { get; set; }
        public bool CompletedAllPhases { get; set; }
        public double? BaselineTemperatureC { get; set; }
        public double? MaximumTemperatureC { get; set; }
        public double? CooldownEndTemperatureC { get; set; }
        public double? CooldownDropC { get; set; }
        public double? BaselineFanRpm { get; set; }
        public double? MaximumFanRpm { get; set; }
        public double? FanIncreaseRpm { get; set; }
        public double? LoadEndTemperatureSlopePerSecond { get; set; }
        public double SafetyCutoffC { get; set; }
        public List<string> Notes { get; private set; }

        public EvaluationResult()
        {
            Level = VerdictLevel.Unknown;
            Verdict = "未判定";
            Basis = "測定データがありません。";
            Limitation = "";
            RecommendedAction = "";
            Notes = new List<string>();
        }
    }

    public sealed class SavedReport
    {
        public string HtmlPath { get; set; }
        public string CsvPath { get; set; }
    }
}
