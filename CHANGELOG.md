# Changelog

## Unreleased

### Added

- **Several layers at once.** Ctrl-click or Shift-click in the Layers panel, Shift-click on the canvas with the
  Move tool, or Select → All Layers (Alt+Ctrl+A). Dragging any of them moves all of them (with guide snapping), and
  so do the arrow keys. Delete removes them all. The align buttons align them to each other, and the new
  **Distribute Horizontally / Vertically** buttons (also in the Layer menu) give three or more layers equal gaps.
- **Rulers and guides in the image editor, the Photoshop way.** A ruler on/off button sits under the tools.
  - Drag out of a ruler to add a guide, and drag a guide with the Move tool (or Ctrl) to move it. Drop it on
    a ruler to delete it.
  - Right-click a guide to lock or unlock it, delete it or change its colour.
  - Layers, selections, marquees, shapes and crops snap to guides (hold Ctrl to place freely).
  - View menu: Guides (Ctrl+;), Lock Guides, Snap to Guides and Clear Guides.

## 5.3.0 — 2026-09-25

### Added

- **Record Video: follow the pointer and zoom, live.** The recording badge has a **follow** button
  (the recorded area glides after the mouse, eased and kept on the screens) and **zoom − / +** from
  50 % to 400 % (also the mouse wheel over the badge). The video keeps its size. The red frame moves
  with the recorded area, and the badge and frame are hidden from capture so they never appear in
  the video.
- **Record Video: tutorial helpers.** **Ctrl+Alt+P** pauses and resumes from anywhere, and the badge can be
  dragged out of the way. Video Setting has a new **While recording** card:
  - **Keep the mouse pointer its normal size when zoomed** (on by default).
  - **Glow on mouse clicks**: yellow left, blue right, green middle.
  - **Action log**: a `.txt` next to the video that lists every click and key press with its second in
    the video.
- **Record Video: your voice and the system sound together.** A new sound source, *System sound +
  microphone*, mixes both into one track, and a **mute** button on the badge silences the microphone
  live.

### Changed

- **Remove Background is much smarter.** It now uses **IS-Net** (the DIS general-use model), which
  sees the picture at 1024 × 1024 instead of 320 × 320. It keeps whole people (raised arms, bags,
  legs), animals and objects with clean edges where the old model cut parts off. It takes a second
  or two and about 1 GB of memory, runs offline like before, and a small "working" window keeps the
  editor responsive meanwhile. The old model is still there as **Image → Remove Background (Fast)**.
- The model ships inside the installers, the portable zip and the Store package (in `Models\` next to
  the exe), so nothing is downloaded. The setup grows to about 170 MB.

## 5.2.0 — 2026-09-25

The image editor now works with several documents in tabs, and gains a Photoshop-style New Document
window, AI background removal and printing.

### Added

- **Document tabs in the image editor.** Several images can be open at once, each in its own tab
  with its own layers, history, selection and zoom. Tabs close with ×, a middle click or **Ctrl+W**,
  and **Ctrl+Tab** cycles through them. There is a new **Window** menu, and a right-click on a tab offers
  **Close Others** / **Close All**.
- **A Photoshop-style New Document window.** It has preset categories (Recent, Print A0-A6/B4/B5/Letter/
  Legal/Tabloid, Ratio 16:9/21:9/4:3/1:1…, Photo, Web, Mobile, Film & Video) and a Preset Details column
  with units (px/in/cm/mm/pt), resolution, orientation, constrain and background contents. A new document
  opens in a new tab and no longer replaces the one you were working on.
- **Remove Background** (the new button under the tools, **Image → Remove Background**, **Alt+Ctrl+B**)
  makes everything but the subject transparent using a small AI model (U²-Netp, via ONNX Runtime) that
  runs offline. With a selection, only the area inside it is changed.
- **File → Print** (**Ctrl+P**) with a Photoshop-style preview: the page to scale with the picture on it
  (drag to move), printer, copies, paper, orientation, center / top / left, scale to fit, scale %, width /
  height in in / cm / mm, the effective print resolution and warnings. Documents now remember the
  resolution they were created (or opened) at, so they print at their real size.
- **File → Add Image** (**Shift+Ctrl+O**) adds pictures to the current document as layers, while
  **File → Open** now opens each picture in a new tab. (Add Image replaces "Place Image as Layer".)
  **File → Exit** (**Ctrl+Q**) closes the editor. **Ctrl+W** now closes the current tab.
- **Photoshop's hand cursor for panning.** Holding **Space** (or picking the Hand tool) shows the open
  hand, and dragging shows the closed fist. Before, it was the pointing-finger link cursor and then the four-way
  arrows. The brush-size circle hides while Space is held.

### Changed

- The download is about 13 MB bigger: Remove Background brings ONNX Runtime and the Visual C++ runtime
  it needs (shipped next to the exe, so nothing extra has to be installed).

## 5.1.2 — 2026-09-12

### Added

- **Straight lines with the brush, the Photoshop way.** Click once, then **Shift**-click somewhere
  else, and the brush paints a straight line between the two points; keep Shift-clicking to chain
  segments. **Shift**-dragging locks a stroke to horizontal or vertical. Both work for the eraser,
  clone stamp, blur, sharpen, dodge and burn, and the pencil draws a straight stroke with Shift held.

### Fixed

- **A brush stroke could leave a stray mark in the corner of the layer.** Each dab wrote its
  changed rectangle back through a locked sub-rectangle of the bitmap, and GDI+ implementations
  disagree about where that buffer maps to — on some it landed at the image origin instead of
  under the brush. The write now targets the image as a whole and touches only the rows the dab
  changed.

## 5.1.1 — 2026-09-12

### Fixed

- **Image editor: Shift now sends a move along the nearest 45°.** Holding Shift while moving a
  layer, a floating selection, a selection outline or a Free Transform box snapped the drag to
  horizontal or vertical only. It follows the nearest axis *or diagonal* now — and travels as far
  along it as the mouse has — which is what Photoshop does and what a diagonal drag should do.

## 5.1.0 — 2026-09-12

### Changed

- **The image editor grew up into a Photoshop-style editor.** The layout, the tools, the keys,
  the menus and the right-click menus now follow Photoshop, so what you know from there works
  here: a two-column tool rail with grouped tools and flyouts, foreground/background swatches
  (**X** swaps, **D** resets), an options bar per tool, **Layers / History** tabs and the asset
  library on the right, a status bar with a typed zoom box, rulers (**Ctrl+R**) and marching
  ants. Number fields, drop-downs and sliders are drawn in the app's own style (type a value,
  click or hold the chevrons, roll the wheel, or use the arrow keys) instead of the stock
  Windows controls. The right-hand panels resize by dragging the handle beside them, and the
  editor remembers that width and its window size. **Help → Keyboard Shortcuts** lists every key.

### Added

- **Selections.** Rectangular and Elliptical Marquee (**M**), Lasso and Polygonal Lasso
  (**L**), Magic Wand (**W**, with tolerance, contiguous and sample-all-layers) - **Shift** adds,
  **Alt** subtracts, **Shift+Alt** intersects, with the four mode buttons in the options bar and a
  feather setting. Select → All / Deselect / Reselect / Inverse / Layer Pixels / Modify (Border,
  Smooth, Expand, Contract, Feather) / Grow / Similar. **Transform Selection** (right-click the
  selection, or Select → Transform Selection) puts the transform box around the marching ants
  themselves: move, scale, rotate, skew, distort or perspective the outline and press Enter - the
  pixels stay put. With a marquee or lasso tool, dragging inside the selection slides the outline. Every filter, adjustment, fill, stroke,
  paint stroke and Delete respects the selection; **Ctrl+J** / **Shift+Ctrl+J** lift the selected
  pixels onto a layer; **Ctrl+C / Ctrl+X** copy or cut them (with alpha, and **Shift+Ctrl+V**
  pastes in place); dragging inside the selection with Move moves just those pixels; **Image →
  Crop** crops to it.
- **Free Transform (Ctrl+T).** Scale (proportional by default, **Shift** frees it, **Alt** from
  the centre), rotate by dragging outside a corner (**Shift** snaps 15°), **Ctrl**-drag an edge to
  skew, **Ctrl**-drag a corner to distort, **Ctrl+Alt+Shift** for perspective - the last two warp
  the pixels on commit. Exact X / Y / W / H / angle / skew entry in the options bar, **Enter**
  commits, **Esc** cancels, right-click for the mode menu and Rotate 180° / 90° / Flip. **Edit →
  Transform → Again** (**Shift+Ctrl+T**) repeats the last transform.
- **Painting.** A real Brush (**B**) with size, hardness, opacity and flow; Eraser (**E**); Clone
  Stamp (**S**, **Alt**-click the source); Gradient (**G**, linear/radial, to background or to
  transparent); Paint Bucket (**Shift+G**); Blur / Sharpen (**R**); Dodge / Burn (**O**);
  Eyedropper (**I**, **Alt** for background, point/3×3/5×5). **[ ]** change the size, **Shift+[ ]**
  the hardness, **1**…**0** the opacity. The old freehand pen lives on as the Pencil
  (**Shift+B**) and still makes editable stroke layers.
- **Image → Adjustments**, each with a live preview: Brightness/Contrast, Levels (**Ctrl+L**,
  histogram, draggable points, per channel), Curves (**Ctrl+M**), Exposure, Vibrance,
  Hue/Saturation (**Ctrl+U**, Colorize), Color Balance (**Ctrl+B**), Black & White
  (**Alt+Shift+Ctrl+B**, with tint), Photo Filter, Invert (**Ctrl+I**), Posterize, Threshold,
  Desaturate (**Shift+Ctrl+U**), Equalize, Auto Tone / Auto Contrast / Auto Color.
- **Filter menu**: Gaussian / Motion / Box Blur, Sharpen / Sharpen More / Unsharp Mask, Add Noise /
  Median / Reduce Noise, Mosaic, Emboss / Find Edges / Solarize, High Pass, Vignette, and Last
  Filter (**Alt+Ctrl+F**). Big layers preview on a smaller copy so the sliders stay quick.
- **Layers**: all of Photoshop's blend modes, a lock, drag-to-reorder, New Layer
  (**Shift+Ctrl+N**), Merge Down (**Ctrl+E**), Merge Visible (**Shift+Ctrl+E**), Flatten,
  Rasterize, Arrange (**Ctrl+]**, **Ctrl+[**, with Shift for front/back), Align to Canvas, and
  **Layer Style** - Drop Shadow, Outer Glow, Stroke and Color Overlay, non-destructive and
  previewed live. A **History** panel lists fifty steps; click one to jump.
- **Shapes**: Rounded Rectangle (corner radius) and Polygon (any number of sides) join
  Rectangle, Ellipse, Line and Arrow under **U** (**Shift+U** cycles); **Alt** draws from the
  centre. Text gained left/centre/right alignment.
- **Crop** with handles, ratio presets (1:1, 4:3, 16:9, 3:2, original) and *Delete Cropped
  Pixels*; **Image → Canvas Size** with an anchor; **Trim**; **Reveal All**; **Image Rotation →
  180°**; **Edit → Fill** (**Shift+F5**, with blend mode and *Preserve transparency*) and **Edit
  → Stroke**; Zoom (**Z**) and Hand (**H**) tools; **Ctrl+H** hides the extras.
- MicroApp now runs as a **64-bit** process on 64-bit Windows, so the editor is no longer
  capped at what a 32-bit process can address. The history keeps its bitmaps within a memory
  budget (the status bar shows how much it holds), **Edit → Purge History** frees it all, and
  **Help → Save Diagnostic Snapshot** (**F12**) writes the editor's state and a picture of the
  window to `%LocalAppData%\MicroApp\diag`. An error on the UI thread is now logged to
  `diag\errors.log` and shown in the app's own dialog instead of the Windows crash box.

## 4.9.3 — 2026-08-29

### Fixed

- **Searching the notes list now looks inside the notes.** The search box on the Notes window
  only matched a note's name and its title line; a word that appeared further down found
  nothing. It now searches the full text of every note, the way the Archive window always did —
  same matching, same per-note cache, so typing stays instant.

## 4.9.2 — 2026-08-29

### Changed

- **Image editor: the side panels got their outlines back — the soft way.** Removing the black
  Windows borders in 4.9.1 left the layers and asset boxes floating with no edges at all; they
  now wear a quiet one-pixel grey frame that matches the rest of the theme.

## 4.9.1 — 2026-08-29

### Changed

- **Image editor: the options bar stays out of the way.** It only appears when the current tool
  actually has options to show — drawing, text, blur, crop, or a selected layer. Plain Move with
  nothing selected keeps the full height for the canvas.
- **Image editor: no more hard black borders.** The layers list and the asset panels lost their
  Windows-drawn black outlines; the panel buttons became soft borderless chips; the empty-state
  icon is drawn properly instead of falling back to a hollow box glyph.

## 4.9.0 — 2026-08-29

### Added

- **Shortcuts — every hot key in one window.** Tray → *Shortcuts* lists all ten hot keys with
  their modifiers and keys in one place. They are the same values the feature windows edit, so a
  change made here shows up in Key/OCR/Capture/GIF/Video/Note Setting and a change made there
  shows up here — one setting, two doors. Duplicate combinations are caught on save, and
  clearing a key box turns that hot key off.

- **Typed dates.** Two new hot keys type the date straight into whatever window has focus,
  through the same engine the clipboard paste uses: **Ctrl+Shift+D** for the short date and
  **Ctrl+Shift+M** for the long one. Their formats are editable on the Shortcuts window (with a
  live preview) and are shared with the note toolbar's date buttons.

- **Asset categories can be renamed.** Right-click a category in the image editor's asset panel
  (or press F2) → *Rename Category*; the folder on disk is renamed and the tree follows. The
  context menu also creates sub-categories.

### Changed

- **The image editor looks the part now.** The tool rail got grouped sections, rounded hover
  and active states; the options bar leads with the current tool's name; every layer row shows
  a little preview thumbnail of what it holds, a cleaner eye toggle and a quiet accent
  selection instead of a solid slab; the layers header counts its layers; the canvas floats on
  a soft shadow; the empty editor greets with a proper drop-target instead of a bare line of
  text; and the status bar separates facts (size, zoom, layer count) from the per-tool hint.

## 4.8.0 — 2026-08-29

### Added

- **An image editor.** Tray → *Image Editor* or **Ctrl+Alt+E** opens a Photoshop-style window:
  tool rail on the left, canvas in the middle, layers and an asset library on the right. It opens
  on whatever image is on the clipboard, and Ctrl+V keeps adding more.

  Everything you put on the canvas — a pasted screenshot, a rectangle, an arrow, a text box, a
  logo — stays its **own layer**: show or hide it with the eye, fade its opacity, move it up or
  down the stack, rename, duplicate, delete. Nothing is flattened until export.

  The tools: move/select (with resize handles, a rotate handle, Shift to keep proportions),
  crop, rectangle, ellipse, line, arrow, freehand pen, text and a **blur brush** that paints a
  soft-edged blur over the private parts of a screenshot. Shapes have stroke colour, width and
  optional fill; text has font, size, bold/italic/underline, colour, a background box and an
  outline, and can be rotated and mirrored like any other layer — mirrored text renders truly
  mirrored. The Image menu rotates or mirrors the whole canvas, and Resize scales the whole
  composition, layers included.

  The **asset library** keeps logos and stamps at hand, organised in categories and
  sub-categories — it is simply a folder tree under `%AppData%\MicroApp\Assets`, so Explorer can
  fill it too. Import images into a category, double-click one to drop it in as a layer, or save
  any layer back into the library as a PNG asset.

  Out again: File → Save As (PNG keeps transparency, JPG for mail, BMP), or Ctrl+Shift+C puts
  the finished image straight back on the clipboard. Undo covers everything, forty steps deep.

## 4.7.7 — 2026-08-09

### Fixed

- **A long Bangla note no longer freezes while you type it.** In a note of a few thousand Bangla
  characters, the window locked up for two to three seconds shortly after every pause in typing, over
  and over, which made a long piece of writing almost impossible to work in. The cause was the
  fixed-width setting added in 4.7.6: to keep Bangla readable it puts Nirmala UI on every Bangla run
  in the note, and it was redoing that for the whole note after every keystroke — several hundred
  separate pieces of text, each one costing a font of its own.

  It now only restyles the words that actually changed, and it asks the editor for the face directly
  instead of building a font to compare against. Typing into that same note went from a **2.8 second**
  freeze to **17 milliseconds**, and pasting the whole thing in from **2.5 seconds** to **70
  milliseconds**. Nothing about the result changed: Bangla is still drawn in Nirmala UI with its vowel
  signs joined, the Latin around it still lines up column for column, and the phonetic suggestion list
  still stays open while you pick from it.

## 4.7.6 — 2026-08-08

### Changed

- **Notes are set in fixed-width text, the way Notepad is.** Every character now takes the same
  width, so a Markdown table, a log or a block of code pasted into a note lines up column for column
  instead of drifting a little further out of true on every row. The face is **Consolas**, Notepad's
  own. A new switch in Note Setting, **Fixed-width text, like Notepad**, is on by default and turns
  the whole thing back to Nirmala UI when you would rather write prose; open notes follow the switch
  without being reopened.

  Bangla is not left behind. No fixed-width face on Windows carries Bengali, so every Bangla run in a
  note is drawn in **Nirmala UI** while the Latin around it keeps the fixed-width face — the columns
  still line up, and the vowel signs still join onto their consonant. Notes with no Bangla in them do
  no extra work at all. Phonetic typing is untouched by it: the suggestion list stays open while you
  pick from it, and the restyling passes behind it without moving the caret or the scroll position.

## 4.7.5 — 2026-08-05

### Added

- **The capture frame can be moved and resized before it is taken.** Screen capture, GIF recording and
  video recording used to take whatever you had dragged the moment you let go of the mouse, so a frame
  a few pixels out meant starting over. The frame now stays on screen instead: drag it by the middle —
  a four-way arrow appears there while the pointer is inside — to move the whole thing, drag any of the
  eight handles to resize it,
  or nudge it with the arrow keys (Ctrl for ten pixels at a time, Shift to resize instead of move).
  Dragging outside the frame starts a fresh one. **Enter**, a double-click inside the frame, or the
  tick button takes it; **Esc**, a right-click or the cross button cancels.

  A locked ratio is kept through every move and resize, so a 16:9 frame stays 16:9 whichever handle you
  pull. A locked pixel size is unchanged — the box is already exactly the size you asked for and one
  click takes it.

- **A delay before the shot is taken**, in Capture Setting: *seconds before it grabs*, 0 by default,
  which behaves exactly as before. Give it a few seconds and the frame stays outlined with a badge
  counting down beside it, leaving you time to open the menu, tooltip or hover state you are trying to
  photograph. Neither window takes the focus, so the menu you open stays open, and both are gone before
  the picture is read. Click the badge to call it off. A delayed shot reads the screen fresh at the end
  of the count rather than using the frozen copy, which is what makes it useful.

- **8:5 in the lock-ratio list**, in Capture Setting, GIF Setting and Video Setting alike. The list now
  shows all of its shapes without scrolling. Any `W:H` you type by hand still works, as before.

## 4.7.4 — 2026-08-02

### Added

- **Keep a note on top.** The last button on a note's toolbar pins that note above other windows —
  useful while copying out of it into something else. It lights up while it is on, and is per note:
  a new note starts off, and nothing is remembered between sessions.

### Changed

- **The settings button is gone from a note's toolbar**, replaced by the on-top toggle. Note Setting
  is still on the tray menu, where the other settings windows live.

## 4.7.3 — 2026-08-02

### Fixed

- **Text you just wrote is no longer replaced by the older copy in the cloud.** With sync on, a note
  could snap back to what it said before — most visibly when the Grammar button or Ask AI rewrote it,
  but the same happened to ordinary typing. A sync pulls before it pushes, and the pull only asked
  whether the cloud's copy looked newer; it never asked whether this PC had an edit it had not sent
  yet. So a copy that looked newer — which is easy on a PC whose clock is fast — was written over the
  fresh text before the push half of the very same pass could send it up, and the open note reloaded
  from disk in front of you.

  An unsent local edit now always wins: the pull leaves it alone (and will not honour a delete from
  another PC against it either), and the push sends it stamped above whatever it replaces, so it
  sticks.

## 4.7.2 — 2026-08-02

### Changed

- **A note's first toolbar button is now Save.** It was *New note*, which a note window did not
  really need — the hot key and the notes list both make new notes. Notes still save themselves as
  you type; the button writes the file out there and then and says *Saved*, for when you want to be
  sure.
- **Delete all notes is gone from the notes list.** One mis-aimed click could take every note; notes
  are deleted one at a time now, or archived if they are just in the way.

- **The Bangla suggestion list is smaller.** It took more of the note than it needed to: rows are
  26px instead of 32, the Bangla is 10pt instead of 11, and the panel is narrower — about 168 x 210
  for eight suggestions, down from 240 x 258. Vowel signs that sit above the line still have their
  room.

## 4.7.1 — 2026-08-02

### Added

- **New notes keep the colour you last chose.** Colours used to be picked from the note's name, so
  every note came out a different one. Pick a colour — from the note's toolbar button or the list's
  right-click menu — and every new note on that PC starts in it. It is a setting on that machine, not
  part of a note, so it is not synced and each PC can have its own. *Automatic* puts the old
  per-note-name behaviour back.

### Fixed

- **Pins, colours and the manual order sync again, both ways.** 4.7.0 started stamping changes with
  the database's clock, which is right in principle — but on a PC whose own clock ran fast, the
  corrected stamps landed *behind* the ones that PC had already written, so its changes looked older
  than what was in the cloud and were never sent. Stamps now only ever move forward.
- **Whether this PC has edited a note no longer depends on comparing clocks at all.** Each note's
  state at the last sync is recorded in a `.sync-state` file beside the notes, so a local edit is
  found by comparing the file against its own record. A note being sent is also stamped above
  whatever it replaces, so a copy can never win for ever. The first sync after updating pushes every
  note once, which puts any stamps written by a wrong clock back in order.

## 4.7.0 — 2026-08-01

### Added

- **A colour button on the note's own toolbar.** Set a note's colour from the note itself rather than
  only from the list — the button shows the colour it is wearing, and the notes list picks the change
  up straight away. The colour is kept in the sidecar next to the notes, so it stays on this PC and
  travels with the note when sync is on.
- **A search box across the top of the notes list**, matching a note's file name and its title (the
  first line) as you type. The Archive has had one since 4.6.0; this is the same idea for the notes
  you are still using.

### Changed

- **Sync is now near-realtime.** It used to check every three minutes. Each PC now checks every 15
  seconds, but reads a single small "pulse" document to do it and only reads the notes themselves
  when that says something actually changed — so checking 12× as often costs about 5,700 reads a day
  whatever the number of notes, well inside the free tier. A change you make goes up about three
  seconds later. In testing, a pin, a colour and a whole reordering made on one PC appeared on
  another in 7-9 seconds.
- **Sync stamps come from the database's clock, not each PC's.** Newest-wins compares stamps written
  by different machines, so a PC whose clock or time zone is wrong would win every conflict and
  overwrite everyone else's edits with its stale copies. The offset is learned from the reply to each
  write, so a misconfigured PC no longer poisons the notes.

### Fixed

- **A reordering made on one PC now reaches the others.** Pin, colour and archive travelled per note,
  but the manual order is one list rather than a per-note value, and the test deciding who owned it
  was made after the incoming decoration had already been applied — so the two sides always looked
  equal and the order never moved. It is now measured before anything is applied.
- **The note window is wide enough for its toolbar again** — the new colour button pushed the Grammar
  button underneath the Bangla one at the default width.

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
