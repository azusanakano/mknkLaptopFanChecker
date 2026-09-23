# Windowsネイティブ構成について

## 構成

| 部分 | 方式 | 目的 |
|---|---|---|
| `mknkLaptopFanChecker.exe` | C++ / Win32 / x64ネイティブ | ダブルクリック起動、Unicodeパス処理、引数引き継ぎ、エラー表示 |
| `runtime\mknkLaptopFanChecker.exe` | Windows Forms / .NET Framework | 検査画面、テスト制御、常駐監視、通知、レポート |
| `runtime\LibreHardwareMonitorLib.dll` | LibreHardwareMonitor | CPU温度、負荷、ファンRPM等の読み取り |
| `runtime\PawnIO_setup.exe` | 署名付きセンサードライバー導入部 | 対応機種で低レベルセンサーへ安全にアクセス |

## この構成を選んだ理由

ファンRPMやCPU内部温度は、Windows標準APIだけでは取得できないノートPCが多くあります。GUIを完全なアンマネージドWin32へ置き換えると、既存のLibreHardwareMonitorセンサー対応を失い、検査精度が下がります。

そこで v1.2.1 は、利用者が直接起動する入口を64bit Win32ネイティブEXEにしつつ、実績のあるセンサーエンジンと検査GUIを保持しています。通常利用時にCMD、PowerShell、ブラウザー、Pythonは起動しません。

最上位EXEと検査GUIには、提供写真を基にした「ゆりちゃん」とCPUファンの専用マルチサイズアイコンを埋め込み、ウィンドウ、タスクバー、通知領域にも同じアイコンを表示します。PawnIOの既定セキュリティに合わせ、検査GUIは管理者権限でセンサーへ接続します。自動起動は最高権限のタスクと非昇格ネイティブ監視起動を組み合わせます。

ネイティブ起動部のC++標準ライブラリと例外処理ランタイムはEXEへ静的リンクしています。配布先に `libc++.dll` または `libunwind.dll` を追加する必要はありません。

## 制約

- 検査GUIには.NET Framework 4.7.2以上が必要です。
- 配布物はコード署名されていません。初回起動時にWindows SmartScreenが警告する場合があります。
- EC/BIOSがRPMを公開しない機種では、完全な直接判定はできません。
- 「正常傾向」は冷却系の寿命や断続故障まで保証するものではありません。
