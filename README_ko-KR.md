<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**언어:** [English](./README.md) | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | **한국어** | [Português do Brasil](./README_pt-BR.md) | [Italiano](./README_it-IT.md) | [Русский](./README_ru-RU.md) | [简体中文](./README_zh-CN.md)

### Wiki
- [실행 옵션](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [CLI 설정 옵션](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### VRCVideoCacher란?

VRCVideoCacher는 **VRChat**과 **Resonite**에서 YouTube(및 기타) 영상을 안정적으로 재생할 수 있게 해 주고, 로컬 디스크에 캐시하여 다음부터 즉시 불러오도록 하는 도구입니다.

YouTube는 **SABR**이라는 새로운 스트리밍 프로토콜로 전환하고 있으며, 이는 일반적인 `yt-dlp` 링크 추출로는 재생할 수 없습니다. VRCVideoCacher는 SABR을 네이티브로 지원하여 탐색(시크) 가능한 영상으로 게임에 다시 스트리밍합니다. 덕분에 YouTube의 변경에도 불구하고 최대 **4K**, 전체 탐색 바와 함께 재생을 유지할 수 있습니다.

### 기능

- 📺 **YouTube의 새로운 SABR 스트림을 네이티브로 재생** — 일반 yt-dlp로는 더 이상 재생할 수 없는 영상까지 포함하여, 탐색 가능한 HD/4K 영상으로 AVPro에 다시 스트리밍합니다.
- 🍪 **"로봇이 아님을 확인하려면 로그인하세요" 우회** — YouTube 쿠키와 자동 관리되는 PO 토큰 공급자를 사용합니다. 수동 설정이 필요 없습니다.
- 💾 **로컬 영상 캐시** — 용량 제한을 설정할 수 있어, 반복 재생 시 즉시 불러오고 대역폭을 절약합니다.
- 🖥️ **데스크톱 앱** — 대시보드, 캐시 브라우저, 다운로드 대기열, 재생 기록, 로그 뷰어를 제공합니다. 시스템 트레이로 최소화할 수 있으며 `--nogui`로 헤드리스 실행도 가능합니다.
- 🔄 **자동 업데이트** — 앱 자체와 도구(yt-dlp, FFmpeg, Deno)를 자동으로 최신 상태로 유지합니다.
- 🎮 **VRChat**과 **Resonite**, **Windows**와 **Linux**에서 작동합니다.

### 어떻게 작동하나요?

시작할 때 VRChat/Resonite의 `yt-dlp.exe`를, 백그라운드에서 실행되는 VRCVideoCacher 앱으로 영상 요청을 전달하는 작은 스텁으로 교체합니다. 앱을 종료하면 원래의 `yt-dlp.exe`가 자동으로 복원됩니다.

누락된 코덱 자동 설치: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### 위험이 있나요?

**VRC나 EAC에서?** 없습니다.

**YouTube/Google에서?** YouTube 접근을 자동화하는 이상 이론적인 위험은 항상 존재하므로, 신중을 기하려면 별도의(보조) Google 계정을 사용하세요.

다만 실제로는:
- 메인 개발자는 개발 초기부터 자신의 기본 YouTube 계정을 계속 사용해 왔지만 아무런 문제가 없었습니다.
- 이 도구와 관련된 계정 정지 신고는 지금까지 단 한 건도 없었습니다.

### 봇 확인을 우회하는 방법

YouTube의 "로봇이 아님을 확인하려면 로그인하세요" 장벽은 두 가지가 함께 작동하여 돌파합니다:

- **YouTube 쿠키** — 요청이 실제 로그인된 세션처럼 보이게 합니다.
- **PO 토큰** — YouTube가 이제 요구하는 출처 증명(proof-of-origin) 토큰입니다. 백그라운드에서 자동으로 생성·관리되므로 설정이 필요 없습니다.

PO 토큰 쪽은 VRCVideoCacher가 알아서 처리합니다. 여러분이 준비할 것은 쿠키뿐입니다:

1. 대시보드의 기본 제공 **YouTube 쿠키 설정** 마법사를 엽니다. [Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) 또는 [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)용 브라우저 확장 프로그램이 설치됩니다([자세히](https://github.com/clienthax/VRCVideoCacherBrowserExtension)).
2. VRCVideoCacher를 실행한 상태에서 로그인된 [YouTube.com](https://www.youtube.com)을 최소 한 번 방문합니다.
3. 쿠키를 받으면 확장 프로그램을 제거해도 됩니다.

> **참고:** 로그인 상태로 같은 브라우저에서 YouTube를 다시 방문하면 YouTube가 쿠키를 교체하여 VRCVideoCacher가 저장한 쿠키가 무효화됩니다.
>
> 이를 방지하려면 다음 중 하나를 하세요:
> - 이후 해당 브라우저에서 YouTube 쿠키를 삭제하거나,
> - 확장 프로그램을 설치된 채로 두거나,
> - 이 용도로만 쓰는 별도의 브라우저/계정을 사용하세요.

### 광고에 관하여 (비 Premium 계정)

SABR은 광고를 **서버 측**에서 삽입합니다. 비 Premium 계정의 경우, 클라이언트가 광고를 "시청"할 때까지 YouTube가 실제 영상을 보류하며, 영상 데이터가 전송되기 전에 짧은 강제 대기 시간이 발생합니다. VRChat에서는 영상이 재생되기 전 몇 초간의 멈춤으로 나타납니다.

- **YouTube Premium 계정**은 SABR이 광고 없이 제공되어 이러한 지연이 없습니다. 가장 쾌적한 경험입니다.
- **비 Premium 계정**은 SABR 재생 시 이 시작 지연을 겪게 됩니다.

지연이 신경 쓰인다면 설정에서 "SABR 스트리밍 우선" 옵션을 **끄세요**. 그러면 VRCVideoCacher가 먼저 일반 직접 URL을 시도하고, SABR로만 재생 가능한 영상에 대해서만 SABR로 대체하므로 대부분의 영상에서 광고 지연을 피할 수 있습니다.

비 Premium 계정에서 실제로 SABR을 스트리밍하는 동안에는 이 지연을 없앨 방법이 없습니다. 대기 시간은 VRCVideoCacher가 아니라 YouTube의 서버가 강제하는 것입니다.

### YouTube 영상이 가끔 재생되지 않는 문제 해결

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

시스템 시간을 동기화하세요: Windows 설정 → 시간 및 언어 → 날짜 및 시간 → "추가 설정"에서 "지금 동기화"를 클릭. 또한 **Windows Update**를 최신으로 유지하는 것도 중요합니다. 최신 스트림에서 사용하는 일부 오디오 코덱은 최신 Windows에서만 올바르게 디코딩됩니다.

### 제거

- VRCX를 사용 중이라면 `%AppData%\VRCX\startup`에서 시작 프로그램 바로가기 "VRCVideoCacher"를 삭제하세요.
- `%AppData%\VRCVideoCacher`에서 설정과 캐시를 삭제하세요.
- `%LocalAppData%Low\VRChat\VRChat\Tools`에서 "yt-dlp.exe"를 삭제한 뒤 VRChat을 다시 시작하거나 월드에 재입장하세요.
