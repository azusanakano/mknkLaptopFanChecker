using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;

namespace Mknk.LaptopFanChecker
{
    public sealed class MainForm : Form
    {
        private readonly ISensorReader _reader;
        private readonly bool _startupMonitorMode;
        private readonly bool _demoMode;
        private readonly CpuStressRunner _stress;
        private readonly HeatAnomalyDetector _heatDetector;
        private readonly AppSettings _settings;
        private readonly List<TelemetrySample> _testSamples;
        private readonly List<TelemetrySample> _monitorSamples;
        private readonly List<TelemetrySample> _heatHistory;
        private readonly Timer _timer;
        private readonly NotifyIcon _trayIcon;
        private readonly Icon _applicationIcon;

        private TestPhase _phase;
        private TestDurations _durations;
        private DateTime _phaseStarted;
        private DateTime _testStarted;
        private DateTime _monitorStarted;
        private SensorSnapshot _lastSnapshot;
        private EvaluationResult _evaluation;
        private bool _allowExit;
        private bool _initializingStartup;
        private bool _shownTrayHint;

        private Label _deviceLabel;
        private Label _sourceLabel;
        private Label _tempValue;
        private Label _tempDetail;
        private Label _loadValue;
        private Label _fanValue;
        private Label _fanDetail;
        private Label _monitorValue;
        private Label _monitorDetail;
        private Label _phaseLabel;
        private Label _countdownLabel;
        private Label _resultTitle;
        private Label _resultBasis;
        private Label _resultLimit;
        private Label _statusLabel;
        private Panel _resultPanel;
        private ProgressBar _progress;
        private TelemetryChart _chart;
        private Button _startButton;
        private Button _stopButton;
        private Button _reportButton;
        private Button _repairSensorButton;
        private ComboBox _presetCombo;
        private ComboBox _loadCombo;
        private NumericUpDown _safetyCutoff;
        private NumericUpDown _heatAlertThreshold;
        private CheckBox _airflowCheck;
        private CheckBox _noiseCheck;
        private CheckBox _startupCheck;
        private CheckBox _heatMonitorCheck;

        private static readonly Color WindowBackground = Color.FromArgb(14, 20, 31);
        private static readonly Color PanelBackground = Color.FromArgb(25, 34, 49);
        private static readonly Color CardBackground = Color.FromArgb(31, 43, 61);
        private static readonly Color TextPrimary = Color.FromArgb(238, 244, 252);
        private static readonly Color TextSecondary = Color.FromArgb(164, 181, 204);
        private static readonly Color Accent = Color.FromArgb(56, 189, 248);
        private static readonly Color Green = Color.FromArgb(36, 196, 129);
        private static readonly Color Amber = Color.FromArgb(244, 173, 53);
        private static readonly Color Red = Color.FromArgb(245, 85, 104);

        public MainForm(ISensorReader reader, bool startupMonitorMode, bool demoMode)
        {
            _reader = reader;
            _startupMonitorMode = startupMonitorMode;
            _demoMode = demoMode;
            _stress = new CpuStressRunner();
            _heatDetector = new HeatAnomalyDetector();
            _settings = AppSettings.Load();
            _testSamples = new List<TelemetrySample>();
            _monitorSamples = new List<TelemetrySample>();
            _heatHistory = new List<TelemetrySample>();
            _phase = TestPhase.Monitoring;
            _monitorStarted = DateTime.Now;

            Text = AppInfo.ProductName + " " + AppInfo.VersionLabel + (_demoMode ? " [デモ]" : "");
            _applicationIcon = LoadApplicationIcon();
            Icon = (Icon)_applicationIcon.Clone();
            BackColor = WindowBackground;
            ForeColor = TextPrimary;
            Font = new Font("Yu Gothic UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            MinimumSize = new Size(1040, 700);
            Size = new Size(1450, 1100);
            StartPosition = FormStartPosition.CenterScreen;

            BuildInterface();
            _trayIcon = BuildTrayIcon();
            _timer = new Timer { Interval = 1000 };
            _timer.Tick += TimerTick;

            Shown += OnShown;
            Resize += OnResize;
            FormClosing += OnFormClosing;
        }

        private void BuildInterface()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(18, 14, 18, 10),
                BackColor = WindowBackground
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildDeviceBar(), 0, 1);
            root.Controls.Add(BuildMetricCards(), 0, 2);

            SplitContainer split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Size = new Size(1100, 500),
                SplitterWidth = 8,
                BackColor = WindowBackground,
                Panel1MinSize = 600,
                Panel2MinSize = 320,
                FixedPanel = FixedPanel.Panel2
            };
            split.SplitterDistance = 790;
            split.Panel1.BackColor = WindowBackground;
            split.Panel2.BackColor = WindowBackground;
            split.Panel1.Controls.Add(BuildMainArea());
            split.Panel2.Controls.Add(BuildControlPanel());
            root.Controls.Add(split, 0, 3);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "センサーを初期化しています…"
            };
            root.Controls.Add(_statusLabel, 0, 4);
        }

        private Control BuildHeader()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = WindowBackground };
            Label title = new Label
            {
                AutoSize = true,
                Text = AppInfo.ProductName,
                Font = new Font("Yu Gothic UI", 20F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(2, 2)
            };
            Label subtitle = new Label
            {
                AutoSize = true,
                Text = "回転数・温度応答・冷却挙動を記録し、異常発熱も常時監視",
                Font = new Font("Yu Gothic UI", 9.5F),
                ForeColor = TextSecondary,
                Location = new Point(5, 43)
            };
            Label mode = new Label
            {
                AutoSize = true,
                Text = _demoMode ? "DEMO MODE" : "LAPTOP ONLY",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = _demoMode ? Amber : Accent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(920, 15)
            };
            Label version = new Label
            {
                Name = "appVersionLabel",
                AutoSize = true,
                Text = AppInfo.VersionLabel,
                AccessibleName = "アプリのバージョン " + AppInfo.Version,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(920, 40)
            };
            panel.Resize += delegate
            {
                mode.Left = panel.Width - mode.Width - 8;
                version.Left = panel.Width - version.Width - 8;
            };
            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            panel.Controls.Add(mode);
            panel.Controls.Add(version);
            return panel;
        }

        private Control BuildDeviceBar()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = PanelBackground, Padding = new Padding(12, 4, 12, 4) };
            _deviceLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = _reader.Profile.DisplayName + "   |   " + _reader.Profile.CpuName + "   |   基本速度 " + _reader.Profile.CpuBaseSpeedText,
                AutoEllipsis = true
            };
            _sourceLabel = new Label
            {
                Dock = DockStyle.Right,
                Width = 260,
                ForeColor = Accent,
                TextAlign = ContentAlignment.MiddleRight,
                Text = "センサー確認中"
            };
            panel.Controls.Add(_deviceLabel);
            panel.Controls.Add(_sourceLabel);
            return panel;
        }

        private Control BuildMetricCards()
        {
            TableLayoutPanel table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                Padding = new Padding(0, 8, 0, 8)
            };
            for (int i = 0; i < 4; i++)
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            table.Controls.Add(BuildMetricCard("CPU 温度", out _tempValue, out _tempDetail, Accent), 0, 0);
            Label loadDetail;
            table.Controls.Add(BuildMetricCard("CPU 負荷", out _loadValue, out loadDetail, Color.FromArgb(255, 174, 66)), 1, 0);
            loadDetail.Text = "リアルタイム";
            table.Controls.Add(BuildMetricCard("CPU ファン", out _fanValue, out _fanDetail, Color.FromArgb(217, 111, 255)), 2, 0);
            table.Controls.Add(BuildMetricCard("異常発熱監視", out _monitorValue, out _monitorDetail, Green), 3, 0);
            return table;
        }

        private Panel BuildMetricCard(string caption, out Label value, out Label detail, Color color)
        {
            Panel card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CardBackground,
                Margin = new Padding(4),
                Padding = new Padding(14, 10, 12, 8)
            };
            Label captionLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Text = caption,
                ForeColor = TextSecondary,
                Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold)
            };
            value = new Label
            {
                Dock = DockStyle.Top,
                Height = 43,
                Text = "—",
                ForeColor = color,
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            detail = new Label
            {
                Dock = DockStyle.Fill,
                Text = "未検出",
                ForeColor = TextSecondary,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(detail);
            card.Controls.Add(value);
            card.Controls.Add(captionLabel);
            return card;
        }

        private Control BuildMainArea()
        {
            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(0, 0, 6, 0)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            _chart = new TelemetryChart { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            panel.Controls.Add(_chart, 0, 0);

            _resultPanel = new Panel { Dock = DockStyle.Fill, BackColor = PanelBackground, Padding = new Padding(18, 14, 18, 12) };
            _resultTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                Font = new Font("Yu Gothic UI", 15F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Text = "常時監視中"
            };
            _resultBasis = new Label
            {
                Dock = DockStyle.Top,
                Height = 62,
                ForeColor = TextPrimary,
                Text = "CPU温度を監視しています。自動テストを実行すると、ファン応答を判定します。"
            };
            _resultLimit = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = TextSecondary,
                Text = "RPM非公開機種では、温度と冷却勾配による間接判定になります。",
                AutoEllipsis = true
            };
            _resultPanel.Controls.Add(_resultLimit);
            _resultPanel.Controls.Add(_resultBasis);
            _resultPanel.Controls.Add(_resultTitle);
            panel.Controls.Add(_resultPanel, 0, 1);
            return panel;
        }

        private Control BuildControlPanel()
        {
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = WindowBackground, Padding = new Padding(2, 0, 0, 0) };
            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = WindowBackground,
                Padding = new Padding(0)
            };
            scroll.Controls.Add(flow);

            flow.Controls.Add(BuildTestGroup());
            flow.Controls.Add(BuildObservationGroup());
            flow.Controls.Add(BuildMonitoringGroup());
            flow.Controls.Add(BuildToolsGroup());
            return scroll;
        }

        private GroupBox BuildTestGroup()
        {
            GroupBox group = NewGroup("自動CPUファンテスト", 310, 250);
            Label presetLabel = SmallLabel("検査時間", 14, 30);
            _presetCombo = new ComboBox { Left = 112, Top = 26, Width = 178, DropDownStyle = ComboBoxStyle.DropDownList };
            _presetCombo.Items.AddRange(new object[] { "クイック（70秒）", "標準（140秒）", "精密（270秒）" });
            _presetCombo.SelectedIndex = _demoMode ? 0 : 1;

            Label loadLabel = SmallLabel("CPU負荷", 14, 66);
            _loadCombo = new ComboBox { Left = 112, Top = 62, Width = 178, DropDownStyle = ComboBoxStyle.DropDownList };
            _loadCombo.Items.AddRange(new object[] { "70%", "80%（推奨）", "90%" });
            _loadCombo.SelectedIndex = 1;

            Label cutoffLabel = SmallLabel("保護停止温度", 14, 102);
            _safetyCutoff = new NumericUpDown { Left = 210, Top = 98, Width = 80, Minimum = 80, Maximum = 100, Value = 92, DecimalPlaces = 0, TextAlign = HorizontalAlignment.Right };

            _phaseLabel = new Label { Left = 14, Top = 134, Width = 180, Height = 24, Text = "状態：待機", ForeColor = TextPrimary };
            _countdownLabel = new Label { Left = 205, Top = 134, Width = 85, Height = 24, Text = "—", ForeColor = Accent, TextAlign = ContentAlignment.MiddleRight };
            _progress = new ProgressBar { Left = 14, Top = 161, Width = 276, Height = 14, Minimum = 0, Maximum = 100 };

            _startButton = NewButton("自動テスト開始", 14, 190, 170, Accent, Color.FromArgb(5, 35, 50));
            _startButton.Click += StartTestClicked;
            _stopButton = NewButton("停止", 194, 190, 96, Color.FromArgb(78, 91, 110), TextPrimary);
            _stopButton.Enabled = false;
            _stopButton.Click += delegate { AbortTest(false); };

            group.Controls.AddRange(new Control[] { presetLabel, _presetCombo, loadLabel, _loadCombo, cutoffLabel, _safetyCutoff, _phaseLabel, _countdownLabel, _progress, _startButton, _stopButton });
            return group;
        }

        private GroupBox BuildObservationGroup()
        {
            GroupBox group = NewGroup("人による確認（判定へ反映）", 310, 112);
            _airflowCheck = new CheckBox { Left = 14, Top = 29, Width = 276, Height = 25, Text = "負荷中に排気が強くなった", ForeColor = TextPrimary };
            _noiseCheck = new CheckBox { Left = 14, Top = 61, Width = 276, Height = 25, Text = "擦れ音・唸り・周期音がある", ForeColor = Red };
            _airflowCheck.CheckedChanged += ObservationChanged;
            _noiseCheck.CheckedChanged += ObservationChanged;
            group.Controls.Add(_airflowCheck);
            group.Controls.Add(_noiseCheck);
            return group;
        }

        private GroupBox BuildMonitoringGroup()
        {
            GroupBox group = NewGroup("異常発熱の自動監視", 310, 178);
            _heatMonitorCheck = new CheckBox { Left = 14, Top = 28, Width = 276, Height = 25, Text = "常時監視を有効にする", ForeColor = TextPrimary, Checked = _settings.HeatMonitoringEnabled };
            _heatMonitorCheck.CheckedChanged += delegate
            {
                _settings.HeatMonitoringEnabled = _heatMonitorCheck.Checked;
                SaveSettingsSafely();
                UpdateMonitorCard();
            };

            Label alertLabel = SmallLabel("危険温度の通知", 14, 64);
            _heatAlertThreshold = new NumericUpDown
            {
                Left = 210,
                Top = 60,
                Width = 80,
                Minimum = 75,
                Maximum = 105,
                Value = (decimal)_settings.AbsoluteAlertC,
                DecimalPlaces = 0,
                TextAlign = HorizontalAlignment.Right
            };
            _heatAlertThreshold.ValueChanged += delegate
            {
                _settings.AbsoluteAlertC = (double)_heatAlertThreshold.Value;
                SaveSettingsSafely();
                UpdateMonitorCard();
            };

            _startupCheck = new CheckBox { Left = 14, Top = 98, Width = 276, Height = 25, Text = "Windowsログイン時に自動起動", ForeColor = TextPrimary };
            _startupCheck.CheckedChanged += StartupCheckChanged;
            Label note = new Label
            {
                Left = 14,
                Top = 125,
                Width = 276,
                Height = 40,
                Text = "ログオン時は管理者センサーを使い、負荷なしでタスクトレイ監視します。",
                ForeColor = TextSecondary
            };
            group.Controls.AddRange(new Control[] { _heatMonitorCheck, alertLabel, _heatAlertThreshold, _startupCheck, note });
            return group;
        }

        private GroupBox BuildToolsGroup()
        {
            GroupBox group = NewGroup("記録・ツール", 310, 187);
            _reportButton = NewButton("HTML＋CSVレポート保存", 14, 28, 276, Color.FromArgb(42, 70, 94), TextPrimary);
            _reportButton.Enabled = false;
            _reportButton.Click += SaveReportClicked;
            _repairSensorButton = NewButton("温度取得を再検出・修復", 14, 67, 276, Color.FromArgb(91, 73, 35), TextPrimary);
            _repairSensorButton.Click += RepairSensorAccess;
            Button sensors = NewButton("検出センサー一覧", 14, 106, 133, Color.FromArgb(42, 55, 75), TextPrimary);
            sensors.Click += delegate { ShowSensorList(); };
            Button guide = NewButton("安全な使い方", 157, 106, 133, Color.FromArgb(42, 55, 75), TextPrimary);
            guide.Click += delegate { ShowSafetyGuide(); };
            Button folder = NewButton("レポート保存先を開く", 14, 145, 276, Color.FromArgb(42, 55, 75), TextPrimary);
            folder.Click += delegate
            {
                try { Process.Start(ReportWriter.ResolveReportDirectory()); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "保存先", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            group.Controls.AddRange(new Control[] { _reportButton, _repairSensorButton, sensors, guide, folder });
            return group;
        }

        private GroupBox NewGroup(string text, int width, int height)
        {
            return new GroupBox
            {
                Text = text,
                Width = width,
                Height = height,
                ForeColor = TextPrimary,
                BackColor = PanelBackground,
                Margin = new Padding(0, 0, 0, 10),
                Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold)
            };
        }

        private Label SmallLabel(string text, int left, int top)
        {
            // Keep fixed-position labels clear of the input controls that begin at X=112.
            // The previous 150px width covered the left 52px of both ComboBox controls.
            return new Label { Text = text, Left = left, Top = top, Width = 94, Height = 24, ForeColor = TextSecondary, Font = new Font("Yu Gothic UI", 9F) };
        }

        private Button NewButton(string text, int left, int top, int width, Color back, Color fore)
        {
            return new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
        }

        private NotifyIcon BuildTrayIcon()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("画面を開く", null, delegate { ShowFromTray(); });
            menu.Items.Add("CPUファンテスト", null, delegate { ShowFromTray(); StartTestClicked(this, EventArgs.Empty); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("終了", null, delegate { ExitApplication(); });
            NotifyIcon icon = new NotifyIcon
            {
                Icon = _applicationIcon,
                Text = "ネコシステム社 CPUファン監視 " + AppInfo.VersionLabel,
                ContextMenuStrip = menu,
                Visible = true
            };
            icon.DoubleClick += delegate { ShowFromTray(); };
            return icon;
        }

        private void OnShown(object sender, EventArgs e)
        {
            bool startupEnabled = !_demoMode && StartupTaskManager.IsEnabled();
            string startupRepairError = null;
            if (startupEnabled && !_startupMonitorMode)
                startupRepairError = StartupTaskManager.RepairIfEnabled(Application.ExecutablePath);

            _initializingStartup = true;
            try { _startupCheck.Checked = startupEnabled && startupRepairError == null; }
            finally { _initializingStartup = false; }
            if (_demoMode)
            {
                _startupCheck.Checked = false;
                _startupCheck.Enabled = false;
            }

            if (startupRepairError != null)
            {
                MessageBox.Show(
                    "登録済みの自動起動を現在のアプリ保存場所へ更新できませんでした。\r\n\r\n" + startupRepairError +
                    "\r\n\r\n［Windowsログイン時に自動起動］をもう一度有効にしてください。",
                    "自動起動の修復が必要です",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            _timer.Start();
            TimerTick(this, EventArgs.Empty);
            if (_startupMonitorMode)
            {
                BeginInvoke(new Action(delegate
                {
                    WindowState = FormWindowState.Minimized;
                    MinimizeToTray(false);
                    bool temperatureAvailable = _lastSnapshot != null && _lastSnapshot.TemperatureC.HasValue;
                    _trayIcon.ShowBalloonTip(
                        5000,
                        temperatureAvailable ? "CPU温度監視を開始" : "CPU温度を取得できません",
                        temperatureAvailable
                            ? "異常発熱をタスクトレイで監視しています。負荷テストは実行しません。"
                            : "自動監視は起動しましたが、温度センサーがありません。ドライバーとセンサー一覧を確認してください。",
                        temperatureAvailable ? ToolTipIcon.Info : ToolTipIcon.Warning);
                }));
            }
        }

        private void TimerTick(object sender, EventArgs e)
        {
            SensorSnapshot snapshot;
            try
            {
                snapshot = _reader.Read();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "センサー取得エラー: " + ex.Message;
                return;
            }
            _lastSnapshot = snapshot;
            UpdateMetrics(snapshot);

            bool active = IsTestActive();
            if (active)
            {
                double elapsed = Math.Max(0.0, (snapshot.Timestamp - _testStarted).TotalSeconds);
                _testSamples.Add(ToSample(snapshot, _phase, elapsed));
                _chart.SetSamples(_testSamples);

                if (snapshot.TemperatureC.HasValue && snapshot.TemperatureC.Value >= (double)_safetyCutoff.Value)
                {
                    SafetyStop(snapshot.TemperatureC.Value);
                    return;
                }
                AdvanceTestIfNeeded();
                UpdateTestProgress();
            }
            else
            {
                double elapsed = Math.Max(0.0, (snapshot.Timestamp - _monitorStarted).TotalSeconds);
                TelemetrySample monitoringSample = ToSample(snapshot, TestPhase.Monitoring, elapsed);
                _monitorSamples.Add(monitoringSample);
                while (_monitorSamples.Count > 300)
                    _monitorSamples.RemoveAt(0);
                if (_phase == TestPhase.Monitoring)
                    _chart.SetSamples(_monitorSamples);

                if (!snapshot.TemperatureC.HasValue)
                {
                    string reason = String.IsNullOrWhiteSpace(snapshot.TemperatureFailureReason)
                        ? "CPU温度センサーを取得できません。"
                        : snapshot.TemperatureFailureReason;
                    SetResultDisplay(VerdictLevel.Caution, "CPU温度を取得できません", reason,
                        "［温度取得を再検出・修復］を実行し、必要な場合はPCを再起動してください。");
                }

                _heatHistory.Add(monitoringSample);
                DateTime cutoff = snapshot.Timestamp.AddMinutes(-10);
                _heatHistory.RemoveAll(s => s.Timestamp < cutoff);
                HeatAlert alert = _heatDetector.Check(_heatHistory, snapshot, _settings);
                if (alert != null)
                    HandleHeatAlert(alert, snapshot);
            }

            _statusLabel.Text = String.IsNullOrWhiteSpace(snapshot.Error)
                ? "センサー更新: " + snapshot.Timestamp.ToString("HH:mm:ss")
                : "注意: " + snapshot.Error;
        }

        private void StartTestClicked(object sender, EventArgs e)
        {
            if (IsTestActive())
                return;
            if (_lastSnapshot == null || !_lastSnapshot.TemperatureC.HasValue)
            {
                MessageBox.Show("CPU温度を取得できないため、安全な負荷テストを開始できません。\r\n［温度取得を再検出・修復］を実行し、必要な場合はPCを再起動してください。", "温度センサーが必要です", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_lastSnapshot.TemperatureIsCpuDirect)
            {
                DialogResult acpi = MessageBox.Show("CPU直結温度ではなくACPI温度を使用しています。保護停止の精度が下がります。\r\nそれでも短時間テストを実行しますか？", "間接温度センサー", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (acpi != DialogResult.Yes)
                    return;
            }

            if (SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online)
            {
                DialogResult battery = MessageBox.Show("ACアダプターが接続されていません。バッテリー時は性能制限で判定が不正確になります。\r\n続行しますか？", "AC接続を推奨", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (battery != DialogResult.Yes)
                    return;
            }

            DialogResult prepared = MessageBox.Show(
                "開始前の確認\r\n\r\n・硬く平らな机に置く\r\n・吸気口と排気口を塞がない\r\n・HWiNFO等の他のセンサー監視ソフトを閉じる\r\n・異臭、煙、膨張バッテリーがあるPCでは実行しない\r\n\r\nCPUへ設定した負荷を掛けます。続行しますか？",
                "安全確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (prepared != DialogResult.Yes)
                return;

            _durations = SelectedDurations();
            _testSamples.Clear();
            _heatHistory.Clear();
            _evaluation = null;
            _airflowCheck.Checked = false;
            _noiseCheck.Checked = false;
            _testStarted = DateTime.Now;
            _phaseStarted = _testStarted;
            _phase = TestPhase.Idle;
            _reader.SetTestPhase(_phase);
            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _reportButton.Enabled = false;
            _presetCombo.Enabled = false;
            _loadCombo.Enabled = false;
            _safetyCutoff.Enabled = false;
            SetResultDisplay(VerdictLevel.Unknown, "待機温度を測定中", "まず無負荷状態を記録します。負荷工程は自動で始まります。", "テスト中は吸排気口を塞がないでください。");
            UpdateTestProgress();
        }

        private void AdvanceTestIfNeeded()
        {
            double phaseSeconds = (DateTime.Now - _phaseStarted).TotalSeconds;
            if (_phase == TestPhase.Idle && phaseSeconds >= _durations.IdleSeconds)
            {
                _phase = TestPhase.Load;
                _phaseStarted = DateTime.Now;
                _reader.SetTestPhase(_phase);
                _stress.Start(SelectedLoadPercent());
                SetResultDisplay(VerdictLevel.Unknown, "CPU負荷中", "温度上昇とファン増速を測定しています。", "設定温度へ達すると負荷を即時停止します。");
            }
            else if (_phase == TestPhase.Load && phaseSeconds >= _durations.LoadSeconds)
            {
                _stress.Stop();
                _phase = TestPhase.Cooldown;
                _phaseStarted = DateTime.Now;
                _reader.SetTestPhase(_phase);
                SetResultDisplay(VerdictLevel.Unknown, "冷却応答を測定中", "負荷を停止し、温度低下とファン減速を記録しています。", "冷却工程が終わるまでPCを同じ場所に置いてください。");
            }
            else if (_phase == TestPhase.Cooldown && phaseSeconds >= _durations.CooldownSeconds)
            {
                CompleteTest();
            }
        }

        private void CompleteTest()
        {
            _stress.Stop();
            _phase = TestPhase.Complete;
            _reader.SetTestPhase(_phase);
            RestoreTestControls();
            Reevaluate();
            _reportButton.Enabled = true;
            _heatHistory.Clear();
            _trayIcon.ShowBalloonTip(5000, "CPUファンテスト完了", _evaluation == null ? "判定が完了しました。" : _evaluation.Verdict, ToToolTipIcon(_evaluation == null ? VerdictLevel.Unknown : _evaluation.Level));
        }

        private void AbortTest(bool safety)
        {
            if (!IsTestActive())
                return;
            _stress.Stop();
            _phase = safety ? TestPhase.SafetyStop : TestPhase.Aborted;
            _reader.SetTestPhase(_phase);
            RestoreTestControls();
            Reevaluate();
            _reportButton.Enabled = _testSamples.Count > 0;
            _heatHistory.Clear();
        }

        private void SafetyStop(double temperature)
        {
            AbortTest(true);
            SystemSounds.Hand.Play();
            _trayIcon.ShowBalloonTip(10000, "高温保護停止", String.Format("CPU温度 {0:0.0}℃。負荷を停止しました。", temperature), ToolTipIcon.Error);
            MessageBox.Show(String.Format("CPU温度が {0:0.0}℃ に達したため、負荷を自動停止しました。\r\nPCが冷えるまで待ち、冷却系を点検してください。", temperature), "高温保護停止", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void Reevaluate()
        {
            if (_testSamples.Count == 0)
                return;
            _evaluation = FanTestEvaluator.Evaluate(_testSamples, (double)_safetyCutoff.Value, _airflowCheck.Checked, _noiseCheck.Checked, _phase);
            SetResultDisplay(_evaluation.Level, _evaluation.Verdict, _evaluation.Basis, _evaluation.Limitation + "  推奨: " + _evaluation.RecommendedAction);
        }

        private void ObservationChanged(object sender, EventArgs e)
        {
            if (_phase == TestPhase.Complete || _phase == TestPhase.Aborted || _phase == TestPhase.SafetyStop)
                Reevaluate();
        }

        private void SaveReportClicked(object sender, EventArgs e)
        {
            if (_testSamples.Count == 0)
                return;
            if (_evaluation == null)
                Reevaluate();
            try
            {
                SavedReport saved = ReportWriter.Save(_reader.Profile, _testSamples, _evaluation, _airflowCheck.Checked, _noiseCheck.Checked, _reader.GetRawSensorReport());
                _statusLabel.Text = "レポート保存: " + saved.HtmlPath;
                DialogResult open = MessageBox.Show("HTMLレポートとCSVログを保存しました。\r\n\r\n" + saved.HtmlPath + "\r\n\r\nレポートを開きますか？", "保存完了", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (open == DialogResult.Yes)
                    Process.Start(saved.HtmlPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("レポートを保存できませんでした。\r\n" + ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void HandleHeatAlert(HeatAlert alert, SensorSnapshot snapshot)
        {
            string path = String.Empty;
            try { path = HeatAlertLogger.Append(alert, snapshot, _reader.Profile); }
            catch (Exception ex) { _statusLabel.Text = "発熱警告ログの保存失敗: " + ex.Message; }

            SystemSounds.Exclamation.Play();
            _trayIcon.ShowBalloonTip(10000, alert.Title, alert.Message, alert.Critical ? ToolTipIcon.Error : ToolTipIcon.Warning);
            SetResultDisplay(alert.Critical ? VerdictLevel.Suspect : VerdictLevel.Caution, alert.Title, alert.Message, "検知ログ: " + (String.IsNullOrWhiteSpace(path) ? "保存できませんでした" : path));
            if (alert.Critical && _settings.ShowWindowOnCriticalAlert)
                ShowFromTray();
        }

        private void StartupCheckChanged(object sender, EventArgs e)
        {
            if (_initializingStartup || _demoMode)
                return;
            if (_startupCheck.Checked && (_lastSnapshot == null || !_lastSnapshot.TemperatureC.HasValue))
            {
                DialogResult register = MessageBox.Show(
                    "現在はCPU温度を取得できていません。\r\nドライバー修復やPC再起動後に取得できる場合があります。\r\n\r\n自動起動だけ先に登録しますか？",
                    "CPU温度は現在未検出です", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (register != DialogResult.Yes)
                {
                    _initializingStartup = true;
                    _startupCheck.Checked = false;
                    _initializingStartup = false;
                    return;
                }
            }
            _startupCheck.Enabled = false;
            string error = _startupCheck.Checked
                ? StartupTaskManager.Enable(Application.ExecutablePath)
                : StartupTaskManager.Disable();
            _startupCheck.Enabled = true;
            if (error != null)
            {
                _initializingStartup = true;
                _startupCheck.Checked = !_startupCheck.Checked;
                _initializingStartup = false;
                MessageBox.Show(error, "自動起動設定", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                _statusLabel.Text = _startupCheck.Checked
                    ? "Windowsログイン時の自動監視を有効にしました。"
                    : "Windowsログイン時の自動監視を無効にしました。";
            }
        }

        private void UpdateMetrics(SensorSnapshot snapshot)
        {
            _tempValue.Text = snapshot.TemperatureC.HasValue ? snapshot.TemperatureC.Value.ToString("0.0") + " ℃" : "—";
            _tempDetail.Text = snapshot.TemperatureC.HasValue
                ? snapshot.TemperatureSensorName
                : "未検出（修復ボタンを実行）";
            _tempValue.ForeColor = !snapshot.TemperatureC.HasValue ? TextSecondary :
                snapshot.TemperatureC.Value >= _settings.AbsoluteAlertC ? Red :
                snapshot.TemperatureC.Value >= _settings.SustainedAlertC ? Amber : Accent;

            _loadValue.Text = snapshot.CpuLoadPercent.HasValue ? snapshot.CpuLoadPercent.Value.ToString("0") + " %" : "—";
            _fanValue.Text = snapshot.FanRpm.HasValue ? snapshot.FanRpm.Value.ToString("0") + " RPM" : "非公開";
            _fanDetail.Text = snapshot.FanSensorAvailable ? snapshot.FanSensorName : "温度挙動で間接判定";
            _sourceLabel.Text = snapshot.SensorSource;
            UpdateMonitorCard();
        }

        private void UpdateMonitorCard()
        {
            if (_monitorValue == null)
                return;
            bool temperatureAvailable = _lastSnapshot != null && _lastSnapshot.TemperatureC.HasValue;
            if (_settings.HeatMonitoringEnabled && !temperatureAvailable)
            {
                _monitorValue.Text = "監視不可";
                _monitorValue.ForeColor = Amber;
                _monitorDetail.Text = "CPU温度センサー未検出";
            }
            else
            {
                _monitorValue.Text = _settings.HeatMonitoringEnabled ? "監視中" : "停止中";
                _monitorValue.ForeColor = _settings.HeatMonitoringEnabled ? Green : TextSecondary;
                _monitorDetail.Text = _settings.HeatMonitoringEnabled
                    ? String.Format("危険 {0:0}℃ / 持続 {1:0}℃", _settings.AbsoluteAlertC, _settings.SustainedAlertC)
                    : "通知・記録は行いません";
            }
        }

        private void UpdateTestProgress()
        {
            if (!IsTestActive() || _durations == null)
            {
                if (_phase == TestPhase.Complete) { _phaseLabel.Text = "状態：完了"; _countdownLabel.Text = "完了"; _progress.Value = 100; }
                else if (_phase == TestPhase.SafetyStop) { _phaseLabel.Text = "状態：高温停止"; _countdownLabel.Text = "停止"; }
                else if (_phase == TestPhase.Aborted) { _phaseLabel.Text = "状態：中断"; _countdownLabel.Text = "中断"; }
                return;
            }
            int totalElapsed = (int)Math.Max(0, (DateTime.Now - _testStarted).TotalSeconds);
            int remaining = Math.Max(0, _durations.TotalSeconds - totalElapsed);
            _progress.Value = Math.Max(0, Math.Min(100, totalElapsed * 100 / Math.Max(1, _durations.TotalSeconds)));
            _phaseLabel.Text = "状態：" + ReportWriter.PhaseName(_phase);
            _countdownLabel.Text = remaining + " 秒";
        }

        private void SetResultDisplay(VerdictLevel level, string title, string basis, string limitation)
        {
            Color color = level == VerdictLevel.Normal ? Green : level == VerdictLevel.Caution ? Amber : level == VerdictLevel.Suspect ? Red : Accent;
            _resultPanel.BackColor = Color.FromArgb(
                Math.Min(255, PanelBackground.R + color.R / 18),
                Math.Min(255, PanelBackground.G + color.G / 18),
                Math.Min(255, PanelBackground.B + color.B / 18));
            _resultTitle.ForeColor = color;
            _resultTitle.Text = title;
            _resultBasis.Text = basis;
            _resultLimit.Text = limitation;
        }

        private TestDurations SelectedDurations()
        {
            if (_presetCombo.SelectedIndex == 0) return TestDurations.Quick();
            if (_presetCombo.SelectedIndex == 2) return TestDurations.Thorough();
            return TestDurations.Standard();
        }

        private int SelectedLoadPercent()
        {
            if (_loadCombo.SelectedIndex == 0) return 70;
            if (_loadCombo.SelectedIndex == 2) return 90;
            return 80;
        }

        private bool IsTestActive()
        {
            return _phase == TestPhase.Idle || _phase == TestPhase.Load || _phase == TestPhase.Cooldown;
        }

        private TelemetrySample ToSample(SensorSnapshot snapshot, TestPhase phase, double elapsed)
        {
            return new TelemetrySample
            {
                Timestamp = snapshot.Timestamp,
                ElapsedSeconds = elapsed,
                Phase = phase,
                TemperatureC = snapshot.TemperatureC,
                CpuLoadPercent = snapshot.CpuLoadPercent,
                FanRpm = snapshot.FanRpm,
                FanControlPercent = snapshot.FanControlPercent,
                FanSensorAvailable = snapshot.FanSensorAvailable,
                TemperatureSensorName = snapshot.TemperatureSensorName,
                FanSensorName = snapshot.FanSensorName,
                SensorSource = snapshot.SensorSource
            };
        }

        private void RestoreTestControls()
        {
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _presetCombo.Enabled = true;
            _loadCombo.Enabled = true;
            _safetyCutoff.Enabled = true;
            UpdateTestProgress();
        }

        private void RepairSensorAccess(object sender, EventArgs e)
        {
            if (IsTestActive())
            {
                MessageBox.Show("自動CPUファンテストを停止してから実行してください。", "センサー修復", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _repairSensorButton.Enabled = false;
            _timer.Stop();
            try
            {
                _reader.Reload();
                SensorSnapshot snapshot = _reader.Read();
                _lastSnapshot = snapshot;
                UpdateMetrics(snapshot);
                if (snapshot.TemperatureC.HasValue)
                {
                    SetResultDisplay(VerdictLevel.Normal, "CPU温度の取得を確認", snapshot.TemperatureSensorName,
                        "センサーを再初期化しました。");
                    MessageBox.Show("CPU温度を取得できました。", "再検出完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                bool driverReady = SensorDriverManager.OfferInstallOrUpdate(false);
                _reader.Reload();
                snapshot = _reader.Read();
                _lastSnapshot = snapshot;
                UpdateMetrics(snapshot);
                if (snapshot.TemperatureC.HasValue)
                {
                    SetResultDisplay(VerdictLevel.Normal, "CPU温度の取得を確認", snapshot.TemperatureSensorName,
                        "PawnIOドライバーを確認し、センサーを再初期化しました。");
                    MessageBox.Show("CPU温度を取得できました。", "修復完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string reason = String.IsNullOrWhiteSpace(snapshot.TemperatureFailureReason)
                    ? "CPU温度センサーはまだ検出されていません。"
                    : snapshot.TemperatureFailureReason;
                SetResultDisplay(VerdictLevel.Caution, "CPU温度はまだ未検出です", reason,
                    "PCを再起動し、他の監視ソフトを終了してから再度修復を実行してください。");
                MessageBox.Show(
                    reason + (driverReady ? "\r\n\r\nドライバーを有効にするため、PCを再起動してください。" : "\r\n\r\nドライバーの導入・更新は完了していません。") +
                    "\r\nそれでも未検出なら、［検出センサー一覧］の内容をお送りください。",
                    "CPU温度の取得確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("センサーの再初期化に失敗しました。\r\n" + ex.Message, "センサー修復エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _repairSensorButton.Enabled = true;
                _timer.Start();
            }
        }

        private void ShowSensorList()
        {
            string report = _reader.GetRawSensorReport();
            Form dialog = new Form
            {
                Text = "検出センサー一覧",
                Size = new Size(850, 600),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = WindowBackground,
                ForeColor = TextPrimary,
                Font = Font
            };
            TextBox text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                WordWrap = false,
                BackColor = Color.FromArgb(9, 14, 23),
                ForeColor = TextPrimary,
                Font = new Font("Consolas", 9F),
                Text = report
            };
            Button copy = new Button { Dock = DockStyle.Bottom, Height = 38, Text = "クリップボードへコピー" };
            copy.Click += delegate { try { Clipboard.SetText(report); } catch { } };
            dialog.Controls.Add(text);
            dialog.Controls.Add(copy);
            dialog.ShowDialog(this);
        }

        private void ShowSafetyGuide()
        {
            MessageBox.Show(
                "安全な検査条件\r\n\r\n1. ACアダプターを接続し、硬く平らな机へ置く\r\n2. 吸排気口を塞がず、他の監視ソフトを終了する\r\n3. 異臭・煙・液体跡・膨張バッテリーがあれば負荷テストしない\r\n4. 高温停止後は十分に冷えるまで再実行しない\r\n5. RPM非公開なら排気と作動音を人が確認する\r\n\r\n本アプリはファン制御値、BIOS、電源設定を書き換えません。自動起動時は監視のみで、CPU負荷を掛けません。",
                "安全な使い方", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SaveSettingsSafely()
        {
            try { _settings.Save(); }
            catch (Exception ex) { _statusLabel.Text = "設定保存エラー: " + ex.Message; }
        }

        private void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnResize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
                MinimizeToTray(!_startupMonitorMode);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                MinimizeToTray(true);
                return;
            }
            _timer.Stop();
            _stress.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _applicationIcon.Dispose();
        }

        private void MinimizeToTray(bool showHint)
        {
            Hide();
            ShowInTaskbar = false;
            if (showHint && !_shownTrayHint)
            {
                _trayIcon.ShowBalloonTip(3500, "タスクトレイで常駐中", "画面を閉じても監視を継続します。終了するには、トレイメニューの「終了」を選んでください。", ToolTipIcon.Info);
                _shownTrayHint = true;
            }
        }

        private void ExitApplication()
        {
            _allowExit = true;
            Close();
        }

        private static ToolTipIcon ToToolTipIcon(VerdictLevel level)
        {
            if (level == VerdictLevel.Suspect) return ToolTipIcon.Error;
            if (level == VerdictLevel.Caution) return ToolTipIcon.Warning;
            return ToolTipIcon.Info;
        }

        private static Icon LoadApplicationIcon()
        {
            try
            {
                using (Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    if (extracted != null)
                        return (Icon)extracted.Clone();
                }
            }
            catch
            {
                // 埋め込みアイコンを取得できない環境ではWindows標準アイコンへ退避します。
            }
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
