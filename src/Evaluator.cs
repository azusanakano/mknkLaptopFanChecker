using System;
using System.Collections.Generic;
using System.Linq;

namespace Mknk.LaptopFanChecker
{
    public static class FanTestEvaluator
    {
        public static EvaluationResult Evaluate(
            IList<TelemetrySample> samples,
            double safetyCutoffC,
            bool airflowIncreaseConfirmed,
            bool abnormalNoise,
            TestPhase finalPhase)
        {
            EvaluationResult result = new EvaluationResult();
            result.SafetyCutoffC = safetyCutoffC;
            result.CompletedAllPhases = finalPhase == TestPhase.Complete;

            List<TelemetrySample> idle = samples.Where(s => s.Phase == TestPhase.Idle).ToList();
            List<TelemetrySample> load = samples.Where(s => s.Phase == TestPhase.Load).ToList();
            List<TelemetrySample> cool = samples.Where(s => s.Phase == TestPhase.Cooldown).ToList();

            result.BaselineTemperatureC = Median(LastValues(idle, s => s.TemperatureC, 10));
            result.MaximumTemperatureC = Maximum(samples, s => s.TemperatureC);
            result.CooldownEndTemperatureC = Median(LastValues(cool, s => s.TemperatureC, 10));
            if (result.MaximumTemperatureC.HasValue && result.CooldownEndTemperatureC.HasValue)
                result.CooldownDropC = result.MaximumTemperatureC.Value - result.CooldownEndTemperatureC.Value;

            bool fanSensorAvailable = load.Count >= 5 && load.All(s => s.FanSensorAvailable && s.FanRpm.HasValue && s.FanRpm.Value >= 0.0) &&
                samples.Where(s => s.FanSensorAvailable).Select(s => s.FanSensorName).Distinct().Count() <= 1;
            result.DirectRpmAssessment = fanSensorAvailable;
            result.BaselineFanRpm = Median(LastValues(idle, s => s.FanRpm, 10));
            result.MaximumFanRpm = Maximum(load, s => s.FanRpm);
            if (result.BaselineFanRpm.HasValue && result.MaximumFanRpm.HasValue)
                result.FanIncreaseRpm = result.MaximumFanRpm.Value - result.BaselineFanRpm.Value;

            result.LoadEndTemperatureSlopePerSecond = EndSlope(load, 20);

            if (finalPhase == TestPhase.SensorLost)
            {
                SetUnknown(result, "検査停止：温度センサーを取得できません", "安全にCPU温度を監視できなくなったため、負荷を停止しました。");
                result.RecommendedAction = "センサー接続を再検出し、CPU温度を確認してから再検査してください。";
                return result;
            }

            if (finalPhase == TestPhase.Aborted)
            {
                SetUnknown(result, "テスト中断", "使用者がテストを途中で停止したため、判定に必要な全工程がありません。");
                result.RecommendedAction = "吸気・排気口を確認し、標準テストを最初から実行してください。";
                return result;
            }

            if (load.Count < 5 || !result.MaximumTemperatureC.HasValue)
            {
                SetUnknown(result, "判定不能", "CPU温度または負荷工程のデータが不足しています。");
                result.RecommendedAction = "管理者として再起動し、センサー一覧でCPU温度が取得できることを確認してください。";
                return result;
            }

            double maxTemp = result.MaximumTemperatureC.Value;
            double baselineTemp = result.BaselineTemperatureC.HasValue ? result.BaselineTemperatureC.Value : load.Where(s => s.TemperatureC.HasValue).Select(s => s.TemperatureC.Value).First();
            double tempRise = maxTemp - baselineTemp;
            double coolDrop = result.CooldownDropC.HasValue ? result.CooldownDropC.Value : 0.0;
            double slope = result.LoadEndTemperatureSlopePerSecond.HasValue ? result.LoadEndTemperatureSlopePerSecond.Value : 0.0;

            if (finalPhase == TestPhase.SafetyStop || maxTemp >= safetyCutoffC)
            {
                result.Level = VerdictLevel.Suspect;
                result.Verdict = "要点検：高温保護停止";
                result.Basis = String.Format("CPU温度が保護停止値 {0:0}℃ に到達しました（最高 {1:0.0}℃）。", safetyCutoffC, maxTemp);
                result.Limitation = "設定した保護値による停止であり、故障を確定するものではありません。CPU固有のTjMaxやメーカー仕様を確認してください。";
                result.RecommendedAction = "冷却後に、ファン接続・埃詰まり・ヒートシンク密着・グリス・排気を点検してください。";
                return result;
            }

            if (abnormalNoise)
            {
                result.Level = VerdictLevel.Suspect;
                result.Verdict = "要点検：ファン異音";
                result.Basis = "負荷中の擦れ音・唸り・周期音が記録されています。回転していても軸受や羽根の異常はあり得ます。";
                result.Limitation = "異音の種類や発生部位はソフトウェアだけでは特定できません。";
                result.RecommendedAction = "電源を切り、羽根への接触、ケーブル、軸ぶれ、固定ねじを目視点検してください。";
                return result;
            }

            if (fanSensorAvailable)
            {
                double baselineFan = result.BaselineFanRpm.HasValue ? result.BaselineFanRpm.Value : 0.0;
                double maxFan = result.MaximumFanRpm.HasValue ? result.MaximumFanRpm.Value : 0.0;
                double fanIncrease = maxFan - baselineFan;
                bool rotating = maxFan > 0.0;
                bool responded = (maxFan >= 500.0 && fanIncrease >= 200.0) || (baselineFan < 100.0 && maxFan >= 600.0);
                bool coolingResponse = coolDrop >= 3.0;
                bool stabilized = slope <= 0.12 && maxTemp <= safetyCutoffC - 3.0;

                if (rotating && (responded || coolingResponse || stabilized))
                {
                    result.Level = VerdictLevel.Normal;
                    result.Verdict = "正常傾向：回転数を直接確認";
                    result.Basis = String.Format(
                        "負荷中に最大 {0:0} RPM を検出。待機時から {1:+0;-0;0} RPM、冷却工程で {2:0.0}℃低下しました。",
                        maxFan, fanIncrease, coolDrop);
                    result.Limitation = "単回テストです。断続故障、異音、長時間負荷時の性能までは保証しません。";
                    result.RecommendedAction = "検査記録を保存し、異音がなければ通常のPC再生工程へ進めます。";
                    return result;
                }

                if (!rotating && maxTemp >= 80.0)
                {
                    result.Level = VerdictLevel.Suspect;
                    result.Verdict = "異常疑い：負荷中の回転を確認できず";
                    result.Basis = String.Format(
                        "CPU温度が最高 {0:0.0}℃（待機時比 +{1:0.0}℃）まで上昇しましたが、有効なファン回転を検出できませんでした。",
                        maxTemp, tempRise);
                    result.Limitation = "ECやBIOSがRPMを正しく公開しない機種では、実際に回転していても0 RPMと表示されることがあります。";
                    result.RecommendedAction = "排気の風と作動音を確認し、BIOS診断またはメーカー診断も併用してください。";
                    return result;
                }

                result.Level = VerdictLevel.Caution;
                result.Verdict = "再確認推奨：回転応答が小さい";
                result.Basis = String.Format(
                    "回転数センサーはありますが、明確な増速または冷却低下を確認できませんでした（最大 {0:0} RPM、温度低下 {1:0.0}℃）。",
                    maxFan, coolDrop);
                result.Limitation = "静音停止、ファンレス、水冷、低負荷、すでに高回転だった場合にも同じ結果になります。冷却構成を確認してください。";
                result.RecommendedAction = "精密テストを実行し、排気の変化とメーカー診断を照合してください。";
                return result;
            }

            bool indirectCooling = coolDrop >= 4.0;
            bool indirectStable = slope <= 0.08 && maxTemp < 88.0;
            bool manualSupport = airflowIncreaseConfirmed && coolDrop >= 2.0;

            if ((maxTemp >= safetyCutoffC - 3.0 && coolDrop < 2.0) || (maxTemp > 88.0 && slope > 0.18))
            {
                result.Level = VerdictLevel.Suspect;
                result.Verdict = "要点検：冷却不足の疑い（間接判定）";
                result.Basis = String.Format(
                    "RPMは取得できず、最高 {0:0.0}℃、負荷終盤の温度勾配 {1:+0.00;-0.00;0.00}℃/秒、冷却低下 {2:0.0}℃でした。",
                    maxTemp, slope, coolDrop);
                result.Limitation = "ファン停止そのものを直接確認した結果ではありません。ヒートシンクや吸排気の問題も同じ挙動になります。";
                result.RecommendedAction = "排気と作動音を確認後、埃詰まり・グリス・ヒートシンク密着・ファン接続を点検してください。";
                return result;
            }

            if (indirectCooling || indirectStable || manualSupport)
            {
                result.Level = VerdictLevel.Normal;
                result.Verdict = airflowIncreaseConfirmed
                    ? "正常傾向：冷却挙動＋排気確認（間接判定）"
                    : "正常傾向：冷却挙動を確認（間接判定）";
                result.Basis = String.Format(
                    "CPUファンのRPMを特定できませんが、最高 {0:0.0}℃、負荷終盤 {1:+0.00;-0.00;0.00}℃/秒、冷却工程で {2:0.0}℃低下しました。",
                    maxTemp, slope, coolDrop);
                result.Limitation = "温度挙動からの推定であり、ファン回転・軸受・異音を直接証明するものではありません。";
                result.RecommendedAction = "排気が増えることと異音がないことを人が確認し、レポートに残してください。";
                return result;
            }

            result.Level = VerdictLevel.Caution;
            result.Verdict = "判定保留：RPM未特定・温度差不足";
            result.Basis = String.Format(
                "RPMを取得できず、冷却工程の温度低下は {0:0.0}℃でした。短い測定では正常・異常を分けられません。",
                coolDrop);
            result.Limitation = "EC・BIOS・マザーボードがCPUファンのRPMを公開しない構成では、ソフトウェアだけでは限界があります。";
            result.RecommendedAction = "精密テストを行い、排気・作動音・BIOS診断を併用してください。";
            return result;
        }

        private static void SetUnknown(EvaluationResult result, string verdict, string basis)
        {
            result.Level = VerdictLevel.Unknown;
            result.Verdict = verdict;
            result.Basis = basis;
            result.Limitation = "判定に必要な測定条件がそろっていません。";
        }

        private static IEnumerable<double> LastValues(
            IEnumerable<TelemetrySample> source,
            Func<TelemetrySample, double?> selector,
            int count)
        {
            return source.Where(s => selector(s).HasValue)
                .OrderByDescending(s => s.ElapsedSeconds)
                .Take(count)
                .Select(s => selector(s).Value);
        }

        private static double? Median(IEnumerable<double> source)
        {
            List<double> values = source.OrderBy(v => v).ToList();
            if (values.Count == 0)
                return null;
            int middle = values.Count / 2;
            if ((values.Count % 2) == 1)
                return values[middle];
            return (values[middle - 1] + values[middle]) / 2.0;
        }

        private static double? Maximum(
            IEnumerable<TelemetrySample> source,
            Func<TelemetrySample, double?> selector)
        {
            List<double> values = source.Where(s => selector(s).HasValue).Select(s => selector(s).Value).ToList();
            if (values.Count == 0)
                return null;
            return values.Max();
        }

        private static double? EndSlope(IList<TelemetrySample> source, int maxPoints)
        {
            List<TelemetrySample> values = source.Where(s => s.TemperatureC.HasValue)
                .OrderByDescending(s => s.ElapsedSeconds)
                .Take(maxPoints)
                .OrderBy(s => s.ElapsedSeconds)
                .ToList();
            if (values.Count < 3)
                return null;

            double meanX = values.Average(s => s.ElapsedSeconds);
            double meanY = values.Average(s => s.TemperatureC.Value);
            double numerator = 0.0;
            double denominator = 0.0;
            foreach (TelemetrySample sample in values)
            {
                double dx = sample.ElapsedSeconds - meanX;
                numerator += dx * (sample.TemperatureC.Value - meanY);
                denominator += dx * dx;
            }
            if (denominator < 0.0001)
                return null;
            return numerator / denominator;
        }
    }
}
