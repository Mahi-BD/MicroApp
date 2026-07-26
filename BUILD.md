# Building MicroApp

## What you need

| | |
|---|---|
| OS | Windows 10 / 11 (the app is WinForms + Win32, it does not build on Linux or macOS) |
| SDK | .NET Framework 4.8 targeting pack |
| Build tool | Visual Studio 2022+ (or Build Tools) with MSBuild, or `msbuild` from a Developer prompt |
| Restore | NuGet (bundled with MSBuild 16+) |

NuGet packages restore automatically:

- **MouseKeyHook** — global mouse/keyboard hooks used for target picking and Esc handling.
- **Microsoft.Windows.SDK.Contracts** — WinRT projections so .NET Framework can call
  `Windows.Media.Ocr`, the OCR engine built into Windows.

`AutoItX3` ships in the repository as a DLL and is copied to the output folder.

## Build

```powershell
# restore + build
msbuild MicroApp.sln /t:Restore,Build /p:Configuration=Debug

# release build, no code signing
msbuild MicroApp.sln /p:Configuration=Release /p:SkipCodeSigning=true
```

Output lands in `bin\Debug\` or `bin\Release\`. `MicroApp.exe` plus the AutoItX and MouseKeyHook DLLs
are all you need to run it — it is xcopy-portable.

> Building `Release` **without** `/p:SkipCodeSigning=true` runs `sign.bat`, which expects a code signing
> certificate. Skip signing unless you have one.

## Installer

The shipped installer is NSIS (`Setup\nsis\MicroApp.nsi`). It needs only `makensis`, which runs on
Windows **and** Linux, so it can be built anywhere:

```sh
cd Setup/nsis
makensis -DVERSION=4.2.2 -DSRC=../../bin/Release MicroApp.nsi            # all users, Program Files, admin
makensis -DPERUSER -DVERSION=4.2.2 -DSRC=../../bin/Release MicroApp.nsi  # just me, %LOCALAPPDATA%, no admin
```

`SRC` points at the folder holding the built app. The result is
`MicroApp-<version>[-peruser]-setup.exe`, which offers a **Run MicroApp when Windows starts** checkbox
on its last page (a Startup-folder shortcut, not an HKCU Run value, because the installer runs elevated).

Silent use:

```
MicroApp-4.2.2-setup.exe /S            # install, no startup entry
MicroApp-4.2.2-setup.exe /S /STARTUP   # install and start with Windows
MicroApp-4.2.2-setup.exe /S /D=C:\Tools\MicroApp
"C:\Program Files\MicroApp\Uninstall.exe" /S
```

`Setup\MicroApp-Setup.wixproj` is the older WiX/MSI project inherited from upstream. It still builds an
MSI locally if you have the WiX toolset, but CI skips it: WiX v6 wants a FireGiant license that hosted
runners do not have.

## Layout

| Path | What it is |
|---|---|
| `Program.cs` | Tray app: menu, hot keys, and the flow for each feature |
| `SettingsForm.*` | Key Setting window |
| `OcrSettingsForm.*` / `OcrService.cs` / `OcrResultForm.cs` | OCR settings, Windows OCR wrapper, result preview |
| `CaptureSettingsForm.*` | Screen capture settings |
| `GifSettingsForm.*` / `GifRecorder.cs` / `GifWriter.cs` | GIF settings, recorder, incremental GIF89a encoder |
| `VideoSettingsForm.*` / `VideoRecorder.cs` | Video settings and the MP4 screen/sound recorder |
| `Mp4Writer.cs` / `AudioCapture.cs` | Media Foundation sink writer (H.264+AAC) and WASAPI loopback/mic capture |
| `RegionCaptureOverlay.cs` | The freeze-and-drag crosshair picker, plus the ratio/pixel locks |
| `ModernUI.cs` | Theme (light/dark) and the custom-drawn controls every window uses |
| `ModernDialog.cs` / `Toast.cs` | Themed message dialog and the one-second notification |
| `AboutForm.cs` | About window |
| `Native.cs` | Win32/DWM interop: scan codes, cursors, dark title bars |
| `Resources\*.ico` | App and tray icons |
| `Setup\` | WiX installer |

## Notes for contributors

- The UI is custom-drawn. Colours and fonts come from `Theme`; if you add a control, give it a
  `Theme.PaintBackdrop` call at the top of `OnPaint` and let `Theme.Apply` colour it.
- Every settings window uses the same canvas: 640 × 612, an 84 px header, cards 592 px wide at x=24,
  and the footer buttons at y=564.
- Hot keys are registered through `HotKeyManager`; each handler must check that the event matches its
  own configured key, because the event is global to the app.
