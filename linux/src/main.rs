//! CopyClaude — port Linux/X11 (Rust + GTK4).
//!
//! Fenêtre flottante toujours au-dessus du terminal, qui ne vole jamais le focus
//! et ne touche jamais au CLIPBOARD : elle lit la sélection PRIMARY (copy-on-select
//! natif de X11). Réimplémentation indépendante de la version Windows (.NET/WPF).
//!
//! Phase 2 : capture de la sélection PRIMARY (XFixes) affichée en blocs grisés.
//! Le multi-terminal (un buffer par fenêtre + onglets) arrive en Phase 3.

mod process_filter;
mod x11;

use gtk4::glib;
use gtk4::prelude::*;
use gtk4::{
    Application, ApplicationWindow, Box as GtkBox, Button, Label, Orientation, ScrolledWindow,
    TextBuffer, TextTag, TextView, ToggleButton, WindowHandle,
};

const APP_ID: &str = "com.copyclaude.Linux";

/// Nom du tag appliqué aux blocs capturés (petit, grisé, non éditable).
const CAPTURE_TAG: &str = "capture";

fn main() -> glib::ExitCode {
    let app = Application::builder().application_id(APP_ID).build();
    app.connect_activate(build_ui);
    app.run()
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

    // Toggle Auto-focus : quand actif, chaque capture donnera le focus à la
    // fenêtre (seule exception volontaire à la règle « jamais voler le focus »).
    // Branché en Phase 4 ; présent dès maintenant pour figer l'UI.
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
    header_box.append(&title);
    header_box.append(&auto_focus);
    header_box.append(&clear_button);
    header_box.append(&quit_button);

    // WindowHandle : rend l'en-tête draggable pour déplacer la fenêtre (GTK4).
    let header = WindowHandle::builder().child(&header_box).build();

    // --- Éditeur : un TextView, blocs capturés taggés (petit/grisé/non éditable),
    // notes en taille normale et éditables. ---
    let editor = TextView::builder()
        .editable(true)
        .monospace(true)
        .wrap_mode(gtk4::WrapMode::WordChar)
        .build();
    let buffer = editor.buffer();

    // Tag des blocs capturés.
    let capture_tag = TextTag::builder()
        .name(CAPTURE_TAG)
        .scale(0.82)
        .foreground("#A0A8B8")
        .editable(false)
        .build();
    buffer.tag_table().add(&capture_tag);

    let scroller = ScrolledWindow::builder()
        .vexpand(true)
        .child(&editor)
        .build();

    let root = GtkBox::new(Orientation::Vertical, 0);
    root.append(&header);
    root.append(&scroller);
    window.set_child(Some(&root));

    // Boutons.
    {
        let buffer = buffer.clone();
        clear_button.connect_clicked(move |_| buffer.set_text(""));
    }
    {
        let window = window.clone();
        quit_button.connect_clicked(move |_| window.close());
    }

    // --- Capture : thread X11 → channel → boucle GTK ---
    let (tx, rx) = async_channel::unbounded::<x11::selection::Capture>();
    std::thread::spawn(move || {
        if let Err(e) = x11::selection::run(tx) {
            eprintln!("CopyClaude: watcher X11 arrêté : {e}");
        }
    });
    {
        let buffer = buffer.clone();
        let editor = editor.clone();
        glib::spawn_future_local(async move {
            while let Ok((text, _xid)) = rx.recv().await {
                append_capture_block(&buffer, &text);
                // Curseur en fin + scroll (la cible terminal sera gérée en Phase 3).
                let mut end = buffer.end_iter();
                buffer.place_cursor(&end);
                editor.scroll_to_iter(&mut end, 0.0, false, 0.0, 0.0);
            }
        });
    }

    // Une fois la fenêtre réalisée, elle a un XID X11 : on pose les hints WM
    // (always-on-top + ne jamais voler le focus).
    window.connect_realize(|win| {
        if let Some(xid) = x11_window_id(win) {
            if let Err(e) = x11::props::apply_floating_hints(xid) {
                eprintln!("CopyClaude: pose des hints X11 échouée : {e}");
            }
            let _ = x11::props::set_above(xid, true);
        } else {
            eprintln!("CopyClaude: surface non-X11 (Wayland ?) — hints non appliqués.");
        }
    });

    window.present();
}

/// Ajoute un bloc capturé en fin de buffer : lignes préfixées « > », taggées
/// (petit/grisé/non éditable), suivies d'une ligne vide en taille normale où
/// taper la note. N'écrase jamais le texte déjà saisi.
fn append_capture_block(buffer: &TextBuffer, text: &str) {
    let block: String = text
        .replace("\r\n", "\n")
        .trim_end_matches('\n')
        .split('\n')
        .map(|line| format!("> {line}"))
        .collect::<Vec<_>>()
        .join("\n");

    let mut end = buffer.end_iter();
    // Séparation visuelle si le buffer n'est pas vide.
    if buffer.char_count() > 0 {
        buffer.insert(&mut end, "\n");
    }

    let start_offset = buffer.end_iter().offset();
    let mut end = buffer.end_iter();
    buffer.insert(&mut end, &block);

    // Tagge le bloc fraîchement inséré.
    let start = buffer.iter_at_offset(start_offset);
    let end = buffer.end_iter();
    buffer.apply_tag_by_name(CAPTURE_TAG, &start, &end);

    // Ligne de note en taille normale (hors tag).
    let mut end = buffer.end_iter();
    buffer.insert(&mut end, "\n");
}

/// Récupère le XID X11 de la fenêtre GTK, ou `None` sous Wayland.
fn x11_window_id(window: &ApplicationWindow) -> Option<u32> {
    use gdk4_x11::X11Surface;
    let surface = window.surface()?;
    let x11_surface = surface.downcast::<X11Surface>().ok()?;
    Some(x11_surface.xid() as u32)
}
