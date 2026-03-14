<div align="center">

![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki)
[![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960)
[![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest)
[![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/t6x6p6Tzs)

<hr>
</div>

**Language:** [English](./README.md) | **日本語** | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md)

### VRCVideoCacher とは何でしょうか？

VRCVideoCacher は、VRChat での動画をローカルディスクにキャッシュしたり、YouTube の動画の読み込み失敗を修正などを行うためのツールです。

### どのように動作しますか？

VRChat の yt-dlp.exe を独自の stub yt-dlp に置換します。これはアプリケーションの起動時に置換され、終了時に復元されます。

不足しているコーデックを自動でインストール: 
[VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### 何かリスクはありますか？

VRC か EAC で何かあったかって？いいえ。

YouTube/Google からは？おそらく可能なのでしょうけど、できるのであれば別の Google アカウントを使用することを強く推奨します。

### YouTube のボット検出を回避する方法

YouTube の動画の読み込みに失敗する問題を解決するには、[こちら](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)から Chrome の拡張機能をインストールするか、もしくは[こちら](https://addons.mozilla.org/ja-JP/firefox/addon/vrcvideocachercookiesexporter)から Firefox の拡張機能をインストールする必要があります。詳細は[こちら](https://github.com/clienthax/VRCVideoCacherBrowserExtension)をご覧ください。VRCVideoCacher の実行中に少なくとも、[YouTube.com](https://www.youtube.com) にログインした状態で 1 回アクセスしてください。VRCVideoCacher が Cookie を受信後は、拡張機能を安全にアンインストールできます。ただし、アカウントがログインしたまま同じブラウザーで YouTube に再度アクセスすると、YouTube が Cookie を更新し、VRCVideoCacher に保存されている Cookie を無効にしてしまうことにご注意ください。これを回避するには、VRCVideoCacher が Cookie を受信後にブラウザーの YouTube の Cookie を削除するか、メインの YouTube アカウントを使用している場合であれば拡張機能をインストールしたままにするか、シンプルにするためにメインのアカウントとは全く別の Web ブラウザーを使用することを推奨します。

### YouTube の動画が再生できないことがある問題を修正するには

> 読み込みに失敗しました。この症状はファイルが見つからないかコーデックが非対応か動画の解像度が高すぎるか、システムリソースが不足しています。

システム時刻を同期するには、Windows の設定 -> 時刻と言語 -> 日付と時刻を開き、「追加設定」の下にある「今すぐ同期」をクリックします。

### アンインストール

- VRCX を使用している場合は `%AppData%\VRCX\startup` からスタートアップのショートカット「VRCVideoCacher」を削除します
- `%AppData%\VRCVideoCacher` から設定とキャッシュを削除します
- `%AppData%\..\LocalLow\VRChat\VRChat\Tools` から「yt-dlp.exe」を削除し、VRChat を再起動するかワールドに Join し直してください
