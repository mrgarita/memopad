; MemoPad のインストーラ定義（Inno Setup 6）
; ビルド手順は installer\build-installer.ps1 を参照。
; 事前に dotnet publish で self-contained の出力を installer\publish\ に作っておく。

#define MyAppName "MemoPad"
#ifndef MyAppVersion
  #define MyAppVersion "0.8.0"
#endif
#define MyAppPublisher "mrgarita"
#define MyAppExeName "MemoPad.exe"

[Setup]
AppId={{6C1C7C0E-6D7B-4E0F-9C2B-4B4B1E2F5A10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; インストーラ自体のアイコンは Inno Setup 標準（箱に下向きの矢印）のままにする。
; v0.6.0〜v0.10.2 はアプリと同じアイコンにしていたが、インストーラだと見分けにくいので戻した（FB-27）
OutputDir=output
OutputBaseFilename=MemoPad-setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 管理者権限が無くてもユーザー単位でインストールできるようにする
PrivilegesRequiredOverridesAllowed=dialog
PrivilegesRequired=lowest

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する(&D)"; GroupDescription: "追加のショートカット:"; Flags: unchecked
; 名前の txtassoc は .txt だけだった頃のもの。変えると更新時に前回の選択を引き継がないのでそのままにする
Name: "txtassoc"; Description: "「プログラムから開く」の候補に MemoPad を追加する(&O)"; GroupDescription: "関連付け（.txt・.log・.md・ソース コードなど。既定のアプリは変えません）:"

[Files]
; Scintilla のパッケージは arm64／x86 版のネイティブ DLL も持ち込むが、
; 配布するのは x64 だけなので除く（実際に使うのは exe と同じ場所に置かれた x64 版）
Source: "publish\*"; DestDir: "{app}"; Excludes: "runtimes\win-arm64\*,runtimes\win-x86\*"; Flags: ignoreversion recursesubdirs createallsubdirs
; 同梱している Scintilla のライセンス表記（v0.10.0 で本文のエディタに採用）
Source: "..\LICENSE-Scintilla.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} をアンインストール"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; 「プログラムから開く」の候補に入れる拡張子（FB-29。v0.10.3 までは .txt だけ）。
; 既定のアプリは変えない。"." は拡張子のないファイル（Makefile、Dockerfile、LICENSE など）
#dim OpenWithExts[83] { \
  ".txt", ".log", ".md", ".markdown", ".rst", ".tex", ".diff", ".patch", \
  ".ini", ".cfg", ".conf", ".config", ".inf", ".properties", ".toml", ".yaml", ".yml", ".env", ".reg", \
  ".editorconfig", ".gitignore", ".gitattributes", \
  ".csv", ".tsv", ".json", ".jsonc", ".xml", ".xsl", ".xslt", ".sql", ".svg", \
  ".html", ".htm", ".xhtml", ".css", ".scss", ".less", ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx", ".vue", ".php", \
  ".c", ".h", ".cpp", ".hpp", ".cc", ".cxx", ".cs", ".java", ".kt", ".go", ".rs", ".swift", ".vb", ".bas", \
  ".py", ".rb", ".pl", ".lua", ".sh", ".bash", ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".vbs", \
  ".sln", ".csproj", ".vbproj", ".vcxproj", ".props", ".targets", ".xaml", ".manifest", ".iss", ".cmake", ".gradle", \
  "." }
#define I
; 拡張子ごとに .ext\OpenWithList\MemoPad.exe を作る。アンインストールは逆順に進むので、
; MemoPad.exe のキーを消したあと、空になった OpenWithList と .ext も消える（もともと中身があれば残る）
#sub EmitOpenWith
Root: HKA; Subkey: "Software\Classes\{#OpenWithExts[I]}"; Flags: uninsdeletekeyifempty; Tasks: txtassoc
Root: HKA; Subkey: "Software\Classes\{#OpenWithExts[I]}\OpenWithList"; Flags: uninsdeletekeyifempty; Tasks: txtassoc
Root: HKA; Subkey: "Software\Classes\{#OpenWithExts[I]}\OpenWithList\{#MyAppExeName}"; ValueType: none; Flags: uninsdeletekey; Tasks: txtassoc
#endsub

[Registry]
; MemoPad をアプリとして登録する（「プログラムから開く」で選んだときの表示名と、開くときのコマンド）
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey; Tasks: txtassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: txtassoc
#for {I = 0; I < DimOf(OpenWithExts); I++} EmitOpenWith

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} を起動する"; Flags: nowait postinstall skipifsilent
