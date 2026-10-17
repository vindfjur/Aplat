using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Aplat.Controls;
using Aplat.Models;

// The lasso's model without the UI: ops picked up into a live group, moved / scaled / turned,
// settled as one GroupOp. The live picture, the journal's replay and the saved form must agree,
// the group must be hit-tested through its transform, and putting the ops back (Esc) must restore
// the journal exactly.
static class GroupTests
{
    const int W = 600, H = 600;
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }

    static int Diff(Bitmap a, Bitmap b, int tol)
    {
        int n = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                if (p.A == 0 && q.A == 0) continue;
                int d = Math.Abs(p.A - q.A);
                if (p.A > 200 && q.A > 200)
                    d = Math.Max(d, Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B))));
                if (d > tol) n++;
            }
        return n;
    }

    static StrokeOp Stroke(StrokeStyle st, int r, int argb, int seed, params float[] xy)
    {
        var pts = new List<PointF>();
        for (int i = 0; i < xy.Length; i += 2) pts.Add(new PointF(xy[i], xy[i + 1]));
        var arr = pts.ToArray();
        return new StrokeOp { Style = st, Radius = r, Scale = 1f, Argb = argb, Opacity = 100, Seed = seed, Pts = arr,
                              Bounds = StrokeOp.BoundsFor(arr, r, new Size(W, H)) };
    }

    static Bitmap Replayed(InkJournal j)
    {
        var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        j.Replay(b, new Rectangle(0, 0, W, H));
        return b;
    }

    // What SceneView.LiftSelection builds.
    static LiveItem MakeGroup(List<InkOp> picked, float k)
    {
        var ops = new List<InkOp>(); float[] m0 = { 1, 0, 0, 1, 0, 0 };
        if (picked.Count == 1 && picked[0] is GroupOp)
        {
            var g = (GroupOp)picked[0].Clone(); m0 = g.M; ops.AddRange(g.Children); g.Children = new List<InkOp>();
        }
        else foreach (var op in picked) ops.Add(op.Clone());
        RectangleF ib; float ow;
        using (var probe = GroupOp.Create(ops, m0, new Size(W, H)))
        {
            ib = probe.InkBounds; ow = probe.InkWidth; probe.Children = new List<InkOp>();
        }
        var box0 = RectangleF.FromLTRB(ib.Left, ib.Top * k, ib.Right, ib.Bottom * k);
        return new LiveItem { Kind = LiveKind.Group, Ops = ops, M0 = m0, Box0 = box0, GroupK = k, GroupInk = ow,
                              P1 = new PointF(box0.Left, box0.Top), P2 = new PointF(box0.Right, box0.Bottom) };
    }

    // What SceneView.RecordLiveInk builds on settling.
    static GroupOp Settle(LiveItem live)
    {
        var copy = live.Clone();
        float[] m;
        using (var gm = copy.GroupMatrix()) m = gm.Elements;
        var g = GroupOp.Create(copy.Ops, m, new Size(W, H));
        copy.Ops = null; copy.Dispose();
        return g;
    }

    static int Main()
    {
        var j = new InkJournal();
        var s1 = Stroke(StrokeStyle.Brush, 12, Color.Red.ToArgb(), 1, 60, 100, 160, 140, 260, 90);
        var s2 = Stroke(StrokeStyle.Pen, 6, Color.Blue.ToArgb(), 2, 80, 200, 200, 230, 260, 300);
        var rect = new LiveItem { Kind = LiveKind.Rect, P1 = new PointF(100, 360), P2 = new PointF(240, 460),
                                  Color = Color.FromArgb(220, Color.Green), StrokeWidth = 8, Fill = ShapeFill.Both };
        var item = new ItemOp { Item = rect, K = 1f, Bounds = rect.PaintBounds(W, H) };
        var far = Stroke(StrokeStyle.Marker, 16, Color.Orange.ToArgb(), 3, 400, 80, 520, 100, 560, 160);
        foreach (var op in new InkOp[] { s1, s2, item, far }) j.Add(op);
        var original = Replayed(j);

        // ── Lift, edit, settle ───────────────────────────────────────
        var picked = new List<InkOp> { s1, s2, item };
        var layer = Replayed(j);
        List<KeyValuePair<int, InkOp>> removed;
        j.Lift(picked, layer, out removed);
        Check(j.Ops.Count == 1 && j.Ops[0] == far, "lifting takes the picked ops out of the journal");
        var without = new Bitmap(layer);
        using (var empty = new InkJournal()) { empty.Add(far.Clone()); Check(Diff(without, Replayed(empty), 24) == 0, "the layer is replayed without them"); }

        var live = MakeGroup(picked, 1f);
        var liveLayer = new Bitmap(without);
        using (var g = Graphics.FromImage(liveLayer)) live.Render(g);
        Check(Diff(liveLayer, original, 24) == 0, "untouched, the live group looks exactly like what was picked up (" + Diff(liveLayer, original, 24) + " px off)");

        PointF c = live.Center;
        live.Angle = 30f;
        live.SetBox(new PointF(c.X + 150, c.Y + 40), live.Box.Width * 1.5f, live.Box.Height * 1.5f);
        liveLayer = new Bitmap(without);
        using (var g = Graphics.FromImage(liveLayer)) live.Render(g);

        var group = Settle(live);
        j.Add(group);
        var replay = Replayed(j);
        int off = Diff(replay, liveLayer, 24);
        Check(off < 30, "the journal's replay of the settled group matches the live picture (" + off + " px off, anti-aliasing)");
        Check(replay.GetPixel(120, 100).A == 0 || Diff(replay, original, 24) > 0, "the picture really moved");

        // ── Saved form ───────────────────────────────────────────────
        var back = InkOp.FromRecord64(group.Record64);
        Check(back is GroupOp && ((GroupOp)back).Children.Count == 3, "a group round-trips through its record (3 children)");
        var jb = InkJournal.Deserialize(j.SerializeHeader(0, new Size(W, H)), j.OpRecords(), null, out uint _);
        Check(jb != null && jb.Ops.Count == 2 && jb.Ops[1] is GroupOp, "...and through the journal's saved form");
        Check(Diff(Replayed(jb), replay, 0) == 0, "...replaying to the same picture");

        // ── Hit-testing through the transform ────────────────────────
        PointF[] samples = new List<PointF>(group.SamplePoints()).ToArray();
        Check(samples.Length >= 6, "a group offers sample points of its ink (" + samples.Length + ")");
        PointF q;
        bool hitOnInk = group.NearestInk(samples[0], 3, out q);
        Check(hitOnInk, "a point of the moved ink hits the group");
        Check(j.HitTest(replay, samples[0], 3) == group, "the journal's hit test finds the group at its new place");
        Check(j.HitTest(replay, new PointF(160, 140), 3) == null, "...and nothing at the old one");

        // ── Moving the same drawing again does not nest ──────────────
        var again = MakeGroup(new List<InkOp> { group }, 1f);
        Check(again.Ops.Count == 3, "lifting a lone group opens it (3 ops, no nesting)");
        var group2 = Settle(again);
        Check(group2.Children.Count == 3 && Math.Abs(group2.M[0] - group.M[0]) < 1e-3f && Math.Abs(group2.M[4] - group.M[4]) < 0.05f,
              "...an untouched second pass keeps the transform (" + group2.M[0].ToString("0.000") + " ≈ " + group.M[0].ToString("0.000") + ")");

        // ── Recolour ─────────────────────────────────────────────────
        var tint = MakeGroup(new List<InkOp> { s1, s2, item }, 1f);
        foreach (var op in tint.Ops) op.Recolor(Color.Purple.ToArgb());
        string before = s1.Record64;
        Check(s1.Argb == Color.Red.ToArgb() && s1.Record64 == before, "recolouring the copies leaves the journal's originals alone");
        var tinted = (StrokeOp)tint.Ops[0];
        Check((tinted.Argb & 0xFFFFFF) == (Color.Purple.ToArgb() & 0xFFFFFF) && tinted.Record64 != before, "a recoloured copy has a fresh record");

        // ── Esc: the ops go back where they were ─────────────────────
        j.Discard(group);
        j.Unlift(removed);
        Check(j.Ops.Count == 4 && j.Ops[0] == s1 && j.Ops[1] == s2 && j.Ops[2] == item && j.Ops[3] == far, "putting the ops back restores the journal's order");
        Check(Diff(Replayed(j), original, 0) == 0, "...and the picture exactly");

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
