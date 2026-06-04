//! Connexion X11 dédiée au thread GTK, pour les lectures à la demande (titre
//! d'onglet, existence d'un terminal lors du sweep). Distincte de la connexion
//! du watcher : pas de partage entre threads.

use x11rb::connection::Connection;
use x11rb::rust_connection::RustConnection;

use crate::x11::atoms::Atoms;
use crate::x11::query;

pub struct X11Reader {
    conn: RustConnection,
    atoms: Atoms,
}

impl X11Reader {
    pub fn new() -> Result<Self, Box<dyn std::error::Error>> {
        let (conn, _screen) = x11rb::connect(None)?;
        let atoms = Atoms::intern(&conn)?;
        Ok(X11Reader { conn, atoms })
    }

    pub fn title(&self, xid: u32) -> Option<String> {
        query::window_title(&self.conn, &self.atoms, xid)
    }

    pub fn exists(&self, xid: u32) -> bool {
        query::window_exists(&self.conn, xid)
    }
}
