//! Couche X11 (x11rb).
//! - `props`    : pose des hints WM sur la fenêtre flottante (always-on-top,
//!                no-focus-steal).
//! - `selection`: surveillance de la sélection PRIMARY via XFixes (la capture).
//!
//! Le suivi de la fenêtre active (`_NET_ACTIVE_WINDOW`) pour le multi-terminal
//! arrivera en Phase 3.

pub mod props;
pub mod selection;
