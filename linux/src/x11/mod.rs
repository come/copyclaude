//! Couche X11 (x11rb).
//! - `atoms`   : atomes internés, partagés.
//! - `query`   : lectures sans état (fenêtre active, PID, titre, existence).
//! - `props`   : pose des hints WM (always-on-top, no-focus-steal) + activation.
//! - `reader`  : connexion de lecture pour le thread GTK (titres, sweep).
//! - `watcher` : thread X11 (sélection PRIMARY via XFixes + suivi fenêtre active).

pub mod atoms;
pub mod props;
pub mod query;
pub mod reader;
pub mod watcher;
