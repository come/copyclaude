# CopyClaude — Fenêtre flottante de capture de sélections terminal

Petite app Windows en arrière-plan : chaque sélection de texte dans le terminal
est ajoutée en bas d'une fenêtre flottante éditable, **sans voler le focus** et
**sans toucher au presse-papier** (la sélection reste collable avec Ctrl+V).

## Pré-requis

1. **Windows Terminal — `copyOnSelect`** : c'est le déclencheur de la capture.
   Dans le `settings.json` de Windows Terminal (Paramètres → *Ouvrir le fichier
   JSON*), ajouter :

   ```json
   "copyOnSelect": true
   ```

2. **.NET 8 SDK** pour le build.

## Lancer

```powershell
dotnet run
```

## Utilisation

- Sélectionner du texte dans Windows Terminal → le texte apparaît en bas de la
  fenêtre flottante, chaque ligne préfixée `> `, suivie d'une ligne vide où le
  caret est posé.
- Cliquer dans la fenêtre permet d'y taper librement (notes sous un bloc, etc.).
  Les captures suivantes s'ajoutent toujours **en fin**, rien n'est écrasé.
- La fenêtre se déplace par son en-tête, se redimensionne par le coin bas-droit.
- `Effacer` vide le contenu ; `✕` quitte l'app.

## Filtre des applications capturées

Seules les copies faites pendant qu'un process de l'allowlist est au premier
plan sont capturées. Défauts : `WindowsTerminal`, `pwsh`, `powershell`,
`conhost`, `Code`.

Pour personnaliser : créer un fichier `allowlist.txt` à côté de l'exe, un nom
de process par ligne (sans `.exe`, `#` pour commenter). S'il est présent et non
vide, il remplace les défauts.

## Garanties

- **Presse-papier en lecture seule** : l'app ne fait jamais de `SetText`. Ce
  que vous copiez reste exactement ce que Ctrl+V collera ailleurs.
- **Pas de vol de focus** : la fenêtre est `Topmost` avec `WS_EX_NOACTIVATE` ;
  les ajouts automatiques ne désactivent jamais le terminal.
- **Topmost seulement devant le terminal** : quand une autre app passe au
  premier plan, la fenêtre perd `Topmost` et se laisse recouvrir ; elle
  redevient au-dessus dès le retour sur le terminal (ou un clic sur elle).
- **Event-based** : écoute via `AddClipboardFormatListener`, aucun polling.
- **Debounce 300 ms** : un drag de sélection ne produit qu'un seul bloc.
