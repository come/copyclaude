//! Filtre par nom de process (équivalent de `ProcessFilter` côté Windows).
//! On ne retient une sélection que si la fenêtre active appartient à un process
//! de l'allowlist. Allowlist configurable via `allowlist.txt` (un nom par ligne,
//! sans extension, `#` pour commenter), cherché à côté du binaire puis dans
//! `~/.config/copyclaude/`.

use std::collections::HashSet;
use std::fs;
use std::path::PathBuf;

/// Allowlist par défaut des terminaux Linux courants (nom de `/proc/<pid>/comm`).
const DEFAULT_ALLOWLIST: &[&str] = &[
    "gnome-terminal-",  // gnome-terminal-server (comm tronqué à 15 caractères)
    "konsole",
    "xterm",
    "alacritty",
    "kitty",
    "wezterm",
    "wezterm-gui",
    "foot",
    "tilix",
    "xfce4-terminal",
    "code",
];

pub struct ProcessFilter {
    allowlist: HashSet<String>,
    own_pid: u32,
}

impl ProcessFilter {
    pub fn new() -> Self {
        ProcessFilter {
            allowlist: load_allowlist(),
            own_pid: std::process::id(),
        }
    }

    /// Vrai si le PID correspond à un process de l'allowlist.
    pub fn is_terminal(&self, pid: u32) -> bool {
        match process_comm(pid) {
            Some(name) => self.allowlist.contains(&name),
            None => false,
        }
    }

    /// Vrai si le PID est notre propre process (la fenêtre flottante).
    pub fn is_own(&self, pid: u32) -> bool {
        pid == self.own_pid
    }
}

/// Lit `/proc/<pid>/comm` (nom du process, sans chemin ni arguments).
fn process_comm(pid: u32) -> Option<String> {
    let raw = fs::read_to_string(format!("/proc/{pid}/comm")).ok()?;
    Some(raw.trim_end().to_string())
}

fn load_allowlist() -> HashSet<String> {
    if let Some(path) = allowlist_path() {
        if let Ok(content) = fs::read_to_string(&path) {
            let names: HashSet<String> = content
                .lines()
                .map(str::trim)
                .filter(|l| !l.is_empty() && !l.starts_with('#'))
                .map(str::to_string)
                .collect();
            if !names.is_empty() {
                return names;
            }
        }
    }
    DEFAULT_ALLOWLIST.iter().map(|s| s.to_string()).collect()
}

/// `allowlist.txt` à côté du binaire, sinon dans `~/.config/copyclaude/`.
fn allowlist_path() -> Option<PathBuf> {
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            let p = dir.join("allowlist.txt");
            if p.exists() {
                return Some(p);
            }
        }
    }
    if let Ok(home) = std::env::var("HOME") {
        let p = PathBuf::from(home).join(".config/copyclaude/allowlist.txt");
        if p.exists() {
            return Some(p);
        }
    }
    None
}
