//! Surveillance de la sélection **PRIMARY** via l'extension XFixes — l'équivalent
//! Linux de `ClipboardListener` (Windows). Tourne sur un thread dédié avec sa
//! propre connexion X11, et envoie chaque capture stabilisée au thread GTK via
//! un `async-channel`.
//!
//! On lit PRIMARY (le copy-on-select natif de X11), jamais le CLIPBOARD : la
//! sélection reste collable au clic-milieu et Ctrl+V ailleurs est intact.

use std::time::{Duration, Instant};

use x11rb::connection::Connection;
use x11rb::protocol::xfixes::{ConnectionExt as _, SelectionEvent, SelectionEventMask};
use x11rb::protocol::xproto::{
    Atom, AtomEnum, ConnectionExt as _, CreateWindowAux, Window, WindowClass,
};
use x11rb::protocol::Event;
use x11rb::CURRENT_TIME;

use crate::process_filter::ProcessFilter;

/// Délai d'attente d'une sélection stable : copy-on-select peut émettre plusieurs
/// changements d'ownership pendant un drag, on n'agit qu'une fois le calme revenu.
const DEBOUNCE: Duration = Duration::from_millis(300);

/// Une capture stabilisée : texte + XID de la fenêtre terminal source.
pub type Capture = (String, u32);

struct Atoms {
    net_active_window: Atom,
    net_wm_pid: Atom,
    utf8_string: Atom,
    transfer: Atom,
}

/// Boucle de surveillance (bloquante) : à lancer sur un thread dédié.
pub fn run(sender: async_channel::Sender<Capture>) -> Result<(), Box<dyn std::error::Error>> {
    let filter = ProcessFilter::new();
    let (conn, screen_num) = x11rb::connect(None)?;
    let root = conn.setup().roots[screen_num].root;
    let root_visual = conn.setup().roots[screen_num].root_visual;
    let root_depth = conn.setup().roots[screen_num].root_depth;

    // Négocier la version XFixes (obligatoire avant tout appel de l'extension).
    conn.xfixes_query_version(5, 0)?.reply()?;

    // Fenêtre invisible non mappée, propriétaire de nos requêtes de sélection
    // (équivalent de la fenêtre message-only Windows).
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

    let atoms = Atoms {
        net_active_window: conn.intern_atom(false, b"_NET_ACTIVE_WINDOW")?.reply()?.atom,
        net_wm_pid: conn.intern_atom(false, b"_NET_WM_PID")?.reply()?.atom,
        utf8_string: conn.intern_atom(false, b"UTF8_STRING")?.reply()?.atom,
        transfer: conn.intern_atom(false, b"COPYCLAUDE_SELECTION")?.reply()?.atom,
    };

    // S'abonner aux changements de propriétaire de PRIMARY.
    let primary: Atom = AtomEnum::PRIMARY.into();
    conn.xfixes_select_selection_input(
        win,
        primary,
        SelectionEventMask::SET_SELECTION_OWNER
            | SelectionEventMask::SELECTION_WINDOW_DESTROY
            | SelectionEventMask::SELECTION_CLIENT_CLOSE,
    )?;
    conn.flush()?;

    let mut deadline: Option<Instant> = None;
    let mut source: Option<Window> = None;

    loop {
        // Drainer les events en attente.
        while let Some(event) = conn.poll_for_event()? {
            if let Event::XfixesSelectionNotify(ev) = event {
                // Nouvelle sélection PRIMARY posée par une autre fenêtre que nous.
                if ev.subtype == SelectionEvent::SET_SELECTION_OWNER
                    && ev.selection == primary
                    && ev.owner != win
                    && ev.owner != x11rb::NONE
                {
                    // Filtre AU MOMENT de l'event : on ne retient que les copies
                    // faites pendant qu'un terminal de l'allowlist est actif.
                    if let Some(active) = active_window(&conn, root, &atoms)? {
                        if let Some(pid) = window_pid(&conn, active, &atoms)? {
                            if filter.is_terminal(pid) {
                                source = Some(active);
                                deadline = Some(Instant::now() + DEBOUNCE);
                            }
                        }
                    }
                }
            }
        }

        // Sélection stabilisée → lire le texte et l'émettre.
        if let Some(at) = deadline {
            if Instant::now() >= at {
                deadline = None;
                if let Some(src) = source.take() {
                    if let Some(text) = read_primary(&conn, win, primary, &atoms)? {
                        if !text.is_empty() {
                            // send_blocking : le thread X11 n'est pas async.
                            let _ = sender.send_blocking((text, src));
                        }
                    }
                }
            }
        }

        std::thread::sleep(Duration::from_millis(20));
    }
}

/// Fenêtre active courante (`_NET_ACTIVE_WINDOW` sur la root).
fn active_window(
    conn: &impl Connection,
    root: Window,
    atoms: &Atoms,
) -> Result<Option<Window>, Box<dyn std::error::Error>> {
    let reply = conn
        .get_property(false, root, atoms.net_active_window, AtomEnum::WINDOW, 0, 1)?
        .reply()?;
    Ok(reply.value32().and_then(|mut it| it.next()))
}

/// PID propriétaire d'une fenêtre (`_NET_WM_PID`).
fn window_pid(
    conn: &impl Connection,
    window: Window,
    atoms: &Atoms,
) -> Result<Option<u32>, Box<dyn std::error::Error>> {
    let reply = conn
        .get_property(false, window, atoms.net_wm_pid, AtomEnum::CARDINAL, 0, 1)?
        .reply()?;
    Ok(reply.value32().and_then(|mut it| it.next()))
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
