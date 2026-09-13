# MemoPad

<img src="docs/assets/app-icon-256.png" alt="MemoPad のアイコン" width="96" align="right">

Windows 標準の「メモ帳」の使い勝手はそのままに、**背景色・文字色・フォントを自由に変えられる**テキスト エディタです。

- 紹介ページ：https://mrgarita.github.io/memopad/
- 制作の備忘録（機能の洗い出し・実装・フィードバック対応の記録）：https://mrgarita.github.io/memopad/site/
- ダウンロード：[Releases](https://github.com/mrgarita/memopad/releases/latest) の `MemoPad-setup-<バージョン>.exe`

> **【お知らせ】配布を再開しました（2026-09-13）**
>
> 大きなテキスト ファイル（10 MB／12 万行程度の日本語ファイル）を開くと固まる不具合のため配布を止めていましたが、
> **v0.10.0 で本文のエディタを Scintilla に替えて解消しました**（操作を受け付けない時間の最長が 43.5 秒 → 0.03 秒）。
> 最新は **v0.10.1**（大きなファイルを開いた直後のウィンドウ移動のカクつきを修正）です。
> 不具合のある v0.6.0〜v0.9.1 のインストーラは取り下げたままです。
> 経緯は[備忘録の該当箇所](https://mrgarita.github.io/memopad/site/step3/index.html#fb19)に記録しています。

## 特長

| 機能 | 内容 |
|---|---|
| 色変更 | 背景色と文字色を 1 つのダイアログで選ぶ。PICO-8 の 16 色＋カラーピッカー、プレビュー付き |
| 配色パターン | 目に優しい 16 パターン（ライト 8・ダーク 8）をカードから選ぶと、背景色と文字色が一度に変わる |
| メモ帳の機能 | タブ、検索・置換、行へ移動、ズーム、右端で折り返す、日付と時刻、ページ設定と印刷、エンコード／改行コードの切り替え、未保存の確認 |
| 入力レスポンス | 入力から表示まで画面の 1 フレーム以内（約 17 ms）。メモ帳と同等で、IME も快適 |
| 大きなファイル | 10 MB・12 万行の日本語ファイルでも画面が止まらない（v0.10.0 で本文を Scintilla に変更） |
| 起動の速さ | ウィンドウが出るまで約 0.26 秒。同じ端末のメモ帳（約 0.20 秒）と同程度 |
| テーマ | ライト／ダーク／システム設定に追従 |

## SmartScreen の警告について

コード署名証明書（有償）を付けていないため、初回実行時に Windows SmartScreen が
「WindowsによってPCが保護されました」と表示することがあります。ソフト自体に問題はありません。

- その画面内の **「詳細情報」** → 発行元「不明な発行元」の下に出る **「実行」** で続行
- または、ダウンロードした `.exe` を右クリック → 「プロパティ」→ 「全般」タブ下部の
  「セキュリティ：このファイルは他のコンピューターから取得したものです…」の **「許可する」** に
  チェック → OK。以後は警告なしで実行できます

## 動作環境

Windows 10 / 11（64 ビット）。インストーラに .NET ランタイムを同梱しているので、追加のインストールは不要です。設定は `%APPDATA%\MemoPad\settings.json` に保存されます。

## ビルド

.NET 9 SDK があれば `dotnet` CLI だけでビルドできます（Visual Studio 2022 でも `memopad.sln` を開けます）。

```powershell
dotnet build src\memopad\memopad.csproj
```

インストーラは Inno Setup 6 を使います（`winget install JRSoftware.InnoSetup`）。

```powershell
.\installer\build-installer.ps1
# → installer\output\MemoPad-setup-<version>.exe
```

## リポジトリ構成

| パス | 内容 |
|---|---|
| `src/memopad/` | アプリ本体（C#／.NET 9。メイン ウィンドウは Windows Forms、ダイアログは WPF、本文は Scintilla） |
| `installer/` | Inno Setup のスクリプトとビルド スクリプト |
| `docs/index.html` | 紹介ページ（GitHub Pages） |
| `docs/site/` | 制作の備忘録（step1〜step3） |
| `docs/tools/` | スクリーンショット撮影・動作確認用の PowerShell スクリプト |
| `project.txt` | プロジェクトの目的・仕様・進行 step |

## 制作の流れ

1. **step1**：メモ帳（Windows 11 Store 版）の機能を UI Automation で洗い出し、MemoPad で再現する範囲を決める
2. **step2**：C#／WPF で実装し、インストーラを作る（v0.8.0 でメイン ウィンドウを Windows Forms に作り直し）
3. **step3**：実際に使ったフィードバックを 1 件ずつ反映する。フィードバックが 0 件になった時点を v1.0 とする

詳しくは[備忘録](https://mrgarita.github.io/memopad/site/)を参照してください。

## ライセンス

[MIT License](LICENSE)

## 謝辞

- 本文のエディタに [Scintilla](https://www.scintilla.org/)（Neil Hodgson ほか）を使わせてもらっています。
  .NET 向けのラッパーは [Scintilla5.NET](https://github.com/desjarlais/Scintilla.NET)（MIT License）。
  Scintilla のライセンスは同梱の `LICENSE-Scintilla.txt` を参照してください
- 16 色パレットは [PICO-8](https://www.lexaloffle.com/pico-8.php)（Lexaloffle Games）の配色を使わせてもらっています
- Windows およびメモ帳は Microsoft の製品です
