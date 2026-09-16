<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

*Ce document est la version française de [../TOOLS.md](../TOOLS.md). / This document is the French version.*

# Référence des outils

Référence développeur pour les outils intégrés de Heimdall, les fournisseurs
d'outils externes et l'infrastructure d'hébergement des outils.

## Vue d'ensemble

Heimdall embarque 58 outils intégrés enregistrés par `ToolRegistry`. Le jeton
brut `Entry(` apparaît plus souvent dans `ToolRegistry.cs` parce qu'il
correspond aussi au record interne `ToolEntry`, au chemin d'enregistrement
dynamique des outils externes et à la méthode d'aide privée. Le nombre effectif
d'outils intégrés correspond à l'ensemble des entrées de registre déclarées dans
le constructeur :

- Réseau : 17
- Sécurité : 15
- Encodage : 6
- Système : 14
- Outils natifs externes : 6

Les outils issus des fournisseurs externes sont détectés à l'exécution et
ajoutés au registre après les scans de démarrage. Ils ne font pas partie du
décompte des 58 outils intégrés.

## Catalogue des outils intégrés

Les noms ci-dessous proviennent de `locales/en.json` ; les identifiants sont les
clés de registre stables utilisées par la palette de commandes, les onglets, les
panneaux divisés et la recherche d'outils.

### Réseau

| ID | Nom |
|---|---|
| `PING` | Ping Monitor |
| `DNS` | DNS Lookup |
| `CERT` | Certificate Inspector |
| `PORTSCAN` | Port Scanner |
| `SUBNET` | Subnet Calculator |
| `IPCONV` | IP Address Converter |
| `HTTP` | HTTP Status Codes |
| `WHOIS` | WHOIS Lookup |
| `HTTPHEADERS` | HTTP Header Analyzer |
| `BANNER` | Banner Grabber |
| `TCPTRACE` | Traceroute |
| `SNMPWALK` | SNMP Walker |
| `ARPMON` | ARP Monitor |
| `FWTEST` | Firewall Tester |
| `NETMAP` | Network Cartography |
| `NETCALC` | Network Calculator |
| `TCPPING` | TCP Ping |

### Sécurité

| ID | Nom |
|---|---|
| `HASH` | Hash Generator |
| `HMAC` | HMAC Generator |
| `PASSWORD` | Password Generator |
| `SSHKEY` | SSH Key Generator |
| `CERTGEN` | Certificate Generator |
| `JWT` | JWT Parser |
| `TOTP` | TOTP Generator |
| `PWDAUDIT` | Password Auditor |
| `SSHAUDIT` | SSH Key Auditor |
| `TLSAUDIT` | TLS Auditor |
| `DNSSEC` | DNS Security Checker |
| `SMBENUM` | SMB Enumerator |
| `DEFAULTCREDS` | Default Credential Scanner |
| `CVELOOKUP` | CVE Lookup |
| `SECNUMCLOUD` | SecNumCloud Audit |

### Encodage

| ID | Nom |
|---|---|
| `BASE64` | Base64 Encoder / Decoder |
| `URLENC` | URL Encoder / Decoder |
| `JSON` | JSON Formatter |
| `REGEX` | Regex Tester |
| `DIFF` | Text Diff |
| `TEXTCASE` | Text Case Converter |

### Système

| ID | Nom |
|---|---|
| `CHMOD` | Chmod Calculator |
| `DATETIME` | DateTime Converter |
| `UUID` | UUID Generator |
| `ULID` | ULID Generator |
| `CRONTAB` | Crontab Builder |
| `LOGVIEW` | Log Viewer |
| `HOSTS` | Hosts File Editor |
| `SSHCONFIG` | SSH Config Generator |
| `CRONJOB` | Cron Job Manager |
| `SERVICES` | Service Status Dashboard |
| `NOTES` | Notes |
| `DIAGRAM` | Diagram Editor |
| `CMDLIB` | Command Library |
| `PRIVLAUNCH` | Privilege Launcher |

### Natifs externes

| ID | Nom |
|---|---|
| `WOL` | Wake-on-LAN |
| `OPENPORTS` | Open Ports |
| `NETIF` | Network Interfaces |
| `ROUTES` | Route Table |
| `DNSBATCH` | DNS Batch Resolver |
| `WIFI` | WiFi Networks |

## Infrastructure des outils

`ToolRegistry` est la source de vérité unique pour les métadonnées des outils
intégrés et pour leur fabrique de création. Ajouter un outil intégré revient à
ajouter une entrée de registre et l'implémentation `IToolView` correspondante.

`ToolDescriptor` porte les métadonnées stables :

- `Id` : clé de recherche courte, par exemple `PING`
- `Category` : `Network`, `Security`, `Encoding`, `System` ou `External`
- `CategoryLabelKey` : clé i18n de l'en-tête de catégorie
- `LabelKey` : clé i18n du nom affiché
- `LabelWithArgKey` : clé i18n optionnelle pour les suggestions de la palette de
  commandes comportant un argument cible
- `CommandPrefixes` : alias pour la palette de commandes
- `IsNetworkTool` : indique si un lancement autonome doit demander une cible
- `IconResourceKey` : clé de ressource de géométrie XAML
- `DescriptionKey` : clé de description explicite optionnelle ; lorsqu'elle est
  nulle, l'interface applique la convention `ToolDesc{Id}`

`IToolView` est le contrat d'exécution des vues d'outil. Il expose :

- `Initialize(ToolContext?, LocalizationManager?)`
- `CanClose()`
- `Dispose()`

`ToolsTabPopulationService` construit à la fois l'onglet Tools complet et
l'arborescence Tools de la barre latérale à partir du registre. Il gère les
favoris, les récents, le filtrage par recherche, le regroupement par catégorie,
les cartes d'outil et l'héritage de contexte.

`SidebarToolCategoryViewModel` et `SidebarToolItemViewModel` alimentent
l'arborescence Tools de la barre latérale. La recherche s'appuie sur un texte
recherchable précalculé en minuscules (`name + aliases`), afin que le filtrage
n'alloue pas de mémoire à répétition.

Les icônes utilisent des ressources de géométrie telles que
`Geo.Tool.PortScanner` et des ressources de pinceau par catégorie telles que
`ToolNetworkBrush`. `ToolRegistry.GetGeometryKey()` et
`ToolRegistry.GetCategoryBrushKey()` existent pour les convertisseurs XAML qui
n'ont pas accès à l'injection de dépendances.

Le routage par passerelle se joue au niveau de la vue, et pas uniquement au
niveau du registre. `ToolGatewayConnector` crée un client SSH pour l'exécution
de commandes distantes à travers une passerelle SSH et exige une clé d'hôte de
passerelle épinglée avant de se connecter. Les vues qui exposent actuellement un
sélecteur de route `CmbRouteVia` sont les suivantes :

- `BannerGrabberView`
- `CertInspectorView`
- `DefaultCredentialView`
- `DnsLookupView`
- `DnsSecurityView`
- `FirewallTesterView`
- `HttpHeaderAnalyzerView`
- `NetworkCartographyView`
- `PingToolView`
- `PortScannerView`
- `SecNumCloudAuditView`
- `SmbEnumeratorView`
- `SnmpWalkerView`
- `TcpTracerouteView`
- `TlsAuditView`
- `WhoisLookupView`

Les outils qui lancent un shell distant à travers une passerelle doivent
conserver des délais d'expiration par sonde et une sélection explicite du shell.
Pour les tests `/dev/tcp`, utilisez `timeout ... bash -c ...` afin que les ports
filtrés ne laissent pas de processus shell distants en cours d'exécution une
fois le canal de commande SSH tué.

## Fournisseurs d'outils externes

Les outils tiers détectés à l'exécution s'appuient sur `IExternalToolProvider`,
`ExternalToolProviderService`, `ExternalToolInfo` et `ExternalToolWrapperView`.

Implémentations de fournisseurs actuelles :

- `SysinternalsToolProvider` : recherche les outils Sysinternals tels que
  PsExec, PsInfo, PsList, PsService, PsPing, Tcpvcon, Autorunsc, Sigcheck,
  AccessChk, Handle, ListDLLs, Disk Usage et Whois.
- `NirSoftToolProvider` : recherche les outils NirSoft tels que PingInfoView,
  CurrPorts, NetworkLatencyView, WakeMeOnLan, FastResolver, CountryTraceRoute,
  DNSDataView, NetResView, NetworkInterfacesView, WifiInfoView,
  Wireless Network Watcher, FullEventLogView, TaskSchedulerView, USBDeview,
  BlueScreenView et ProduKey.
- `NanaRunToolProvider` : recherche les outils du projet NanaRun exploitables en
  ligne de commande, aujourd'hui représentés par MinSudo et SynthRdp.

`ExternalToolProviderService.ScanAll()` agrège les fournisseurs et applique les
chemins de recherche configurés par l'utilisateur dans `AppSettings`
(`SysinternalsPath`, `NirSoftPath`, `NanaRunPath`). Les outils détectés sont
enregistrés dans `ToolRegistry` sous forme d'entrées `ToolDescriptor`
dynamiques, avec des identifiants au format :

```text
EXT:PROVIDER:TOOLID
```

`ExternalToolWrapperView` lance l'exécutable détecté, applique les
substitutions `{Host}` et `{Port}` issues de `ToolContext`, capture stdout et
stderr, puis affiche la sortie sous forme de texte ou de données structurées
selon `OutputFormat`. Les outils marqués `RequiresElevation` affichent un
avertissement d'élévation en amont.

Règle de licence : les binaires tiers sont uniquement détectés et encapsulés. Ne
redistribuez pas les outils NirSoft, Sysinternals, NanaRun ou d'autres outils
tiers dans les paquets Heimdall, sauf si leur licence l'autorise explicitement.

## Moteur d'audit SecNumCloud

`SecNumCloudAuditEngine` orchestre l'outil `SECNUMCLOUD`. Il est aligné sur
quatre chapitres orientés SecNumCloud :

- Réseau
- Cryptographie
- Contrôle d'accès
- Exploitation

Le moteur exécute actuellement 15 contrôles couvrant la découverte, l'exposition
réseau, TLS, SSH, SMB, SNMP, les en-têtes HTTP, les enregistrements DNS, les
identifiants par défaut, la correspondance de bannières avec les CVE et la
posture d'exploitation.

La progression est exposée par des évènements :

- `PhaseProgress`
- `StatusChanged`
- `CheckCompleted`

Exports :

- `HtmlReportGenerator` produit un rapport HTML autonome.
- `CsvEvidenceExporter` exporte les lignes de preuve d'audit.
- `DrawIoExporter`, dans `Heimdall.Core.Discovery`, produit des diagrammes de
  topologie Draw.io à partir des données de découverte et de cartographie.

La localisation est injectée dans `SecNumCloudAuditEngine` via un délégué
`Func<string, string>`. Gardez les chaînes de sortie de l'audit localisables et
ne codez pas en dur de texte destiné aux utilisateurs dans le moteur.

## Éditeur de diagrammes

L'outil `DIAGRAM` embarque l'éditeur draw.io dans une surface WebView2.
L'éditeur est vendoré sous `src/Heimdall.App/Assets/drawio/` et n'atteint jamais
le réseau : il est chargé avec `offline=1&stealth=1`, et le fichier `index.html`
vendoré porte une Content-Security-Policy qui maintient toutes les requêtes sur
l'hôte virtuel local `heimdall-drawio.local`.

### Deux entrées

- **Depuis la liste des outils**, avec une toile vide.
- **Depuis la cartographie réseau**, par le bouton Éditer le diagramme. Le scan
  est converti en document draw.io et remis à l'éditeur comme contenu non
  enregistré. Aucun fichier ne lui correspond, donc le premier enregistrement
  demande où le placer.

### Barre d'outils

La barre de menus et la barre d'outils de draw.io sont masquées. Dans une iframe
WebView2 elles paraissent interactives sans répondre de manière fiable, donc
Heimdall fournit la surface de commande : Nouveau, Ouvrir, Enregistrer,
Enregistrer sous, Exporter PNG, Exporter SVG, Ligne, Format, Annuler, Rétablir,
zoom arrière, 100%, zoom avant, Dupliquer, Supprimer. La bibliothèque de formes,
la toile et le panneau de format sont ceux de draw.io et fonctionnent
normalement. Un clic droit sur la toile ouvre un menu contextuel Heimdall qui
déclenche les mêmes actions draw.io.

### Enregistrement

L'en-tête affiche le nom du fichier courant, suivi de `(modifié)` tant que
l'éditeur porte des modifications absentes du disque. Enregistrer écrit dans le
fichier courant ; Enregistrer sous en demande toujours un nouveau. Ctrl+S dans
l'éditeur fait la meme chose que le bouton Enregistrer. Fermer l'onglet, ou
remplacer le document par Nouveau ou Ouvrir, demande quoi faire du travail non
enregistré.

L'état de l'éditeur vit dans `DiagramDocumentState` : il porte le chemin du
fichier, le contenu que l'éditeur a signalé en dernier, et le contenu lu depuis
le disque ou écrit sur le disque en dernier. Tout le reste lit cet
enregistrement, et c'est pourquoi un enregistrement, un Enregistrer sous et
l'invite de travail non enregistré ne peuvent pas se contredire.

### Langue, theme et WebView2

La page hôte est ouverte sous la forme
`heimdall-host.html?lang=<en|fr>&theme=<dark|light>` et transmet les deux à
draw.io, afin que l'éditeur suive Heimdall plutôt que la langue du système
d'exploitation. Les raccourcis navigateur sont désactivés : F5 rechargerait la
page hôte et perdrait le diagramme. Sans runtime WebView2, l'outil affiche un
message de repli au lieu d'un éditeur.

Les ressources pèsent environ 44 Mo pour 2300 fichiers et sont exclues des
builds Debug (`Heimdall.App.csproj`) ; l'outil signale alors qu'il n'est
disponible qu'en build Release.

### Mettre à jour l'éditeur vendoré

Exécuter `scripts/Vendor-DrawIo.ps1` sur une copie amont de `src/main/webapp`.
Le script copie le sous-ensemble livré, réapplique les deux modifications
Heimdall (le hook `heimdallDrawioApp` dans `js/bootstrap.js`, la
Content-Security-Policy dans `index.html`) et réécrit les lignes de version de
`VENDORED.md` et des deux fichiers `THIRD-PARTY-NOTICES`.
`DiagramEditorGuardTests` vérifie ensuite que ces manifestes correspondent à la
version que le bundle declare. Faire un test de fumée complet de l'outil avant
de commiter : les gardes lisent le source, pas le comportement.

### Export Draw.io sans l'éditeur

`DrawIoExporter`, dans `Heimdall.Core.Discovery`, transforme un
`NetworkScanSnapshot` en XML draw.io : un couloir par rôle classifié, plus un
pour les hôtes sans port ouvert. Il prend un localiseur `Func<string, string>` :
les noms de rôle sont les identités produites par le classifieur et restent en
l'état, mais les en-têtes de couloir et les lignes de noeud passent par le
catalogue de locales. La couleur du couloir et le style de ses noeuds viennent
d'un unique enregistrement `RolePalette`, ce qui leur interdit de diverger.

## Command Library et TwinShell

L'outil `CMDLIB` intègre la bibliothèque de commandes TwinShell au sein de
Heimdall.

Composants principaux :

- `TwinShellBootstrapper` : enregistre dans le conteneur d'injection de
  dépendances de Heimdall la persistance TwinShell, les dépôts, les services, le
  pont de localisation, le pont de paramètres et les services de synchronisation
  Git.
- `CommandLibraryView` : vue d'outil WPF.
- `CommandLibraryViewModel` et ses classes partielles : filtrage, actions de
  commande, historique, favoris, génération et état de l'interface.
- Projets TwinShell : `TwinShell.Core`, `TwinShell.Persistence` et
  `TwinShell.Infrastructure`.

La persistance repose sur SQLite via `TwinShellDbContext`. Le chemin de la base
de données partagée se trouve dans les données d'application locales de
l'utilisateur, dans un répertoire `TwinShell`.

Les données de départ se trouvent dans `data/seed/actions/`. Les fichiers dont
le nom commence par `_` sont ignorés ; le jeu de données actuel contient 514
fichiers JSON d'actions.

Principaux parcours utilisateur :

- recherche approximative avec filtres par plateforme, catégorie et niveau de
  risque
- génération de commandes paramétrées
- favoris et historique des commandes
- import/export au format JSON compatible TwinShell
- synchronisation Git via `IGitSyncService`
- envoi vers le terminal via `ToolContext.SendCommandAction`

Les actions système issues des données de départ sont protégées : les actions
d'édition et de suppression sont masquées pour les commandes qui n'ont pas été
créées par l'utilisateur, et la fusion à l'import ignore les actions système.

## Où trouver quoi

- `src/Heimdall.App/Services/ToolRegistry.cs` : registre des outils intégrés et
  enregistrement dynamique des outils externes.
- `src/Heimdall.Core/Models/ToolDescriptor.cs` : record de métadonnées d'outil.
- `src/Heimdall.Core/Models/IToolView.cs` : contrat des vues d'outil.
- `src/Heimdall.App/Services/ToolsTabPopulationService.cs` : peuplement de
  l'onglet Tools complet et de la section Tools de la barre latérale.
- `src/Heimdall.App/ViewModels/SidebarToolsViewModels.cs` : view-models de
  l'arborescence Tools de la barre latérale.
- `src/Heimdall.App/Services/ToolGatewayConnector.cs` : utilitaire de routage
  par passerelle SSH pour les outils.
- `src/Heimdall.Core/Configuration/ExternalToolProvider.cs` : modèle et
  interface des fournisseurs externes.
- `src/Heimdall.Core/Configuration/SysinternalsToolProvider.cs` : fournisseur
  Sysinternals.
- `src/Heimdall.Core/Configuration/NirSoftToolProvider.cs` : fournisseur
  NirSoft.
- `src/Heimdall.Core/Configuration/NanaRunToolProvider.cs` : fournisseur
  NanaRun.
- `src/Heimdall.App/Services/ExternalToolProviderService.cs` : agrégation des
  fournisseurs et orchestration des scans.
- `src/Heimdall.App/Views/Tools/ExternalToolWrapperView.xaml.cs` : hôte
  générique pour les outils externes.
- `src/Heimdall.App/Services/SecNumCloudAuditEngine.cs` : orchestration de
  l'audit SecNumCloud.
- `src/Heimdall.App/Services/HtmlReportGenerator.cs` : rapport d'audit HTML.
- `src/Heimdall.App/Services/CsvEvidenceExporter.cs` : export CSV des preuves.
- `src/Heimdall.Core/Discovery/DrawIoExporter.cs` : export de topologie Draw.io.
- `src/Heimdall.App/Services/TwinShellBootstrapper.cs` : injection de
  dépendances TwinShell et initialisation des données de départ.
- `src/Heimdall.App/Views/Tools/CommandLibraryView.xaml.cs` : vue de l'outil
  Command Library.
- `src/Heimdall.App/ViewModels/CommandLibraryViewModel*.cs` : tranches de
  view-model de Command Library.
- `data/seed/actions/` : actions de départ de la bibliothèque de commandes.
