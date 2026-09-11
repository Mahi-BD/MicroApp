using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// The menu bar (File / Edit / Image / Layer / Select / Filter / View / Help, with
    /// Photoshop's shortcuts), the right-click menus, and the adjustment / filter
    /// commands with their live-preview dialogs.
    /// </summary>
    partial class ImageEditorForm
    {
        delegate void AdjustApply(Pixels px, AdjustDialog d, float scale, byte[] mask);

        Action _lastFilter;
        string _lastFilterName;
        ToolStripMenuItem _lastFilterItem, _rulersItem, _extrasItem;

        // ================================================================= menu bar

        void BuildMenu()
        {
            _menu = new MenuStrip
            {
                Renderer = new ModernMenuRenderer(),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Base,
                Padding = new Padding(8, 4, 0, 4)
            };

            var file = new ToolStripMenuItem("File");
            file.DropDownItems.Add(Item("New…", Keys.Control | Keys.N, delegate { NewDocument(); }));
            file.DropDownItems.Add(Item("Open…", Keys.Control | Keys.O, delegate { OpenFile(); }));
            file.DropDownItems.Add(Item("Place Image as Layer…", Keys.None, delegate { OpenFile(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("Save As…", Keys.Control | Keys.Shift | Keys.S, delegate { SaveAs(); }));
            file.DropDownItems.Add(Item("Save", Keys.Control | Keys.S, delegate { SaveAs(); }));
            file.DropDownItems.Add(Item("Copy Merged to Clipboard", Keys.Control | Keys.Shift | Keys.C, delegate { CopyResult(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("Close", Keys.Control | Keys.W, delegate { Close(); }));

            var edit = new ToolStripMenuItem("Edit");
            edit.DropDownItems.Add(Item("Undo", Keys.Control | Keys.Z, delegate { DoUndo(); }));
            edit.DropDownItems.Add(Item("Redo", Keys.Control | Keys.Shift | Keys.Z, delegate { DoRedo(); }));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Cut", Keys.Control | Keys.X, delegate { CopySelection(true); }));
            edit.DropDownItems.Add(Item("Copy", Keys.Control | Keys.C, delegate { CopySelection(false); }));
            edit.DropDownItems.Add(Item("Paste", Keys.Control | Keys.V, delegate { PasteFromClipboard(false, false); }));
            edit.DropDownItems.Add(Item("Paste in Place", Keys.Control | Keys.Shift | Keys.V, delegate { PasteFromClipboard(false, true); }));
            edit.DropDownItems.Add(Item("Clear", Keys.None, delegate { if (HasSelection) ClearSelection(); else DeleteLayer(); }));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Fill…", Keys.Shift | Keys.F5, delegate { FillCommand(); }));
            edit.DropDownItems.Add(Item("Stroke…", Keys.None, delegate { StrokeCommand(); }));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Free Transform", Keys.Control | Keys.T, delegate { BeginTransform(SelectedLayer(), TransformMode.Free); }));
            var transform = new ToolStripMenuItem("Transform");
            transform.DropDownItems.Add(Item("Again", Keys.Control | Keys.Shift | Keys.T, delegate { TransformAgain(); }));
            transform.DropDownItems.Add(new ToolStripSeparator());
            transform.DropDownItems.Add(Item("Scale", Keys.None, delegate { BeginTransform(SelectedLayer(), TransformMode.Scale); }));
            transform.DropDownItems.Add(Item("Rotate", Keys.None, delegate { BeginTransform(SelectedLayer(), TransformMode.Rotate); }));
            transform.DropDownItems.Add(Item("Skew", Keys.None, delegate { BeginTransform(SelectedLayer(), TransformMode.Skew); }));
            transform.DropDownItems.Add(Item("Distort", Keys.None, delegate { BeginTransform(SelectedLayer(), TransformMode.Distort); }));
            transform.DropDownItems.Add(Item("Perspective", Keys.None, delegate { BeginTransform(SelectedLayer(), TransformMode.Perspective); }));
            transform.DropDownItems.Add(new ToolStripSeparator());
            transform.DropDownItems.Add(Item("Rotate 180°", Keys.None, delegate { QuickTransform("Rotate 180°"); }));
            transform.DropDownItems.Add(Item("Rotate 90° Clockwise", Keys.None, delegate { QuickTransform("Rotate 90° CW"); }));
            transform.DropDownItems.Add(Item("Rotate 90° Counter Clockwise", Keys.None, delegate { QuickTransform("Rotate 90° CCW"); }));
            transform.DropDownItems.Add(new ToolStripSeparator());
            transform.DropDownItems.Add(Item("Flip Horizontal", Keys.None, delegate { QuickTransform("Flip Horizontal"); }));
            transform.DropDownItems.Add(Item("Flip Vertical", Keys.None, delegate { QuickTransform("Flip Vertical"); }));
            edit.DropDownItems.Add(transform);

            var image = new ToolStripMenuItem("Image");
            var adjust = new ToolStripMenuItem("Adjustments");
            adjust.DropDownItems.Add(Item("Brightness/Contrast…", Keys.None, delegate { BrightnessContrast(); }));
            adjust.DropDownItems.Add(Item("Levels…", Keys.Control | Keys.L, delegate { Levels(); }));
            adjust.DropDownItems.Add(Item("Curves…", Keys.Control | Keys.M, delegate { Curves(); }));
            adjust.DropDownItems.Add(Item("Exposure…", Keys.None, delegate { Exposure(); }));
            adjust.DropDownItems.Add(new ToolStripSeparator());
            adjust.DropDownItems.Add(Item("Vibrance…", Keys.None, delegate { Vibrance(); }));
            adjust.DropDownItems.Add(Item("Hue/Saturation…", Keys.Control | Keys.U, delegate { HueSaturation(); }));
            adjust.DropDownItems.Add(Item("Color Balance…", Keys.Control | Keys.B, delegate { ColorBalance(); }));
            adjust.DropDownItems.Add(Item("Black & White…", Keys.Control | Keys.Alt | Keys.Shift | Keys.B, delegate { BlackWhite(); }));
            adjust.DropDownItems.Add(Item("Photo Filter…", Keys.None, delegate { PhotoFilter(); }));
            adjust.DropDownItems.Add(new ToolStripSeparator());
            adjust.DropDownItems.Add(Item("Invert", Keys.Control | Keys.I, delegate { RunInstant("Invert", (p, m) => PixelOps.Invert(p, m)); }));
            adjust.DropDownItems.Add(Item("Posterize…", Keys.None, delegate { Posterize(); }));
            adjust.DropDownItems.Add(Item("Threshold…", Keys.None, delegate { Threshold(); }));
            adjust.DropDownItems.Add(new ToolStripSeparator());
            adjust.DropDownItems.Add(Item("Desaturate", Keys.Control | Keys.Shift | Keys.U, delegate { RunInstant("Desaturate", (p, m) => PixelOps.Desaturate(p, m)); }));
            adjust.DropDownItems.Add(Item("Equalize", Keys.None, delegate { RunInstant("Equalize", (p, m) => PixelOps.Equalize(p, m)); }));
            image.DropDownItems.Add(adjust);
            image.DropDownItems.Add(Item("Auto Tone", Keys.Control | Keys.Shift | Keys.L, delegate { RunInstant("Auto Tone", (p, m) => PixelOps.AutoTone(p, m)); }));
            image.DropDownItems.Add(Item("Auto Contrast", Keys.Control | Keys.Alt | Keys.Shift | Keys.L, delegate { RunInstant("Auto Contrast", (p, m) => PixelOps.AutoContrast(p, m)); }));
            image.DropDownItems.Add(Item("Auto Color", Keys.Control | Keys.Shift | Keys.B, delegate { RunInstant("Auto Color", (p, m) => PixelOps.AutoColor(p, m)); }));
            image.DropDownItems.Add(new ToolStripSeparator());
            image.DropDownItems.Add(Item("Image Size…", Keys.Control | Keys.Alt | Keys.I, delegate { ResizeDocument(); }));
            image.DropDownItems.Add(Item("Canvas Size…", Keys.Control | Keys.Alt | Keys.C, delegate { CanvasSize(); }));
            var rotation = new ToolStripMenuItem("Image Rotation");
            rotation.DropDownItems.Add(Item("180°", Keys.None, delegate { RotateCanvas(180); }));
            rotation.DropDownItems.Add(Item("90° Clockwise", Keys.None, delegate { RotateCanvas(90); }));
            rotation.DropDownItems.Add(Item("90° Counter Clockwise", Keys.None, delegate { RotateCanvas(-90); }));
            rotation.DropDownItems.Add(new ToolStripSeparator());
            rotation.DropDownItems.Add(Item("Flip Canvas Horizontal", Keys.None, delegate { FlipCanvas(true); }));
            rotation.DropDownItems.Add(Item("Flip Canvas Vertical", Keys.None, delegate { FlipCanvas(false); }));
            image.DropDownItems.Add(rotation);
            image.DropDownItems.Add(new ToolStripSeparator());
            image.DropDownItems.Add(Item("Crop", Keys.None, delegate { CropToSelection(); }));
            image.DropDownItems.Add(Item("Trim", Keys.None, delegate { TrimCanvas(); }));
            image.DropDownItems.Add(Item("Reveal All", Keys.None, delegate { RevealAll(); }));

            var layer = new ToolStripMenuItem("Layer");
            layer.DropDownItems.Add(Item("New Layer", Keys.Control | Keys.Shift | Keys.N, delegate { NewLayer(); }));
            layer.DropDownItems.Add(Item("Layer Via Copy", Keys.Control | Keys.J, delegate { DuplicateLayer(false); }));
            layer.DropDownItems.Add(Item("Layer Via Cut", Keys.Control | Keys.Shift | Keys.J, delegate { LayerViaCopy(true); }));
            layer.DropDownItems.Add(Item("Duplicate Layer", Keys.None, delegate { EditorLayer s = SelectedLayer(); if (s == null) return; var keep = _selection; _selection = null; DuplicateLayer(true); _selection = keep; }));
            layer.DropDownItems.Add(Item("Delete Layer", Keys.None, delegate { DeleteLayer(); }));
            layer.DropDownItems.Add(Item("Rename Layer…", Keys.None, delegate { RenameLayer(); }));
            layer.DropDownItems.Add(new ToolStripSeparator());
            var style = new ToolStripMenuItem("Layer Style");
            style.DropDownItems.Add(Item("Drop Shadow…", Keys.None, delegate { LayerStyle(0); }));
            style.DropDownItems.Add(Item("Outer Glow…", Keys.None, delegate { LayerStyle(1); }));
            style.DropDownItems.Add(Item("Stroke…", Keys.None, delegate { LayerStyle(2); }));
            style.DropDownItems.Add(Item("Color Overlay…", Keys.None, delegate { LayerStyle(3); }));
            style.DropDownItems.Add(new ToolStripSeparator());
            style.DropDownItems.Add(Item("Clear Layer Style", Keys.None, delegate { ClearLayerStyle(); }));
            layer.DropDownItems.Add(style);
            layer.DropDownItems.Add(Item("Rasterize Layer", Keys.None, delegate { RasterizeLayer(); }));
            layer.DropDownItems.Add(Item("Lock / Unlock Layer", Keys.Control | Keys.OemQuestion, delegate { ToggleLock(); }));
            layer.DropDownItems.Add(new ToolStripSeparator());
            var arrange = new ToolStripMenuItem("Arrange");
            arrange.DropDownItems.Add(Item("Bring to Front", Keys.Control | Keys.Shift | Keys.OemCloseBrackets, delegate { MoveLayerTo(true); }));
            arrange.DropDownItems.Add(Item("Bring Forward", Keys.Control | Keys.OemCloseBrackets, delegate { MoveLayer(1); }));
            arrange.DropDownItems.Add(Item("Send Backward", Keys.Control | Keys.OemOpenBrackets, delegate { MoveLayer(-1); }));
            arrange.DropDownItems.Add(Item("Send to Back", Keys.Control | Keys.Shift | Keys.OemOpenBrackets, delegate { MoveLayerTo(false); }));
            layer.DropDownItems.Add(arrange);
            var align = new ToolStripMenuItem("Align to Canvas");
            align.DropDownItems.Add(Item("Left Edges", Keys.None, delegate { AlignLayer(0); }));
            align.DropDownItems.Add(Item("Horizontal Centers", Keys.None, delegate { AlignLayer(1); }));
            align.DropDownItems.Add(Item("Right Edges", Keys.None, delegate { AlignLayer(2); }));
            align.DropDownItems.Add(new ToolStripSeparator());
            align.DropDownItems.Add(Item("Top Edges", Keys.None, delegate { AlignLayer(3); }));
            align.DropDownItems.Add(Item("Vertical Centers", Keys.None, delegate { AlignLayer(4); }));
            align.DropDownItems.Add(Item("Bottom Edges", Keys.None, delegate { AlignLayer(5); }));
            layer.DropDownItems.Add(align);
            layer.DropDownItems.Add(new ToolStripSeparator());
            layer.DropDownItems.Add(Item("Merge Down", Keys.Control | Keys.E, delegate { MergeDown(); }));
            layer.DropDownItems.Add(Item("Merge Visible", Keys.Control | Keys.Shift | Keys.E, delegate { MergeVisible(); }));
            layer.DropDownItems.Add(Item("Flatten Image", Keys.None, delegate { FlattenImage(); }));
            layer.DropDownItems.Add(new ToolStripSeparator());
            layer.DropDownItems.Add(Item("Save Layer as Asset…", Keys.None, delegate { SaveLayerAsAsset(); }));

            var select = new ToolStripMenuItem("Select");
            select.DropDownItems.Add(Item("All", Keys.Control | Keys.A, delegate { SelectAll(); }));
            select.DropDownItems.Add(Item("Deselect", Keys.Control | Keys.D, delegate { Deselect(); }));
            select.DropDownItems.Add(Item("Reselect", Keys.Control | Keys.Shift | Keys.D, delegate { Reselect(); }));
            select.DropDownItems.Add(Item("Inverse", Keys.Control | Keys.Shift | Keys.I, delegate { SelectInverse(); }));
            select.DropDownItems.Add(new ToolStripSeparator());
            select.DropDownItems.Add(Item("Layer Pixels", Keys.None, delegate { SelectLayerPixels(); }));
            select.DropDownItems.Add(new ToolStripSeparator());
            var modify = new ToolStripMenuItem("Modify");
            modify.DropDownItems.Add(Item("Border…", Keys.None, delegate { ModifySelection("Border"); }));
            modify.DropDownItems.Add(Item("Smooth…", Keys.None, delegate { ModifySelection("Smooth"); }));
            modify.DropDownItems.Add(Item("Expand…", Keys.None, delegate { ModifySelection("Expand"); }));
            modify.DropDownItems.Add(Item("Contract…", Keys.None, delegate { ModifySelection("Contract"); }));
            modify.DropDownItems.Add(Item("Feather…", Keys.Shift | Keys.F6, delegate { ModifySelection("Feather"); }));
            select.DropDownItems.Add(modify);
            select.DropDownItems.Add(Item("Grow", Keys.None, delegate { GrowSelection(false); }));
            select.DropDownItems.Add(Item("Similar", Keys.None, delegate { GrowSelection(true); }));

            var filter = new ToolStripMenuItem("Filter");
            _lastFilterItem = Item("Last Filter", Keys.Control | Keys.Alt | Keys.F, delegate { if (_lastFilter != null) _lastFilter(); });
            _lastFilterItem.Enabled = false;
            filter.DropDownItems.Add(_lastFilterItem);
            filter.DropDownItems.Add(new ToolStripSeparator());
            var blur = new ToolStripMenuItem("Blur");
            blur.DropDownItems.Add(Item("Gaussian Blur…", Keys.None, delegate { GaussianBlur(); }));
            blur.DropDownItems.Add(Item("Motion Blur…", Keys.None, delegate { MotionBlur(); }));
            blur.DropDownItems.Add(Item("Box Blur…", Keys.None, delegate { BoxBlur(); }));
            filter.DropDownItems.Add(blur);
            var sharpen = new ToolStripMenuItem("Sharpen");
            sharpen.DropDownItems.Add(Item("Sharpen", Keys.None, delegate { RunInstant("Sharpen", (p, m) => PixelOps.Sharpen(p, 0.5f, m), true); }));
            sharpen.DropDownItems.Add(Item("Sharpen More", Keys.None, delegate { RunInstant("Sharpen More", (p, m) => PixelOps.Sharpen(p, 1.2f, m), true); }));
            sharpen.DropDownItems.Add(Item("Unsharp Mask…", Keys.None, delegate { UnsharpMask(); }));
            filter.DropDownItems.Add(sharpen);
            var noise = new ToolStripMenuItem("Noise");
            noise.DropDownItems.Add(Item("Add Noise…", Keys.None, delegate { AddNoise(); }));
            noise.DropDownItems.Add(Item("Median…", Keys.None, delegate { Median(); }));
            noise.DropDownItems.Add(Item("Reduce Noise", Keys.None, delegate { RunInstant("Reduce Noise", (p, m) => PixelOps.Median(p, 1, m), true); }));
            filter.DropDownItems.Add(noise);
            var pixelate = new ToolStripMenuItem("Pixelate");
            pixelate.DropDownItems.Add(Item("Mosaic…", Keys.None, delegate { Mosaic(); }));
            filter.DropDownItems.Add(pixelate);
            var stylize = new ToolStripMenuItem("Stylize");
            stylize.DropDownItems.Add(Item("Emboss…", Keys.None, delegate { Emboss(); }));
            stylize.DropDownItems.Add(Item("Find Edges", Keys.None, delegate { RunInstant("Find Edges", (p, m) => PixelOps.FindEdges(p, m), true); }));
            stylize.DropDownItems.Add(Item("Solarize", Keys.None, delegate { RunInstant("Solarize", (p, m) => PixelOps.Solarize(p, m), true); }));
            filter.DropDownItems.Add(stylize);
            var other = new ToolStripMenuItem("Other");
            other.DropDownItems.Add(Item("High Pass…", Keys.None, delegate { HighPass(); }));
            other.DropDownItems.Add(Item("Vignette…", Keys.None, delegate { Vignette(); }));
            filter.DropDownItems.Add(other);

            var view = new ToolStripMenuItem("View");
            view.DropDownItems.Add(Item("Zoom In", Keys.Control | Keys.Oemplus, delegate { SetZoom(_zoom * 1.25f); }));
            view.DropDownItems.Add(Item("Zoom Out", Keys.Control | Keys.OemMinus, delegate { SetZoom(_zoom / 1.25f); }));
            view.DropDownItems.Add(Item("Fit on Screen", Keys.Control | Keys.D0, delegate { FitView(); _canvasPanel.Invalidate(); }));
            view.DropDownItems.Add(Item("100%", Keys.Control | Keys.D1, delegate { SetZoom(1f); }));
            view.DropDownItems.Add(Item("200%", Keys.None, delegate { SetZoom(2f); }));
            view.DropDownItems.Add(new ToolStripSeparator());
            _rulersItem = Item("Rulers", Keys.Control | Keys.R, delegate { ToggleRulers(); });
            view.DropDownItems.Add(_rulersItem);
            _extrasItem = Item("Extras (selection edges, transform box)", Keys.Control | Keys.H, delegate { ToggleExtras(); });
            _extrasItem.Checked = true;
            view.DropDownItems.Add(_extrasItem);

            var help = new ToolStripMenuItem("Help");
            help.DropDownItems.Add(Item("Keyboard Shortcuts…", Keys.None, delegate { ShowShortcuts(); }));
            help.DropDownItems.Add(Item("Image Editor Help", Keys.F1, delegate { OpenHelp(); }));
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(Item("Save Diagnostic Snapshot", Keys.F12, delegate { SaveDiagnostic(); }));

            _menu.Items.Add(file);
            _menu.Items.Add(edit);
            _menu.Items.Add(image);
            _menu.Items.Add(layer);
            _menu.Items.Add(select);
            _menu.Items.Add(filter);
            _menu.Items.Add(view);
            _menu.Items.Add(help);
        }

        static ToolStripMenuItem Item(string text, Keys keys, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text, null, onClick) { Padding = new Padding(4, 3, 4, 3) };
            if (keys != Keys.None) item.ShortcutKeys = keys;
            return item;
        }

        static ContextMenuStrip NewContextMenu()
        {
            return new ContextMenuStrip { Renderer = new ModernMenuRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Base };
        }

        void ToggleRulers()
        {
            _showRulers = !_showRulers;
            _rulersItem.Checked = _showRulers;
            if (_viewFitted) FitView();
            _canvasPanel.Invalidate();
        }

        void ToggleExtras()
        {
            _showExtras = !_showExtras;
            _extrasItem.Checked = _showExtras;
            _canvasPanel.Invalidate();
        }

        void OpenHelp()
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HELP.md");
                if (System.IO.File.Exists(path)) System.Diagnostics.Process.Start(path);
                else System.Diagnostics.Process.Start("https://github.com/Mahi-BD/MicroApp/blob/main/HELP.md#image-editor");
            }
            catch { }
        }

        // ============================================================ context menus

        void ShowCanvasContextMenu(Point screen, PointF cp)
        {
            ContextMenuStrip m = NewContextMenu();
            if (_xf != null)
            {
                foreach (TransformMode mode in new[] { TransformMode.Free, TransformMode.Scale, TransformMode.Rotate, TransformMode.Skew, TransformMode.Distort, TransformMode.Perspective })
                {
                    TransformMode md = mode;
                    var it = new ToolStripMenuItem(mode == TransformMode.Free ? "Free Transform" : mode.ToString(), null, delegate { SetTransformMode(md); }) { Checked = _xf.Mode == mode };
                    m.Items.Add(it);
                }
                m.Items.Add(new ToolStripSeparator());
                foreach (string q in new[] { "Rotate 180°", "Rotate 90° CW", "Rotate 90° CCW", "Flip Horizontal", "Flip Vertical" })
                {
                    string what = q;
                    m.Items.Add(new ToolStripMenuItem(q, null, delegate { QuickTransform(what); }));
                }
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Commit (Enter)", null, delegate { CommitTransform(); }));
                m.Items.Add(new ToolStripMenuItem("Cancel (Esc)", null, delegate { CancelTransform(); }));
                m.Show(_canvasPanel, screen);
                return;
            }

            if (_tool == Tool.Move)
            {
                // the layers under the cursor, top first - click one to select it
                var under = new List<int>();
                for (int i = _layers.Count - 1; i >= 0; i--) if (_layers[i].Visible && _layers[i].HitTest(cp)) under.Add(i);
                foreach (int idx in under)
                {
                    int li = idx;
                    var it = new ToolStripMenuItem(_layers[li].Name, null, delegate { _sel = li; AfterDocumentChange(); }) { Checked = li == _sel };
                    m.Items.Add(it);
                }
                if (under.Count > 0) m.Items.Add(new ToolStripSeparator());
                EditorLayer sel = SelectedLayer();
                if (sel != null)
                {
                    m.Items.Add(new ToolStripMenuItem("Free Transform", null, delegate { BeginTransform(sel, TransformMode.Free); }) { ShortcutKeyDisplayString = "Ctrl+T" });
                    m.Items.Add(new ToolStripMenuItem("Duplicate Layer", null, delegate { DuplicateLayer(true); }));
                    m.Items.Add(new ToolStripMenuItem("Delete Layer", null, delegate { DeleteLayer(); }));
                    m.Items.Add(new ToolStripMenuItem("Rename Layer…", null, delegate { RenameLayer(); }));
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add(new ToolStripMenuItem("Layer Style…", null, delegate { LayerStyle(0); }));
                    m.Items.Add(BlendSubmenu(sel));
                    m.Items.Add(new ToolStripMenuItem("Rasterize Layer", null, delegate { RasterizeLayer(); }));
                    m.Items.Add(new ToolStripMenuItem(sel.Locked ? "Unlock Layer" : "Lock Layer", null, delegate { ToggleLock(); }));
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add(new ToolStripMenuItem("Bring to Front", null, delegate { MoveLayerTo(true); }) { ShortcutKeyDisplayString = "Shift+Ctrl+]" });
                    m.Items.Add(new ToolStripMenuItem("Send to Back", null, delegate { MoveLayerTo(false); }) { ShortcutKeyDisplayString = "Shift+Ctrl+[" });
                    m.Items.Add(new ToolStripMenuItem("Merge Down", null, delegate { MergeDown(); }) { ShortcutKeyDisplayString = "Ctrl+E", Enabled = _sel > 0 });
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add(new ToolStripMenuItem("Flip Horizontal", null, delegate { QuickTransform("Flip Horizontal"); }));
                    m.Items.Add(new ToolStripMenuItem("Flip Vertical", null, delegate { QuickTransform("Flip Vertical"); }));
                    m.Items.Add(new ToolStripMenuItem("Rotate 90° Clockwise", null, delegate { QuickTransform("Rotate 90° CW"); }));
                }
                else
                {
                    m.Items.Add(new ToolStripMenuItem("Paste", null, delegate { PasteFromClipboard(false, false); }) { ShortcutKeyDisplayString = "Ctrl+V" });
                    m.Items.Add(new ToolStripMenuItem("New Layer", null, delegate { NewLayer(); }) { ShortcutKeyDisplayString = "Shift+Ctrl+N" });
                }
                if (HasSelection)
                {
                    m.Items.Add(new ToolStripSeparator());
                    AddSelectionItems(m);
                }
            }
            else if (IsSelectionTool(_tool))
            {
                AddSelectionItems(m);
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Layer Via Copy", null, delegate { LayerViaCopy(false); }) { ShortcutKeyDisplayString = "Ctrl+J", Enabled = HasSelection });
                m.Items.Add(new ToolStripMenuItem("Layer Via Cut", null, delegate { LayerViaCopy(true); }) { ShortcutKeyDisplayString = "Shift+Ctrl+J", Enabled = HasSelection });
                m.Items.Add(new ToolStripMenuItem("New Layer", null, delegate { NewLayer(); }) { ShortcutKeyDisplayString = "Shift+Ctrl+N" });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Free Transform", null, delegate { BeginTransform(SelectedLayer(), TransformMode.Free); }) { ShortcutKeyDisplayString = "Ctrl+T" });
                m.Items.Add(new ToolStripMenuItem("Fill…", null, delegate { FillCommand(); }) { ShortcutKeyDisplayString = "Shift+F5" });
                m.Items.Add(new ToolStripMenuItem("Stroke…", null, delegate { StrokeCommand(); }));
                m.Items.Add(new ToolStripMenuItem("Crop", null, delegate { CropToSelection(); }) { Enabled = HasSelection });
            }
            else if (_tool == Tool.Crop)
            {
                m.Items.Add(new ToolStripMenuItem("Apply Crop", null, delegate { ApplyCrop(); }) { ShortcutKeyDisplayString = "Enter", Enabled = _cropRect.HasValue });
                m.Items.Add(new ToolStripMenuItem("Cancel Crop", null, delegate { _cropRect = null; RelayoutOptions(); _canvasPanel.Invalidate(); }) { ShortcutKeyDisplayString = "Esc", Enabled = _cropRect.HasValue });
                m.Items.Add(new ToolStripMenuItem("Crop to Selection", null, delegate { CropToSelection(); }) { Enabled = HasSelection });
                m.Items.Add(new ToolStripSeparator());
                string[] ratios = { "Unconstrained", "1 : 1 (Square)", "4 : 3", "16 : 9", "3 : 2", "Original Ratio" };
                for (int i = 0; i < ratios.Length; i++)
                {
                    int r = i;
                    m.Items.Add(new ToolStripMenuItem(ratios[i], null, delegate { _optCropRatio.SelectedIndex = r; }) { Checked = _cropRatio == i });
                }
            }
            else if (IsBrushTool(_tool))
            {
                var sizes = new ToolStripMenuItem("Brush Size");
                foreach (int s in new[] { 3, 5, 10, 20, 40, 80, 150, 300 })
                {
                    int size = s;
                    sizes.DropDownItems.Add(new ToolStripMenuItem(size + " px", null, delegate { _brushSize = size; SyncBrushOptions(); _canvasPanel.Invalidate(); }) { Checked = _brushSize == size });
                }
                m.Items.Add(sizes);
                var hard = new ToolStripMenuItem("Hardness");
                foreach (int h in new[] { 0, 25, 50, 75, 100 })
                {
                    int hv = h;
                    hard.DropDownItems.Add(new ToolStripMenuItem(hv + "%", null, delegate { _brushHardness = hv; SyncBrushOptions(); _canvasPanel.Invalidate(); }) { Checked = _brushHardness == hv });
                }
                m.Items.Add(hard);
                var opacity = new ToolStripMenuItem("Opacity");
                foreach (int o in new[] { 10, 25, 50, 75, 100 })
                {
                    int ov = o;
                    opacity.DropDownItems.Add(new ToolStripMenuItem(ov + "%", null, delegate { _brushOpacity = ov; SyncBrushOptions(); }) { Checked = _brushOpacity == ov });
                }
                m.Items.Add(opacity);
                if (_tool == Tool.Clone) { m.Items.Add(new ToolStripSeparator()); m.Items.Add(new ToolStripMenuItem("Set Clone Source Here", null, delegate { SetCloneSource(cp); })); }
                if (HasSelection) { m.Items.Add(new ToolStripSeparator()); AddSelectionItems(m); }
            }
            else if (_tool == Tool.Text)
            {
                EditorLayer sel = SelectedLayer();
                var t = sel as TextLayer;
                if (t != null) m.Items.Add(new ToolStripMenuItem("Edit Text", null, delegate { PushUndo("Edit Type"); BeginInlineEdit(t, false); }));
                m.Items.Add(new ToolStripMenuItem("Free Transform", null, delegate { BeginTransform(sel, TransformMode.Free); }) { Enabled = sel != null });
                m.Items.Add(new ToolStripMenuItem("Rasterize Type", null, delegate { RasterizeLayer(); }) { Enabled = t != null });
            }
            else
            {
                m.Items.Add(new ToolStripMenuItem("Paste", null, delegate { PasteFromClipboard(false, false); }) { ShortcutKeyDisplayString = "Ctrl+V" });
                m.Items.Add(new ToolStripMenuItem("Free Transform", null, delegate { BeginTransform(SelectedLayer(), TransformMode.Free); }) { ShortcutKeyDisplayString = "Ctrl+T", Enabled = SelectedLayer() != null });
                if (HasSelection) { m.Items.Add(new ToolStripSeparator()); AddSelectionItems(m); }
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Fit on Screen", null, delegate { FitView(); _canvasPanel.Invalidate(); }) { ShortcutKeyDisplayString = "Ctrl+0" });
            m.Items.Add(new ToolStripMenuItem("100%", null, delegate { SetZoom(1f); }) { ShortcutKeyDisplayString = "Ctrl+1" });
            m.Show(_canvasPanel, screen);
        }

        void AddSelectionItems(ContextMenuStrip m)
        {
            m.Items.Add(new ToolStripMenuItem("Select All", null, delegate { SelectAll(); }) { ShortcutKeyDisplayString = "Ctrl+A" });
            m.Items.Add(new ToolStripMenuItem("Deselect", null, delegate { Deselect(); }) { ShortcutKeyDisplayString = "Ctrl+D", Enabled = HasSelection });
            m.Items.Add(new ToolStripMenuItem("Reselect", null, delegate { Reselect(); }) { ShortcutKeyDisplayString = "Shift+Ctrl+D", Enabled = _lastSelection != null });
            m.Items.Add(new ToolStripMenuItem("Select Inverse", null, delegate { SelectInverse(); }) { ShortcutKeyDisplayString = "Shift+Ctrl+I", Enabled = HasSelection });
            m.Items.Add(new ToolStripMenuItem("Feather…", null, delegate { ModifySelection("Feather"); }) { ShortcutKeyDisplayString = "Shift+F6", Enabled = HasSelection });
            m.Items.Add(new ToolStripMenuItem("Expand…", null, delegate { ModifySelection("Expand"); }) { Enabled = HasSelection });
            m.Items.Add(new ToolStripMenuItem("Contract…", null, delegate { ModifySelection("Contract"); }) { Enabled = HasSelection });
        }

        ToolStripMenuItem BlendSubmenu(EditorLayer layer)
        {
            var blend = new ToolStripMenuItem("Blending Mode");
            for (int i = 0; i < BlendModes.Names.Length; i++)
            {
                int mode = i;
                var it = new ToolStripMenuItem(BlendModes.Names[i], null, delegate
                {
                    PushUndo("Blending Mode");
                    layer.Blend = (BlendMode)mode;
                    layer.DropCache();
                    AfterDocumentChange();
                }) { Checked = (int)layer.Blend == i };
                blend.DropDownItems.Add(it);
                if (i == 0 || i == 4 || i == 8 || i == 11 || i == 13) blend.DropDownItems.Add(new ToolStripSeparator());
            }
            return blend;
        }

        void ShowLayerContextMenu(Control host, Point at)
        {
            ContextMenuStrip m = NewContextMenu();
            EditorLayer sel = SelectedLayer();
            m.Items.Add(new ToolStripMenuItem("New Layer", null, delegate { NewLayer(); }) { ShortcutKeyDisplayString = "Shift+Ctrl+N" });
            if (sel != null)
            {
                m.Items.Add(new ToolStripMenuItem("Duplicate Layer", null, delegate { DuplicateLayer(true); }));
                m.Items.Add(new ToolStripMenuItem("Delete Layer", null, delegate { DeleteLayer(); }));
                m.Items.Add(new ToolStripMenuItem("Rename Layer…", null, delegate { RenameLayer(); }));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Layer Style…", null, delegate { LayerStyle(0); }));
                m.Items.Add(BlendSubmenu(sel));
                m.Items.Add(new ToolStripMenuItem(sel.Locked ? "Unlock Layer" : "Lock Layer", null, delegate { ToggleLock(); }) { ShortcutKeyDisplayString = "Ctrl+/" });
                m.Items.Add(new ToolStripMenuItem("Rasterize Layer", null, delegate { RasterizeLayer(); }));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Bring to Front", null, delegate { MoveLayerTo(true); }));
                m.Items.Add(new ToolStripMenuItem("Bring Forward", null, delegate { MoveLayer(1); }));
                m.Items.Add(new ToolStripMenuItem("Send Backward", null, delegate { MoveLayer(-1); }));
                m.Items.Add(new ToolStripMenuItem("Send to Back", null, delegate { MoveLayerTo(false); }));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Merge Down", null, delegate { MergeDown(); }) { Enabled = _sel > 0 });
                m.Items.Add(new ToolStripMenuItem("Merge Visible", null, delegate { MergeVisible(); }));
                m.Items.Add(new ToolStripMenuItem("Flatten Image", null, delegate { FlattenImage(); }));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Select Layer Pixels", null, delegate { SelectLayerPixels(); }));
                m.Items.Add(new ToolStripMenuItem("Save Layer as Asset…", null, delegate { SaveLayerAsAsset(); }));
            }
            m.Show(host, at);
        }

        // ================================================================= select

        void SelectAll()
        {
            if (!EnsureDoc()) return;
            PushUndo("Select All");
            SetSelection(EditorSelection.All(_canvas));
        }

        void Deselect()
        {
            if (!HasSelection) return;
            PushUndo("Deselect");
            SetSelection(null);
        }

        void Reselect()
        {
            if (_lastSelection == null || _lastSelection.Width != _canvas.Width || _lastSelection.Height != _canvas.Height) { Toast.Show("Nothing to reselect."); return; }
            PushUndo("Reselect");
            EditorSelection s = _lastSelection;
            SetSelection(s);
        }

        void SelectInverse()
        {
            if (!EnsureDoc()) return;
            PushUndo("Select Inverse");
            SetSelection(_selection == null ? EditorSelection.All(_canvas) : _selection.Invert());
        }

        /// <summary>Select &gt; Layer Pixels: the opaque part of the current layer (Ctrl-click on a thumbnail in Photoshop).</summary>
        void SelectLayerPixels()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return; }
            using (Bitmap alone = sel.RenderAlone(_canvas, false))
            {
                Pixels p = Pixels.From(alone);
                var mask = new byte[p.Width * p.Height];
                for (int i = 0, k = 0; k < mask.Length; i += 4, k++) mask[k] = p.Data[i + 3];
                PushUndo("Select Layer Pixels");
                SetSelection(EditorSelection.FromMask(mask, _canvas, 0));
            }
        }

        void ModifySelection(string how)
        {
            if (!HasSelection) { Toast.Show("Make a selection first."); return; }
            string label = how == "Feather" ? "Feather Radius" : how == "Border" ? "Width" : how == "Smooth" ? "Sample Radius" : how + " By";
            int? v = NumberPrompt.Ask(this, how + " Selection", label, 1, how == "Feather" ? 250 : 500, how == "Feather" ? 5 : 4, "pixels");
            if (!v.HasValue) return;
            PushUndo(how + " Selection");
            EditorSelection s = _selection;
            switch (how)
            {
                case "Feather": s = s.Feathered(v.Value); break;
                case "Border": s = s.Border(v.Value); break;
                case "Smooth": s = s.Smooth(v.Value); break;
                case "Expand": s = s.Expand(v.Value); break;
                case "Contract": s = s.Contract(v.Value); break;
            }
            SetSelection(s);
        }

        /// <summary>Select &gt; Grow / Similar: extends the selection to colours like the ones already in it (wand tolerance).</summary>
        void GrowSelection(bool everywhere)
        {
            if (!HasSelection) { Toast.Show("Make a selection first."); return; }
            Pixels px = Pixels.From(Composite());
            byte[] cur = _selection.Mask;
            byte[] result = (byte[])cur.Clone();
            // seed from a sparse sample of selected pixels so a big selection stays quick
            Rectangle b = _selection.Bounds;
            int step = Math.Max(1, (int)Math.Sqrt(b.Width * (long)b.Height / 400.0));
            int seeds = 0;
            for (int y = b.Top; y < b.Bottom; y += step)
                for (int x = b.Left; x < b.Right; x += step)
                {
                    if (cur[y * _canvas.Width + x] < 128) continue;
                    byte[] m = PixelOps.FloodMask(px, x, y, _wandTolerance, !everywhere);
                    for (int i = 0; i < result.Length; i++) if (m[i] > result[i]) result[i] = m[i];
                    if (++seeds > 400) break;
                }
            PushUndo(everywhere ? "Similar" : "Grow");
            SetSelection(EditorSelection.FromMask(result, _canvas, 0));
        }

        // =================================================================== edit

        void FillCommand()
        {
            if (!EnsureDoc()) return;
            CommitTransform();
            FillDialog d = FillDialog.Ask(this, _fg, _bg);
            if (d == null) return;
            PushUndo("Fill");
            RasterLayer target = PaintTarget(true, "Fill");
            if (target == null) { PopUndo(); d.Dispose(); return; }
            Pixels px = Pixels.From(target.Image);
            byte[] mask = HasSelection ? _selection.MaskForLayer(target) : null;
            if (d.PreserveTransparency)
            {
                var keep = new byte[px.Width * px.Height];
                for (int i = 0, k = 0; k < keep.Length; i += 4, k++) keep[k] = (byte)((mask == null ? 255 : mask[k]) * px.Data[i + 3] / 255);
                mask = keep;
            }
            PixelOps.Fill(px, d.ResolvedColor(_fg, _bg), d.FillOpacity / 100f, mask, d.Mode);
            target.Image = px.ToBitmap();
            d.Dispose();
            AfterDocumentChange();
        }

        /// <summary>Edit &gt; Stroke: paints a band along the selection edge onto the layer.</summary>
        void StrokeCommand()
        {
            if (!EnsureDoc()) return;
            if (!HasSelection) { Toast.Show("Make a selection first."); return; }
            CommitTransform();
            StrokeDialog d = StrokeDialog.Ask(this, _fg);
            if (d == null) return;
            PushUndo("Stroke");
            RasterLayer target = PaintTarget(true, "Stroke");
            if (target == null) { PopUndo(); d.Dispose(); return; }
            // the band in canvas space, then mapped into the layer's pixels through a selection
            byte[] hard = new byte[_selection.Mask.Length];
            for (int i = 0; i < hard.Length; i++) hard[i] = _selection.Mask[i] >= 128 ? (byte)255 : (byte)0;
            int wdt = d.StrokeWidth;
            byte[] dOut = EditorSelection.DistanceOutside(hard, _canvas.Width, _canvas.Height, wdt + 2);
            byte[] dIn = EditorSelection.DistanceInside(hard, _canvas.Width, _canvas.Height, wdt + 2);
            var band = new byte[hard.Length];
            for (int i = 0; i < band.Length; i++)
            {
                bool inside = hard[i] > 0;
                bool on;
                switch (d.StrokeLocation)
                {
                    case 0: on = inside && dIn[i] <= wdt; break;
                    case 2: on = !inside && dOut[i] <= wdt; break;
                    default: on = inside ? dIn[i] <= (wdt + 1) / 2 : dOut[i] <= wdt / 2; break;
                }
                band[i] = on ? (byte)255 : (byte)0;
            }
            EditorSelection bandSel = EditorSelection.FromMask(band, _canvas, 0);
            byte[] mask = bandSel.MaskForLayer(target);
            Pixels px = Pixels.From(target.Image);
            PixelOps.Fill(px, d.StrokeColor, d.StrokeOpacity / 100f, mask ?? FullMask(px), BlendMode.Normal);
            target.Image = px.ToBitmap();
            d.Dispose();
            AfterDocumentChange();
        }

        static byte[] FullMask(Pixels p)
        {
            var m = new byte[p.Width * p.Height];
            for (int i = 0; i < m.Length; i++) m[i] = 255;
            return m;
        }

        // ============================================================ adjustments

        /// <summary>The image layer an adjustment or filter works on; text and shapes offer to become pixels.</summary>
        RasterLayer AdjustTarget(string name)
        {
            if (!EnsureDoc()) return null;
            CommitInlineEdit();
            CommitTransform();
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return null; }
            if (sel.Locked) { Toast.Show("The layer is locked."); return null; }
            var raster = sel as RasterLayer;
            if (raster == null)
            {
                if (!ModernDialog.Confirm("Rasterize the layer?", name + " works on pixels. The " + sel.KindLabel.ToLowerInvariant() + " layer will be converted and can no longer be edited as " + sel.KindLabel.ToLowerInvariant() + ".", "Rasterize", "Cancel"))
                    return null;
                PushUndo("Rasterize Layer");
                raster = sel.Rasterize(_canvas);
                _layers[_sel] = raster;
                AfterDocumentChange();
            }
            return raster;
        }

        /// <summary>An adjustment or filter with a live-preview dialog; big layers preview on a smaller copy.</summary>
        void RunAdjustment(string name, Func<AdjustDialog> build, AdjustApply apply, bool isFilter)
        {
            RasterLayer layer = AdjustTarget(name);
            if (layer == null) return;
            PushUndo(name);
            Bitmap original = layer.Image;
            float scale = 1f;
            Bitmap small = original;
            long n = (long)original.Width * original.Height;
            if (n > 1_200_000)
            {
                scale = (float)Math.Sqrt(1_200_000.0 / n);
                small = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)), PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(original, 0, 0, small.Width, small.Height);
                }
            }
            layer.Image = small;
            layer.ContentVersion++;
            byte[] smallMask = HasSelection ? _selection.MaskForLayer(layer) : null;
            Pixels smallPx = Pixels.From(small);
            Bitmap current = small;

            AdjustDialog d = build();
            d.Changed += delegate
            {
                Bitmap next;
                if (!d.PreviewOn) next = small;
                else
                {
                    Pixels work = smallPx.Clone();
                    try { apply(work, d, scale, smallMask); } catch { }
                    next = work.ToBitmap();
                }
                if (current != small && current != next) current.Dispose();
                current = next;
                layer.Image = current;
                layer.ContentVersion++;
                InvalidateDoc();
            };
            d.Kick();
            DialogResult result = d.ShowDialog(this);
            if (current != small) current.Dispose();
            if (small != original) small.Dispose();
            layer.Image = original;
            layer.ContentVersion++;

            if (result == DialogResult.OK)
            {
                ApplyFull(layer, apply, d, name);
                if (isFilter)
                {
                    _lastFilter = delegate
                    {
                        RasterLayer t = AdjustTarget(name);
                        if (t == null) return;
                        PushUndo(name);
                        ApplyFull(t, apply, d, name);
                        AfterDocumentChange();
                    };
                    _lastFilterName = name;
                    _lastFilterItem.Text = "Last Filter: " + name;
                    _lastFilterItem.Enabled = true;
                }
                else d.Dispose();
            }
            else
            {
                PopUndo();
                d.Dispose();
            }
            AfterDocumentChange();
        }

        void ApplyFull(RasterLayer layer, AdjustApply apply, AdjustDialog d, string name)
        {
            byte[] mask = HasSelection ? _selection.MaskForLayer(layer) : null;
            Pixels px = Pixels.From(layer.Image);
            try { apply(px, d, 1f, mask); }
            catch (Exception ex) { ModernDialog.Info(name + " failed", ex.Message); return; }
            layer.Image = px.ToBitmap();
            layer.ContentVersion++;
        }

        /// <summary>An adjustment with no dialog (Invert, Auto Tone, Desaturate...).</summary>
        void RunInstant(string name, Action<Pixels, byte[]> op, bool isFilter = false)
        {
            RasterLayer layer = AdjustTarget(name);
            if (layer == null) return;
            PushUndo(name);
            byte[] mask = HasSelection ? _selection.MaskForLayer(layer) : null;
            Pixels px = Pixels.From(layer.Image);
            try { op(px, mask); }
            catch (Exception ex) { ModernDialog.Info(name + " failed", ex.Message); PopUndo(); return; }
            layer.Image = px.ToBitmap();
            layer.ContentVersion++;
            if (isFilter)
            {
                _lastFilter = delegate { RunInstant(name, op, false); };
                _lastFilterName = name;
                _lastFilterItem.Text = "Last Filter: " + name;
                _lastFilterItem.Enabled = true;
            }
            AfterDocumentChange();
        }

        void BrightnessContrast()
        {
            RunAdjustment("Brightness/Contrast", delegate
            {
                var d = new AdjustDialog("Brightness/Contrast");
                d.AddSlider("b", "Brightness", -150, 150, 0);
                d.AddSlider("c", "Contrast", -50, 100, 0);
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.BrightnessContrast(px, d.Value("b"), d.Value("c"), m), false);
        }

        void Levels()
        {
            RasterLayer target = SelectedRaster();
            var ctl = new LevelsControl();
            if (target != null) ctl.Histogram = PixelOps.LumaHistogram(Pixels.From(target.Image));
            RunAdjustment("Levels", delegate
            {
                var d = new AdjustDialog("Levels");
                d.AddCombo("ch", "Channel", new[] { "RGB", "Red", "Green", "Blue" }, 0);
                d.AddCustom(ctl, ctl.Height);
                var inB = d.AddSlider("ib", "Input black", 0, 253, 0);
                var inW = d.AddSlider("iw", "Input white", 2, 255, 255);
                var gam = d.AddSlider("g", "Midtones (gamma ×100)", 10, 999, 100);
                var outB = d.AddSlider("ob", "Output black", 0, 255, 0);
                var outW = d.AddSlider("ow", "Output white", 0, 255, 255);
                bool sync = false;
                ctl.Changed += delegate
                {
                    sync = true;
                    d.SetValue("ib", ctl.InBlack); d.SetValue("iw", ctl.InWhite); d.SetValue("g", (int)Math.Round(ctl.Gamma * 100));
                    d.SetValue("ob", ctl.OutBlack); d.SetValue("ow", ctl.OutWhite);
                    sync = false;
                };
                EventHandler fromSliders = delegate
                {
                    if (sync) return;
                    ctl.Set(d.Value("ib"), Math.Max(d.Value("ib") + 2, d.Value("iw")), d.Value("g") / 100f, d.Value("ob"), d.Value("ow"));
                };
                inB.ValueChanged += fromSliders; inW.ValueChanged += fromSliders; gam.ValueChanged += fromSliders; outB.ValueChanged += fromSliders; outW.ValueChanged += fromSliders;
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Levels(px, d.Value("ib"), Math.Max(d.Value("ib") + 2, d.Value("iw")), d.Value("g") / 100f, d.Value("ob"), d.Value("ow"), d.Index("ch"), m), false);
        }

        void Curves()
        {
            RasterLayer target = SelectedRaster();
            var ctl = new CurveControl();
            if (target != null) ctl.Histogram = PixelOps.LumaHistogram(Pixels.From(target.Image));
            RunAdjustment("Curves", delegate
            {
                var d = new AdjustDialog("Curves");
                var ch = d.AddCombo("ch", "Channel", new[] { "RGB", "Red", "Green", "Blue" }, 0);
                d.AddCustom(ctl, ctl.Height);
                ch.SelectedIndexChanged += delegate { ctl.Channel = ch.SelectedIndex; ctl.Invalidate(); };
                ctl.Changed += delegate { d.Kick(); };
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Curves(px,
                    ctl.IsIdentity(0) ? null : PixelOps.CurveLut(ctl.Channels[0]),
                    ctl.IsIdentity(1) ? null : PixelOps.CurveLut(ctl.Channels[1]),
                    ctl.IsIdentity(2) ? null : PixelOps.CurveLut(ctl.Channels[2]),
                    ctl.IsIdentity(3) ? null : PixelOps.CurveLut(ctl.Channels[3]), m), false);
        }

        void Exposure()
        {
            RunAdjustment("Exposure", delegate
            {
                var d = new AdjustDialog("Exposure");
                d.AddSlider("e", "Exposure (×100)", -500, 500, 0);
                d.AddSlider("o", "Offset (×1000)", -500, 500, 0);
                d.AddSlider("g", "Gamma correction (×100)", 10, 300, 100);
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Exposure(px, d.Value("e") / 100f, d.Value("o") / 1000f, d.Value("g") / 100f, m), false);
        }

        void Vibrance()
        {
            RunAdjustment("Vibrance", delegate
            {
                var d = new AdjustDialog("Vibrance");
                d.AddSlider("v", "Vibrance", -100, 100, 0);
                d.AddSlider("s", "Saturation", -100, 100, 0);
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Vibrance(px, d.Value("v"), d.Value("s"), m), false);
        }

        void HueSaturation()
        {
            RunAdjustment("Hue/Saturation", delegate
            {
                var d = new AdjustDialog("Hue/Saturation");
                d.AddSlider("h", "Hue", -180, 180, 0, "°");
                d.AddSlider("s", "Saturation", -100, 100, 0);
                d.AddSlider("l", "Lightness", -100, 100, 0);
                d.AddCheck("c", "Colorize", false);
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.HueSaturation(px, d.Value("h"), d.Value("s"), d.Value("l"), d.Check("c"), m), false);
        }

        void ColorBalance()
        {
            RunAdjustment("Color Balance", delegate
            {
                var d = new AdjustDialog("Color Balance");
                d.AddCombo("t", "Tone", new[] { "Shadows", "Midtones", "Highlights" }, 1);
                d.AddSlider("r", "Cyan  ⟷  Red", -100, 100, 0);
                d.AddSlider("g", "Magenta  ⟷  Green", -100, 100, 0);
                d.AddSlider("b", "Yellow  ⟷  Blue", -100, 100, 0);
                d.AddCheck("p", "Preserve Luminosity", true);
                d.Finish();
                return d;
            }, delegate(Pixels px, AdjustDialog d, float s, byte[] m)
            {
                var zero = new int[3];
                var vals = new[] { d.Value("r"), d.Value("g"), d.Value("b") };
                int tone = d.Index("t");
                PixelOps.ColorBalance(px, tone == 0 ? vals : zero, tone == 1 ? vals : zero, tone == 2 ? vals : zero, d.Check("p"), m);
            }, false);
        }

        void BlackWhite()
        {
            RunAdjustment("Black & White", delegate
            {
                var d = new AdjustDialog("Black & White");
                d.AddSlider("r", "Reds", -200, 300, 40);
                d.AddSlider("y", "Yellows", -200, 300, 60);
                d.AddSlider("g", "Greens", -200, 300, 40);
                d.AddSlider("c", "Cyans", -200, 300, 60);
                d.AddSlider("b", "Blues", -200, 300, 20);
                d.AddSlider("m", "Magentas", -200, 300, 80);
                d.AddCheck("t", "Tint", false);
                d.AddColor("tc", "Tint colour", Color.FromArgb(225, 211, 179));
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.BlackWhite(px, new[] { d.Value("r"), d.Value("y"), d.Value("g"), d.Value("c"), d.Value("b"), d.Value("m") }, d.Check("t"), d.ColorOf("tc"), m), false);
        }

        void PhotoFilter()
        {
            RunAdjustment("Photo Filter", delegate
            {
                var d = new AdjustDialog("Photo Filter");
                var preset = d.AddCombo("f", "Filter", new[] { "Warming Filter (85)", "Warming Filter (81)", "Cooling Filter (80)", "Cooling Filter (82)", "Sepia", "Red", "Orange", "Yellow", "Green", "Cyan", "Blue", "Violet", "Magenta", "Custom" }, 0);
                var sw = d.AddColor("c", "Colour", Color.FromArgb(236, 138, 0));
                d.AddSlider("d", "Density", 1, 100, 25, "%");
                d.AddCheck("p", "Preserve Luminosity", true);
                preset.SelectedIndexChanged += delegate
                {
                    Color[] presets =
                    {
                        Color.FromArgb(236, 138, 0), Color.FromArgb(235, 177, 19), Color.FromArgb(0, 109, 255), Color.FromArgb(0, 181, 255),
                        Color.FromArgb(172, 122, 51), Color.FromArgb(234, 26, 26), Color.FromArgb(243, 130, 24), Color.FromArgb(250, 224, 20),
                        Color.FromArgb(25, 200, 25), Color.FromArgb(29, 203, 234), Color.FromArgb(29, 53, 234), Color.FromArgb(155, 29, 234), Color.FromArgb(227, 24, 227)
                    };
                    if (preset.SelectedIndex < presets.Length) sw.Color = presets[preset.SelectedIndex];
                    d.Kick();
                };
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.PhotoFilter(px, d.ColorOf("c"), d.Value("d"), d.Check("p"), m), false);
        }

        void Posterize()
        {
            RunAdjustment("Posterize", delegate
            {
                var d = new AdjustDialog("Posterize");
                d.AddSlider("l", "Levels", 2, 255, 4);
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Posterize(px, d.Value("l"), m), false);
        }

        void Threshold()
        {
            RunAdjustment("Threshold", delegate
            {
                var d = new AdjustDialog("Threshold");
                d.AddSlider("t", "Threshold Level", 1, 255, 128);
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Threshold(px, d.Value("t"), m), false);
        }

        // ================================================================ filters

        void GaussianBlur()
        {
            RunAdjustment("Gaussian Blur", delegate
            {
                var d = new AdjustDialog("Gaussian Blur");
                d.AddSlider("r", "Radius (×10)", 1, 2500, 30, "px");
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.GaussianBlur(px, d.Value("r") / 10f * s, m), true);
        }

        void BoxBlur()
        {
            RunAdjustment("Box Blur", delegate
            {
                var d = new AdjustDialog("Box Blur");
                d.AddSlider("r", "Radius", 1, 200, 5, "px");
                d.Finish();
                return d;
            }, delegate(Pixels px, AdjustDialog d, float s, byte[] m)
            {
                int r = Math.Max(1, (int)Math.Round(d.Value("r") * s));
                if (m == null) { PixelOps.Premultiply(px); PixelOps.BoxBlur(px, r); PixelOps.Unpremultiply(px); }
                else
                {
                    Pixels orig = px.Clone();
                    PixelOps.Premultiply(px); PixelOps.BoxBlur(px, r); PixelOps.Unpremultiply(px);
                    PixelOps.MixByMask(orig, px, m);
                }
            }, true);
        }

        void MotionBlur()
        {
            RunAdjustment("Motion Blur", delegate
            {
                var d = new AdjustDialog("Motion Blur");
                d.AddSlider("a", "Angle", -90, 90, 0, "°");
                d.AddSlider("d", "Distance", 1, 500, 20, "px");
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.MotionBlur(px, d.Value("a"), Math.Max(1, (int)Math.Round(d.Value("d") * s)), m), true);
        }

        void UnsharpMask()
        {
            RunAdjustment("Unsharp Mask", delegate
            {
                var d = new AdjustDialog("Unsharp Mask");
                d.AddSlider("a", "Amount", 1, 500, 100, "%");
                d.AddSlider("r", "Radius (×10)", 1, 1000, 15, "px");
                d.AddSlider("t", "Threshold", 0, 255, 0, "levels");
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.UnsharpMask(px, d.Value("a"), Math.Max(0.3f, d.Value("r") / 10f * s), d.Value("t"), m), true);
        }

        void AddNoise()
        {
            RunAdjustment("Add Noise", delegate
            {
                var d = new AdjustDialog("Add Noise");
                d.AddSlider("a", "Amount", 1, 100, 10, "%");
                d.AddCombo("dist", "Distribution", new[] { "Uniform", "Gaussian" }, 0);
                d.AddCheck("m", "Monochromatic", false);
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.AddNoise(px, d.Value("a"), d.Check("m"), d.Index("dist") == 1, m), true);
        }

        void Median()
        {
            RunAdjustment("Median", delegate
            {
                var d = new AdjustDialog("Median");
                d.AddSlider("r", "Radius", 1, 8, 2, "px");
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Median(px, Math.Max(1, (int)Math.Round(d.Value("r") * s)), m), true);
        }

        void Mosaic()
        {
            RunAdjustment("Mosaic", delegate
            {
                var d = new AdjustDialog("Mosaic");
                d.AddSlider("c", "Cell Size", 2, 200, 10, "square");
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Mosaic(px, Math.Max(2, (int)Math.Round(d.Value("c") * s)), m), true);
        }

        void Emboss()
        {
            RunAdjustment("Emboss", delegate
            {
                var d = new AdjustDialog("Emboss");
                d.AddSlider("a", "Angle", -180, 180, 135, "°");
                d.AddSlider("h", "Height", 1, 20, 3, "px");
                d.AddSlider("m", "Amount", 1, 500, 100, "%");
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Emboss(px, d.Value("a"), Math.Max(1, (int)Math.Round(d.Value("h") * s)), d.Value("m"), m), true);
        }

        void HighPass()
        {
            RunAdjustment("High Pass", delegate
            {
                var d = new AdjustDialog("High Pass");
                d.AddSlider("r", "Radius (×10)", 1, 2500, 100, "px");
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.HighPass(px, d.Value("r") / 10f * s, m), true);
        }

        void Vignette()
        {
            RunAdjustment("Vignette", delegate
            {
                var d = new AdjustDialog("Vignette");
                d.AddSlider("a", "Amount (darken  ⟷  lighten)", -100, 100, -50);
                d.AddSlider("m", "Midpoint", 1, 100, 50);
                d.Finish();
                return d;
            }, (px, d, s, m) => PixelOps.Vignette(px, -d.Value("a"), d.Value("m"), m), true);
        }

        // =================================================================== help

        /// <summary>
        /// F12: writes what the editor is doing to %LocalAppData%\MicroApp\diag - a text
        /// dump of the tool, drag, layers and selection, plus a JPEG of the window as the
        /// editor itself renders it. For bug reports where a screenshot cannot be taken.
        /// </summary>
        void SaveDiagnostic()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicroApp", "diag");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("MicroApp image editor diagnostic " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("version " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
                sb.AppendLine("tool=" + _tool + " drag=" + _drag + " transform=" + (_xf == null ? "none" : _xf.Mode + (_xf.Quad ? " quad" : "") + (_xf.FloatHost != null ? " on-selection" : "")));
                sb.AppendLine("spaceDown=" + _spaceDown + " spaceHeld=" + SpaceHeld() + " autoSelect=" + _autoSelect + " showTransformControls=" + _showTransformControls);
                sb.AppendLine("canvas=" + _canvas.Width + "x" + _canvas.Height + " zoom=" + _zoom + " origin=" + _origin + " window=" + Size + " dpi=" + DeviceDpi);
                sb.AppendLine("selectedLayer=" + _sel + " undo=" + _undo.Count + " redo=" + _redo.Count + " floating=" + (_floatLayer != null));
                sb.AppendLine("selection=" + (HasSelection ? _selection.Bounds + " feather=" + _selection.Feather : "none") + " lastSelection=" + (_lastSelection != null));
                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    EditorLayer l = _layers[i];
                    var r = l as RasterLayer;
                    sb.AppendLine(string.Format("  [{0}] {1} ({2}) bounds={3} rot={4} shear={5},{6} flip={7}{8} vis={9} lock={10} op={11} blend={12}{13}",
                        i, l.Name, l.KindLabel, l.Bounds, l.RotationDeg, l.ShearX, l.ShearY, l.FlipH ? "H" : "", l.FlipV ? "V" : "",
                        l.Visible, l.Locked, l.Opacity, l.Blend, r != null ? " image=" + r.Image.Width + "x" + r.Image.Height : ""));
                }
                foreach (Snapshot s in _undo) sb.Append(s.Name).Append(" | ");
                sb.AppendLine();
                File.WriteAllText(Path.Combine(dir, "editor-" + stamp + ".txt"), sb.ToString());

                // the window as the editor draws it (its own rendering, not the screen)
                using (var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height)))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    float scale = Math.Min(1f, 1100f / bmp.Width);
                    using (var small = new Bitmap(Math.Max(1, (int)(bmp.Width * scale)), Math.Max(1, (int)(bmp.Height * scale))))
                    {
                        using (Graphics g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(bmp, 0, 0, small.Width, small.Height);
                        }
                        SaveJpeg(small, Path.Combine(dir, "editor-" + stamp + ".jpg"), 70);
                    }
                }
                Toast.Show("Diagnostic saved.\r\n" + dir);
            }
            catch (Exception ex) { ModernDialog.Info("Could not save the diagnostic", ex.Message); }
        }

        void ShowShortcuts()
        {
            var rows = new List<KeyValuePair<string, string>>();
            Action<string> group = name => rows.Add(new KeyValuePair<string, string>(name, null));
            Action<string, string> row = (a, b) => rows.Add(new KeyValuePair<string, string>(a, b));
            group("Tools");
            row("Move", "V"); row("Rectangular / Elliptical Marquee", "M  (Shift+M switches)"); row("Lasso / Polygonal Lasso", "L  (Shift+L)");
            row("Magic Wand", "W"); row("Crop", "C"); row("Eyedropper", "I"); row("Brush / Pencil", "B  (Shift+B)"); row("Eraser", "E");
            row("Clone Stamp", "S"); row("Gradient / Paint Bucket", "G  (Shift+G)"); row("Blur / Sharpen", "R  (Shift+R)"); row("Dodge / Burn", "O  (Shift+O)");
            row("Type", "T"); row("Shapes (rectangle, rounded, ellipse, polygon, line, arrow)", "U  (Shift+U cycles)"); row("Hand", "H  (or hold Space)"); row("Zoom", "Z");
            row("Default colours / Swap colours", "D / X"); row("Brush size / hardness", "[ ]  /  Shift+[ ]"); row("Brush or layer opacity", "1 … 0");
            group("Selection");
            row("Select All / Deselect / Reselect", "Ctrl+A / Ctrl+D / Shift+Ctrl+D"); row("Inverse", "Shift+Ctrl+I"); row("Add / Subtract / Intersect while selecting", "Shift / Alt / Shift+Alt");
            row("Constrain to square or circle", "Shift while dragging"); row("Feather", "Shift+F6"); row("Move the selection outline", "Arrows (with a selection tool)");
            row("Clear the selected pixels", "Delete"); row("Layer via Copy / via Cut", "Ctrl+J / Shift+Ctrl+J"); row("Fill", "Shift+F5");
            group("Transform");
            row("Free Transform", "Ctrl+T"); row("Transform Again", "Shift+Ctrl+T"); row("Keep proportions (corner handle)", "default; Shift frees it"); row("Scale about the centre", "Alt");
            row("Rotate", "drag just outside a corner; Shift snaps 15°"); row("Skew", "Ctrl-drag an edge handle"); row("Distort", "Ctrl-drag a corner"); row("Perspective", "Ctrl+Alt+Shift-drag a corner");
            row("Commit / Cancel", "Enter / Esc");
            group("Layers");
            row("New Layer", "Shift+Ctrl+N"); row("Duplicate layer", "Ctrl+J (no selection)"); row("Merge Down / Merge Visible", "Ctrl+E / Shift+Ctrl+E");
            row("Bring Forward / Send Backward", "Ctrl+] / Ctrl+["); row("Bring to Front / Send to Back", "Shift+Ctrl+] / Shift+Ctrl+["); row("Lock layer", "Ctrl+/");
            row("Nudge layer 1 px / 10 px", "Arrows / Shift+Arrows"); row("Rename", "double-click the name");
            group("Image");
            row("Levels / Curves", "Ctrl+L / Ctrl+M"); row("Hue/Saturation / Color Balance", "Ctrl+U / Ctrl+B"); row("Black & White", "Alt+Shift+Ctrl+B");
            row("Invert / Desaturate", "Ctrl+I / Shift+Ctrl+U"); row("Auto Tone / Auto Contrast / Auto Color", "Shift+Ctrl+L / Alt+Shift+Ctrl+L / Shift+Ctrl+B");
            row("Image Size / Canvas Size", "Alt+Ctrl+I / Alt+Ctrl+C"); row("Last Filter", "Alt+Ctrl+F");
            group("View");
            row("Zoom in / out", "Ctrl++ / Ctrl+-  (or the wheel)"); row("Fit on Screen / 100%", "Ctrl+0 / Ctrl+1"); row("Rulers", "Ctrl+R"); row("Hide extras", "Ctrl+H"); row("Pan", "Space+drag or the middle button");
            group("Edit");
            row("Undo / Redo", "Ctrl+Z / Shift+Ctrl+Z (Ctrl+Y)"); row("Cut / Copy / Paste", "Ctrl+X / Ctrl+C / Ctrl+V"); row("Copy Merged / Paste in Place", "Shift+Ctrl+C / Shift+Ctrl+V");
            row("Save As / Close", "Ctrl+S / Ctrl+W");
            using (var d = new ShortcutsDialog(rows)) d.ShowDialog(this);
        }
    }
}
