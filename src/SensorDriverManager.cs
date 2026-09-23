using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;

namespace Mknk.LaptopFanChecker
{
    public static class SensorDriverManager
    {
        private const string ExpectedInstallerSha256 = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
        private static readonly Version RequiredVersion = new Version(2, 2, 0, 0);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        public static Version MinimumSupportedVersion
        {
            get { return RequiredVersion; }
        }

        public static bool IsInstalled()
        {
            return InstalledVersion() != null;
        }

        public static Version InstalledVersion()
        {
            try
            {
                Version libraryVersion = LibreHardwareMonitor.PawnIo.PawnIo.Version;
                if (libraryVersion != null)
                    return libraryVersion;
            }
            catch { }

            Version result = ReadVersion(RegistryView.Registry64);
            if (result != null)
                return result;
            return ReadVersion(RegistryView.Registry32);
        }

        public static bool IsLoaded()
        {
            try
            {
                const uint GenericReadWrite = 0xC0000000;
                const uint ShareReadWriteDelete = 0x00000007;
                const uint OpenExisting = 3;
                using (SafeFileHandle handle = CreateFile(@"\\.\PawnIO", GenericReadWrite, ShareReadWriteDelete,
                    IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
                {
                    return handle != null && !handle.IsInvalid;
                }
            }
            catch { return false; }
        }

        public static bool IsProcessElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool OfferInstallOrUpdate(bool startupMonitorMode)
        {
            return OfferInstallOrUpdate(startupMonitorMode, false);
        }

        public static bool OfferInstallOrUpdate(bool startupMonitorMode, bool forceRepair)
        {
            Version installed = InstalledVersion();
            bool update = installed != null && installed < RequiredVersion;
            if (installed != null && !update && !forceRepair)
                return true;

            string reason = forceRepair && installed != null && !update
                ? "CPU温度を取得できないため、公式PawnIOドライバー " + installed + " を修復インストールします。"
                : update
                ? "CPU温度とファンRPMの取得に使う公式PawnIOドライバー " + installed + " は古いため、" + RequiredVersion + " へ更新が必要です。"
                : "CPU温度とファンRPMの取得には、公式PawnIO " + RequiredVersion + " センサードライバーが必要です。";
            string extra = startupMonitorMode
                ? "\r\n\r\n自動監視を有効にするには導入を推奨します。"
                : "\r\n\r\n導入しない場合も起動できますが、温度・RPMを取得できない機種があります。";
            DialogResult answer = MessageBox.Show(
                reason + extra + "\r\n\r\n公式ドライバーを今すぐ" + (forceRepair && !update ? "修復" : update ? "更新" : "導入") + "しますか？",
                "センサードライバーの確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return false;

            string installer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PawnIO_setup.exe");
            if (!File.Exists(installer))
            {
                MessageBox.Show("PawnIO_setup.exe が見つかりません。配布ZIPを展開し直してください。", "ドライバー導入エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            string actualHash;
            try { actualHash = Sha256(installer); }
            catch (Exception ex)
            {
                MessageBox.Show("ドライバーの検証に失敗しました。\r\n" + ex.Message, "ドライバー導入エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (!String.Equals(actualHash, ExpectedInstallerSha256, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("PawnIO_setup.exe のSHA-256が公式配布物と一致しないため、実行を拒否しました。\r\nZIPを再取得してください。", "安全のため停止", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            try
            {
                bool rebootRequired;
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = installer,
                    Arguments = "-install -silent",
                    Verb = "runas",
                    UseShellExecute = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                using (Process process = Process.Start(info))
                {
                    if (process == null || !process.WaitForExit(120000))
                    {
                        MessageBox.Show("ドライバー導入が時間内に完了しませんでした。", "ドライバー導入エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    rebootRequired = process.ExitCode == 3010;
                    if (process.ExitCode != 0 && !rebootRequired)
                    {
                        MessageBox.Show("ドライバー導入プログラムがエラーを返しました（終了コード " + process.ExitCode + "）。", "ドライバー導入エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                }

                Version after = InstalledVersion();
                if (after == null || after < RequiredVersion)
                {
                    MessageBox.Show("導入完了を確認できませんでした。PCを再起動してから再度お試しください。", "ドライバー確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (rebootRequired)
                {
                    MessageBox.Show("PawnIO " + after + " へ更新しました。ドライバーを有効にするため、PCを再起動してください。", "再起動が必要です", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return true;
                }
                MessageBox.Show("PawnIOセンサードライバー " + after + " を導入しました。", "導入完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("ドライバーを導入できませんでした。\r\n" + ex.Message, "ドライバー導入エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static Version ReadVersion(RegistryView view)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
                {
                    if (key == null)
                        return null;
                    Version version;
                    return Version.TryParse(Convert.ToString(key.GetValue("DisplayVersion")), out version) ? version : new Version(1, 0);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string Sha256(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
