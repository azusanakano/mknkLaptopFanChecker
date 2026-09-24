using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace Mknk.LaptopFanChecker
{
    public interface ISensorReader : IDisposable
    {
        SystemProfile Profile { get; }
        SensorSnapshot Read();
        string GetRawSensorReport();
        string Reload();
        void SetTestPhase(TestPhase phase);
    }

    public sealed class LibreHardwareSensorReader : ISensorReader
    {
        private const int ProcessorInformationLevel = 11;

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessorPowerInformation
        {
            public uint Number;
            public uint MaxMhz;
            public uint CurrentMhz;
            public uint MhzLimit;
            public uint MaxIdleState;
            public uint CurrentIdleState;
        }

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint CallNtPowerInformation(
            int informationLevel,
            IntPtr inputBuffer,
            uint inputBufferLength,
            [Out] ProcessorPowerInformation[] outputBuffer,
            uint outputBufferLength);

        private readonly object _sync = new object();
        private readonly Computer _computer;
        private bool _opened;
        private string _openError;
        private string _lastTemperatureFailureReason;
        private List<SensorRecord> _lastRecords;
        private readonly CoolingSensorSelection _coolingSensors = new CoolingSensorSelection();

        public SystemProfile Profile { get; private set; }

        public LibreHardwareSensorReader()
        {
            Profile = LoadSystemProfile();
            _lastRecords = new List<SensorRecord>();
            _openError = String.Empty;
            _lastTemperatureFailureReason = String.Empty;
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsMemoryEnabled = false,
                IsGpuEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
                IsBatteryEnabled = false,
                IsPsuEnabled = false
            };

            Reload();
        }

        public SensorSnapshot Read()
        {
            lock (_sync)
            {
                SensorSnapshot snapshot = new SensorSnapshot();
                snapshot.Timestamp = DateTime.Now;
                Version installedDriver = SensorDriverManager.InstalledVersion();
                snapshot.LowLevelDriverInstalled = installedDriver != null;
                snapshot.LowLevelDriverVersion = installedDriver == null ? "未導入" : installedDriver.ToString();
                snapshot.LowLevelDriverLoaded = SensorDriverManager.IsLoaded();
                snapshot.ProcessElevated = SensorDriverManager.IsProcessElevated();
                snapshot.SensorSource = _opened
                    ? (snapshot.LowLevelDriverLoaded ? "LibreHardwareMonitor + PawnIO" : "LibreHardwareMonitor（PawnIO未接続）")
                    : "Windows WMI（限定）";
                snapshot.Error = _openError;

                List<SensorRecord> records = new List<SensorRecord>();
                if (_opened)
                {
                    try
                    {
                        _computer.Accept(new UpdateVisitor());
                        foreach (IHardware hardware in _computer.Hardware)
                            CollectHardware(hardware, hardware.Name, records);
                    }
                    catch (Exception ex)
                    {
                        snapshot.Error = AppendError(snapshot.Error, "センサー更新: " + ex.Message);
                    }
                }
                _lastRecords = records;

                CoolingSensorSelection.SelectTemperature(records, snapshot);
                CoolingSensorSelection.SelectCpuLoad(records, snapshot);
                snapshot.CpuClockGHz = CpuClockMetrics.SelectGHz(records);
                _coolingSensors.SelectFan(records, snapshot);

                if (!snapshot.TemperatureC.HasValue)
                {
                    double? acpi = ReadAcpiTemperature();
                    if (acpi.HasValue)
                    {
                        snapshot.TemperatureC = acpi;
                        snapshot.TemperatureSensorName = "ACPI Thermal Zone（CPU直結とは限らない）";
                        snapshot.TemperatureIsCpuDirect = false;
                        snapshot.SensorSource = _opened ? "LibreHardwareMonitor + ACPI補助" : "Windows ACPI";
                    }
                }

                if (!snapshot.TemperatureC.HasValue)
                {
                    int cpuTemperatureSensorCount = records.Count(r =>
                        EqualsIgnoreCase(r.SensorType, "Temperature") &&
                        EqualsIgnoreCase(r.HardwareType, "Cpu"));
                    snapshot.TemperatureFailureReason = SensorDiagnostics.ExplainMissingTemperature(
                        installedDriver,
                        SensorDriverManager.MinimumSupportedVersion,
                        snapshot.LowLevelDriverLoaded,
                        snapshot.ProcessElevated,
                        cpuTemperatureSensorCount);
                    snapshot.TemperatureSensorName = snapshot.TemperatureFailureReason;
                    snapshot.Error = AppendError(snapshot.Error, snapshot.TemperatureFailureReason);
                    _lastTemperatureFailureReason = snapshot.TemperatureFailureReason;
                }
                else
                {
                    _lastTemperatureFailureReason = String.Empty;
                }

                if (!snapshot.CpuLoadPercent.HasValue)
                    snapshot.CpuLoadPercent = ReadWmiCpuLoad();

                return snapshot;
            }
        }

        public string GetRawSensorReport()
        {
            lock (_sync)
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("ネコシステム社 CPUファンチェッカー - センサー一覧");
                builder.AppendLine("取得日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                builder.AppendLine("PC: " + Profile.DisplayName);
                builder.AppendLine("CPU: " + Profile.CpuName);
                builder.AppendLine("CPU構成（Windows）: " + Profile.CpuTopologyText);
                builder.AppendLine("CPU基本速度（Windows ProcessorInformation）: " + Profile.CpuBaseSpeedText);
                builder.AppendLine("CPU実働速度（コア平均）: " + CpuClockMetrics.FormatGHz(CpuClockMetrics.SelectGHz(_lastRecords)));
                Version installedDriver = SensorDriverManager.InstalledVersion();
                builder.AppendLine("管理者権限: " + (SensorDriverManager.IsProcessElevated() ? "あり" : "なし"));
                builder.AppendLine("PawnIO必要版: " + SensorDriverManager.MinimumSupportedVersion);
                builder.AppendLine("PawnIO導入版: " + (installedDriver == null ? "未導入" : installedDriver.ToString()));
                builder.AppendLine("PawnIO接続: " + (SensorDriverManager.IsLoaded() ? "成功" : "失敗"));
                if (!String.IsNullOrWhiteSpace(_lastTemperatureFailureReason))
                    builder.AppendLine("温度取得診断: " + _lastTemperatureFailureReason);
                if (!String.IsNullOrWhiteSpace(_openError))
                    builder.AppendLine("初期化メモ: " + _openError);
                builder.AppendLine();

                if (_lastRecords.Count == 0)
                {
                    builder.AppendLine("LibreHardwareMonitorのセンサーは検出されませんでした。");
                    builder.AppendLine("管理者権限、BIOS/EC対応、他の監視ソフトとの競合を確認してください。");
                }
                else
                {
                    foreach (SensorRecord record in _lastRecords.OrderBy(r => r.Path).ThenBy(r => r.SensorType).ThenBy(r => r.SensorName))
                    {
                        builder.Append(record.Path).Append(" | ")
                            .Append(record.SensorType).Append(" | ")
                            .Append(record.SensorName).Append(" = ")
                            .AppendLine(record.Value.HasValue ? record.Value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "null");
                    }
                }
                return builder.ToString();
            }
        }

        public string Reload()
        {
            lock (_sync)
            {
                if (_opened)
                {
                    try { _computer.Close(); }
                    catch { }
                }

                _opened = false;
                _openError = String.Empty;
                _lastRecords.Clear();
                _coolingSensors.Reset();
                _lastTemperatureFailureReason = String.Empty;

                Version installedDriver = SensorDriverManager.InstalledVersion();
                if (installedDriver == null)
                    _openError = "PawnIOセンサードライバー未導入（CPU温度の取得に必要です）";
                else if (installedDriver < SensorDriverManager.MinimumSupportedVersion)
                    _openError = "PawnIO " + installedDriver + " は古いため更新が必要です";

                try
                {
                    _computer.Open();
                    _opened = true;
                }
                catch (Exception ex)
                {
                    _openError = AppendError(_openError, "LibreHardwareMonitor初期化: " + ex.Message);
                }

                if (_opened && !SensorDriverManager.IsLoaded())
                {
                    string access = SensorDriverManager.IsProcessElevated()
                        ? "PawnIOドライバーへ接続できません（再起動または修復が必要です）"
                        : "管理者権限がないためPawnIOドライバーへ接続できません";
                    _openError = AppendError(_openError, access);
                }

                return String.IsNullOrWhiteSpace(_openError) ? null : _openError;
            }
        }

        public void SetTestPhase(TestPhase phase)
        {
            // 実機センサーでは工程通知は不要。デモ実装との共通インターフェース用。
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_opened)
                {
                    try { _computer.Close(); }
                    catch { }
                    _opened = false;
                }
            }
        }

        private static void CollectHardware(IHardware hardware, string path, List<SensorRecord> records)
        {
            foreach (ISensor sensor in hardware.Sensors)
            {
                records.Add(new SensorRecord
                {
                    HardwareType = hardware.HardwareType.ToString(),
                    HardwareIdentifier = hardware.Identifier.ToString(),
                    Identifier = sensor.Identifier.ToString(),
                    HardwareName = hardware.Name,
                    Path = path,
                    SensorName = sensor.Name,
                    SensorType = sensor.SensorType.ToString(),
                    Value = sensor.Value.HasValue ? (double?)sensor.Value.Value : null
                });
            }

            foreach (IHardware subHardware in hardware.SubHardware)
                CollectHardware(subHardware, path + " > " + subHardware.Name, records);
        }

        private static bool EqualsIgnoreCase(string left, string right)
        {
            return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendError(string existing, string message)
        {
            if (String.IsNullOrWhiteSpace(existing))
                return message;
            return existing + " / " + message;
        }

        private static SystemProfile LoadSystemProfile()
        {
            SystemProfile profile = new SystemProfile();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        profile.Manufacturer = Convert.ToString(item["Manufacturer"]).Trim();
                        profile.Model = Convert.ToString(item["Model"]).Trim();
                        break;
                    }
                }
            }
            catch { }

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        profile.CpuName = Convert.ToString(item["Name"]).Trim();
                        break;
                    }
                }
            }
            catch { }

            profile.CpuBaseSpeedMHz = ReadWindowsPerformanceBaseSpeed();
            CpuTopologyReader.Populate(profile);

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption, Version FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        profile.OperatingSystem = (Convert.ToString(item["Caption"]) + " " + Convert.ToString(item["Version"])).Trim();
                        break;
                    }
                }
            }
            catch { }

            return profile;
        }

        private static double? ReadWindowsPerformanceBaseSpeed()
        {
            try
            {
                int processorCount = Math.Max(1, Environment.ProcessorCount);
                ProcessorPowerInformation[] values = new ProcessorPowerInformation[processorCount];
                int structureSize = Marshal.SizeOf(typeof(ProcessorPowerInformation));
                uint status = CallNtPowerInformation(
                    ProcessorInformationLevel,
                    IntPtr.Zero,
                    0,
                    values,
                    checked((uint)(structureSize * values.Length)));
                if (status != 0)
                    return null;

                uint maxMhz = values.Where(value => value.MaxMhz > 0).Select(value => value.MaxMhz).DefaultIfEmpty(0U).Max();
                return maxMhz > 0 ? (double?)maxMhz : null;
            }
            catch
            {
                return null;
            }
        }

        private static double? ReadAcpiTemperature()
        {
            try
            {
                List<double> values = new List<double>();
                ManagementScope scope = new ManagementScope(@"\\.\root\wmi");
                ObjectQuery query = new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        if (item["CurrentTemperature"] == null)
                            continue;
                        double celsius = Convert.ToDouble(item["CurrentTemperature"], CultureInfo.InvariantCulture) / 10.0 - 273.15;
                        if (celsius > 0.0 && celsius < 126.0)
                            values.Add(celsius);
                    }
                }
                return values.Count == 0 ? null : (double?)values.Max();
            }
            catch
            {
                return null;
            }
        }

        private static double? ReadWmiCpuLoad()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor"))
                {
                    List<double> values = new List<double>();
                    foreach (ManagementObject item in searcher.Get())
                    {
                        if (item["LoadPercentage"] != null)
                            values.Add(Convert.ToDouble(item["LoadPercentage"], CultureInfo.InvariantCulture));
                    }
                    return values.Count == 0 ? null : (double?)values.Average();
                }
            }
            catch
            {
                return null;
            }
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }

            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (IHardware subHardware in hardware.SubHardware)
                    subHardware.Accept(this);
            }

            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }

    public sealed class DemoSensorReader : ISensorReader
    {
        private readonly Random _random = new Random(3939);
        private TestPhase _phase;
        private DateTime _phaseStarted;
        private double _temperature;
        private double _fan;

        public SystemProfile Profile { get; private set; }

        public DemoSensorReader()
        {
            Profile = new SystemProfile
            {
                Manufacturer = "MKNK Demo",
                Model = "PC Fan Test Model",
                CpuName = "Demo CPU 8-Core",
                CpuBaseSpeedMHz = 3200.0,
                CpuSocketCount = 1,
                CpuCoreCount = 8,
                CpuLogicalProcessorCount = 16,
                OperatingSystem = "Windows 11（デモ）"
            };
            _phase = TestPhase.Monitoring;
            _phaseStarted = DateTime.Now;
            _temperature = 47.0;
            _fan = 0.0;
        }

        public SensorSnapshot Read()
        {
            double seconds = (DateTime.Now - _phaseStarted).TotalSeconds;
            double targetTemp = 48.0;
            double targetFan = 0.0;
            double load = 7.0;

            if (_phase == TestPhase.Idle)
            {
                targetTemp = 49.0;
                targetFan = 0.0;
                load = 8.0;
            }
            else if (_phase == TestPhase.Load)
            {
                targetTemp = Math.Min(82.0, 52.0 + seconds * 0.65);
                targetFan = seconds < 5.0 ? 0.0 : Math.Min(3600.0, 500.0 + seconds * 85.0);
                load = 82.0;
            }
            else if (_phase == TestPhase.Cooldown || _phase == TestPhase.Complete)
            {
                targetTemp = Math.Max(51.0, 78.0 - seconds * 0.55);
                targetFan = Math.Max(500.0, 3000.0 - seconds * 55.0);
                load = 6.0;
            }

            _temperature += (targetTemp - _temperature) * 0.28;
            _fan += (targetFan - _fan) * 0.35;
            double jitter = (_random.NextDouble() - 0.5) * 0.35;

            return new SensorSnapshot
            {
                Timestamp = DateTime.Now,
                TemperatureC = _temperature + jitter,
                CpuLoadPercent = Math.Max(0.0, Math.Min(100.0, load + jitter * 4.0)),
                CpuClockGHz = (_phase == TestPhase.Load ? 3.8 : 1.2) + jitter,
                FanRpm = Math.Max(0.0, _fan + jitter * 35.0),
                FanControlPercent = targetFan <= 0.0 ? 0.0 : Math.Min(100.0, targetFan / 40.0),
                FanSensorAvailable = true,
                TemperatureIsCpuDirect = true,
                TemperatureSensorName = "CPU Package（デモ）",
                FanSensorName = "CPU Fan（デモ）",
                SensorSource = "内蔵デモセンサー"
            };
        }

        public string GetRawSensorReport()
        {
            return "デモモード\r\nCPU > Temperature | CPU Package = simulated\r\nEC > Fan | CPU Fan = simulated\r\nCPU > Load | CPU Total = simulated\r\nCPU > Clock | CPU Core = simulated (MHz)\r\n";
        }

        public void SetTestPhase(TestPhase phase)
        {
            _phase = phase;
            _phaseStarted = DateTime.Now;
        }

        public void Dispose() { }

        public string Reload()
        {
            return null;
        }
    }
}
