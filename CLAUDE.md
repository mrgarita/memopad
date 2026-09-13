# CLAUDE.md

このファイルは、本リポジトリで作業する Claude Code（claude.ai/code）への
指針を提供します。

## リポジトリの状態

step3（フィードバック対応）を進行中。10 回目までのフィードバックを v0.2.0〜v0.10.0 で対応済み
（一覧は `docs/site/step3/index.html` 1 節）。**FB-19「大きなファイルを開くと固まり、その後の操作でも
固まる」は v0.10.0 で本文のエディタを Scintilla に替えて解消した**（最長の無応答 43,456 ms → 34 ms）。
**2026-09-13 にユーザーの確認（OK）を得て配布を再開した**。GitHub Releases に置くのは **v0.10.0 のみ**で、
不具合のある v0.6.0〜v0.9.1 のインストーラは削除したまま（ローカルの `installer/output/` には全バージョンがある）。
**FB-21「大きなファイルを開いた直後だけウィンドウ移動がカクつく」は v0.10.1 で対応済み**
（原因は Scintilla が折り返しの計算を 10 ms ずつ空き時間に進めること。対策 4 案を実測して
「移動・リサイズ中だけ計算を止める」を採用。`docs/site/step3/index.html` 15.9・16 節）。「正」となるのは
`project.txt`（目的・仕様・進行 step）と、`docs/site/step1/index.html` 10 節の
仕分け表（再現する機能の確定リスト）。実装は `src/memopad/`（C#。メイン ウィンドウは Windows Forms、ダイアログは WPF）、
インストーラは `installer/`、備忘録サイトは `docs/site/` にある。
GitHub に公開済み（https://github.com/mrgarita/memopad）。GitHub Pages は main の `/docs` を公開し、
`docs/index.html` が紹介ページ、`docs/site/` が備忘録（https://mrgarita.github.io/memopad/）。
インストーラは GitHub Releases に添付する。ライセンスは MIT（`LICENSE`）。

## プロダクト概要

Windows 標準添付のエディタ「メモ帳」（notepad.exe）の機能はそのままに、
**背景色・文字色・フォントを自由に変えられる** ようにしたテキストエディタ
`MemoPad.exe` を制作する（v0.8.0 で名称を memopad から **MemoPad** に改めた。リポジトリ名・
フォルダー名・名前空間 `Memopad` はそのまま）。ユーザーはシンプルなエディタとしてメモ帳を使う機会が
多いが、背景色と文字色を自由に設定できない点に不満がある（フォントはメモ帳でも
変更可能）ため、その不満を解消することが目的。完成品はインストーラ形式で配布する。

### 進行 step

| step | 内容 | 成果物 |
|---|---|---|
| step1 | メモ帳（notepad.exe）の機能をあぶり出す | 機能一覧の備忘録 HTML |
| step2 | step1 に基づき `MemoPad.exe` を作成する | 実行ファイル＋インストーラ、備忘録 HTML |
| step3 | 完成品のフィードバックを行う。フィードバックが 0 になった時点でプロジェクト完了（その時点のバージョンを **v1.0** とする） | 修正版、備忘録 HTML |

### 厳守すべき制約

- **メモ帳の既存機能を損なわない。** step1 で洗い出した機能はすべて `MemoPad.exe`
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
2. step2：`MemoPad.exe` がメモ帳の既存機能を再現し、背景色・文字色・フォントの
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

- **アプリ本体**：C#（.NET 9、`net9.0-windows`）。**メイン ウィンドウは Windows Forms**
  （`Views/MainForm.cs` と `Views/TitleBar.cs` など。入口は `Program.cs`）、**ダイアログは WPF**
  （`Dialogs/`。`Services/WpfHost.cs` が最初に開くときだけ WPF を初期化する）。v0.7.0 までは
  全体が WPF だったが、起動時間がメモ帳に届かないため v0.8.0 で作り直した（`docs/site/step3/index.html`
  8〜9 節）。Visual Studio 2022 が無い端末でも `dotnet` CLI だけでビルドできる構成
  （`memopad.sln` があるので Visual Studio でも開ける）。選定理由は `docs/site/step2/index.html` 1 節
- **メイン ウィンドウの自前描画**：標準のタイトル バーは `WM_NCCALCSIZE` で外し、`WM_NCHITTEST` で
  リサイズとドラッグ移動を自分で答える。**タイトル行やウィンドウの縁を覆う子コントロールは、
  `Views/ChromeHitTest.cs` の `TryPassToFrame` を `WndProc` で呼んで当たり判定を親へ譲る**
  （子が HTCLIENT と答えると親の答えが使われず、移動もリサイズもできなくなる。v0.8.0 の不具合。
  新しく縁に届くコントロールを足すときは同じ 3 行を入れる）。タイトル行のタブとボタンは `Button` を
  継承した実体のあるコントロールにする（素の `Control` に自前描画すると UI Automation から見えず、
  支援技術と `docs\tools\test-tabclick.ps1` の確認が効かなくなる）。配色は `Services/ThemeService.cs` の
  `ThemePalette`、メニューの見た目は `Views/FluentMenuRenderer.cs`（項目の字下げはチェック欄
  `ShowImageMargin` の有無で決まる。`ToolStripMenuItem.Padding` の左右は効かない）
- **本文のエディタ**：**Scintilla**（NuGet の `Scintilla5.NET`。`Views/PlainTextEdit.cs` が
  `ScintillaNET.Scintilla` を継承）。v0.2.0〜v0.9.2 は Win32 の RichEdit だったが、**日本語と英字が
  混ざった行のレイアウトが極端に遅く**、10 MB・12 万行のファイルで 43 秒固まり、その後もリサイズのたびに
  28 秒固まるため v0.10.0 で置き換えた（FB-19）。原因は GDI への文字幅の問い合わせで、RichEdit でも
  Win32 の Edit でも Windows App SDK の WinUI でも避けられない。Scintilla は同じ計算を空き時間に
  細切れで行うので画面が止まらない（最長の無応答 34 ms。詳細は `docs/site/step3/index.html` 15 節）。
  - **改行はエディタ内部で LF に統一する**（`EolMode = Eol.Lf`、読み込み時に `EditorView` が変換。
    保存時に `TextFileService` がファイルの改行コードへ戻す）
  - **「編集済み」は Scintilla の保存ポイント**（`SetSavePoint` と `SavePointLeft`／`SavePointReached`）で
    判定する。`TextChanged` で判定すると、コントロールの初期化でも変更とみなされ新規タブが編集済みになる
  - **ファイルのドロップは `Views/FileDropTarget.cs` を `RegisterDragDrop` で登録して自前で受ける**
    （エディタに任せるとファイル名が本文に貼り付けられる。v0.9.1 の FB-16 と同じ理由）
  - ホイールで最終行まで届くこと（FB-17）と折り返しオフの横スクロール（FB-18）は Scintilla 標準で満たす。
    自前の `SmoothWheelScroller.cs` は不要になったので削除した
  - ズームはフォント サイズを倍率で計算する（Scintilla の `Zoom` は整数ポイントの増減なので % と合わない）
  - **折り返しオンのとき、Scintilla は全行の折り返し位置を「10 ms ずつ空き時間に」計算する**（Windows 版は
    10 ms 間隔のタイマー。番号 2）。12 万行で約 4 秒かかり、その間のウィンドウ移動・リサイズは 1 コマごとに
    最大 10 ms 待たされてカクついた（FB-21）。**v0.10.1 で `WM_ENTERSIZEMOVE`〜`WM_EXITSIZEMOVE` の間だけ
    そのタイマーを通さないようにした**（`PlainTextEdit.PauseIdleWork` と `MainForm.WndProc`）。
    見えている範囲の折り返しは描画時に計算されるので表示は正しく、放すと計算は再開して最後まで終わる。
    DirectWrite ＋ `SCI_SETLAYOUTTHREADS` も試したが、文字のにじみ方が変わり読み込みが 0.2 秒増えるので不採用（16 節）
  - **既知の問題：折り返しの計算中（開いた直後の約 4 秒）に文字を打つと 1 文字 117〜133 ms かかる**
    （計算が終われば 15.7 ms）。レイアウト結果を待つためで、別課題として扱う
  - **既知の問題：本文が UI Automation に出ない**（`ControlType.Pane`・パターンなし）。RichEdit は
    `ControlType.Document` ＋ TextPattern/ValuePattern で読めていたので支援技術に対する回帰。
    Scintilla の `SCI_SETACCESSIBILITY` は GTK 版だけの機能で Windows では効かない。別課題として対応予定
- **ビルドと確認**：`dotnet build src\memopad\memopad.csproj`。前回のビルド サーバーが `obj` の
  生成ファイルを掴んで 1 回おきに失敗することがあるので、失敗したら `dotnet build-server shutdown`
  →`src\memopad\obj` を削除→`-nodeReuse:false -p:UseSharedCompilation=false` で再ビルドする。動作確認とスクリーンショットは `docs\tools\capture-memopad.ps1`
  （`-ExePath` と `-OutDir` を絶対パスで指定。実行中はマウス／キーボードに触らない）。
  入力→表示の遅延と起動の段階別時間は環境変数 `MEMOPAD_PERF=1` の診断ログ（`Services/PerfLog.cs`）で
  調べられる。起動時間の比較は `docs\tools\measure-startup.ps1`（`-Target notepad` または exe のパス）、
  入力→表示の遅延は `docs\tools\measure-input.ps1`（`-FilePath` で大きなファイルを開いた直後も測れる）
- **起動時間**：v0.8.0 でウィンドウ表示まで 221 ms、描画完了まで 332 ms（同じ端末のメモ帳は 211 ms /
  467 ms）。WPF だった v0.7.0 は 505 ms で、空の WPF ウィンドウでも約 400 ms かかるのが下限だった
  （`docs/site/step3/index.html` 8〜9 節）。効いている対策は Windows Forms 化のほか、
  設定 JSON のソース ジェネレーター変換、`Services/StartupWarmup.cs` の別スレッド先読み、
  アプリ本体の `PublishReadyToRun`。複合 ReadyToRun（`PublishReadyToRunComposite`）は初回起動が
  3 秒以上悪化するので使わない
- **インストーラ**：Inno Setup 6（winget で導入済み）。`installer\build-installer.ps1`
  で self-contained 発行→ `installer\output\MemoPad-setup-<version>.exe`
- **色の機能**：「色変更」ダイアログ（`Dialogs/ColorChangeDialog`、PICO-8 の 16 色＋カラーピッカー。
  v0.4.0）と「配色パターン」ダイアログ（`Dialogs/ColorSchemeDialog`、`Services/ColorSchemes.cs` の
  16 パターン。v0.5.0）。どちらも結果は設定の背景色・文字色（"#RRGGBB"）に入る
- **アプリ アイコン**：ユーザー提供の `memopad.ico`（v0.8.0 でクリーム色の文書に緑の罫線へ差し替え。
  生成スクリプトは廃止）。差し替えるときは `src/memopad/memopad.ico` と `docs/assets/`
  （紹介ページ用 PNG・favicon）を同時に更新する
- **設定の保存先**：`%APPDATA%\MemoPad\settings.json`
- **備忘録サイト**：`docs/site/` 配下の静的 HTML。`index.html` を起点に各 step の
  ページへリンクする。スクリーンショットは `docs/site/img/` に置く
- **MCP サーバー**：Visual Studio 等、本プロジェクトで使えそうな MCP サーバーが
  あれば機能追加を検討する（導入前にユーザーへ提案する）
- **バージョン**：step3 でフィードバックが 0 件になった時点を v1.0 とする
- **コスト**：既存資源（GitHub 無料アカウント等）と無料ツールで完結させ、
  新規の有料サービスは使わない
