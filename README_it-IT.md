<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Lingua:** [English](./README.md) | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md) | **Italiano** | [Русский](./README_ru-RU.md) | [简体中文](./README_zh-CN.md)

### Wiki
- [Opzioni di avvio](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [Opzioni di configurazione da CLI](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### Cos'è VRCVideoCacher?

VRCVideoCacher mantiene i video di YouTube (e di altri siti) riproducibili in modo affidabile in **VRChat** e **Resonite**, e li memorizza nella cache sul disco locale in modo che si carichino istantaneamente la volta successiva.

YouTube sta passando a un nuovo protocollo di streaming chiamato **SABR**, che la normale estrazione dei link di `yt-dlp` non è in grado di riprodurre. VRCVideoCacher parla SABR in modo nativo e lo ritrasmette al gioco come video navigabile — così la riproduzione continua a funzionare nonostante i cambiamenti di YouTube, fino a **4K**, con una barra di avanzamento completa.

### Funzionalità

- 📺 **Riproduce i nuovi stream SABR di YouTube** in modo nativo — inclusi i video che il normale yt-dlp non riesce più a riprodurre — ritrasmessi ad AVPro come video HD/4K navigabile.
- 🍪 **Aggira il "Accedi per confermare che non sei un robot"** usando i tuoi cookie di YouTube più un provider di token PO gestito automaticamente — senza configurazione manuale.
- 💾 **Cache video locale** con limite di dimensione configurabile, così le riproduzioni ripetute si caricano istantaneamente e risparmiano banda.
- 🖥️ **App desktop** con dashboard, browser della cache, coda di download, cronologia di riproduzione e visualizzatore dei log. Si riduce a icona nell'area di notifica, oppure può essere eseguita senza interfaccia con `--nogui`.
- 🔄 **Autoaggiornante** — mantiene sé stessa e i suoi strumenti (yt-dlp, FFmpeg, Deno) sempre aggiornati automaticamente.
- 🎮 Funziona con **VRChat** e **Resonite**, su **Windows** e **Linux**.

### Come funziona?

All'avvio sostituisce il `yt-dlp.exe` di VRChat/Resonite con un piccolo stub che inoltra le richieste video all'app VRCVideoCacher in esecuzione in background. Il `yt-dlp.exe` originale viene ripristinato automaticamente alla chiusura dell'app.

Installa automaticamente i codec mancanti: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Ci sono rischi?

**Da VRC o EAC?** No.

**Da YouTube/Google?** C'è sempre un rischio teorico quando si automatizza l'accesso a YouTube, quindi usa un account Google separato/alternativo se vuoi essere prudente.

In pratica, però:
- Lo sviluppatore principale usa il proprio account YouTube principale fin dall'inizio dello sviluppo, senza problemi.
- Non abbiamo mai ricevuto una singola segnalazione di ban di account legata a questo strumento.

### Come aggiriamo i controlli anti-bot

Il muro "Accedi per confermare che non sei un robot" di YouTube viene superato grazie a due elementi che lavorano insieme:

- **I tuoi cookie di YouTube** — così le richieste sembrano una vera sessione con accesso effettuato.
- **Un token PO** — un token di prova d'origine (proof-of-origin) che YouTube ora richiede. Viene generato e gestito automaticamente in background; nessuna configurazione necessaria.

VRCVideoCacher si occupa da solo del lato token PO — tutto ciò che devi fornire sono i tuoi cookie:

1. Apri la procedura guidata **Configurazione Cookie di YouTube** integrata nella dashboard. Installa la nostra estensione del browser per [Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) o [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter) ([maggiori informazioni](https://github.com/clienthax/VRCVideoCacherBrowserExtension)).
2. Con VRCVideoCacher in esecuzione, visita [YouTube.com](https://www.youtube.com) con l'accesso effettuato almeno una volta.
3. Una volta ottenuti i cookie, puoi disinstallare l'estensione.

> **Nota:** se navighi di nuovo su YouTube nello stesso browser mentre sei ancora connesso, YouTube ruota i cookie e invalida quelli memorizzati da VRCVideoCacher.
>
> Per evitarlo, esegui una di queste operazioni:
> - Elimina i cookie di YouTube da quel browser in seguito, oppure
> - Lascia l'estensione installata, oppure
> - Usa un browser/account separato solo per questo.

### Una nota sulla pubblicità (account non Premium)

SABR inserisce la pubblicità **lato server**. Per gli account non Premium, YouTube trattiene il video vero e proprio finché il client non "guarda" l'annuncio — deve attendere un breve ritardo forzato prima che venga inviato qualsiasi dato video. In VRChat questo si manifesta come una pausa di alcuni secondi prima che il video inizi.

- **Gli account YouTube Premium** ricevono SABR senza pubblicità, senza tale ritardo. È l'esperienza più fluida.
- **Gli account non Premium** noteranno questi ritardi all'avvio nella riproduzione SABR.

Se i ritardi ti danno fastidio, **disattiva** l'opzione "Preferisci streaming SABR" nelle Impostazioni. VRCVideoCacher proverà allora prima il normale URL diretto e ricorrerà a SABR solo per i video disponibili esclusivamente in SABR — evitando il ritardo pubblicitario nella maggior parte dei video.

Non c'è modo di eliminare il ritardo mentre si effettua effettivamente lo streaming SABR su un account non Premium: l'attesa è imposta dai server di YouTube, non da VRCVideoCacher.

### Correggere i video di YouTube che a volte non vengono riprodotti

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

Sincronizza l'ora di sistema: Impostazioni di Windows → Data/ora e lingua → Data e ora → sotto "Impostazioni aggiuntive" fai clic su "Sincronizza ora". È importante anche mantenere aggiornato **Windows Update** — alcuni codec audio usati dagli stream più recenti si decodificano correttamente solo su un Windows aggiornato.

### Disinstallazione

- Se usi VRCX, elimina il collegamento di avvio "VRCVideoCacher" da `%AppData%\VRCX\startup`
- Elimina configurazione e cache da `%AppData%\VRCVideoCacher`
- Elimina "yt-dlp.exe" da `%LocalAppData%Low\VRChat\VRChat\Tools` e riavvia VRChat o rientra nel mondo.
