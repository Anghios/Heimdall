<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Contribuer à Heimdall

*Également disponible en anglais : [CONTRIBUTING.md](CONTRIBUTING.md).*

Les contributions sont les bienvenues. Cette page est la porte d'entrée : ce à quoi ressemble la
relecture, et la poignée de conventions du projet qu'on ne devine pas de l'extérieur. Les
mécaniques de compilation, de test et de nommage vivent dans
[docs/fr/DEVELOPMENT.md](docs/fr/DEVELOPMENT.md), et cette page ne les répète pas.

## Licence et paternité

Heimdall est sous licence Apache 2.0. En vertu de la section 5 de cette licence, tout ce que vous
soumettez délibérément pour inclusion est couvert par les mêmes termes : il n'y a pas d'accord de
contributeur à signer ni quoi que ce soit à envoyer par courrier.

Vos commits restent les vôtres. Les pull requests sont fusionnées par un commit de merge et non
écrasées, si bien que votre paternité survit dans `git log` et dans le graphe des contributeurs du
dépôt, et les contributeurs sont nommés dans l'entrée de journal où leur travail apparaît.

Les nouveaux fichiers portent l'en-tête Apache 2.0 standard. Mettez votre propre nom dans la ligne
de copyright d'un fichier que vous avez écrit.

## Avant d'ouvrir une pull request

```powershell
powershell -File Build.ps1 -Mode Debug
```

Cette seule commande fait office de barrière : elle lance les tests, vérifie le formatage avec
`dotnet format --verify-no-changes`, compile et publie. Les avertissements sont des erreurs dans
tout le projet, donc une compilation qui affiche un avertissement ne passe pas. Si elle atteint
`[5/5] Published`, la CI sera d'accord avec vous.

`Build.ps1` réécrit les métadonnées de version dans `src/Heimdall.App/Heimdall.App.csproj` avant
de compiler. Restaurez ce fichier avant de committer si vous n'aviez pas l'intention de changer la
version.

## Ce que regarde la relecture

**Un changement est attendu avec un test qui échoue sans lui.** Pas un test qui exerce le code, un
test qui aurait attrapé le défaut. La façon habituelle de le montrer est de casser le correctif
exprès, de regarder le nouveau test passer au rouge, puis de remettre en état : plusieurs fichiers
de test de ce dépôt décrivent le mutant qui les a prouvés, et une pull request qui dit lequel elle
a utilisé est relue plus vite.

**Le sujet de commit dit ce qui a changé, pas ce qui a été tapé.** Les sujets sont des phrases en
minuscules qui parlent de comportement, avec un type et une portée :

```
fix(ssh): the host key prompt accepted a key it had refused
feat(tree): a folder can be dropped between two others
docs: the interface is trilingual, and the key count is measured
```

Le corps explique pourquoi, et ce qui a été mesuré. Une branche se nomme de la même manière,
`<type>/<description-courte-en-kebab>`, d'après le changement et jamais d'après un outil ou une
personne. Le vocabulaire complet est dans [docs/fr/DEVELOPMENT.md](docs/fr/DEVELOPMENT.md).

**Aucun texte destiné à l'utilisateur n'est écrit dans le code.** Chaque phrase qu'un utilisateur
peut lire appartient aux catalogues de traduction sous `locales/`, atteinte par
`{loc:Translate Key}` en XAML ou par le localiseur en C#. Les clés sont en CamelCase et nommées
par contexte : `ErrorPlinkNotFound`, `BtnConnect`.

**Tous les catalogues portent les mêmes clés.** `LocaleCatalogueParityTests` l'impose dans les deux
sens et sur les paramètres de format : une clé ajoutée à l'anglais seul parvient aux autres langues
sous la forme du nom de la clé, à la place de la phrase.

## Les caractères refusés

C'est la convention qui surprend le plus une première contribution, parce qu'un éditeur de texte
produit les caractères refusés sans qu'on le lui demande.

| Refusé | Écrire à la place |
|---|---|
| tiret cadratin, tiret demi-cadratin, trait d'union Unicode, signe moins | `-` |
| guillemets courbes, guillemets bas, guillemets français ou espagnols | `"` |
| apostrophe courbe | `'` |
| points de suspension en un seul caractère | `...` |
| espaces insécable, fine et de largeur nulle, marque d'ordre des octets | une espace ordinaire, ou rien |
| ligatures oe et ae | `oe`, `ae` |

Un caractère ASCII ordinaire dit la même chose et survit à un terminal Windows, à un diff, à une
page de code console et à un journal de CI. La règle vaut dans le code, dans les commentaires, dans
la documentation, dans les messages de commit et dans les catalogues de traduction, quelle que soit
la langue.

**Les accents ne sont pas de la typographie.** Chaque lettre accentuée du français et de l'espagnol
est la bienvenue, tout comme les flèches, les caractères de tracé de boîte, les cases à cocher et
les emoji là où ils portent du sens. Ce qui est refusé, c'est le substitut typographique d'un
caractère que l'ASCII possède déjà.

`SourceTypographyGuardTests` et `DocumentationTypographyGuardTests` l'imposent, si bien qu'un écart
fait échouer la compilation plutôt que d'atteindre une release.

## La documentation existe en deux langues

Les documents publics sont versionnés et en miroir : l'anglais à son emplacement habituel, le
français à côté (`README.md` et `README.fr.md`, `docs/X.md` et `docs/fr/X.md`). Un changement sur
l'un n'est pas terminé tant que l'autre ne dit pas la même chose. Le journal des versions et les
notes de version publiées font exception et restent en anglais seul.

Si vous n'êtes à l'aise que dans une des deux langues, écrivez ce côté-là et dites-le dans la pull
request. Un miroir manquant représente peu de travail pour le mainteneur ; un miroir faux, non.

## Ajouter une langue

L'interface existe en anglais, en français et en espagnol. Une quatrième langue touche les endroits
suivants, et les gardes vous diront si vous en oubliez un :

- [ ] `locales/<code>.json`, portant exactement les clés de `locales/en.json`, dans le même ordre
      et à la même mise en page ligne à ligne, pour que les catalogues se lisent côte à côte
- [ ] la copie `Content` dans `src/Heimdall.App/Heimdall.App.csproj`
- [ ] un `ComboBoxItem` dans la liste des langues de `src/Heimdall.App/MainWindow.xaml`, portant le
      code dans `Tag`
- [ ] `ValidLocales` dans `src/Heimdall.Core/Configuration/SchemaValidator.cs`
- [ ] `SupportedCultures` sur le pont de localisation dans
      `src/Heimdall.App/Services/TwinShellBootstrapper.cs`
- [ ] la liste d'autorisation du garde mojibake dans
      `tests/Heimdall.Core.Tests/LocaleMojibakeGuardTests.cs`, si la langue emploie des lettres
      qu'aucune langue livrée n'emploie
- [ ] `THIRD-PARTY-NOTICES.<code>.md`, et les liens croisés en tête de ses fichiers frères

Deux choses qu'une nouvelle langue n'atteint pas aujourd'hui, et aucune des deux n'est bloquante :
l'éditeur draw.io embarqué ne livre que des ressources anglaises et françaises, et les listes de
mots du générateur de phrases de passe sont anglaises et françaises.

## Signaler plutôt que corriger

Un rapport de bogue qui nomme la version, dit ce qui était attendu et ce qui s'est produit, et
porte les lignes de journal pertinentes vaut mieux qu'un correctif qui devine. Pour tout ce qui a
une dimension de sécurité, lisez d'abord [SECURITY.fr.md](SECURITY.fr.md) et n'ouvrez pas de ticket
public.

## Comment se passe la relecture

Attendez-vous à ce que le mainteneur pousse des commits de suite par-dessus une contribution plutôt
que de la renvoyer à travers plusieurs tours de relecture. Quand cela arrive, les commits sont
distincts des vôtres, chacun dit ce qu'il change et pourquoi, et la pull request explique le
raisonnement. Si vous préférez faire ces changements vous-même, dites-le et la relecture vous
attendra.
