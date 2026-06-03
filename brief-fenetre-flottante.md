# Brief — Fenêtre flottante de capture de sélections terminal

## Objectif
Construire une petite application Windows qui transforme chaque sélection de
texte dans le terminal en une entrée ajoutée à une **fenêtre flottante
persistante**, dans laquelle je peux aussi taper mon propre texte. La sélection
doit **rester disponible dans le presse-papier** pour un coller normal.

## Contexte technique
- OS : Windows 10/11, shell PowerShell, hôte Windows Terminal.
- Stack imposée : .NET 8, C#, WPF pour la fenêtre flottante (WinForms autorisé
  uniquement pour une icône tray optionnelle).
- L'app tourne en arrière-plan ; la fenêtre flottante est toujours au premier
  plan mais ne doit **jamais voler le focus** lors d'un ajout automatique.

## Comportement attendu
1. Quand je sélectionne du texte dans le terminal, le texte sélectionné
   s'ajoute en bas du contenu de la fenêtre flottante, comme un nouveau bloc.
2. La fenêtre flottante est éditable : je peux cliquer dedans et taper du texte
   librement, notamment juste en dessous d'un bloc capturé.
3. Si je refais une sélection dans le terminal, le nouveau texte s'ajoute
   **après** le contenu existant (append en fin), sans écraser ce que j'ai
   déjà tapé.
4. La ligne sélectionnée doit **AUSSI rester dans le presse-papier système** :
   je dois pouvoir faire Ctrl+V ailleurs et coller cette ligne. Autrement dit,
   l'app *lit* le presse-papier mais ne l'écrase jamais.

## Mécanisme de détection (important)
- Windows Terminal n'expose pas d'API de sélection. Le déclencheur repose sur
  le réglage `"copyOnSelect": true` de Windows Terminal : sélectionner met le
  texte dans le presse-papier.
- L'app écoute les changements de presse-papier via une fenêtre *message-only*
  et `AddClipboardFormatListener` (message `WM_CLIPBOARDUPDATE = 0x031D`).
  Event-based, pas de polling.
- Comme `copyOnSelect` place déjà la sélection dans le presse-papier et que
  l'app ne fait que LIRE, l'exigence « la ligne reste dans le presse-papier »
  est satisfaite gratuitement. **Ne jamais écrire dans le presse-papier.**

## Contraintes techniques clés
- **Debounce** : `copyOnSelect` émet un event à chaque tick pendant le drag.
  Attendre ~300 ms de stabilité avant d'ajouter le bloc, pour ne capturer que
  la sélection finale et pas un bloc par tick.
- **Filtre fenêtre active** : ne capturer que si la fenêtre au premier plan est
  le terminal. Vérifier au moment de l'event via `GetForegroundWindow` +
  `GetWindowThreadProcessId` + nom de process, avec une allowlist configurable
  (`WindowsTerminal`, `pwsh`, `powershell`, `conhost`, `Code`). Sinon mes
  copier dans d'autres apps pollueraient la fenêtre.
- **Fenêtre non-activante** : les ajouts automatiques ne doivent jamais voler
  le focus du terminal. Fenêtre WPF avec `Topmost = true`,
  `ShowActivated = false`, et style étendu `WS_EX_NOACTIVATE (0x08000000)`
  appliqué via `HwndSource` sur `SourceInitialized`. Un clic manuel dedans
  peut, lui, l'activer pour que je tape.
- **Lecture presse-papier robuste** : le presse-papier peut être verrouillé une
  fraction de seconde → retenter quelques fois avec un petit délai, attraper
  `COMException`.
- **Pas d'auto-déclenchement** : l'app n'écrivant jamais dans le presse-papier,
  il n'y a pas de boucle ; ne pas en créer une.

## UI de la fenêtre flottante
- Fenêtre compacte, redimensionnable, repositionnable (drag par une zone
  d'en-tête), toujours au premier plan.
- Contenu = une zone de texte multiligne éditable unique, qui accumule les
  blocs capturés et mon texte libre.
- Format d'un bloc capturé : un préfixe discret pour distinguer la capture de
  mes notes (ex. ligne capturée préfixée `> `), suivi d'une ligne vide où le
  caret se place pour que je tape juste en dessous.
- Après un ajout automatique, placer le caret en fin de buffer pour que je
  puisse enchaîner ma note sans cliquer.

## Critères d'acceptation
1. `copyOnSelect` activé : sélectionner une ligne dans Windows Terminal
   l'ajoute en bas de la fenêtre flottante dans la seconde, sans que le
   terminal perde le focus.
2. Après cette sélection, Ctrl+V dans le Bloc-notes colle exactement la ligne
   sélectionnée.
3. Taper du texte dans la fenêtre juste sous un bloc capturé fonctionne et
   n'est pas écrasé par une sélection suivante (qui s'ajoute en fin).
4. Copier du texte dans une autre application (ex. navigateur) n'ajoute RIEN à
   la fenêtre flottante.
5. Drag rapide pour sélectionner : un seul bloc ajouté grâce au debounce.

## Pré-requis utilisateur (à documenter dans le README)
- Activer `"copyOnSelect": true` dans le `settings.json` de Windows Terminal.
- .NET 8 SDK pour le build (`dotnet run`).

## Nice-to-have (optionnel, seulement si le cœur marche)
- Icône tray : activer/désactiver la capture, vider la fenêtre, ouvrir/sauver
  le contenu dans un `.md`.
- Raccourci « flush » : copier tout le contenu de la fenêtre dans le
  presse-papier et vider, pour le coller en bloc dans le prompt.
- Persistance du buffer sur disque entre redémarrages.

## Méthode
Si un choix d'implémentation est ambigu, propose 2 options et leurs compromis
plutôt que de trancher seul. Commence par un plan court avant de coder.
