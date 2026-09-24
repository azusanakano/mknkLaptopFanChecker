using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mknk.LaptopFanChecker
{
    public sealed partial class MainForm
    {
        private SmartBoostPolicy _boostPolicy;
        private Task<MemoryBoostResult> _boostTask;
        private CancellationTokenSource _boostCancellation;
        private DateTime _nextMemoryCheckUtc;
        private Label _memoryLabel;
        private Label _boostStatusLabel;
        private CheckBox _boostEnabledCheck;
        private bool _closing;

        private Control BuildSmartBoostGroup()
        {
            _boostPolicy = new SmartBoostPolicy(_settings.LastSmartBoostAttemptUtc);
            GroupBox group = NewGroup("スマートブースト（メモリ）", 310, 178);
            _boostEnabledCheck = new CheckBox
            {
                Left = 14, Top = 25, Width = 278, Height = 26,
                Text = "メモリ整理を自動実行", ForeColor = TextPrimary,
                Checked = !_demoMode && _settings.SmartBoostEnabled, Enabled = !_demoMode
            };
            _boostEnabledCheck.CheckedChanged += delegate
            {
                _settings.SmartBoostEnabled = _boostEnabledCheck.Checked;
                if (!_settings.SmartBoostEnabled && _boostCancellation != null)
                    _boostCancellation.Cancel();
                _boostPolicy.OnEnabledChanged(_settings.SmartBoostEnabled);
                _boostStatusLabel.Text = _settings.SmartBoostEnabled ? "メモリ使用率を監視中" : "自動実行OFF";
                SaveSettingsSafely();
                if (_settings.SmartBoostEnabled)
                    UpdateSmartBoost();
            };
            _memoryLabel = new Label { Left = 14, Top = 54, Width = 278, Height = 22, ForeColor = Accent, Text = "メモリ使用率: 確認中" };
            _boostStatusLabel = new Label
            {
                Left = 14, Top = 78, Width = 278, Height = 36, ForeColor = TextPrimary,
                Text = _demoMode ? "デモでは実行しません" : (_settings.SmartBoostEnabled ? "メモリ使用率を監視中" : "自動実行OFF"), AutoEllipsis = true
            };
            Label note = new Label
            {
                Left = 14, Top = 119, Width = 278, Height = 48, ForeColor = TextSecondary,
                Font = new Font("Yu Gothic UI", 8.5F),
                Text = "ON時に即実行／以後85%以上30秒・30分間隔\r\nファンテスト中は終了後に実行します。\r\n整理後、一時的に動作が遅くなる場合があります。"
            };
            group.Controls.AddRange(new Control[] { _boostEnabledCheck, _memoryLabel, _boostStatusLabel, note });
            return group;
        }

        private bool BoostIsRunning { get { return _boostTask != null && !_boostTask.IsCompleted; } }

        private void UpdateSmartBoost()
        {
            if (_closing || _memoryLabel == null)
                return;
            if (_boostTask != null && _boostTask.IsCompleted)
            {
                try
                {
                    MemoryBoostResult result = _boostTask.GetAwaiter().GetResult();
                    string usage = result.Before != null && result.After != null
                        ? String.Format(" / 使用率 {0:0}% → {1:0}%", result.Before.UsedPercent, result.After.UsedPercent) : String.Empty;
                    _boostStatusLabel.Text = DateTime.Now.ToString("HH:mm") +
                        (result.Cancelled ? " 中止" : " 整理 " + result.TrimmedProcesses + "件") + usage;
                }
                catch (Exception ex) { _boostStatusLabel.Text = "メモリ整理を完了できません: " + ex.Message; }
                _boostTask = null;
                _boostCancellation.Dispose();
                _boostCancellation = null;
                _startButton.Enabled = !IsTestActive();
            }
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextMemoryCheckUtc && !_boostPolicy.HasPendingImmediateRun)
                return;
            _nextMemoryCheckUtc = nowUtc.AddSeconds(5);
            MemoryReading memory = _demoMode
                ? new MemoryReading { TotalBytes = 16UL * 1024 * 1024 * 1024, AvailableBytes = 9UL * 1024 * 1024 * 1024 }
                : MemoryBoost.ReadMemory();
            _memoryLabel.Text = memory == null ? "メモリ使用率: 取得不可" :
                String.Format("メモリ使用率: {0:0}%（{1:0.0} GB中）", memory.UsedPercent, memory.TotalBytes / 1073741824.0);
            if (_settings.SmartBoostEnabled && (_startingTest || IsTestActive()))
                _boostStatusLabel.Text = "ファンテスト中のため待機";
            else if (_settings.SmartBoostEnabled && BoostIsRunning && _boostPolicy.HasPendingImmediateRun)
                _boostStatusLabel.Text = "実行中の整理が完了したら実行します。";
            else if (_boostStatusLabel.Text == "ファンテスト中のため待機")
                _boostStatusLabel.Text = _settings.SmartBoostEnabled ? "メモリ使用率を監視中" : "自動実行OFF";
            bool blocked = _demoMode || _startingTest || IsTestActive() || BoostIsRunning;
            if (!_boostPolicy.ShouldRun(nowUtc, memory == null ? null : (double?)memory.UsedPercent, _settings.SmartBoostEnabled, blocked))
                return;
            _settings.LastSmartBoostAttemptUtc = _boostPolicy.LastAttemptUtc;
            if (!SaveSettingsSafely())
            {
                _boostStatusLabel.Text = "実行時刻を保存できないため、整理を見送りました。";
                return;
            }
            _boostStatusLabel.Text = "バックグラウンドアプリのメモリを整理中…";
            _startButton.Enabled = false;
            _boostCancellation = new CancellationTokenSource();
            CancellationToken token = _boostCancellation.Token;
            _boostTask = Task.Factory.StartNew(() => MemoryBoost.Run(token), token, TaskCreationOptions.None, TaskScheduler.Default);
        }
    }
}
