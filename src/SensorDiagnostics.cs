using System;

namespace Mknk.LaptopFanChecker
{
    public static class SensorDiagnostics
    {
        public static string ExplainMissingTemperature(
            Version installedDriverVersion,
            Version requiredDriverVersion,
            bool driverLoaded,
            bool processElevated,
            int cpuTemperatureSensorCount)
        {
            if (installedDriverVersion == null)
                return "PawnIOセンサードライバー未導入";

            if (requiredDriverVersion != null && installedDriverVersion < requiredDriverVersion)
                return "PawnIO " + installedDriverVersion + " は古いため " + requiredDriverVersion + " 以上へ更新が必要";

            if (!processElevated)
                return "管理者権限がないためPawnIOへ接続できません";

            if (!driverLoaded)
                return "PawnIOドライバーへ接続できません（PC再起動またはドライバー修復が必要）";

            if (cpuTemperatureSensorCount > 0)
                return "CPU温度センサーは検出しましたが値を読めません（BIOS・他ソフト競合を確認）";

            return "CPU温度センサーを列挙できません（BIOS・機種対応を確認）";
        }
    }
}
