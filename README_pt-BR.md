<div align="center">

![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki)
[![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960)
[![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest)
[![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Language:** **English** | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md)

### Wiki
- [Opções de inicialização](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [Preferências de configuração Cli](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### O que é VRCVideoCacher?

VRCVideoCacher é uma ferramenta usada para armazenar vídeos do VRChat em cachê para seu disco local e/ou corrigir vídeos do YouTube que não carregam.

### Como funciona?

O aplicativo substitui o yt-dlp.exe do VRChat com o nosso próprio yt-dlp provisório, ele é substituido ao iniciar nosso aplicativo e é restaurado ao encerrá-lo.

Instalar automaticamente codecs não encontrados: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Há algum risco envolvido ao usar o aplicativo?

Pela parte do VRChat e EAC? Não.

Pela parte do YouTube/Google? Talvez, nós recomendamos fortemente que use uma conta alternativa do Google, se possível.

### Como circumvir a detecção do Bot do YouTube

Para resolver o problema de vídeos do YouTube que não carregam, você precisará instalar nossa extensão do Chrome [daqui](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) ou do Firefox [daqui](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter), mais informações [aqui](https://github.com/clienthax/VRCVideoCacherBrowserExtension). Visite [YouTube.com](https://www.youtube.com) enquanto logado(a), no mínimo uma vez enquanto o VRCVideoCacher estiver executando. 

Após o VRCVideoCacher obter seus cookies, você pode desinstalar a extensão com total segurança. Esteja ciente que caso visite o YouTube novamente com o mesmo navegador enquanto a conta ainda estiver logada, o YouTube irá atualizar seus cookies e invalidando os cookies armazenados no VRCVideoCacher. 

Para evitar isso, recomendamos que você delete os cookies de seu navegador após o VRCVideoCacher tenha os obtido. Você pode manter a extensão instalada caso estiver usando sua conta do YouTube principal, ou até mesmo usar um navegador separado para deixar as coisas mais simples.

### Corrigindo Fix YouTube videos sometimes failing to play

> Falha no carregamento. Arquivo não encontrado, codec não compatível, resolução do vídeo muito alta ou recursos do sistema insuficientes.

Sincronize o horário do sistema, abra as Configurações do Windows → Hora e idioma → Data e hora, em "Configurações adicionais" clique em "Sincronizar agora".

### Desintalar

- Se você usa VRCX, delete o atalho de inicialização "VRCVideoCacher" de `%AppData%\VRCX\startup`
- Deletar configurações e cachê de `%AppData%\VRCVideoCacher`
- Deletar "yt-dlp.exe" de `%AppData%\..\LocalLow\VRChat\VRChat\Tools` e reiniciar o VRChat ou reentrar na instância.
