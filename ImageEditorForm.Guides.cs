using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Guides, the Photoshop way: drag out of the top ruler for a horizontal guide or the left
    /// ruler for a vertical one; drag a guide with the Move tool (or any tool with Ctrl) to move
    /// it, and back onto a ruler to delete it. Right-click a guide to lock it, delete it or
    /// change its colour. While moving layers or selections, or drawing marquees, shapes and
    /// crops, edges and centres snap to guides within a few screen pixels. Guides belong to
    /// the document (each tab has its own) and sit outside the undo history, as in Photoshop.
    /// </summary>
    partial class ImageEditorForm
    {
        /// <summary>One guide: a line across the canvas at a canvas coordinate.</summary>
        class Guide
        {
            public bool Vertical;      // vertical: at x = Pos; horizontal: at y = Pos
            public float Pos;
            public bool Locked;
            public Color Color = DefaultGuideColor;
        }

        static readonly Color DefaultGuideColor = Color.FromArgb(0, 200, 255);   // Photoshop's cyan
        const int GuideHit = 4;       // screen px either side of a guide that grabs it
        const int SnapDistance = 8;   // screen px within which things snap to a guide

        List<Guide> _guides = new List<Guide>();
        bool _showGuides = true;
        bool _snapToGuides = true;
        Guide _guideDrag;             // the guide being dragged
        float _guideFrom;             // its position before the drag (restored by Esc)
        bool _guideNew;               // it came out of a ruler just now
        ToolStripMenuItem _showGuidesItem, _snapGuidesItem, _lockGuidesItem;

        // ------------------------------------------------------------ geometry

        float GuideScreen(Guide g)
        {
            return g.Vertical ? _origin.X + g.Pos * _zoom : _origin.Y + g.Pos * _zoom;
        }

        /// <summary>The guide under a screen point (nearest within a few pixels), or null.</summary>
        Guide HitGuide(Point screen)
        {
            if (!_showGuides || _guides.Count == 0) return null;
            if (_showRulers && (screen.X < RulerSize || screen.Y < RulerSize)) return null;
            Guide best = null;
            float bestD = GuideHit + 0.5f;
            foreach (Guide g in _guides)
            {
                float d = Math.Abs((g.Vertical ? screen.X : screen.Y) - GuideScreen(g));
                if (d < bestD) { bestD = d; best = g; }
            }
            return best;
        }

        /// <summary>0: not on a ruler; 1: the top ruler (horizontal guides); 2: the left one (vertical).</summary>
        int RulerAt(Point screen)
        {
            if (!_showRulers) return 0;
            if (screen.Y < RulerSize && screen.X >= RulerSize) return 1;
            if (screen.X < RulerSize && screen.Y >= RulerSize) return 2;
            return 0;
        }

        // ------------------------------------------------------------ painting

        void PaintGuides(Graphics g)
        {
            if (!_showGuides || _guides.Count == 0) return;
            Rectangle area = ViewArea();
            GraphicsState st = g.Save();
            g.SetClip(area);
            g.SmoothingMode = SmoothingMode.None;
            foreach (Guide gd in _guides)
            {
                float s = (float)Math.Round(GuideScreen(gd)) + 0.5f;
                using (var p = new Pen(gd == _guideDrag ? Color.FromArgb(255, gd.Color) : Color.FromArgb(220, gd.Color), 1f))
                {
                    if (gd.Locked) p.DashPattern = new[] { 6f, 3f };   // locked guides read as dashed
                    if (gd.Vertical) g.DrawLine(p, s, area.Top, s, area.Bottom);
                    else g.DrawLine(p, area.Left, s, area.Right, s);
                }
            }
            g.Restore(st);

            // while dragging, the position in the ruler's units next to the pointer
            if (_guideDrag != null && _drag == Drag.Guide)
            {
                string text = (_guideDrag.Vertical ? "X: " : "Y: ") + Math.Round(_guideDrag.Pos) + " px";
                Size sz = TextRenderer.MeasureText(text, Theme.Small);
                var box = new Rectangle(_mouseScreen.X + 14, _mouseScreen.Y + 14, sz.Width + 8, sz.Height + 4);
                using (var bg = new SolidBrush(Color.FromArgb(230, 30, 30, 34))) g.FillRectangle(bg, box);
                TextRenderer.DrawText(g, text, Theme.Small, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // ------------------------------------------------------------ mouse

        /// <summary>
        /// Mouse down on a ruler or a guide: starts a guide drag and returns true. Left on a
        /// ruler makes a new guide; left on an unlocked guide moves it (Move tool, or Ctrl with
        /// any other tool, as in Photoshop).
        /// </summary>
        bool GuideMouseDown(MouseEventArgs e, PointF cp)
        {
            if (e.Button != MouseButtons.Left) return false;
            int ruler = RulerAt(e.Location);
            if (ruler != 0)
            {
                _guideDrag = new Guide { Vertical = ruler == 2, Pos = ruler == 2 ? cp.X : cp.Y };
                _guides.Add(_guideDrag);
                _guideNew = true;
                _showGuides = true;
                SyncGuideItems();
                _drag = Drag.Guide;
                _canvasPanel.Cursor = _guideDrag.Vertical ? Cursors.VSplit : Cursors.HSplit;
                _canvasPanel.Invalidate();
                return true;
            }
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            if (_tool != Tool.Move && !ctrl) return false;
            Guide hit = HitGuide(e.Location);
            if (hit == null || hit.Locked) return false;
            _guideDrag = hit;
            _guideFrom = hit.Pos;
            _guideNew = false;
            _drag = Drag.Guide;
            _canvasPanel.Cursor = hit.Vertical ? Cursors.VSplit : Cursors.HSplit;
            return true;
        }

        void GuideMouseMove(PointF cp)
        {
            if (_guideDrag == null) return;
            float v = _guideDrag.Vertical ? cp.X : cp.Y;
            // whole pixels, like Photoshop's guides at 100 %; Shift snaps to the ruler's ticks
            v = (float)Math.Round(v);
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                int tick = RulerTick() / 5;
                if (tick > 0) v = (float)Math.Round(v / tick) * tick;
            }
            _guideDrag.Pos = v;
            _canvasPanel.Invalidate();
        }

        /// <summary>Drop: a guide released over a ruler (or outside the view) is deleted.</summary>
        void GuideMouseUp(Point screen)
        {
            Guide g = _guideDrag;
            _guideDrag = null;
            if (g == null) return;
            Rectangle area = ViewArea();
            bool gone = g.Vertical ? screen.X < area.Left || screen.X > area.Right
                                   : screen.Y < area.Top || screen.Y > area.Bottom;
            if (gone) _guides.Remove(g);
            _canvasPanel.Invalidate();
            _statusRight.Text = gone ? (_guideNew ? "" : "Guide deleted") : ToolHint(_tool);
        }

        void CancelGuideDrag()
        {
            if (_guideDrag == null) return;
            if (_guideNew) _guides.Remove(_guideDrag); else _guideDrag.Pos = _guideFrom;
            _guideDrag = null;
            _drag = Drag.None;
            _canvasPanel.Invalidate();
        }

        /// <summary>Hover feedback: the split cursor over rulers and grabbable guides. True when it applied.</summary>
        bool GuideCursor(Point screen)
        {
            int ruler = RulerAt(screen);
            if (ruler != 0) { _canvasPanel.Cursor = ruler == 2 ? Cursors.VSplit : Cursors.HSplit; return true; }
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            if (_tool != Tool.Move && !ctrl) return false;
            Guide hit = HitGuide(screen);
            if (hit == null || hit.Locked) return false;
            _canvasPanel.Cursor = hit.Vertical ? Cursors.VSplit : Cursors.HSplit;
            return true;
        }

        /// <summary>The ruler's labelled tick spacing in canvas pixels at the current zoom.</summary>
        int RulerTick()
        {
            int[] steps = { 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };
            foreach (int s in steps) if (s * _zoom >= 60) return s;
            return steps[steps.Length - 1];
        }

        // ------------------------------------------------------------ menu

        /// <summary>Right-click on a guide: its own menu instead of the canvas one. True when it was on a guide.</summary>
        bool ShowGuideMenu(Point screen)
        {
            Guide g = HitGuide(screen);
            if (g == null) return false;
            ContextMenuStrip m = NewContextMenu();
            m.Items.Add(new ToolStripMenuItem(g.Locked ? "Unlock Guide" : "Lock Guide", null, delegate
            {
                g.Locked = !g.Locked;
                _canvasPanel.Invalidate();
            }));
            m.Items.Add(new ToolStripMenuItem("Delete Guide", null, delegate
            {
                _guides.Remove(g);
                _canvasPanel.Invalidate();
            }));
            m.Items.Add(new ToolStripMenuItem("Guide Colour…", null, delegate
            {
                using (var dialog = new ColorDialog { Color = g.Color, FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { g.Color = dialog.Color; _canvasPanel.Invalidate(); }
            }));
            m.Items.Add(new ToolStripMenuItem("Use This Colour for All Guides", null, delegate
            {
                foreach (Guide x in _guides) x.Color = g.Color;
                _canvasPanel.Invalidate();
            }) { Enabled = _guides.Count > 1 });
            m.Items.Add(new ToolStripSeparator());
            AddGuideCommands(m.Items);
            m.Show(_canvasPanel, screen);
            return true;
        }

        /// <summary>The guide commands shared by the View menu and a guide's own menu.</summary>
        void AddGuideCommands(ToolStripItemCollection items)
        {
            bool allLocked = _guides.Count > 0 && _guides.TrueForAll(x => x.Locked);
            items.Add(new ToolStripMenuItem(allLocked ? "Unlock All Guides" : "Lock All Guides", null, delegate { ToggleLockGuides(); })
                { ShortcutKeyDisplayString = "Alt+Ctrl+;", Enabled = _guides.Count > 0 });
            items.Add(new ToolStripMenuItem("Snap to Guides", null, delegate { ToggleSnapGuides(); })
                { ShortcutKeyDisplayString = "Shift+Ctrl+;", Checked = _snapToGuides });
            items.Add(new ToolStripMenuItem("Clear All Guides", null, delegate { ClearGuides(); }) { Enabled = _guides.Count > 0 });
        }

        void ToggleShowGuides()
        {
            _showGuides = !_showGuides;
            SyncGuideItems();
            _canvasPanel.Invalidate();
        }

        void ToggleLockGuides()
        {
            if (_guides.Count == 0) return;
            bool lockAll = !_guides.TrueForAll(x => x.Locked);
            foreach (Guide g in _guides) g.Locked = lockAll;
            SyncGuideItems();
            Toast.Show(lockAll ? "Guides locked." : "Guides unlocked.");
            _canvasPanel.Invalidate();
        }

        void ToggleSnapGuides()
        {
            _snapToGuides = !_snapToGuides;
            SyncGuideItems();
            Toast.Show(_snapToGuides ? "Snap to guides: on" : "Snap to guides: off");
        }

        void ClearGuides()
        {
            if (_guides.Count == 0) return;
            _guides.Clear();
            SyncGuideItems();
            _canvasPanel.Invalidate();
        }

        void SyncGuideItems()
        {
            if (_showGuidesItem != null) _showGuidesItem.Checked = _showGuides;
            if (_snapGuidesItem != null) _snapGuidesItem.Checked = _snapToGuides;
            if (_lockGuidesItem != null) _lockGuidesItem.Checked = _guides.Count > 0 && _guides.TrueForAll(x => x.Locked);
            if (_toolRail != null) _toolRail.Invalidate();
        }

        // ------------------------------------------------------------ snapping

        bool SnapActive { get { return _snapToGuides && _showGuides && _guides.Count > 0; } }

        /// <summary>
        /// How far to shift so that one of <paramref name="candidates"/> (canvas coordinates on one
        /// axis) lands on the nearest guide of that orientation within the snap distance; 0 if none.
        /// </summary>
        float SnapShift(bool vertical, params float[] candidates)
        {
            if (!SnapActive) return 0;
            float limit = SnapDistance / _zoom;
            float best = 0, bestAbs = float.MaxValue;
            foreach (Guide g in _guides)
            {
                if (g.Vertical != vertical) continue;
                foreach (float c in candidates)
                {
                    float d = g.Pos - c;
                    float a = Math.Abs(d);
                    if (a <= limit && a < bestAbs) { bestAbs = a; best = d; }
                }
            }
            return bestAbs == float.MaxValue ? 0 : best;
        }

        /// <summary>A rectangle moved so its edges or centre sit on nearby guides.</summary>
        RectangleF SnapRect(RectangleF r)
        {
            float dx = SnapShift(true, r.Left, r.Left + r.Width / 2f, r.Right);
            float dy = SnapShift(false, r.Top, r.Top + r.Height / 2f, r.Bottom);
            r.Offset(dx, dy);
            return r;
        }

        /// <summary>A point (a marquee corner, a shape's end, a crop corner) pulled onto nearby guides.</summary>
        PointF SnapPoint(PointF p)
        {
            return new PointF(p.X + SnapShift(true, p.X), p.Y + SnapShift(false, p.Y));
        }
    }
}
