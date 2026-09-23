using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("ネコシステム社 ノートPC CPUファンチェッカー")]
[assembly: AssemblyDescription("ノートPC専用 CPUファン・異常発熱検査ツール")]
[assembly: AssemblyCompany("ネコシステム社")]
[assembly: AssemblyProduct("ネコシステム社 ノートPC CPUファンチェッカー")]
[assembly: AssemblyCopyright("Copyright © 2026 ネコシステム社")]
[assembly: AssemblyVersion(Mknk.LaptopFanChecker.AppInfo.AssemblyVersion)]
[assembly: AssemblyFileVersion(Mknk.LaptopFanChecker.AppInfo.AssemblyVersion)]
[assembly: AssemblyInformationalVersion(Mknk.LaptopFanChecker.AppInfo.Version)]

namespace Mknk.LaptopFanChecker
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool demo = args.Any(a => String.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase));
            bool monitor = args.Any(a => String.Equals(a, "--monitor", StringComparison.OrdinalIgnoreCase));
            if (!demo && !Environment.Is64BitOperatingSystem)
            {
                MessageBox.Show("この版は64bit版 Windows 10/11 専用です。", "mknkLaptopFanChecker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\mknkLaptopFanChecker-7B0AA03D", out created))
            {
                if (!created)
                {
                    if (!monitor)
                        MessageBox.Show("CPUファンチェッカーはすでに起動しています。タスクトレイを確認してください。", "mknkLaptopFanChecker", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show("予期しないエラーが発生しました。\r\n" + e.Exception.Message, "mknkLaptopFanChecker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                ISensorReader reader = null;
                try
                {
                    // ログオン時は登録済みの最高権限タスクで起動するため、確認画面は表示しません。
                    if (!demo && !monitor)
                        SensorDriverManager.OfferInstallOrUpdate(false);
                    reader = demo ? (ISensorReader)new DemoSensorReader() : new LibreHardwareSensorReader();
                    Application.Run(new MainForm(reader, monitor, demo));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("起動できませんでした。\r\n" + ex.Message, "mknkLaptopFanChecker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (reader != null)
                        reader.Dispose();
                }
            }
        }
    }
}
