<div align="center">

![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki)
[![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960)
[![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest)
[![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Language:** **English** | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md)

### Wiki
- [Launch Options](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [Cli Config Options](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### What is VRCVideoCacher?

VRCVideoCacher is a tool used to cache VRChat videos to your local disk and/or fix YouTube videos from failing to load.

### How does it work?

It replaces VRChats yt-dlp.exe with our own stub yt-dlp, this gets replaced on application startup and is restored on exit.

Auto install missing codecs: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Are there any risks involved?

From VRC or EAC? no.

From YouTube/Google? maybe, we strongly recommend you use an alternative Google account if possible.

### How to circumvent YouTube bot detection

In order to fix YouTube videos failing to load, you'll need to install our Chrome extension from [here](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) or Firefox from [here](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter), more info [here](https://github.com/clienthax/VRCVideoCacherBrowserExtension). Visit [YouTube.com](https://www.youtube.com) while signed in, at least once while VRCVideoCacher is running, after VRCVideoCacher has obtained your cookies you can safely uninstall the extension, although be aware that if you visit YouTube again with the same browser while the account is still logged in, YouTube will refresh you cookies invalidating the cookies stored in VRCVideoCacher. To circumvent this I recommended deleting your YouTube cookies from your browser after VRCVideoCacher has obtained them, or if you're using your main YouTube account leave the extension installed, or maybe even use an entirely separate web browser from your main one to keep things simple.

### Fix YouTube videos sometimes failing to play

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

Sync system time, Open Windows Settings -> Time & Language -> Date & Time, under "Additional settings" click "Sync now"

### Uninstalling

- If you have VRCX, delete the startup shortcut "VRCVideoCacher" from `%AppData%\VRCX\startup`
- Delete config and cache from `%AppData%\VRCVideoCacher`
- Delete "yt-dlp.exe" from `%AppData%\..\LocalLow\VRChat\VRChat\Tools` and restart VRChat or rejoin world.
