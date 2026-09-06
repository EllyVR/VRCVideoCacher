<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Язык:** [English](./README.md) | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md) | [Italiano](./README_it-IT.md) | **Русский** | [简体中文](./README_zh-CN.md)

### Wiki
- [Параметры запуска](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [Параметры конфигурации CLI](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### Что такое VRCVideoCacher?

VRCVideoCacher обеспечивает стабильное воспроизведение видео с YouTube (и других сайтов) в **VRChat** и **Resonite**, а также кэширует их на локальный диск, чтобы в следующий раз они загружались мгновенно.

YouTube переходит на новый протокол потоковой передачи под названием **SABR**, который обычное извлечение ссылок через `yt-dlp` воспроизвести не может. VRCVideoCacher поддерживает SABR нативно и ретранслирует его в игру как видео с возможностью перемотки — поэтому воспроизведение продолжает работать несмотря на изменения YouTube, вплоть до **4K**, с полной полосой перемотки.

### Возможности

- 📺 **Нативно воспроизводит новые SABR-потоки YouTube** — включая видео, которые обычный yt-dlp больше не может воспроизвести, — ретранслируя их в AVPro как видео HD/4K с перемоткой.
- 🍪 **Обходит проверку «Войдите, чтобы подтвердить, что вы не робот»** с помощью ваших cookie YouTube и автоматически управляемого провайдера PO-токенов — без ручной настройки.
- 💾 **Локальный кэш видео** с настраиваемым ограничением размера, чтобы повторные воспроизведения загружались мгновенно и экономили трафик.
- 🖥️ **Настольное приложение** с панелью управления, обозревателем кэша, очередью загрузок, историей воспроизведения и просмотром логов. Сворачивается в системный трей или запускается без интерфейса с помощью `--nogui`.
- 🔄 **Самообновление** — автоматически поддерживает в актуальном состоянии само приложение и его инструменты (yt-dlp, FFmpeg, Deno).
- 🎮 Работает с **VRChat** и **Resonite**, на **Windows** и **Linux**.

### Как это работает?

При запуске приложение заменяет `yt-dlp.exe` из VRChat/Resonite небольшой заглушкой, которая перенаправляет запросы видео в приложение VRCVideoCacher, работающее в фоне. Оригинальный `yt-dlp.exe` автоматически восстанавливается при выходе из приложения.

Автоматически устанавливает недостающие кодеки: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Есть ли риски?

**Со стороны VRC или EAC?** Нет.

**Со стороны YouTube/Google?** При автоматизации доступа к YouTube всегда есть теоретический риск, поэтому, если хотите перестраховаться, используйте отдельный / дополнительный аккаунт Google.

Однако на практике:
- Основной разработчик использует свой основной аккаунт YouTube с самого начала разработки — без каких-либо проблем.
- Мы не получили ни одного сообщения о блокировке аккаунта, связанной с этим инструментом.

### Как мы обходим проверки на робота

Стену YouTube «Войдите, чтобы подтвердить, что вы не робот» удаётся преодолеть благодаря двум компонентам, работающим вместе:

- **Ваши cookie YouTube** — чтобы запросы выглядели как реальная сессия с выполненным входом.
- **PO-токен** — токен подтверждения происхождения (proof-of-origin), который теперь требует YouTube. Он генерируется и управляется автоматически в фоне; настройка не нужна.

Сторону PO-токена VRCVideoCacher берёт на себя — вам нужно предоставить только cookie:

1. Откройте встроенный мастер **настройки cookie YouTube** на панели управления. Он установит наше расширение браузера для [Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) или [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter) ([подробнее](https://github.com/clienthax/VRCVideoCacherBrowserExtension)).
2. При запущенном VRCVideoCacher хотя бы один раз зайдите на [YouTube.com](https://www.youtube.com) под своей учётной записью.
3. После получения cookie расширение можно удалить.

> **Примечание:** если вы снова зайдёте на YouTube в том же браузере, оставаясь в системе, YouTube обновит (ротирует) cookie и сделает недействительными те, что сохранил VRCVideoCacher.
>
> Чтобы этого избежать, сделайте одно из следующего:
> - Удалите cookie YouTube из этого браузера после получения, или
> - Оставьте расширение установленным, или
> - Используйте для этого отдельный браузер / аккаунт.

### О рекламе (аккаунты без Premium)

SABR вставляет рекламу **на стороне сервера**. Для аккаунтов без Premium YouTube удерживает само видео, пока клиент не «просмотрит» рекламу — приходится выждать короткую принудительную задержку, прежде чем будут отправлены какие-либо видеоданные. В VRChat это проявляется как пауза в несколько секунд перед началом воспроизведения.

- **Аккаунты YouTube Premium** получают SABR без рекламы и без такой задержки. Это самый плавный вариант.
- **Аккаунты без Premium** будут сталкиваться с этими задержками при запуске воспроизведения через SABR.

Если задержки мешают, **отключите** параметр «Предпочитать потоковую передачу SABR» в настройках. Тогда VRCVideoCacher сначала попробует обычный прямой URL и переключится на SABR только для видео, доступных исключительно через SABR, — избегая рекламной задержки для большинства видео.

Пока вы фактически ведёте потоковую передачу через SABR на аккаунте без Premium, устранить задержку невозможно: ожидание навязывается серверами YouTube, а не VRCVideoCacher.

### Исправление ситуаций, когда видео с YouTube иногда не воспроизводится

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

Синхронизируйте системное время: Параметры Windows → Время и язык → Дата и время → в разделе «Дополнительные параметры» нажмите «Синхронизировать». Важно также поддерживать **Windows Update** в актуальном состоянии — некоторые аудиокодеки, используемые в новых потоках, корректно декодируются только на обновлённой Windows.

### Удаление

- Если вы используете VRCX, удалите ярлык автозапуска «VRCVideoCacher» из `%AppData%\VRCX\startup`
- Удалите конфигурацию и кэш из `%AppData%\VRCVideoCacher`
- Удалите «yt-dlp.exe» из `%LocalAppData%Low\VRChat\VRChat\Tools` и перезапустите VRChat или перезайдите в мир.
