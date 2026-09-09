<div align="center">

[![Bannière d’en-tête](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)](https://store.steampowered.com/app/4296960)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki) [![Télécharger sur Steam](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960) [![Télécharger sur GitHub](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest) [![Serveur Discord](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

<hr>
</div>

**Langue :** [English](./README.md) | **Français** | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md) | [Italiano](./README_it-IT.md) | [Русский](./README_ru-RU.md) | [简体中文](./README_zh-CN.md)

### Wiki

- [Options de lancement](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [Options de configuration en ligne de commande](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### Qu’est-ce que VRCVideoCacher ?

VRCVideoCacher assure une lecture fiable des vidéos YouTube (et d’autres plateformes) dans **VRChat** et **Resonite**, et les met en cache sur votre disque local pour qu’elles se chargent instantanément lors des prochaines lectures.

YouTube migre vers un nouveau protocole de diffusion en continu appelé **SABR**, que la simple extraction de liens avec `yt-dlp` ne permet pas de lire. VRCVideoCacher prend en charge SABR nativement et retransmet le flux au jeu sous forme de vidéo dans laquelle vous pouvez vous déplacer librement. La lecture continue ainsi de fonctionner malgré les changements de YouTube, jusqu’en **4K**, avec une barre de navigation complète.

### Fonctionnalités

- 📺 **Lit nativement les nouveaux flux SABR de YouTube**, y compris les vidéos que yt-dlp seul ne peut plus lire, et les retransmet à AVPro en HD/4K avec la possibilité de se déplacer dans la vidéo.
- 🍪 **Résout le blocage « Connectez-vous pour confirmer que vous n’êtes pas un robot »** grâce à vos cookies YouTube et à un fournisseur de jetons PO géré automatiquement, sans configuration manuelle.
- 💾 **Cache vidéo local** avec une limite de taille configurable, pour charger instantanément les vidéos déjà lues et réduire la consommation de bande passante.
- 🖥️ **Application de bureau** avec un tableau de bord, un explorateur du cache, une file d’attente des téléchargements, un historique de lecture et une visionneuse de journaux. Elle peut se réduire dans la zone de notification ou fonctionner sans interface graphique avec `--nogui`.
- 🔄 **Mise à jour automatique** de l’application et de ses outils (yt-dlp, FFmpeg, Deno).
- 🎮 Compatible avec **VRChat** et **Resonite**, sous **Windows** et **Linux**.

### Comment ça fonctionne ?

Au démarrage, l’application remplace le fichier `yt-dlp.exe` de VRChat/Resonite par un petit programme relais qui transmet les requêtes vidéo à VRCVideoCacher, exécuté en arrière-plan. Le fichier `yt-dlp.exe` d’origine est automatiquement restauré à la fermeture de l’application.

Installation automatique des codecs manquants : [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Y a-t-il des risques ?

**Du côté de VRC ou d’EAC ?** Non.

**Du côté de YouTube/Google ?** Il existe toujours un risque théorique lorsque l’on automatise l’accès à YouTube. Utilisez donc un compte Google distinct ou secondaire si vous souhaitez prendre des précautions.

Dans la pratique, toutefois :

- La personne qui développe principalement cet outil utilise son compte YouTube principal depuis le tout début du développement, sans aucun problème.
- Nous n’avons jamais reçu le moindre signalement de bannissement de compte lié à cet outil.

### Comment nous résolvons les vérifications anti-robots

Le blocage de YouTube « Connectez-vous pour confirmer que vous n’êtes pas un robot » est résolu grâce à deux éléments complémentaires :

- **Vos cookies YouTube**, pour que les requêtes ressemblent à celles d’une véritable session connectée.
- **Un jeton PO**, un jeton de preuve d’origine désormais exigé par YouTube. Il est généré et géré automatiquement pour vous en arrière-plan, sans configuration nécessaire.

VRCVideoCacher s’occupe lui-même du jeton PO. Il vous suffit de fournir vos cookies :

1. Ouvrez l’assistant intégré de **configuration des cookies YouTube** depuis le tableau de bord. Il installe notre extension de navigateur pour [Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) ou [Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter) ([plus d’informations](https://github.com/clienthax/VRCVideoCacherBrowserExtension)).
2. Pendant que VRCVideoCacher est en cours d’exécution, rendez-vous au moins une fois sur [YouTube.com](https://www.youtube.com) en étant connecté à votre compte.
3. Une fois vos cookies reçus par l’application, vous pouvez désinstaller l’extension.

> **Remarque :** si vous naviguez à nouveau sur YouTube dans le même navigateur en restant connecté à votre compte, YouTube renouvelle vos cookies et invalide ceux enregistrés par VRCVideoCacher.
>
> Pour éviter cela, vous pouvez :
>
> - Supprimer ensuite vos cookies YouTube de ce navigateur ;
> - Garder l’extension installée ;
> - Utiliser un navigateur ou un compte distinct uniquement à cet effet.

### À propos des publicités (comptes sans Premium)

SABR insère les publicités **côté serveur**. Pour les comptes sans Premium, YouTube retient la vidéo tant que le client n’a pas « regardé » la publicité : un court délai d’attente est imposé avant l’envoi des données vidéo. Dans VRChat, cela se traduit par une pause de quelques secondes avant le début de la lecture.

- **Les comptes YouTube Premium** reçoivent les flux SABR sans publicité et sans ce délai. C’est l’expérience la plus fluide.
- **Les comptes sans Premium** subissent ces délais au démarrage de la lecture via SABR.

Si ces délais vous gênent, **désactivez** l’option « Privilégier la diffusion en continu via SABR » dans les paramètres. VRCVideoCacher essaiera alors d’abord l’URL directe habituelle et ne se repliera sur SABR que pour les vidéos disponibles uniquement via ce protocole. Cela évite le délai publicitaire pour la plupart des vidéos.

Il n’est pas possible de supprimer ce délai lors de la diffusion via SABR avec un compte sans Premium : l’attente est imposée par les serveurs de YouTube, et non par VRCVideoCacher.

### Résoudre les échecs de lecture occasionnels des vidéos YouTube

> Échec du chargement. Fichier introuvable, codec non pris en charge, résolution vidéo trop élevée ou ressources système insuffisantes.

Synchronisez l’heure de votre système : Paramètres Windows → Heure et langue → Date et heure → sous « Paramètres supplémentaires », cliquez sur « Synchroniser maintenant ». Il est également important d’installer les mises à jour de **Windows Update** : certains codecs audio utilisés par les flux récents ne sont correctement décodés que sur un système Windows à jour.

### Désinstallation

- Si vous utilisez VRCX, supprimez le raccourci de démarrage « VRCVideoCacher » dans `%AppData%\VRCX\startup`.
- Supprimez la configuration et le cache dans `%AppData%\VRCVideoCacher`.
- Supprimez « yt-dlp.exe » dans `%LocalAppData%Low\VRChat\VRChat\Tools`, puis redémarrez VRChat ou rejoignez à nouveau le monde.
