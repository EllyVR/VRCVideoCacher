<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Nyelv:** [English](./README.md) | [日本語](./README_ja-JP.md) | **Magyar** | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md) | [Italiano](./README_it-IT.md) | [Русский](./README_ru-RU.md) | [简体中文](./README_zh-CN.md)

### Wiki
- [Indítási beállítások](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [CLI konfigurációs beállítások](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### Mi az a VRCVideoCacher?

A VRCVideoCacher megbízhatóan lejátszhatóvá teszi a YouTube (és más) videókat a **VRChat** és a **Resonite** alatt, valamint a helyi lemezre gyorsítótárazza őket, hogy legközelebb azonnal betöltődjenek.

A YouTube egy új, **SABR** nevű streamelési protokollra tér át, amelyet a hagyományos `yt-dlp` link-kinyerés nem tud lejátszani. A VRCVideoCacher natívan beszéli a SABR-t, és tekerhető videóként streameli tovább a játéknak – így a lejátszás a YouTube változásai ellenére is működik, akár **4K** felbontásban, teljes tekerősávval.

### Funkciók

- 📺 **Lejátssza a YouTube új SABR streamjeit** natívan – beleértve azokat a videókat is, amelyeket a sima yt-dlp már nem tud – tekerhető HD/4K videóként az AVPro számára továbbstreamelve.
- 🍪 **Megkerüli a „Jelentkezz be, hogy igazold, nem vagy robot” ellenőrzést** a YouTube-sütijeid és egy automatikusan kezelt PO-token-szolgáltató segítségével – kézi beállítás nélkül.
- 💾 **Helyi videó-gyorsítótár** állítható méretkorláttal, így az ismételt lejátszások azonnal betöltődnek, és sávszélességet takarítanak meg.
- 🖥️ **Asztali alkalmazás** irányítópulttal, gyorsítótár-böngészővel, letöltési sorral, lejátszási előzményekkel és naplónézővel. A tálcára kicsinyíthető, vagy fejléc nélkül futtatható a `--nogui` kapcsolóval.
- 🔄 **Önfrissítő** – automatikusan naprakészen tartja önmagát és eszközeit (yt-dlp, FFmpeg, Deno).
- 🎮 Működik **VRChat** és **Resonite** alatt, **Windows** és **Linux** rendszeren.

### Hogyan működik?

Indításkor lecseréli a VRChat/Resonite `yt-dlp.exe` fájlját egy kis stubra, amely a videókéréseket a háttérben futó VRCVideoCacher alkalmazásnak továbbítja. Az eredeti `yt-dlp.exe` automatikusan visszaáll az alkalmazás bezárásakor.

Automatikusan telepíti a hiányzó kodekeket: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Vannak kockázatok?

**A VRC vagy az EAC felől?** Nincsenek.

**A YouTube/Google felől?** A YouTube-hozzáférés automatizálásakor mindig van elméleti kockázat, ezért ha óvatos szeretnél lenni, használj külön / másodlagos Google-fiókot.

A gyakorlatban azonban:
- A fő fejlesztő a fejlesztés kezdete óta a saját elsődleges YouTube-fiókját használja, minden probléma nélkül.
- Soha nem érkezett egyetlen bejelentés sem az eszközzel kapcsolatos fióktiltásról.

### Hogyan kerüljük meg a robotellenőrzést?

A YouTube „Jelentkezz be, hogy igazold, nem vagy robot” falát két dolog együttműködésével törjük át:

- **A YouTube-sütijeid** – hogy a kérések valódi, bejelentkezett munkamenetnek tűnjenek.
- **Egy PO-token** – egy proof-of-origin (eredetigazoló) token, amelyet a YouTube ma már megkövetel. Ezt a háttérben automatikusan előállítjuk és kezeljük; nincs szükség beállításra.

A PO-token oldalt a VRCVideoCacher magától kezeli – neked csak a sütiket kell megadnod:

1. Nyisd meg a beépített **YouTube süti-beállító** varázslót az irányítópulton. Ez telepíti a böngészőbővítményünket [Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) vagy [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter) alá ([további információ](https://github.com/clienthax/VRCVideoCacherBrowserExtension)).
2. A VRCVideoCacher futása mellett látogasd meg legalább egyszer a [YouTube.com](https://www.youtube.com) oldalt bejelentkezve.
3. Miután megkapta a sütiket, a bővítményt eltávolíthatod.

> **Megjegyzés:** ha ugyanabban a böngészőben, bejelentkezve újra megnyitod a YouTube-ot, a YouTube lecseréli a sütiket, és érvényteleníti a VRCVideoCacher által tárolt sütiket.
>
> Ennek elkerülése érdekében tedd az alábbiak egyikét:
> - Töröld utána a YouTube-sütiket a böngészőből, vagy
> - Hagyd telepítve a bővítményt, vagy
> - Használj erre a célra külön böngészőt / fiókot.

### Néhány szó a hirdetésekről (nem Premium fiókok)

A SABR **szerveroldalon** szúrja be a hirdetéseket. Nem Premium fiókok esetén a YouTube visszatartja a tényleges videót, amíg a kliens „megnézi” a hirdetést – ki kell várnia egy rövid, kényszerített késleltetést, mielőtt bármilyen videóadat megérkezne. A VRChatben ez néhány másodperces szünetként jelentkezik a videó indulása előtt.

- **A YouTube Premium fiókok** SABR-t reklámmentesen kapnak, ilyen késleltetés nélkül. Ez a legzökkenőmentesebb élmény.
- **A nem Premium fiókok** ezt az indítási késleltetést tapasztalják a SABR-lejátszásnál.

Ha a késleltetés zavar, kapcsold **ki** a „SABR streamelés előnyben részesítése” opciót a Beállításokban. A VRCVideoCacher ekkor először a szokásos közvetlen URL-t próbálja, és csak a kizárólag SABR-ban elérhető videóknál vált SABR-re – így a videók többségénél elkerülhető a hirdetéskésleltetés.

Amíg egy nem Premium fiókon ténylegesen SABR-t streamelsz, nincs mód a késleltetés eltávolítására: a várakozást a YouTube szerverei kényszerítik ki, nem a VRCVideoCacher.

### YouTube-videók időnkénti lejátszási hibájának javítása

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

Szinkronizáld a rendszeridőt: Windows Beállítások → Idő és nyelv → Dátum és idő → a „További beállítások” alatt kattints a „Szinkronizálás most” gombra. Fontos a **Windows Update** naprakészen tartása is – az újabb streamek egyes hangkodekjei csak naprakész Windowson dekódolódnak helyesen.

### Eltávolítás

- Ha használsz VRCX-et, töröld a „VRCVideoCacher” indítási parancsikont innen: `%AppData%\VRCX\startup`
- Töröld a konfigurációt és a gyorsítótárat innen: `%AppData%\VRCVideoCacher`
- Töröld a „yt-dlp.exe” fájlt innen: `%LocalAppData%Low\VRChat\VRChat\Tools`, majd indítsd újra a VRChatet, vagy lépj be újra a világba.
