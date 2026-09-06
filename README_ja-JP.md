<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**言語:** [English](./README.md) | **日本語** | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md) | [Italiano](./README_it-IT.md) | [Русский](./README_ru-RU.md) | [简体中文](./README_zh-CN.md)

### Wiki
- [起動オプション](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [CLI 設定オプション](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### VRCVideoCacher とは？

VRCVideoCacher は、**VRChat** と **Resonite** で YouTube（およびその他）の動画を安定して再生できるようにし、さらにローカルディスクにキャッシュして次回から瞬時に読み込めるようにするツールです。

YouTube は **SABR** と呼ばれる新しいストリーミングプロトコルへ移行を進めており、これは通常の `yt-dlp` のリンク抽出では再生できません。VRCVideoCacher は SABR にネイティブ対応し、シーク可能な動画としてゲームへ再配信します。そのため、YouTube の仕様変更が続いても、最大 **4K**、フルのシークバー付きで再生を維持できます。

### 主な機能

- 📺 **YouTube の新しい SABR ストリームをネイティブ再生** — 通常の yt-dlp では再生できなくなった動画も含め、シーク可能な HD/4K 動画として AVPro へ再配信します。
- 🍪 **「ロボットではないことを確認」を回避** — YouTube の Cookie と、自動管理される PO トークンプロバイダーを利用。手動設定は不要です。
- 💾 **ローカル動画キャッシュ** — サイズ上限を設定可能。再生した動画は次回から瞬時に読み込め、帯域も節約できます。
- 🖥️ **デスクトップアプリ** — ダッシュボード、キャッシュブラウザー、ダウンロードキュー、再生履歴、ログビューアーを搭載。システムトレイに最小化でき、`--nogui` でヘッドレス実行も可能です。
- 🔄 **自動更新** — 本体および各ツール（yt-dlp、FFmpeg、Deno）を自動で最新に保ちます。
- 🎮 **VRChat** と **Resonite**、**Windows** と **Linux** に対応。

### 仕組み

起動時に、VRChat / Resonite の `yt-dlp.exe` を、バックグラウンドで動作する VRCVideoCacher アプリへ動画リクエストを転送する小さなスタブに置き換えます。アプリの終了時には、元の `yt-dlp.exe` が自動的に復元されます。

不足しているコーデックを自動インストール: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### リスクはありますか？

**VRC や EAC から？** ありません。

**YouTube / Google から？** YouTube へのアクセスを自動化する以上、理論上のリスクは常に存在します。慎重を期すなら、別の（サブ）Google アカウントの使用をおすすめします。

ただし実際には:
- メイン開発者は開発当初から自身のメイン YouTube アカウントを使い続けていますが、問題は発生していません。
- 本ツールに起因するアカウント BAN の報告は、これまで一度もありません。

### ボットチェックを回避する仕組み

YouTube の「ロボットではないことを確認してください」という壁は、次の 2 つを組み合わせて突破します:

- **あなたの YouTube Cookie** — リクエストを実際のログイン済みセッションのように見せます。
- **PO トークン** — YouTube が現在要求する proof-of-origin（出所証明）トークン。バックグラウンドで自動的に生成・管理されるため、設定は不要です。

PO トークン側は VRCVideoCacher が自動で処理します。あなたが用意するのは Cookie だけです:

1. ダッシュボードの **YouTube Cookie セットアップ** ウィザードを開きます。[Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) または [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter) 用のブラウザー拡張機能がインストールされます（[詳細](https://github.com/clienthax/VRCVideoCacherBrowserExtension)）。
2. VRCVideoCacher を起動した状態で、ログイン済みの [YouTube.com](https://www.youtube.com) を少なくとも 1 回開きます。
3. Cookie が取得されたら、拡張機能はアンインストールして構いません。

> **注意:** ログイン状態のまま同じブラウザーで再度 YouTube を閲覧すると、YouTube が Cookie をローテーションし、VRCVideoCacher が保存した Cookie が無効になります。
>
> これを避けるには、次のいずれかを行ってください:
> - 取得後にそのブラウザーから YouTube の Cookie を削除する、または
> - 拡張機能をインストールしたままにする、または
> - この用途専用に別のブラウザー／アカウントを使う。

### 広告について（非 Premium アカウント）

SABR は広告を**サーバー側**で挿入します。非 Premium アカウントの場合、クライアントが広告を「視聴」するまで YouTube は実際の動画を送信せず、動画データが届く前に短い強制的な待機時間が発生します。VRChat では、動画の再生が始まるまでに数秒の一時停止として現れます。

- **YouTube Premium アカウント** は SABR が広告なしで配信され、このような遅延はありません。最も快適に再生できます。
- **非 Premium アカウント** では、SABR 再生時にこの開始遅延が発生します。

遅延が気になる場合は、設定の「SABR ストリーミングを優先」を**オフ**にしてください。VRCVideoCacher はまず通常の直接 URL を試し、SABR でしか再生できない動画のみ SABR にフォールバックするため、ほとんどの動画で広告遅延を回避できます。

非 Premium アカウントで実際に SABR をストリーミングしている間は、この遅延を取り除く方法はありません。待機時間は VRCVideoCacher ではなく YouTube のサーバーによって強制されるものです。

### YouTube 動画が時々再生されない問題の修正

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

システム時刻を同期してください: Windows の設定 → 時刻と言語 → 日付と時刻 →「追加の設定」の「今すぐ同期」をクリック。また、**Windows Update** を最新に保つことも重要です。新しいストリームで使われる一部の音声コーデックは、最新の Windows でないと正しくデコードできません。

### アンインストール

- VRCX を使用している場合、`%AppData%\VRCX\startup` からスタートアップショートカット「VRCVideoCacher」を削除します。
- `%AppData%\VRCVideoCacher` から設定とキャッシュを削除します。
- `%LocalAppData%Low\VRChat\VRChat\Tools` から「yt-dlp.exe」を削除し、VRChat を再起動するかワールドに再入場します。
