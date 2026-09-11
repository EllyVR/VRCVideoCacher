<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**言語:** [English](./README.md) | **日本語** | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md) | [Italiano](./README_it-IT.md) | [Русский](./README_ru-RU.md) | [简体中文](./README_zh-CN.md)


### Wiki
- [起動のオプション](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [Cli 構成のオプション](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### VRCVideoCacher とはなんですか？

VRCVideoCacher は、**VRChat** と **Resonite** で YouTube (とその他) の動画を安定して再生可能な状態にします。また、動画をローカルディスクにキャッシュするため、次回以降は即座に読み込みが可能です。 

YouTube は現在、**SABR** と呼ばれる新しいストリーミングプロトコルへの移行を進めていますが、通常版の `yt-dlp` によるリンク抽出では再生ができません。VRCVideoCacher は SABR にネイティブで対応しており、シーク可能な動画としてゲーム内に再ストリーミングを行います。そのため、YouTube 側の仕様変更の影響を受けることなく、フル機能なスクラブバー (シークバー) を備えた最大 **4K** 画質での動画再生が可能になります。

### 機能

- 📺 **YouTube の SABR ストリームをネイティブで再生** — 通常の yt-dlp で再生できなくなった動画を含め、シーク可能な HD/4K 動画として AVPro へ再ストリーミングします。
- 🍪 **「ロボットではないことを確認」を回避** — YouTube の Cookie と自動管理される PO トークンプロバイダーを使用するため、手動での設定が不要です。
- 💾 **ローカルに動画をキャッシュ** — 繰り返し再生時の読み込みが瞬時に行われるようになり、帯域幅の消費を抑えることができます。サイズは任意で制限を設定可能です。
- 🖥️ **デスクトップアプリ** — ダッシュボード、キャッシュブラウザー、ダウンロードキュー、再生履歴、ログビューアーの機能に加え、システムトレイの最小化や `--nogui` のオプションでヘッドレス実行が可能です。
- 🔄 **自動更新** — 本体およびツール群 (yt-dlp、FFmpeg、Deno) を自動的に最新の状態にできます。
- 🎮 **VRChat** と **Resonite**、**Windows** と **Linux** に対応しています。

### どのような仕組みなのですか？

起動時に VRChat や Resonite 内の `yt-dlp.exe` をバックグラウンドで動作する VRCVideoCacher アプリへ動画の要求を転送する小さなスタブ (代理となるプログラム) に置換します。アプリの終了時は元の `yt-dlp.exe` が自動的に復元されます。

不足しているコーデックを自動的にインストール: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### 何かリスクはありますか？

**VRC または EAC からのリスクは？** いいえ。

**YouTube/Google からは？** YouTube へのアクセスを自動化することは理論上では常にリスクを伴うため、念のためにサブアカウントを使用することをおすすめします。

ですが、実際は:
- 開発者本人が開発初期からメインの YouTube アカウントを使用していますが、トラブルは一切発生していません。
- これまでに本ツールの使用に関連したアカウント停止の報告も一切受けていません。

### ボットの確認の修正方法
YouTube の「ボットではないことを確認するためにログインしてください」という問題の壁は、2 つの要素を組み合わせることで突破可能です:

- **YouTube の Cookie** — ログイン済みのセッションを本物に見せかけるために使用します。
- **PO トークン** — YouTube が現在要求している「オリジン証明トークン」です。バックグラウンドで自動的に生成・管理されるため、手動での設定は不要です。

PO トークンは VRCVideoCacher 側が処理を自動で行うため、所持している Cookie を提供するだけで利用できます:

1. ダッシュボードにある内蔵の「YouTube Cookie のセットアップ」ウィザードを開きます。
2. VRCVideoCacher を起動した状態でログインをしたまま [YouTube.com](https://www.youtube.com) に一回以上アクセスします。
3. Cookie の取得が完了後に拡張機能はアンインストールして構いません。

> **注意:** 同じブラウザーでログインしたまま再度 YouTube を閲覧すると、Cookie がローテーション (更新) され、VRCVideoCacher に保存された Cookie が無効化されてしまいます。
>
> これを回避するには、以下のいずれかを行ってください:
> - 操作後にそのブラウザーから YouTube の Cookie を削除する
> - 拡張機能をインストールしたままにする
> - 本ツール専用の別のブラウザーまたは、アカウントを使用する

### 広告に関する注意 (非 Premium アカウント)

SABR は**サーバー側**で広告を挿入します。非 Premium アカウントの場合、クライアントが広告を「視聴」するまで YouTube 側が実際の動画データを送信しないため、動画データが送信されるまでに強制的な待機時間が発生します。VRChat 内では、動画再生が開始する前に数秒間の停止として表示されます。

- **YouTube Premium アカウント**は SABR でも広告なしで配信されるため、そのような遅延は発生しません。これが最もスムーズに再生できる環境になります。
- **非 Premium アカウント**は SABR 再生時動画開始までに遅延が発生します。

遅延が気になる場合は、設定内の「SABR ストリーミングを優先」を**オフ**にしてください。これより、VRCVideoCacher は始めに通常の直接 URL での再生を試行、SABR 専用の動画でのみ SABR へフォールバックするようになり、ほとんどの動画で広告による遅延を回避できます。

非 Premium のアカウントで実際に SABR ストリーミングを行う場合、この遅延を回避する方法はありません。この待機時間は VRCVideoCacher によるものではなく、YouTube のサーバー側が強制しているためです。

### YouTube の再生が時々再生に失敗する問題の修正

> 読み込みに失敗しました。ファイルが見つからないか、コーデックが非対応、解像度が高すぎる、またはシステムリソースが不足しています。

システムの時刻を同期してください: 「Windows の設定」 → 「時刻と言語」 → 「日付と時刻」を開いて、「追加の設定」内にある「今すぐ同期」をクリックします。また、**Windows Update** で最新の状態に保つことも重要です。最新のストリーミング形式で使われている一部の音声コーデックは、最新の状態な Windows でのみ正常にデコードされます。

### アンインストール

- VRCX を使用している方は、`%AppData%\VRCX\startup` からスタートアップのショートカット「VRCVideoCacher」を削除します
- `%AppData%\VRCVideoCacher` から構成ファイルとキャッシュを削除します
- `%LocalAppData%Low\VRChat\VRChat\Tools` から「yt-dlp.exe」を削除し、VRChat を再起動するか、ワールドに Join し直してください
