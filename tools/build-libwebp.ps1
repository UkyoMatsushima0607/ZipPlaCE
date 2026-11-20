# libwebp（Google の WebP ライブラリ、BSD 3 条項ライセンス）の DLL を公式ソースからビルドする。
# ZipPla CE は exe と同じフォルダに libwebp.dll（と依存する DLL）があれば WebP をそれで読み、なければ ffmpeg で読む。
#
# 使い方: powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-libwebp.ps1 -OutDir <DLL のコピー先> [-WorkDir <作業フォルダ>] [-Version 1.6.0]
#   - 公式配布サイト（storage.googleapis.com/downloads.webmproject.org）からソースをダウンロードし、配布サイトの MD5 と照合する
#   - Visual Studio（Build Tools を含む。C++ ビルドツールが必要）の nmake で 64bit の DLL をビルドする
#   - Visual C++ ランタイムを静的リンクする（配布先に vcruntime のインストールを求めない）
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$WorkDir = "",
    [string]$Version = "1.6.0"
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $WorkDir) { $WorkDir = Join-Path $repo ".work\tmp\libwebp-build" }
New-Item -ItemType Directory -Force $WorkDir, $OutDir | Out-Null
$WorkDir = (Resolve-Path $WorkDir).Path; $OutDir = (Resolve-Path $OutDir).Path

$name = "libwebp-$Version"
$url = "https://storage.googleapis.com/downloads.webmproject.org/releases/webp/$name.tar.gz"
$tarball = Join-Path $WorkDir "$name.tar.gz"
if (-not (Test-Path $tarball)) {
    Write-Output "ダウンロード: $url"
    Invoke-WebRequest -Uri $url -OutFile $tarball -UseBasicParsing
}

# 配布サイトが返す MD5（x-goog-hash）と照合する
$head = Invoke-WebRequest -Uri $url -Method Head -UseBasicParsing
$expected = ($head.Headers["x-goog-hash"] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -like "md5=*" }) -replace '^md5=', ''
$md5 = [Security.Cryptography.MD5]::Create()
$actual = [Convert]::ToBase64String($md5.ComputeHash([IO.File]::ReadAllBytes($tarball)))
if ($expected -and $actual -ne $expected) { throw "MD5 が一致しません: $actual / 期待値 $expected" }
Write-Output "MD5 一致: $actual"

$src = Join-Path $WorkDir $name
if (-not (Test-Path $src)) {
    tar -xzf $tarball -C $WorkDir
    if ($LASTEXITCODE -ne 0) { throw "展開に失敗しました" }
}

# Visual Studio の 64bit ビルド環境で nmake を実行する
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio の C++ ビルドツールが見つかりません" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
$objDir = Join-Path $WorkDir "output"
$cmd = "`"$vcvars`" >nul && cd /d `"$src`" && nmake /nologo /f Makefile.vc CFG=release-dynamic RTLIBCFG=static OBJDIR=`"$objDir`""
cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "nmake に失敗しました（終了コード $LASTEXITCODE）" }

$bin = Join-Path $objDir "release-dynamic\x64\bin"
$dlls = Get-ChildItem $bin -Filter *.dll
if (-not $dlls) { throw "DLL が見つかりません: $bin" }
foreach ($dll in $dlls) { Copy-Item $dll.FullName $OutDir -Force; Write-Output ("コピー: {0}（{1:N0} バイト）" -f $dll.Name, $dll.Length) }
Copy-Item (Join-Path $src "COPYING") (Join-Path $OutDir "libwebp-COPYING.txt") -Force
Write-Output "ライセンス文: libwebp-COPYING.txt"
