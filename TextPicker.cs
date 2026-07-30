using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Reads the real text of whatever UI element sits under a screen point, through
    /// UI Automation — no OCR involved, so the result is exact, multi-line and instant.
    /// Password fields are never read.
    /// </summary>
    static class UiTextReader
    {
        const int MaxChars = 32000;      // hard cap on what one pick may return
        const int MaxDescendants = 200;  // BFS budget when the element itself is silent

        /// <summary>Full extraction, used when the user clicks. Null when nothing readable.</summary>
        public static string ExtractTextAt(Point screenPoint)
        {
            AutomationElement el = FromPoint(screenPoint);
            if (el == null) return null;

            string text = ReadElement(el, MaxChars);
            if (!string.IsNullOrWhiteSpace(text)) return Clean(text);

            // the element itself said nothing: gather its descendants' texts (lists,
            // panels, message bodies made of many small text runs)
            text = ReadDescendants(el);
            if (!string.IsNullOrWhiteSpace(text)) return Clean(text);

            // still nothing: climb a little — the point may have hit decoration
            // inside a control that does carry text
            var walker = TreeWalker.ControlViewWalker;
            AutomationElement parent = el;
            for (int i = 0; i < 3; i++)
            {
                try { parent = walker.GetParent(parent); } catch (Exception) { break; }
                if (parent == null) break;
                text = ReadElement(parent, MaxChars);
                if (!string.IsNullOrWhiteSpace(text)) return Clean(text);
            }
            return null;
        }

        /// <summary>
        /// Cheap look used for the live hover preview: a short snippet plus the element's
        /// screen bounds. Never throws.
        /// </summary>
        public static bool Peek(Point screenPoint, out string snippet, out Rectangle bounds)
        {
            snippet = null;
            bounds = Rectangle.Empty;
            AutomationElement el = FromPoint(screenPoint);
            if (el == null) return false;

            try
            {
                var r = el.Current.BoundingRectangle;
                if (!r.IsEmpty && r.Width > 0 && r.Height > 0)
                {
                    bounds = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
                }
            }
            catch (Exception) { }

            snippet = ReadElement(el, 400);
            return true;
        }

        static AutomationElement FromPoint(Point p)
        {
            try
            {
                return AutomationElement.FromPoint(new System.Windows.Point(p.X, p.Y));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>TextPattern first (whole document, keeps line breaks), then Value, then Name.</summary>
        static string ReadElement(AutomationElement el, int maxChars)
        {
            try
            {
                if (el.Current.IsPassword) return null;
            }
            catch (Exception) { return null; }

            object pattern;
            try
            {
                if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                {
                    string t = ((TextPattern)pattern).DocumentRange.GetText(maxChars);
                    if (!string.IsNullOrWhiteSpace(t)) return t;
                }
            }
            catch (Exception) { }
            try
            {
                if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                {
                    string t = ((ValuePattern)pattern).Current.Value;
                    if (!string.IsNullOrWhiteSpace(t)) return t;
                }
            }
            catch (Exception) { }
            try
            {
                string t = el.Current.Name;
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Breadth-first sweep of the subtree, one line per element that carries text.</summary>
        static string ReadDescendants(AutomationElement root)
        {
            var sb = new StringBuilder();
            var queue = new Queue<AutomationElement>();
            queue.Enqueue(root);
            int seen = 0;
            string last = null;
            var walker = TreeWalker.RawViewWalker;

            while (queue.Count > 0 && seen < MaxDescendants && sb.Length < MaxChars)
            {
                AutomationElement el = queue.Dequeue();
                seen++;

                if (el != root)
                {
                    string t = ReadElement(el, MaxChars - sb.Length);
                    if (!string.IsNullOrWhiteSpace(t) && t != last)
                    {
                        if (sb.Length > 0) sb.Append("\r\n");
                        sb.Append(t.Trim());
                        last = t;
                    }
                }
                try
                {
                    for (AutomationElement child = walker.GetFirstChild(el);
                         child != null && seen + queue.Count < MaxDescendants;
                         child = walker.GetNextSibling(child))
                    {
                        queue.Enqueue(child);
                    }
                }
                catch (Exception) { }
            }
            return sb.ToString();
        }

        static string Clean(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n").Trim();
            if (text.Length > MaxChars) text = text.Substring(0, MaxChars);
            return text;
        }
    }

    /// <summary>
    /// The little card that follows the crosshair while picking, showing what would be
    /// grabbed — same idea as a colour picker's live swatch. Hit-test transparent, so it
    /// never gets in the way of the element under the cursor.
    /// </summary>
    class TextPickerHud : Form
    {
        const int MaxWidth = 380;
        string _text = "";
        const string Hint = "Click to pick the text.   Right-click or Esc cancels.";

        public TextPickerHud()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Surface;
            Font = Theme.Base;
            DoubleBuffered = true;
            Size = new Size(280, 52);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_TOPMOST
                cp.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x8;
                return cp;
            }
        }

        public void UpdatePreview(string snippet, Point cursor)
        {
            _text = string.IsNullOrWhiteSpace(snippet) ? "" : snippet.Trim();

            // measure: up to 5 preview lines plus the hint line
            string preview = PreviewText();
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            Size body = string.IsNullOrEmpty(preview)
                ? Size.Empty
                : TextRenderer.MeasureText(preview, Theme.Base, new Size(MaxWidth - 24, 5 * Font.Height), flags);
            Size hint = TextRenderer.MeasureText(Hint, Theme.Small, new Size(MaxWidth - 24, 40), flags);

            int w = Math.Max(body.Width, hint.Width) + 24;
            int h = 12 + (body.Height > 0 ? body.Height + 6 : 0) + hint.Height + 10;
            Size = new Size(Math.Min(MaxWidth, Math.Max(200, w)), h);

            // sit below-right of the crosshair, flipping when near a screen edge
            var screen = Screen.FromPoint(cursor).Bounds;
            int x = cursor.X + 18, y = cursor.Y + 24;
            if (x + Width > screen.Right - 4) x = cursor.X - Width - 18;
            if (y + Height > screen.Bottom - 4) y = cursor.Y - Height - 24;
            Location = new Point(x, y);
            Invalidate();
        }

        string PreviewText()
        {
            if (string.IsNullOrEmpty(_text)) return "";
            string t = _text;
            int cut = 0, lines = 0;
            while (cut < t.Length && lines < 5)
            {
                int nl = t.IndexOf('\n', cut);
                if (nl < 0) { cut = t.Length; break; }
                cut = nl + 1;
                lines++;
            }
            if (cut < t.Length) t = t.Substring(0, cut).TrimEnd() + " …";
            if (t.Length > 420) t = t.Substring(0, 420) + " …";
            return t;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var fill = new SolidBrush(Theme.Surface))
                g.FillRectangle(fill, ClientRectangle);
            using (var border = new Pen(Theme.Accent))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            int y = 8;
            string preview = PreviewText();
            if (!string.IsNullOrEmpty(preview))
            {
                var r = new Rectangle(12, y, Width - 24, 5 * Font.Height + 4);
                TextRenderer.DrawText(g, preview, Theme.Base, r, Theme.Text, flags);
                y += TextRenderer.MeasureText(preview, Theme.Base, new Size(Width - 24, 5 * Font.Height), flags).Height + 6;
            }
            TextRenderer.DrawText(g, Hint, Theme.Small,
                new Rectangle(12, y, Width - 24, Height - y - 4), Theme.TextDim, flags);
        }
    }

    /// <summary>Thin accent frame drawn around the element the crosshair is over.</summary>
    class ElementOutline : Form
    {
        const int Thickness = 2;

        public ElementOutline()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Accent;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x8;   // no-activate, toolwindow, hit-transparent, topmost
                return cp;
            }
        }

        public void Outline(Rectangle bounds)
        {
            if (bounds.IsEmpty || bounds.Width < 4 || bounds.Height < 4)
            {
                Visible = false;
                return;
            }
            Bounds = bounds;
            var frame = new Region(new Rectangle(0, 0, bounds.Width, bounds.Height));
            frame.Exclude(new Rectangle(Thickness, Thickness,
                bounds.Width - 2 * Thickness, bounds.Height - 2 * Thickness));
            Region = frame;
            if (!Visible) Show();
        }
    }
}
