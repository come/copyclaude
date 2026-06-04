//! CopyClaude — port Linux/X11 (Rust + GTK4).
//!
//! Fenêtre flottante toujours au-dessus du terminal, qui ne vole jamais le focus
//! et ne touche jamais au CLIPBOARD : elle lit la sélection PRIMARY (copy-on-select
//! natif de X11). Un buffer de captures par fenêtre de terminal (clé = XID),
//! l'affichage suit le terminal actif, une barre d'onglets permet de basculer à
//! la main. Réimplémentation indépendante de la version Windows (.NET/WPF).

mod process_filter;
mod x11;

use std::cell::{Cell, RefCell};
use std::collections::HashMap;
use std::rc::Rc;

use gtk4::gdk;
use gtk4::glib;
use gtk4::prelude::*;
use gtk4::{
    Application, ApplicationWindow, Box as GtkBox, Button, CssProvider, Label, Orientation,
    ScrolledWindow, TextBuffer, TextTag, TextView, ToggleButton, WindowHandle,
};

use x11::reader::X11Reader;
use x11::watcher::X11Event;

const APP_ID: &str = "com.copyclaude.Linux";

/// XID conventionnel de l'onglet « Notes » (texte tapé avant toute capture).
const NOTES_XID: u32 = 0;

/// Tag des blocs capturés (petit, grisé, non éditable).
const CAPTURE_TAG: &str = "capture";

/// Longueur max d'une étiquette d'onglet avant troncature.
const MAX_TAB_LEN: usize = 24;

const CSS: &str = "
window { background: #1e1e28; color: #dcdcdc; }
.ccl-header { background: #2d2d3a; padding: 2px 4px; }
.ccl-header label { color: #ffffff; }
.ccl-tabbar { background: #262631; }
textview { background: #1e1e28; color: #dcdcdc; padding: 6px; }
textview text { background: #1e1e28; }
";

/// Un buffer = le document d'un terminal et son onglet dans la barre.
struct TermBuffer {
    buffer: TextBuffer,
    tab: ToggleButton,
}

/// État partagé de l'UI (sur le thread GTK uniquement → `Rc`, pas de `Send`).
struct AppState {
    window: ApplicationWindow,
    text_view: TextView,
    tab_bar: GtkBox,
    tab_bar_host: ScrolledWindow,
    auto_focus: ToggleButton,
    reader: Option<X11Reader>,
    /// XID de notre propre fenêtre (connu après `realize`).
    self_xid: Cell<u32>,
    buffers: RefCell<HashMap<u32, TermBuffer>>,
    /// Ordre d'insertion, pour choisir un repli quand le buffer affiché disparaît.
    order: RefCell<Vec<u32>>,
    /// XID du buffer actuellement affiché (None tant qu'aucune capture).
    current: Cell<Option<u32>>,
}

fn main() -> glib::ExitCode {
    let app = Application::builder().application_id(APP_ID).build();
    app.connect_startup(|_| load_css());
    app.connect_activate(build_ui);
    app.run()
}

fn load_css() {
    let provider = CssProvider::new();
    provider.load_from_data(CSS);
    if let Some(display) = gdk::Display::default() {
        gtk4::style_context_add_provider_for_display(
            &display,
            &provider,
            gtk4::STYLE_PROVIDER_PRIORITY_APPLICATION,
        );
    }
}

fn build_ui(app: &Application) {
    let window = ApplicationWindow::builder()
        .application(app)
        .title("Captures terminal")
        .decorated(false)
        .default_width(420)
        .default_height(320)
        .resizable(true)
        .build();

    // --- En-tête : zone de drag (WindowHandle) + titre + boutons ---
    let title = Label::builder()
        .label("Captures terminal")
        .halign(gtk4::Align::Start)
        .hexpand(true)
        .margin_start(10)
        .build();

    let auto_focus = ToggleButton::builder()
        .label("Auto-focus")
        .tooltip_text("Prendre le focus à chaque capture, prêt à taper la note")
        .build();

    let clear_button = Button::builder()
        .label("Effacer")
        .tooltip_text("Vider le buffer affiché")
        .build();

    let quit_button = Button::builder()
        .label("✕")
        .tooltip_text("Quitter l'application")
        .build();

    let header_box = GtkBox::new(Orientation::Horizontal, 4);
    header_box.add_css_class("ccl-header");
    header_box.append(&title);
    header_box.append(&auto_focus);
    header_box.append(&clear_button);
    header_box.append(&quit_button);
    let header = WindowHandle::builder().child(&header_box).build();

    // --- Barre d'onglets : un onglet par terminal, masquée s'il y en a ≤ 1 ---
    let tab_bar = GtkBox::new(Orientation::Horizontal, 2);
    let tab_bar_host = ScrolledWindow::builder()
        .child(&tab_bar)
        .hscrollbar_policy(gtk4::PolicyType::External)
        .vscrollbar_policy(gtk4::PolicyType::Never)
        .visible(false)
        .build();
    tab_bar_host.add_css_class("ccl-tabbar");

    // --- Éditeur ---
    let text_view = TextView::builder()
        .editable(true)
        .monospace(true)
        .wrap_mode(gtk4::WrapMode::WordChar)
        .build();
    text_view.set_buffer(Some(&make_buffer()));

    let scroller = ScrolledWindow::builder().vexpand(true).child(&text_view).build();

    let root = GtkBox::new(Orientation::Vertical, 0);
    root.append(&header);
    root.append(&tab_bar_host);
    root.append(&scroller);
    window.set_child(Some(&root));

    let state = Rc::new(AppState {
        window: window.clone(),
        text_view: text_view.clone(),
        tab_bar,
        tab_bar_host,
        auto_focus: auto_focus.clone(),
        reader: X11Reader::new().ok(),
        self_xid: Cell::new(0),
        buffers: RefCell::new(HashMap::new()),
        order: RefCell::new(Vec::new()),
        current: Cell::new(None),
    });

    // Boutons.
    {
        let text_view = text_view.clone();
        clear_button.connect_clicked(move |_| text_view.buffer().set_text(""));
    }
    {
        let window = window.clone();
        quit_button.connect_clicked(move |_| window.close());
    }

    // --- Capture / premier plan : thread X11 → channel → boucle GTK ---
    let (tx, rx) = async_channel::unbounded::<X11Event>();
    std::thread::spawn(move || {
        if let Err(e) = x11::watcher::run(tx) {
            eprintln!("CopyClaude: watcher X11 arrêté : {e}");
        }
    });
    {
        let state = state.clone();
        glib::spawn_future_local(async move {
            while let Ok(event) = rx.recv().await {
                match event {
                    X11Event::Capture { text, xid } => state.append_block(&text, xid),
                    X11Event::Foreground { xid, relevant } => {
                        state.on_foreground_changed(relevant, xid)
                    }
                }
            }
        });
    }

    // Hints X11 une fois la fenêtre réalisée (always-on-top + no-focus-steal).
    {
        let state = state.clone();
        window.connect_realize(move |win| {
            if let Some(xid) = x11_window_id(win) {
                state.self_xid.set(xid);
                if let Err(e) = x11::props::apply_floating_hints(xid) {
                    eprintln!("CopyClaude: pose des hints X11 échouée : {e}");
                }
                let _ = x11::props::set_above(xid, true);
            } else {
                eprintln!("CopyClaude: surface non-X11 (Wayland ?) — hints non appliqués.");
            }
        });
    }

    window.present();
}

impl AppState {
    /// Ajoute un bloc capturé en fin du buffer du terminal source (jamais d'écrasement).
    fn append_block(self: &Rc<Self>, text: &str, xid: u32) {
        self.sweep_closed_buffers();
        self.get_or_create_buffer(xid);
        self.refresh_title(xid);

        if let Some(buffer) = self.buffer_of(xid) {
            append_capture(&buffer, text);
        }

        if self.auto_focus.is_active() {
            self.switch_to(xid);
            self.grab_focus();
        } else if self.current.get() == Some(xid) {
            self.scroll_to_end();
        }
    }

    /// Suivi du premier plan : `above` seulement devant un terminal (ou nous-mêmes),
    /// et bascule automatique vers le buffer du terminal actif.
    fn on_foreground_changed(self: &Rc<Self>, relevant: bool, xid: u32) {
        let _ = x11::props::set_above(self.self_xid.get(), relevant);
        self.sweep_closed_buffers();
        if relevant && self.buffers.borrow().contains_key(&xid) {
            self.refresh_title(xid);
            if self.current.get() != Some(xid) {
                self.switch_to(xid);
            }
        }
    }

    /// Affiche le buffer demandé : swap du document, surlignage d'onglet, fin de texte.
    fn switch_to(self: &Rc<Self>, xid: u32) {
        let Some(buffer) = self.buffer_of(xid) else { return };
        self.current.set(Some(xid));
        self.text_view.set_buffer(Some(&buffer));
        for (&id, b) in self.buffers.borrow().iter() {
            b.tab.set_active(id == xid);
        }
        self.scroll_to_end();
    }

    fn get_or_create_buffer(self: &Rc<Self>, xid: u32) {
        if self.buffers.borrow().contains_key(&xid) {
            return;
        }

        // Première capture : si on a déjà tapé dans le document de départ, il
        // devient l'onglet « Notes » (jamais supprimé) ; sinon il sera remplacé.
        let empty = self.buffers.borrow().is_empty();
        if empty && self.current.get().is_none() && document_has_text(&self.text_view.buffer()) {
            let notes_buf = self.text_view.buffer();
            self.register_buffer(NOTES_XID, notes_buf);
            if let Some(b) = self.buffers.borrow().get(&NOTES_XID) {
                b.tab.set_label("Notes");
                b.tab.set_active(true);
            }
            self.current.set(Some(NOTES_XID));
        }

        self.register_buffer(xid, make_buffer());
        if self.current.get().is_none() {
            self.switch_to(xid);
        }
    }

    fn register_buffer(self: &Rc<Self>, xid: u32, buffer: TextBuffer) {
        let tab = ToggleButton::builder().label("Terminal").build();
        {
            let state = self.clone();
            tab.connect_clicked(move |_| state.switch_to(xid));
        }
        self.tab_bar.append(&tab);
        self.buffers.borrow_mut().insert(xid, TermBuffer { buffer, tab });
        self.order.borrow_mut().push(xid);
        self.update_tab_bar_visibility();
    }

    /// Retire les buffers dont la fenêtre de terminal n'existe plus (terminal
    /// fermé = notes supprimées). Event-driven : appelé à chaque capture / focus.
    fn sweep_closed_buffers(self: &Rc<Self>) {
        let Some(reader) = self.reader.as_ref() else { return };

        let dead: Vec<u32> = self
            .order
            .borrow()
            .iter()
            .copied()
            .filter(|&id| id != NOTES_XID && !reader.exists(id))
            .collect();

        if !dead.is_empty() {
            let mut buffers = self.buffers.borrow_mut();
            for id in &dead {
                if let Some(b) = buffers.remove(id) {
                    self.tab_bar.remove(&b.tab);
                }
                if self.current.get() == Some(*id) {
                    self.current.set(None);
                }
            }
            self.order.borrow_mut().retain(|id| !dead.contains(id));
        }

        // Le buffer affiché a disparu → repli sur le premier restant, sinon vide.
        if self.current.get().is_none() {
            let first = self.order.borrow().first().copied();
            match first {
                Some(id) => self.switch_to(id),
                None => self.text_view.set_buffer(Some(&make_buffer())),
            }
        }
        self.update_tab_bar_visibility();
    }

    /// Titre vivant : relu sur la fenêtre du terminal à chaque capture / focus.
    fn refresh_title(self: &Rc<Self>, xid: u32) {
        if xid == NOTES_XID {
            return;
        }
        let (Some(reader), buffers) = (self.reader.as_ref(), self.buffers.borrow()) else { return };
        let Some(b) = buffers.get(&xid) else { return };
        let title = reader.title(xid).unwrap_or_else(|| "Terminal".to_string());
        b.tab.set_tooltip_text(Some(&title));
        let label = if title.chars().count() > MAX_TAB_LEN {
            let truncated: String = title.chars().take(MAX_TAB_LEN - 1).collect();
            format!("{truncated}…")
        } else {
            title
        };
        b.tab.set_label(&label);
    }

    fn update_tab_bar_visibility(&self) {
        self.tab_bar_host.set_visible(self.buffers.borrow().len() > 1);
    }

    /// Donne le focus à la fenêtre (Auto-focus) puis place le curseur dans l'éditeur.
    fn grab_focus(&self) {
        let _ = x11::props::activate(self.self_xid.get());
        self.text_view.grab_focus();
    }

    fn scroll_to_end(&self) {
        let buffer = self.text_view.buffer();
        let mut end = buffer.end_iter();
        buffer.place_cursor(&end);
        self.text_view.scroll_to_iter(&mut end, 0.0, false, 0.0, 0.0);
    }

    /// Handle (clonable) du `TextBuffer` d'un XID, borrow relâché immédiatement.
    fn buffer_of(&self, xid: u32) -> Option<TextBuffer> {
        self.buffers.borrow().get(&xid).map(|b| b.buffer.clone())
    }
}

/// Crée un `TextBuffer` doté du tag « capture ».
fn make_buffer() -> TextBuffer {
    let buffer = TextBuffer::new(None::<&gtk4::TextTagTable>);
    let tag = TextTag::builder()
        .name(CAPTURE_TAG)
        .scale(0.82)
        .foreground("#a0a8b8")
        .editable(false)
        .build();
    buffer.tag_table().add(&tag);
    buffer
}

/// Insère un bloc capturé en fin de buffer : lignes préfixées « > », taggées
/// (petit/grisé/non éditable), suivies d'une ligne vide en taille normale.
fn append_capture(buffer: &TextBuffer, text: &str) {
    let block: String = text
        .replace("\r\n", "\n")
        .trim_end_matches('\n')
        .split('\n')
        .map(|line| format!("> {line}"))
        .collect::<Vec<_>>()
        .join("\n");

    let mut end = buffer.end_iter();
    if buffer.char_count() > 0 {
        buffer.insert(&mut end, "\n");
    }

    let start_offset = buffer.end_iter().offset();
    let mut end = buffer.end_iter();
    buffer.insert(&mut end, &block);

    let start = buffer.iter_at_offset(start_offset);
    let end = buffer.end_iter();
    buffer.apply_tag_by_name(CAPTURE_TAG, &start, &end);

    // Ligne de note en taille normale (hors tag).
    let mut end = buffer.end_iter();
    buffer.insert(&mut end, "\n");
}

fn document_has_text(buffer: &TextBuffer) -> bool {
    let (start, end) = buffer.bounds();
    !buffer.text(&start, &end, false).trim().is_empty()
}

/// Récupère le XID X11 de la fenêtre GTK, ou `None` sous Wayland.
fn x11_window_id(window: &ApplicationWindow) -> Option<u32> {
    use gdk4_x11::prelude::*;
    use gdk4_x11::X11Surface;
    let surface = window.surface()?;
    let x11_surface = surface.downcast::<X11Surface>().ok()?;
    Some(x11_surface.xid() as u32)
}
