# CopyClaude — Linux (X11)

Port Linux natif de [CopyClaude](../README.md), en **Rust + GTK4**. Fenêtre
flottante qui capture chaque sélection de texte d'un terminal, **sans voler le
focus** et **sans toucher au presse-papier** : elle lit la sélection **PRIMARY**
(le copy-on-select natif de X11), donc rien à configurer côté terminal et le
CLIPBOARD (Ctrl+V) reste intact.

> Réimplémentation indépendante de la version Windows (.NET/WPF) — aucun code
> partagé. Voir le plan : `../../.claude/plans/` (ou le dossier de plans local).

## État

- [x] **Phase 1** — coquille de la fenêtre flottante + hints X11
      (always-on-top, no-focus-steal via `_NET_WM_STATE_ABOVE` + `WM_HINTS.input=false`).
- [ ] Phase 2 — capture de la sélection PRIMARY (XFixes).
- [ ] Phase 3 — multi-terminal (`_NET_ACTIVE_WINDOW`, buffers par fenêtre, onglets).
- [ ] Phase 4 — finitions UI (Auto-focus, drag/resize, styles).
- [ ] Phase 5 — packaging .deb / .rpm / AppImage + CI.

## Build

Nécessite Rust (stable) et les libs de dev GTK4 + X11 :

```bash
# Debian/Ubuntu
sudo apt install libgtk-4-dev libx11-dev libxfixes-dev

cargo build --release
cargo run
```

> Doit tourner sous **X11** (session « Xorg »). Wayland est hors périmètre v1.

## Packaging (à venir, Phase 5)

```bash
cargo install cargo-deb cargo-generate-rpm
cargo deb            # → target/debian/copyclaude_*.deb
cargo generate-rpm   # → target/generate-rpm/copyclaude-*.rpm
```

Dépendances runtime déclarées : `libgtk-4-1`, `libx11-6`, `libxfixes3`.
L'icône `packaging/icon.png` (256×256) reste à générer depuis `../icon.ico`.
