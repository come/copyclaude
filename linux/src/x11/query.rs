//! Lectures X11 sans état, utilisables depuis n'importe quelle connexion :
//! fenêtre active, PID, titre, existence d'une fenêtre. Toutes les erreurs sont
//! avalées (→ `None`/`false`) : ces lectures sont best-effort.

use x11rb::connection::Connection;
use x11rb::protocol::xproto::{AtomEnum, ConnectionExt, Window};

use crate::x11::atoms::Atoms;

/// Fenêtre active courante (`_NET_ACTIVE_WINDOW` sur la root).
pub fn active_window(conn: &impl Connection, atoms: &Atoms, root: Window) -> Option<Window> {
    let reply = conn
        .get_property(false, root, atoms.net_active_window, AtomEnum::WINDOW, 0, 1)
        .ok()?
        .reply()
        .ok()?;
    reply.value32().and_then(|mut it| it.next()).filter(|&w| w != 0)
}

/// PID propriétaire d'une fenêtre (`_NET_WM_PID`).
pub fn window_pid(conn: &impl Connection, atoms: &Atoms, window: Window) -> Option<u32> {
    let reply = conn
        .get_property(false, window, atoms.net_wm_pid, AtomEnum::CARDINAL, 0, 1)
        .ok()?
        .reply()
        .ok()?;
    reply.value32().and_then(|mut it| it.next())
}

/// Titre d'une fenêtre : `_NET_WM_NAME` (UTF-8), repli sur `WM_NAME`.
pub fn window_title(conn: &impl Connection, atoms: &Atoms, window: Window) -> Option<String> {
    if let Ok(cookie) =
        conn.get_property(false, window, atoms.net_wm_name, atoms.utf8_string, 0, 1024)
    {
        if let Ok(reply) = cookie.reply() {
            if !reply.value.is_empty() {
                return Some(String::from_utf8_lossy(&reply.value).into_owned());
            }
        }
    }
    let reply = conn
        .get_property(false, window, AtomEnum::WM_NAME, AtomEnum::STRING, 0, 1024)
        .ok()?
        .reply()
        .ok()?;
    if reply.value.is_empty() {
        None
    } else {
        Some(String::from_utf8_lossy(&reply.value).into_owned())
    }
}

/// Vrai si la fenêtre existe encore (sinon `GetWindowAttributes` renvoie BadWindow).
pub fn window_exists(conn: &impl Connection, window: Window) -> bool {
    match conn.get_window_attributes(window) {
        Ok(cookie) => cookie.reply().is_ok(),
        Err(_) => false,
    }
}
