# Changelog

## 4.6.0 — 2026-08-01

### Added

- **An Archive window.** Archived notes used to be hidden behind a *Show archived notes* toggle that
  mixed them back into the main list. They now have their own window, reached from the archive button
  in the notes toolbar: newest first with the date and time on every row, a **search box** in the top
  right that matches note names *and* their contents as you type, double-click to open, and
  **Unarchive** to put one back where it was. Archiving still changes nothing on disk.
- **Note Setting redesigned** into two columns. It had grown past the bottom of a laptop screen; it
  now fits without scrolling.
- **Note sync across PCs — optional, and off unless you turn it on.** Notes remain ordinary `.txt`
  files on one PC by default, needing no account and no network. *Set up sync* in Note Setting opens a
  wizard whose first choice is *Just this PC*; pick either of the other two and the notes are mirrored
  to a database so they turn up on every PC you use. *Disconnect* puts it back to local-only at any
  time, leaving every note in place. Pins, archive flags, colours and the drag order
  travel with them, and a note deleted on one PC is deleted on the others.
- **The database is one you own.** MicroApp ships with no project of its own — the wizard walks
  through making a free Firebase project under your own Google account (it copies the security rules
  to the clipboard and opens the console for you). The notes go straight from your PC to your
  project; they never pass through anyone else's account.
- **No account to invent.** There is no email address or password to make up: MicroApp creates its
  own sign-in inside your project. The first PC ends up with a **sync code**; every PC after that
  pastes that one code and is done. Note Setting can show the code again to add another PC later.
- **Settings now survive an upgrade.** Windows keeps .NET settings in a per-version folder, so every
  previous version bump quietly reset hot keys and API keys back to defaults. The first run of a new
  build now carries the old settings across.

### Fixed

- **Archiving, pinning, recolouring or reordering a note no longer undoes itself when sync is on.**
  None of those touch the note's `.txt` file, so the sync had no reason to send them up — but it
  still pulled the old flags back down over them, and within three minutes an archived note
  reappeared in the list. Decoration now carries its own timestamp (a fifth field in `.notes-meta`)
  and syncs on that, independently of the note's text. Sidecars written by older versions still load;
  they simply start with no timestamp.

The `.txt` files stay the source of truth and everything works offline; a sync runs a few seconds
after a change and every three minutes otherwise, newest copy wins. The sign-in is sealed to the
Windows account with DPAPI, so copying `user.config` to another PC does not carry it. `.sync-log` in
the Notes folder holds the last 60 syncs.

## 4.5.0 — 2026-07-31

### Added

- **Right-click a note in the list** for *Open*, **Pin to top**, **Archive**, a **Colour** submenu and
  *Delete*.
- **Pinned notes** stay at the top of the list with a pin marker, however the rest is ordered.
- **Archived notes** drop out of the list without being deleted. *Show archived notes* brings them
  back, dimmed and marked, where *Restore from archive* puts one back.
- **Drag notes into the order you want.** Grab a row, an accent line shows where it will land, and
  the order sticks — it is remembered between sessions. New notes still arrive at the top.
- **A colour per note.** Every note gets its own colour automatically, shown as a bar down the left
  of its row with a matching tint; pick a different one from *Colour* (eight colours, or back to
  Automatic).

Pins, archive flags, colours and the manual order live in a small `.notes-meta` file inside the
Notes folder. The notes themselves stay plain `.txt`, and deleting that file only loses the
decoration.

## 4.4.1 — 2026-07-31

### Fixed

- **Text no longer runs off the right edge of a note.** The last characters of a line could be cut
  off — hidden behind the scrollbar strip — in notes short enough not to need a scrollbar. Lines now
  wrap at the same place whether or not the note scrolls.

## 4.4.0 — 2026-07-31

### Added

- **Bangla phonetic typing in notes** — click **E / ক** on the note toolbar (or press
  **Ctrl+Shift+L**) and type Bangla the way it sounds: `ami` offers আমি, `bhalo` offers ভালো. A
  suggestion list appears under the word — **↑ ↓** to move, **Enter**, **Tab** or **Space** to
  pick, **Esc** to dismiss — and `.` becomes দাঁড়ি (।) while digits become ০–৯. It uses the
  [string.bd](https://string.bd) dictionary, so it needs a free API token in Note Setting; nothing
  else about notes goes online.
- **Ask AI box under every note** — type an instruction ("rewrite this as a Facebook post",
  "translate to English", "make it formal") and press Enter. **Select text first and only that part
  is rewritten**; with nothing selected the whole note is.
- **Right-click a word to translate it** — an English word offers Bangla from the string.bd
  dictionary, a Bangla word offers English from your AI provider. Click one and it replaces the
  word. Right-clicking a selection translates the whole selection.
- **OpenRouter** as an AI provider, alongside MiMo, Gemini and ChatGPT.
- **Undo / Redo and text-size buttons on the note toolbar** — undo and redo also on **Ctrl+Z** and
  **Ctrl+Y**, and **A- / A+** step the note font between 8 and 28 pt. The size is remembered and
  applies to every open note.

### Changed

- **Notes open in front.** A note opened with the hot key now comes up over whatever you were
  working in, instead of behind it.
- **Notes now use Nirmala UI** instead of Consolas. Consolas has no Bengali letters, so mixed
  English and Bangla text used to render at two visibly different sizes; now it matches.
- **The note editor has the same slim scrollbar as the notes list** instead of the fat Windows one.
- **New toolbar icons throughout Notes** — the hand-drawn glyphs are gone in favour of the Fluent
  icon set Windows itself uses.
- **Note Setting** gained the string.bd token and the OpenRouter provider without growing.

### Fixed

- **Windows display scaling (125%, 150%, …) no longer breaks the settings windows.** Their layouts
  are drawn at fixed pixel positions, so at a scale other than 100% they used to overlap; every
  fixed-size window now scales as a whole and stays pixel-perfect. Screen capture, OCR and the
  recording overlays keep working in true screen pixels.
- Asking the AI to rewrite a selected line no longer swallows the line break after it.

## 4.3.5 — 2026-07-30

### Added

- **Notes** (default **Ctrl+Shift+N**) — a quick scratch pad. Every press of the hot key opens a
  fresh note; each note is one window backed by one plain `.txt` file that saves itself as you type
  (under `Notes\` next to the exe, or `%AppData%\MicroApp\Notes` when that isn't writable). The
  window title follows the first line of the note. Notes come with:
  - a toolbar: new note, all notes, strip spaces, join lines, insert date / long date / timestamp
    (three configurable formats with live previews in Note Setting);
  - **spell check** with red squiggles as you type (Windows' own spell checker; English, plus
    Bangla when a Bangla dictionary is installed — Bangla words are never sent to the English
    checker), right-click for suggestions and *Add to dictionary*;
  - a **Grammar** button that fixes spelling and grammar with AI — MiMo, Gemini or ChatGPT, using
    your own API key set in Note Setting (English, Bangla or mixed; nothing is sent anywhere unless
    you click the button);
  - an **All notes** browser — newest first with a first-line preview, a slim scrollbar, click to
    select, click again (or Enter) to open, plus New note / Open / Delete and icon buttons to
    close all open notes or delete every note. The window remembers its size and position;
  - a **Hide note windows from the taskbar** switch (on by default) so a pile of open notes does
    not flood the taskbar;
  - notes use Notepad's font (Consolas 11).
- **Pick Text** (default **Ctrl+Alt+T**) — a text picker that works like a colour picker: a **+**
  crosshair with a live preview of the text under it; one click grabs the element's exact text
  through UI Automation — no OCR, character-perfect, multi-line. If the clicked element has no text
  of its own, the texts inside it are gathered one per line. The click is swallowed so the app
  underneath is never activated; password fields are never read. Delivery follows the OCR Setting
  (clipboard by default).
- **A red frame around the recorded region** while video records — so it is always clear what is
  being filmed. It sits just outside the recording, is click-through, and turns grey while paused.

### Changed

- **Video recording no longer has a time limit** — it runs until you save it. The *seconds at
  most* setting is gone.
- **Pause and save on the recording badge** — the badge now carries a pause/resume button and a
  save button. Paused stretches are simply absent from the file: no frames, no sound, no gap.
  The badge shows PAUSED and the timer freezes while paused. Esc still stops and saves.
- **Hot keys act on the key press, not the release** — the crosshair (paste, OCR, capture, GIF,
  video, pick text) now appears the moment the combination goes down, while the keys are still
  held. Typing itself still waits until every modifier is released, so held keys can never corrupt
  the injected keystrokes.

### Fixed

- **Paste as keystrokes now types Bangla and every other script correctly.** Characters the active
  keyboard layout cannot produce were being mapped through whatever other layout was installed, and
  the target read those key codes in its own layout — Bangla (and Hindi, Arabic, …) came out as the
  wrong characters. Such characters are now injected directly as Unicode. (Hardware VM/IPMI
  consoles that ignore Unicode input still receive only what their layout can express.)
- **Video recordings could vanish on some machines** — on PCs where the Windows video encoder
  refuses to be shared between threads, every frame write failed (E_NOINTERFACE) and the empty
  file was deleted, so recordings silently never saved. The encoder now lives entirely on the
  recording thread, which is safe everywhere. If the encoder ever fails before the first frame,
  MicroApp now says exactly why instead of just "Nothing was recorded".
- **Video Setting layout (again)** — the Selection lock rows were cramped together; they now use
  the same spacing as the other settings windows.

## 4.3.4 — 2026-07-27

### Fixed

- **Corrupted video recordings** — recording now always uses the H.264 encoder built into
  Windows instead of the GPU vendor's encoder, which produced broken files on some machines
  when fed screen frames. Frame timestamps are also guaranteed to strictly increase, and if
  the encoder ever fails mid-recording MicroApp now says so and still finalises the frames
  it managed to write, instead of silently reporting a saved-but-unplayable file.

## 4.3.3 — 2026-07-27

### Fixed

- **Video Setting layout** — the card descriptions overlapped the *HELD WITH* label and the
  *Just save* row; the rows now sit where they do in the other settings windows.

## 4.3.2 — 2026-07-27

### Added

- **Record Video** — record any screen region to a small MP4 (H.264 + AAC), with sound from the
  system output or the microphone. Works exactly like GIF recording: a tray item or its own hot key
  (default Ctrl + Alt + R), drag a region, a REC badge outside the frame, Esc or the hot key again to
  stop. Encoding uses the Media Foundation encoders built into Windows, streamed straight to disk, so
  a minute of screen costs megabytes rather than the hundreds a GIF would — and nothing new is
  bundled. Video Setting has its own frame rate, length limit, file-size/quality trade, sound source,
  selection lock and folder (defaults to Videos\MicroApp). When the system plays nothing, the sound
  track is padded with silence so picture and audio stay in step. On audio-less machines the
  recording simply has no sound track; on Windows N the Media Feature Pack is required.

- **Hot keys shown in the tray menu** — *Grab text (OCR)*, *Screen Capture*, *Record GIF* and
  *Record Video* now display their current hot key next to the item, and the labels update as soon
  as a settings window is closed. Cleared hot keys show nothing.

## 4.2.2 — 2026-07-26

### Added

- **Take a hot key over from another app** — when a combination is already registered elsewhere,
  MicroApp now asks instead of only reporting the failure. Answering *Yes, use it here* claims the key
  through a low-level keyboard hook: MicroApp acts on it and swallows it, so the app holding the
  registration stops receiving it. The answer is remembered per combination, and MicroApp returns to a
  normal registration once the combination is free again.

## 4.2.1 — 2026-07-25

First release under the MicroApp name. Everything below is new relative to the ClickPaste fork point.

### Added

- **Grab text (OCR)** — drag over any part of the screen and read the text under it, using the OCR
  engine built into Windows. Output to the clipboard, to an editable preview window, or typed into the
  window you came from. Language picker, and an option to keep or flow line breaks.
- **Screen capture** — freeze-and-drag region capture to the clipboard, to a PNG, or both.
- **Selection lock** — constrain the capture box to a preset aspect ratio, or to an exact pixel size
  that follows the pointer and takes the shot on a single click.
- **Record GIF** — record a screen region as an animated GIF, with its own hot key, frame rate, length
  limit, selection lock and output folder. Frames stream to disk while recording; a red badge outside
  the recorded area shows elapsed time and stops on click.
- **About window** with author and contact details.
- **Installer** (`Setup\nsis\MicroApp.nsi`) with a **Run MicroApp when Windows starts** checkbox,
  Start Menu and optional desktop shortcuts, an uninstaller, Add/Remove Programs entry, and silent
  switches (`/S`, `/STARTUP`). Builds for all users (Program Files) or per user (no admin needed).

### Changed

- Complete interface redesign: light/dark theme that follows Windows, custom-drawn cards, buttons,
  radios, checkboxes and inputs, and a new icon set.
- Settings split into four focused windows — Key, OCR, Capture and GIF — all on the same 640 × 612
  canvas, reachable from the tray menu.
- Tray menu rebuilt on `ContextMenuStrip` and themed; "Settings" is now "Key Setting".
- Notifications replaced tray balloons with a one-second toast that never takes focus.
- Confirmation and error dialogs replaced `MessageBox` with a themed dialog.
- Release builds only invoke `sign.bat` when code signing is configured
  (`/p:SkipCodeSigning=true` skips it), so CI can build Release.
- Default manifest is `uiAccess="false"`, so unsigned builds run from any folder.

### Project

- Renamed from ClickPaste to MicroApp: assembly, namespace, solution, project and installer.
