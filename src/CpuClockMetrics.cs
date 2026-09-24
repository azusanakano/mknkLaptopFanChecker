using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Mknk.LaptopFanChecker
{
    internal sealed class SensorRecord
    {
        public string HardwareIdentifier { get; set; }
        public string Identifier { get; set; }
        public string HardwareType;
        public string HardwareName;
        public string Path;
        public string SensorName;
        public string SensorType;
        public double? Value;
    }

    internal static class CpuClockMetrics
    {
        // Only individual core clocks: exclude bus, uncore, GPU, and effective clocks.
        private static readonly Regex CoreName = new Regex(
            @"^(?:(?:CPU )?Core|[PE]-Core)(?: #\d+)?(?: \([PE]-Core\))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static double? SelectGHz(IEnumerable<SensorRecord> records)
        {
            List<SensorRecord> clocks = records.Where(r =>
                String.Equals(r.HardwareType, "Cpu", StringComparison.OrdinalIgnoreCase) &&
                String.Equals(r.SensorType, "Clock", StringComparison.OrdinalIgnoreCase) &&
                r.Value.HasValue && r.Value.Value > 0.0 && r.Value.Value <= 10000.0).ToList();
            List<double> values = clocks.Where(r => CoreName.IsMatch(r.SensorName ?? String.Empty))
                .Select(r => r.Value.Value).ToList();
            if (values.Count == 0)
            {
                values = clocks.Where(r => String.Equals(r.SensorName, "Cores (Average)", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Value.Value).ToList();
            }
            return values.Count == 0 ? null : (double?)(values.Average() / 1000.0);
        }

        public static string FormatGHz(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) + " GHz" : "取得不可";
        }
    }
}
