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
    /// <summary>
    /// The image editor: a Photoshop-style window with a tool rail on the left, the canvas
    /// in the middle, and layers + the asset library on the right. It opens from the tray
    /// menu or its hot key, starts from a clipboard paste, a file or a blank canvas, and
    /// every mark, text box and pasted image stays its own movable layer until export.
    /// </summary>
    class ImageEditorForm : PixelPerfectForm
    {
        // ---- the document ----------------------------------------------------------
        readonly List<EditorLayer> _layers = new List<EditorLayer>();   // bottom → top
        Size _canvas = Size.Empty;
        Color _canvasBg = Color.Transparent;
        bool _hasDoc;
        int _sel = -1;
        bool _dirty;
        int _nameCounter;

        // ---- undo ------------------------------------------------------------------
        class Snapshot
        {
            public EditorLayer[] Layers;
            public Size Canvas;
            public Color CanvasBg;
            public int Sel;
        }
        readonly List<Snapshot> _undo = new List<Snapshot>();
        readonly List<Snapshot> _redo = new List<Snapshot>();
        string _coalesceKey;
        DateTime _coalesceAt;

        // ---- view ------------------------------------------------------------------
        float _zoom = 1f;
        PointF _origin;                    // canvas (0,0) in panel client pixels
        bool _viewFitted;

        // ---- tools -----------------------------------------------------------------
        enum Tool { Move, Crop, Rect, Ellipse, Line, Arrow, Pen, Text, Blur }
        Tool _tool = Tool.Move;

        Color _stroke = Color.FromArgb(244, 63, 94);
        Color _fill = Color.Transparent;
        float _strokeW = 4;
        string _fontFamily = "Segoe UI";
        float _fontSize = 32;
        bool _bold, _italic, _underline;
        Color _textColor = Color.FromArgb(244, 63, 94);
        Color _textBack = Color.Transparent;
        Color _textOutline = Color.Transparent;
        int _brushSize = 48;
        int _blurStrength = 8;

        // ---- interaction state -----------------------------------------------------
        enum Drag { None, Pan, Move, Resize, Rotate, Draw, Crop, Blur }
        Drag _drag = Drag.None;
        bool _dragUndoPushed;
        Point _mouseScreen;                // last mouse position, panel coords
        PointF _downCanvas;                // mouse-down, canvas coords
        Point _downScreen;
        PointF _panOrigin0;
        bool _spaceDown;

        RectangleF _bounds0;               // layer geometry at drag start
        float _rot0;
        Matrix _matrix0;
        int _handle = -1;                  // 0..7 resize, 8 rotate
        PointF _anchorCanvas;

        ShapeLayer _draft;                 // shape being drawn
        List<PointF> _penPts;              // freehand, canvas coords

        RectangleF? _cropRect;

        RasterLayer _blurTarget;
        List<PointF> _blurDabs;            // bitmap pixel coords
        List<PointF> _blurScreenPts;       // for the stroke preview
        float _blurImgRadius;

        TextLayer _editing;                // inline text edit
        bool _editingWasVisible;

        // ---- ui --------------------------------------------------------------------
        MenuStrip _menu;
        Panel _toolRail, _optionsBar, _rightSide;
        CanvasPanel _canvasPanel;
        Panel _status;
        Label _statusLeft, _statusRight;
        Label _optToolLbl;
        Label _layersTitle;
        readonly List<int> _railSeparators = new List<int>();
        readonly Dictionary<Tool, Button> _toolButtons = new Dictionary<Tool, Button>();
        readonly List<Control> _optionOrder = new List<Control>();
        readonly ToolTip _tips = new ToolTip();
        bool _syncingOptions;

        // shape options
        Label _optStrokeLbl, _optFillLbl, _optWidthLbl;
        SwatchButton _optStroke, _optFill;
        NumericUpDown _optWidth;
        // text options
        Label _optFontLbl, _optSizeLbl, _optTextColorLbl, _optTextBackLbl, _optTextOutlineLbl;
        ComboBox _optFont;
        NumericUpDown _optSize;
        Button _optBold, _optItalic, _optUnderline;
        SwatchButton _optTextColor, _optTextBack, _optTextOutline;
        // blur options
        Label _optBrushLbl, _optStrengthLbl;
        NumericUpDown _optBrush, _optStrength;
        // crop options
        ModernButton _optCropApply, _optCropCancel;
        Label _optCropLbl;
        // move options
        ModernButton _optMirrorH, _optMirrorV, _optRotate90;

        // layers panel
        ListBox _layerList;
        TrackBar _opacity;
        Label _opacityLbl;
        Button _layerUp, _layerDown, _layerDup, _layerDel, _layerAdd;
        TextBox _renameBox;

        // assets panel
        TreeView _assetTree;
        ListView _assetList;
        ImageList _assetThumbs;
        Button _assetImport, _assetFolder;

        TextBox _inlineEdit;

        static ImageEditorForm _open;

        /// <summary>Opens the editor (or brings the open one forward). Seeds from the clipboard.</summary>
        public static void Open()
        {
            if (_open != null && !_open.IsDisposed)
            {
                if (_open.WindowState == FormWindowState.Minimized) _open.WindowState = FormWindowState.Normal;
                _open.Activate();
                Native.SetForegroundWindow(_open.Handle);
                return;
            }
            var form = new ImageEditorForm();
            _open = form;
            form.TopMost = true;      // the hot key fires from another app: make sure we surface
            form.Show();
            Native.SetForegroundWindow(form.Handle);
            form.BeginInvoke(new Action(() =>
            {
                form.TopMost = false;
                form.PasteFromClipboard(true);   // quiet: a text-only clipboard is not an error
            }));
        }

        ImageEditorForm()
        {
            Theme.Init(ThemeHelper.IsDarkMode);

            Text = "MicroApp Image Editor";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 800);
            MinimumSize = new Size(900, 600);
            BackColor = Theme.Bg;
            KeyPreview = true;
            AllowDrop = true;
            try { Icon = Properties.Resources.AppIcon; } catch { }

            BuildMenu();
            BuildOptionsBar();
            BuildToolRail();
            BuildRightSide();
            BuildCanvas();
            BuildStatus();

            // docking order decides the layout: menu on top, options under it, status at
            // the bottom, tool rail left, panels right, canvas fills the rest
            Controls.Add(_canvasPanel);
            Controls.Add(_toolRail);
            Controls.Add(_rightSide);
            Controls.Add(_status);
            Controls.Add(_optionsBar);
            Controls.Add(_menu);
            MainMenuStrip = _menu;

            DragEnter += Form_DragEnter;
            DragDrop += Form_DragDrop;
            FormClosing += Form_FormClosing;
            FormClosed += delegate { if (_open == this) _open = null; };
            Shown += delegate
            {
                Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode);
                RefreshAssetTree();
            };

            SelectTool(Tool.Move);
            UpdateStatus();
        }

        // =============================================================== UI building

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
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("Save As…", Keys.Control | Keys.S, delegate { SaveAs(); }));
            file.DropDownItems.Add(Item("Copy Result to Clipboard", Keys.Control | Keys.Shift | Keys.C, delegate { CopyResult(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("Close", Keys.Control | Keys.W, delegate { Close(); }));

            var edit = new ToolStripMenuItem("Edit");
            edit.DropDownItems.Add(Item("Undo", Keys.Control | Keys.Z, delegate { DoUndo(); }));
            edit.DropDownItems.Add(Item("Redo", Keys.Control | Keys.Y, delegate { DoRedo(); }));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Paste as Layer", Keys.Control | Keys.V, delegate { PasteFromClipboard(false); }));
            edit.DropDownItems.Add(Item("Duplicate Layer", Keys.Control | Keys.J, delegate { DuplicateLayer(); }));
            edit.DropDownItems.Add(Item("Delete Layer", Keys.None, delegate { DeleteLayer(); }));

            var image = new ToolStripMenuItem("Image");
            image.DropDownItems.Add(Item("Resize…", Keys.Control | Keys.Alt | Keys.I, delegate { ResizeDocument(); }));
            image.DropDownItems.Add(new ToolStripSeparator());
            image.DropDownItems.Add(Item("Rotate 90° Clockwise", Keys.None, delegate { RotateCanvas(true); }));
            image.DropDownItems.Add(Item("Rotate 90° Counter-clockwise", Keys.None, delegate { RotateCanvas(false); }));
            image.DropDownItems.Add(Item("Mirror Horizontally", Keys.None, delegate { FlipCanvas(true); }));
            image.DropDownItems.Add(Item("Mirror Vertically", Keys.None, delegate { FlipCanvas(false); }));

            var layer = new ToolStripMenuItem("Layer");
            layer.DropDownItems.Add(Item("Rotate 90°", Keys.None, delegate { RotateLayer90(); }));
            layer.DropDownItems.Add(Item("Mirror Horizontally", Keys.None, delegate { MirrorLayer(true); }));
            layer.DropDownItems.Add(Item("Mirror Vertically", Keys.None, delegate { MirrorLayer(false); }));
            layer.DropDownItems.Add(new ToolStripSeparator());
            layer.DropDownItems.Add(Item("Move Up", Keys.Control | Keys.OemCloseBrackets, delegate { MoveLayer(1); }));
            layer.DropDownItems.Add(Item("Move Down", Keys.Control | Keys.OemOpenBrackets, delegate { MoveLayer(-1); }));
            layer.DropDownItems.Add(Item("Rename…", Keys.F2, delegate { RenameLayer(); }));
            layer.DropDownItems.Add(new ToolStripSeparator());
            layer.DropDownItems.Add(Item("Save Layer as Asset…", Keys.None, delegate { SaveLayerAsAsset(); }));

            _menu.Items.Add(file);
            _menu.Items.Add(edit);
            _menu.Items.Add(image);
            _menu.Items.Add(layer);
        }

        static ToolStripMenuItem Item(string text, Keys keys, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text, null, onClick) { Padding = new Padding(4, 3, 4, 3) };
            if (keys != Keys.None) item.ShortcutKeys = keys;
            return item;
        }

        void BuildToolRail()
        {
            _toolRail = new Panel
            {
                Dock = DockStyle.Left,
                Width = 50,
                BackColor = Theme.Surface
            };
            _toolRail.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(p, _toolRail.Width - 1, 0, _toolRail.Width - 1, _toolRail.Height);
                    foreach (int sy in _railSeparators)
                        e.Graphics.DrawLine(p, 12, sy, _toolRail.Width - 13, sy);
                }
            };
            int y = 12;
            y = AddTool(Tool.Move, "✥", "Move / select  (V)", y);
            y = AddTool(Tool.Crop, "⛶", "Crop  (C)", y);
            _railSeparators.Add(y + 4);
            y += 11;
            y = AddTool(Tool.Rect, "▭", "Rectangle  (R)", y);
            y = AddTool(Tool.Ellipse, "◯", "Ellipse  (E)", y);
            y = AddTool(Tool.Line, "╱", "Line  (L)", y);
            y = AddTool(Tool.Arrow, "➤", "Arrow  (A)", y);
            y = AddTool(Tool.Pen, "✎", "Freehand pen  (P)", y);
            _railSeparators.Add(y + 4);
            y += 11;
            y = AddTool(Tool.Text, "T", "Text  (T)", y);
            AddTool(Tool.Blur, "≈", "Blur brush  (B)", y);
        }

        int AddTool(Tool tool, string glyph, string tip, int y)
        {
            var b = new Button
            {
                Location = new Point(7, y),
                Size = new Size(36, 36),
                Text = glyph,
                Font = new Font("Segoe UI", 12.5F),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.Text,
                BackColor = Theme.Surface,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.FieldBg;
            b.Region = new Region(Theme.Round(new Rectangle(0, 0, 36, 36), 9));
            b.Click += delegate { SelectTool(tool); };
            _tips.SetToolTip(b, tip);
            _toolRail.Controls.Add(b);
            _toolButtons[tool] = b;
            return y + 41;
        }

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

            // shape tools
            _optStrokeLbl = OptLabel("Stroke");
            _optStroke = OptSwatch(_stroke, false, delegate(Color c) { _stroke = c; ApplyShapeOptions(); });
            _optFillLbl = OptLabel("Fill");
            _optFill = OptSwatch(_fill, true, delegate(Color c) { _fill = c; ApplyShapeOptions(); });
            _optWidthLbl = OptLabel("Width");
            _optWidth = OptNumeric(1, 40, (int)_strokeW, delegate { _strokeW = (float)_optWidth.Value; ApplyShapeOptions(); });

            // text tool
            _optFontLbl = OptLabel("Font");
            _optFont = new ComboBox
            {
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                Font = Theme.Base
            };
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
            _optSize = OptNumeric(6, 400, (int)_fontSize, delegate { _fontSize = (float)_optSize.Value; ApplyTextOptions(); });
            _optBold = OptToggle("B", new Font("Segoe UI", 9.5F, FontStyle.Bold), delegate { _bold = !_bold; StyleToggle(_optBold, _bold); ApplyTextOptions(); });
            _optItalic = OptToggle("I", new Font("Segoe UI", 9.5F, FontStyle.Italic), delegate { _italic = !_italic; StyleToggle(_optItalic, _italic); ApplyTextOptions(); });
            _optUnderline = OptToggle("U", new Font("Segoe UI", 9.5F, FontStyle.Underline), delegate { _underline = !_underline; StyleToggle(_optUnderline, _underline); ApplyTextOptions(); });
            _optTextColorLbl = OptLabel("Colour");
            _optTextColor = OptSwatch(_textColor, false, delegate(Color c) { _textColor = c; ApplyTextOptions(); });
            _optTextBackLbl = OptLabel("Box");
            _optTextBack = OptSwatch(_textBack, true, delegate(Color c) { _textBack = c; ApplyTextOptions(); });
            _optTextOutlineLbl = OptLabel("Outline");
            _optTextOutline = OptSwatch(_textOutline, true, delegate(Color c) { _textOutline = c; ApplyTextOptions(); });

            // blur tool
            _optBrushLbl = OptLabel("Brush");
            _optBrush = OptNumeric(6, 400, _brushSize, delegate { _brushSize = (int)_optBrush.Value; _canvasPanel.Invalidate(); });
            _optStrengthLbl = OptLabel("Strength");
            _optStrength = OptNumeric(1, 30, _blurStrength, delegate { _blurStrength = (int)_optStrength.Value; });

            // crop tool
            _optCropApply = OptButton("Apply crop", true, delegate { ApplyCrop(); });
            _optCropCancel = OptButton("Cancel", false, delegate { _cropRect = null; _canvasPanel.Invalidate(); RelayoutOptions(); });
            _optCropLbl = OptLabel("");

            // move tool
            _optMirrorH = OptButton("Mirror ↔", false, delegate { MirrorLayer(true); });
            _optMirrorV = OptButton("Mirror ↕", false, delegate { MirrorLayer(false); });
            _optRotate90 = OptButton("Rotate 90°", false, delegate { RotateLayer90(); });
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

        NumericUpDown OptNumeric(int min, int max, int value, EventHandler changed)
        {
            var n = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
                Width = 56,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                Font = Theme.Base
            };
            n.ValueChanged += delegate(object s, EventArgs e) { if (!_syncingOptions) changed(s, e); };
            _optionsBar.Controls.Add(n);
            _optionOrder.Add(n);
            return n;
        }

        Button OptToggle(string text, Font font, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                Font = font,
                Size = new Size(28, 24),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.Text,
                BackColor = Theme.Surface,
                TabStop = false
            };
            b.FlatAppearance.BorderColor = Theme.Border;
            b.Click += delegate(object s, EventArgs e) { if (!_syncingOptions) onClick(s, e); };
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
                Size = new Size(Math.Max(84, TextRenderer.MeasureText(text, Theme.Base).Width + 24), 26),
                Font = Theme.Small
            };
            b.Click += onClick;
            _optionsBar.Controls.Add(b);
            _optionOrder.Add(b);
            return b;
        }

        void StyleToggle(Button b, bool on)
        {
            b.BackColor = on ? Theme.Accent : Theme.Surface;
            b.ForeColor = on ? Theme.OnAccent : Theme.Text;
        }

        /// <summary>Shows the options that belong to the current tool, laid out left to right.</summary>
        void RelayoutOptions()
        {
            bool shape = _tool == Tool.Rect || _tool == Tool.Ellipse || _tool == Tool.Line ||
                         _tool == Tool.Arrow || _tool == Tool.Pen ||
                         (_tool == Tool.Move && SelectedLayer() is ShapeLayer);
            bool text = _tool == Tool.Text || (_tool == Tool.Move && SelectedLayer() is TextLayer);
            bool fillable = _tool == Tool.Rect || _tool == Tool.Ellipse;
            var selShape = SelectedLayer() as ShapeLayer;
            if (_tool == Tool.Move && selShape != null)
                fillable = selShape.Kind == ShapeKind.Rectangle || selShape.Kind == ShapeKind.Ellipse;

            _optStrokeLbl.Visible = _optStroke.Visible = shape;
            _optFillLbl.Visible = _optFill.Visible = shape && fillable;
            _optWidthLbl.Visible = _optWidth.Visible = shape;

            _optFontLbl.Visible = _optFont.Visible = text;
            _optSizeLbl.Visible = _optSize.Visible = text;
            _optBold.Visible = _optItalic.Visible = _optUnderline.Visible = text;
            _optTextColorLbl.Visible = _optTextColor.Visible = text;
            _optTextBackLbl.Visible = _optTextBack.Visible = text;
            _optTextOutlineLbl.Visible = _optTextOutline.Visible = text;

            _optBrushLbl.Visible = _optBrush.Visible = _tool == Tool.Blur;
            _optStrengthLbl.Visible = _optStrength.Visible = _tool == Tool.Blur;

            bool crop = _tool == Tool.Crop && _cropRect.HasValue;
            _optCropApply.Visible = _optCropCancel.Visible = crop;
            _optCropLbl.Visible = _tool == Tool.Crop;
            if (_tool == Tool.Crop)
                _optCropLbl.Text = _cropRect.HasValue
                    ? string.Format("{0} × {1} px", (int)_cropRect.Value.Width, (int)_cropRect.Value.Height)
                    : "Drag over the canvas to choose the crop";

            bool move = _tool == Tool.Move && _sel >= 0;
            _optMirrorH.Visible = _optMirrorV.Visible = _optRotate90.Visible = move;

            // the bar only earns its row when a tool actually has options to show;
            // plain Move with nothing selected keeps the whole strip hidden
            bool anything = false;
            foreach (Control c in _optionOrder)
                if (c != _optToolLbl && c.Visible) { anything = true; break; }
            _optionsBar.Visible = anything;

            int x = 12;
            foreach (Control c in _optionOrder)
            {
                if (!c.Visible) continue;
                c.Location = new Point(x, (_optionsBar.Height - c.Height) / 2);
                x += c.Width + (c is Label ? 4 : 10);
            }
        }

        void BuildRightSide()
        {
            _rightSide = new Panel
            {
                Dock = DockStyle.Right,
                Width = 268,
                BackColor = Theme.Surface,
                Padding = new Padding(8)
            };
            _rightSide.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(p, 0, 0, 0, _rightSide.Height);
                    // a quiet grey frame around each field container - enough definition
                    // to read as a panel, none of the old black harshness
                    foreach (Control c in new Control[] { _layerList, _assetTree, _assetList })
                    {
                        if (c == null || !c.Visible) continue;
                        e.Graphics.DrawRectangle(p, c.Left - 1, c.Top - 1, c.Width + 1, c.Height + 1);
                    }
                }
            };

            // ----- layers (top half) -----
            _layersTitle = new Label
            {
                Text = "LAYERS",
                Font = new Font("Segoe UI Semibold", 8.25F),
                ForeColor = Theme.TextDim,
                Location = new Point(10, 8),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            var layersTitle = _layersTitle;

            _layerList = new ListBox
            {
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 36,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                IntegralHeight = false
            };
            _layerList.DrawItem += LayerList_DrawItem;
            _layerList.MouseDown += LayerList_MouseDown;
            _layerList.MouseDoubleClick += LayerList_MouseDoubleClick;
            _layerList.SelectedIndexChanged += LayerList_SelectedIndexChanged;

            _opacityLbl = new Label
            {
                Text = "Opacity 100%",
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _opacity = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 100,
                TickStyle = TickStyle.None,
                AutoSize = false,
                Height = 24,
                BackColor = Theme.Surface
            };
            _opacity.MouseDown += delegate { PushUndoCoalesced("opacity"); };
            _opacity.ValueChanged += Opacity_ValueChanged;

            _layerUp = SmallButton("▲", "Move layer up");
            _layerDown = SmallButton("▼", "Move layer down");
            _layerDup = SmallButton("⧉", "Duplicate layer");
            _layerDel = SmallButton("✕", "Delete layer");
            _layerAdd = SmallButton("＋", "Add an image file as a layer");
            _layerUp.Click += delegate { MoveLayer(1); };
            _layerDown.Click += delegate { MoveLayer(-1); };
            _layerDup.Click += delegate { DuplicateLayer(); };
            _layerDel.Click += delegate { DeleteLayer(); };
            _layerAdd.Click += delegate { AddImageFileLayer(); };

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

            // ----- assets (bottom half) -----
            var assetsTitle = new Label
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

            _rightSide.Controls.Add(layersTitle);
            _rightSide.Controls.Add(_layerList);
            _rightSide.Controls.Add(_opacityLbl);
            _rightSide.Controls.Add(_opacity);
            _rightSide.Controls.Add(_layerUp);
            _rightSide.Controls.Add(_layerDown);
            _rightSide.Controls.Add(_layerDup);
            _rightSide.Controls.Add(_layerDel);
            _rightSide.Controls.Add(_layerAdd);
            _rightSide.Controls.Add(_renameBox);
            _rightSide.Controls.Add(assetsTitle);
            _rightSide.Controls.Add(_assetTree);
            _rightSide.Controls.Add(_assetList);
            _rightSide.Controls.Add(_assetImport);
            _rightSide.Controls.Add(_assetFolder);

            _rightSide.Resize += delegate
            {
                int w = _rightSide.ClientSize.Width - 20;
                int h = _rightSide.ClientSize.Height;
                int layersH = (int)(h * 0.42);

                _layerList.SetBounds(10, 26, w, layersH - 88);
                _opacityLbl.Location = new Point(10, 26 + layersH - 84);
                _opacity.SetBounds(10, 26 + layersH - 66, w, 24);
                int bx = 10;
                foreach (Button b in new[] { _layerUp, _layerDown, _layerDup, _layerDel, _layerAdd })
                {
                    b.Location = new Point(bx, 26 + layersH - 36);
                    bx += b.Width + 6;
                }

                int ay = 26 + layersH + 8;
                assetsTitle.Location = new Point(10, ay);
                int treeH = Math.Max(60, (h - ay - 96) / 3);
                _assetTree.SetBounds(10, ay + 18, w, treeH);
                _assetList.SetBounds(10, ay + 18 + treeH + 6, w, Math.Max(60, h - (ay + 18 + treeH + 6) - 40));
                _assetImport.Location = new Point(10, h - 32);
                _assetFolder.Location = new Point(10 + _assetImport.Width + 6, h - 32);
                _rightSide.Invalidate();
            };
        }

        Button SmallButton(string text, string tip)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(34, 26),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.Text,
                BackColor = Theme.Surface,
                Font = Theme.Small,
                TabStop = false
            };
            b.BackColor = Theme.FieldBg;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.Border;
            _tips.SetToolTip(b, tip);
            return b;
        }

        void BuildCanvas()
        {
            _canvasPanel = new CanvasPanel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeHelper.IsDarkMode ? Color.FromArgb(24, 24, 28) : Color.FromArgb(210, 210, 214)
            };
            _canvasPanel.Paint += Canvas_Paint;
            _canvasPanel.MouseDown += Canvas_MouseDown;
            _canvasPanel.MouseMove += Canvas_MouseMove;
            _canvasPanel.MouseUp += Canvas_MouseUp;
            _canvasPanel.MouseWheel += Canvas_MouseWheel;
            _canvasPanel.MouseEnter += delegate { if (ActiveControl == null || !(ActiveControl is TextBoxBase)) _canvasPanel.Focus(); };
            _canvasPanel.Resize += delegate { if (_viewFitted) FitView(); _canvasPanel.Invalidate(); };
        }

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
            _statusLeft = new Label
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Font = Theme.Small,
                Padding = new Padding(12, 6, 0, 0)
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
            _status.Controls.Add(_statusLeft);
            _status.Controls.Add(_statusRight);
        }

        // ============================================================== document ops

        EditorLayer SelectedLayer()
        {
            return _sel >= 0 && _sel < _layers.Count ? _layers[_sel] : null;
        }

        string NextName(string kind)
        {
            _nameCounter++;
            return kind + " " + _nameCounter;
        }

        void NewDocument()
        {
            CommitInlineEdit();
            int w = _hasDoc ? _canvas.Width : 1200, h = _hasDoc ? _canvas.Height : 800;
            bool white = true;
            if (!CanvasSizeDialog.Ask(this, "New canvas", ref w, ref h, true, ref white)) return;
            if (_hasDoc && _dirty &&
                !ModernDialog.Confirm("Start over?", "The current layers will be discarded.", "Start new", "Keep working")) return;

            _layers.Clear();
            _undo.Clear();
            _redo.Clear();
            _canvas = new Size(w, h);
            _canvasBg = white ? Color.White : Color.Transparent;
            _hasDoc = true;
            _dirty = false;
            _sel = -1;
            _cropRect = null;
            FitView();
            AfterDocumentChange();
        }

        void OpenFile()
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.wmf;*.emf|All files|*.*",
                Title = "Open image"
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try { AddBitmapLayer(AssetStore.LoadFull(ofd.FileName), Path.GetFileNameWithoutExtension(ofd.FileName)); }
                catch (Exception ex) { ModernDialog.Info("Could not open it", ex.Message); }
            }
        }

        void AddImageFileLayer()
        {
            OpenFile();
        }

        /// <summary>Paste: becomes the document when there is none, a new layer otherwise.</summary>
        void PasteFromClipboard(bool quiet)
        {
            Bitmap bmp = null;
            try
            {
                Image img = Clipboard.GetImage();
                if (img != null)
                {
                    bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                    img.Dispose();
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    foreach (string file in Clipboard.GetFileDropList())
                    {
                        if (AssetStore.IsSupported(file)) { bmp = AssetStore.LoadFull(file); break; }
                    }
                }
            }
            catch (System.Runtime.InteropServices.ExternalException) { }

            if (bmp == null)
            {
                if (!quiet) Toast.Show("No image on the clipboard.");
                return;
            }
            AddBitmapLayer(bmp, NextName("Pasted"));
        }

        void AddBitmapLayer(Bitmap bmp, string name)
        {
            if (!_hasDoc)
            {
                _canvas = bmp.Size;
                _canvasBg = Color.Transparent;
                _hasDoc = true;
                var first = new RasterLayer(bmp) { Name = name, Bounds = new RectangleF(0, 0, bmp.Width, bmp.Height) };
                _layers.Add(first);
                _sel = 0;
                FitView();
                AfterDocumentChange();
                return;
            }

            PushUndo();
            // drop it centred, scaled down when it would not fit the canvas
            float scale = Math.Min(1f, Math.Min((float)_canvas.Width / bmp.Width, (float)_canvas.Height / bmp.Height));
            float w = bmp.Width * scale, h = bmp.Height * scale;
            var layer = new RasterLayer(bmp)
            {
                Name = name,
                Bounds = new RectangleF((_canvas.Width - w) / 2f, (_canvas.Height - h) / 2f, w, h)
            };
            _layers.Add(layer);
            _sel = _layers.Count - 1;
            SelectTool(Tool.Move);
            AfterDocumentChange();
        }

        void SaveAs()
        {
            CommitInlineEdit();
            if (!EnsureDoc()) return;
            using (var sfd = new SaveFileDialog
            {
                Filter = "PNG (keeps transparency)|*.png|JPEG|*.jpg|Bitmap|*.bmp",
                Title = "Save image",
                FileName = "MicroApp " + DateTime.Now.ToString("yyyy-MM-dd HHmmss")
            })
            {
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                    using (Bitmap flat = Flattened(ext == ".jpg" || ext == ".jpeg" || ext == ".bmp"))
                    {
                        if (ext == ".jpg" || ext == ".jpeg") SaveJpeg(flat, sfd.FileName, 90);
                        else if (ext == ".bmp") flat.Save(sfd.FileName, ImageFormat.Bmp);
                        else flat.Save(sfd.FileName, ImageFormat.Png);
                    }
                    _dirty = false;
                    Toast.Show("Saved.\r\n" + sfd.FileName);
                }
                catch (Exception ex) { ModernDialog.Info("Could not save it", ex.Message); }
            }
        }

        static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
            if (codec == null) { bmp.Save(path, ImageFormat.Jpeg); return; }
            using (var p = new EncoderParameters(1))
            {
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                bmp.Save(path, codec, p);
            }
        }

        void CopyResult()
        {
            CommitInlineEdit();
            if (!EnsureDoc()) return;
            try
            {
                using (Bitmap flat = Flattened(true))
                {
                    Clipboard.SetImage(flat);
                }
                _dirty = false;
                Toast.Show("Result copied to the clipboard.");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Toast.Show("The clipboard is busy - try again.");
            }
        }

        /// <summary>The finished picture. opaque: composed over white for JPG/clipboard.</summary>
        Bitmap Flattened(bool opaque)
        {
            Color bg = _canvasBg;
            if (opaque && bg.A < 255) bg = Color.White;
            return EditorRender.Flatten(_layers, _canvas, bg);
        }

        bool EnsureDoc()
        {
            if (_hasDoc) return true;
            Toast.Show("Nothing here yet - paste an image or use File > New.");
            return false;
        }

        void Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_dirty && _layers.Count > 0 &&
                !ModernDialog.Confirm("Close the editor?", "The layers were not saved or copied out.", "Close anyway", "Keep editing"))
            {
                e.Cancel = true;
            }
        }

        void Form_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        void Form_DragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (string file in files)
            {
                if (!AssetStore.IsSupported(file)) continue;
                try { AddBitmapLayer(AssetStore.LoadFull(file), Path.GetFileNameWithoutExtension(file)); }
                catch (Exception ex) { ModernDialog.Info("Could not open it", ex.Message); }
            }
        }

        // ===================================================================== undo

        Snapshot TakeSnapshot()
        {
            var s = new Snapshot
            {
                Layers = _layers.Select(l => l.Clone()).ToArray(),
                Canvas = _canvas,
                CanvasBg = _canvasBg,
                Sel = _sel
            };
            return s;
        }

        void RestoreSnapshot(Snapshot s)
        {
            _layers.Clear();
            _layers.AddRange(s.Layers.Select(l => l.Clone()));
            _canvas = s.Canvas;
            _canvasBg = s.CanvasBg;
            _sel = Math.Min(s.Sel, _layers.Count - 1);
            _cropRect = null;
            AfterDocumentChange();
        }

        void PushUndo()
        {
            if (!_hasDoc) return;
            _undo.Add(TakeSnapshot());
            if (_undo.Count > 40) _undo.RemoveAt(0);
            _redo.Clear();
            _coalesceKey = null;
            _dirty = true;
        }

        /// <summary>
        /// Undo for rapid-fire tweaks (opacity slider, spinner arrows): the first change of
        /// a burst snapshots, the rest within a second ride on it, so undo steps back over
        /// the whole adjustment instead of one click at a time.
        /// </summary>
        void PushUndoCoalesced(string key)
        {
            if (!_hasDoc) return;
            if (_coalesceKey == key && (DateTime.UtcNow - _coalesceAt).TotalSeconds < 1.2)
            {
                _coalesceAt = DateTime.UtcNow;
                _dirty = true;
                return;
            }
            PushUndo();
            _coalesceKey = key;
            _coalesceAt = DateTime.UtcNow;
        }

        void DoUndo()
        {
            if (_undo.Count == 0) return;
            CancelInlineEdit(false);
            _redo.Add(TakeSnapshot());
            Snapshot s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            RestoreSnapshot(s);
        }

        void DoRedo()
        {
            if (_redo.Count == 0) return;
            CancelInlineEdit(false);
            _undo.Add(TakeSnapshot());
            Snapshot s = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            RestoreSnapshot(s);
        }

        /// <summary>One call after anything that changed layers/canvas: refresh every view.</summary>
        void AfterDocumentChange()
        {
            RefreshLayerList();
            SyncOptionsFromSelection();
            RelayoutOptions();
            UpdateStatus();
            _canvasPanel.Invalidate();
        }

        // ================================================================= view math

        PointF CanvasToScreen(PointF p)
        {
            return new PointF(_origin.X + p.X * _zoom, _origin.Y + p.Y * _zoom);
        }

        PointF ScreenToCanvas(Point p)
        {
            return new PointF((p.X - _origin.X) / _zoom, (p.Y - _origin.Y) / _zoom);
        }

        Rectangle CanvasScreenRect()
        {
            return new Rectangle((int)_origin.X, (int)_origin.Y,
                                 (int)(_canvas.Width * _zoom), (int)(_canvas.Height * _zoom));
        }

        void FitView()
        {
            if (!_hasDoc || _canvas.Width < 1) return;
            Size client = _canvasPanel.ClientSize;
            float z = Math.Min((client.Width - 48f) / _canvas.Width, (client.Height - 48f) / _canvas.Height);
            _zoom = Math.Max(0.02f, Math.Min(1f, z));
            CenterView();
            _viewFitted = true;
            UpdateStatus();
        }

        void CenterView()
        {
            Size client = _canvasPanel.ClientSize;
            _origin = new PointF((client.Width - _canvas.Width * _zoom) / 2f,
                                 (client.Height - _canvas.Height * _zoom) / 2f);
        }

        void ZoomAt(Point screenPt, float factor)
        {
            if (!_hasDoc) return;
            float z = Math.Max(0.02f, Math.Min(16f, _zoom * factor));
            if (Math.Abs(z - _zoom) < 0.0001f) return;
            PointF before = ScreenToCanvas(screenPt);
            _zoom = z;
            _origin = new PointF(screenPt.X - before.X * _zoom, screenPt.Y - before.Y * _zoom);
            _viewFitted = false;
            UpdateStatus();
            _canvasPanel.Invalidate();
        }

        void UpdateStatus()
        {
            _statusLeft.Text = _hasDoc
                ? string.Format("{0} × {1} px    {2:0}%    {3} layer{4}", _canvas.Width, _canvas.Height,
                                _zoom * 100, _layers.Count, _layers.Count == 1 ? "" : "s")
                : "No image yet";
            string hint;
            string toolName;
            switch (_tool)
            {
                case Tool.Move: toolName = "Move"; hint = "drag moves · handles resize · top handle rotates · double-click text edits"; break;
                case Tool.Crop: toolName = "Crop"; hint = "drag the crop, then Enter applies and Esc cancels"; break;
                case Tool.Rect: toolName = "Rectangle"; hint = "drag to draw · Shift keeps it square"; break;
                case Tool.Ellipse: toolName = "Ellipse"; hint = "drag to draw · Shift keeps it round"; break;
                case Tool.Line: toolName = "Line"; hint = "drag from end to end · Shift snaps to 45°"; break;
                case Tool.Arrow: toolName = "Arrow"; hint = "drag from tail to head · Shift snaps to 45°"; break;
                case Tool.Pen: toolName = "Pen"; hint = "draw freehand · every stroke is its own layer"; break;
                case Tool.Text: toolName = "Text"; hint = "click the canvas to place a text box · Ctrl+Enter commits"; break;
                case Tool.Blur: toolName = "Blur"; hint = "paint over an image layer to blur it softly"; break;
                default: toolName = ""; hint = ""; break;
            }
            _statusRight.Text = hint;
            if (_optToolLbl != null) _optToolLbl.Text = toolName;
        }

        // ================================================================== painting

        static TextureBrush _checker;

        static TextureBrush Checker()
        {
            if (_checker == null)
            {
                var tile = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(tile))
                {
                    g.Clear(Color.FromArgb(238, 238, 238));
                    using (var b = new SolidBrush(Color.FromArgb(205, 205, 205)))
                    {
                        g.FillRectangle(b, 0, 0, 8, 8);
                        g.FillRectangle(b, 8, 8, 8, 8);
                    }
                }
                _checker = new TextureBrush(tile);
            }
            return _checker;
        }

        void Canvas_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (!_hasDoc)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle client = _canvasPanel.ClientRectangle;
                var zone = new Rectangle(client.Width / 2 - 190, client.Height / 2 - 110, 380, 220);
                using (GraphicsPath path = Theme.Round(zone, 14))
                using (var p = new Pen(Theme.Border, 1.6f) { DashStyle = DashStyle.Dash })
                    g.DrawPath(p, path);
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    // a little picture glyph, drawn by hand - font emoji come out as hollow boxes here
                    int cx = zone.X + zone.Width / 2, iy = zone.Y + 28;
                    using (var p = new Pen(Theme.TextDim, 2f) { LineJoin = LineJoin.Round })
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        using (GraphicsPath frame = Theme.Round(new Rectangle(cx - 28, iy, 56, 44), 7))
                            g.DrawPath(p, frame);
                        g.DrawEllipse(p, cx - 16, iy + 9, 9, 9);
                        g.DrawLines(p, new[]
                        {
                            new PointF(cx - 21, iy + 37),
                            new PointF(cx - 6, iy + 20),
                            new PointF(cx + 4, iy + 31),
                            new PointF(cx + 12, iy + 23),
                            new PointF(cx + 21, iy + 37)
                        });
                    }
                    using (var strong = new SolidBrush(Theme.Text))
                        g.DrawString("Paste an image to begin", Theme.Strong, strong,
                                     new Rectangle(zone.X, zone.Y + 92, zone.Width, 30), sf);
                    using (var dim = new SolidBrush(Theme.TextDim))
                        g.DrawString("Ctrl+V pastes the clipboard · drop a file anywhere\r\nor start blank with File > New",
                                     Theme.Small, dim, new Rectangle(zone.X, zone.Y + 124, zone.Width, 60), sf);
                }
                return;
            }

            Rectangle screen = CanvasScreenRect();

            // a soft shadow so the canvas floats above the work area
            for (int i = 4; i >= 1; i--)
            {
                using (var sh = new SolidBrush(Color.FromArgb(14, 0, 0, 0)))
                    g.FillRectangle(sh, screen.X - i, screen.Y - i + 2, screen.Width + i * 2, screen.Height + i * 2);
            }

            // the canvas: checkerboard behind whatever transparency is left
            g.FillRectangle(Checker(), screen);

            GraphicsState st = g.Save();
            g.SetClip(screen);
            g.TranslateTransform(_origin.X, _origin.Y);
            g.ScaleTransform(_zoom, _zoom);
            EditorRender.Prepare(g);
            if (_canvasBg.A > 0)
                using (var b = new SolidBrush(_canvasBg))
                    g.FillRectangle(b, 0, 0, _canvas.Width, _canvas.Height);
            foreach (EditorLayer layer in _layers) layer.Draw(g);
            if (_draft != null) _draft.Draw(g);
            g.Restore(st);

            using (var border = new Pen(Theme.Border))
                g.DrawRectangle(border, screen.X, screen.Y, screen.Width, screen.Height);

            // blur stroke preview: soft circles where the brush has been
            if (_drag == Drag.Blur && _blurScreenPts != null)
            {
                using (var b = new SolidBrush(Color.FromArgb(60, Theme.Accent)))
                    foreach (PointF p in _blurScreenPts)
                        g.FillEllipse(b, p.X - _brushSize / 2f, p.Y - _brushSize / 2f, _brushSize, _brushSize);
            }

            // brush cursor for the blur tool
            if (_tool == Tool.Blur && _canvasPanel.ClientRectangle.Contains(_mouseScreen))
            {
                using (var p = new Pen(Theme.Accent, 1.5f))
                    g.DrawEllipse(p, _mouseScreen.X - _brushSize / 2f, _mouseScreen.Y - _brushSize / 2f, _brushSize, _brushSize);
            }

            // crop overlay: dim everything outside the chosen rectangle
            if (_cropRect.HasValue)
            {
                RectangleF c = _cropRect.Value;
                PointF tl = CanvasToScreen(new PointF(c.Left, c.Top));
                PointF br = CanvasToScreen(new PointF(c.Right, c.Bottom));
                var cropScreen = new RectangleF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
                using (var outside = new Region(_canvasPanel.ClientRectangle))
                using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                {
                    outside.Exclude(cropScreen);
                    g.FillRegion(dim, outside);
                }
                using (var p = new Pen(Color.White, 1f) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(p, cropScreen.X, cropScreen.Y, cropScreen.Width, cropScreen.Height);
            }

            // selection: dashed outline, resize handles, the rotate handle on a stalk
            EditorLayer sel = SelectedLayer();
            if (sel != null && _tool == Tool.Move && _editing == null)
            {
                PointF[] corners = sel.CanvasCorners().Select(p => CanvasToScreen(p)).ToArray();
                using (var p = new Pen(Theme.Accent, 1.4f) { DashStyle = DashStyle.Dash })
                    g.DrawPolygon(p, corners);

                PointF[] handles = HandleScreenPositions(sel);
                using (var fill = new SolidBrush(Color.White))
                using (var edge = new Pen(Theme.Accent, 1.4f))
                {
                    // the stalk from the top edge to the rotate handle
                    PointF topMid = MidPoint(corners[0], corners[1]);
                    g.DrawLine(edge, topMid, handles[8]);
                    for (int i = 0; i < 8; i++)
                    {
                        g.FillRectangle(fill, handles[i].X - 4, handles[i].Y - 4, 8, 8);
                        g.DrawRectangle(edge, handles[i].X - 4, handles[i].Y - 4, 8, 8);
                    }
                    g.FillEllipse(fill, handles[8].X - 5, handles[8].Y - 5, 10, 10);
                    g.DrawEllipse(edge, handles[8].X - 5, handles[8].Y - 5, 10, 10);
                }
            }
        }

        static PointF MidPoint(PointF a, PointF b)
        {
            return new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
        }

        /// <summary>
        /// Handle layout in layer-local coordinates:
        /// 0 TL, 1 TM, 2 TR, 3 MR, 4 BR, 5 BM, 6 BL, 7 ML - opposite handle = (i+4)%8.
        /// </summary>
        static PointF[] HandleLocalPositions(RectangleF b)
        {
            float mx = b.X + b.Width / 2f, my = b.Y + b.Height / 2f;
            return new[]
            {
                new PointF(b.Left, b.Top), new PointF(mx, b.Top), new PointF(b.Right, b.Top),
                new PointF(b.Right, my), new PointF(b.Right, b.Bottom), new PointF(mx, b.Bottom),
                new PointF(b.Left, b.Bottom), new PointF(b.Left, my)
            };
        }

        /// <summary>The eight resize handles plus [8] = the rotate handle, in screen pixels.</summary>
        PointF[] HandleScreenPositions(EditorLayer layer)
        {
            PointF[] local = HandleLocalPositions(layer.Bounds);
            var result = new PointF[9];
            for (int i = 0; i < 8; i++) result[i] = CanvasToScreen(layer.ToCanvas(local[i]));
            // rotate handle: 26 screen px out from the top-middle, away from the box
            PointF topMid = result[1];
            PointF centre = CanvasToScreen(layer.Center);
            float dx = topMid.X - centre.X, dy = topMid.Y - centre.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) { dx = 0; dy = -1; len = 1; }
            result[8] = new PointF(topMid.X + dx / len * 26, topMid.Y + dy / len * 26);
            return result;
        }

        int HitHandle(EditorLayer layer, Point screenPt)
        {
            PointF[] handles = HandleScreenPositions(layer);
            for (int i = 0; i < 9; i++)
            {
                float r = i == 8 ? 8 : 6;
                if (Math.Abs(screenPt.X - handles[i].X) <= r && Math.Abs(screenPt.Y - handles[i].Y) <= r) return i;
            }
            return -1;
        }

        // ============================================================= mouse machine

        void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            ZoomAt(e.Location, e.Delta > 0 ? 1.15f : 1f / 1.15f);
        }

        void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            _canvasPanel.Focus();
            bool wasEditingText = _editing != null;
            CommitInlineEdit();
            _mouseScreen = e.Location;
            _downScreen = e.Location;
            _dragUndoPushed = false;
            if (!_hasDoc) return;
            PointF cp = ScreenToCanvas(e.Location);
            _downCanvas = cp;

            if (e.Button == MouseButtons.Middle || (_spaceDown && e.Button == MouseButtons.Left))
            {
                _drag = Drag.Pan;
                _panOrigin0 = _origin;
                _canvasPanel.Cursor = Cursors.SizeAll;
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            switch (_tool)
            {
                case Tool.Move:
                {
                    EditorLayer sel = SelectedLayer();
                    if (sel != null)
                    {
                        int h = HitHandle(sel, e.Location);
                        if (h == 8)
                        {
                            _drag = Drag.Rotate;
                            _rot0 = sel.RotationDeg;
                            PointF c = CanvasToScreen(sel.Center);
                            _bounds0 = sel.Bounds;
                            _downScreen = e.Location;
                            _handle = 8;
                            _rotStart = (float)(Math.Atan2(e.Y - c.Y, e.X - c.X) * 180 / Math.PI);
                            return;
                        }
                        if (h >= 0)
                        {
                            _drag = Drag.Resize;
                            _handle = h;
                            _bounds0 = sel.Bounds;
                            var selText = sel as TextLayer;
                            _fontSizeAtDragStart = selText != null ? selText.FontSize : 0;
                            _matrix0 = sel.GetMatrix();
                            PointF[] local = HandleLocalPositions(_bounds0);
                            _anchorCanvas = TransformPoint(_matrix0, local[(h + 4) % 8]);
                            return;
                        }
                    }
                    // pick the topmost layer under the cursor
                    int hit = -1;
                    for (int i = _layers.Count - 1; i >= 0; i--)
                        if (_layers[i].Visible && _layers[i].HitTest(cp)) { hit = i; break; }
                    _sel = hit;
                    RefreshLayerList();
                    SyncOptionsFromSelection();
                    RelayoutOptions();
                    if (hit >= 0)
                    {
                        var hitText = _layers[hit] as TextLayer;
                        if (e.Clicks == 2 && hitText != null)
                        {
                            PushUndo();
                            BeginInlineEdit(hitText, false);
                            break;
                        }
                        _drag = Drag.Move;
                        _bounds0 = _layers[hit].Bounds;
                    }
                    _canvasPanel.Invalidate();
                    break;
                }

                case Tool.Crop:
                    _drag = Drag.Crop;
                    _cropRect = new RectangleF(Clamp(cp).X, Clamp(cp).Y, 0, 0);
                    break;

                case Tool.Rect:
                case Tool.Ellipse:
                case Tool.Line:
                case Tool.Arrow:
                    _drag = Drag.Draw;
                    _draft = null;
                    break;

                case Tool.Pen:
                    _drag = Drag.Draw;
                    _penPts = new List<PointF> { cp };
                    _draft = null;
                    break;

                case Tool.Text:
                    if (!wasEditingText) PlaceTextLayer(cp);
                    break;

                case Tool.Blur:
                    BeginBlurStroke(cp, e.Location);
                    break;
            }
        }

        float _rotStart;

        /// <summary>The undo step for a move/resize/rotate: taken at the first real movement,
        /// so a click that selects and lets go never burns one.</summary>
        void DragUndoOnce()
        {
            if (_dragUndoPushed) return;
            _dragUndoPushed = true;
            // the layer is still where it started - every drag recomputes from _bounds0/_rot0
            PushUndo();
        }

        static PointF TransformPoint(System.Drawing.Drawing2D.Matrix m, PointF p)
        {
            PointF[] pts = { p };
            m.TransformPoints(pts);
            return pts[0];
        }

        PointF Clamp(PointF p)
        {
            return new PointF(Math.Max(0, Math.Min(_canvas.Width, p.X)),
                              Math.Max(0, Math.Min(_canvas.Height, p.Y)));
        }

        void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            _mouseScreen = e.Location;
            if (!_hasDoc)
            {
                return;
            }
            PointF cp = ScreenToCanvas(e.Location);

            switch (_drag)
            {
                case Drag.Pan:
                    _origin = new PointF(_panOrigin0.X + (e.X - _downScreen.X), _panOrigin0.Y + (e.Y - _downScreen.Y));
                    _viewFitted = false;
                    _canvasPanel.Invalidate();
                    return;

                case Drag.Move:
                {
                    EditorLayer sel = SelectedLayer();
                    if (sel == null) return;
                    DragUndoOnce();
                    RectangleF b = _bounds0;
                    b.Offset(cp.X - _downCanvas.X, cp.Y - _downCanvas.Y);
                    sel.Bounds = b;
                    _canvasPanel.Invalidate();
                    return;
                }

                case Drag.Resize:
                    DragUndoOnce();
                    ResizeDrag(cp, (ModifierKeys & Keys.Shift) == Keys.Shift);
                    return;

                case Drag.Rotate:
                {
                    EditorLayer sel = SelectedLayer();
                    if (sel == null) return;
                    DragUndoOnce();
                    PointF c = CanvasToScreen(sel.Center);
                    float a = (float)(Math.Atan2(e.Y - c.Y, e.X - c.X) * 180 / Math.PI);
                    float rot = _rot0 + (a - _rotStart);
                    if ((ModifierKeys & Keys.Shift) == Keys.Shift) rot = (float)Math.Round(rot / 15f) * 15f;
                    sel.RotationDeg = Normalise(rot);
                    _canvasPanel.Invalidate();
                    return;
                }

                case Drag.Draw:
                    if (_tool == Tool.Pen)
                    {
                        if (_penPts != null && (_penPts.Count == 0 || Dist(_penPts[_penPts.Count - 1], cp) > 1.5f / _zoom))
                            _penPts.Add(cp);
                        _draft = PenDraft();
                    }
                    else
                    {
                        _draft = ShapeDraft(_downCanvas, cp, (ModifierKeys & Keys.Shift) == Keys.Shift);
                    }
                    _canvasPanel.Invalidate();
                    return;

                case Drag.Crop:
                {
                    PointF a = Clamp(_downCanvas), b = Clamp(cp);
                    _cropRect = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                                                    Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                    RelayoutOptions();
                    _canvasPanel.Invalidate();
                    return;
                }

                case Drag.Blur:
                    ContinueBlurStroke(cp, e.Location);
                    return;
            }

            // idle: cursor feedback (and the blur brush ring follows the mouse)
            if (_tool == Tool.Blur)
            {
                _canvasPanel.Invalidate();
                _canvasPanel.Cursor = Cursors.Cross;
                return;
            }
            if (_tool == Tool.Move)
            {
                EditorLayer sel = SelectedLayer();
                if (sel != null)
                {
                    int h = HitHandle(sel, e.Location);
                    if (h == 8) { _canvasPanel.Cursor = Cursors.Hand; return; }
                    if (h >= 0)
                    {
                        _canvasPanel.Cursor = (h == 1 || h == 5) ? Cursors.SizeNS
                                            : (h == 3 || h == 7) ? Cursors.SizeWE
                                            : (h == 0 || h == 4) ? Cursors.SizeNWSE : Cursors.SizeNESW;
                        return;
                    }
                }
                bool over = false;
                for (int i = _layers.Count - 1; i >= 0; i--)
                    if (_layers[i].Visible && _layers[i].HitTest(cp)) { over = true; break; }
                _canvasPanel.Cursor = over ? Cursors.SizeAll : Cursors.Default;
                return;
            }
            _canvasPanel.Cursor = _tool == Tool.Move ? Cursors.Default : Cursors.Cross;
        }

        static float Dist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static float Normalise(float deg)
        {
            deg = deg % 360f;
            if (deg > 180f) deg -= 360f;
            if (deg < -180f) deg += 360f;
            return deg;
        }

        void ResizeDrag(PointF canvasPt, bool keepAspect)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null || _matrix0 == null) return;

            // work in the layer's own (unrotated) space, through the matrix captured at
            // mouse-down so the mapping does not drift while the bounds change under it
            PointF local;
            using (Matrix inv = _matrix0.Clone())
            {
                inv.Invert();
                local = TransformPoint(inv, canvasPt);
            }
            PointF[] anchors = HandleLocalPositions(_bounds0);
            PointF anchor = anchors[(_handle + 4) % 8];

            float left = _bounds0.Left, right = _bounds0.Right, top = _bounds0.Top, bottom = _bounds0.Bottom;
            bool horizontal = _handle != 1 && _handle != 5;   // top/bottom-middle: vertical only
            bool vertical = _handle != 3 && _handle != 7;     // left/right-middle: horizontal only
            if (horizontal)
            {
                if (anchor.X > _bounds0.Left + _bounds0.Width / 2f) { left = Math.Min(local.X, anchor.X - 8); right = anchor.X; }
                else { right = Math.Max(local.X, anchor.X + 8); left = anchor.X; }
            }
            if (vertical)
            {
                if (anchor.Y > _bounds0.Top + _bounds0.Height / 2f) { top = Math.Min(local.Y, anchor.Y - 8); bottom = anchor.Y; }
                else { bottom = Math.Max(local.Y, anchor.Y + 8); top = anchor.Y; }
            }

            var nb = RectangleF.FromLTRB(left, top, right, bottom);
            bool corner = _handle == 0 || _handle == 2 || _handle == 4 || _handle == 6;
            if (keepAspect && corner && _bounds0.Width > 1 && _bounds0.Height > 1)
            {
                float ratio = _bounds0.Width / _bounds0.Height;
                if (nb.Width / Math.Max(1f, nb.Height) > ratio)
                    nb = ResizeAbout(nb, anchor, nb.Height * ratio, nb.Height);
                else
                    nb = ResizeAbout(nb, anchor, nb.Width, nb.Width / ratio);
            }

            // a corner drag on text scales the type itself; an edge drag reflows the box
            var text = sel as TextLayer;
            if (text != null && corner && _bounds0.Height > 1)
                text.FontSize = Math.Max(4f, _fontSizeAtDragStart * (nb.Height / _bounds0.Height));

            sel.Bounds = nb;
            // the centre moved, so the rotation pivot moved: put the anchor corner back
            PointF anchorNow = sel.ToCanvas(HandleLocalPositions(nb)[(_handle + 4) % 8]);
            RectangleF fixedUp = sel.Bounds;
            fixedUp.Offset(_anchorCanvas.X - anchorNow.X, _anchorCanvas.Y - anchorNow.Y);
            sel.Bounds = fixedUp;
            _canvasPanel.Invalidate();
        }

        static RectangleF ResizeAbout(RectangleF r, PointF anchor, float w, float h)
        {
            float x = anchor.X > r.Left + r.Width / 2f ? r.Right - w : r.Left;
            float y = anchor.Y > r.Top + r.Height / 2f ? r.Bottom - h : r.Top;
            return new RectangleF(x, y, w, h);
        }

        float _fontSizeAtDragStart;

        ShapeLayer ShapeDraft(PointF a, PointF b, bool square)
        {
            ShapeKind kind = _tool == Tool.Rect ? ShapeKind.Rectangle
                           : _tool == Tool.Ellipse ? ShapeKind.Ellipse
                           : _tool == Tool.Line ? ShapeKind.Line : ShapeKind.Arrow;
            var s = new ShapeLayer { Kind = kind, Stroke = _stroke, StrokeWidth = _strokeW, Fill = _fill };
            if (kind == ShapeKind.Line || kind == ShapeKind.Arrow)
            {
                if (square)   // shift: snap the line to 45° steps
                {
                    double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
                    double snap = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
                    float len = Dist(a, b);
                    b = new PointF(a.X + (float)(Math.Cos(snap) * len), a.Y + (float)(Math.Sin(snap) * len));
                }
                var box = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                if (box.Width < 1) box.Width = 1;
                if (box.Height < 1) box.Height = 1;
                s.Bounds = box;
                s.Points.Add(new PointF((a.X - box.X) / box.Width, (a.Y - box.Y) / box.Height));
                s.Points.Add(new PointF((b.X - box.X) / box.Width, (b.Y - box.Y) / box.Height));
            }
            else
            {
                float w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);
                if (square) w = h = Math.Max(w, h);
                float x = b.X < a.X ? a.X - w : a.X;
                float y = b.Y < a.Y ? a.Y - h : a.Y;
                s.Bounds = new RectangleF(x, y, w, h);
            }
            return s;
        }

        ShapeLayer PenDraft()
        {
            if (_penPts == null || _penPts.Count < 2) return null;
            float minX = _penPts.Min(p => p.X), maxX = _penPts.Max(p => p.X);
            float minY = _penPts.Min(p => p.Y), maxY = _penPts.Max(p => p.Y);
            var box = new RectangleF(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
            var s = new ShapeLayer { Kind = ShapeKind.Freehand, Stroke = _stroke, StrokeWidth = _strokeW, Bounds = box };
            foreach (PointF p in _penPts)
                s.Points.Add(new PointF((p.X - box.X) / box.Width, (p.Y - box.Y) / box.Height));
            return s;
        }

        void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            Drag was = _drag;
            _drag = Drag.None;
            _canvasPanel.Cursor = Cursors.Default;

            switch (was)
            {
                case Drag.Draw:
                {
                    ShapeLayer done = _draft;
                    _draft = null;
                    _penPts = null;
                    if (done != null && (done.Bounds.Width > 2 || done.Bounds.Height > 2))
                    {
                        PushUndo();
                        done.Name = NextName(done.Kind.ToString());
                        _layers.Add(done);
                        _sel = _layers.Count - 1;
                        AfterDocumentChange();
                    }
                    else _canvasPanel.Invalidate();
                    break;
                }

                case Drag.Crop:
                    if (_cropRect.HasValue && (_cropRect.Value.Width < 3 || _cropRect.Value.Height < 3))
                        _cropRect = null;
                    RelayoutOptions();
                    _canvasPanel.Invalidate();
                    break;

                case Drag.Blur:
                    FinishBlurStroke();
                    break;

                case Drag.Move:
                case Drag.Resize:
                case Drag.Rotate:
                    if (_matrix0 != null) { _matrix0.Dispose(); _matrix0 = null; }
                    _canvasPanel.Invalidate();
                    break;
            }
        }

        // ================================================================ blur brush

        void BeginBlurStroke(PointF cp, Point screenPt)
        {
            RasterLayer target = SelectedLayer() as RasterLayer;
            if (target == null || !target.Visible)
            {
                target = null;
                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    var r = _layers[i] as RasterLayer;
                    if (r != null && r.Visible && r.HitTest(cp)) { target = r; _sel = i; RefreshLayerList(); break; }
                }
            }
            if (target == null)
            {
                Toast.Show("Blur works on image layers - click one first.");
                return;
            }
            _blurTarget = target;
            _blurDabs = new List<PointF>();
            _blurScreenPts = new List<PointF>();
            // brush radius: screen px → canvas units → this layer's bitmap pixels
            float sx = target.Image.Width / Math.Max(1f, target.Bounds.Width);
            float sy = target.Image.Height / Math.Max(1f, target.Bounds.Height);
            _blurImgRadius = _brushSize / 2f / _zoom * (sx + sy) / 2f;
            _drag = Drag.Blur;
            ContinueBlurStroke(cp, screenPt);
        }

        void ContinueBlurStroke(PointF cp, Point screenPt)
        {
            if (_blurTarget == null) return;
            PointF local = _blurTarget.ToLocal(cp);
            RectangleF b = _blurTarget.Bounds;
            _blurDabs.Add(new PointF((local.X - b.X) * _blurTarget.Image.Width / Math.Max(1f, b.Width),
                                     (local.Y - b.Y) * _blurTarget.Image.Height / Math.Max(1f, b.Height)));
            _blurScreenPts.Add(screenPt);
            _canvasPanel.Invalidate();
        }

        void FinishBlurStroke()
        {
            RasterLayer target = _blurTarget;
            List<PointF> dabs = _blurDabs;
            _blurTarget = null;
            _blurDabs = null;
            _blurScreenPts = null;
            if (target == null || dabs == null || dabs.Count == 0) { _canvasPanel.Invalidate(); return; }

            PushUndo();
            try
            {
                // REPLACE the bitmap - undo snapshots share the old reference
                target.Image = EditorRender.BlurDabs(target.Image, dabs, _blurImgRadius, _blurStrength * 2);
            }
            catch (Exception ex)
            {
                ModernDialog.Info("Blur failed", ex.Message);
            }
            AfterDocumentChange();
        }

        // ================================================================ text layers

        string _editTextBefore;
        bool _editingIsNew;

        void PlaceTextLayer(PointF cp)
        {
            PushUndo();
            var layer = new TextLayer
            {
                Name = NextName("Text"),
                Bounds = new RectangleF(cp.X, cp.Y, Math.Max(160, _fontSize * 8), _fontSize * 1.8f),
                FontFamily = _fontFamily,
                FontSize = _fontSize,
                Bold = _bold,
                Italic = _italic,
                Underline = _underline,
                Color = _textColor,
                BackColor = _textBack,
                OutlineColor = _textOutline
            };
            _layers.Add(layer);
            _sel = _layers.Count - 1;
            RefreshLayerList();
            BeginInlineEdit(layer, true);
        }

        void BeginInlineEdit(TextLayer layer, bool isNew)
        {
            CommitInlineEdit();
            _editing = layer;
            _editingIsNew = isNew;
            _editTextBefore = layer.Text;
            _editingWasVisible = layer.Visible;
            layer.Visible = false;

            if (_inlineEdit == null)
            {
                _inlineEdit = new TextBox
                {
                    Multiline = true,
                    AcceptsReturn = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    WordWrap = true,
                    Visible = false
                };
                _inlineEdit.KeyDown += InlineEdit_KeyDown;
                _inlineEdit.LostFocus += delegate { CommitInlineEdit(); };
                _canvasPanel.Controls.Add(_inlineEdit);
            }

            PointF tl = CanvasToScreen(new PointF(layer.Bounds.Left, layer.Bounds.Top));
            _inlineEdit.SetBounds((int)tl.X, (int)tl.Y,
                Math.Max(80, (int)(layer.Bounds.Width * _zoom)),
                Math.Max(30, (int)(layer.Bounds.Height * _zoom)));
            try { _inlineEdit.Font = new Font(layer.FontFamily, Math.Max(4f, layer.FontSize * _zoom), layer.Style & ~FontStyle.Underline, GraphicsUnit.Pixel); }
            catch { _inlineEdit.Font = Theme.Base; }
            _inlineEdit.ForeColor = layer.Color.A > 60 ? Color.FromArgb(255, layer.Color) : Theme.Text;
            _inlineEdit.BackColor = layer.BackColor.A > 60 ? Color.FromArgb(255, layer.BackColor) : Theme.FieldBg;
            _inlineEdit.Text = layer.Text;
            _inlineEdit.Visible = true;
            _inlineEdit.Focus();
            _inlineEdit.SelectionStart = _inlineEdit.TextLength;
            _canvasPanel.Invalidate();
            UpdateStatus();
        }

        void InlineEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CancelInlineEdit(true);
            }
            else if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CommitInlineEdit();
            }
        }

        void CommitInlineEdit()
        {
            if (_editing == null) return;
            TextLayer layer = _editing;
            _editing = null;
            string text = _inlineEdit.Text;
            _inlineEdit.Visible = false;
            layer.Visible = _editingWasVisible;

            if (string.IsNullOrEmpty(text.Trim()) && _editingIsNew)
            {
                // nothing typed: take the fresh layer (and its undo step) back out
                _layers.Remove(layer);
                if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
                _sel = _layers.Count - 1;
                AfterDocumentChange();
                return;
            }

            layer.Text = text;
            GrowTextBounds(layer);
            if (text == _editTextBefore && !_editingIsNew && _undo.Count > 0)
            {
                _undo.RemoveAt(_undo.Count - 1);   // opened and closed without changing anything
            }
            AfterDocumentChange();
        }

        void CancelInlineEdit(bool restore)
        {
            if (_editing == null) return;
            TextLayer layer = _editing;
            _editing = null;
            _inlineEdit.Visible = false;
            layer.Visible = _editingWasVisible;
            if (restore)
            {
                if (_editingIsNew)
                {
                    _layers.Remove(layer);
                    if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
                    _sel = _layers.Count - 1;
                }
                else
                {
                    layer.Text = _editTextBefore;
                    if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
                }
            }
            AfterDocumentChange();
        }

        /// <summary>After editing, make sure the wrap box is tall enough for the text.</summary>
        void GrowTextBounds(TextLayer layer)
        {
            try
            {
                using (Graphics g = _canvasPanel.CreateGraphics())
                using (Font f = layer.MakeFont())
                {
                    SizeF size = g.MeasureString(layer.Text + " ", f, (int)Math.Max(20, layer.Bounds.Width));
                    RectangleF b = layer.Bounds;
                    if (size.Height > b.Height) { b.Height = size.Height + 4; layer.Bounds = b; }
                    if (size.Width > b.Width) { b.Width = size.Width + 4; layer.Bounds = b; }
                }
            }
            catch { }
        }

        // ================================================================== keyboard

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && !IsTypingContext()) _spaceDown = true;
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) _spaceDown = false;
            base.OnKeyUp(e);
        }

        bool IsTypingContext()
        {
            if (_inlineEdit != null && _inlineEdit.Visible) return true;
            if (_renameBox.Visible) return true;
            Control c = ActiveControl;
            // focus may sit on the inner edit of a NumericUpDown/ComboBox
            foreach (Control host in new Control[] { _optFont, _optSize, _optWidth, _optBrush, _optStrength })
                if (host != null && host.ContainsFocus) return true;
            return c is TextBoxBase;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool typing = IsTypingContext();

            if (keyData == Keys.Escape)
            {
                if (_editing != null) { CancelInlineEdit(true); return true; }
                if (_renameBox.Visible) { _renameBox.Visible = false; return true; }
                if (_cropRect.HasValue) { _cropRect = null; RelayoutOptions(); _canvasPanel.Invalidate(); return true; }
                if (_sel >= 0) { _sel = -1; RefreshLayerList(); RelayoutOptions(); _canvasPanel.Invalidate(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            if (typing)
            {
                // hand the standard editing combos to the text box instead of the menu
                if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Control | Keys.Z) ||
                    keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.J))
                    return false;
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData == Keys.F2 && _assetTree.ContainsFocus) { RenameAssetFolder(); return true; }

            switch (keyData)
            {
                case Keys.Enter:
                    if (_tool == Tool.Crop && _cropRect.HasValue) { ApplyCrop(); return true; }
                    break;
                case Keys.Delete:
                case Keys.Back:
                    if (_assetList.ContainsFocus || _assetTree.ContainsFocus) break;
                    if (_sel >= 0) { DeleteLayer(); return true; }
                    break;
                case Keys.V: SelectTool(Tool.Move); return true;
                case Keys.C: SelectTool(Tool.Crop); return true;
                case Keys.R: SelectTool(Tool.Rect); return true;
                case Keys.E: SelectTool(Tool.Ellipse); return true;
                case Keys.L: SelectTool(Tool.Line); return true;
                case Keys.A: SelectTool(Tool.Arrow); return true;
                case Keys.P: SelectTool(Tool.Pen); return true;
                case Keys.T: SelectTool(Tool.Text); return true;
                case Keys.B: SelectTool(Tool.Blur); return true;
                case Keys.Control | Keys.D0: FitView(); _canvasPanel.Invalidate(); return true;
                case Keys.Control | Keys.D1:
                    _zoom = 1f; CenterView(); _viewFitted = false; UpdateStatus(); _canvasPanel.Invalidate(); return true;
                case Keys.Oemplus:
                case Keys.Add:
                    ZoomAt(new Point(_canvasPanel.Width / 2, _canvasPanel.Height / 2), 1.15f); return true;
                case Keys.OemMinus:
                case Keys.Subtract:
                    ZoomAt(new Point(_canvasPanel.Width / 2, _canvasPanel.Height / 2), 1f / 1.15f); return true;
            }

            // nudge the selected layer with the arrows
            if (_sel >= 0)
            {
                int dx = 0, dy = 0;
                Keys bare = keyData & ~Keys.Shift;
                if (bare == Keys.Left) dx = -1;
                else if (bare == Keys.Right) dx = 1;
                else if (bare == Keys.Up) dy = -1;
                else if (bare == Keys.Down) dy = 1;
                if (dx != 0 || dy != 0)
                {
                    int step = (keyData & Keys.Shift) == Keys.Shift ? 10 : 1;
                    PushUndoCoalesced("nudge");
                    RectangleF b = _layers[_sel].Bounds;
                    b.Offset(dx * step, dy * step);
                    _layers[_sel].Bounds = b;
                    _canvasPanel.Invalidate();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ================================================================= tools etc

        void SelectTool(Tool tool)
        {
            CommitInlineEdit();
            _tool = tool;
            if (tool != Tool.Crop) _cropRect = null;
            foreach (KeyValuePair<Tool, Button> kv in _toolButtons)
            {
                bool on = kv.Key == tool;
                kv.Value.BackColor = on ? Theme.Accent : Theme.Surface;
                kv.Value.ForeColor = on ? Theme.OnAccent : Theme.Text;
            }
            UpdateStatus();
            RelayoutOptions();
            _canvasPanel.Cursor = tool == Tool.Move ? Cursors.Default : Cursors.Cross;
            _canvasPanel.Invalidate();
        }

        void ApplyCrop()
        {
            if (!_cropRect.HasValue) return;
            Rectangle r = Rectangle.Round(_cropRect.Value);
            r.Intersect(new Rectangle(0, 0, _canvas.Width, _canvas.Height));
            if (r.Width < 2 || r.Height < 2) { _cropRect = null; _canvasPanel.Invalidate(); return; }

            PushUndo();
            _canvas = r.Size;
            foreach (EditorLayer layer in _layers)
            {
                RectangleF b = layer.Bounds;
                b.Offset(-r.X, -r.Y);
                layer.Bounds = b;
            }
            _cropRect = null;
            FitView();
            SelectTool(Tool.Move);
            AfterDocumentChange();
        }

        void ResizeDocument()
        {
            CommitInlineEdit();
            if (!EnsureDoc()) return;
            int w = _canvas.Width, h = _canvas.Height;
            bool scaleLayers = true;
            if (!CanvasSizeDialog.AskResize(this, ref w, ref h, _canvas, ref scaleLayers)) return;
            if (w == _canvas.Width && h == _canvas.Height) return;

            PushUndo();
            float fx = (float)w / _canvas.Width, fy = (float)h / _canvas.Height;
            _canvas = new Size(w, h);
            if (scaleLayers)
            {
                float f = (fx + fy) / 2f;
                foreach (EditorLayer layer in _layers)
                {
                    RectangleF b = layer.Bounds;
                    layer.Bounds = new RectangleF(b.X * fx, b.Y * fy, b.Width * fx, b.Height * fy);
                    var text = layer as TextLayer;
                    if (text != null) text.FontSize = Math.Max(4f, text.FontSize * f);
                    var shape = layer as ShapeLayer;
                    if (shape != null) shape.StrokeWidth = Math.Max(1f, shape.StrokeWidth * f);
                }
            }
            FitView();
            AfterDocumentChange();
        }

        void RotateCanvas(bool clockwise)
        {
            if (!EnsureDoc()) return;
            PushUndo();
            Size old = _canvas;
            _canvas = new Size(old.Height, old.Width);
            foreach (EditorLayer layer in _layers)
            {
                PointF c = layer.Center;
                PointF nc = clockwise ? new PointF(old.Height - c.Y, c.X) : new PointF(c.Y, old.Width - c.X);
                layer.RotationDeg = Normalise(layer.RotationDeg + (clockwise ? 90 : -90));
                layer.Bounds = new RectangleF(nc.X - layer.Bounds.Width / 2f, nc.Y - layer.Bounds.Height / 2f,
                                              layer.Bounds.Width, layer.Bounds.Height);
            }
            FitView();
            AfterDocumentChange();
        }

        void FlipCanvas(bool horizontal)
        {
            if (!EnsureDoc()) return;
            PushUndo();
            foreach (EditorLayer layer in _layers)
            {
                PointF c = layer.Center;
                PointF nc = horizontal ? new PointF(_canvas.Width - c.X, c.Y) : new PointF(c.X, _canvas.Height - c.Y);
                if (horizontal) layer.FlipH = !layer.FlipH; else layer.FlipV = !layer.FlipV;
                layer.RotationDeg = Normalise(-layer.RotationDeg);
                layer.Bounds = new RectangleF(nc.X - layer.Bounds.Width / 2f, nc.Y - layer.Bounds.Height / 2f,
                                              layer.Bounds.Width, layer.Bounds.Height);
            }
            AfterDocumentChange();
        }

        void MirrorLayer(bool horizontal)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return; }
            PushUndo();
            if (horizontal) sel.FlipH = !sel.FlipH; else sel.FlipV = !sel.FlipV;
            AfterDocumentChange();
        }

        void RotateLayer90()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return; }
            PushUndo();
            sel.RotationDeg = Normalise(sel.RotationDeg + 90);
            AfterDocumentChange();
        }

        // ============================================================== layers panel

        bool _rebuildingLayerList;

        void RefreshLayerList()
        {
            _rebuildingLayerList = true;
            try
            {
                _layerList.BeginUpdate();
                _layerList.Items.Clear();
                for (int i = _layers.Count - 1; i >= 0; i--) _layerList.Items.Add(_layers[i].Name ?? "Layer");
                int display = _sel >= 0 ? _layers.Count - 1 - _sel : -1;
                if (display >= 0 && display < _layerList.Items.Count) _layerList.SelectedIndex = display;
                _layerList.EndUpdate();

                EditorLayer sel = SelectedLayer();
                _syncingOptions = true;
                _opacity.Value = sel != null ? Math.Max(0, Math.Min(100, sel.Opacity)) : 100;
                _syncingOptions = false;
                _opacityLbl.Text = "Opacity " + _opacity.Value + "%";
                _opacity.Enabled = _layerUp.Enabled = _layerDown.Enabled =
                    _layerDup.Enabled = _layerDel.Enabled = sel != null;
                if (_layersTitle != null)
                    _layersTitle.Text = _layers.Count == 0 ? "LAYERS" : "LAYERS  ·  " + _layers.Count;
            }
            finally { _rebuildingLayerList = false; }
        }

        int DisplayToLayer(int displayIndex)
        {
            return _layers.Count - 1 - displayIndex;
        }

        void LayerList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_rebuildingLayerList) return;
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

            // quiet selection: an accent wash and a slim bar, not a solid slab
            using (var bg = new SolidBrush(Theme.FieldBg)) g.FillRectangle(bg, e.Bounds);
            if (selected)
            {
                using (var wash = new SolidBrush(Color.FromArgb(Theme.Dark ? 46 : 30, Theme.Accent)))
                    g.FillRectangle(wash, e.Bounds);
                using (var bar = new SolidBrush(Theme.Accent))
                    g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
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

            // a little preview of what the layer holds
            Rectangle thumb = new Rectangle(e.Bounds.X + 30, e.Bounds.Y + 4, 28, 28);
            DrawLayerThumb(g, layer, thumb);

            string label = layer.Name ?? "Layer";
            using (var b = new SolidBrush(fore))
                g.DrawString(label, Theme.Base, b, e.Bounds.X + 64, e.Bounds.Y + 9);
            if (layer.Opacity < 100)
            {
                string pct = layer.Opacity + "%";
                SizeF w = g.MeasureString(pct, Theme.Small);
                using (var b = new SolidBrush(Theme.TextDim))
                    g.DrawString(pct, Theme.Small, b, e.Bounds.Right - w.Width - 6, e.Bounds.Y + 11);
            }
        }

        /// <summary>A 28px preview: rasters show their pixels, text a T in its colour, shapes themselves.</summary>
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
                            case ShapeKind.Ellipse: g.DrawEllipse(p, inner); break;
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
                                    new Point(inner.Left, inner.Bottom),
                                    new Point(inner.Left + inner.Width / 3, inner.Top + 2),
                                    new Point(inner.Right - inner.Width / 3, inner.Bottom - 2),
                                    new Point(inner.Right, inner.Top)
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
            if (index < 0) return;
            if (e.X <= 26)
            {
                int li = DisplayToLayer(index);
                if (li >= 0 && li < _layers.Count)
                {
                    PushUndoCoalesced("visibility");
                    _layers[li].Visible = !_layers[li].Visible;
                    _layerList.Invalidate();
                    _canvasPanel.Invalidate();
                }
            }
        }

        void LayerList_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.X <= 26) return;
            int index = _layerList.IndexFromPoint(e.Location);
            if (index < 0) return;
            _layerList.SelectedIndex = index;
            RenameLayer();
        }

        void RenameLayer()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            int display = _layers.Count - 1 - _sel;
            Rectangle item = _layerList.GetItemRectangle(display);
            _renameBox.SetBounds(_layerList.Left + 28, _layerList.Top + item.Y + 3, _layerList.Width - 34, 24);
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
            PushUndo();
            sel.Name = name;
            RefreshLayerList();
        }

        void Opacity_ValueChanged(object sender, EventArgs e)
        {
            if (_syncingOptions) return;
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            PushUndoCoalesced("opacity");
            sel.Opacity = _opacity.Value;
            _opacityLbl.Text = "Opacity " + _opacity.Value + "%";
            _layerList.Invalidate();
            _canvasPanel.Invalidate();
        }

        void MoveLayer(int direction)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            int target = _sel + direction;
            if (target < 0 || target >= _layers.Count) return;
            PushUndo();
            _layers[_sel] = _layers[target];
            _layers[target] = sel;
            _sel = target;
            AfterDocumentChange();
        }

        void DuplicateLayer()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            PushUndo();
            EditorLayer copy = sel.Clone();
            copy.Name = sel.Name + " copy";
            RectangleF b = copy.Bounds;
            b.Offset(16, 16);
            copy.Bounds = b;
            _layers.Insert(_sel + 1, copy);
            _sel = _sel + 1;
            AfterDocumentChange();
        }

        void DeleteLayer()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) return;
            PushUndo();
            _layers.RemoveAt(_sel);
            if (_sel >= _layers.Count) _sel = _layers.Count - 1;
            AfterDocumentChange();
        }

        // ============================================================== options sync

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
                    _optStroke.Color = _stroke;
                    _optFill.Color = _fill;
                    _optWidth.Value = Math.Max(_optWidth.Minimum, Math.Min(_optWidth.Maximum, (decimal)_strokeW));
                }
                var text = sel as TextLayer;
                if (text != null)
                {
                    _fontFamily = text.FontFamily;
                    _fontSize = text.FontSize;
                    _bold = text.Bold;
                    _italic = text.Italic;
                    _underline = text.Underline;
                    _textColor = text.Color;
                    _textBack = text.BackColor;
                    _textOutline = text.OutlineColor;
                    _optFont.SelectedItem = _fontFamily;
                    _optSize.Value = Math.Max(_optSize.Minimum, Math.Min(_optSize.Maximum, (decimal)_fontSize));
                    StyleToggle(_optBold, _bold);
                    StyleToggle(_optItalic, _italic);
                    StyleToggle(_optUnderline, _underline);
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
                PushUndoCoalesced("shapeopts");
                shape.Stroke = _stroke;
                shape.Fill = _fill;
                shape.StrokeWidth = _strokeW;
                _canvasPanel.Invalidate();
            }
        }

        void ApplyTextOptions()
        {
            var text = SelectedLayer() as TextLayer;
            if (text != null && (_tool == Tool.Move || _tool == Tool.Text))
            {
                PushUndoCoalesced("textopts");
                text.FontFamily = _fontFamily;
                text.FontSize = _fontSize;
                text.Bold = _bold;
                text.Italic = _italic;
                text.Underline = _underline;
                text.Color = _textColor;
                text.BackColor = _textBack;
                text.OutlineColor = _textOutline;
                GrowTextBounds(text);
                if (_editing == text && _inlineEdit.Visible)
                {
                    try { _inlineEdit.Font = new Font(_fontFamily, Math.Max(4f, _fontSize * _zoom), text.Style & ~FontStyle.Underline, GraphicsUnit.Pixel); }
                    catch { }
                    _inlineEdit.ForeColor = _textColor.A > 60 ? Color.FromArgb(255, _textColor) : Theme.Text;
                }
                _canvasPanel.Invalidate();
            }
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
                var item = new ListViewItem(Path.GetFileNameWithoutExtension(file), _assetThumbs.Images.Count - 1)
                {
                    Tag = file
                };
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

        /// <summary>Renders just the selected layer and stores it in the library as a PNG.</summary>
        void SaveLayerAsAsset()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return; }
            string name = EditorPrompt.Ask(this, "Save layer as asset", "Asset name", sel.Name);
            if (string.IsNullOrEmpty(name)) return;

            PointF[] corners = sel.CanvasCorners();
            float minX = corners.Min(p => p.X), minY = corners.Min(p => p.Y);
            float maxX = corners.Max(p => p.X), maxY = corners.Max(p => p.Y);
            int w = Math.Max(1, (int)Math.Ceiling(maxX - minX));
            int h = Math.Max(1, (int)Math.Ceiling(maxY - minY));
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    EditorRender.Prepare(g);
                    g.TranslateTransform(-minX, -minY);
                    bool wasVisible = sel.Visible;
                    int wasOpacity = sel.Opacity;
                    sel.Visible = true;
                    sel.Opacity = 100;
                    sel.Draw(g);
                    sel.Visible = wasVisible;
                    sel.Opacity = wasOpacity;
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
    }

    /// <summary>The drawing surface: double-buffered and focusable so it can take the keys.</summary>
    class CanvasPanel : Panel
    {
        public CanvasPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
        }
    }

    /// <summary>
    /// A colour well for the options bar. Left-click opens the picker; right-click (when
    /// clearing is allowed) sets "none", drawn as a checkerboard with a red slash.
    /// </summary>
    class SwatchButton : Button
    {
        readonly bool _allowClear;
        Color _color = Color.Red;

        public event EventHandler ColorChanged;

        public SwatchButton(bool allowClear)
        {
            _allowClear = allowClear;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderColor = Theme.Border;
            BackColor = Theme.Surface;
            TabStop = false;
        }

        public Color Color
        {
            get { return _color; }
            set { _color = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle r = new Rectangle(5, 5, Width - 11, Height - 11);
            Graphics g = e.Graphics;
            if (_color.A < 255)
            {
                using (var light = new SolidBrush(System.Drawing.Color.White))
                using (var dark = new SolidBrush(System.Drawing.Color.FromArgb(200, 200, 200)))
                {
                    g.FillRectangle(light, r);
                    for (int y = 0; y < r.Height; y += 5)
                        for (int x = y / 5 % 2 == 0 ? 0 : 5; x < r.Width; x += 10)
                            g.FillRectangle(dark, r.X + x, r.Y + y, Math.Min(5, r.Width - x), Math.Min(5, r.Height - y));
                }
            }
            if (_color.A > 0)
                using (var b = new SolidBrush(_color)) g.FillRectangle(b, r);
            else
                using (var p = new Pen(System.Drawing.Color.Red, 1.6f)) g.DrawLine(p, r.Left, r.Bottom, r.Right, r.Top);
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, r);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right && _allowClear)
            {
                Color = System.Drawing.Color.Transparent;
                if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            using (var dialog = new ColorDialog { Color = _color.A > 0 ? _color : System.Drawing.Color.White, FullOpen = true })
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    Color = dialog.Color;
                    if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
                }
            }
        }
    }

    /// <summary>Width/height dialog for File > New and Image > Resize, in the app's style.</summary>
    class CanvasSizeDialog : PixelPerfectForm
    {
        readonly NumericUpDown _w, _h;
        readonly ModernCheckBox _scale;
        readonly ModernRadioButton _white, _transparent;
        readonly Size _original;
        readonly ModernCheckBox _lockRatio;
        bool _linking;

        CanvasSizeDialog(string title, int w, int h, bool askBackground, bool white, bool askScale, Size original)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            _original = original;

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(320, askBackground || askScale ? 208 : 156);
            BackColor = Theme.Bg;
            Font = Theme.Base;

            var wl = new Label { Text = "Width", Location = new Point(24, 24), AutoSize = true, ForeColor = Theme.TextDim };
            _w = new NumericUpDown
            {
                Minimum = 8, Maximum = 20000, Value = Math.Max(8, Math.Min(20000, w)),
                Location = new Point(24, 44), Width = 120,
                BackColor = Theme.FieldBg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle
            };
            var hl = new Label { Text = "Height", Location = new Point(172, 24), AutoSize = true, ForeColor = Theme.TextDim };
            _h = new NumericUpDown
            {
                Minimum = 8, Maximum = 20000, Value = Math.Max(8, Math.Min(20000, h)),
                Location = new Point(172, 44), Width = 120,
                BackColor = Theme.FieldBg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(wl); Controls.Add(_w); Controls.Add(hl); Controls.Add(_h);

            int y = 78;
            if (askScale)
            {
                _lockRatio = new ModernCheckBox
                {
                    Text = "Keep the current proportions", Location = new Point(24, y),
                    AutoSize = true, Checked = true
                };
                Controls.Add(_lockRatio);
                y += 28;
                _scale = new ModernCheckBox
                {
                    Text = "Scale the layers with the canvas", Location = new Point(24, y),
                    AutoSize = true, Checked = true
                };
                Controls.Add(_scale);
                y += 30;
                _w.ValueChanged += LinkedW;
                _h.ValueChanged += LinkedH;
            }
            if (askBackground)
            {
                _white = new ModernRadioButton { Text = "White background", Location = new Point(24, y), AutoSize = true, Checked = white };
                _transparent = new ModernRadioButton { Text = "Transparent", Location = new Point(180, y), AutoSize = true, Checked = !white };
                Controls.Add(_white); Controls.Add(_transparent);
                y += 30;
            }

            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(96, 32), DialogResult = DialogResult.OK };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(96, 32), DialogResult = DialogResult.Cancel };
            ok.Location = new Point(ClientSize.Width - 96 - 24, ClientSize.Height - 44);
            cancel.Location = new Point(ok.Left - 96 - 8, ClientSize.Height - 44);
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }

        void LinkedW(object sender, EventArgs e)
        {
            if (_linking || _lockRatio == null || !_lockRatio.Checked || _original.Width < 1) return;
            _linking = true;
            decimal v = Math.Round(_w.Value * _original.Height / _original.Width);
            _h.Value = Math.Max(_h.Minimum, Math.Min(_h.Maximum, v));
            _linking = false;
        }

        void LinkedH(object sender, EventArgs e)
        {
            if (_linking || _lockRatio == null || !_lockRatio.Checked || _original.Height < 1) return;
            _linking = true;
            decimal v = Math.Round(_h.Value * _original.Width / _original.Height);
            _w.Value = Math.Max(_w.Minimum, Math.Min(_w.Maximum, v));
            _linking = false;
        }

        public static bool Ask(IWin32Window owner, string title, ref int w, ref int h, bool askBackground, ref bool white)
        {
            using (var dialog = new CanvasSizeDialog(title, w, h, askBackground, white, false, Size.Empty))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                w = (int)dialog._w.Value;
                h = (int)dialog._h.Value;
                if (dialog._white != null) white = dialog._white.Checked;
                return true;
            }
        }

        public static bool AskResize(IWin32Window owner, ref int w, ref int h, Size original, ref bool scaleLayers)
        {
            using (var dialog = new CanvasSizeDialog("Resize image", w, h, false, true, true, original))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                w = (int)dialog._w.Value;
                h = (int)dialog._h.Value;
                scaleLayers = dialog._scale.Checked;
                return true;
            }
        }
    }

    /// <summary>A one-line themed prompt (layer rename, asset category names).</summary>
    class EditorPrompt : PixelPerfectForm
    {
        readonly TextBox _box;

        EditorPrompt(string title, string label, string initial)
        {
            Theme.Init(ThemeHelper.IsDarkMode);

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 130);
            BackColor = Theme.Bg;
            Font = Theme.Base;

            var caption = new Label { Text = label, Location = new Point(24, 18), AutoSize = true, ForeColor = Theme.TextDim };
            _box = new TextBox
            {
                Location = new Point(24, 40), Width = 292,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.FieldBg, ForeColor = Theme.Text, Text = initial
            };
            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(96, 32), DialogResult = DialogResult.OK, Location = new Point(220, 82) };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(96, 32), DialogResult = DialogResult.Cancel, Location = new Point(116, 82) };
            Controls.Add(caption); Controls.Add(_box); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }

        public static string Ask(IWin32Window owner, string title, string label, string initial)
        {
            using (var dialog = new EditorPrompt(title, label, initial))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                return dialog._box.Text.Trim();
            }
        }
    }
}
