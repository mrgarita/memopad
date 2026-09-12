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
; インストーラ自体のアイコンもアプリと同じにする
SetupIconFile=..\src\memopad\memopad.ico
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
Name: "txtassoc"; Description: "「プログラムから開く」の候補に MemoPad を追加する(&O)"; GroupDescription: "関連付け:"

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

[Registry]
; 「プログラムから開く」に出す（既定のアプリは変えない）
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey; Tasks: txtassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: txtassoc
Root: HKA; Subkey: "Software\Classes\.txt\OpenWithList\{#MyAppExeName}"; ValueType: none; Flags: uninsdeletekey; Tasks: txtassoc

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} を起動する"; Flags: nowait postinstall skipifsilent
