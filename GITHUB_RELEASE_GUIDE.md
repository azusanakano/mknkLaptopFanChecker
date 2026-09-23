# GitHub登録・公開手順

このフォルダーは、ネコシステム社 ノートPC CPUファンチェッカー v1.2.1 のGitHub登録用パッケージです。

## 新しいリポジトリへ登録

1. GitHubで空のリポジトリを作成します。READMEやライセンスは既に同梱しているため、作成画面では追加しません。
2. このZIPを展開し、展開先フォルダーの**中身**をリポジトリ直下へ配置します。
3. 次のコマンドを実行します。

```powershell
git init
git branch -M main
git add .
git commit -m "Initial release: NekoSystem Laptop Fan Checker v1.2.1"
git remote add origin https://github.com/OWNER/REPOSITORY.git
git push -u origin main
```

4. GitHubの「Actions」タブで `Windows build` が成功することを確認します。

## リリース作成

1. `v1.2.1` タグを作成してpushします。
2. Actionsの成果物 `NekoSystem-LaptopFanChecker-*` を取得します。
3. `NekoSystem-LaptopFanChecker-1.2.1-win64.zip` とSHA-256をGitHub Releaseへ添付します。

```powershell
git tag v1.2.1
git push origin v1.2.1
```

## ビルド構成

- `Build-Windows.ps1`: Windows Forms検査GUIをビルド
- `native-launcher-source/Build-NativeLauncher.ps1`: 64bit Win32起動EXEを `/MT` で静的リンク
- `tests/EvaluatorTests.csproj`: 判定・発熱監視・自動起動・レポートのテスト
- `Package-Release.ps1`: 利用者向けZIPとSHA-256一覧を生成
- `.github/workflows/build.yml`: 上記をWindows Server 2022で自動実行

公開前に、リポジトリの説明、連絡先、GitHubのSecurity policyを運用方針に合わせて更新してください。
