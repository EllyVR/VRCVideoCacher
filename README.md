<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Language:** **English** | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md) | [Italiano](./README_it-IT.md) | [Русский](./README_ru-RU.md) | [简体中文](./README_zh-CN.md) | [Français](./README_fr-FR.md)

### Wiki
- [Launch Options](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [Cli Config Options](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### What is VRCVideoCacher?

VRCVideoCacher keeps YouTube (and other) videos playing reliably in **VRChat** and **Resonite**, and caches them to your local disk so they load instantly next time.

YouTube has been moving to a new streaming protocol called **SABR**, which ordinary `yt-dlp` link extraction can't play. VRCVideoCacher speaks SABR natively and restreams it to the game as seekable video — so playback keeps working through YouTube's changes, in up to **4K**, with a full scrub bar.

### Features

- 📺 **Plays YouTube's new SABR streams** natively — including videos that plain yt-dlp can no longer play — restreamed to AVPro as seekable HD/4K video.
- 🍪 **Beats "Sign in to confirm you're not a bot"** using your YouTube cookies plus an auto-managed PO token provider — no manual setup.
- 💾 **Local video cache** with a configurable size limit, so repeat plays load instantly and cut bandwidth.
- 🖥️ **Desktop app** with a dashboard, cache browser, download queue, play history and log viewer. Minimizes to the system tray, or run headless with `--nogui`.
- 🔄 **Self-updating** — keeps itself and its tools (yt-dlp, FFmpeg, Deno) up to date automatically.
- 🎮 Works with **VRChat** and **Resonite**, on **Windows** and **Linux**.

### How does it work?

On startup it replaces VRChat/Resonite's `yt-dlp.exe` with a small stub that forwards video requests to the VRCVideoCacher app running in the background. The original `yt-dlp.exe` is restored automatically when the app exits.

Auto installs missing codecs: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Are there any risks involved?

**From VRC or EAC?** No.

**From YouTube/Google?** There's always a theoretical risk when automating access to YouTube, so use a separate/alternative Google account if you want to be cautious.

In practice, though:
- The main developer has used their primary YouTube account since the very start of development, with no issues.
- We've never had a single report of an account ban related to this tool.

### How we fix bot checks

YouTube's "Sign in to confirm you're not a bot" wall is beaten with two things working together:

- **Your YouTube cookies** — so requests look like a real signed-in session.
- **A PO token** — a proof-of-origin token YouTube now demands. This is minted and managed for you automatically in the background; no setup needed.

VRCVideoCacher handles the PO token side on its own — all you need to provide is your cookies:

1. Open the built-in **YouTube Cookie Setup** wizard on the dashboard. It installs our browser extension for [Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) or [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter) ([more info](https://github.com/clienthax/VRCVideoCacherBrowserExtension)).
2. With VRCVideoCacher running, visit [YouTube.com](https://www.youtube.com) while signed in, at least once.
3. Once it has your cookies, you can uninstall the extension.

> **Note:** if you browse YouTube again in the same browser while still logged in, YouTube rotates your cookies and invalidates the ones VRCVideoCacher stored.
>
> To avoid this, either:
> - Delete your YouTube cookies from that browser afterwards, or
> - Leave the extension installed, or
> - Use a separate browser/account just for this.

### A note on ads (non-Premium accounts)

SABR injects ads **server-side**. For non-Premium accounts, YouTube withholds the actual video until the client "watches" the ad — it has to wait out a short enforced delay before any video data is sent. In VRChat this shows up as a pause of a few seconds before a video starts playing.

- **YouTube Premium accounts** are served SABR ad-free, with no such delay. This is the smoothest experience.
- **Non-Premium accounts** will see those startup delays on SABR playback.

If the delays bother you, turn **off** the "Prefer SABR streaming" option in Settings. VRCVideoCacher will then try the normal direct URL first, and only fall back to SABR for videos that are SABR-only — avoiding the ad delay on most videos.

There's no way to remove the delay while actually streaming SABR on a non-Premium account: the wait is enforced by YouTube's servers, not by VRCVideoCacher.

### Fix YouTube videos sometimes failing to play

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

Sync your system time: Windows Settings → Time & Language → Date & Time → under "Additional settings" click "Sync now". Keeping **Windows Update** current also matters — some audio codecs used by newer streams only decode correctly on an up-to-date Windows.

### Uninstalling

- If you have VRCX, delete the startup shortcut "VRCVideoCacher" from `%AppData%\VRCX\startup`
- Delete config and cache from `%AppData%\VRCVideoCacher`
- Delete "yt-dlp.exe" from `%LocalAppData%Low\VRChat\VRChat\Tools` and restart VRChat or rejoin world.
