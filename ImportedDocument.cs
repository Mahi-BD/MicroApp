using System;
using System.Collections.Generic;
using System.Drawing;

namespace MicroApp
{
    /// <summary>One raster layer read out of another program's file (Photoshop, Illustrator).</summary>
    sealed class ImportedLayer
    {
        public string Name;
        public Bitmap Image;                    // null for a layer with no pixels
        public Rectangle Bounds;                // where it sits on the canvas
        public int Opacity = 100;               // 0..100
        public bool Visible = true;
        public BlendMode Blend = BlendMode.Normal;
    }

    /// <summary>One canvas: a Photoshop document, or one Illustrator artboard.</summary>
    sealed class ImportedPage
    {
        public string Name;
        public int Width, Height;
        public readonly List<ImportedLayer> Layers = new List<ImportedLayer>();   // bottom first, like the editor
    }

    /// <summary>
    /// What an importer hands the image editor: pages of plain raster layers, plus short
    /// notes about anything it could not reproduce (shown to the user after opening).
    /// </summary>
    sealed class ImportedDocument : IDisposable
    {
        public float Ppi = 72;
        public readonly List<ImportedPage> Pages = new List<ImportedPage>();
        public readonly List<string> Notes = new List<string>();

        public void Dispose()
        {
            foreach (ImportedPage p in Pages)
                foreach (ImportedLayer l in p.Layers)
                    if (l.Image != null) { l.Image.Dispose(); l.Image = null; }
        }
    }
}
