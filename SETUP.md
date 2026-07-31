# Setup

How to install MicroApp, set it up the first time, upgrade it and remove it again.

For what each feature does, see **[README.md](README.md)**; for the full reference and
troubleshooting, **[HELP.md](HELP.md)**.

---

## Requirements

- **Windows 10 (1809 or newer) or Windows 11**, 64-bit.
- **.NET Framework 4.8** — already part of both, nothing to install.
- Nothing else. No account, no service, no runtime download.

Optional, only if you want them:

- an **OCR language pack** for *Grab text* — *Windows Settings → Time & language → Language & region*;
- an **AI provider key** for the Grammar and Ask AI buttons in Notes;
- a **[string.bd](https://string.bd) API token** for Bangla phonetic typing.

---

## Pick a download

All three are on the [latest release](https://github.com/Mahi-BD/MicroApp/releases/latest) page.

| Download | Installs to | Needs admin | Use it when |
|---|---|---|---|
| `MicroApp-<version>-setup.exe` | `C:\Program Files\MicroApp` | yes | It is your own PC and you want it for every user |
| `MicroApp-<version>-peruser-setup.exe` | `%LOCALAPPDATA%\Programs\MicroApp` | no | You cannot run installers as administrator |
| `MicroApp-<version>-win-x64.zip` | wherever you unzip it | no | Portable — a USB stick, a locked-down machine, or just trying it out |

All three contain the same application. The installers add Start-menu shortcuts, an optional desktop
shortcut, an entry in *Apps & features*, and can start MicroApp with Windows.

The files are not code-signed, so SmartScreen may show *Windows protected your PC* — choose **More
info → Run anyway**, or use the portable zip, which does not trigger it.

---

## Install

### With the installer

1. Run the setup `.exe` and follow the pages: licence, install folder, optional desktop shortcut.
2. The last page has **Run MicroApp when Windows starts** — tick it and a shortcut goes into your
   Startup folder.
3. Finish. MicroApp appears in the **notification area** (the tray, bottom-right). There is no main
   window — everything is on the tray icon's right-click menu.

### Portable

Unzip `MicroApp-<version>-win-x64.zip` anywhere and run `MicroApp.exe`. Keep the DLLs next to it.
Notes are saved in a `Notes` folder beside the exe when that folder is writable, so a portable copy
keeps its notes with it.

### Silent install (for IT)

```
MicroApp-<version>-setup.exe /S                      ; silent, default folder
MicroApp-<version>-setup.exe /S /STARTUP             ; silent + start with Windows
MicroApp-<version>-setup.exe /S /D=C:\Tools\MicroApp ; silent, custom folder
```

`/D=` must come last and takes an unquoted path. The per-user installer accepts the same switches and
needs no elevation.

---

## First run

MicroApp works out of the box: press **Ctrl+Alt+V** to type your clipboard somewhere, **Ctrl+Shift+O**
to read text off the screen, **Ctrl+Shift+N** for a note. The
[default hot keys](README.md#default-hot-keys) are all editable.

Two optional things are worth setting up while you are here — both live in **tray → Note Setting**,
and both are used only when you ask for them.

### AI (Grammar, Ask AI, Bangla → English)

1. Get a key from your provider: [MiMo](https://mimo.mi.com),
   [Gemini](https://aistudio.google.com), [ChatGPT](https://platform.openai.com) or
   [OpenRouter](https://openrouter.ai).
2. Tray → **Note Setting** → pick the **provider**, paste the **API key**.
3. The **model** box fills in with that provider's usual model; change it if you use another one.
4. **MiMo only:** Token Plan keys (`tp-…`) need the regional **base URL** from the MiMo console. The
   box is enabled when MiMo is selected.

### Bangla phonetic typing

1. Get an API token from **[string.bd](https://string.bd)** (free).
2. Tray → **Note Setting** → paste it into **Bangla (string.bd)**.
3. In any note, press **Ctrl+Shift+L** (or click **E / ক** on the toolbar) and type by sound:
   `ami` → আমি.

Keys and tokens are stored in your own Windows user settings file (see *Where things live*). They are
never part of the installer or the zip, and nothing is sent anywhere until you press a button that
needs them.

---

## Upgrade

Run the new installer over the old one — same flavour as before (standard over standard, per-user over
per-user). It replaces the files in place; **your settings, hot keys, keys and notes are kept**. For
the portable build, unzip the new version over the old folder.

There is no auto-update and no update check. Watch the
[releases page](https://github.com/Mahi-BD/MicroApp/releases) or the repository for new versions.

---

## Uninstall

*Settings → Apps → Installed apps → MicroApp → Uninstall*, or the Start-menu **Uninstall MicroApp**
shortcut. Silently: `"%ProgramFiles%\MicroApp\Uninstall.exe" /S`.

The uninstaller closes MicroApp if it is running, then removes the program files, the Start-menu
folder, the desktop shortcut, the Startup shortcut and the registry entries.

**It deliberately leaves your data alone** — settings, hot keys, API keys and every note stay where
they are, so reinstalling picks up exactly where you left off. To remove them too, delete
`%LOCALAPPDATA%\MicroApp` (settings) and your notes folder.

---

## Where things live

| What | Where |
|---|---|
| The app | `C:\Program Files\MicroApp`, `%LOCALAPPDATA%\Programs\MicroApp`, or your portable folder |
| Settings, hot keys, API keys | `%LOCALAPPDATA%\MicroApp\...\user.config` |
| Notes | `Notes\` next to `MicroApp.exe` when writable, otherwise `%AppData%\MicroApp\Notes` |
| Screenshots | `Pictures\MicroApp\`, or your Capture Setting folder |
| GIFs | your GIF Setting folder (falls back to the image folder) |
| Videos | `Videos\MicroApp\`, or your Video Setting folder |

---

## If something goes wrong

- **The tray icon never appears** — check whether MicroApp is already running (only one copy runs at
  a time; a second one exits silently), and look in the tray overflow area (the `^` arrow).
- **A hot key does nothing** — another app already owns that combination. MicroApp offers to take it
  over when it finds one taken; otherwise pick a different key in the matching settings window.
- **It does not start with Windows** — re-run the installer and tick the last-page checkbox, or drop a
  shortcut to `MicroApp.exe` into `shell:startup` yourself.
- **Nothing types into an elevated window** — a normal-privilege app cannot send input to an
  administrator one. Run MicroApp as administrator too.

The complete list is in **[HELP.md → Troubleshooting](HELP.md#troubleshooting)**.
