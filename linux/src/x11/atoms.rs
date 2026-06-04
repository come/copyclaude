//! Atomes X11 internés une fois, partagés entre le watcher (thread X11) et le
//! reader (thread GTK).

use x11rb::connection::Connection;
use x11rb::protocol::xproto::{Atom, ConnectionExt};

pub struct Atoms {
    pub net_active_window: Atom,
    pub net_wm_pid: Atom,
    pub net_wm_name: Atom,
    pub utf8_string: Atom,
    /// Propriété temporaire où le serveur dépose le contenu de PRIMARY.
    pub transfer: Atom,
}

impl Atoms {
    pub fn intern(conn: &impl Connection) -> Result<Self, Box<dyn std::error::Error>> {
        Ok(Atoms {
            net_active_window: intern(conn, b"_NET_ACTIVE_WINDOW")?,
            net_wm_pid: intern(conn, b"_NET_WM_PID")?,
            net_wm_name: intern(conn, b"_NET_WM_NAME")?,
            utf8_string: intern(conn, b"UTF8_STRING")?,
            transfer: intern(conn, b"COPYCLAUDE_SELECTION")?,
        })
    }
}

fn intern(conn: &impl Connection, name: &[u8]) -> Result<Atom, Box<dyn std::error::Error>> {
    Ok(conn.intern_atom(false, name)?.reply()?.atom)
}
