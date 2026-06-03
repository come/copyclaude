# CopyClaude — Floating window capturing terminal selections

A small Windows background app: every text selection in the terminal is appended
to an editable floating window, **without stealing focus** and **without
touching the clipboard** (the selection stays pasteable with Ctrl+V).

![CopyClaude demo](docs/copyclaude.gif)

## Install

Download the latest installer (`CopyClaude-Setup-*.exe`) from the
[Releases](../../releases) page and run it — per-user install, no admin rights
required.

Or build from source with the .NET 8 SDK:

```powershell
dotnet run
```

## Prerequisites

**Windows Terminal — `copyOnSelect`**: this is what triggers the capture.
In Windows Terminal's `settings.json` (Settings → *Open JSON file*), add:

```json
"copyOnSelect": true
```

## Usage

- Select text in Windows Terminal → it appears at the bottom of the floating
  window as a small, dimmed block prefixed with `> `, followed by an empty
  paragraph where the caret is placed.
- Click inside the window to type freely in normal-size text (notes under a
  captured block, etc.). New captures are always appended **at the end** —
  nothing you typed is ever overwritten.
- Drag the window by its header, resize it from the bottom-right corner.
- `Auto-focus` (toggle): when enabled, each capture gives focus to the window,
  caret ready to type the note — the one deliberate exception to the
  no-focus-stealing rule.
- `Effacer` clears the content; `✕` quits the app.

## Filtering captured applications

Only copies made while a process from the allowlist is in the foreground are
captured. Defaults: `WindowsTerminal`, `pwsh`, `powershell`, `conhost`, `Code`.

To customize: create an `allowlist.txt` file next to the exe, one process name
per line (without `.exe`, `#` for comments). If present and non-empty, it
replaces the defaults.

## Guarantees

- **Read-only clipboard**: the app never calls `SetText`. Whatever you copy is
  exactly what Ctrl+V will paste elsewhere.
- **No focus stealing**: the window uses `WS_EX_NOACTIVATE`; automatic appends
  never deactivate the terminal.
- **Topmost only in front of the terminal**: when another app comes to the
  foreground, the window drops `Topmost` and lets itself be covered; it comes
  back on top as soon as you return to the terminal (or click it).
- **Event-based**: clipboard listening via `AddClipboardFormatListener`,
  foreground tracking via `SetWinEventHook` — no polling anywhere.
- **300 ms debounce**: a selection drag produces a single block.
