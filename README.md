# BlueShift

Windows 向けの時間帯スケジュール型ブルーライトカットアプリです。WinUI 3 で作成されています。

## 機能

- 時間帯ごとのブルーライト強度スケジュール
- タスクトレイ常駐（バックグラウンド動作）
- ログオン時の自動起動
- 設定画面・アップデート確認（GitHub Releases）
- 日本語 / 英語 UI

## ダウンロード

[Releases](https://github.com/kazu-1234/BlueShift/releases) から `BlueShift-v1.0.22-win-x64.zip` をダウンロードし、任意のフォルダに展開して `BlueShift.exe` を実行してください。  
.NET の別途インストールは不要です（自己完結型ビルド）。

## ビルド

```powershell
dotnet publish App1.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true
```

## 免責事項

本ソフトウェアの使用により生じたいかなる損害についても、開発者は一切の責任を負いません。自己責任でご使用ください。特に重要な作業を行う PC での使用には十分ご注意ください。

## ライセンス

MIT License（[LICENSE](LICENSE) を参照）
