# Notice

MicroApp is a fork of **ClickPaste** by Collective Software LLC
(<https://github.com/Collective-Software/ClickPaste>), used under the BSD 3-Clause license reproduced
in [LICENSE](LICENSE). The original copyright notice is retained there in full.

Upstream provided the clipboard-to-keystrokes engine (`SendKeys` / AutoIt / `SendInput` typing,
the tray target picker, and the hot key manager).

Added in MicroApp, © 2026 Samsur Rahman Mahi, under the same BSD 3-Clause terms:

- Screen text recognition (OCR) built on `Windows.Media.Ocr`, with a preview window.
- Screen region capture to clipboard or PNG, with locked-ratio and locked-pixel selection.
- Animated GIF recording, including an incremental GIF89a encoder and the recording badge.
- A redesigned interface: light/dark theme, custom-drawn controls, four settings windows, an About
  window, and a one-second notification toast.
- New application and tray icons.

## Third-party components

| Component | License |
|---|---|
| AutoItX3 (AutoIt) | See `AutoIt_License.html` |
| MouseKeyHook | MIT |
| Microsoft.Windows.SDK.Contracts | Microsoft Software License Terms |
