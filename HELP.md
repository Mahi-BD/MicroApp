# MicroApp — Help

Everything MicroApp does, with every setting and the things that commonly go wrong.
Version 4.7.5.

- [Getting started](#getting-started)
- [Paste as keystrokes](#paste-as-keystrokes)
- [Grab text (OCR)](#grab-text-ocr)
- [Pick Text](#pick-text)
- [Screen capture](#screen-capture)
- [Record GIF](#record-gif)
- [Record Video](#record-video)
- [Notes](#notes)
- [Settings reference](#settings-reference)
- [Where files go](#where-files-go)
- [Troubleshooting](#troubleshooting)

---

## Getting started

Installing, silent-install switches, upgrading and uninstalling are covered separately in
**[SETUP.md](SETUP.md)**.

MicroApp runs in the notification area. There is no main window and nothing to log into.

- **Left-click** the tray icon → start a paste (pick a target, then click it).
- **Right-click** the tray icon → the menu:

```
Grab text (OCR)
Pick Text
Screen Capture
Record GIF
Record Video
New Note
────────────────
Key Setting
OCR Setting
Capture Setting
GIF Setting
Video Setting
Note Setting
────────────────
About
Exit
```

Global hot keys act the moment the combination is pressed — the crosshair appears while the keys
are still held down.

MicroApp's windows are laid out in fixed pixels and scale as a whole, so they stay correct at any
Windows display scaling (100%, 125%, 150%, …), while capture, OCR and the recording overlays keep
working in true screen pixels.

Only one copy runs at a time. Starting a second one exits silently.

Notifications appear bottom-right for about a second and never take focus. Click one to dismiss it early.

---

## Paste as keystrokes

Some windows refuse a normal paste: VM consoles, remote desktop sessions, KVM/IPMI consoles, "no paste"
password fields. MicroApp types the clipboard character by character instead, so those windows see
ordinary typing. Text in any script is typed correctly — characters the keyboard layout cannot produce
(Bangla, Hindi, Arabic, CJK, …) are injected directly as Unicode. The one exception is hardware
VM/IPMI consoles that ignore Unicode input; those can only receive what their own layout can express.

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

## Pick Text

Grabs the **exact** text of a control — like a colour picker, but for text. No OCR: it asks the
control itself (through Windows UI Automation), so the result is character-perfect and keeps every
line, however long the text is. Works on labels, edit boxes, lists, buttons, message bodies — anything
that exposes its text to Windows. Password fields are never read.

**How to use it**

1. Press **Ctrl+Alt+T**, or tray → *Pick Text*.
2. The pointer becomes a **+** crosshair. As you move, the element under it is outlined and a small
   card previews the text it holds.
3. **Click** to take that text. **Esc** or a right-click cancels; the click never reaches the app
   underneath, so you won't accidentally press what you're picking from.
4. The text is delivered per your OCR Setting: clipboard *(default)*, preview window, or typed out.

If the element you click carries no text of its own, MicroApp gathers the texts of everything inside
it (one line each) — handy for lifting a whole list or dialog at once. Prefer *Grab text (OCR)* for
images, videos and remote desktops, where there is no real text behind the pixels.

---

## Screen capture

**How to use it**

1. Press **Ctrl+Alt+S**, or tray → *Screen Capture*.
2. Drag over the area. The size badge shows the exact pixel dimensions as you drag.
3. Let go and the frame stays put so you can get it right:
   - **drag inside it** to move the whole frame — a four-way arrow appears in the middle while the
     pointer is over it,
   - **drag a handle** — the corners, and the edge middles once the frame is big enough — to resize it,
   - **arrow keys** nudge it a pixel at a time, **Ctrl+arrows** ten, **Shift+arrows** resize instead,
   - drag anywhere outside to start over.
4. **Enter**, a double-click inside the frame, or the tick button takes the shot.
5. **Esc**, a right-click, the cross button, or a click without dragging cancels.

**Delay** (Capture Setting) — *seconds before it grabs*, 0 by default, which takes the shot the moment
you confirm the frame. Set it to a few seconds and the picker gets out of the way instead: the frame
stays outlined, a badge counts down beside it, and you have that long to open the menu, tooltip or
hover state you are trying to photograph. Neither the outline nor the badge takes the focus — so the
menu you open stays open — and both are gone before the picture is taken. Click the badge to call it
off. Unlike an immediate capture, which uses the frozen copy made when the picker opened, a delayed
one reads the screen fresh at the end of the count, which is the whole point.

**Selection lock** (Capture Setting):

- **Lock ratio** — the box snaps to the chosen shape while you drag: 16:9, 16:10, 8:5, 4:3, 3:2, 1:1,
  21:9, 9:16, 3:4, or any `W:H` you type. The size stays up to you, and the shape survives every later
  move and resize.
- **Lock pixel size** — the box is exactly the width and height you set, follows the pointer, and a
  single click takes the shot. This overrides lock ratio (a fixed size already fixes the shape), and
  there is nothing to adjust afterwards.

**After capture** — copy the image to the clipboard, save it as a PNG, or both.

Because the picker works on a frozen copy of the screen, the dimmed overlay never appears in the result,
and nothing on screen can move between aiming and capturing.

---

## Record GIF

**How to use it**

1. Press **Ctrl+Alt+G**, or tray → *Record GIF*.
2. Pick the region (same crosshair, same locks — GIF has its own lock settings). Adjust the frame
   the same way as a screen capture — move it, resize it from the handles — and press **Enter** or
   the tick to start.
3. Recording starts. A red **REC 3.2s / 10s** badge appears just outside the recorded area.
4. Stop with **Esc**, by pressing the hot key again, by clicking the badge, or by letting the time
   limit expire.

**Recording** (GIF Setting) — frame rate 1–30 fps (10 by default) and a maximum length in seconds
(10 by default). Higher fps means smoother playback and a bigger file.

**After capture** — the GIF file is always written; you can additionally have the path copied to the
clipboard, or have the file opened in your default viewer.

The mouse pointer is drawn into the frames, since screen copies leave it out.

---

## Record Video

Like Record GIF, but the result is a small **MP4 (H.264 + AAC)** — a minute of screen costs
megabytes rather than the hundreds a GIF would — and it can include **sound**.

**How to use it**

1. Press **Ctrl+Alt+R**, or tray → *Record Video*.
2. Pick the region (same crosshair, same locks — video has its own lock settings). Adjust the frame
   the same way as a screen capture — move it, resize it from the handles — and press **Enter** or
   the tick to start.
3. Recording starts, with a red **REC** badge outside the recorded area. The badge has a
   **pause/resume** button — paused stretches are left out of the file entirely — and a **save**
   button that stops the recording and keeps the MP4.
4. Recording has **no time limit**: it runs until you save it, press **Esc**, or press the hot
   key again.

**Recording** (Video Setting) — frame rate 1–30 fps (20 by default), a **quality** choice (*Small file* / *Balanced* / *Sharp*) that trades file size
against picture crispness, and a **sound** source: *No sound*, *System sound* (whatever the machine
is playing) or *Microphone*.

Encoding uses the H.264 and AAC encoders built into Windows — nothing extra is installed, and the
video streams to disk while it records. If no audio device is available the recording is silently
made without a sound track. On Windows *N* editions the Media Feature Pack must be installed.

**After recording** — the MP4 is always written (to `Videos\MicroApp` unless you pick another
folder); you can additionally have the path copied or the file opened in your default player.

While recording, a thin **red frame** marks the recorded region (grey while paused). It sits just
outside the recording and is click-through, so it never appears in the video and never gets in
the way.

---

## Notes

A scratch pad that is always one hot key away. Every press of **Ctrl+Shift+N** (or tray → *New
Note*) opens a **fresh** note — older ones come back through the note list.

A new note opens **in front** of whatever you were working in.

Each note is one window backed by one plain `.txt` file. There is no save button: the file follows
your typing with less than a second of lag. Close the window and the note is on disk; close an empty
note and its file is removed. The window title always shows the note's first line. Note windows stay
**off the taskbar** by default (a switch in Note Setting brings them back).

The text is **fixed-width**, the way Notepad is: every character takes the same space, so a pasted
Markdown table, a log or a block of code lines up column for column instead of drifting. Bangla is
drawn in **Nirmala UI** wherever it appears, since no fixed-width face carries Bengali — a note that
mixes the two keeps its English lined up and its Bangla properly joined. Turn *Fixed-width text* off
in Note Setting to put the whole note back in Nirmala UI. **A-** and **A+** on the toolbar step the
size between 8 and 28 pt; the choice is remembered and applies to every open note at once.

**The toolbar** — new note · all notes · remove every space · join all lines · insert date · insert
long date · insert timestamp · **undo** · **redo** · **A-** · **A+** · **E / ক** (Bangla) ·
**Grammar** · settings. The three date/time formats are configurable in Note Setting, each with a
live preview. Undo and redo also answer to **Ctrl+Z** and **Ctrl+Y**.

**Spell check** — misspellings get a red squiggle as you pause typing, using the spell checker built
into Windows. English is always checked; Bangla words are checked only when a Bangla dictionary is
installed, and are never sent to the English checker. Right-click a squiggled word for suggestions
and *Add to dictionary*.

**Bangla phonetic typing** — click **E / ক** on the toolbar or press **Ctrl+Shift+L**, and type
Bangla the way it sounds. A list of candidates appears under the word as you type: `ami` → আমি,
`bhalo` → ভালো, `bangla` → বাংলা.

| Key | Does |
|---|---|
| `↑` `↓` | move through the candidates |
| `Enter` or `Tab` | take the highlighted one |
| `Space` | take the highlighted one and type the space |
| `Esc` | dismiss the list (a second `Esc` closes the note) |
| `.` | types দাঁড়ি (।) |
| `0`–`9` | type ০–৯ |

Typing punctuation or any other separator also accepts the highlighted candidate, so ordinary typing
just works. Press **Ctrl+Shift+L** again for English — you can switch mid-sentence.

This looks words up in the **[string.bd](https://string.bd)** dictionary, so it needs an API token
(free from string.bd) in Note Setting. Lookups are cached, so repeated words convert instantly even
when you type quickly.

![Bangla phonetic typing](docs/note-bangla.png)

**Grammar (AI)** — one click sends the note to the AI service you configured and replaces the text
with the corrected version (English, Bangla or mixed — the language is preserved). Four providers:
**MiMo**, **Gemini**, **ChatGPT**, **OpenRouter**. Each needs your own API key, set in Note Setting;
for MiMo the base URL is also configurable (Token Plan subscriptions get a regional URL from the MiMo
console). Nothing ever leaves your machine unless you press the button.

**Ask AI** — the box under the note takes an instruction in plain words and applies it to the note:
*rewrite this as a Facebook post*, *translate to English*, *make it shorter*, *turn this into bullet
points*. Press Enter or click the send button.

**Select text before you ask and only that part is rewritten** — the rest of the note is left exactly
as it was, and the new text stays selected so you can refine it again.

**Translate a word** — right-click any word for its translation: an **English** word offers Bangla
from the string.bd dictionary, a **Bangla** word offers English from your AI provider. Pick one and it
replaces the word. Right-clicking inside a selection translates the whole selection instead of one
word.

**All notes** (toolbar list button) — every note with a first-line preview and a slim scrollbar.
**Click** a note to select it, **click it again** (or press Enter, or double-click) to open it. The
footer has *New note*, two icon buttons — **close all open notes** and **delete all notes** — and
*Delete* / *Open* for the selected one. The window remembers its size and position.

Each note carries its **own colour**, shown as a bar down the left of its row with a matching tint.
The colour is picked automatically so a full list is easy to scan; *Colour* on the right-click menu
sets a specific one (eight to choose from) or puts it back to *Automatic*. The **colour button on the
note's own toolbar** does the same from inside the note, and shows the colour that note is wearing.

Whichever colour you pick last becomes the colour **new notes on this PC start in**. That is a setting
on this machine rather than part of a note, so it is not synced and each PC can differ; *Automatic*
goes back to picking a colour from the note's name.

**Search** with the box across the top of the list: it matches a note's file name and its title — the
first line, which is the bold text on its row. (The Archive's box additionally searches inside notes.)

**Right-click a note** for:

| Item | Does |
|---|---|
| **Open** | Same as double-clicking it |
| **Pin to top** | Keeps the note at the top of the list, with a pin marker, whatever the order |
| **Archive** | Takes the note out of the list, without deleting it, and puts it in the Archive window |
| **Colour** | Picks the note's colour, or *Automatic* |
| **Delete** | Deletes that note |
| **Archive…** | Opens the Archive window |

#### The Archive

Archived notes leave the main list and live in their own window — the **archive button** in the notes
toolbar, or *Archive…* on the right-click menu. They are listed **newest first** by the time each was
last written, with the date and time on every row.

- **Search** — the box in the top right filters as you type, matching both the note's name and
  everything written inside it.
- **Double-click** a row (or *Open*) to open the note as usual; it stays archived.
- **Unarchive** — the button, or the right-click item, puts a note back in the main list with the
  colour and place it had before.
- **Delete** works the same as in the main list.

Archiving changes nothing on disk: the `.txt` file stays exactly where it was, and *archived* is one
flag in `.notes-meta`.

**Drag to reorder** — grab any row and drag it up or down; an accent line shows where it will land.
The order you build is remembered between sessions, pinned notes always float above the rest, and a
brand-new note still arrives at the top. (The Archive is always in date order, so rows there do not
drag.)

Pins, archive flags, colours and the manual order are kept in a small `.notes-meta` file inside the
notes folder, each with the time it last changed so sync can tell a fresh setting from a stale one. The notes themselves stay plain `.txt` files — delete `.notes-meta` and you only lose
the decoration, never a note.

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

### Video Setting

| Setting | Default |
|---|---|
| Record video hot key | `Ctrl + Alt + R` |
| Frames per second | 20 |
| Quality | Balanced |
| Sound | System sound |
| Lock ratio | off, 16:9 |
| Lock pixel size | off, 1280 × 720 |
| After recording | Just save |
| Video folder | `Videos\MicroApp` |

### Note Setting

| Setting | Default |
|---|---|
| New note hot key | `Ctrl + Shift + N` |
| Hide note windows from the taskbar | on |
| Fixed-width text, like Notepad | on |
| Date format | `yyyy-MM-dd` |
| Long date format | `dddd, dd MMMM yyyy` |
| Timestamp format | `yyyy-MM-dd HH:mm:ss` |
| Text size | 11 pt |
| AI provider | MiMo |
| Model | `mimo-v2.5` |
| Base URL (MiMo) | the MiMo Token Plan endpoint |
| API key | *(empty — set your own)* |
| Bangla token (string.bd) | *(empty — set your own)* |
| Sync | *(off — set up in the wizard)* |

Both AI keys are stored in your own user settings file (see *Where files go*) and are never included
in the installer or the portable download. Switching provider fills in that provider's usual model
name; the base URL box only applies to MiMo.

#### Sync

**Sync is optional and off until you turn it on.** Out of the box notes are ordinary `.txt` files in a
folder on this PC and nothing leaves it — that mode is fully supported and needs no account, no
network and no setup. Everything below only applies if you want the same notes on more than one PC.

If you do, the database is **a Firebase project you create and own** — nothing ships with the app, and
the notes never pass through anyone else's account. **Set up sync** in Note Setting opens a wizard
whose first page includes *Just this PC*, so staying local is a choice you can make (or come back to)
rather than the absence of one. There is no address or password to invent: MicroApp makes its own
sign-in inside your project.

**On the first PC** the wizard walks through making the project. It is free (Firebase Spark plan, no
card):

1. Create a project at [console.firebase.google.com](https://console.firebase.google.com).
2. Build → Firestore Database → Create database (Standard, Native mode, a region near you).
3. Rules tab → paste the rules from the wizard (**Copy rules**) → Publish.
4. Build → Authentication → Get started → Email/Password → Enable.
5. Project settings → General → Your apps → Web app. The snippet holds `projectId` and `apiKey`.
6. Put those two into the wizard and press **Create and connect**.

It then shows a **sync code**. **On every other PC**, run the wizard, choose *I have a sync code*,
paste it, and that is the whole setup — the code carries the project, the key and the sign-in.

Anyone holding the code can read the notes, so pass it across the way you would a password. Note
Setting shows it again later under **Sync code**.

**Going back to local-only** at any time: **Disconnect** in Note Setting (or *Just this PC* in the
wizard). The notes already on the PC stay exactly where they are and keep working as plain files; the
copies in your database stay there too until you delete them yourself.

The `.txt` files on disk stay the source of truth, so notes work with no network. A sync runs a few
seconds after a change, and each PC checks the others every 15 seconds — cheaply, by reading one small
marker document and only fetching the notes when it says something changed. The newer copy of a note
wins, judged on the database's clock rather than each PC's, and a note deleted on one PC is deleted on
the others. Pins, colours, archive flags and the manual order travel with the notes. `.sync-log` in the Notes folder records the last 60 syncs
if something looks wrong.

Clearing a hot key's key box (Delete or Backspace) disables that hot key.

### When another app already owns the hot key

Global hot keys are first-come-first-served: whichever app registered the combination first keeps it.
When MicroApp finds one taken it asks

> **Ctrl + Alt + V is already registered by another application.**
> Use it for MicroApp instead? MicroApp will see the key first, and the other app will stop receiving it.

Answer **Yes, use it here** and MicroApp takes the combination over — it watches the keyboard directly,
acts on the key, and swallows it, so the app holding the registration no longer sees it. **Leave it**
leaves that MicroApp hot key switched off; the tray menu still works, and you can pick a different key
in the settings window.

The answer is remembered for that exact combination, so you are asked once, not at every start. Change
the key and the next conflict asks again. If the other app is closed later, MicroApp goes back to a
normal registration on its own.

Two things a take-over cannot do: combinations Windows reserves for itself (`Ctrl+Alt+Del`, `Win+L`,
`Win+Shift+S`) never reach any application, and an unsigned build outside `C:\Program Files` cannot
take a key while a UAC-elevated window has the focus.

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
- **Videos** — `MicroApp-YYYYMMDD-HHMMSS.mp4` in `Videos\MicroApp`, or the folder you set in Video Setting.
- **Notes** — `Note-YYYYMMDD-HHMMSS.txt` in a `Notes` folder next to `MicroApp.exe` when that is
  writable (portable use), otherwise under `%AppData%\MicroApp\Notes`. Pins, archive flags, colours
  and the manual order sit beside them in `.notes-meta`; sync keeps `.sync-log` and `.notes-deleted`
  in the same folder.
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
Another app already owns that combination — global hot keys are first-come-first-served. MicroApp
offers to take it over when it finds one taken (see *When another app already owns the hot key*); if
you answered **Leave it**, that hot key stays off until you pick a different key in the matching
settings window. Note that Windows itself reserves some combinations (for example `Win+Shift+S`) and
those cannot be taken over by anything.

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

**Bangla typing shows no suggestions.**
Notes needs a string.bd API token — set it in Note Setting. If the token is there, check that the
machine can reach `string.bd`; MicroApp never blocks your typing on a lookup, so a failed one simply
leaves the word as you typed it.

**The Grammar button, Ask AI or a Bangla→English translation reports an error.**
The message comes straight from the AI provider. The usual causes are an empty or wrong API key, a
model name that provider does not serve, or no credit left on the account. MiMo Token Plan keys
(`tp-…`) also need the regional base URL from the MiMo console.

**The GIF is huge.**
Lower the frame rate, shorten the recording, or select a smaller area. GIF has no interframe
compression to speak of — file size scales with area × frames.

**A second copy won't start.**
By design: one instance at a time. If no window and no tray icon are visible, check Task Manager for a
stale `MicroApp.exe` and end it.

---

Questions, bugs, ideas: **Samsur Rahman Mahi** — [mahi@rampsbd.com](mailto:mahi@rampsbd.com)
