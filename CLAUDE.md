# CLAUDE.md

このファイルは、本リポジトリで作業する Claude Code（claude.ai/code）への
指針を提供します。

## リポジトリの状態

step3（フィードバック対応）を進行中。初回フィードバック 4 件を v0.2.0〜v0.5.0 に分けて対応する
（一覧は `docs/site/step3/index.html` 1 節）。「正」となるのは
`project.txt`（目的・仕様・進行 step）と、`docs/site/step1/index.html` 10 節の
仕分け表（再現する機能の確定リスト）。実装は `src/memopad/`（C#／WPF）、
インストーラは `installer/`、備忘録サイトは `docs/site/` にある。
Git リポジトリはローカルのみ（GitHub 未公開）。

## プロダクト概要

Windows 標準添付のエディタ「メモ帳」（notepad.exe）の機能はそのままに、
**背景色・文字色・フォントを自由に変えられる** ようにしたテキストエディタ
`memopad.exe` を制作する。ユーザーはシンプルなエディタとしてメモ帳を使う機会が
多いが、背景色と文字色を自由に設定できない点に不満がある（フォントはメモ帳でも
変更可能）ため、その不満を解消することが目的。完成品はインストーラ形式で配布する。

### 進行 step

| step | 内容 | 成果物 |
|---|---|---|
| step1 | メモ帳（notepad.exe）の機能をあぶり出す | 機能一覧の備忘録 HTML |
| step2 | step1 に基づき `memopad.exe` を作成する | 実行ファイル＋インストーラ、備忘録 HTML |
| step3 | 完成品のフィードバックを行う。フィードバックが 0 になった時点でプロジェクト完了（その時点のバージョンを **v1.0** とする） | 修正版、備忘録 HTML |

### 厳守すべき制約

- **メモ帳の既存機能を損なわない。** step1 で洗い出した機能はすべて `memopad.exe`
  でも同等に動くこと
- 背景色・文字色は **標準色 16 色＋カラーピッカー** で簡単に変更できること
- **インストール形式で配布できる** こと（Inno Setup を想定）
- **タブ（複数ファイル）を備える。** 1 ウィンドウ 1 ファイルにはしない（2026-09-06 確定）
- **未保存のまま閉じたら確認ダイアログを出す。** タブを閉じる・ウィンドウを閉じる・終了の
  3 か所で「保存／保存しない／キャンセル」を確認する（2026-09-06 確定）
- **セッション復元は作らない。** 起動時は常に新規の空タブから始める（2026-09-06 確定）
- **マークダウン モード、スペル チェック／自動修正、Copilot などの AI 機能は作らない**
  （2026-09-06 確定。仕分けの全体は `docs/site/step1/index.html` の 10 節）
- 各 step ごとに **備忘録として HTML 化した文書を `docs/site/` に残す**。
  トップページ `docs/site/index.html` から各 step の文書を参照でき、そのまま
  公開できる状態に保つ
- 備忘録 HTML は文字だけにせず、スクリーンショットや概念図を併載する
- 著作権に配慮し、Windows／メモ帳の画像素材やアイコンは転載しない

### 完了条件

1. step1：メモ帳の機能一覧が `docs/site/` に備忘録として掲載されている
2. step2：`memopad.exe` がメモ帳の既存機能を再現し、背景色・文字色・フォントの
   変更が動作する。インストーラでインストール／アンインストールできる
3. step3：ユーザーによる完成品のフィードバックがすべて対応済みで、新規の
   フィードバックが 0 件になる。その時点のバージョンを v1.0 とする
4. 各 step の備忘録 HTML が `docs/site/index.html` から参照でき、公開可能な状態にある

## 使用言語

プロジェクト配下での生成テキストはすべて日本語で書く。対象は以下を含む。

- ユーザーへのチャット返信、進捗報告、ターン末の要約
- `AskUserQuestion` の質問文・選択肢のラベルと説明
- コード内のコメント（`//`, `#`, `/* */`, docstring 等）
- コミットメッセージ、PR タイトル、PR 本文
- README、CLAUDE.md、設計メモ、その他のドキュメント類
- エラーメッセージやログ出力のうち、エンドユーザー向けに表示される文字列

例外として **英語のまま** とするのは次のみ（言語仕様・エコシステム慣習のため）。

- プログラミング言語のキーワード、標準ライブラリ／フレームワーク／パッケージの API 名
- 識別子（変数名・関数名・クラス名・ファイル名・ディレクトリ名など）
- 設定ファイルのキー名、環境変数名

## 確定済みの実装方針

以下は `project.txt` で「現時点で検討している環境のため変更可」とされている。
変更する場合は本ファイルと `project.txt` の両方を更新する。

- **アプリ本体**：C#／WPF（.NET 9、`net9.0-windows`）。テーマは .NET 9 の Fluent
  テーマ（`Application.ThemeMode`）。Visual Studio 2022 が無い端末でも `dotnet` CLI
  だけでビルドできる構成（`memopad.sln` があるので Visual Studio でも開ける）。
  選定理由は `docs/site/step2/index.html` 1 節
- **本文のエディタ**：Win32 の RichEdit（Windows Forms の `RichTextBox` を継承した
  `Views/PlainTextEdit.cs`）を `WindowsFormsHost` で埋め込む。WPF の TextBox は入力から表示まで
  約 50 ms かかりメモ帳（約 19 ms）より遅かったため v0.2.0 で置き換えた（経緯は
  `docs/site/step3/index.html` 2 節）。RichEdit の中では WPF の InputBinding が効かないので、
  ショートカットは `MainWindow.HandleEditorCommandKey` で `Commands.All` と突き合わせる
- **ビルドと確認**：`dotnet build src\memopad\memopad.csproj`。WPF のマークアップ コンパイルが
  `obj` の生成ファイルを見失って 1 回おきに失敗することがあるので、失敗したら `src\memopad\obj`
  を消して再ビルドする。動作確認とスクリーンショットは `docs\tools\capture-memopad.ps1`
  （`-ExePath` と `-OutDir` を絶対パスで指定。実行中はマウス／キーボードに触らない）。
  入力→表示の遅延は環境変数 `MEMOPAD_PERF=1` の診断ログ（`Services/PerfLog.cs`）で調べられる
- **インストーラ**：Inno Setup 6（winget で導入済み）。`installer\build-installer.ps1`
  で self-contained 発行→ `installer\output\memopad-setup-<version>.exe`
- **設定の保存先**：`%APPDATA%\memopad\settings.json`
- **備忘録サイト**：`docs/site/` 配下の静的 HTML。`index.html` を起点に各 step の
  ページへリンクする。スクリーンショットは `docs/site/img/` に置く
- **MCP サーバー**：Visual Studio 等、本プロジェクトで使えそうな MCP サーバーが
  あれば機能追加を検討する（導入前にユーザーへ提案する）
- **バージョン**：step3 でフィードバックが 0 件になった時点を v1.0 とする
- **コスト**：既存資源（GitHub 無料アカウント等）と無料ツールで完結させ、
  新規の有料サービスは使わない
