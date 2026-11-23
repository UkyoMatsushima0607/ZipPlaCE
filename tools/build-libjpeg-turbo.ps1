# libjpeg-turbo（IJG ライセンス・修正 BSD ライセンス・zlib ライセンス）の turbojpeg.dll を公式ソースからビルドする。
# ZipPla CE は exe と同じフォルダに turbojpeg.dll があれば JPEG をそれで読み、なければ従来どおり GDI+・WIC で読む。
#
# 使い方: powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-libjpeg-turbo.ps1 -OutDir <DLL のコピー先>
#           [-WorkDir <作業フォルダ>] [-Version 3.1.2] [-Sha256 <ソースの tar.gz の SHA-256>] [-NasmVersion 2.16.03]
#   - GitHub の公式リリースからソースを、nasm.us から NASM（SIMD のコードのアセンブル用）をダウンロードする
#   - ソースの SHA-256 を表示する。-Sha256 を付ければ照合し、違えば止める
#     （公式リリースの .sig を GPG で確かめた値を渡すことを勧める）
#   - Visual Studio（Build Tools を含む。C++ ビルドツールと CMake が必要）で 64bit の DLL をビルドする
#   - Visual C++ ランタイムを静的リンクする（WITH_CRT_DLL=OFF。配布先に vcruntime のインストールを求めない）
#   - ライセンス文（LICENSE.md・README.ijg）を libjpeg-turbo-LICENSE.md・libjpeg-turbo-README.ijg.txt としてコピーする
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$WorkDir = "",
    [string]$Version = "3.1.2",
    [string]$Sha256 = "",
    [string]$NasmVersion = "2.16.03"
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $WorkDir) { $WorkDir = Join-Path $repo ".work\tmp\libjpeg-turbo-build" }
New-Item -ItemType Directory -Force $WorkDir, $OutDir | Out-Null
$WorkDir = (Resolve-Path $WorkDir).Path; $OutDir = (Resolve-Path $OutDir).Path

$name = "libjpeg-turbo-$Version"
$url = "https://github.com/libjpeg-turbo/libjpeg-turbo/releases/download/$Version/$name.tar.gz"
$tarball = Join-Path $WorkDir "$name.tar.gz"
if (-not (Test-Path $tarball)) {
    Write-Output "ダウンロード: $url"
    Invoke-WebRequest -Uri $url -OutFile $tarball -UseBasicParsing
}
$actual = (Get-FileHash $tarball -Algorithm SHA256).Hash
Write-Output "SHA-256: $actual"
if ($Sha256 -and $actual -ne $Sha256.ToUpperInvariant()) { throw "SHA-256 が一致しません: $actual / 期待値 $Sha256" }
if (-not $Sha256) { Write-Warning "-Sha256 を付けていないので照合していません。公式リリースの .sig で確かめた値を渡すことを勧めます。" }

$src = Join-Path $WorkDir $name
if (-not (Test-Path $src)) {
    tar -xzf $tarball -C $WorkDir
    if ($LASTEXITCODE -ne 0) { throw "展開に失敗しました" }
}

# NASM（libjpeg-turbo の SIMD のコードは NASM の書式）
$nasmName = "nasm-$NasmVersion"
$nasmZip = Join-Path $WorkDir "$nasmName-win64.zip"
$nasmDir = Join-Path $WorkDir $nasmName
if (-not (Test-Path (Join-Path $nasmDir "nasm.exe"))) {
    $nasmUrl = "https://www.nasm.us/pub/nasm/releasebuilds/$NasmVersion/win64/$nasmName-win64.zip"
    Write-Output "ダウンロード: $nasmUrl"
    Invoke-WebRequest -Uri $nasmUrl -OutFile $nasmZip -UseBasicParsing
    Write-Output ("NASM の SHA-256: {0}" -f (Get-FileHash $nasmZip -Algorithm SHA256).Hash)
    Expand-Archive $nasmZip -DestinationPath $WorkDir -Force
}
$nasm = Join-Path $nasmDir "nasm.exe"
if (-not (Test-Path $nasm)) { throw "nasm.exe が見つかりません: $nasmDir" }

# Visual Studio の 64bit ビルド環境で CMake（NMake）を実行する
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio の C++ ビルドツールが見つかりません" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
$build = Join-Path $WorkDir "build-$Version"
New-Item -ItemType Directory -Force $build | Out-Null
$configure = "cmake -G `"NMake Makefiles`" -DCMAKE_BUILD_TYPE=Release -DENABLE_SHARED=ON -DENABLE_STATIC=OFF -DWITH_TURBOJPEG=ON " +
    "-DWITH_CRT_DLL=OFF -DREQUIRE_SIMD=ON -DCMAKE_ASM_NASM_COMPILER=`"$nasm`" `"$src`""
$cmd = "`"$vcvars`" >nul && cd /d `"$build`" && $configure && nmake /nologo turbojpeg"
cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました（終了コード $LASTEXITCODE）" }

$dll = Get-ChildItem $build -Recurse -Filter turbojpeg.dll | Select-Object -First 1
if (-not $dll) { throw "turbojpeg.dll が見つかりません: $build" }
Copy-Item $dll.FullName $OutDir -Force
Write-Output ("コピー: {0}（{1:N0} バイト）" -f $dll.Name, $dll.Length)
Copy-Item (Join-Path $src "LICENSE.md") (Join-Path $OutDir "libjpeg-turbo-LICENSE.md") -Force
Copy-Item (Join-Path $src "README.ijg") (Join-Path $OutDir "libjpeg-turbo-README.ijg.txt") -Force
Write-Output "ライセンス文: libjpeg-turbo-LICENSE.md・libjpeg-turbo-README.ijg.txt"
