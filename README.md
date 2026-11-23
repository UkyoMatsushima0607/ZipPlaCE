# ZipPla CE

## これはなに？
`https://sites.google.com/site/riostoolbox/zippla`（リンク切れ）で配布されていた ZipPla の非公式改造版です。  
himamon 氏の「ZipPla雑改造版」をもとに、高速化・省メモリ化と圧縮ファイルの読み込みの高速化を行っています。  
オリジナルのreadmeは[readme_original.txt](readme_original.txt)を参照。

## ダウンロード
[Releases](https://github.com/UkyoMatsushima0607/ZipPlaCE/releases/latest) から最新版の zip をダウンロードし、フォルダごと好きな場所に展開して `ZipPla.exe` を起動してください。インストールは不要です。  
設定は `ZipPla.exe` と同じフォルダに保存されるので、Program Files など書き込みできない場所には置かないでください。

## 改造内容
### ZipPla雑改造版から引き継いだもの
- 現在表示しているアイテムのみサムネイルを読み込むようにしました。ファイル数の多いフォルダを開いた場合でもメモリ使用量が増加しなくなります。

### カタログ（ファイル一覧・サムネイル）
- サムネイルを並列に作成し、大きな JPEG は縮小してデコードするようにしました。
- サムネイルキャッシュを JPEG で保存し、書き込みは後回しにしました。切り抜きの位置もキャッシュに記録します。
- ファイル一覧を仮想モードにし、ファイル数の多いフォルダでのメモリ使用量と作り直しの時間を減らしました。
- 書庫の中の表示、フォルダの列挙、履歴の処理を速くしました。

### ビューア
- ページの読み込みを並列に行い、先読みしたページは縮小してデコードするようにしました。
- 縮小画像を作ったあとの原寸画像を捨てる、グレースケールを 8bpp のまま扱うなどで、メモリ使用量を減らしました。

### 圧縮ファイル・画像形式
- solid 書庫（7z・RAR5 など）は 7z.dll 経由で読み込み、ページを順に読むときの展開のやり直しをなくしました。
- WebP は libwebp で直接デコードします（アニメーション WebP にも対応）。
- JPEG は、exe と同じフォルダに libjpeg-turbo の turbojpeg.dll があればそれでデコードします（SIMD の IDCT・DCT の段階の縮小）。なければ従来どおり GDI+・WIC で読みます。

## 実行に必要なもの
- .NET Framework 4.8（Windows 10 / 11 には標準で入っています）
- 7z.dll（7z・rar などを開く場合）：exe と同じフォルダの `7z-x64.dll` / `7z.dll`、なければ 7-Zip のインストール先のものを使います。
- ffmpeg.exe（動画のサムネイルなどに使う場合）：[ffmpeg.org](https://ffmpeg.org/download.html) の「Windows builds」から入手し、exe と同じフォルダに置いてください。

## ビルドに関して
Visual Studio 2022 の MSBuild で `source/ZipPla.sln` をビルドします（.NET Framework 4.8）。  
Releaseビルドの場合サムネイルの読み込み失敗時にエラーダイアログが表示されます。  
AutoBuildビルドの場合はエラーダイアログが表示されないため、こちらを利用を推奨します。

## ライセンス
オリジナルのZipPlaと同じく、AGPLでライセンスされています。  
同梱の turbojpeg.dll（libjpeg-turbo）は IJG・修正 BSD・zlib のライセンスです。This software is based in part on the work of the Independent JPEG Group.
