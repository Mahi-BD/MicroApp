using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Several layers selected at once, as in Photoshop: Ctrl-click (toggle) and Shift-click
    /// (range) in the Layers panel, Shift-click on the canvas with the Move tool, Select >
    /// All Layers. The current layer (_sel) stays the "primary" one that single-layer
    /// commands work on; <see cref="_multi"/> holds the others. Moving, nudging, aligning,
    /// distributing and deleting act on all of them.
    /// </summary>
    partial class ImageEditorForm
    {
        readonly HashSet<EditorLayer> _multi = new HashSet<EditorLayer>();   // selected besides the primary
        Dictionary<EditorLayer, RectangleF> _multiBounds0;                    // bounds at the start of a group drag

        /// <summary>Every selected layer (primary included), bottom to top.</summary>
        List<EditorLayer> SelectedLayers()
        {
            var list = new List<EditorLayer>();
            for (int i = 0; i < _layers.Count; i++)
            {
                EditorLayer l = _layers[i];
                if (l.Floating) continue;
                if (i == _sel || _multi.Contains(l)) list.Add(l);
            }
            return list;
        }

        bool IsMulti { get { PruneMulti(); return _multi.Count > 0 && _sel >= 0; } }

        bool IsLayerSelected(EditorLayer l)
        {
            return (_sel >= 0 && _sel < _layers.Count && _layers[_sel] == l) || _multi.Contains(l);
        }

        /// <summary>Drops layers that are gone (deleted, merged, undone) from the multi-selection.</summary>
        void PruneMulti()
        {
            if (_multi.Count == 0) return;
            EditorLayer primary = _sel >= 0 && _sel < _layers.Count ? _layers[_sel] : null;
            _multi.RemoveWhere(l => l == primary || !_layers.Contains(l) || l.Floating);
        }

        void ClearMulti()
        {
            if (_multi.Count == 0) return;
            _multi.Clear();
            _canvasPanel.Invalidate();
            if (_layerList != null) _layerList.Invalidate();
        }

        /// <summary>
        /// A click on layer <paramref name="index"/> (Layers panel or canvas): plain replaces the
        /// selection, Ctrl toggles that layer in or out, Shift adds the range from the primary.
        /// </summary>
        void LayerClicked(int index, bool ctrl, bool shift)
        {
            if (index < 0 || index >= _layers.Count) return;
            EditorLayer clicked = _layers[index];
            EditorLayer primary = _sel >= 0 && _sel < _layers.Count ? _layers[_sel] : null;
            if (shift && primary != null)
            {
                int a = Math.Min(_sel, index), b = Math.Max(_sel, index);
                for (int i = a; i <= b; i++) if (!_layers[i].Floating) _multi.Add(_layers[i]);
                _sel = index;
            }
            else if (ctrl && primary != null)
            {
                if (clicked == primary)
                {
                    // Ctrl-click the primary: it leaves the selection, another selected layer takes over
                    PruneMulti();
                    if (_multi.Count > 0)
                    {
                        EditorLayer next = _multi.OrderByDescending(l => _layers.IndexOf(l)).First();
                        _multi.Remove(next);
                        _sel = _layers.IndexOf(next);
                    }
                }
                else if (_multi.Contains(clicked)) _multi.Remove(clicked);
                else
                {
                    _multi.Add(primary);
                    _sel = index;
                }
            }
            else
            {
                _multi.Clear();
                _sel = index;
            }
            PruneMulti();
            RefreshLayerList();
            SyncOptionsFromSelection();
            RelayoutOptions();
            UpdateStatus();
            _canvasPanel.Invalidate();
        }

        /// <summary>Select &gt; All Layers (Alt+Ctrl+A): every unlocked, visible layer.</summary>
        void SelectAllLayers()
        {
            if (!EnsureDoc()) return;
            CommitTransform();
            _multi.Clear();
            int top = -1;
            for (int i = 0; i < _layers.Count; i++)
            {
                EditorLayer l = _layers[i];
                if (l.Floating || l.Locked || !l.Visible) continue;
                _multi.Add(l);
                top = i;
            }
            if (top < 0) { Toast.Show("No unlocked layers to select."); return; }
            _sel = top;
            PruneMulti();
            RefreshLayerList();
            RelayoutOptions();
            UpdateStatus();
            _canvasPanel.Invalidate();
        }

        // ------------------------------------------------------------ moving together

        void BeginGroupMove()
        {
            _multiBounds0 = new Dictionary<EditorLayer, RectangleF>();
            foreach (EditorLayer l in SelectedLayers()) if (!l.Locked) _multiBounds0[l] = l.Bounds;
            _drag = Drag.Move;
        }

        /// <summary>The group drag: every selected layer by the same offset; the group's box snaps to guides.</summary>
        void GroupMoveTo(float dx, float dy, bool snap)
        {
            if (_multiBounds0 == null || _multiBounds0.Count == 0) return;
            if (snap)
            {
                RectangleF box = UnionBox(_multiBounds0.Keys, _multiBounds0);
                box.Offset(dx, dy);
                RectangleF snapped = SnapRect(box);
                dx += snapped.X - box.X; dy += snapped.Y - box.Y;
            }
            foreach (var kv in _multiBounds0)
            {
                RectangleF b = kv.Value;
                b.Offset(dx, dy);
                kv.Key.Bounds = b;
            }
            InvalidateDoc();
        }

        /// <summary>The bounding box of layers on the canvas (their transformed boxes), optionally at saved bounds.</summary>
        static RectangleF UnionBox(IEnumerable<EditorLayer> layers, Dictionary<EditorLayer, RectangleF> at = null)
        {
            bool any = false;
            RectangleF u = RectangleF.Empty;
            foreach (EditorLayer l in layers)
            {
                RectangleF box = l.CanvasBox();
                if (at != null && at.ContainsKey(l))
                {
                    // the saved bounds moved by the same amount the live box differs from the live bounds
                    RectangleF now = l.Bounds, then = at[l];
                    box.Offset(then.X - now.X, then.Y - now.Y);
                }
                u = any ? RectangleF.Union(u, box) : box;
                any = true;
            }
            return u;
        }

        /// <summary>Arrow keys with several layers selected: all of them move.</summary>
        bool NudgeGroup(int dx, int dy)
        {
            if (!IsMulti) return false;
            PushUndoCoalesced("nudge", "Nudge");
            foreach (EditorLayer l in SelectedLayers())
            {
                if (l.Locked) continue;
                RectangleF b = l.Bounds;
                b.Offset(dx, dy);
                l.Bounds = b;
            }
            InvalidateDoc();
            return true;
        }

        // ------------------------------------------------------------ align, distribute, delete

        /// <summary>
        /// Align with several layers selected: to the edges / centres of the selection's box
        /// (Photoshop's behaviour); with one layer the canvas is the reference as before.
        /// how: 0 left, 1 horizontal centres, 2 right, 3 top, 4 vertical centres, 5 bottom.
        /// </summary>
        bool AlignGroup(int how)
        {
            if (!IsMulti) return false;
            List<EditorLayer> layers = SelectedLayers().Where(l => !l.Locked).ToList();
            if (layers.Count < 2) return false;
            CommitTransform();
            PushUndo("Align Layers");
            RectangleF u = UnionBox(layers);
            foreach (EditorLayer l in layers)
            {
                RectangleF box = l.CanvasBox();
                float dx = 0, dy = 0;
                switch (how)
                {
                    case 0: dx = u.Left - box.Left; break;
                    case 1: dx = u.Left + u.Width / 2f - (box.Left + box.Width / 2f); break;
                    case 2: dx = u.Right - box.Right; break;
                    case 3: dy = u.Top - box.Top; break;
                    case 4: dy = u.Top + u.Height / 2f - (box.Top + box.Height / 2f); break;
                    case 5: dy = u.Bottom - box.Bottom; break;
                }
                RectangleF b = l.Bounds;
                b.Offset(dx, dy);
                l.Bounds = b;
            }
            AfterDocumentChange();
            return true;
        }

        /// <summary>
        /// Distribute spacing: the outermost two layers stay put and the ones between them move so
        /// every gap between neighbours is the same, left to right (or top to bottom).
        /// </summary>
        void DistributeLayers(bool horizontal)
        {
            List<EditorLayer> layers = SelectedLayers().Where(l => !l.Locked && l.Visible).ToList();
            if (layers.Count < 3)
            {
                Toast.Show("Select three or more layers to distribute: Shift-click them on the canvas or Ctrl-click them in the Layers panel.");
                return;
            }
            CommitTransform();
            var items = layers.Select(l => new { Layer = l, Box = l.CanvasBox() })
                              .OrderBy(x => horizontal ? x.Box.Left + x.Box.Width / 2f : x.Box.Top + x.Box.Height / 2f)
                              .ToList();
            float start = horizontal ? items.First().Box.Left : items.First().Box.Top;
            float end = horizontal ? items.Last().Box.Right : items.Last().Box.Bottom;
            float total = items.Sum(x => horizontal ? x.Box.Width : x.Box.Height);
            float gap = (end - start - total) / (items.Count - 1);
            PushUndo(horizontal ? "Distribute Horizontally" : "Distribute Vertically");
            float at = start;
            foreach (var x in items)
            {
                float want = at;
                float have = horizontal ? x.Box.Left : x.Box.Top;
                RectangleF b = x.Layer.Bounds;
                if (horizontal) b.Offset(want - have, 0); else b.Offset(0, want - have);
                x.Layer.Bounds = b;
                at += (horizontal ? x.Box.Width : x.Box.Height) + gap;
            }
            AfterDocumentChange();
            Toast.Show(string.Format("{0} layers, {1:0.#} px apart", items.Count, gap));
        }

        /// <summary>Delete with several layers selected: all of them go, in one undo step.</summary>
        bool DeleteGroup()
        {
            if (!IsMulti) return false;
            List<EditorLayer> doomed = SelectedLayers();
            if (_xf != null) CancelTransform();
            PushUndo("Delete Layers");
            int lowest = doomed.Min(l => _layers.IndexOf(l));
            foreach (EditorLayer l in doomed) _layers.Remove(l);
            _multi.Clear();
            _sel = Math.Min(Math.Max(0, lowest - 1), _layers.Count - 1);
            AfterDocumentChange();
            return true;
        }

        // ------------------------------------------------------------ painting

        /// <summary>Several layers selected: a dashed box round each and a solid one round the group.</summary>
        bool PaintGroupControls(Graphics g)
        {
            if (_xf != null || !IsMulti || _tool != Tool.Move || !_showTransformControls) return false;
            List<EditorLayer> layers = SelectedLayers();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var dash = new Pen(Color.FromArgb(170, Theme.Accent), 1f) { DashStyle = DashStyle.Dash })
                foreach (EditorLayer l in layers)
                    g.DrawPolygon(dash, Array.ConvertAll(l.CanvasCorners(), p => CanvasToScreen(p)));
            RectangleF u = UnionBox(layers);
            PointF a = CanvasToScreen(new PointF(u.Left, u.Top)), b = CanvasToScreen(new PointF(u.Right, u.Bottom));
            using (var solid = new Pen(Theme.Accent, 1.4f))
                g.DrawRectangle(solid, a.X, a.Y, b.X - a.X, b.Y - a.Y);
            string label = layers.Count + " layers";
            Size sz = TextRenderer.MeasureText(label, Theme.Small);
            var tag = new Rectangle((int)a.X, (int)a.Y - sz.Height - 6, sz.Width + 10, sz.Height + 4);
            using (var bg = new SolidBrush(Theme.Accent)) g.FillRectangle(bg, tag);
            TextRenderer.DrawText(g, label, Theme.Small, tag, Theme.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return true;
        }
    }
}
