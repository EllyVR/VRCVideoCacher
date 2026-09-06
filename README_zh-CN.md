<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**语言:** [English](./README.md) | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md) | [Italiano](./README_it-IT.md) | [Русский](./README_ru-RU.md) | **简体中文**

### Wiki
- [启动选项](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [命令行配置选项](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### VRCVideoCacher 是什么？

VRCVideoCacher 让 YouTube（及其他网站）的视频在 **VRChat** 和 **Resonite** 中稳定播放，并将其缓存到本地磁盘，以便下次即时加载。

YouTube 正在迁移到一种名为 **SABR** 的新流媒体协议，普通的 `yt-dlp` 链接提取无法播放它。VRCVideoCacher 原生支持 SABR，并将其作为可拖动进度的视频重新推流给游戏——因此即使 YouTube 不断变化，也能以最高 **4K**、带完整进度条继续播放。

### 功能

- 📺 **原生播放 YouTube 的新 SABR 流** —— 包括普通 yt-dlp 已无法播放的视频 —— 作为可拖动进度的 HD/4K 视频重新推流给 AVPro。
- 🍪 **绕过“登录以确认您不是机器人”** —— 使用你的 YouTube Cookie 以及自动管理的 PO 令牌提供程序，无需手动设置。
- 💾 **本地视频缓存** —— 可配置容量上限，重复播放即时加载并节省带宽。
- 🖥️ **桌面应用** —— 提供仪表盘、缓存浏览器、下载队列、播放历史和日志查看器。可最小化到系统托盘，或使用 `--nogui` 无界面运行。
- 🔄 **自动更新** —— 自动让自身及其工具（yt-dlp、FFmpeg、Deno）保持最新。
- 🎮 支持 **VRChat** 和 **Resonite**，可在 **Windows** 和 **Linux** 上运行。

### 工作原理

启动时，它会将 VRChat/Resonite 的 `yt-dlp.exe` 替换为一个小型存根，该存根将视频请求转发给在后台运行的 VRCVideoCacher 应用。应用退出时会自动恢复原始的 `yt-dlp.exe`。

自动安装缺失的编解码器：[VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### 有风险吗？

**来自 VRC 或 EAC？** 没有。

**来自 YouTube/Google？** 自动化访问 YouTube 始终存在理论上的风险，因此若想谨慎起见，可使用一个单独的/备用的 Google 账号。

不过在实际使用中：
- 主要开发者从开发之初就一直使用自己的主 YouTube 账号，未遇到任何问题。
- 我们从未收到任何一例与本工具相关的账号封禁报告。

### 我们如何绕过机器人检测

YouTube 的“登录以确认您不是机器人”这道墙，是靠两样东西协同突破的：

- **你的 YouTube Cookie** —— 让请求看起来像一个真实的已登录会话。
- **PO 令牌** —— YouTube 现在要求的来源证明（proof-of-origin）令牌。它会在后台自动生成和管理，无需任何设置。

PO 令牌这一侧由 VRCVideoCacher 自行处理——你只需要提供 Cookie：

1. 打开仪表盘中内置的 **YouTube Cookie 设置** 向导。它会安装我们的浏览器扩展，适用于 [Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) 或 [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)（[更多信息](https://github.com/clienthax/VRCVideoCacherBrowserExtension)）。
2. 在 VRCVideoCacher 运行的情况下，登录后至少访问一次 [YouTube.com](https://www.youtube.com)。
3. 获取到 Cookie 后，你就可以卸载该扩展了。

> **注意：** 如果你在保持登录的同一浏览器中再次访问 YouTube，YouTube 会轮换你的 Cookie，使 VRCVideoCacher 已保存的 Cookie 失效。
>
> 为避免这种情况，请任选其一：
> - 之后从该浏览器中删除你的 YouTube Cookie，或
> - 保持该扩展处于安装状态，或
> - 专门为此使用一个单独的浏览器/账号。

### 关于广告的说明（非 Premium 账号）

SABR 会在**服务器端**插入广告。对于非 Premium 账号，YouTube 会在客户端“观看”完广告之前扣住真正的视频——必须先等过一段强制的延迟，才会发送任何视频数据。在 VRChat 中，这表现为视频开始播放前有几秒钟的停顿。

- **YouTube Premium 账号** 获得无广告的 SABR，没有这种延迟。这是最流畅的体验。
- **非 Premium 账号** 在 SABR 播放时会遇到这种开始延迟。

如果这些延迟让你困扰，请在设置中**关闭**“优先使用 SABR 流媒体”选项。这样 VRCVideoCacher 会先尝试常规的直链，仅对只能通过 SABR 播放的视频回退到 SABR——从而在大多数视频上避免广告延迟。

在非 Premium 账号上实际进行 SABR 流媒体传输时，无法消除这段延迟：等待是由 YouTube 的服务器强制的，而非 VRCVideoCacher。

### 修复 YouTube 视频有时无法播放的问题

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

同步系统时间：Windows 设置 → 时间和语言 → 日期和时间 → 在“其他设置”下点击“立即同步”。保持 **Windows Update** 最新同样重要——较新流媒体使用的某些音频编解码器只有在已更新的 Windows 上才能正确解码。

### 卸载

- 如果你使用 VRCX，请从 `%AppData%\VRCX\startup` 删除启动快捷方式“VRCVideoCacher”。
- 从 `%AppData%\VRCVideoCacher` 删除配置和缓存。
- 从 `%LocalAppData%Low\VRChat\VRChat\Tools` 删除“yt-dlp.exe”，然后重启 VRChat 或重新进入世界。
