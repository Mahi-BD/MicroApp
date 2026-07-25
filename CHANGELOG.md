# Changelog

## Unreleased

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
