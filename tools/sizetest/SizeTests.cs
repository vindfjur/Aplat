using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using Aplat.Controls;
using Aplat.Models;
// One size slider, one meaning: the width of the stroke in px, for every drawing tool.
// Draws a straight stroke with each tool through the real SceneView mapping (size → stroke radius →
// StrokeOp.DrawStroke) and measures how wide it came out.
class SizeTests
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    static int Extent(Bitmap bmp, bool erased)
    {
        int lo = int.MaxValue, hi = -1;
        for (int x = bmp.Width / 2 - 60; x < bmp.Width / 2 + 60; x++)
            for (int y = 0; y < bmp.Height; y++)
            {
                int a = bmp.GetPixel(x, y).A;
                if (erased ? a < 200 : a >= 20) { lo = Math.Min(lo, y); hi = Math.Max(hi, y); }
            }
        return hi < lo ? 0 : hi - lo + 1;
    }

    [STAThread] static void Main()
    {
        var view = new SceneView();
        var radiusOf = typeof(SceneView).GetMethod("CanvasStrokeRadius", F);
        var styleOf = typeof(SceneView).GetMethod("StyleOf", BindingFlags.Static | BindingFlags.NonPublic);
        var penW = typeof(SceneView).GetMethod("LivePenWidth", F);
        var layer = new Bitmap(Material.CanvasPaintSize, 8);   // only the width matters (the sheet scale)
        float scale = Material.PaintScale(layer);
        Check(Math.Abs(scale - 2f) < 1e-3, "the sheet is twice the 1024 reference");
        int checks = 0;
        foreach (int size in new[] { 6, 28, 90 })
        {
            view.BrushRadius = size;
            float want = size * scale;   // stroke width in sheet px
            foreach (BrushKind kind in new[] { BrushKind.Brush, BrushKind.Pencil, BrushKind.Pen, BrushKind.Marker, BrushKind.Eraser })
            {
                view.PaintKind = kind;
                int r = (int)radiusOf.Invoke(view, new object[] { layer });
                var style = (StrokeStyle)styleOf.Invoke(null, new object[] { kind });
                bool erase = kind == BrushKind.Eraser;
                using (var bmp = new Bitmap(600, (int)(want * 1.6f) + 40))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        if (erase) g.Clear(Color.White);
                        float y = bmp.Height / 2f;
                        var pts = new[] { new PointF(100, y), new PointF(300, y), new PointF(500, y) };
                        StrokeOp.DrawStroke(g, style, pts, r, scale, Color.Black, 100, 7, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    }
                    int got = Extent(bmp, erase);
                    float lo = kind == BrushKind.Pencil ? want * 0.7f : want - 3f;   // the pencil's grain is sparse at the rim
                    float hi = want + 6f;
                    Check(got >= lo && got <= hi, kind + " at size " + size + ": stroke is " + got + " px wide, size says " + want);
                    checks++;
                }
            }
            // A shape's outline is the same width again.
            Check((int)penW.Invoke(view, new object[] { layer }) == (int)Math.Round(want), "shape outline width at size " + size);
            checks++;
        }
        Console.WriteLine("PASS: " + checks + " checks — brush, pencil, pen, marker, eraser and shape outline are as wide as the size says.");
    }
}
