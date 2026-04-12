<div align="center">

![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki)
[![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960)
[![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest)
[![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Language:** [English](./README.md) | [日本語](./README_ja-JP.md) | **Magyar** | [한국어](./README_ko-KR.md)

### Micsoda a VRCVideoCacher?

VRCVideoCacher egy segédprogram, amely a VRChat videókat a helyi merevlemezre menti, és kijavítja a YouTube videók betöltési hibáit.

### Hogyan működik?

It replaces VRChats yt-dlp.exe with our own stub yt-dlp, this gets replaced on application startup and is restored on exit.

Hiányzó kódekek automatikus telepítése: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Van-e bármilyen kockázat?

A VRC-től vagy az EAC-től? Nincs.

A YouTube/Google-tól? Lehetséges, ezért erősen javasoljuk, hogy ha lehetséges, használj alternatív Google-fiókot.

### Hogyan lehet megkerülni a YouTube botok észlelését?

YouTube videók betöltési problémájának megoldásához telepítened kell Chrome bővítményünket [innen](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) vagy Firefox bővítményünket [innen](https://addons. mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter), további információk [itt](https://github.com/clienthax/VRCVideoCacherBrowserExtension). Látogass el a [YouTube.com](https://www.youtube.com) oldalra bejelentkezve, legalább egyszer, miközben a VRCVideoCacher fut, miután a VRCVideoCacher megszerezte a sütiket, biztonságosan eltávolíthatod a bővítményt, de ne feled, hogy ha ugyanazzal a böngészővel újra felkeresd a YouTube-ot, miközben a fiók még be van jelentkezve, a YouTube frissíti a sütiket, érvénytelenítve a VRCVideoCacher-ben tárolt sütiket. Ennek elkerülése érdekében azt javaslom, hogy töröld a YouTube sütieidet a böngésződből, miután a VRCVideoCacher megszerezte őket, vagy ha a fő YouTube-fiókod használd, hagyd telepítve a kiterjesztést, vagy akár használj egy teljesen különálló böngészőt a fő böngésződtől, hogy egyszerűbb legyen a dolgok kezelése.

### Javítsd meg a YouTube videók lejátszásának néha bekövetkező meghibásodását

> Betöltés sikertelen. A fájl nem található, a kódek nem támogatott, a videó felbontása túl magas vagy a rendszer erőforrásai nem elegendőek.

Szinkronizáld a rendszeridőt, nyisd meg a Windows beállításaid -> Idő és nyelv -> Dátum és idő, az "Egyéb beállítások" alatt kattintson a "Szinkronizálás most" gombra.

### Eltávolítás

- Ha rendelkezel VRCX-szel, töröld a "VRCVideoCacher" indítóparancsot a `%AppData%\VRCX\startup` mappából.
- Töröld a konfigurációt és a gyorsítótárat a `%AppData%\VRCVideoCacher` mappából.
- Töröld az "yt-dlp.exe" fájlt a `%AppData%\..\LocalLow\VRChat\VRChat\Tools` mappából, majd indítsd újra a VRChat alkalmazást, vagy csatlakozz újra a világhoz.
