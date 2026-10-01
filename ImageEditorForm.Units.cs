using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Ruler units, the Photoshop way: right-click a ruler for Pixels, Inches, Centimeters,
    /// Millimeters, Points, Picas, Percent and more. The rulers and the guide read-out then
    /// count in that unit (physical units go through the document's resolution), and
    /// Snap to Unit makes guides land on the ruler's marks like a grid.
    /// </summary>
    partial class ImageEditorForm
    {
        enum RulerUnit { Pixels, Inches, Centimeters, Millimeters, Points, Picas, Feet, Meters, Percent }

        static readonly string[] UnitNames = { "Pixels", "Inches", "Centimeters", "Millimeters", "Points", "Picas", "Feet", "Meters", "Percent" };
        static readonly string[] UnitSuffix = { "px", "in", "cm", "mm", "pt", "pc", "ft", "m", "%" };

        RulerUnit _rulerUnit = LoadRulerUnit();
        bool _snapToUnit = LoadSnapToUnit();
        ToolStripMenuItem _rulerUnitsItem;

        static RulerUnit LoadRulerUnit()
        {
            try
            {
                RulerUnit u;
                if (Enum.TryParse(Properties.Settings.Default.EditorRulerUnit, out u) && Enum.IsDefined(typeof(RulerUnit), u)) return u;
            }
            catch (Exception) { }
            return RulerUnit.Pixels;
        }

        static bool LoadSnapToUnit()
        {
            try { return Properties.Settings.Default.EditorSnapToUnit; } catch (Exception) { return false; }
        }

        void SaveUnitSettings()
        {
            try
            {
                Properties.Settings.Default.EditorRulerUnit = _rulerUnit.ToString();
                Properties.Settings.Default.EditorSnapToUnit = _snapToUnit;
                Properties.Settings.Default.Save();
            }
            catch (Exception) { }
        }

        /// <summary>Canvas pixels in one unit along an axis (Percent depends on the axis).</summary>
        float PixelsPerUnit(bool horizontal)
        {
            float ppi = _ppi > 0 ? _ppi : 72;
            switch (_rulerUnit)
            {
                case RulerUnit.Inches: return ppi;
                case RulerUnit.Centimeters: return ppi / 2.54f;
                case RulerUnit.Millimeters: return ppi / 25.4f;
                case RulerUnit.Points: return ppi / 72f;
                case RulerUnit.Picas: return ppi / 6f;
                case RulerUnit.Feet: return ppi * 12f;
                case RulerUnit.Meters: return ppi / 0.0254f;
                case RulerUnit.Percent:
                    float side = horizontal ? _canvas.Width : _canvas.Height;
                    return Math.Max(1f, side) / 100f;
                default: return 1f;
            }
        }

        /// <summary>
        /// The labelled step of a ruler in units (1, 2 or 5 times a power of ten, at least 60
        /// screen pixels apart) and how many small marks divide it.
        /// </summary>
        void RulerStep(bool horizontal, out double step, out int minors)
        {
            double ppu = PixelsPerUnit(horizontal) * _zoom;      // screen px per unit
            step = 1;
            minors = 5;
            for (int exp = -4; exp <= 7; exp++)
            {
                double pow = Math.Pow(10, exp);
                foreach (int lead in new[] { 1, 2, 5 })
                {
                    double s = lead * pow;
                    if (_rulerUnit == RulerUnit.Pixels && s < 1) continue;   // no fractional pixels
                    if (s * ppu >= 60)
                    {
                        step = s;
                        minors = lead == 2 ? 4 : 5;
                        // inches split into halves, quarters and eighths, like a tape measure
                        if (_rulerUnit == RulerUnit.Inches && lead == 1 && exp == 0) minors = s * ppu >= 120 ? 8 : 4;
                        if (_rulerUnit == RulerUnit.Inches && lead == 5 && exp == -1) minors = 4;   // eighths
                        return;
                    }
                }
            }
            step = 1e7;
        }

        /// <summary>The spacing of the ruler's small marks, in canvas pixels -- what Snap to Unit locks onto.</summary>
        float UnitSnapSpacing(bool horizontal)
        {
            double step; int minors;
            RulerStep(horizontal, out step, out minors);
            return (float)(step / minors * PixelsPerUnit(horizontal));
        }

        /// <summary>A canvas position in the current unit, e.g. "2.54 cm".</summary>
        string FormatUnit(float px, bool horizontal)
        {
            double v = (px - (horizontal ? _rulerZero.X : _rulerZero.Y)) / PixelsPerUnit(horizontal);
            string fmt = _rulerUnit == RulerUnit.Pixels ? "0" : _rulerUnit == RulerUnit.Percent ? "0.#" : "0.###";
            return v.ToString(fmt, CultureInfo.CurrentCulture) + " " + UnitSuffix[(int)_rulerUnit];
        }

        static string RulerLabel(double v)
        {
            if (Math.Abs(v) < 1e-9) v = 0;
            return v.ToString("0.####", CultureInfo.CurrentCulture);
        }

        bool OnRulerArea(Point screen)
        {
            return _showRulers && (screen.X < RulerSize || screen.Y < RulerSize);
        }

        /// <summary>Right-click on a ruler: the unit list and Snap to Unit.</summary>
        void ShowRulerMenu(Point screen)
        {
            ContextMenuStrip m = NewContextMenu();
            AddUnitItems(m.Items);
            m.Show(_canvasPanel, screen);
        }

        /// <summary>The units (one checked) and Snap to Unit -- shared by the ruler menu and View > Ruler Units.</summary>
        void AddUnitItems(ToolStripItemCollection items)
        {
            for (int i = 0; i < UnitNames.Length; i++)
            {
                RulerUnit u = (RulerUnit)i;
                items.Add(new ToolStripMenuItem(UnitNames[i], null, delegate { SetRulerUnit(u); }) { Checked = _rulerUnit == u });
            }
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Snap to Unit", null, delegate { ToggleSnapToUnit(); })
            { Checked = _snapToUnit, ToolTipText = "Guides land on the ruler's marks, like a grid" });
            items.Add(new ToolStripMenuItem("Reset Ruler Origin (0, 0)", null, delegate { ResetRulerOrigin(); })
            { Enabled = _rulerZero != PointF.Empty, ToolTipText = "Put 0,0 back on the image's top-left corner (or double-click the ruler corner)" });
            string res = string.Format(CultureInfo.CurrentCulture, "Resolution: {0:0.##} ppi", _ppi);
            items.Add(new ToolStripMenuItem(res) { Enabled = false });
        }

        void SetRulerUnit(RulerUnit u)
        {
            _rulerUnit = u;
            SaveUnitSettings();
            _canvasPanel.Invalidate();
            Toast.Show("Ruler units: " + UnitNames[(int)u]);
        }

        void ToggleSnapToUnit()
        {
            _snapToUnit = !_snapToUnit;
            SaveUnitSettings();
            Toast.Show(_snapToUnit ? "Snap to unit: on" : "Snap to unit: off");
        }

        /// <summary>View > Ruler Units: rebuilt each time it opens so the checks are current.</summary>
        ToolStripMenuItem BuildRulerUnitsMenu()
        {
            _rulerUnitsItem = new ToolStripMenuItem("Ruler Units");
            _rulerUnitsItem.DropDownItems.Add(new ToolStripMenuItem("-"));   // placeholder so the arrow shows
            _rulerUnitsItem.DropDownOpening += delegate
            {
                _rulerUnitsItem.DropDownItems.Clear();
                AddUnitItems(_rulerUnitsItem.DropDownItems);
            };
            return _rulerUnitsItem;
        }
    
        // ------------------------------------------------------------ ruler origin (zero point)

        /// <summary>Where the rulers count from, in canvas pixels; (0, 0) is the image's top-left corner.</summary>
        PointF _rulerZero;
        PointF _originDragAt;     // where the zero point would land, while it is being dragged

        bool OnRulerCorner(Point screen)
        {
            return _showRulers && screen.X < RulerSize && screen.Y < RulerSize;
        }

        /// <summary>A canvas position pulled onto the nearest small ruler mark, counted from the origin.</summary>
        float SnapToUnitMarks(float v, bool horizontal)
        {
            float tick = UnitSnapSpacing(horizontal);
            if (tick <= 0) return (float)Math.Round(v);
            float zero = horizontal ? _rulerZero.X : _rulerZero.Y;
            return zero + (float)Math.Round((v - zero) / tick) * tick;
        }

        /// <summary>Press in the ruler corner: start dragging a new zero point, as in Photoshop.</summary>
        bool OriginMouseDown(Point screen, PointF cp)
        {
            if (!OnRulerCorner(screen) || !_hasDoc) return false;
            _drag = Drag.RulerOrigin;
            _originDragAt = _rulerZero;
            _canvasPanel.Cursor = Cursors.Cross;
            _canvasPanel.Invalidate();
            return true;
        }

        void OriginMouseMove(PointF cp)
        {
            // whole pixels; Snap to Unit (or Shift) puts it on a ruler mark; the image's
            // edges, centre and guides pull it in when they are close, like a guide drag
            float x = (float)Math.Round(cp.X), y = (float)Math.Round(cp.Y);
            if (_snapToUnit || (ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                x = SnapToUnitMarks(cp.X, true);
                y = SnapToUnitMarks(cp.Y, false);
            }
            float limit = SnapDistance / _zoom;
            foreach (float c in new[] { 0f, _canvas.Width / 2f, (float)_canvas.Width })
                if (Math.Abs(cp.X - c) <= limit) x = c;
            foreach (float c in new[] { 0f, _canvas.Height / 2f, (float)_canvas.Height })
                if (Math.Abs(cp.Y - c) <= limit) y = c;
            PointF p = new PointF(x, y);
            if (SnapActive) p = SnapPoint(p);
            _originDragAt = p;
            _statusRight.Text = string.Format("Ruler origin: {0:0} , {1:0} px from the image's top-left", p.X, p.Y);
            _canvasPanel.Invalidate();
        }

        /// <summary>Release: inside the view sets the new zero point; back over the rulers changes nothing.</summary>
        void OriginMouseUp(Point screen)
        {
            if (ViewArea().Contains(screen))
            {
                _rulerZero = _originDragAt;
                Toast.Show(_rulerZero == PointF.Empty ? "Ruler origin: image corner (0, 0)"
                    : string.Format("Ruler origin moved to {0:0}, {1:0} px. Double-click the ruler corner to reset.", _rulerZero.X, _rulerZero.Y));
            }
            _statusRight.Text = ToolHint(_tool);
            _canvasPanel.Invalidate();
        }

        void CancelOriginDrag()
        {
            _drag = Drag.None;
            _statusRight.Text = ToolHint(_tool);
            _canvasPanel.Invalidate();
        }

        void ResetRulerOrigin()
        {
            _rulerZero = PointF.Empty;
            _canvasPanel.Invalidate();
            Toast.Show("Ruler origin: image corner (0, 0)");
        }

        /// <summary>While the zero point is dragged: dotted cross-hairs right across the view.</summary>
        void PaintOriginDrag(Graphics g)
        {
            if (_drag != Drag.RulerOrigin) return;
            Rectangle area = ViewArea();
            float sx = (float)Math.Round(_origin.X + _originDragAt.X * _zoom) + 0.5f;
            float sy = (float)Math.Round(_origin.Y + _originDragAt.Y * _zoom) + 0.5f;
            GraphicsState st = g.Save();
            g.SetClip(area);
            g.SmoothingMode = SmoothingMode.None;
            using (var light = new Pen(Color.FromArgb(220, 255, 255, 255)))
            using (var dark = new Pen(Color.FromArgb(230, 20, 20, 24)) { DashPattern = new[] { 3f, 3f } })
            {
                g.DrawLine(light, sx, area.Top, sx, area.Bottom);
                g.DrawLine(light, area.Left, sy, area.Right, sy);
                g.DrawLine(dark, sx, area.Top, sx, area.Bottom);
                g.DrawLine(dark, area.Left, sy, area.Right, sy);
            }
            g.Restore(st);
        }

        /// <summary>The corner box: Photoshop's dotted cross-hair, in the accent colour once the origin is moved.</summary>
        void PaintRulerCorner(Graphics g)
        {
            Color ink = _rulerZero != PointF.Empty || _drag == Drag.RulerOrigin ? Theme.Accent : Theme.TextDim;
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(ink))
            using (var dots = new Pen(ink) { DashPattern = new[] { 1f, 1f } })
            {
                int c = RulerSize / 2;
                g.DrawLine(dots, 4, c, RulerSize - 4, c);
                g.DrawLine(dots, c, 4, c, RulerSize - 4);
                g.DrawRectangle(pen, c - 3, c - 3, 6, 6);
            }
            using (var p = new Pen(Theme.TextDim))
            {
                g.DrawLine(p, RulerSize - 1, 0, RulerSize - 1, RulerSize - 1);
                g.DrawLine(p, 0, RulerSize - 1, RulerSize - 1, RulerSize - 1);
            }
            g.Restore(st);
        }
    }
}
