<#
.SYNOPSIS
  MemoPad を発行（self-contained, win-x64）して Inno Setup でインストーラを作る。

.DESCRIPTION
  1. dotnet publish で installer\publish\ に実行ファイル一式を出力する（.NET ランタイム同梱）
  2. Inno Setup のコンパイラ（ISCC.exe）で installer\memopad.iss をビルドし、
     installer\output\MemoPad-setup-<version>.exe を作る

  Inno Setup 6 が必要。未導入なら winget install JRSoftware.InnoSetup で入れる。

.PARAMETER Version
  インストーラに埋め込むバージョン。省略時は csproj の <Version> を使う。

.EXAMPLE
  .\installer\build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src\memopad\memopad.csproj'
$publishDir = Join-Path $PSScriptRoot 'publish'

if (-not $Version) {
    $Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = '0.1.0' }
}

Write-Host "== 発行（self-contained win-x64） version=$Version" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $publishDir -p:PublishSingleFile=false -p:Version=$Version -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish に失敗しました' }

Write-Host '== Inno Setup でインストーラを作成' -ForegroundColor Cyan
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'ISCC.exe（Inno Setup 6）が見つかりません。winget install JRSoftware.InnoSetup で導入してください。' }

& $iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot 'memopad.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC に失敗しました' }
Get-ChildItem (Join-Path $PSScriptRoot 'output') -Filter *.exe | Select-Object FullName, Length
