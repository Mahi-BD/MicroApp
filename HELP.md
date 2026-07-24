# MicroApp — Help

Everything MicroApp does, with every setting and the things that commonly go wrong.
Version 4.2.1.

- [Getting started](#getting-started)
- [Paste as keystrokes](#paste-as-keystrokes)
- [Grab text (OCR)](#grab-text-ocr)
- [Screen capture](#screen-capture)
- [Record GIF](#record-gif)
- [Settings reference](#settings-reference)
- [Where files go](#where-files-go)
- [Troubleshooting](#troubleshooting)

---

## Getting started

MicroApp runs in the notification area. There is no main window and nothing to log into.

- **Left-click** the tray icon → start a paste (pick a target, then click it).
- **Right-click** the tray icon → the menu:

```
Grab text (OCR)
Screen Capture
Record GIF
────────────────
Key Setting
OCR Setting
Capture Setting
GIF Setting
────────────────
About
Exit
```

Only one copy runs at a time. Starting a second one exits silently.

Notifications appear bottom-right for about a second and never take focus. Click one to dismiss it early.

---

## Paste as keystrokes

Some windows refuse a normal paste: VM consoles, remote desktop sessions, KVM/IPMI consoles, "no paste"
password fields. MicroApp types the clipboard character by character instead, so those windows see
ordinary typing.

**How to use it**

1. Copy text anywhere.
2. Click the tray icon, or press **Ctrl+Alt+V**.
3. The pointer turns into a crosshair. Click the window you want it typed into.
4. Press **Esc** at any time to stop typing.

**Typing method** (Key Setting)

| Method | Notes |
|---|---|
| `SendKeys` | The classic Windows path. Fastest, but some apps ignore it. |
| `AutoIt Send` | Handles more keyboard layouts. |
| `SendInput` | Scan codes with an ALT-numpad fallback. Works in VM consoles. **Default.** |

**Delays** — *milliseconds before typing starts* gives the target window time to take focus;
*milliseconds between keystrokes* slows typing down for apps that drop fast input. If characters go
missing, raise the between-keystrokes delay first.

**Safety** — tick *Ask me first when pasting more than N keystrokes* to get a confirmation before a long
paste. The dialog names the target window, so you can back out if you clicked the wrong one.

**Hot key mode** — the hot key can either put you in target-picking mode ("Let me click a target") or
start typing into the current window immediately ("Start typing right away").

---

## Grab text (OCR)

Reads text off the screen: a browser, a scanned PDF, an image, a paused video, a remote desktop, an
error dialog that won't let you select text.

**How to use it**

1. Press **Ctrl+Shift+O**, or tray → *Grab text (OCR)*.
2. The screen freezes and dims, and the pointer becomes a crosshair.
3. Drag over the text. **Esc** or a right-click cancels.
4. The text is delivered per your OCR Setting.

**After capture** options:

- **Copy it to the clipboard** *(default)* — a toast confirms how many characters were copied.
- **Show it in a window first** — an editable preview with **Copy** and **Type it out**.
- **Type it straight into the window I was using** — focus returns to where you were, then it types.

**Keep the original line breaks** — on by default. Turn it off to flow the result into one paragraph,
which is what you usually want when lifting a sentence out of a wrapped column (hyphenated line ends
are rejoined).

**Language** — the list shows the OCR packs Windows has installed. "Use my Windows languages" follows
your Windows language order. To add a language: *Windows Settings → Time & language → Language & region
→ Add a language*, then check that its optional **Optical character recognition** feature is installed.

Small selections are upscaled before recognition, so grabbing a short line of small text still works.

---

## Screen capture

**How to use it**

1. Press **Ctrl+Alt+S**, or tray → *Screen Capture*.
2. Drag over the area. The size badge shows the exact pixel dimensions as you drag.
3. **Esc**, a right-click, or a click without dragging cancels.

**Selection lock** (Capture Setting):

- **Lock ratio** — the box snaps to the chosen shape while you drag: 16:9, 16:10, 4:3, 3:2, 1:1, 21:9,
  9:16, 3:4, or any `W:H` you type. The size stays up to you.
- **Lock pixel size** — the box is exactly the width and height you set, follows the pointer, and a
  single click takes the shot. This overrides lock ratio (a fixed size already fixes the shape).

**After capture** — copy the image to the clipboard, save it as a PNG, or both.

Because the picker works on a frozen copy of the screen, the dimmed overlay never appears in the result,
and nothing on screen can move between aiming and capturing.

---

## Record GIF

**How to use it**

1. Press **Ctrl+Alt+G**, or tray → *Record GIF*.
2. Pick the region (same crosshair, same locks — GIF has its own lock settings).
3. Recording starts. A red **REC 3.2s / 10s** badge appears just outside the recorded area.
4. Stop with **Esc**, by pressing the hot key again, by clicking the badge, or by letting the time
   limit expire.

**Recording** (GIF Setting) — frame rate 1–30 fps (10 by default) and a maximum length in seconds
(10 by default). Higher fps means smoother playback and a bigger file.

**After capture** — the GIF file is always written; you can additionally have the path copied to the
clipboard, or have the file opened in your default viewer.

The mouse pointer is drawn into the frames, since screen copies leave it out.

---

## Settings reference

### Key Setting

| Setting | Default |
|---|---|
| Typing method | SendInput |
| Milliseconds before typing starts | 0 |
| Milliseconds between keystrokes | 15 |
| Ask me first when pasting more than | off, 100 keystrokes |
| Hot key | `Ctrl + Alt + V` |
| When pressed | Let me click a target |

### OCR Setting

| Setting | Default |
|---|---|
| Capture hot key | `Ctrl + Shift + O` |
| Language | Use my Windows languages |
| After capture | Copy it to the clipboard |
| Keep the original line breaks | on |

### Capture Setting

| Setting | Default |
|---|---|
| Screen capture hot key | `Ctrl + Alt + S` |
| Lock ratio | off, 16:9 |
| Lock pixel size | off, 1920 × 1080 |
| After capture | Copy to clipboard |
| Image folder | `Pictures\MicroApp` |

### GIF Setting

| Setting | Default |
|---|---|
| Record GIF hot key | `Ctrl + Alt + G` |
| Frames per second | 10 |
| Seconds at most | 10 |
| Lock ratio | off, 16:9 |
| Lock pixel size | off, 800 × 600 |
| After capture | Just save |
| GIF folder | falls back to the image folder |

Clearing a hot key's key box (Delete or Backspace) disables that hot key.

---

## Starting with Windows

The installer's last page has a **Run MicroApp when Windows starts** checkbox, ticked by default. It
creates a MicroApp shortcut in the Startup folder — for all users when you use the standard installer,
for you alone with the per-user one.

To change your mind later, without reinstalling:

- **Turn it on** — press `Win+R`, type `shell:startup`, and drop a shortcut to `MicroApp.exe` in the
  folder that opens.
- **Turn it off** — delete the MicroApp shortcut from that folder. `shell:common startup` is the
  all-users equivalent.
- Task Manager's **Startup apps** tab can also disable it without deleting anything.

Uninstalling removes the shortcut either way.

## Where files go

- **Screenshots** — `Pictures\MicroApp\MicroApp-YYYYMMDD-HHMMSS.png`, or the folder you set in Capture Setting.
- **GIFs** — `MicroApp-YYYYMMDD-HHMMSS.gif` in the GIF folder; if that is blank, the image folder is used.
- **The app itself** — `C:\Program Files\MicroApp` (standard installer),
  `%LOCALAPPDATA%\Programs\MicroApp` (per-user installer), or wherever you unzipped the portable build.
- **Your settings** — the standard per-user .NET settings file under
  `%LOCALAPPDATA%\MicroApp\...\user.config`. Uninstalling the app does not delete it.

---

## Troubleshooting

**The app won't start: "A referral was returned from the server."**
The build has `uiAccess="true"` in its manifest, which Windows only allows for a signed binary running
from `C:\Program Files`. Either install it there and sign it, or build with `uiAccess="false"` (the
default in this repository), which works everywhere except UAC-elevated windows.

**Nothing happens when I press a hot key.**
Another app already owns that combination — global hot keys are first-come-first-served, and MicroApp
shows a "hot key unavailable" notice at startup when registration fails. Change it in the matching
settings window. Note that Windows itself reserves some combinations (for example `Win+Shift+S`).

**Typing skips or duplicates characters.**
Raise *milliseconds between keystrokes* in Key Setting, and try the `SendInput` method. Remote desktops
and VM consoles usually need 15–30 ms.

**Nothing is typed into an admin window.**
A normal-privilege app cannot send input to an elevated one. Run MicroApp elevated too, or use the
signed + Program Files route described above.

**OCR says "No text found in that selection."**
The text may be too small or too low-contrast. Zoom in first, or select a tighter area. Very light text
on a busy background is the usual failure case.

**The language list is empty / OCR fails immediately.**
Windows has no OCR language pack installed. Add one under *Time & language → Language & region*.

**"Clipboard is busy."**
Another app is holding the clipboard open. MicroApp retries for a moment, then tells you. Try again.

**The GIF is huge.**
Lower the frame rate, shorten the recording, or select a smaller area. GIF has no interframe
compression to speak of — file size scales with area × frames.

**A second copy won't start.**
By design: one instance at a time. If no window and no tray icon are visible, check Task Manager for a
stale `MicroApp.exe` and end it.

---

Questions, bugs, ideas: **Samsur Rahman Mahi** — [mahi@rampsbd.com](mailto:mahi@rampsbd.com)
