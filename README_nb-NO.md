<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Språk:** [English](./README.md) | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md) | [Italiano](./README_it-IT.md) | [Русский](./README_ru-RU.md) | [简体中文](./README_zh-CN.md) | [Français](./README_fr-FR.md) | **Norsk (bokmål)**

### Wiki
- [Oppstartsalternativer](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [CLI-konfigurasjonsalternativer](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### Hva er VRCVideoCacher?

VRCVideoCacher sørger for at YouTube-videoer (og andre) spilles av pålitelig i **VRChat** og **Resonite**, og hurtigbufrer dem til din lokale disk slik at de lastes inn umiddelbart neste gang.

YouTube har gått over til en ny strømmeprotokoll som kalles **SABR**, som vanlig `yt-dlp`-lenkeuttrekking ikke kan spille av. VRCVideoCacher støtter SABR naturlig og strømmer det på nytt til spillet som en video det kan spoles i — slik at avspillingen fortsetter å fungere til tross for YouTubes endringer, i opptil **4K**, med en fullverdig skrubbelinje.

### Funksjoner

- 📺 **Spiller av YouTubes nye SABR-strømmer** direkte — inkludert videoer som vanlig yt-dlp ikke lenger kan spille av — strømmet på nytt til AVPro som søkbar HD/4K-video.
- 🍪 **Omgår «Logg på for å bekrefte at du ikke er en robot»** ved hjelp av YouTube-informasjonskapslene dine og en selvstyrt PO-token-tilbyder — ingen manuelt oppsett kreves.
- 💾 **Lokal videohurtigbuffer** med en konfigurerbar størrelsesgrense, slik at gjentatte avspillinger lastes inn umiddelbart og sparer båndbredde.
- 🖥️ **Skrivebordsapp** med oversikt, hurtigbufferleser, nedlastingskø, avspillingshistorikk og loggvisning. Kan minimeres til systemkurven eller kjøres uten grafisk grensesnitt med `--nogui`.
- 🔄 **Selvoppdaterende** — holder seg selv og verktøyene sine (yt-dlp, FFmpeg, Deno) oppdatert automatisk.
- 🎮 Fungerer med **VRChat** og **Resonite**, på **Windows** og **Linux**.

### Hvordan fungerer det?

Ved oppstart erstatter den VRChats/Resonites `yt-dlp.exe` med en liten stubb som videresender videoforespørsler til VRCVideoCacher-appen som kjører i bakgrunnen. Den opprinnelige `yt-dlp.exe`-filen gjenopprettes automatisk når appen avsluttes.

Installerer manglende kodeker automatisk: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Er det knyttet noen risikoer til dette?

**Fra VRC eller EAC?** Nei.

**Fra YouTube/Google?** Det er alltid en teoretisk risiko ved automatisert tilgang til YouTube, så bruk en separat/alternativ Google-konto hvis du vil være forsiktig.

I praksis har det imidlertid vist seg at:
- THovedutvikleren har brukt sin primære YouTube-konto helt siden starten av utviklingen, uten problemer.
- Vi har aldri hatt en eneste rapport om kontoutestengelse knyttet til dette verktøyet.

### Slik fikser vi botsjekker

YouTubes «Logg på for å bekrefte at du ikke er en robot»-sperre omgås ved at to ting fungerer sammen:

- **YouTube-informasjonskapslene dine** — slik at forespørsler ser ut som en ekte innlogget økt.
- **Et PO token** — et opprinnelsesbevis (proof-of-origin) som YouTube nå krever. Dette genereres og administreres automatisk for deg i bakgrunnen; ingen oppsett er nødvendig.

VRCVideoCacher håndterer PO-token-delen på egen hånd — alt du trenger å oppgi, er informasjonskapslene dine:

1. Åpne den innebygde veiviseren for **YouTube-informasjonskapseloppsett** på oversikten. Den installerer nettleserutvidelsen vår for [Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) eller [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter) ([mer informasjon](https://github.com/clienthax/VRCVideoCacherBrowserExtension)).
2. Mens VRCVideoCacher kjører, besøker du [YouTube.com](https://www.youtube.com) mens du er logget på, minst én gang.
3. Når den har mottatt informasjonskapslene dine, kan du avinstallere utvidelsen.

> **Merk:** Hvis du surfer på YouTube igjen i samme nettleser mens du fremdeles er logget på, roterer YouTube informasjonskapslene dine og gjør dem som VRCVideoCacher lagret ugyldige.
> 
> For å unngå dette kan du enten:
> - Slette YouTube-informasjonskapslene fra den nettleseren etterpå, eller
> - La utvidelsen være installert, eller
> - Bruke en separat nettleser / konto kun til dette formålet.

### En merknad om reklame (for ikke-Premium-kontoer)

SABR setter inn reklame på **serversiden**. For ikke-Premium-kontoer holder YouTube selve videoen tilbake til klienten «ser» reklamen – den må vente ut en kort, pålagt forsinkelse før videodata sendes. I VRChat viser dette seg som en pause på noen sekunder før en video begynner i spilles av.

- **YouTube Premium-kontoer** mottar SABR uten reklame og uten slik forsinkelse. Dette gir den jevneste opplevelsen.
- **Ikke-Premium-kontoer** vil oppleve disse oppstartsforsinkelsene ved SABR-avspilling.

Hvis forsinkelsene plager deg, kan du slå **av** alternativet «Foretrekk SABR-strømming» i innstillingene. VRCVideoCacher vil da prøve den vanlige direkte-URL-en først, og bare falle tilbake til SABR for videoer som krever SABR – noe som unngår reklameforsinkelsen på de fleste videoer.

Det er ingen måte å fjerne forsinkelsen på når man faktisk strømmer SABR med en ikke-Premium-konto: ventetiden håndteres av YouTubes servere, ikke av VRCVideoCacher.

### Slik fikser du at YouTube-videoer av og til ikke spilles av

> Lasting mislyktes. Filen ble ikke funnet, kodeken støttes ikke, videopoppløsningen er for høy eller utilstrekkelige systemressurser.

Synkroniser systemtiden din: Windows-innstillinger → Tid og språk → Dato og klokkeslett → under «Flere innstillinger» klikker du på «Synkroniser nå». Det er også viktig å holde **Windows Update** oppdatert – noen lydkodeker som brukes av nyere strømmer, dekodes bare riktig på en oppdatert Windows-versjon.

### Avinstallering

- Hvis du bruker VRCX, sletter du oppstartssnarveien «VRCVideoCacher» fra `%AppData%\VRCX\startup`
- Slett konfigurasjon og hurtigbuffer fra `%AppData%\VRCVideoCacher`
- Slett «yt-dlp.exe» fra `%LocalAppData%Low\VRChat\VRChat\Tools` og start VRChat på nytt eller bli med i verdenen igjen.
