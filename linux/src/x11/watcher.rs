//! Thread de surveillance X11 (connexion + boucle d'events dédiées) — l'équivalent
//! Linux de `ClipboardListener` + `ForegroundWatcher` réunis :
//! - **sélection PRIMARY** via XFixes (le copy-on-select natif de X11) ;
//! - **fenêtre active** via `PropertyNotify` sur `_NET_ACTIVE_WINDOW`.
//!
//! On lit PRIMARY, jamais le CLIPBOARD : Ctrl+V ailleurs reste intact.
//! Les events sont envoyés au thread GTK via un `async-channel`.

use std::time::{Duration, Instant};

use x11rb::connection::Connection;
use x11rb::protocol::xfixes::{ConnectionExt as _, SelectionEvent, SelectionEventMask};
use x11rb::protocol::xproto::{
    Atom, AtomEnum, ChangeWindowAttributesAux, ConnectionExt as _, CreateWindowAux, EventMask,
    Window, WindowClass,
};
use x11rb::protocol::Event;
use x11rb::CURRENT_TIME;

use crate::process_filter::ProcessFilter;
use crate::x11::atoms::Atoms;
use crate::x11::query;

/// Délai d'attente d'une sélection stable (un drag émet plusieurs changements).
const DEBOUNCE: Duration = Duration::from_millis(300);

/// Événements remontés au thread GTK.
pub enum X11Event {
    /// Sélection terminal stabilisée : texte + XID de la fenêtre source.
    Capture { text: String, xid: u32 },
    /// Changement de fenêtre active : XID + pertinence (terminal ou nous-mêmes).
    Foreground { xid: u32, relevant: bool },
}

/// Boucle de surveillance (bloquante) : à lancer sur un thread dédié.
pub fn run(sender: async_channel::Sender<X11Event>) -> Result<(), Box<dyn std::error::Error>> {
    let filter = ProcessFilter::new();
    let (conn, screen_num) = x11rb::connect(None)?;
    let (root, root_visual, root_depth) = {
        let s = &conn.setup().roots[screen_num];
        (s.root, s.root_visual, s.root_depth)
    };

    // Négocier XFixes (obligatoire avant tout appel de l'extension).
    conn.xfixes_query_version(5, 0)?.reply()?;

    // Fenêtre invisible non mappée, propriétaire de nos requêtes de sélection.
    let win = conn.generate_id()?;
    conn.create_window(
        root_depth,
        win,
        root,
        0,
        0,
        1,
        1,
        0,
        WindowClass::INPUT_OUTPUT,
        root_visual,
        &CreateWindowAux::new(),
    )?;

    let atoms = Atoms::intern(&conn)?;
    let primary: Atom = AtomEnum::PRIMARY.into();

    // S'abonner aux changements de propriétaire de PRIMARY…
    conn.xfixes_select_selection_input(
        win,
        primary,
        SelectionEventMask::SET_SELECTION_OWNER
            | SelectionEventMask::SELECTION_WINDOW_DESTROY
            | SelectionEventMask::SELECTION_CLIENT_CLOSE,
    )?;
    // …et aux changements de propriétés de la root (pour `_NET_ACTIVE_WINDOW`).
    conn.change_window_attributes(
        root,
        &ChangeWindowAttributesAux::new().event_mask(EventMask::PROPERTY_CHANGE),
    )?;
    conn.flush()?;

    let mut deadline: Option<Instant> = None;
    let mut source: Option<Window> = None;

    loop {
        while let Some(event) = conn.poll_for_event()? {
            match event {
                // Nouvelle sélection PRIMARY posée par une autre fenêtre que nous.
                Event::XfixesSelectionNotify(ev)
                    if ev.subtype == SelectionEvent::SET_SELECTION_OWNER
                        && ev.selection == primary
                        && ev.owner != win
                        && ev.owner != x11rb::NONE =>
                {
                    // Filtre AU MOMENT de l'event : copie faite pendant qu'un
                    // terminal de l'allowlist est actif.
                    if let Some(active) = query::active_window(&conn, &atoms, root) {
                        if let Some(pid) = query::window_pid(&conn, &atoms, active) {
                            if filter.is_terminal(pid) {
                                source = Some(active);
                                deadline = Some(Instant::now() + DEBOUNCE);
                            }
                        }
                    }
                }
                // Changement de fenêtre active.
                Event::PropertyNotify(ev) if ev.window == root && ev.atom == atoms.net_active_window => {
                    let (xid, relevant) = match query::active_window(&conn, &atoms, root) {
                        Some(active) => {
                            let relevant = query::window_pid(&conn, &atoms, active)
                                .map(|pid| filter.is_terminal(pid) || filter.is_own(pid))
                                .unwrap_or(false);
                            (active, relevant)
                        }
                        None => (0, false),
                    };
                    let _ = sender.send_blocking(X11Event::Foreground { xid, relevant });
                }
                _ => {}
            }
        }

        // Sélection stabilisée → lire le texte et l'émettre.
        if let Some(at) = deadline {
            if Instant::now() >= at {
                deadline = None;
                if let Some(src) = source.take() {
                    if let Some(text) = read_primary(&conn, win, primary, &atoms)? {
                        if !text.is_empty() {
                            let _ = sender.send_blocking(X11Event::Capture { text, xid: src });
                        }
                    }
                }
            }
        }

        std::thread::sleep(Duration::from_millis(20));
    }
}

/// Demande le contenu de PRIMARY en UTF-8 et attend le `SelectionNotify`.
fn read_primary(
    conn: &impl Connection,
    win: Window,
    primary: Atom,
    atoms: &Atoms,
) -> Result<Option<String>, Box<dyn std::error::Error>> {
    conn.convert_selection(win, primary, atoms.utf8_string, atoms.transfer, CURRENT_TIME)?;
    conn.flush()?;

    let deadline = Instant::now() + Duration::from_millis(1000);
    loop {
        if Instant::now() > deadline {
            return Ok(None); // pas de réponse (sélection disparue)
        }
        while let Some(event) = conn.poll_for_event()? {
            if let Event::SelectionNotify(sn) = event {
                if sn.property == x11rb::NONE {
                    return Ok(None); // conversion refusée
                }
                let reply = conn
                    .get_property(true, win, atoms.transfer, AtomEnum::ANY, 0, 0x1FFF_FFFF)?
                    .reply()?;
                return Ok(Some(String::from_utf8_lossy(&reply.value).into_owned()));
            }
        }
        std::thread::sleep(Duration::from_millis(5));
    }
}
