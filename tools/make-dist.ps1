# ZipPla CE の配布用 zip を作る。
# 使い方: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-dist.ps1 -Version 1.0.0 [-Commit v1.0.0] [-OutDir .work\dist]
#
# - 指定したコミット（既定 HEAD）を git worktree に取り出して AutoBuild 構成でビルドするので、作業ツリーの未コミットの変更は入らない。
# - 入れるもの: ZipPla.exe、ZipPla.exe.config、libwebp の DLL、license\、README.txt（このスクリプトが作る）
# - 入れないもの: ffmpeg.exe（GPL 版で大きい）、7z.dll、config.xml などの実行時に作られるファイル
# - AGPL のため、配布する版のソースが公開リポジトリから取れるようにすること（タグを付けて push する）。
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Commit = "HEAD",
    # zip の置き場所。リポジトリのルートからの相対パスか絶対パス
    [string]$OutDir = ".work\dist",
    # 公開リポジトリに無いコミットでも作る（手元での確認用）
    [switch]$AllowUnpushed
)
$ErrorActionPreference = "Stop"

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repo $OutDir }

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

# ビルド（中間ファイルも含めて専用の出力先に分ける）
$buildOut = Join-Path $repo ".work\tmp\dist-build-$sha"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo ".claude\skills\build\build.ps1") -Configuration AutoBuild -Rebuild -Commit $fullSha -OutputPath $buildOut
if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました" }
$wt = Join-Path $repo ".work\tmp\worktrees\$sha"   # build.ps1 が作る worktree（license をここから取る）

# 配布フォルダを組み立てる
$name = "ZipPlaCE-$Version"
$stageRoot = Join-Path $repo ".work\tmp\dist-stage"
$stage = Join-Path $stageRoot $name
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

foreach ($f in "ZipPla.exe", "ZipPla.exe.config") {
    Copy-Item (Join-Path $buildOut $f) $stage
}
# libwebp（tools\build-libwebp.ps1 で .work\tmp\libwebp-dll に作ったもの）
$webpDir = Join-Path $repo ".work\tmp\libwebp-dll"
foreach ($f in "libwebpdecoder.dll", "libwebpdemux.dll", "libwebp.dll", "libsharpyuv.dll") {
    $src = Join-Path $webpDir $f
    if (-not (Test-Path $src)) { throw "$f がありません。先に tools\build-libwebp.ps1 を実行してください: $src" }
    Copy-Item $src $stage
}
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
Write-Output "確認用に展開したフォルダ: $stage"
