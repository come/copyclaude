# CopyClaude — Linux (X11)

Port Linux natif de [CopyClaude](../README.md), en **Rust + GTK4**. Fenêtre
flottante qui capture chaque sélection de texte d'un terminal, **sans voler le
focus** et **sans toucher au presse-papier** : elle lit la sélection **PRIMARY**
(le copy-on-select natif de X11), donc rien à configurer côté terminal et le
CLIPBOARD (Ctrl+V) reste intact.

> Réimplémentation indépendante de la version Windows (.NET/WPF) — aucun code
> partagé. Voir le plan : `../../.claude/plans/` (ou le dossier de plans local).

## État

Toutes les phases sont écrites et **la compilation + le packaging sont validés en
CI** (job `linux` + `package-linux`). Le comportement à l'exécution reste à
vérifier sur une vraie session **Xorg** (impossible en CI headless).

- [x] **Phase 1** — fenêtre flottante + hints X11 (always-on-top, no-focus-steal
      via `_NET_WM_STATE_ABOVE` + `WM_HINTS.input=false` + `_NET_WM_USER_TIME=0`).
- [x] **Phase 2** — capture de la sélection PRIMARY via XFixes (thread dédié, debounce 300 ms).
- [x] **Phase 3** — multi-terminal : suivi `_NET_ACTIVE_WINDOW`, un buffer par
      fenêtre, barre d'onglets, sweep des terminaux fermés, titres vivants.
- [x] **Phase 4** — Auto-focus (focus via `_NET_ACTIVE_WINDOW`), thème sombre CSS.
- [x] **Phase 5** — packaging `.deb` / `.rpm` / `.tar.gz` + CI (attaché aux releases sur tag `v*`).
- [ ] À faire : passe de test runtime sous Xorg ; AppImage (différé) ; support Wayland (wlroots).

## Build

Nécessite Rust (stable) et les libs de dev GTK4 + X11 :

```bash
# Debian/Ubuntu
sudo apt install libgtk-4-dev libx11-dev libxfixes-dev

cargo build --release
cargo run
```

> Doit tourner sous **X11** (session « Xorg »). Wayland est hors périmètre v1.

## Packaging

```bash
cargo install cargo-deb cargo-generate-rpm
convert "../icon.ico[0]" -resize 256x256 packaging/icon.png  # icône (ImageMagick)
cargo build --release
cargo deb --no-build   # → target/debian/copyclaude_*.deb
cargo generate-rpm     # → target/generate-rpm/copyclaude-*.rpm
```

Dépendances runtime déclarées : `libgtk-4-1`, `libx11-6`, `libxfixes3`. La CI
(`package-linux`) construit `.deb`/`.rpm`/`.tar.gz` à chaque push et les attache
à la release GitHub sur tag `v*`. L'icône PNG est générée depuis `../icon.ico`.
