# コントリビューション

IssueやPull Requestでは、Windows 10/11の版、PC機種、CPU、再現手順、期待結果、実際の結果を記載してください。シリアル番号、ユーザー名、保存先などの個人情報は削除してください。

変更時は次を確認してください。

1. `Build-Windows.ps1` で検査GUIをビルドできること
2. `native-launcher-source/Build-NativeLauncher.ps1` でx64ランチャーをビルドできること
3. `dotnet run --project tests/EvaluatorTests.csproj -c Release` が成功すること
4. ランチャーが `libc++.dll` と `libunwind.dll` に依存しないこと
5. 92℃の保護停止、監視専用起動、ゆりちゃんアイコンを損なわないこと
