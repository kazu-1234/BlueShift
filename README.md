# BlueShift

Windows 向けの時間帯スケジュール型ブルーライトカットアプリです。WinUI 3 で作成されています。

## 機能

- 時間帯ごとのブルーライト強度スケジュール
- タスクトレイ常駐（バックグラウンド動作）
- ログオン時の自動起動
- 設定画面・アップデート確認（GitHub Releases）
- 日本語 / 英語 UI

## ダウンロード

[Releases](https://github.com/kazu-1234/BlueShift/releases) から以下のいずれかを取得してください（.NET の別途インストールは不要です）。

| ファイル | 説明 |
|---------|------|
| `BlueShift-v1.0.34-win-x64.exe` | **単体 exe**（そのまま実行可能） |

## ビルド

```powershell
# 単体 exe（推奨・配布用）
dotnet publish App1.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true `
  -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# フォルダ版
dotnet publish App1.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true
```

## 免責事項

本ソフトウェアの使用により生じたいかなる損害についても、開発者は一切の責任を負いません。自己責任でご使用ください。特に重要な作業を行う PC での使用には十分ご注意ください。

## ライセンス

MIT License（[LICENSE](LICENSE) を参照）
