using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>The chrome around the canvas: tool rail, options bar, layers / history / assets, status bar.</summary>
    partial class ImageEditorForm
    {
        // options bar controls
        // shape
        Label _optStrokeLbl, _optFillLbl, _optWidthLbl, _optRadiusLbl, _optSidesLbl;
        SwatchButton _optStroke, _optFill;
        ModernNumber _optWidth, _optRadius, _optSides;
        // text
        Label _optFontLbl, _optSizeLbl, _optTextColorLbl, _optTextBackLbl, _optTextOutlineLbl;
        ModernCombo _optFont;
        ModernNumber _optSize;
        Button _optBold, _optItalic, _optUnderline, _optAlignL, _optAlignC, _optAlignR;
        SwatchButton _optTextColor, _optTextBack, _optTextOutline;
        // brushes
        Label _optBrushLbl, _optHardLbl, _optOpacityLbl, _optFlowLbl, _optStrengthLbl, _optExposureLbl;
        ModernNumber _optBrush, _optHard, _optOpacity, _optFlow, _optStrength, _optExposure;
        // selection
        Button _optSelNew, _optSelAdd, _optSelSub, _optSelInt;
        Label _optFeatherLbl, _optTolLbl;
        ModernNumber _optFeather, _optTol;
        ModernCheckBox _optAntialias, _optContiguous, _optSampleAll;
        // crop
        Label _optCropRatioLbl, _optCropLbl;
        ModernCombo _optCropRatio;
        ModernCheckBox _optCropDelete;
        Button _optCropOk, _optCropCancel;
        // eyedropper
        Label _optSampleLbl;
        ModernCombo _optSample;
        // gradient
        ModernCombo _optGradKind;
        ModernCheckBox _optGradReverse, _optGradTransparent;
        // move
        ModernCheckBox _optAutoSelect, _optShowControls;
        Button _optAlignLeft, _optAlignCenterH, _optAlignRight, _optAlignTop, _optAlignMiddle, _optAlignBottom;
        ModernButton _optMirrorH, _optMirrorV, _optRotate90;
        // hand / zoom
        ModernButton _optFit, _opt100, _optZoomIn, _optZoomOut;

        SelectionMode _selModeOption = SelectionMode.New;

        // right side
        Panel _tabStrip;
        int _rightTab;                       // 0 layers, 1 history
        Panel _layersPage, _historyPage;
        Label _layersTitle, _assetsTitle;
        ModernCombo _blendCombo;
        ModernNumber _opacityNum;
        Label _opacityLbl;
        Button _lockBtn;
        ListBox _layerList;
        Button _layerFx, _layerNew, _layerDup, _layerDel, _layerUp, _layerDown, _layerMerge;
        TextBox _renameBox;
        int _dragLayerFrom = -1, _dragLayerOver = -1;
        Point _dragLayerDown;
        bool _dragLayerActive;
        ListBox _historyList;
        TreeView _assetTree;
        ListView _assetList;
        ImageList _assetThumbs;
        Button _assetImport, _assetFolder;

        // ================================================================ tool rail

        void BuildToolRail()
        {
            _toolRail = new ToolRail(this)
            {
                Dock = DockStyle.Left,
                Width = 86,
                BackColor = Theme.Surface
            };
        }

        /// <summary>Two columns of tool slots with flyouts, foreground/background swatches under them.</summary>
        class ToolRail : Panel
        {
            readonly ImageEditorForm _f;
            readonly ToolTip _tip = new ToolTip();
            readonly Timer _hold = new Timer { Interval = 320 };
            int _hover = -1, _pressed = -1;
            string _tipText;
            const int Slot = 34, Pitch = 37;

            public ToolRail(ImageEditorForm f)
            {
                _f = f;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                _hold.Tick += delegate { _hold.Stop(); if (_pressed >= 0) ShowFlyout(_pressed); };
            }

            Rectangle SlotRect(int i)
            {
                int row = i / 2, col = i % 2;
                int y = 10 + row * Pitch + (row >= 3 ? 9 : 0) + (row >= 6 ? 9 : 0);
                return new Rectangle(7 + col * (Slot + 4), y, Slot, Slot);
            }

            // Remove Background: a one-click action, not a tool - it sits under the tools, before the colours
            Rectangle RemoveBgRect { get { Rectangle r = SlotRect(ToolGroups.Length + ToolGroups.Length % 2); r.Y += 9; return r; } }
            bool _hoverRemoveBg;
            // the rulers on/off toggle, beside it (guides are dragged out of the rulers)
            Rectangle RulerRect { get { Rectangle r = SlotRect(ToolGroups.Length + ToolGroups.Length % 2 + 1); r.Y += 9; return r; } }
            bool _hoverRuler;

            int SwatchTop { get { return RemoveBgRect.Bottom + 18; } }
            Rectangle FgRect { get { return new Rectangle(12, SwatchTop, 30, 30); } }
            Rectangle BgRect { get { return new Rectangle(30, SwatchTop + 18, 30, 30); } }

            int SlotAt(Point p)
            {
                for (int i = 0; i < ToolGroups.Length; i++) if (SlotRect(i).Contains(p)) return i;
                return -1;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                using (var p = new Pen(Theme.Border)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < ToolGroups.Length; i++)
                {
                    Tool[] group = ToolGroups[i];
                    Tool shown;
                    if (!_f._groupChoice.TryGetValue(group[0], out shown)) shown = group[0];
                    bool active = Array.IndexOf(group, _f._tool) >= 0;
                    if (active) shown = _f._tool;
                    Rectangle r = SlotRect(i);
                    if (active || i == _hover)
                    {
                        using (GraphicsPath rp = Theme.Round(r, 8))
                        using (var b = new SolidBrush(active ? Theme.Accent : Theme.FieldBg))
                            g.FillPath(b, rp);
                    }
                    EditorIcons.Draw(g, shown, new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12), active ? Theme.OnAccent : Theme.Text);
                    if (group.Length > 1)
                    {
                        using (var b = new SolidBrush(active ? Theme.OnAccent : Theme.TextDim))
                            g.FillPolygon(b, new[] { new PointF(r.Right - 4, r.Bottom - 8), new PointF(r.Right - 4, r.Bottom - 4), new PointF(r.Right - 8, r.Bottom - 4) });
                    }
                }
                Rectangle rb = RemoveBgRect;
                using (GraphicsPath rp = Theme.Round(rb, 8))
                {
                    if (_hoverRemoveBg)
                        using (var b = new SolidBrush(Theme.FieldBg)) g.FillPath(b, rp);
                    using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, rp);
                }
                EditorIcons.DrawGlyph(g, "removebg", new Rectangle(rb.X + 6, rb.Y + 6, rb.Width - 12, rb.Height - 12), Theme.Text);
                Rectangle rr = RulerRect;
                bool rulersOn = _f._showRulers;
                using (GraphicsPath rp = Theme.Round(rr, 8))
                {
                    if (rulersOn || _hoverRuler)
                        using (var b = new SolidBrush(rulersOn ? Theme.Accent : Theme.FieldBg)) g.FillPath(b, rp);
                    if (!rulersOn) using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, rp);
                }
                EditorIcons.DrawGlyph(g, "ruler", new Rectangle(rr.X + 6, rr.Y + 6, rr.Width - 12, rr.Height - 12), rulersOn ? Theme.OnAccent : Theme.Text);
                // the colour swatches: background behind, foreground in front
                Rectangle bg = BgRect, fg = FgRect;
                using (var b = new SolidBrush(_f._bg)) g.FillRectangle(b, bg);
                using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, bg);
                using (var b = new SolidBrush(_f._fg)) g.FillRectangle(b, fg);
                using (var edge = new Pen(Theme.Text, 1f)) g.DrawRectangle(edge, fg);
                // the tiny "default" and "swap" marks
                EditorIcons.DrawGlyph(g, "reset", new Rectangle(8, bg.Bottom - 12, 14, 14), Theme.TextDim);
                EditorIcons.DrawGlyph(g, "swap", new Rectangle(fg.Right + 4, fg.Top - 6, 16, 16), Theme.TextDim);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int s = SlotAt(e.Location);
                bool overRb = RemoveBgRect.Contains(e.Location);
                bool overRuler = RulerRect.Contains(e.Location);
                if (s != _hover || overRb != _hoverRemoveBg || overRuler != _hoverRuler) { _hover = s; _hoverRemoveBg = overRb; _hoverRuler = overRuler; Invalidate(); }
                string tip = null;
                if (s >= 0)
                {
                    Tool[] group = ToolGroups[s];
                    Tool shown;
                    if (!_f._groupChoice.TryGetValue(group[0], out shown)) shown = group[0];
                    tip = ToolName(shown) + "  (" + ToolKey(shown) + ")" + (group.Length > 1 ? "  ·  right-click for more" : "");
                }
                else if (overRuler) tip = _f._showRulers ? "Hide rulers (Ctrl+R)" : "Show rulers (Ctrl+R) - drag out of a ruler to add a guide";
                else if (overRb) tip = "Remove Background (Alt+Ctrl+B) - makes everything but the subject transparent (AI, offline); with a selection, only inside it";
                else if (FgRect.Contains(e.Location)) tip = "Foreground colour (click to change)";
                else if (BgRect.Contains(e.Location)) tip = "Background colour (click to change)";
                else if (new Rectangle(8, BgRect.Bottom - 12, 14, 14).Contains(e.Location)) tip = "Default colours (D)";
                else if (new Rectangle(FgRect.Right + 4, FgRect.Top - 6, 16, 16).Contains(e.Location)) tip = "Swap colours (X)";
                if (tip != _tipText) { _tipText = tip; _tip.SetToolTip(this, tip ?? ""); }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hover = -1;
                _hoverRemoveBg = false;
                _hoverRuler = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left && RemoveBgRect.Contains(e.Location)) { _f.RemoveBackground(true); return; }
                if (e.Button == MouseButtons.Left && RulerRect.Contains(e.Location)) { _f.ToggleRulers(); return; }
                int s = SlotAt(e.Location);
                if (s >= 0)
                {
                    if (e.Button == MouseButtons.Right) { ShowFlyout(s); return; }
                    _pressed = s;
                    if (ToolGroups[s].Length > 1) _hold.Start();
                    return;
                }
                if (e.Button != MouseButtons.Left) return;
                if (FgRect.Contains(e.Location)) { PickColour(true); return; }
                if (BgRect.Contains(e.Location)) { PickColour(false); return; }
                if (new Rectangle(8, BgRect.Bottom - 12, 14, 14).Contains(e.Location)) { _f._fg = Color.Black; _f._bg = Color.White; Invalidate(); return; }
                if (new Rectangle(FgRect.Right + 4, FgRect.Top - 6, 16, 16).Contains(e.Location)) { Color t = _f._fg; _f._fg = _f._bg; _f._bg = t; Invalidate(); return; }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                bool held = !_hold.Enabled && _pressed >= 0 && ToolGroups[_pressed].Length > 1;
                _hold.Stop();
                int s = _pressed;
                _pressed = -1;
                if (s < 0 || e.Button != MouseButtons.Left || held) return;
                Tool[] group = ToolGroups[s];
                Tool shown;
                if (!_f._groupChoice.TryGetValue(group[0], out shown)) shown = group[0];
                _f.SelectTool(shown);
            }

            void PickColour(bool foreground)
            {
                using (var dialog = new ColorDialog { Color = foreground ? _f._fg : _f._bg, FullOpen = true })
                {
                    if (dialog.ShowDialog(_f) != DialogResult.OK) return;
                    if (foreground) _f._fg = dialog.Color; else _f._bg = dialog.Color;
                    Invalidate();
                    _f.SyncOptionsFromSelection();
                }
            }

            void ShowFlyout(int slot)
            {
                _hold.Stop();
                _pressed = -1;
                Tool[] group = ToolGroups[slot];
                var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Base };
                foreach (Tool t in group)
                {
                    Tool tool = t;
                    var item = new ToolStripMenuItem(ToolName(tool)) { ShortcutKeyDisplayString = ToolKey(tool), Padding = new Padding(4, 3, 4, 3) };
                    var icon = new Bitmap(20, 20);
                    using (Graphics g = Graphics.FromImage(icon)) EditorIcons.Draw(g, tool, new Rectangle(0, 0, 20, 20), Theme.Text);
                    item.Image = icon;
                    item.Checked = _f._tool == tool;
                    item.Click += delegate { _f.SelectTool(tool); };
                    menu.Items.Add(item);
                }
                Rectangle r = SlotRect(slot);
                menu.Show(this, new Point(r.Right + 2, r.Top));
            }
        }

        static string ToolKey(Tool t)
        {
            switch (t)
            {
                case Tool.Move: return "V";
                case Tool.MarqueeRect: case Tool.MarqueeEllipse: return "M";
                case Tool.Lasso: case Tool.PolyLasso: return "L";
                case Tool.Wand: return "W";
                case Tool.Crop: return "C";
                case Tool.Eyedropper: return "I";
                case Tool.Brush: case Tool.Pencil: return "B";
                case Tool.Eraser: return "E";
                case Tool.Clone: return "S";
                case Tool.Gradient: case Tool.Bucket: return "G";
                case Tool.Blur: case Tool.Sharpen: return "R";
                case Tool.Dodge: case Tool.Burn: return "O";
                case Tool.Text: return "T";
                case Tool.Hand: return "H";
                case Tool.Zoom: return "Z";
                default: return "U";
            }
        }

        // ============================================================== options bar

        void BuildOptionsBar()
        {
            _optionsBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Theme.Surface
            };
            _optionsBar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 0, _optionsBar.Height - 1, _optionsBar.Width, _optionsBar.Height - 1);
            };

            // the current tool's name leads the bar, so the mode is always readable
            _optToolLbl = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Font = Theme.Strong,
                Padding = new Padding(0, 3, 10, 0)
            };
            _optionsBar.Controls.Add(_optToolLbl);
            _optionOrder.Add(_optToolLbl);

            // move
            _optAutoSelect = OptCheck("Auto-Select", _autoSelect, delegate { _autoSelect = _optAutoSelect.Checked; });
            _optShowControls = OptCheck("Show Transform Controls", _showTransformControls, delegate { _showTransformControls = _optShowControls.Checked; _canvasPanel.Invalidate(); });
            _optAlignLeft = OptGlyphButton("alignl", "Align left edges to the canvas", delegate { AlignLayer(0); });
            _optAlignCenterH = OptGlyphButton("alignc", "Align horizontal centres", delegate { AlignLayer(1); });
            _optAlignRight = OptGlyphButton("alignr", "Align right edges", delegate { AlignLayer(2); });
            _optAlignTop = OptGlyphButton("alignt", "Align top edges", delegate { AlignLayer(3); });
            _optAlignMiddle = OptGlyphButton("alignm", "Align vertical centres", delegate { AlignLayer(4); });
            _optAlignBottom = OptGlyphButton("alignb", "Align bottom edges", delegate { AlignLayer(5); });
            _optMirrorH = OptButton("Flip ↔", false, delegate { QuickTransform("Flip Horizontal"); });
            _optMirrorV = OptButton("Flip ↕", false, delegate { QuickTransform("Flip Vertical"); });
            _optRotate90 = OptButton("Rotate 90°", false, delegate { QuickTransform("Rotate 90° CW"); });

            // selection tools
            _optSelNew = OptGlyphButton("selnew", "New selection", delegate { SetSelModeOption(SelectionMode.New); });
            _optSelAdd = OptGlyphButton("seladd", "Add to selection (Shift)", delegate { SetSelModeOption(SelectionMode.Add); });
            _optSelSub = OptGlyphButton("selsub", "Subtract from selection (Alt)", delegate { SetSelModeOption(SelectionMode.Subtract); });
            _optSelInt = OptGlyphButton("selint", "Intersect with selection (Shift+Alt)", delegate { SetSelModeOption(SelectionMode.Intersect); });
            _optFeatherLbl = OptLabel("Feather");
            _optFeather = OptNumeric(0, 250, _marqueeFeather, delegate { _marqueeFeather = (int)_optFeather.Value; });
            _optFeather.Suffix = "px";
            _optAntialias = OptCheck("Anti-alias", _marqueeAntialias, delegate { _marqueeAntialias = _optAntialias.Checked; });
            _optTolLbl = OptLabel("Tolerance");
            _optTol = OptNumeric(0, 255, _wandTolerance, delegate
            {
                if (_tool == Tool.Bucket) _bucketTolerance = (int)_optTol.Value; else _wandTolerance = (int)_optTol.Value;
            });
            _optContiguous = OptCheck("Contiguous", _wandContiguous, delegate { _wandContiguous = _optContiguous.Checked; });
            _optSampleAll = OptCheck("Sample All Layers", _sampleAllLayers, delegate { _sampleAllLayers = _optSampleAll.Checked; });
            SetSelModeOption(SelectionMode.New);

            // shape tools
            _optStrokeLbl = OptLabel("Stroke");
            _optStroke = OptSwatch(_stroke, true, delegate(Color c) { _stroke = c; ApplyShapeOptions(); });
            _optFillLbl = OptLabel("Fill");
            _optFill = OptSwatch(_fill, true, delegate(Color c) { _fill = c; ApplyShapeOptions(); });
            _optWidthLbl = OptLabel("Width");
            _optWidth = OptNumeric(0, 200, (int)_strokeW, delegate { _strokeW = (float)_optWidth.Value; ApplyShapeOptions(); });
            _optRadiusLbl = OptLabel("Radius");
            _optRadius = OptNumeric(0, 500, _cornerRadius, delegate { _cornerRadius = (int)_optRadius.Value; ApplyShapeOptions(); });
            _optSidesLbl = OptLabel("Sides");
            _optSides = OptNumeric(3, 32, _polySides, delegate { _polySides = (int)_optSides.Value; ApplyShapeOptions(); });

            // text tool
            _optFontLbl = OptLabel("Font");
            _optFont = new ModernCombo { Width = 160 };
            try
            {
                foreach (FontFamily fam in FontFamily.Families) _optFont.Items.Add(fam.Name);
                _optFont.SelectedItem = _fontFamily;
                if (_optFont.SelectedIndex < 0 && _optFont.Items.Count > 0) _optFont.SelectedIndex = 0;
            }
            catch { }
            _optFont.SelectedIndexChanged += delegate
            {
                if (_syncingOptions || _optFont.SelectedItem == null) return;
                _fontFamily = (string)_optFont.SelectedItem;
                ApplyTextOptions();
            };
            _optionsBar.Controls.Add(_optFont);
            _optionOrder.Add(_optFont);

            _optSizeLbl = OptLabel("Size");
            _optSize = OptNumeric(4, 600, (int)_fontSize, delegate { _fontSize = (float)_optSize.Value; ApplyTextOptions(); });
            _optSize.Suffix = "px";
            _optBold = OptToggle("B", new Font("Segoe UI", 9.5F, FontStyle.Bold), delegate { _bold = !_bold; StyleToggle(_optBold, _bold); ApplyTextOptions(); });
            _optItalic = OptToggle("I", new Font("Segoe UI", 9.5F, FontStyle.Italic), delegate { _italic = !_italic; StyleToggle(_optItalic, _italic); ApplyTextOptions(); });
            _optUnderline = OptToggle("U", new Font("Segoe UI", 9.5F, FontStyle.Underline), delegate { _underline = !_underline; StyleToggle(_optUnderline, _underline); ApplyTextOptions(); });
            _optAlignL = OptGlyphButton("alignl", "Left align text", delegate { _textAlign = 0; SyncAlignToggles(); ApplyTextOptions(); });
            _optAlignC = OptGlyphButton("alignc", "Center text", delegate { _textAlign = 1; SyncAlignToggles(); ApplyTextOptions(); });
            _optAlignR = OptGlyphButton("alignr", "Right align text", delegate { _textAlign = 2; SyncAlignToggles(); ApplyTextOptions(); });
            _optTextColorLbl = OptLabel("Colour");
            _optTextColor = OptSwatch(_textColor, false, delegate(Color c) { _textColor = c; ApplyTextOptions(); });
            _optTextBackLbl = OptLabel("Box");
            _optTextBack = OptSwatch(_textBack, true, delegate(Color c) { _textBack = c; ApplyTextOptions(); });
            _optTextOutlineLbl = OptLabel("Outline");
            _optTextOutline = OptSwatch(_textOutline, true, delegate(Color c) { _textOutline = c; ApplyTextOptions(); });
            SyncAlignToggles();

            // brushes
            _optBrushLbl = OptLabel("Size");
            _optBrush = OptNumeric(1, 2000, _brushSize, delegate { _brushSize = (int)_optBrush.Value; _canvasPanel.Invalidate(); });
            _optBrush.Suffix = "px"; _optBrush.Width = 76;
            _optHardLbl = OptLabel("Hardness");
            _optHard = OptNumeric(0, 100, _brushHardness, delegate { _brushHardness = (int)_optHard.Value; _canvasPanel.Invalidate(); });
            _optHard.Suffix = "%";
            _optOpacityLbl = OptLabel("Opacity");
            _optOpacity = OptNumeric(1, 100, _brushOpacity, delegate { _brushOpacity = (int)_optOpacity.Value; });
            _optOpacity.Suffix = "%";
            _optFlowLbl = OptLabel("Flow");
            _optFlow = OptNumeric(1, 100, _brushFlow, delegate { _brushFlow = (int)_optFlow.Value; });
            _optFlow.Suffix = "%";
            _optStrengthLbl = OptLabel("Strength");
            _optStrength = OptNumeric(1, 50, _blurStrength, delegate { _blurStrength = (int)_optStrength.Value; });
            _optExposureLbl = OptLabel("Exposure");
            _optExposure = OptNumeric(1, 100, _exposure, delegate { _exposure = (int)_optExposure.Value; });
            _optExposure.Suffix = "%";

            // gradient
            _optGradKind = OptCombo(new[] { "Linear", "Radial" }, _gradientKind, delegate { _gradientKind = _optGradKind.SelectedIndex; });
            _optGradReverse = OptCheck("Reverse", _gradientReverse, delegate { _gradientReverse = _optGradReverse.Checked; });
            _optGradTransparent = OptCheck("Foreground to Transparent", _gradientToTransparent, delegate { _gradientToTransparent = _optGradTransparent.Checked; });

            // eyedropper
            _optSampleLbl = OptLabel("Sample Size");
            _optSample = OptCombo(new[] { "Point Sample", "3 by 3 Average", "5 by 5 Average" }, _sampleSize, delegate { _sampleSize = _optSample.SelectedIndex; });

            // crop
            _optCropRatioLbl = OptLabel("Ratio");
            _optCropRatio = OptCombo(new[] { "Unconstrained", "1 : 1 (Square)", "4 : 3", "16 : 9", "3 : 2", "Original Ratio" }, _cropRatio, delegate
            {
                _cropRatio = _optCropRatio.SelectedIndex;
                if (_cropRect.HasValue) { _cropRect = ApplyCropRatio(_cropRect.Value, false, false); RelayoutOptions(); _canvasPanel.Invalidate(); }
            });
            _optCropLbl = OptLabel("");
            _optCropDelete = OptCheck("Delete Cropped Pixels", _cropDeletePixels, delegate { _cropDeletePixels = _optCropDelete.Checked; });
            _optCropCancel = OptGlyphButton("cancel", "Cancel crop (Esc)", delegate { _cropRect = null; _canvasPanel.Invalidate(); RelayoutOptions(); });
            _optCropOk = OptGlyphButton("check", "Apply crop (Enter)", delegate { ApplyCrop(); });

            // hand / zoom
            _optZoomIn = OptButton("Zoom In", false, delegate { SetZoom(_zoom * 1.25f); });
            _optZoomOut = OptButton("Zoom Out", false, delegate { SetZoom(_zoom / 1.25f); });
            _opt100 = OptButton("100%", false, delegate { SetZoom(1f); });
            _optFit = OptButton("Fit Screen", false, delegate { FitView(); _canvasPanel.Invalidate(); });

            // free transform
            BuildTransformOptions();
        }

        void SetSelModeOption(SelectionMode m)
        {
            _selModeOption = m;
            StyleToggle(_optSelNew, m == SelectionMode.New);
            StyleToggle(_optSelAdd, m == SelectionMode.Add);
            StyleToggle(_optSelSub, m == SelectionMode.Subtract);
            StyleToggle(_optSelInt, m == SelectionMode.Intersect);
        }

        void SyncAlignToggles()
        {
            StyleToggle(_optAlignL, _textAlign == 0);
            StyleToggle(_optAlignC, _textAlign == 1);
            StyleToggle(_optAlignR, _textAlign == 2);
        }

        Label OptLabel(string text)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = Theme.Small,
                Padding = new Padding(0, 4, 0, 0)
            };
            _optionsBar.Controls.Add(l);
            _optionOrder.Add(l);
            return l;
        }

        SwatchButton OptSwatch(Color initial, bool allowClear, Action<Color> changed)
        {
            var b = new SwatchButton(allowClear) { Color = initial, Size = new Size(30, 24) };
            b.ColorChanged += delegate { if (!_syncingOptions) changed(b.Color); };
            if (allowClear) _tips.SetToolTip(b, "Click to pick a colour, right-click for none");
            _optionsBar.Controls.Add(b);
            _optionOrder.Add(b);
            return b;
        }

        ModernNumber OptNumeric(int min, int max, int value, EventHandler changed)
        {
            var n = new ModernNumber { Minimum = min, Maximum = max, Width = 74 };
            n.Value = Math.Max(min, Math.Min(max, value));
            n.ValueChanged += delegate(object s, EventArgs e) { if (!_syncingOptions) changed(s, e); };
            _optionsBar.Controls.Add(n);
            _optionOrder.Add(n);
            return n;
        }

        Button OptToggle(string text, Font font, EventHandler onClick)
        {
            var b = new GlyphButton(null) { Text = text, Font = font, Size = new Size(28, 26) };
            b.Click += delegate(object s, EventArgs e) { if (!_syncingOptions) onClick(s, e); };
            _optionsBar.Controls.Add(b);
            _optionOrder.Add(b);
            return b;
        }

        /// <summary>A 26px button with one of the hand-drawn glyphs.</summary>
        Button OptGlyphButton(string glyph, string tip, EventHandler onClick)
        {
            var b = new GlyphButton(glyph) { Size = new Size(28, 26) };
            b.Click += delegate(object s, EventArgs e) { if (!_syncingOptions) onClick(s, e); };
            _tips.SetToolTip(b, tip);
            _optionsBar.Controls.Add(b);
            _optionOrder.Add(b);
            return b;
        }

        ModernButton OptButton(string text, bool accent, EventHandler onClick)
        {
            var b = new ModernButton
            {
                Text = text,
                Accent = accent,
                Size = new Size(Math.Max(70, TextRenderer.MeasureText(text, Theme.Base).Width + 24), 26),
                Font = Theme.Small
            };
            b.Click += onClick;
            _optionsBar.Controls.Add(b);
            _optionOrder.Add(b);
            return b;
        }

        ModernCheckBox OptCheck(string text, bool value, EventHandler changed)
        {
            var c = new ModernCheckBox
            {
                Text = text,
                Checked = value,
                Size = new Size(TextRenderer.MeasureText(text, Theme.Base).Width + 32, 24),
                BackColor = Theme.Surface     // opaque: a ButtonBase never gets its background painted for it
            };
            c.CheckedChanged += delegate(object s, EventArgs e) { if (!_syncingOptions) changed(s, e); };
            _optionsBar.Controls.Add(c);
            _optionOrder.Add(c);
            return c;
        }

        ModernCombo OptCombo(string[] items, int index, EventHandler changed)
        {
            var c = new ModernCombo { Width = 140 };
            c.Items.AddRange(items);
            c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, index));
            c.SelectedIndexChanged += delegate(object s, EventArgs e) { if (!_syncingOptions) changed(s, e); };
            _optionsBar.Controls.Add(c);
            _optionOrder.Add(c);
            return c;
        }

        static void StyleToggle(Button b, bool on)
        {
            var gb = b as GlyphButton;
            if (gb != null) { gb.Checked = on; return; }
            b.BackColor = on ? Theme.Accent : Theme.Surface;
            b.ForeColor = on ? Theme.OnAccent : Theme.Text;
        }

        readonly HashSet<Control> _shownOptions = new HashSet<Control>();

        /// <summary>Shows the options that belong to the current tool, laid out left to right.</summary>
        void RelayoutOptions()
        {
            if (_optionsBar == null) return;
            // Control.Visible also reports false while the window is not shown yet, so the
            // layout works from its own list of what should be visible, not from the getter
            _shownOptions.Clear();
            _shownOptions.Add(_optToolLbl);

            bool xform = _xf != null;
            ShowTransformOptions(xform);
            if (!xform)
            {
                EditorLayer sel = SelectedLayer();
                var selShape = sel as ShapeLayer;
                bool moveShape = _tool == Tool.Move && selShape != null;
                bool moveText = _tool == Tool.Move && sel is TextLayer;
                switch (_tool)
                {
                    case Tool.Move:
                        ShowOpts(_optAutoSelect, _optShowControls);
                        if (sel != null)
                        {
                            ShowOpts(_optAlignLeft, _optAlignCenterH, _optAlignRight, _optAlignTop, _optAlignMiddle, _optAlignBottom, _optMirrorH, _optMirrorV, _optRotate90);
                        }
                        if (moveShape) ShowShapeOptions(selShape.Kind);
                        if (moveText) ShowTextOptions();
                        break;
                    case Tool.MarqueeRect:
                    case Tool.MarqueeEllipse:
                    case Tool.Lasso:
                    case Tool.PolyLasso:
                        ShowOpts(_optSelNew, _optSelAdd, _optSelSub, _optSelInt, _optFeatherLbl, _optFeather);
                        if (_tool != Tool.MarqueeRect) ShowOpts(_optAntialias);
                        break;
                    case Tool.Wand:
                        _syncingOptions = true; _optTol.Value = _wandTolerance; _syncingOptions = false;
                        ShowOpts(_optSelNew, _optSelAdd, _optSelSub, _optSelInt, _optTolLbl, _optTol, _optContiguous, _optSampleAll);
                        break;
                    case Tool.Crop:
                        ShowOpts(_optCropRatioLbl, _optCropRatio, _optCropLbl, _optCropDelete);
                        if (_cropRect.HasValue)
                        {
                            _optCropLbl.Text = string.Format("{0} × {1} px", (int)_cropRect.Value.Width, (int)_cropRect.Value.Height);
                            ShowOpts(_optCropCancel, _optCropOk);
                        }
                        else _optCropLbl.Text = "Drag over the canvas to choose the crop";
                        break;
                    case Tool.Eyedropper:
                        ShowOpts(_optSampleLbl, _optSample);
                        break;
                    case Tool.Brush:
                        ShowOpts(_optBrushLbl, _optBrush, _optHardLbl, _optHard, _optOpacityLbl, _optOpacity, _optFlowLbl, _optFlow);
                        break;
                    case Tool.Eraser:
                    case Tool.Clone:
                        ShowOpts(_optBrushLbl, _optBrush, _optHardLbl, _optHard, _optOpacityLbl, _optOpacity, _optFlowLbl, _optFlow);
                        if (_tool == Tool.Clone) ShowOpts(_optSampleAll);
                        break;
                    case Tool.Blur:
                    case Tool.Sharpen:
                        ShowOpts(_optBrushLbl, _optBrush, _optHardLbl, _optHard, _optStrengthLbl, _optStrength, _optOpacityLbl, _optOpacity);
                        break;
                    case Tool.Dodge:
                    case Tool.Burn:
                        ShowOpts(_optBrushLbl, _optBrush, _optHardLbl, _optHard, _optExposureLbl, _optExposure);
                        break;
                    case Tool.Bucket:
                        _syncingOptions = true; _optTol.Value = _bucketTolerance; _syncingOptions = false;
                        ShowOpts(_optTolLbl, _optTol, _optContiguous, _optOpacityLbl, _optOpacity);
                        break;
                    case Tool.Gradient:
                        ShowOpts(_optGradKind, _optGradReverse, _optGradTransparent, _optOpacityLbl, _optOpacity);
                        break;
                    case Tool.Pencil:
                        ShowOpts(_optStrokeLbl, _optStroke, _optWidthLbl, _optWidth);
                        break;
                    case Tool.ShapeRect: ShowShapeOptions(ShapeKind.Rectangle); break;
                    case Tool.ShapeRoundRect: ShowShapeOptions(ShapeKind.RoundedRectangle); break;
                    case Tool.ShapeEllipse: ShowShapeOptions(ShapeKind.Ellipse); break;
                    case Tool.ShapePolygon: ShowShapeOptions(ShapeKind.Polygon); break;
                    case Tool.ShapeLine: ShowShapeOptions(ShapeKind.Line); break;
                    case Tool.ShapeArrow: ShowShapeOptions(ShapeKind.Arrow); break;
                    case Tool.Text: ShowTextOptions(); break;
                    case Tool.Hand: ShowOpts(_optFit, _opt100); break;
                    case Tool.Zoom: ShowOpts(_optZoomIn, _optZoomOut, _opt100, _optFit); break;
                }
            }

            int x = 12;
            foreach (Control c in _optionOrder)
            {
                bool on = _shownOptions.Contains(c);
                if (on)
                {
                    c.Location = new Point(x, (_optionsBar.Height - c.Height) / 2);
                    x += c.Width + (c is Label ? 4 : 8);
                }
                if (c.Visible != on || !on) c.Visible = on;
            }
            _optionsBar.Visible = true;
            _optionsBar.Invalidate();
        }

        void ShowOpts(params Control[] controls)
        {
            foreach (Control c in controls) _shownOptions.Add(c);
        }

        void ShowShapeOptions(ShapeKind kind)
        {
            ShowOpts(_optStrokeLbl, _optStroke, _optWidthLbl, _optWidth);
            bool closed = kind == ShapeKind.Rectangle || kind == ShapeKind.RoundedRectangle || kind == ShapeKind.Ellipse || kind == ShapeKind.Polygon;
            if (closed) ShowOpts(_optFillLbl, _optFill);
            if (kind == ShapeKind.RoundedRectangle) ShowOpts(_optRadiusLbl, _optRadius);
            if (kind == ShapeKind.Polygon) ShowOpts(_optSidesLbl, _optSides);
        }

        void ShowTextOptions()
        {
            ShowOpts(_optFontLbl, _optFont, _optSizeLbl, _optSize, _optBold, _optItalic, _optUnderline, _optAlignL, _optAlignC, _optAlignR,
                 _optTextColorLbl, _optTextColor, _optTextBackLbl, _optTextBack, _optTextOutlineLbl, _optTextOutline);
        }

        void SyncBrushOptions()
        {
            _syncingOptions = true;
            try
            {
                _optBrush.Value = Math.Max(_optBrush.Minimum, Math.Min(_optBrush.Maximum, _brushSize));
                _optHard.Value = Math.Max(0, Math.Min(100, _brushHardness));
                _optOpacity.Value = Math.Max(1, Math.Min(100, _brushOpacity));
            }
            finally { _syncingOptions = false; }
        }

        /// <summary>Selected a layer: reflect its style in the options bar (no feedback).</summary>
        void SyncOptionsFromSelection()
        {
            EditorLayer sel = SelectedLayer();
            _syncingOptions = true;
            try
            {
                var shape = sel as ShapeLayer;
                if (shape != null)
                {
                    _stroke = shape.Stroke;
                    _fill = shape.Fill;
                    _strokeW = shape.StrokeWidth;
                    _cornerRadius = shape.CornerRadius;
                    _polySides = shape.Sides;
                    _optStroke.Color = _stroke;
                    _optFill.Color = _fill;
                    _optWidth.Value = Math.Max(_optWidth.Minimum, Math.Min(_optWidth.Maximum, (decimal)_strokeW));
                    _optRadius.Value = Math.Max(_optRadius.Minimum, Math.Min(_optRadius.Maximum, _cornerRadius));
                    _optSides.Value = Math.Max(_optSides.Minimum, Math.Min(_optSides.Maximum, _polySides));
                }
                var text = sel as TextLayer;
                if (text != null)
                {
                    _fontFamily = text.FontFamily;
                    _fontSize = text.FontSize;
                    _bold = text.Bold;
                    _italic = text.Italic;
                    _underline = text.Underline;
                    _textAlign = text.Align;
                    _textColor = text.Color;
                    _textBack = text.BackColor;
                    _textOutline = text.OutlineColor;
                    _optFont.SelectedItem = _fontFamily;
                    _optSize.Value = Math.Max(_optSize.Minimum, Math.Min(_optSize.Maximum, (decimal)_fontSize));
                    StyleToggle(_optBold, _bold);
                    StyleToggle(_optItalic, _italic);
                    StyleToggle(_optUnderline, _underline);
                    SyncAlignToggles();
                    _optTextColor.Color = _textColor;
                    _optTextBack.Color = _textBack;
                    _optTextOutline.Color = _textOutline;
                }
            }
            finally { _syncingOptions = false; }
        }

        /// <summary>An option changed: restyle the selected shape layer (or just set defaults).</summary>
        void ApplyShapeOptions()
        {
            var shape = SelectedLayer() as ShapeLayer;
            if (shape != null && _tool == Tool.Move)
            {
                PushUndoCoalesced("shapeopts", "Shape Options");
                shape.Stroke = _stroke;
                shape.Fill = _fill;
                shape.StrokeWidth = _strokeW;
                shape.CornerRadius = _cornerRadius;
                shape.Sides = _polySides;
                InvalidateDoc();
            }
        }

        void ApplyTextOptions()
        {
            var text = SelectedLayer() as TextLayer;
            if (text != null && (_tool == Tool.Move || _tool == Tool.Text))
            {
                PushUndoCoalesced("textopts", "Type Options");
                text.FontFamily = _fontFamily;
                text.FontSize = _fontSize;
                text.Bold = _bold;
                text.Italic = _italic;
                text.Underline = _underline;
                text.Align = _textAlign;
                text.Color = _textColor;
                text.BackColor = _textBack;
                text.OutlineColor = _textOutline;
                GrowTextBounds(text);
                if (_editing == text && _inlineEdit.Visible)
                {
                    try { _inlineEdit.Font = new Font(_fontFamily, Math.Max(4f, _fontSize * _zoom), text.Style & ~FontStyle.Underline, GraphicsUnit.Pixel); }
                    catch { }
                    _inlineEdit.ForeColor = _textColor.A > 60 ? Color.FromArgb(255, _textColor) : Theme.Text;
                    _inlineEdit.TextAlign = _textAlign == 1 ? HorizontalAlignment.Center : _textAlign == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                }
                InvalidateDoc();
            }
        }

        // =============================================================== right side

        PanelSplitter _splitter;

        /// <summary>The drag handle between the canvas and the right-hand panels; its position is remembered.</summary>
        void BuildSplitter()
        {
            _splitter = new PanelSplitter
            {
                Dock = DockStyle.Right,
                Width = 7,
                MinSize = 220,      // the panel never gets narrower than this
                MinExtra = 360,     // ...and the canvas keeps at least this much
                BackColor = Theme.Surface
            };
            _splitter.SplitterMoved += delegate
            {
                try
                {
                    Properties.Settings.Default.EditorRightPanelWidth = _rightSide.Width;
                    Properties.Settings.Default.Save();
                }
                catch { }
                LayoutRightSide();
                if (_viewFitted) FitView();
                _canvasPanel.Invalidate();
            };
        }

        void BuildRightSide()
        {
            int savedWidth = 320;
            try { savedWidth = Math.Max(220, Math.Min(800, Properties.Settings.Default.EditorRightPanelWidth)); } catch { }
            _rightSide = new Panel
            {
                Dock = DockStyle.Right,
                Width = savedWidth,
                BackColor = Theme.Surface,
                Padding = new Padding(8)
            };
            _rightSide.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(p, 0, 0, 0, _rightSide.Height);
                    foreach (Control c in new Control[] { _layerList, _historyList, _assetTree, _assetList })
                    {
                        if (c == null || !c.Visible || !c.Parent.Visible) continue;
                        Rectangle r = _rightSide.RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
                        e.Graphics.DrawRectangle(p, r.Left - 1, r.Top - 1, r.Width + 1, r.Height + 1);
                    }
                }
            };

            // ----- tabs -----
            _tabStrip = new Panel { Height = 28, BackColor = Theme.Surface };
            _tabStrip.Paint += TabStrip_Paint;
            _tabStrip.MouseDown += delegate(object s, MouseEventArgs e)
            {
                int tab = e.X < 90 ? 0 : e.X < 180 ? 1 : -1;
                if (tab >= 0 && tab != _rightTab) { _rightTab = tab; ShowRightTab(); }
            };

            // ----- layers page -----
            _layersPage = new Panel { BackColor = Theme.Surface };
            _layersTitle = new Label { Text = "", Visible = false };

            _blendCombo = new ModernCombo { Width = 132, Height = 24 };
            _blendCombo.Items.AddRange(BlendModes.Names);
            _blendCombo.SelectedIndex = 0;
            _blendCombo.SelectedIndexChanged += delegate
            {
                if (_syncingOptions) return;
                EditorLayer sel = SelectedLayer();
                if (sel == null) return;
                PushUndoCoalesced("blend", "Blending Mode");
                sel.Blend = (BlendMode)_blendCombo.SelectedIndex;
                sel.DropCache();
                _layerList.Invalidate();
                InvalidateDoc();
            };
            _opacityLbl = new Label { Text = "Opacity", Font = Theme.Small, ForeColor = Theme.TextDim, AutoSize = true, BackColor = Color.Transparent };
            _opacityNum = new ModernNumber { Minimum = 0, Maximum = 100, Width = 70, Height = 24, Suffix = "%" };
            _opacityNum.Value = 100;
            _opacityNum.ValueChanged += Opacity_ValueChanged;
            _lockBtn = new GlyphButton("unlock") { Size = new Size(26, 24) };
            _tips.SetToolTip(_lockBtn, "Lock / unlock the layer (Ctrl+/)");
            _lockBtn.Click += delegate { ToggleLock(); };

            _layerList = new ListBox
            {
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 40,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                IntegralHeight = false
            };
            _layerList.DrawItem += LayerList_DrawItem;
            _layerList.MouseDown += LayerList_MouseDown;
            _layerList.MouseMove += LayerList_MouseMove;
            _layerList.MouseUp += LayerList_MouseUp;
            _layerList.MouseDoubleClick += LayerList_MouseDoubleClick;
            _layerList.SelectedIndexChanged += LayerList_SelectedIndexChanged;

            _layerFx = GlyphSmall("fx", "Layer style (drop shadow, glow, stroke, overlay)", delegate { LayerStyle(0); });
            _layerNew = GlyphSmall("new", "New layer (Shift+Ctrl+N)", delegate { NewLayer(); });
            _layerDup = GlyphSmall("dup", "Duplicate layer (Ctrl+J)", delegate { DuplicateLayer(true); });
            _layerDel = GlyphSmall("trash", "Delete layer", delegate { DeleteLayer(); });
            _layerUp = GlyphSmall("up", "Bring forward (Ctrl+])", delegate { MoveLayer(1); });
            _layerDown = GlyphSmall("down", "Send backward (Ctrl+[)", delegate { MoveLayer(-1); });
            _layerMerge = GlyphSmall("merge", "Merge down (Ctrl+E)", delegate { MergeDown(); });

            _renameBox = new TextBox
            {
                Visible = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                Font = Theme.Base
            };
            _renameBox.KeyDown += RenameBox_KeyDown;
            _renameBox.LostFocus += delegate { CommitRename(); };

            _layersPage.Controls.Add(_blendCombo);
            _layersPage.Controls.Add(_opacityLbl);
            _layersPage.Controls.Add(_opacityNum);
            _layersPage.Controls.Add(_lockBtn);
            _layersPage.Controls.Add(_layerList);
            foreach (Button b in new[] { _layerFx, _layerNew, _layerDup, _layerDel, _layerUp, _layerDown, _layerMerge }) _layersPage.Controls.Add(b);
            _layersPage.Controls.Add(_renameBox);

            // ----- history page -----
            _historyPage = new Panel { BackColor = Theme.Surface, Visible = false };
            _historyList = new ListBox
            {
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                IntegralHeight = false
            };
            _historyList.DrawItem += HistoryList_DrawItem;
            _historyList.MouseDown += HistoryList_MouseDown;
            _historyPage.Controls.Add(_historyList);

            // ----- assets (bottom) -----
            _assetsTitle = new Label
            {
                Text = "ASSETS",
                Font = new Font("Segoe UI Semibold", 8.25F),
                ForeColor = Theme.TextDim,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _assetTree = new TreeView
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                HideSelection = false,
                ShowLines = false,
                Font = Theme.Base
            };
            _assetTree.AfterSelect += delegate { RefreshAssetList(); };
            _assetTree.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Button == MouseButtons.Right) _assetTree.SelectedNode = e.Node;
            };
            var treeMenu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Base };
            treeMenu.Items.Add(new ToolStripMenuItem("New Sub-category…", null, delegate { NewAssetFolder(); }));
            treeMenu.Items.Add(new ToolStripMenuItem("Rename Category…", null, delegate { RenameAssetFolder(); }));
            _assetTree.ContextMenuStrip = treeMenu;

            _assetThumbs = new ImageList { ImageSize = new Size(44, 44), ColorDepth = ColorDepth.Depth32Bit };
            _assetList = new ListView
            {
                View = View.LargeIcon,
                LargeImageList = _assetThumbs,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                HideSelection = false,
                Font = Theme.Small,
                MultiSelect = false
            };
            _assetList.DoubleClick += delegate { AddSelectedAssetAsLayer(); };
            _assetList.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { AddSelectedAssetAsLayer(); e.Handled = true; }
                if (e.KeyCode == Keys.Delete) { DeleteSelectedAsset(); e.Handled = true; }
            };
            var assetMenu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Base };
            assetMenu.Items.Add(new ToolStripMenuItem("Add as Layer", null, delegate { AddSelectedAssetAsLayer(); }));
            assetMenu.Items.Add(new ToolStripMenuItem("Delete Asset", null, delegate { DeleteSelectedAsset(); }));
            _assetList.ContextMenuStrip = assetMenu;

            _assetImport = SmallButton("＋ Import", "Copy image files into this category");
            _assetImport.Width = 74;
            _assetImport.Click += delegate { ImportAssets(); };
            _assetFolder = SmallButton("＋ Folder", "New category or sub-category under the selected one");
            _assetFolder.Width = 74;
            _assetFolder.Click += delegate { NewAssetFolder(); };

            _rightSide.Controls.Add(_tabStrip);
            _rightSide.Controls.Add(_layersPage);
            _rightSide.Controls.Add(_historyPage);
            _rightSide.Controls.Add(_assetsTitle);
            _rightSide.Controls.Add(_assetTree);
            _rightSide.Controls.Add(_assetList);
            _rightSide.Controls.Add(_assetImport);
            _rightSide.Controls.Add(_assetFolder);

            _rightSide.Resize += delegate { LayoutRightSide(); };
        }

        void LayoutRightSide()
        {
            int w = _rightSide.ClientSize.Width - 20;
            int h = _rightSide.ClientSize.Height;
            int topH = (int)(h * 0.56);

            _tabStrip.SetBounds(10, 6, w, 28);
            _layersPage.SetBounds(10, 36, w, topH - 36);
            _historyPage.SetBounds(10, 36, w, topH - 36);

            // layers page
            // header row laid out from the right so nothing can run under the lock button:
            // [blend mode ......] Opacity [100 %] [lock]
            _lockBtn.Location = new Point(w - _lockBtn.Width, 3);
            _opacityNum.Location = new Point(_lockBtn.Left - 6 - _opacityNum.Width, 4);
            _opacityLbl.Location = new Point(_opacityNum.Left - 4 - _opacityLbl.Width, 9);
            _blendCombo.Location = new Point(0, 4);
            _blendCombo.Width = Math.Max(90, _opacityLbl.Left - 10);
            int listTop = 34;
            int footer = 30;
            _layerList.SetBounds(0, listTop, w, Math.Max(40, _layersPage.Height - listTop - footer - 6));
            int bx = 0;
            foreach (Button b in new[] { _layerNew, _layerDup, _layerDel, _layerUp, _layerDown, _layerMerge, _layerFx })
            {
                b.Location = new Point(bx, _layersPage.Height - footer + 2);
                bx += b.Width + 4;
            }
            _historyList.SetBounds(0, 4, w, Math.Max(40, _historyPage.Height - 8));

            int ay = topH + 8;
            _assetsTitle.Location = new Point(10, ay);
            int treeH = Math.Max(50, (h - ay - 96) / 3);
            _assetTree.SetBounds(10, ay + 18, w, treeH);
            _assetList.SetBounds(10, ay + 18 + treeH + 6, w, Math.Max(50, h - (ay + 18 + treeH + 6) - 40));
            _assetImport.Location = new Point(10, h - 32);
            _assetFolder.Location = new Point(10 + _assetImport.Width + 6, h - 32);
            _rightSide.Invalidate();
        }

        void TabStrip_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            string[] names = { "Layers", "History" };
            using (var f = new Font("Segoe UI Semibold", 8.5F))
            {
                for (int i = 0; i < 2; i++)
                {
                    var r = new Rectangle(i * 90, 0, 90, 28);
                    bool on = i == _rightTab;
                    using (var b = new SolidBrush(on ? Theme.Text : Theme.TextDim))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(names[i].ToUpperInvariant(), f, b, r, sf);
                    if (on)
                        using (var acc = new SolidBrush(Theme.Accent)) g.FillRectangle(acc, r.X + 20, r.Bottom - 3, r.Width - 40, 2);
                }
            }
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, 27, _tabStrip.Width, 27);
        }

        void ShowRightTab()
        {
            _layersPage.Visible = _rightTab == 0;
            _historyPage.Visible = _rightTab == 1;
            _tabStrip.Invalidate();
            _rightSide.Invalidate();
            if (_rightTab == 1) RefreshHistoryList();
        }

        Button SmallButton(string text, string tip)
        {
            var b = new ModernButton { Text = text, Size = new Size(34, 26), Font = Theme.Small, TabStop = false, BackColor = Theme.Surface };
            _tips.SetToolTip(b, tip);
            return b;
        }

        Button GlyphSmall(string glyph, string tip, EventHandler onClick)
        {
            var b = new GlyphButton(glyph) { Size = new Size(30, 26) };
            b.Click += onClick;
            _tips.SetToolTip(b, tip);
            return b;
        }

        // ============================================================== status bar

        void BuildStatus()
        {
            _status = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                BackColor = Theme.Surface
            };
            _status.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 0, 0, _status.Width, 0);
            };
            _zoomBox = new ModernNumber { Width = 84, Height = 22, Location = new Point(8, 2), Minimum = 2, Maximum = 3200, DecimalPlaces = 1, Suffix = "%" };
            _zoomBox.Value = 100;
            _zoomBox.ValueChanged += delegate { if (!_syncingZoom) SetZoom((float)_zoomBox.Value / 100f); };
            _tips.SetToolTip(_zoomBox, "Zoom - type a percentage and press Enter");
            var zoomMenuBtn = new GlyphButton("menu") { Size = new Size(22, 22), Location = new Point(94, 2) };
            zoomMenuBtn.Click += delegate
            {
                var menu = new ContextMenuStrip { Renderer = new ModernMenuRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Base };
                foreach (int pct in new[] { 25, 50, 66, 100, 150, 200, 300, 400, 800 })
                {
                    int v = pct;
                    menu.Items.Add(new ToolStripMenuItem(v + "%", null, delegate { SetZoom(v / 100f); }));
                }
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("Fit on Screen", null, delegate { FitView(); _canvasPanel.Invalidate(); }));
                menu.Show(zoomMenuBtn, new Point(0, -menu.Height - 2));
            };
            _statusLeft = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Font = Theme.Small,
                Location = new Point(126, 6)
            };
            _statusColor = new Label
            {
                AutoSize = true,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = Theme.Small,
                Location = new Point(420, 6)
            };
            _statusRight = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = Theme.Small,
                Padding = new Padding(0, 6, 12, 0)
            };
            _status.Controls.Add(_zoomBox);
            _status.Controls.Add(zoomMenuBtn);
            _status.Controls.Add(_statusLeft);
            _status.Controls.Add(_statusColor);
            _status.Controls.Add(_statusRight);
            _statusLeft.TextChanged += delegate { _statusColor.Location = new Point(_statusLeft.Right + 24, 6); };
        }

        // ============================================================== layers panel

        bool _rebuildingLayerList;
        // list row → layer index (top row first); floating pieces mid-move are not rows
        readonly List<int> _listMap = new List<int>();

        int DisplayOf(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layers.Count) return -1;
            // a floating piece shows as its host, the layer just below it
            if (_layers[layerIndex].Floating) layerIndex--;
            return _listMap.IndexOf(layerIndex);
        }

        void RefreshLayerList()
        {
            if (_layerList == null) return;
            _rebuildingLayerList = true;
            try
            {
                _layerList.BeginUpdate();
                _layerList.Items.Clear();
                _listMap.Clear();
                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    if (_layers[i].Floating) continue;
                    _listMap.Add(i);
                    _layerList.Items.Add(_layers[i].Name ?? "Layer");
                }
                int display = DisplayOf(_sel);
                if (display >= 0 && display < _layerList.Items.Count) _layerList.SelectedIndex = display;
                _layerList.EndUpdate();

                EditorLayer sel = SelectedLayer();
                _syncingOptions = true;
                _opacityNum.Value = sel != null ? Math.Max(0, Math.Min(100, sel.Opacity)) : 100;
                _blendCombo.SelectedIndex = sel != null ? (int)sel.Blend : 0;
                _syncingOptions = false;
                ((GlyphButton)_lockBtn).Glyph = sel != null && sel.Locked ? "lock" : "unlock";
                ((GlyphButton)_lockBtn).Checked = sel != null && sel.Locked;
                _opacityNum.Enabled = _blendCombo.Enabled = _lockBtn.Enabled = _layerFx.Enabled =
                    _layerUp.Enabled = _layerDown.Enabled = _layerDup.Enabled = _layerDel.Enabled = sel != null;
                _layerMerge.Enabled = sel != null && _sel > 0;
            }
            finally { _rebuildingLayerList = false; }
        }

        int DisplayToLayer(int displayIndex)
        {
            return displayIndex >= 0 && displayIndex < _listMap.Count ? _listMap[displayIndex] : -1;
        }

        void LayerList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_rebuildingLayerList) return;
            CommitTransform();
            _sel = _layerList.SelectedIndex >= 0 ? DisplayToLayer(_layerList.SelectedIndex) : -1;
            SyncOptionsFromSelection();
            RelayoutOptions();
            RefreshLayerList();
            _canvasPanel.Invalidate();
        }

        void LayerList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || DisplayToLayer(e.Index) < 0 || DisplayToLayer(e.Index) >= _layers.Count) return;
            EditorLayer layer = _layers[DisplayToLayer(e.Index)];
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new SolidBrush(Theme.FieldBg)) g.FillRectangle(bg, e.Bounds);
            if (selected)
            {
                using (var wash = new SolidBrush(Color.FromArgb(Theme.Dark ? 46 : 30, Theme.Accent)))
                    g.FillRectangle(wash, e.Bounds);
                using (var bar = new SolidBrush(Theme.Accent))
                    g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            }
            if (_dragLayerActive && _dragLayerOver == e.Index)
            {
                using (var p = new Pen(Theme.Accent, 2f)) g.DrawLine(p, e.Bounds.X + 4, e.Bounds.Y + 1, e.Bounds.Right - 4, e.Bounds.Y + 1);
            }

            Color fore = layer.Visible ? Theme.Text : Color.FromArgb(130, Theme.Text);

            // the eye: filled when visible, hollow with a slash when hidden
            Rectangle eye = new Rectangle(e.Bounds.X + 9, e.Bounds.Y + e.Bounds.Height / 2 - 5, 13, 10);
            using (var p = new Pen(layer.Visible ? Theme.Text : Theme.TextDim, 1.4f))
            {
                g.DrawEllipse(p, eye);
                if (layer.Visible)
                    using (var b = new SolidBrush(Theme.Text))
                        g.FillEllipse(b, eye.X + 4, eye.Y + 2.5f, 5.5f, 5.5f);
                else
                    g.DrawLine(p, eye.Left - 2, eye.Bottom + 3, eye.Right + 2, eye.Top - 3);
            }

            Rectangle thumb = new Rectangle(e.Bounds.X + 30, e.Bounds.Y + 5, 30, 30);
            DrawLayerThumb(g, layer, thumb);

            string label = layer.Name ?? "Layer";
            using (var b = new SolidBrush(fore))
                g.DrawString(label, Theme.Base, b, e.Bounds.X + 68, e.Bounds.Y + 5);
            string sub = layer.KindLabel;
            if (layer.Blend != BlendMode.Normal) sub += " · " + BlendModes.Names[(int)layer.Blend];
            if (layer.Opacity < 100) sub += " · " + layer.Opacity + "%";
            using (var b = new SolidBrush(Theme.TextDim))
                g.DrawString(sub, Theme.Small, b, e.Bounds.X + 68, e.Bounds.Y + 22);

            int bx = e.Bounds.Right - 22;
            if (layer.Locked) { EditorIcons.DrawGlyph(g, "lock", new Rectangle(bx, e.Bounds.Y + 12, 16, 16), Theme.TextDim); bx -= 20; }
            if (layer.Fx != null && layer.Fx.Any) { EditorIcons.DrawGlyph(g, "fx", new Rectangle(bx, e.Bounds.Y + 12, 16, 16), Theme.Accent); bx -= 20; }
        }

        /// <summary>A 30px preview: rasters show their pixels, text a T in its colour, shapes themselves.</summary>
        void DrawLayerThumb(Graphics g, EditorLayer layer, Rectangle r)
        {
            using (var bg = new SolidBrush(Theme.Dark ? Color.FromArgb(58, 58, 66) : Color.White))
                g.FillRectangle(bg, r);
            var raster = layer as RasterLayer;
            var text = layer as TextLayer;
            var shape = layer as ShapeLayer;
            GraphicsState st = g.Save();
            g.SetClip(r);
            try
            {
                if (raster != null && raster.Image != null)
                {
                    float scale = Math.Min((float)r.Width / raster.Image.Width, (float)r.Height / raster.Image.Height);
                    int w = Math.Max(1, (int)(raster.Image.Width * scale));
                    int h = Math.Max(1, (int)(raster.Image.Height * scale));
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.DrawImage(raster.Image, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
                }
                else if (text != null)
                {
                    using (var f = new Font("Segoe UI Semibold", 13F))
                    using (var b = new SolidBrush(text.Color.A > 40 ? Color.FromArgb(255, text.Color) : Theme.Text))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("T", f, b, r, sf);
                }
                else if (shape != null)
                {
                    Rectangle inner = new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12);
                    using (var p = new Pen(shape.Stroke.A > 40 ? Color.FromArgb(255, shape.Stroke) : Theme.Text, 2f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        switch (shape.Kind)
                        {
                            case ShapeKind.Rectangle: g.DrawRectangle(p, inner); break;
                            case ShapeKind.RoundedRectangle: using (GraphicsPath rp = Theme.Round(inner, 4)) g.DrawPath(p, rp); break;
                            case ShapeKind.Ellipse: g.DrawEllipse(p, inner); break;
                            case ShapeKind.Polygon: using (GraphicsPath pp = shape.ClosedPath(inner)) g.DrawPath(p, pp); break;
                            case ShapeKind.Arrow:
                                using (var cap = new AdjustableArrowCap(3f, 3.6f, true))
                                {
                                    p.CustomEndCap = cap;
                                    g.DrawLine(p, inner.Left, inner.Bottom, inner.Right, inner.Top);
                                }
                                break;
                            case ShapeKind.Freehand:
                                g.DrawCurve(p, new[]
                                {
                                    new Point(inner.Left, inner.Bottom), new Point(inner.Left + inner.Width / 3, inner.Top + 2),
                                    new Point(inner.Right - inner.Width / 3, inner.Bottom - 2), new Point(inner.Right, inner.Top)
                                });
                                break;
                            default: g.DrawLine(p, inner.Left, inner.Bottom, inner.Right, inner.Top); break;
                        }
                    }
                }
            }
            finally { g.Restore(st); }
            using (var edge = new Pen(Color.FromArgb(70, Theme.TextDim))) g.DrawRectangle(edge, r);
        }

        void LayerList_MouseDown(object sender, MouseEventArgs e)
        {
            int index = _layerList.IndexFromPoint(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (index >= 0) _layerList.SelectedIndex = index;
                ShowLayerContextMenu(_layerList, e.Location);
                return;
            }
            if (index < 0) return;
            if (e.X <= 26)
            {
                int li = DisplayToLayer(index);
                if (li >= 0 && li < _layers.Count)
                {
                    PushUndoCoalesced("visibility", "Layer Visibility");
                    _layers[li].Visible = !_layers[li].Visible;
                    _layerList.Invalidate();
                    InvalidateDoc();
                }
                return;
            }
            _dragLayerFrom = index;
            _dragLayerDown = e.Location;
            _dragLayerActive = false;
        }

        void LayerList_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragLayerFrom < 0 || e.Button != MouseButtons.Left) return;
            if (!_dragLayerActive && (Math.Abs(e.X - _dragLayerDown.X) > 4 || Math.Abs(e.Y - _dragLayerDown.Y) > 4)) _dragLayerActive = true;
            if (!_dragLayerActive) return;
            int over = _layerList.IndexFromPoint(e.Location);
            if (over < 0) over = e.Y < 10 ? 0 : _layerList.Items.Count;
            // drop above the row the cursor sits on; the bottom half of a row means below it
            Rectangle r = over < _layerList.Items.Count ? _layerList.GetItemRectangle(over) : Rectangle.Empty;
            if (over < _layerList.Items.Count && e.Y > r.Y + r.Height / 2) over++;
            if (over != _dragLayerOver) { _dragLayerOver = over; _layerList.Invalidate(); }
            _layerList.Cursor = Cursors.SizeNS;
        }

        void LayerList_MouseUp(object sender, MouseEventArgs e)
        {
            _layerList.Cursor = Cursors.Default;
            if (_dragLayerActive && _dragLayerFrom >= 0 && _dragLayerOver >= 0)
            {
                int from = DisplayToLayer(_dragLayerFrom);
                // display index "over" means: insert so the layer sits just above row `over`
                // (rows run top-down, layer indices bottom-up); past the last row = the bottom
                int target = _dragLayerOver < _listMap.Count ? _listMap[_dragLayerOver] + 1 : 0;
                if (from >= 0 && from < _layers.Count)
                {
                    EditorLayer layer = _layers[from];
                    _layers.RemoveAt(from);
                    if (target > from) target--;
                    target = Math.Max(0, Math.Min(_layers.Count, target));
                    if (target != from)
                    {
                        _layers.Insert(target, layer);
                        _sel = target;
                        // record it after the fact: the snapshot is the pre-move order
                        _undo.Add(new Snapshot
                        {
                            Name = "Layer Order",
                            Layers = RebuildOrder(from, target, layer),
                            Canvas = _canvas, CanvasBg = _canvasBg, Sel = from, Selection = _selection
                        });
                        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
                        _redo.Clear();
                        _dirty = true;
                        RefreshHistoryList();
                        AfterDocumentChange();
                    }
                    else _layers.Insert(target, layer);
                }
            }
            _dragLayerFrom = -1;
            _dragLayerOver = -1;
            _dragLayerActive = false;
            _layerList.Invalidate();
        }

        EditorLayer[] RebuildOrder(int from, int target, EditorLayer moved)
        {
            // the order before the move, cloned for the undo stack
            var list = new List<EditorLayer>(_layers);
            list.Remove(moved);
            list.Insert(from, moved);
            return list.Select(l => l.Clone()).ToArray();
        }

        void LayerList_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.X <= 26) return;
            int index = _layerList.IndexFromPoint(e.Location);
            if (index < 0) return;
            _layerList.SelectedIndex = index;
            if (e.X > _layerList.Width - 46 && SelectedLayer() != null && SelectedLayer().Fx != null && SelectedLayer().Fx.Any) { LayerStyle(0); return; }
            RenameLayer();
        }

        void RenameLayer()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            if (_rightTab != 0) { _rightTab = 0; ShowRightTab(); }
            int display = DisplayOf(_sel);
            if (display < 0) return;
            Rectangle item = _layerList.GetItemRectangle(display);
            _renameBox.SetBounds(_layerList.Left + 66, _layerList.Top + item.Y + 4, _layerList.Width - 72, 24);
            _renameBox.Text = sel.Name;
            _renameBox.Visible = true;
            _renameBox.BringToFront();
            _renameBox.Focus();
            _renameBox.SelectAll();
        }

        void RenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; CommitRename(); }
            else if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; _renameBox.Visible = false; }
        }

        void CommitRename()
        {
            if (!_renameBox.Visible) return;
            _renameBox.Visible = false;
            EditorLayer sel = SelectedLayer();
            string name = _renameBox.Text.Trim();
            if (sel == null || name.Length == 0 || name == sel.Name) return;
            PushUndo("Rename Layer");
            sel.Name = name;
            RefreshLayerList();
        }

        void Opacity_ValueChanged(object sender, EventArgs e)
        {
            if (_syncingOptions) return;
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            PushUndoCoalesced("opacity", "Layer Opacity");
            sel.Opacity = (int)_opacityNum.Value;
            _layerList.Invalidate();
            InvalidateDoc();
        }

        void ToggleLock()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            PushUndoCoalesced("lock", sel.Locked ? "Unlock Layer" : "Lock Layer");
            sel.Locked = !sel.Locked;
            if (sel.Locked) CommitTransform();
            RefreshLayerList();
            _canvasPanel.Invalidate();
        }

        // ============================================================== layer ops

        void NewLayer()
        {
            if (!EnsureDoc()) return;
            CommitTransform();
            PushUndo("New Layer");
            var layer = new RasterLayer(NewTransparentBitmap(_canvas.Width, _canvas.Height))
            {
                Name = NextName("Layer"),
                Bounds = new RectangleF(0, 0, _canvas.Width, _canvas.Height)
            };
            int at = _sel >= 0 ? _sel + 1 : _layers.Count;
            _layers.Insert(at, layer);
            _sel = at;
            AfterDocumentChange();
        }

        void MoveLayer(int direction)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            int target = _sel + direction;
            if (target < 0 || target >= _layers.Count) return;
            CommitTransform();
            PushUndo("Layer Order");
            _layers[_sel] = _layers[target];
            _layers[target] = sel;
            _sel = target;
            AfterDocumentChange();
        }

        void MoveLayerTo(bool front)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            int target = front ? _layers.Count - 1 : 0;
            if (target == _sel) return;
            CommitTransform();
            PushUndo("Layer Order");
            _layers.RemoveAt(_sel);
            _layers.Insert(target, sel);
            _sel = target;
            AfterDocumentChange();
        }

        /// <summary>Ctrl+J: a copy of the layer - or, with a selection, just the selected pixels (Layer via Copy).</summary>
        void DuplicateLayer(bool offset)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return; }
            CommitTransform();
            if (HasSelection && sel is RasterLayer) { LayerViaCopy(false); return; }
            PushUndo("Duplicate Layer");
            EditorLayer copy = sel.Clone();
            copy.Name = sel.Name + " copy";
            copy.DropCache();
            if (offset)
            {
                RectangleF b = copy.Bounds;
                b.Offset(16, 16);
                copy.Bounds = b;
            }
            _layers.Insert(_sel + 1, copy);
            _sel = _sel + 1;
            AfterDocumentChange();
        }

        /// <summary>Layer via Copy / Layer via Cut: the selected pixels become their own layer, in place.</summary>
        void LayerViaCopy(bool cut)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return; }
            if (!HasSelection) { if (!cut) DuplicateLayer(false); else Toast.Show("Make a selection first."); return; }
            CommitTransform();
            PushUndo(cut ? "Layer Via Cut" : "Layer Via Copy");
            Bitmap piece;
            using (Bitmap alone = sel.RenderAlone(_canvas, false)) piece = CutOut(alone, _selection);
            var layer = new RasterLayer(piece) { Name = NextName("Layer"), Bounds = _selection.Bounds };
            if (cut)
            {
                var raster = sel as RasterLayer;
                if (raster == null)
                {
                    int idx = _sel;
                    raster = sel.Rasterize(_canvas);
                    _layers[idx] = raster;
                }
                byte[] mask = _selection.MaskForLayer(raster);
                Pixels px = Pixels.From(raster.Image);
                byte[] d = px.Data;
                for (int i = 0, k = 0; i < d.Length; i += 4, k++)
                {
                    int m = mask == null ? 255 : mask[k];
                    if (m != 0) d[i + 3] = (byte)(d[i + 3] * (255 - m) / 255);
                }
                raster.Image = px.ToBitmap();
            }
            _layers.Insert(_sel + 1, layer);
            _sel = _sel + 1;
            AfterDocumentChange();
        }

        void DeleteLayer()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            if (_xf != null && _xf.Layer == sel) CancelTransform();
            PushUndo("Delete Layer");
            _layers.RemoveAt(_sel);
            if (_sel >= _layers.Count) _sel = _layers.Count - 1;
            AfterDocumentChange();
        }

        void MergeDown()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null || _sel == 0) { Toast.Show("There is no layer below to merge into."); return; }
            CommitTransform();
            PushUndo("Merge Down");
            EditorLayer below = _layers[_sel - 1];
            var two = new List<EditorLayer> { below, sel };
            RasterLayer merged = MergeToRaster(two, below.Name);
            merged.Visible = below.Visible || sel.Visible;
            _layers.RemoveAt(_sel);
            _layers[_sel - 1] = merged;
            _sel = _sel - 1;
            AfterDocumentChange();
        }

        void MergeVisible()
        {
            if (!EnsureDoc()) return;
            CommitTransform();
            var visible = _layers.Where(l => l.Visible).ToList();
            if (visible.Count < 2) { Toast.Show("Nothing to merge."); return; }
            PushUndo("Merge Visible");
            RasterLayer merged = MergeToRaster(visible, visible[visible.Count - 1].Name);
            int at = _layers.IndexOf(visible[visible.Count - 1]);
            foreach (EditorLayer l in visible) _layers.Remove(l);
            at = Math.Min(at, _layers.Count);
            _layers.Insert(at, merged);
            _sel = at;
            AfterDocumentChange();
        }

        void FlattenImage()
        {
            if (!EnsureDoc()) return;
            CommitTransform();
            PushUndo("Flatten Image");
            Bitmap flat = EditorRender.Flatten(_layers, _canvas, _canvasBg);
            _layers.Clear();
            _layers.Add(new RasterLayer(flat) { Name = "Background", Bounds = new RectangleF(0, 0, _canvas.Width, _canvas.Height) });
            _sel = 0;
            AfterDocumentChange();
        }

        /// <summary>Composes some layers (with their blend modes and styles) into one pixel layer cropped to their extent.</summary>
        RasterLayer MergeToRaster(List<EditorLayer> layers, string name)
        {
            var visibleCopies = layers.Select(l => { EditorLayer c = l.Clone(); c.Visible = true; return c; }).ToList();
            Bitmap full = EditorRender.Compose(visibleCopies, _canvas, Color.Transparent, null);
            RectangleF box = RectangleF.Empty;
            foreach (EditorLayer l in layers) box = box.IsEmpty ? l.CanvasBox() : RectangleF.Union(box, l.CanvasBox());
            var r = Rectangle.Round(box);
            r.Intersect(new Rectangle(0, 0, _canvas.Width, _canvas.Height));
            if (r.Width < 1 || r.Height < 1) r = new Rectangle(0, 0, _canvas.Width, _canvas.Height);
            Bitmap crop = full.Clone(r, PixelFormat.Format32bppArgb);
            full.Dispose();
            return new RasterLayer(crop) { Name = name, Bounds = new RectangleF(r.X, r.Y, r.Width, r.Height) };
        }

        void RasterizeLayer()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            if (sel is RasterLayer && sel.RotationDeg == 0 && sel.ShearX == 0 && sel.ShearY == 0 && !sel.FlipH && !sel.FlipV) { Toast.Show("Already a pixel layer."); return; }
            CommitTransform();
            PushUndo("Rasterize Layer");
            _layers[_sel] = sel.Rasterize(_canvas);
            AfterDocumentChange();
        }

        /// <summary>Align the selected layer to the canvas: 0 left, 1 h-centre, 2 right, 3 top, 4 v-middle, 5 bottom.</summary>
        void AlignLayer(int how)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            if (sel.Locked) { Toast.Show("The layer is locked."); return; }
            CommitTransform();
            PushUndo("Align");
            RectangleF box = sel.CanvasBox();
            float dx = 0, dy = 0;
            switch (how)
            {
                case 0: dx = -box.Left; break;
                case 1: dx = (_canvas.Width - box.Width) / 2f - box.Left; break;
                case 2: dx = _canvas.Width - box.Right; break;
                case 3: dy = -box.Top; break;
                case 4: dy = (_canvas.Height - box.Height) / 2f - box.Top; break;
                case 5: dy = _canvas.Height - box.Bottom; break;
            }
            RectangleF b = sel.Bounds;
            b.Offset(dx, dy);
            sel.Bounds = b;
            AfterDocumentChange();
        }

        void LayerStyle(int page)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return; }
            CommitTransform();
            PushUndo("Layer Style");
            LayerEffects before = sel.Fx;
            var fx = before == null ? new LayerEffects() : before.Clone();
            if (before == null)
            {
                switch (page) { case 0: fx.DropShadow = true; break; case 1: fx.OuterGlow = true; break; case 2: fx.Stroke = true; break; case 3: fx.ColorOverlay = true; break; }
            }
            sel.Fx = fx;
            sel.DropCache();
            InvalidateDoc();
            using (var dialog = new LayerStyleDialog(fx, page))
            {
                dialog.Changed += delegate { sel.DropCache(); InvalidateDoc(); };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    sel.Fx = fx.Any ? fx : null;
                }
                else
                {
                    sel.Fx = before;
                    PopUndo();
                }
            }
            sel.DropCache();
            AfterDocumentChange();
        }

        void ClearLayerStyle()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null || sel.Fx == null) return;
            PushUndo("Clear Layer Style");
            sel.Fx = null;
            sel.DropCache();
            AfterDocumentChange();
        }

        /// <summary>Renders just the selected layer and stores it in the library as a PNG.</summary>
        void SaveLayerAsAsset()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return; }
            string name = EditorPrompt.Ask(this, "Save layer as asset", "Asset name", sel.Name);
            if (string.IsNullOrEmpty(name)) return;

            RectangleF box = sel.CanvasBox();
            int w = Math.Max(1, (int)Math.Ceiling(box.Width));
            int h = Math.Max(1, (int)Math.Ceiling(box.Height));
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    EditorRender.Prepare(g);
                    g.TranslateTransform(-box.X, -box.Y);
                    bool wasVisible = sel.Visible;
                    sel.Visible = true;
                    sel.Draw(g, 100);
                    sel.Visible = wasVisible;
                }
                try
                {
                    AssetStore.SaveBitmap(bmp, CurrentAssetFolder(), name);
                    Toast.Show("Saved to the asset library.");
                    RefreshAssetList();
                }
                catch (Exception ex) { ModernDialog.Info("Could not save it", ex.Message); }
            }
        }

        // ================================================================= history

        void RefreshHistoryList()
        {
            if (_historyList == null || !_historyPage.Visible) return;
            _historyList.BeginUpdate();
            _historyList.Items.Clear();
            _historyList.Items.Add("Open");
            foreach (Snapshot s in _undo) _historyList.Items.Add(s.Name);
            for (int i = _redo.Count - 1; i >= 0; i--) _historyList.Items.Add(_redo[i].Name);
            _historyList.EndUpdate();
            _historyList.Invalidate();
        }

        void HistoryList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            int current = _undo.Count;   // the row that is "now"
            bool isCurrent = e.Index == current;
            bool future = e.Index > current;
            using (var bg = new SolidBrush(Theme.FieldBg)) g.FillRectangle(bg, e.Bounds);
            if (isCurrent)
            {
                using (var wash = new SolidBrush(Color.FromArgb(Theme.Dark ? 46 : 30, Theme.Accent))) g.FillRectangle(wash, e.Bounds);
                using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            }
            string text = (string)_historyList.Items[e.Index];
            using (var b = new SolidBrush(future ? Theme.TextDim : Theme.Text))
                g.DrawString(text, Theme.Base, b, e.Bounds.X + 12, e.Bounds.Y + 4);
        }

        void HistoryList_MouseDown(object sender, MouseEventArgs e)
        {
            int index = _historyList.IndexFromPoint(e.Location);
            if (index < 0) return;
            int current = _undo.Count;
            int steps = index - current;
            if (steps == 0) return;
            CommitInlineEdit();
            CancelTransform();
            for (int i = 0; i < -steps; i++) DoUndo();
            for (int i = 0; i < steps; i++) DoRedo();
            RefreshHistoryList();
        }

        // ============================================================== assets panel

        string CurrentAssetFolder()
        {
            return _assetTree.SelectedNode != null ? (string)_assetTree.SelectedNode.Tag : "";
        }

        void RefreshAssetTree(string selectRel = null)
        {
            string keep = selectRel != null ? selectRel : CurrentAssetFolder();
            _assetTree.BeginUpdate();
            _assetTree.Nodes.Clear();
            var root = new TreeNode("Assets") { Tag = "" };
            _assetTree.Nodes.Add(root);
            var byPath = new Dictionary<string, TreeNode> { { "", root } };
            foreach (string rel in AssetStore.Folders())
            {
                string parent = Path.GetDirectoryName(rel) ?? "";
                TreeNode parentNode;
                if (!byPath.TryGetValue(parent, out parentNode)) parentNode = root;
                var node = new TreeNode(Path.GetFileName(rel)) { Tag = rel };
                parentNode.Nodes.Add(node);
                byPath[rel] = node;
            }
            root.ExpandAll();
            TreeNode restore;
            _assetTree.SelectedNode = byPath.TryGetValue(keep, out restore) ? restore : root;
            _assetTree.EndUpdate();
            RefreshAssetList();
        }

        void RefreshAssetList()
        {
            _assetList.BeginUpdate();
            _assetList.Items.Clear();
            _assetThumbs.Images.Clear();
            foreach (string file in AssetStore.Files(CurrentAssetFolder()))
            {
                using (Bitmap thumb = AssetStore.LoadThumb(file, 44))
                    _assetThumbs.Images.Add(new Bitmap(thumb));   // ImageList wants its own copy
                var item = new ListViewItem(Path.GetFileNameWithoutExtension(file), _assetThumbs.Images.Count - 1) { Tag = file };
                _assetList.Items.Add(item);
            }
            _assetList.EndUpdate();
        }

        void ImportAssets()
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.wmf;*.emf",
                Title = "Import into “" + (CurrentAssetFolder() == "" ? "Assets" : CurrentAssetFolder()) + "”",
                Multiselect = true
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                int done = 0;
                foreach (string file in ofd.FileNames)
                {
                    try { AssetStore.Import(file, CurrentAssetFolder()); done++; }
                    catch (Exception ex) { ModernDialog.Info("Could not import " + Path.GetFileName(file), ex.Message); }
                }
                if (done > 0) Toast.Show(done + (done == 1 ? " asset imported." : " assets imported."));
                RefreshAssetList();
            }
        }

        void NewAssetFolder()
        {
            string name = EditorPrompt.Ask(this, "New category",
                CurrentAssetFolder() == "" ? "Category name" : "Sub-category under “" + Path.GetFileName(CurrentAssetFolder()) + "”", "");
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                AssetStore.NewFolder(CurrentAssetFolder(), name);
                RefreshAssetTree();
            }
            catch (Exception ex) { ModernDialog.Info("Could not create it", ex.Message); }
        }

        void RenameAssetFolder()
        {
            string rel = CurrentAssetFolder();
            if (rel.Length == 0)
            {
                Toast.Show("Pick a category first - the root itself keeps its name.");
                return;
            }
            string current = Path.GetFileName(rel);
            string name = EditorPrompt.Ask(this, "Rename category", "New name for “" + current + "”", current);
            if (string.IsNullOrEmpty(name) || name == current) return;
            try
            {
                string newRel = AssetStore.RenameFolder(rel, name);
                RefreshAssetTree(newRel);
            }
            catch (Exception ex) { ModernDialog.Info("Could not rename it", ex.Message); }
        }

        void DeleteSelectedAsset()
        {
            if (_assetList.SelectedItems.Count == 0) return;
            string file = (string)_assetList.SelectedItems[0].Tag;
            if (!ModernDialog.Confirm("Delete this asset?", Path.GetFileName(file) + " will be removed from the library.",
                                      "Delete", "Keep it")) return;
            try
            {
                AssetStore.Delete(file);
                RefreshAssetList();
            }
            catch (Exception ex) { ModernDialog.Info("Could not delete it", ex.Message); }
        }

        void AddSelectedAssetAsLayer()
        {
            if (_assetList.SelectedItems.Count == 0) return;
            string file = (string)_assetList.SelectedItems[0].Tag;
            try
            {
                AddBitmapLayer(AssetStore.LoadFull(file), Path.GetFileNameWithoutExtension(file));
            }
            catch (Exception ex) { ModernDialog.Info("Could not load it", ex.Message); }
        }
    }

    /// <summary>A splitter drawn in the app's style: a hairline and a small grip, accent while dragging.</summary>
    class PanelSplitter : Splitter
    {
        bool _hover;

        public PanelSplitter()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
            using (var line = new Pen(Theme.Border)) g.DrawLine(line, 0, 0, 0, Height);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color ink = _hover ? Theme.Accent : Theme.TextDim;
            using (var dot = new SolidBrush(ink))
            {
                float cx = Width / 2f, cy = Height / 2f;
                for (int i = -2; i <= 2; i++) g.FillEllipse(dot, cx - 1.5f, cy + i * 6 - 1.5f, 3, 3);
            }
        }
    }

    /// <summary>A flat button showing one of the hand-drawn glyphs, with an optional checked (accent) state.</summary>
    class GlyphButton : Button
    {
        string _glyph;
        bool _checked;
        bool _hover;

        public GlyphButton(string glyph)
        {
            _glyph = glyph;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Surface;
            TabStop = false;
        }

        public string Glyph { get { return _glyph; } set { _glyph = value; Invalidate(); } }
        public bool Checked { get { return _checked; } set { _checked = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_checked || _hover)
            {
                using (GraphicsPath rp = Theme.Round(r, 5))
                using (var b = new SolidBrush(_checked ? Theme.Accent : Theme.FieldBg))
                    g.FillPath(b, rp);
            }
            Color ink = !Enabled ? Theme.TextDim : _checked ? Theme.OnAccent : Theme.Text;
            if (string.IsNullOrEmpty(_glyph))
                TextRenderer.DrawText(g, Text, Font, r, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            else
                EditorIcons.DrawGlyph(g, _glyph, new Rectangle(2, 2, Width - 5, Height - 5), ink);
        }
    }
}
