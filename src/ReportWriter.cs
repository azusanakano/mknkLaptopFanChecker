using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Mknk.LaptopFanChecker
{
    public static class ReportWriter
    {
        public static SavedReport Save(
            SystemProfile profile,
            IList<TelemetrySample> samples,
            EvaluationResult evaluation,
            bool airflowConfirmed,
            bool abnormalNoise,
            string rawSensorReport)
        {
            string directory = ResolveReportDirectory();
            Directory.CreateDirectory(directory);
            string model = SafeFileName(profile.Model);
            string stem = "FanCheck_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + model;
            string htmlPath = Path.Combine(directory, stem + ".html");
            string csvPath = Path.Combine(directory, stem + ".csv");

            WriteCsv(csvPath, samples);
            WriteHtml(htmlPath, profile, samples, evaluation, airflowConfirmed, abnormalNoise, rawSensorReport, csvPath);
            return new SavedReport { HtmlPath = htmlPath, CsvPath = csvPath };
        }

        public static string ResolveReportDirectory()
        {
            string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            try
            {
                Directory.CreateDirectory(portable);
                string probe = Path.Combine(portable, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return portable;
            }
            catch
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mknkLaptopFanChecker", "Reports");
            }
        }

        private static void WriteCsv(string path, IList<TelemetrySample> samples)
        {
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("timestamp,elapsed_seconds,phase,cpu_temperature_c,cpu_load_percent,fan_rpm,fan_control_percent,fan_sensor_available,temperature_sensor,fan_sensor,sensor_source,cpu_clock_ghz");
                foreach (TelemetrySample sample in samples)
                {
                    writer.WriteLine(String.Join(",", new[]
                    {
                        Csv(sample.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(sample.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture)),
                        Csv(PhaseName(sample.Phase)),
                        Csv(Number(sample.TemperatureC, "0.0")),
                        Csv(Number(sample.CpuLoadPercent, "0.0")),
                        Csv(Number(sample.FanRpm, "0")),
                        Csv(Number(sample.FanControlPercent, "0.0")),
                        Csv(sample.FanSensorAvailable ? "true" : "false"),
                        Csv(sample.TemperatureSensorName),
                        Csv(sample.FanSensorName),
                        Csv(sample.SensorSource),
                        Csv(sample.CpuClockGHz.HasValue ? Number(sample.CpuClockGHz, "0.00") : String.Empty)
                    }));
                }
            }
        }

        private static void WriteHtml(
            string path,
            SystemProfile profile,
            IList<TelemetrySample> samples,
            EvaluationResult result,
            bool airflowConfirmed,
            bool abnormalNoise,
            string rawSensorReport,
            string csvPath)
        {
            string color = result.Level == VerdictLevel.Normal ? "#18a66a" :
                result.Level == VerdictLevel.Caution ? "#d59618" :
                result.Level == VerdictLevel.Suspect ? "#dc4d5a" : "#718096";
            StringBuilder html = new StringBuilder();
            html.Append("<!doctype html><html lang=\"ja\"><head><meta charset=\"utf-8\">");
            html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            html.Append("<title>CPUファン検査レポート</title><style>");
            html.Append("body{font-family:'Yu Gothic UI','Meiryo',sans-serif;background:#f3f6fa;color:#1d2939;margin:0;padding:28px}main{max-width:1080px;margin:auto;background:#fff;border-radius:18px;padding:30px;box-shadow:0 10px 35px #172b4d18}h1{margin:0 0 6px;color:#12304a}.sub{color:#667085}.verdict{margin:24px 0;padding:20px;border-left:8px solid ").Append(color).Append(";background:").Append(color).Append("12;border-radius:10px}.verdict h2{margin:0 0 8px;color:").Append(color).Append("}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px}.card{border:1px solid #d8e1ea;border-radius:10px;padding:14px}.card b{display:block;font-size:12px;color:#667085}.card span{font-size:22px;font-weight:700}.section{margin-top:26px}table{border-collapse:collapse;width:100%;font-size:12px}th,td{border-bottom:1px solid #e6ebf0;padding:7px;text-align:left}th{background:#f7f9fb;position:sticky;top:0}.scroll{max-height:430px;overflow:auto;border:1px solid #e0e6ed;border-radius:10px}pre{white-space:pre-wrap;background:#101828;color:#d0d5dd;padding:16px;border-radius:10px;max-height:300px;overflow:auto}.notice{padding:14px;background:#fff7e6;border-radius:10px;color:#694f00}small{color:#667085}</style></head><body><main>");
            html.Append("<h1>ネコシステム社 CPUファンチェッカー</h1><div class=\"sub\">検査レポート / ")
                .Append(H(DateTime.Now.ToString("yyyy年M月d日 HH:mm:ss"))).Append("</div>");
            html.Append("<div class=\"verdict\"><h2>").Append(H(result.Verdict)).Append("</h2><p>")
                .Append(H(result.Basis)).Append("</p><p><b>限界：</b>").Append(H(result.Limitation))
                .Append("</p><p><b>推奨：</b>").Append(H(result.RecommendedAction)).Append("</p></div>");

            html.Append("<div class=\"grid\">");
            Card(html, "PC", profile.DisplayName);
            Card(html, "CPU", profile.CpuName);
            Card(html, "CPU基本速度（Windows）", profile.CpuBaseSpeedText);
            Card(html, "ソケット", profile.CpuSocketCountText);
            Card(html, "コア", profile.CpuCoreCountText);
            Card(html, "論理プロセッサ数", profile.CpuLogicalProcessorCountText);
            Card(html, "最高CPU温度", Number(result.MaximumTemperatureC, "0.0") + " ℃");
            Card(html, "冷却工程の低下", Number(result.CooldownDropC, "0.0") + " ℃");
            Card(html, "最大ファン回転", Number(result.MaximumFanRpm, "0") + " RPM");
            Card(html, "待機時からの増加", Number(result.FanIncreaseRpm, "+0;-0;0") + " RPM");
            Card(html, "判定方式", result.DirectRpmAssessment ? "RPM直接＋温度" : "温度挙動による間接");
            Card(html, "保護停止値", result.SafetyCutoffC.ToString("0") + " ℃");
            html.Append("</div>");

            html.Append("<div class=\"section\"><h2>人による確認</h2><ul><li>負荷中に排気が強くなった：")
                .Append(airflowConfirmed ? "はい" : "未確認").Append("</li><li>擦れ音・唸り・周期音：")
                .Append(abnormalNoise ? "あり" : "なし／未確認").Append("</li></ul></div>");

            html.Append("<div class=\"section\"><h2>測定ログ</h2><p><small>CSV: ")
                .Append(H(Path.GetFileName(csvPath))).Append("<br>CPU実働速度は検出したコアの動作クロック平均です。</small></p><div class=\"scroll\"><table><thead><tr><th>時刻</th><th>工程</th><th>経過</th><th>温度</th><th>CPU負荷</th><th>CPU実働速度</th><th>Fan</th><th>センサー</th></tr></thead><tbody>");
            foreach (TelemetrySample sample in samples)
            {
                html.Append("<tr><td>").Append(H(sample.Timestamp.ToString("HH:mm:ss"))).Append("</td><td>")
                    .Append(H(PhaseName(sample.Phase))).Append("</td><td>").Append(sample.ElapsedSeconds.ToString("0")).Append(" 秒</td><td>")
                    .Append(H(Number(sample.TemperatureC, "0.0"))).Append(" ℃</td><td>").Append(H(Number(sample.CpuLoadPercent, "0.0"))).Append(" %</td><td>")
                    .Append(H(CpuClockMetrics.FormatGHz(sample.CpuClockGHz))).Append("</td><td>")
                    .Append(H(Number(sample.FanRpm, "0"))).Append(" RPM</td><td>").Append(H(sample.FanSensorName)).Append("</td></tr>");
            }
            html.Append("</tbody></table></div></div>");
            html.Append("<div class=\"section\"><h2>検出センサー（参考）</h2><pre>").Append(H(rawSensorReport)).Append("</pre></div>");
            html.Append("<div class=\"section notice\"><b>注意：</b>本ツールは故障の可能性を絞る検査補助です。CPUファンのRPMを特定できない構成では温度挙動から推定します。メーカー診断、目視、排気・異音確認を置き換えません。ファン制御値やBIOS設定は変更しません。</div>");
            html.Append("<p><small>ネコシステム社 CPUファンチェッカー ").Append(H(AppInfo.Version))
                .Append(" / ネコシステム社 / Sensor engine: LibreHardwareMonitor 0.9.6 (MPL-2.0)</small></p>");
            html.Append("</main></body></html>");
            File.WriteAllText(path, html.ToString(), new UTF8Encoding(true));
        }

        private static void Card(StringBuilder html, string label, string value)
        {
            html.Append("<div class=\"card\"><b>").Append(H(label)).Append("</b><span>").Append(H(value)).Append("</span></div>");
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? String.Empty).Replace("\"", "\"\"") + "\"";
        }

        private static string H(string value)
        {
            return WebUtility.HtmlEncode(value ?? String.Empty);
        }

        private static string Number(double? value, string format)
        {
            return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "—";
        }

        private static string SafeFileName(string value)
        {
            string result = String.IsNullOrWhiteSpace(value) ? "PC" : value;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            if (result.Length > 50)
                result = result.Substring(0, 50);
            return result.Trim();
        }

        public static string PhaseName(TestPhase phase)
        {
            switch (phase)
            {
                case TestPhase.Idle: return "待機";
                case TestPhase.Load: return "CPU負荷";
                case TestPhase.Cooldown: return "冷却";
                case TestPhase.Complete: return "完了";
                case TestPhase.Aborted: return "中断";
                case TestPhase.SafetyStop: return "高温停止";
                case TestPhase.SensorLost: return "温度取得不能で停止";
                default: return "常時監視";
            }
        }
    }
}
