<div align="center">

[![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Idioma:** [English](./README.md) | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | **Português do Brasil** | [Italiano](./README_it-IT.md) | [Русский](./README_ru-RU.md) | [简体中文](./README_zh-CN.md)

### Wiki
- [Opções de inicialização](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [Opções de configuração via CLI](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### O que é o VRCVideoCacher?

O VRCVideoCacher mantém os vídeos do YouTube (e de outros sites) tocando de forma confiável no **VRChat** e no **Resonite**, e os armazena em cache no seu disco local para que carreguem instantaneamente na próxima vez.

O YouTube vem migrando para um novo protocolo de streaming chamado **SABR**, que a extração de links comum do `yt-dlp` não consegue reproduzir. O VRCVideoCacher fala SABR nativamente e o retransmite ao jogo como um vídeo navegável — então a reprodução continua funcionando apesar das mudanças do YouTube, em até **4K**, com barra de progresso completa.

### Recursos

- 📺 **Reproduz os novos streams SABR do YouTube** nativamente — incluindo vídeos que o yt-dlp comum já não consegue reproduzir — retransmitidos ao AVPro como vídeo HD/4K navegável.
- 🍪 **Contorna o "Faça login para confirmar que você não é um robô"** usando seus cookies do YouTube mais um provedor de token PO gerenciado automaticamente — sem configuração manual.
- 💾 **Cache de vídeos local** com limite de tamanho configurável, para que reproduções repetidas carreguem instantaneamente e economizem banda.
- 🖥️ **Aplicativo desktop** com painel, navegador de cache, fila de downloads, histórico de reprodução e visualizador de logs. Minimiza para a bandeja do sistema, ou rode sem interface com `--nogui`.
- 🔄 **Autoatualizável** — mantém a si mesmo e suas ferramentas (yt-dlp, FFmpeg, Deno) sempre atualizados automaticamente.
- 🎮 Funciona com **VRChat** e **Resonite**, no **Windows** e no **Linux**.

### Como funciona?

Na inicialização, ele substitui o `yt-dlp.exe` do VRChat/Resonite por um pequeno stub que encaminha as solicitações de vídeo ao aplicativo VRCVideoCacher em execução em segundo plano. O `yt-dlp.exe` original é restaurado automaticamente quando o aplicativo é encerrado.

Instala automaticamente os codecs ausentes: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Há algum risco envolvido?

**Do VRC ou do EAC?** Não.

**Do YouTube/Google?** Sempre há um risco teórico ao automatizar o acesso ao YouTube, então use uma conta Google separada/alternativa se quiser ser cauteloso.

Na prática, porém:
- O desenvolvedor principal usa sua conta principal do YouTube desde o início do desenvolvimento, sem problemas.
- Nunca recebemos um único relato de banimento de conta relacionado a esta ferramenta.

### Como contornamos as verificações de robô

A barreira "Faça login para confirmar que você não é um robô" do YouTube é vencida com duas coisas trabalhando juntas:

- **Seus cookies do YouTube** — para que as solicitações pareçam uma sessão real conectada.
- **Um token PO** — um token de comprovação de origem (proof-of-origin) que o YouTube agora exige. Ele é gerado e gerenciado automaticamente em segundo plano; nenhuma configuração é necessária.

O VRCVideoCacher cuida do lado do token PO sozinho — tudo o que você precisa fornecer são seus cookies:

1. Abra o assistente de **Configuração de Cookies do YouTube** embutido no painel. Ele instala nossa extensão de navegador para [Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) ou [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter) ([mais informações](https://github.com/clienthax/VRCVideoCacherBrowserExtension)).
2. Com o VRCVideoCacher em execução, visite o [YouTube.com](https://www.youtube.com) conectado pelo menos uma vez.
3. Depois que ele obtiver seus cookies, você pode desinstalar a extensão.

> **Observação:** se você navegar no YouTube novamente no mesmo navegador enquanto ainda estiver conectado, o YouTube rotaciona seus cookies e invalida os que o VRCVideoCacher armazenou.
>
> Para evitar isso, faça uma das seguintes opções:
> - Exclua seus cookies do YouTube daquele navegador depois, ou
> - Deixe a extensão instalada, ou
> - Use um navegador/conta separado só para isso.

### Uma observação sobre anúncios (contas não Premium)

O SABR injeta anúncios **do lado do servidor**. Para contas não Premium, o YouTube retém o vídeo real até que o cliente "assista" ao anúncio — ele precisa aguardar um breve atraso forçado antes que qualquer dado de vídeo seja enviado. No VRChat, isso aparece como uma pausa de alguns segundos antes de o vídeo começar a tocar.

- **Contas YouTube Premium** recebem SABR sem anúncios, sem esse atraso. Essa é a experiência mais fluida.
- **Contas não Premium** verão esses atrasos de início na reprodução via SABR.

Se os atrasos incomodarem, **desative** a opção "Preferir streaming SABR" nas Configurações. O VRCVideoCacher então tentará primeiro a URL direta normal e só recorrerá ao SABR para vídeos que sejam exclusivamente SABR — evitando o atraso de anúncios na maioria dos vídeos.

Não há como remover o atraso enquanto se transmite SABR de fato em uma conta não Premium: a espera é imposta pelos servidores do YouTube, não pelo VRCVideoCacher.

### Corrigir vídeos do YouTube que às vezes não reproduzem

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

Sincronize o horário do sistema: Configurações do Windows → Hora e idioma → Data e hora → em "Configurações adicionais", clique em "Sincronizar agora". Manter o **Windows Update** em dia também importa — alguns codecs de áudio usados por streams mais recentes só decodificam corretamente em um Windows atualizado.

### Desinstalando

- Se você usa o VRCX, exclua o atalho de inicialização "VRCVideoCacher" de `%AppData%\VRCX\startup`
- Exclua a configuração e o cache de `%AppData%\VRCVideoCacher`
- Exclua o "yt-dlp.exe" de `%LocalAppData%Low\VRChat\VRChat\Tools` e reinicie o VRChat ou entre novamente no mundo.
