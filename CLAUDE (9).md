# CLAUDE.md — Fenêtre flottante de capture de sélections terminal

Contexte permanent pour ce projet. Lis-le avant chaque tâche.

## Projet
Petite app Windows en arrière-plan : chaque sélection de texte dans le terminal
est ajoutée à une fenêtre flottante éditable, tout en restant dans le
presse-papier. Spec détaillée dans `brief-fenetre-flottante.md`.

## Stack imposée
- `net8.0-windows`, C#, sortie `WinExe`.
- UI en **WPF**. WinForms autorisé uniquement pour une éventuelle icône tray.
- Outil personnel : reste léger. Pas de MVVM lourd, pas de framework, pas de
  dépendance NuGet sans me demander d'abord et justifier.
- Privilégier du code programmatique simple ; peu de fichiers.

## Commandes
- Build : `dotnet build`
- Lancer : `dotnet run`
- Cible : Windows 10/11 uniquement (P/Invoke user32). Ne pas écrire de chemin de
  code « cross-platform » : c'est inutile ici.

## Conventions de code
- `Nullable` et `ImplicitUsings` activés.
- Tous les `DllImport` regroupés dans une classe statique `Native` ; chaque
  constante Win32 commentée avec sa valeur et son rôle.
- `Main` annoté `[STAThread]` (requis WPF).
- Commentaires, libellés UI et messages de commit en **français**.
- Commits petits et atomiques.

## Pièges Win32 à ne jamais oublier
- **Ne pas voler le focus** : la fenêtre flottante s'update sans s'activer.
  `Topmost = true`, `ShowActivated = false`, et style étendu
  `WS_EX_NOACTIVATE (0x08000000)` appliqué via `HwndSource` sur
  `SourceInitialized`. C'est le piège n°1.
- **Écoute presse-papier event-based** : fenêtre *message-only* +
  `AddClipboardFormatListener`, message `WM_CLIPBOARDUPDATE = 0x031D`.
  Jamais de polling. Appeler `RemoveClipboardFormatListener` à la fermeture.
- **Read-only sur le presse-papier** : l'app LIT seulement. Aucun `SetText` en
  tâche de fond — c'est ce qui garde la ligne collable et évite toute boucle
  de re-déclenchement. (Seul un flush explicite déclenché par moi peut écrire.)
- **Debounce ~300 ms** : `copyOnSelect` émet un event à chaque tick de drag.
  Attendre la stabilité avant d'ajouter un bloc.
- **Filtre fenêtre active** : capturer seulement si le foreground est le
  terminal. `GetForegroundWindow` + `GetWindowThreadProcessId` + nom de process,
  allowlist configurable (`WindowsTerminal`, `pwsh`, `powershell`, `conhost`,
  `Code`). Vérifier AU MOMENT de l'event, avant tout affichage.
- **Lecture clipboard robuste** : peut être verrouillé brièvement → retry court
  (≈5×, ~30 ms) en attrapant `COMException`.
- **Cleanup à la sortie** : retirer le listener, `tray.Visible = false`,
  `Application.Shutdown`.

## À ne pas faire
- Pas de polling du presse-papier.
- Ne pas écrire dans le presse-papier sur un ajout automatique.
- Ne pas activer / focus la fenêtre flottante lors d'un ajout automatique.
- Ne pas sur-architecturer : c'est un outil perso d'une poignée de fichiers.

## Definition of done
Les 5 critères d'acceptation du brief passent, en particulier :
- sélection terminal → bloc ajouté sans perte de focus terminal ;
- Ctrl+V ailleurs colle bien la dernière ligne sélectionnée ;
- copier dans une autre app n'ajoute rien à la fenêtre.

## Méthode de travail
- Plan court avant de coder.
- Si un choix d'implémentation est ambigu, proposer 2 options + compromis
  plutôt que trancher seul.
