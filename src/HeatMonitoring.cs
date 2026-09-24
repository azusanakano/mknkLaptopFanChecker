using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Xml;
using Microsoft.Win32;

namespace Mknk.LaptopFanChecker
{
    public sealed class AppSettings
    {
        public double AbsoluteAlertC { get; set; }
        public double SustainedAlertC { get; set; }
        public int SustainedSeconds { get; set; }
        public double IdleHighAlertC { get; set; }
        public double IdleLoadMaximumPercent { get; set; }
        public int IdleHighSeconds { get; set; }
        public double RapidRiseDeltaC { get; set; }
        public int RapidRiseSeconds { get; set; }
        public double RapidRiseMinimumC { get; set; }
        public int RepeatSuppressionMinutes { get; set; }
        public bool HeatMonitoringEnabled { get; set; }
        public bool ShowWindowOnCriticalAlert { get; set; }
        public bool SmartBoostEnabled { get; set; }
        public DateTime? LastSmartBoostAttemptUtc { get; set; }

        public AppSettings()
        {
            AbsoluteAlertC = 92.0;
            SustainedAlertC = 85.0;
            SustainedSeconds = 30;
            IdleHighAlertC = 80.0;
            IdleLoadMaximumPercent = 25.0;
            IdleHighSeconds = 60;
            RapidRiseDeltaC = 15.0;
            RapidRiseSeconds = 60;
            RapidRiseMinimumC = 75.0;
            RepeatSuppressionMinutes = 10;
            HeatMonitoringEnabled = true;
            ShowWindowOnCriticalAlert = true;
        }

        public static string SettingsDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mknkLaptopFanChecker");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDirectory, "settings.ini"); }
        }

        public static AppSettings Load()
        {
            return Load(SettingsPath);
        }

        internal static AppSettings Load(string path)
        {
            AppSettings settings = new AppSettings();
            try
            {
                if (!File.Exists(path))
                    return settings;
                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;
                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    double number;
                    int integer;
                    bool flag;
                    DateTime instant;
                    if (key == "AbsoluteAlertC" && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) settings.AbsoluteAlertC = Clamp(number, 75.0, 105.0);
                    else if (key == "SustainedAlertC" && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) settings.SustainedAlertC = Clamp(number, 70.0, 100.0);
                    else if (key == "SustainedSeconds" && Int32.TryParse(value, out integer)) settings.SustainedSeconds = Clamp(integer, 10, 300);
                    else if (key == "IdleHighAlertC" && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) settings.IdleHighAlertC = Clamp(number, 60.0, 100.0);
                    else if (key == "IdleLoadMaximumPercent" && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) settings.IdleLoadMaximumPercent = Clamp(number, 5.0, 60.0);
                    else if (key == "IdleHighSeconds" && Int32.TryParse(value, out integer)) settings.IdleHighSeconds = Clamp(integer, 20, 600);
                    else if (key == "RapidRiseDeltaC" && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) settings.RapidRiseDeltaC = Clamp(number, 5.0, 35.0);
                    else if (key == "RapidRiseSeconds" && Int32.TryParse(value, out integer)) settings.RapidRiseSeconds = Clamp(integer, 15, 300);
                    else if (key == "RapidRiseMinimumC" && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) settings.RapidRiseMinimumC = Clamp(number, 50.0, 100.0);
                    else if (key == "RepeatSuppressionMinutes" && Int32.TryParse(value, out integer)) settings.RepeatSuppressionMinutes = Clamp(integer, 1, 120);
                    else if (key == "HeatMonitoringEnabled" && Boolean.TryParse(value, out flag)) settings.HeatMonitoringEnabled = flag;
                    else if (key == "ShowWindowOnCriticalAlert" && Boolean.TryParse(value, out flag)) settings.ShowWindowOnCriticalAlert = flag;
                    else if (key == "SmartBoostEnabled" && Boolean.TryParse(value, out flag)) settings.SmartBoostEnabled = flag;
                    else if (key == "LastSmartBoostAttemptUtc" && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out instant)) settings.LastSmartBoostAttemptUtc = instant.ToUniversalTime();
                }
            }
            catch { }
            return settings;
        }

        public void Save()
        {
            Save(SettingsPath);
        }

        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# mknkLaptopFanChecker settings");
            Append(builder, "AbsoluteAlertC", AbsoluteAlertC);
            Append(builder, "SustainedAlertC", SustainedAlertC);
            Append(builder, "SustainedSeconds", SustainedSeconds);
            Append(builder, "IdleHighAlertC", IdleHighAlertC);
            Append(builder, "IdleLoadMaximumPercent", IdleLoadMaximumPercent);
            Append(builder, "IdleHighSeconds", IdleHighSeconds);
            Append(builder, "RapidRiseDeltaC", RapidRiseDeltaC);
            Append(builder, "RapidRiseSeconds", RapidRiseSeconds);
            Append(builder, "RapidRiseMinimumC", RapidRiseMinimumC);
            Append(builder, "RepeatSuppressionMinutes", RepeatSuppressionMinutes);
            Append(builder, "HeatMonitoringEnabled", HeatMonitoringEnabled);
            Append(builder, "ShowWindowOnCriticalAlert", ShowWindowOnCriticalAlert);
            Append(builder, "SmartBoostEnabled", SmartBoostEnabled);
            if (LastSmartBoostAttemptUtc.HasValue)
                Append(builder, "LastSmartBoostAttemptUtc", LastSmartBoostAttemptUtc.Value.ToString("o", CultureInfo.InvariantCulture));
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static void Append(StringBuilder builder, string key, object value)
        {
            string text = value is IFormattable
                ? ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture);
            builder.Append(key).Append('=').AppendLine(text);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    public enum HeatAlertKind
    {
        AbsoluteHigh,
        SustainedHigh,
        IdleHigh,
        RapidRise
    }

    public sealed class HeatAlert
    {
        public HeatAlertKind Kind { get; set; }
        public DateTime Timestamp { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public double TemperatureC { get; set; }
        public double? CpuLoadPercent { get; set; }
        public double? FanRpm { get; set; }
        public bool Critical { get; set; }
    }

    public sealed class HeatAnomalyDetector
    {
        private readonly Dictionary<HeatAlertKind, DateTime> _lastAlerts = new Dictionary<HeatAlertKind, DateTime>();

        public HeatAlert Check(IList<TelemetrySample> history, SensorSnapshot current, AppSettings settings)
        {
            if (!settings.HeatMonitoringEnabled || !current.TemperatureC.HasValue)
                return null;

            DateTime now = current.Timestamp;
            double temperature = current.TemperatureC.Value;

            if (temperature >= settings.AbsoluteAlertC)
            {
                if (CanAlert(HeatAlertKind.AbsoluteHigh, now, settings))
                {
                    return Register(new HeatAlert
                    {
                        Kind = HeatAlertKind.AbsoluteHigh,
                        Timestamp = now,
                        Title = "高温通知：設定上限に到達",
                        Message = String.Format("CPU温度 {0:0.0}℃。負荷作業を止め、吸排気とファンを確認してください。", temperature),
                        TemperatureC = temperature,
                        CpuLoadPercent = current.CpuLoadPercent,
                        FanRpm = current.FanRpm,
                        Critical = true
                    });
                }
                return null;
            }

            List<TelemetrySample> sustained = Window(history, now, settings.SustainedSeconds);
            bool sustainedHigh = HasCoverage(sustained, settings.SustainedSeconds) &&
                sustained.Count(s => s.TemperatureC.HasValue && s.TemperatureC.Value >= settings.SustainedAlertC) >= Math.Ceiling(sustained.Count * 0.8);
            if (sustainedHigh)
            {
                if (CanAlert(HeatAlertKind.SustainedHigh, now, settings))
                {
                    return Register(new HeatAlert
                    {
                        Kind = HeatAlertKind.SustainedHigh,
                        Timestamp = now,
                        Title = "異常発熱：高温が持続",
                        Message = String.Format("{0:0}℃以上が約{1}秒続いています。冷却系と実行中の処理を確認してください。", settings.SustainedAlertC, settings.SustainedSeconds),
                        TemperatureC = temperature,
                        CpuLoadPercent = current.CpuLoadPercent,
                        FanRpm = current.FanRpm,
                        Critical = temperature >= settings.AbsoluteAlertC - 2.0
                    });
                }
                return null;
            }

            List<TelemetrySample> idleWindow = Window(history, now, settings.IdleHighSeconds);
            if (HasCoverage(idleWindow, settings.IdleHighSeconds))
            {
                List<double> idleLoads = idleWindow.Where(s => s.CpuLoadPercent.HasValue).Select(s => s.CpuLoadPercent.Value).ToList();
                List<double> idleTemps = idleWindow.Where(s => s.TemperatureC.HasValue).Select(s => s.TemperatureC.Value).ToList();
                double averageLoad = idleLoads.Count == 0 ? 100.0 : idleLoads.Average();
                double highFraction = idleTemps.Count == 0 ? 0.0 : (double)idleTemps.Count(v => v >= settings.IdleHighAlertC) / idleTemps.Count;
                if (averageLoad <= settings.IdleLoadMaximumPercent && highFraction >= 0.75)
                {
                    if (CanAlert(HeatAlertKind.IdleHigh, now, settings))
                    {
                        return Register(new HeatAlert
                        {
                            Kind = HeatAlertKind.IdleHigh,
                            Timestamp = now,
                            Title = "異常発熱：低負荷なのに高温",
                            Message = String.Format("平均CPU負荷 {0:0}% で {1:0}℃以上が続いています。埃詰まりやファン停止の疑いがあります。", averageLoad, settings.IdleHighAlertC),
                            TemperatureC = temperature,
                            CpuLoadPercent = current.CpuLoadPercent,
                            FanRpm = current.FanRpm,
                            Critical = false
                        });
                    }
                    return null;
                }
            }

            List<TelemetrySample> riseWindow = Window(history, now, settings.RapidRiseSeconds);
            TelemetrySample oldest = riseWindow.Where(s => s.TemperatureC.HasValue).OrderBy(s => s.Timestamp).FirstOrDefault();
            if (oldest != null && HasCoverage(riseWindow, Math.Min(20, settings.RapidRiseSeconds)) &&
                temperature >= settings.RapidRiseMinimumC &&
                temperature - oldest.TemperatureC.Value >= settings.RapidRiseDeltaC &&
                CanAlert(HeatAlertKind.RapidRise, now, settings))
            {
                double rise = temperature - oldest.TemperatureC.Value;
                return Register(new HeatAlert
                {
                    Kind = HeatAlertKind.RapidRise,
                    Timestamp = now,
                    Title = "異常発熱：急上昇",
                    Message = String.Format("CPU温度が約{0:0}秒で +{1:0.0}℃ 上昇し、{2:0.0}℃です。", (now - oldest.Timestamp).TotalSeconds, rise, temperature),
                    TemperatureC = temperature,
                    CpuLoadPercent = current.CpuLoadPercent,
                    FanRpm = current.FanRpm,
                    Critical = temperature >= settings.SustainedAlertC
                });
            }

            return null;
        }

        private bool CanAlert(HeatAlertKind kind, DateTime now, AppSettings settings)
        {
            DateTime previous;
            if (!_lastAlerts.TryGetValue(kind, out previous))
                return true;
            return (now - previous).TotalMinutes >= settings.RepeatSuppressionMinutes;
        }

        private HeatAlert Register(HeatAlert alert)
        {
            _lastAlerts[alert.Kind] = alert.Timestamp;
            return alert;
        }

        private static List<TelemetrySample> Window(IList<TelemetrySample> history, DateTime now, int seconds)
        {
            DateTime cutoff = now.AddSeconds(-seconds);
            return history.Where(s => s.Timestamp >= cutoff && s.Timestamp <= now).OrderBy(s => s.Timestamp).ToList();
        }

        private static bool HasCoverage(IList<TelemetrySample> values, int requiredSeconds)
        {
            return values.Count >= Math.Max(3, requiredSeconds / 3) &&
                (values[values.Count - 1].Timestamp - values[0].Timestamp).TotalSeconds >= requiredSeconds * 0.8;
        }
    }

    public static class HeatAlertLogger
    {
        public static string Append(HeatAlert alert, SensorSnapshot snapshot, SystemProfile profile)
        {
            string directory = Path.Combine(AppSettings.SettingsDirectory, "HeatAlerts");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "HeatAlerts_" + DateTime.Now.ToString("yyyyMM") + ".csv");
            bool newFile = !File.Exists(path);
            using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(true)))
            {
                if (newFile)
                    writer.WriteLine("timestamp,kind,temperature_c,cpu_load_percent,fan_rpm,model,cpu,sensor_source,message");
                writer.WriteLine(String.Join(",", new string[]
                {
                    Csv(alert.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(alert.Kind.ToString()),
                    Csv(alert.TemperatureC.ToString("0.0", CultureInfo.InvariantCulture)),
                    Csv(snapshot.CpuLoadPercent.HasValue ? snapshot.CpuLoadPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) : ""),
                    Csv(snapshot.FanRpm.HasValue ? snapshot.FanRpm.Value.ToString("0", CultureInfo.InvariantCulture) : ""),
                    Csv(profile.DisplayName),
                    Csv(profile.CpuName),
                    Csv(snapshot.SensorSource),
                    Csv(alert.Message)
                }));
            }
            return path;
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? String.Empty).Replace("\"", "\"\"") + "\"";
        }
    }

    public static class StartupTaskManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "mknkLaptopFanChecker";
        private const string TaskName = "mknk Laptop CPU Fan Monitor";
        private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        public static bool IsEnabled()
        {
            return HasRunEntry() || HasScheduledTask();
        }

        public static string Enable(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                return "実行ファイルが見つかりません。";

            string taskXmlPath = null;
            try
            {
                string startupExecutable = ResolveStartupExecutablePath(executablePath);
                if (!File.Exists(startupExecutable))
                    return "自動起動に使う実行ファイルが見つかりません。ZIPを展開し直してください。";

                string userId;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    userId = identity.User == null ? null : identity.User.Value;
                if (String.IsNullOrWhiteSpace(userId))
                    return "現在のWindowsユーザーを確認できません。";

                string managedExecutable = Path.GetFullPath(executablePath);
                taskXmlPath = Path.Combine(Path.GetTempPath(), "mknk-fan-monitor-" + Guid.NewGuid().ToString("N") + ".xml");
                File.WriteAllText(taskXmlPath, BuildTaskXml(managedExecutable, userId), Encoding.Unicode);
                ProcessResult createResult = RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + taskXmlPath + "\" /F");
                if (createResult.ExitCode != 0)
                    return "管理者センサー用の自動起動タスクを作成できませんでした。" + createResult.Output;

                string command = BuildRunCommand(startupExecutable);
                if (command.Length > 260)
                    return "自動起動へ登録するパスが長すぎます。アプリをデスクトップ直下など、短い保存場所へ移動してください。";
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
                {
                    if (key == null)
                        return "Windowsのユーザー別自動起動設定を開けませんでした。";
                    key.SetValue(RunValueName, command, RegistryValueKind.String);
                }

                string savedCommand = ReadRunCommand();
                if (!String.Equals(savedCommand, command, StringComparison.Ordinal))
                    return "Windowsのユーザー別自動起動設定を確認できませんでした。";

                if (!HasScheduledTask())
                    return "自動起動タスクの登録を確認できませんでした。";
                return null;
            }
            catch (Exception ex)
            {
                return "Windowsログイン時の自動起動を登録できませんでした。" + ex.Message;
            }
            finally
            {
                if (!String.IsNullOrWhiteSpace(taskXmlPath))
                {
                    try { File.Delete(taskXmlPath); }
                    catch { }
                }
            }
        }

        public static string RepairIfEnabled(string executablePath)
        {
            return IsEnabled() ? Enable(executablePath) : null;
        }

        public static string ResolveStartupExecutablePath(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("実行ファイルのパスが必要です。", "executablePath");

            string fullPath = Path.GetFullPath(executablePath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!String.IsNullOrWhiteSpace(directory) &&
                String.Equals(Path.GetFileName(directory), "runtime", StringComparison.OrdinalIgnoreCase))
            {
                DirectoryInfo parent = Directory.GetParent(directory);
                if (parent != null)
                {
                    string nativeLauncher = Path.Combine(parent.FullName, "mknkLaptopFanChecker.exe");
                    if (File.Exists(nativeLauncher))
                        return nativeLauncher;
                }
            }
            return fullPath;
        }

        public static string BuildRunCommand(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("実行ファイルのパスが必要です。", "executablePath");
            return "\"" + Path.GetFullPath(executablePath).Replace("\"", "\\\"") + "\" --start-monitor-task";
        }

        public static string BuildTaskXml(string executablePath, string userId)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("実行ファイルのパスが必要です。", "executablePath");
            if (String.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("WindowsユーザーIDが必要です。", "userId");

            string workingDirectory = Path.GetDirectoryName(executablePath);
            StringBuilder builder = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false };
            using (StringWriter text = new StringWriter(builder, CultureInfo.InvariantCulture))
            using (XmlWriter writer = XmlWriter.Create(text, settings))
            {
                writer.WriteStartDocument();
                WriteTaskElement(writer, "Task");
                writer.WriteAttributeString("version", "1.3");

                WriteTaskElement(writer, "RegistrationInfo");
                WriteTaskValue(writer, "Description", "ネコシステム社 CPUファンチェッカーの異常発熱監視");
                writer.WriteEndElement();

                WriteTaskElement(writer, "Triggers");
                WriteTaskElement(writer, "LogonTrigger");
                WriteTaskValue(writer, "Enabled", "true");
                WriteTaskValue(writer, "UserId", userId);
                writer.WriteEndElement();
                writer.WriteEndElement();

                WriteTaskElement(writer, "Principals");
                WriteTaskElement(writer, "Principal");
                writer.WriteAttributeString("id", "Author");
                WriteTaskValue(writer, "UserId", userId);
                WriteTaskValue(writer, "LogonType", "InteractiveToken");
                WriteTaskValue(writer, "RunLevel", "HighestAvailable");
                writer.WriteEndElement();
                writer.WriteEndElement();

                WriteTaskElement(writer, "Settings");
                WriteTaskValue(writer, "MultipleInstancesPolicy", "IgnoreNew");
                WriteTaskValue(writer, "DisallowStartIfOnBatteries", "false");
                WriteTaskValue(writer, "StopIfGoingOnBatteries", "false");
                WriteTaskValue(writer, "StartWhenAvailable", "true");
                WriteTaskValue(writer, "AllowStartOnDemand", "true");
                WriteTaskValue(writer, "Enabled", "true");
                WriteTaskValue(writer, "RunOnlyIfIdle", "false");
                WriteTaskValue(writer, "WakeToRun", "false");
                WriteTaskValue(writer, "ExecutionTimeLimit", "PT0S");
                WriteTaskValue(writer, "Priority", "7");
                writer.WriteEndElement();

                WriteTaskElement(writer, "Actions");
                writer.WriteAttributeString("Context", "Author");
                WriteTaskElement(writer, "Exec");
                WriteTaskValue(writer, "Command", Path.GetFullPath(executablePath));
                WriteTaskValue(writer, "Arguments", "--monitor");
                WriteTaskValue(writer, "WorkingDirectory", workingDirectory);
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return builder.ToString();
        }

        private static void WriteTaskElement(XmlWriter writer, string name)
        {
            writer.WriteStartElement(name, TaskNamespace);
        }

        private static void WriteTaskValue(XmlWriter writer, string name, string value)
        {
            writer.WriteElementString(name, TaskNamespace, value);
        }

        public static string Disable()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null)
                        key.DeleteValue(RunValueName, false);
                }
                if (HasRunEntry())
                    return "Windowsのユーザー別自動起動設定を解除できませんでした。";

                ProcessResult result = HasScheduledTask()
                    ? RunSchtasks("/Delete /TN \"" + TaskName + "\" /F")
                    : new ProcessResult { ExitCode = 0, Output = String.Empty };
                if (result.ExitCode != 0)
                    return "自動起動タスクの削除に失敗しました。" + result.Output;
                return null;
            }
            catch (Exception ex)
            {
                return "Windowsログイン時の自動起動を解除できませんでした。" + ex.Message;
            }
        }

        private static bool HasRunEntry()
        {
            return !String.IsNullOrWhiteSpace(ReadRunCommand());
        }

        private static string ReadRunCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                    return key == null ? null : Convert.ToString(key.GetValue(RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            }
            catch
            {
                return null;
            }
        }

        private static bool HasScheduledTask()
        {
            return RunSchtasks("/Query /TN \"" + TaskName + "\"").ExitCode == 0;
        }

        private static ProcessResult RunSchtasks(string arguments)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit(10000);
                    return new ProcessResult { ExitCode = process.HasExited ? process.ExitCode : -1, Output = output.Trim() };
                }
            }
            catch (Exception ex)
            {
                return new ProcessResult { ExitCode = -1, Output = ex.Message };
            }
        }

        private sealed class ProcessResult
        {
            public int ExitCode;
            public string Output;
        }
    }
}
