//! Pose sur la fenêtre GTK (via son XID) les hints X11 qui reproduisent le
//! comportement `WS_EX_NOACTIVATE` + `Topmost` de la version Windows :
//! always-on-top, type « utility », et surtout **ne jamais voler le focus**.
//!
//! GTK4 a retiré les API correspondantes (`set_keep_above`, `set_type_hint`…),
//! devenues inutiles sous Wayland — on passe donc directement par x11rb.

use x11rb::connection::Connection;
use x11rb::properties::WmHints;
use x11rb::protocol::xproto::{
    AtomEnum, ClientMessageEvent, ConnectionExt, EventMask, PropMode, Window,
};
// Fournit les helpers `change_property8/16/32` (trait distinct de xproto::ConnectionExt).
use x11rb::wrapper::ConnectionExt as _;
use x11rb::CURRENT_TIME;

/// Action `_NET_WM_STATE` : 0 = retirer, 1 = ajouter (cf. spec EWMH).
const NET_WM_STATE_REMOVE: u32 = 0;
const NET_WM_STATE_ADD: u32 = 1;

/// Applique l'état « fenêtre flottante » une fois pour toutes, juste après que
/// la fenêtre GTK a obtenu un XID. À appeler depuis le handler `realize`.
pub fn apply_floating_hints(xid: u32) -> Result<(), Box<dyn std::error::Error>> {
    let (conn, _screen) = x11rb::connect(None)?;
    let window: Window = xid;

    // 1) Ne pas accepter le focus clavier au mapping : WM_HINTS.input = false
    //    + _NET_WM_USER_TIME = 0 (indique au WM « ne m'active pas à l'ouverture »).
    let hints = WmHints {
        input: Some(false),
        ..WmHints::default()
    };
    hints.set(&conn, window)?;

    let user_time = conn.intern_atom(false, b"_NET_WM_USER_TIME")?.reply()?.atom;
    conn.change_property32(PropMode::REPLACE, window, user_time, AtomEnum::CARDINAL, &[0])?;

    // 2) Type « utility » : pas dans l'alt-tab, traité comme un utilitaire flottant.
    let wm_type = conn.intern_atom(false, b"_NET_WM_WINDOW_TYPE")?.reply()?.atom;
    let wm_type_utility = conn
        .intern_atom(false, b"_NET_WM_WINDOW_TYPE_UTILITY")?
        .reply()?
        .atom;
    conn.change_property32(PropMode::REPLACE, window, wm_type, AtomEnum::ATOM, &[wm_type_utility])?;

    conn.flush()?;
    Ok(())
}

/// Active/désactive l'état always-on-top (`_NET_WM_STATE_ABOVE`). Reproduit le
/// `Topmost` qui suit le premier plan : au-dessus devant un terminal, lâché sinon.
pub fn set_above(xid: u32, above: bool) -> Result<(), Box<dyn std::error::Error>> {
    let (conn, screen_num) = x11rb::connect(None)?;
    let root = conn.setup().roots[screen_num].root;
    let window: Window = xid;

    let wm_state = conn.intern_atom(false, b"_NET_WM_STATE")?.reply()?.atom;
    let wm_state_above = conn.intern_atom(false, b"_NET_WM_STATE_ABOVE")?.reply()?.atom;

    // Fenêtre déjà mappée → on demande le changement au WM via un message client
    // adressé à la root (mode requis par la spec EWMH).
    let action = if above { NET_WM_STATE_ADD } else { NET_WM_STATE_REMOVE };
    let event = ClientMessageEvent::new(32, window, wm_state, [action, wm_state_above, 0, 1, 0]);
    conn.send_event(
        false,
        root,
        EventMask::SUBSTRUCTURE_NOTIFY | EventMask::SUBSTRUCTURE_REDIRECT,
        event,
    )?;
    conn.flush()?;
    Ok(())
}

/// Donne le focus à notre fenêtre (message client `_NET_ACTIVE_WINDOW`). Utilisé
/// uniquement sur action explicite (clic, ou toggle Auto-focus) — jamais sur un
/// ajout automatique. Pas besoin de l'`AttachThreadInput` de Windows : X11 honore
/// la requête d'activation directement.
pub fn activate(xid: u32) -> Result<(), Box<dyn std::error::Error>> {
    let (conn, screen_num) = x11rb::connect(None)?;
    let root = conn.setup().roots[screen_num].root;
    let net_active = conn.intern_atom(false, b"_NET_ACTIVE_WINDOW")?.reply()?.atom;

    // data[0] = 1 : indication « demande applicative » (cf. spec EWMH).
    let event = ClientMessageEvent::new(32, xid, net_active, [1, CURRENT_TIME, 0, 0, 0]);
    conn.send_event(
        false,
        root,
        EventMask::SUBSTRUCTURE_NOTIFY | EventMask::SUBSTRUCTURE_REDIRECT,
        event,
    )?;
    conn.flush()?;
    Ok(())
}
