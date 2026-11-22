# ZipPla CE の配布用 zip を作る。
# 使い方: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-dist.ps1 -Version 1.0.0 [-Commit v1.0.0] [-OutDir .work\dist]
#         [-WebPDir <libwebp の DLL のフォルダ>] [-NoWebP] [-AllowUnpushed]
#
# - 指定したコミット（既定 HEAD）を一時フォルダの git worktree に取り出し、AutoBuild 構成でクリーンビルドする。
#   作業ツリーの未コミットの変更は入らない。worktree と中間ファイルは終わったら消す。
# - 必要なもの: git、Visual Studio 2022（MSBuild と .NET Framework 4.8 の開発パック）
# - libwebp の DLL は tools\build-libwebp.ps1 -OutDir <フォルダ> で作り、-WebPDir で指定する（既定 .work\tmp\libwebp-dll）。
#   -NoWebP なら入れない（WebP は ffmpeg で読み、アニメーション WebP は読めない）。
# - 入れるもの: ZipPla.exe、ZipPla.exe.config、libwebp の DLL、license\、README.txt（このスクリプトが作る）
# - 入れないもの: ffmpeg.exe（GPL 版で大きい）、7z.dll、config.xml などの実行時に作られるファイル
# - AGPL のため、配布する版のソースが公開リポジトリから取れるようにすること（タグを付けて push する）。
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Commit = "HEAD",
    # zip の置き場所。リポジトリのルートからの相対パスか絶対パス
    [string]$OutDir = ".work\dist",
    # libwebp の DLL（libwebpdecoder.dll・libwebpdemux.dll・libwebp.dll・libsharpyuv.dll）があるフォルダ
    [string]$WebPDir = ".work\tmp\libwebp-dll",
    [switch]$NoWebP,
    # 公開リポジトリに無いコミットでも作る（手元での確認用）
    [switch]$AllowUnpushed
)
$ErrorActionPreference = "Stop"

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repo $OutDir }
if (-not [IO.Path]::IsPathRooted($WebPDir)) { $WebPDir = Join-Path $repo $WebPDir }
$webpNames = "libwebpdecoder.dll", "libwebpdemux.dll", "libwebp.dll", "libsharpyuv.dll"
if (-not $NoWebP) {
    foreach ($f in $webpNames) {
        if (-not (Test-Path (Join-Path $WebPDir $f))) { throw "$f がありません: $WebPDir（tools\build-libwebp.ps1 -OutDir で作るか、-NoWebP を付けてください）" }
    }
}

# MSBuild を探す（Visual Studio 2022。旧形式の csproj なので dotnet build は使えない）
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe が見つかりません。Visual Studio をインストールしてください。" }
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild.exe が見つかりません。" }

$fullSha = (git -C $repo rev-parse "$Commit^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or -not $fullSha) { throw "コミットが見つかりません: $Commit" }
$sha = (git -C $repo rev-parse --short $fullSha).Trim()

# 配布物のソースを公開できているか（origin のブランチに含まれているか）
cmd /c "git -C `"$repo`" fetch --quiet origin >nul 2>&1"
$remoteBranches = @(git -C $repo branch -r --contains $fullSha 2>$null | Where-Object { $_ -match 'origin/' })
if ($remoteBranches.Count -eq 0) {
    $msg = "コミット $sha は origin に push されていません。AGPL のためソースを公開してから配布してください。"
    if ($AllowUnpushed) { Write-Warning $msg } else { throw "$msg（確認用に作るなら -AllowUnpushed）" }
}
$tags = @(git -C $repo tag --points-at $fullSha)
if ($tags.Count -eq 0) { Write-Warning "コミット $sha にタグがありません。配布するなら git tag v$Version $sha と git push origin v$Version を推奨します。" }

# 一時フォルダに worktree を作ってビルドする（中間ファイルも含めてそこに閉じ込める）
$work = Join-Path ([IO.Path]::GetTempPath()) "ZipPlaCE-dist-$sha"
if (Test-Path $work) { cmd /c "git -C `"$repo`" worktree remove --force `"$work\src`" >nul 2>&1"; Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null
$wt = Join-Path $work "src"
$buildOut = Join-Path $work "bin\"
$name = "ZipPlaCE-$Version"
$stage = Join-Path $work "stage\$name"
try {
    # git は進捗を標準エラーに出すので、PowerShell 5.1 でエラー扱いにならないよう cmd 経由で呼ぶ
    cmd /c "git -C `"$repo`" worktree add --detach `"$wt`" $fullSha >nul 2>&1"
    if ($LASTEXITCODE -ne 0) { throw "git worktree add に失敗しました: $wt" }
    $log = Join-Path $work "build.log"
    Write-Output "ビルド: $sha（AutoBuild）"
    & $msbuild (Join-Path $wt "source\ZipPla\ZipPla.csproj") /t:Rebuild /p:Configuration=AutoBuild /p:Platform=AnyCPU "/p:OutputPath=$buildOut" "/p:IntermediateOutputPath=$(Join-Path $work 'obj\')" /m /nologo /v:minimal "/flp:LogFile=$log;Verbosity=normal;Encoding=UTF-8" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Get-Content $log -Encoding UTF8 | Where-Object { $_ -match ': error ' } | Select-Object -Unique -First 20 | ForEach-Object { Write-Output "  $_" }
        throw "ビルドに失敗しました（ログ: $log）"
    }
    $warnings = @(Get-Content $log -Encoding UTF8 | Where-Object { $_ -match ': warning ' } | Select-Object -Unique)
    Write-Output ("ビルド成功（警告 {0} 件）" -f $warnings.Count)

    # 配布フォルダを組み立てる
    New-Item -ItemType Directory -Force $stage | Out-Null
    foreach ($f in "ZipPla.exe", "ZipPla.exe.config") { Copy-Item (Join-Path $buildOut $f) $stage }
    if (-not $NoWebP) { foreach ($f in $webpNames) { Copy-Item (Join-Path $WebPDir $f) $stage } }
    Copy-Item (Join-Path $wt "license") (Join-Path $stage "license") -Recurse

$exeVersion = (Get-Item (Join-Path $stage "ZipPla.exe")).VersionInfo.FileVersion
$origin = (git -C $repo remote get-url origin).Trim() -replace '\.git$', ''
$sourceRef = if ($tags.Count -gt 0) { $tags[0] } else { $fullSha }
$readme = @"
ZipPla CE $Version
==================

画像ビューア ZipPla（Rio's Toolbox 作）の非公式改造版です。
高速化・省メモリ化と、圧縮ファイルの読み込みの高速化を行っています。

- ZipPla 本体のバージョン: $exeVersion
- ソースコード: $origin/tree/$sourceRef
  （コミット $fullSha）

使い方
------
フォルダごと好きな場所に置き、ZipPla.exe を起動してください。インストールは不要です。
設定（config.xml など）は ZipPla.exe と同じフォルダに保存されます。
書き込みできないフォルダ（Program Files など）には置かないでください。

必要なもの
----------
- .NET Framework 4.8（Windows 10 / 11 には標準で入っています）
- 7-Zip（7z・rar などを開く場合）
  7-Zip をインストールしておけば、その 7z.dll を使います。
  ZipPla.exe と同じフォルダに 7z.dll（または 7z-x64.dll）を置いても使えます。
- ffmpeg.exe（動画のサムネイル・再生、一部の画像形式を使う場合）
  https://ffmpeg.org/download.html から入手し、ZipPla.exe と同じフォルダに置いてください。
  ffmpeg.exe がないと起動時に案内が出ます。不要なら、その画面で
  「起動時に ffmpeg.exe の存在をチェックする」を外してください。

初回起動
--------
初めて起動したときは、サムネイルキャッシュの保存方法を尋ねる画面が出ます。

ライセンス
----------
GNU Affero General Public License v3.0（license\AGPL.txt）です。
同梱ライブラリの著作権表示は license\About.txt を、
libwebp のライセンスは license\libwebp-COPYING.txt・libwebp-PATENTS.txt を見てください。
"@
# メモ帳などで文字化けしないよう BOM 付き UTF-8・CRLF で書く
[IO.File]::WriteAllText((Join-Path $stage "README.txt"), ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding $true))

# zip にする
New-Item -ItemType Directory -Force $OutDir | Out-Null
$zip = Join-Path $OutDir "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash

Write-Output ""
Write-Output "中身:"
Get-ChildItem $stage -Recurse -File | ForEach-Object { Write-Output ("  {0,-40} {1,10:N0}" -f $_.FullName.Substring($stage.Length + 1), $_.Length) }
Write-Output ""
Write-Output ("作成しました: {0}（{1:N2} MB）" -f $zip, ((Get-Item $zip).Length / 1MB))
Write-Output "SHA256: $hash"
}
finally {
    # worktree と中間ファイルを消す
    cmd /c "git -C `"$repo`" worktree remove --force `"$wt`" >nul 2>&1"
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
