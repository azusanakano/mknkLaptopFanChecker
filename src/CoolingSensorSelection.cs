using System;
using System.Collections.Generic;
using System.Linq;

namespace Mknk.LaptopFanChecker
{
    internal sealed class CoolingSensorSelection
    {
        private string _fanKey;
        public void Reset() { _fanKey = null; }

        public static void SelectTemperature(IList<SensorRecord> records, SensorSnapshot snapshot)
        {
            List<SensorRecord> candidates = records.Where(r => Is(r.HardwareType, "Cpu") && Is(r.SensorType, "Temperature") &&
                Valid(r.Value, 0.01, 125.99) && !Contains(r.SensorName, "distance") && !Contains(r.SensorName, "tjmax")).ToList();
            // Tctl can include a control offset. Prefer the actual die temperature on the same CPU.
            HashSet<string> hasTdie = new HashSet<string>(candidates.Where(r => Contains(r.SensorName, "tdie")).Select(HardwareKey));
            SensorRecord selected = candidates.Where(r => !(Contains(r.SensorName, "tctl") && !Contains(r.SensorName, "tdie") && hasTdie.Contains(HardwareKey(r))))
                .OrderByDescending(r => r.Value.Value).FirstOrDefault();
            if (selected == null) return;
            snapshot.TemperatureC = selected.Value;
            snapshot.TemperatureSensorName = selected.SensorName + " / " + selected.HardwareName;
            snapshot.TemperatureIsCpuDirect = true;
        }

        public static void SelectCpuLoad(IList<SensorRecord> records, SensorSnapshot snapshot)
        {
            double sum = 0.0;
            int weights = 0;
            foreach (IGrouping<string, SensorRecord> cpu in records.Where(r => Is(r.HardwareType, "Cpu") && Is(r.SensorType, "Load") && Valid(r.Value, 0, 100.5)).GroupBy(HardwareKey))
            {
                SensorRecord total = cpu.FirstOrDefault(r => Contains(r.SensorName, "total"));
                List<SensorRecord> cores = cpu.Where(r => Contains(r.SensorName, "core") && !Contains(r.SensorName, "max") && !Contains(r.SensorName, "average")).ToList();
                if (total == null && cores.Count == 0) continue;
                int weight = Math.Max(1, cores.Count);
                sum += (total != null ? total.Value.Value : cores.Average(r => r.Value.Value)) * weight;
                weights += weight;
            }
            snapshot.CpuLoadPercent = weights == 0 ? null : (double?)(sum / weights);
        }

        public void SelectFan(IList<SensorRecord> records, SensorSnapshot snapshot)
        {
            List<SensorRecord> candidates = records.Where(r => Is(r.SensorType, "Fan") && IsCpuFan(r)).OrderBy(SensorKey, StringComparer.Ordinal).ToList();
            if (_fanKey == null && candidates.Count > 0)
                _fanKey = SensorKey(candidates[0]);
            SensorRecord selected = candidates.FirstOrDefault(r => SensorKey(r) == _fanKey);
            if (selected == null || !Valid(selected.Value, 0, 30000)) return;
            snapshot.FanSensorAvailable = true;
            snapshot.FanRpm = selected.Value;
            snapshot.FanSensorName = selected.SensorName + " / " + selected.HardwareName;
            SensorRecord control = records.FirstOrDefault(r => Is(r.SensorType, "Control") &&
                HardwareKey(r) == HardwareKey(selected) && Is(r.SensorName, selected.SensorName) && Valid(r.Value, 0, 100));
            if (control != null) snapshot.FanControlPercent = control.Value;
        }

        private static bool IsCpuFan(SensorRecord record)
        {
            string name = (record.SensorName ?? String.Empty).ToLowerInvariant();
            if (Contains(record.HardwareType, "gpu") || name.Contains("gpu") || name.Contains("pump") || name.Contains("ポンプ") ||
                name.Contains("chassis") || name.Contains("case") || name.Contains("system") || name.Contains("sys") || name.Contains("psu") || name.Contains("aio"))
                return false;
            return name.StartsWith("cpu", StringComparison.Ordinal) || name.StartsWith("processor", StringComparison.Ordinal) || Is(record.HardwareType, "Cpu");
        }

        private static string HardwareKey(SensorRecord record) { return record.HardwareIdentifier ?? record.Path ?? record.HardwareName ?? String.Empty; }
        private static string SensorKey(SensorRecord record) { return record.Identifier ?? (HardwareKey(record) + "|" + record.SensorName); }
        private static bool Contains(string value, string token) { return (value ?? String.Empty).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0; }
        private static bool Is(string left, string right) { return String.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        private static bool Valid(double? value, double min, double max) { return value.HasValue && value.Value >= min && value.Value <= max; }
    }
}
