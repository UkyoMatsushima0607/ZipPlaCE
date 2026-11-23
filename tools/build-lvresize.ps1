# lvresize.dll（source/native/lvresize。Lanczos・Spline などの縮小の中心部分のネイティブ版）を Visual Studio でビルドする。
# ZipPla CE は exe と同じフォルダに lvresize.dll があれば、ガンマ 1.0 の縮小をそれで描く（実行時に AVX-512・AVX2・スカラーを選ぶ。結果は C# の版と同じ）。
#
# 使い方: powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-lvresize.ps1 -OutDir <DLL のコピー先> [-Source <lvresize.c のフォルダ>]
#   - Visual Studio（Build Tools を含む。C++ ビルドツールが必要）の cl で 64bit の DLL をビルドする
#   - Visual C++ ランタイムを静的リンクする（/MT。配布先に vcruntime のインストールを求めない）
#   - AVX2・AVX-512 の命令は関数の中で CPU を確かめてから使うので、/arch は付けない
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$Source = ""
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $Source) { $Source = Join-Path $repo "source\native\lvresize" }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path; $Source = (Resolve-Path $Source).Path

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio の C++ ビルドツールが見つかりません" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
$obj = Join-Path ([IO.Path]::GetTempPath()) ("lvresize-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $obj | Out-Null
try {
    $cmd = "`"$vcvars`" >nul && cd /d `"$obj`" && cl /nologo /O2 /W3 /MT /LD `"$Source\lvresize.c`" /Fe:lvresize.dll /link /NOLOGO"
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました（終了コード $LASTEXITCODE）" }
    $dll = Join-Path $obj "lvresize.dll"
    Copy-Item $dll $OutDir -Force
    Write-Output ("コピー: lvresize.dll（{0:N0} バイト）" -f (Get-Item $dll).Length)
}
finally {
    Remove-Item $obj -Recurse -Force -ErrorAction SilentlyContinue
}
