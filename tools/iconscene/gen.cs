// Generates the application icon by building a tiny 3-D scene and rendering it,
// then packing the render at several sizes into a multi-resolution icon.ico.
//
// The scene is the product's identity: a shaded cube (the "3D" half, in the app's
// accent blue) with a paint-stroke ribbon sweeping across it (the "Paint" half, in
// the selection orange), on a rounded graphite tile. Everything is drawn at 4×
// and downscaled, so edges stay crisp at every icon size.
//
//   mcs tools/iconscene/gen.cs -r:System.Drawing.dll -out:gen.exe && mono gen.exe
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class IconScene
{
    // ── App palette (matches ThemePalette / GLRenderer) ──────────────────────
    static readonly Color TileTop = Color.FromArgb(45, 47, 54);
    static readonly Color TileBot = Color.FromArgb(24, 25, 29);
    static readonly Color Accent  = Color.FromArgb(92, 134, 255);   // 3-D cube
    static readonly Color Stroke  = Color.FromArgb(255, 160, 40);   // paint ribbon

    struct V3 { public double X, Y, Z; public V3(double x, double y, double z){X=x;Y=y;Z=z;} }

    static V3 Sub(V3 a, V3 b){ return new V3(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
    static V3 Cross(V3 a, V3 b){ return new V3(a.Y*b.Z-a.Z*b.Y, a.Z*b.X-a.X*b.Z, a.X*b.Y-a.Y*b.X); }
    static double Dot(V3 a, V3 b){ return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
    static V3 Norm(V3 a){ double l=Math.Sqrt(Dot(a,a)); return l<1e-9?a:new V3(a.X/l,a.Y/l,a.Z/l); }

    // Dimetric camera: rotate around Y then tilt down around X, orthographic.
    static V3 Rot(V3 p, double ay, double ax)
    {
        double cy=Math.Cos(ay), sy=Math.Sin(ay);
        double x=p.X*cy + p.Z*sy, z=-p.X*sy + p.Z*cy, y=p.Y;
        double cx=Math.Cos(ax), sx=Math.Sin(ax);
        double y2=y*cx - z*sx, z2=y*sx + z*cx;
        return new V3(x, y2, z2);
    }

    static void RenderScene(Graphics g, int S)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Rounded tile with a vertical graphite gradient.
        float r = S * 0.225f;
        using (var path = Round(new RectangleF(0,0,S,S), r))
        using (var br = new LinearGradientBrush(new RectangleF(0,0,S,S), TileTop, TileBot, 90f))
        {
            g.FillPath(br, path);
            // A soft top sheen for a premium, slightly glassy tile.
            using (var sheen = new LinearGradientBrush(new RectangleF(0,0,S,S*0.5f),
                       Color.FromArgb(28,255,255,255), Color.FromArgb(0,255,255,255), 90f))
            using (var clip = Round(new RectangleF(0,0,S,S), r))
            {
                var old = g.Clip; g.SetClip(clip);
                g.FillRectangle(sheen, 0,0,S,S*0.55f);
                g.Clip = old;
            }
        }

        // ── Cube scene ────────────────────────────────────────────────────────
        double ay = -0.7854, ax = 0.6155;         // true isometric (45°, 35.26°)
        double sc = S * 0.215;                    // cube screen scale
        double cx = S * 0.50, cy = S * 0.50;      // scene centre on the tile
        V3 light = Norm(new V3(-0.4, 1.0, 0.75)); // key light

        V3[] cube =
        {
            new V3(-1,-1,-1), new V3(1,-1,-1), new V3(1,1,-1), new V3(-1,1,-1),
            new V3(-1,-1, 1), new V3(1,-1, 1), new V3(1,1, 1), new V3(-1,1, 1),
        };
        int[][] faces =
        {
            new[]{4,5,6,7}, // +Z front
            new[]{1,5,6,2}, // +X right
            new[]{3,2,6,7}, // +Y top
            new[]{0,1,2,3}, // -Z back
            new[]{0,4,7,3}, // -X left
            new[]{0,1,5,4}, // -Y bottom
        };

        var vr = new V3[8];
        for (int i=0;i<8;i++) vr[i]=Rot(cube[i], ay, ax);

        Func<V3,PointF> P = v => new PointF((float)(cx + v.X*sc), (float)(cy - v.Y*sc));

        // Painter's order: far faces first.
        var order = new List<int>();
        for (int f=0; f<faces.Length; f++) order.Add(f);
        order.Sort((a,b)=>{
            double za=0, zb=0;
            foreach (int i in faces[a]) za+=vr[i].Z; za/=faces[a].Length;
            foreach (int i in faces[b]) zb+=vr[i].Z; zb/=faces[b].Length;
            return za.CompareTo(zb);
        });

        // Soft contact shadow under the cube.
        using (var sh = new GraphicsPath())
        {
            float sw=(float)(sc*2.3), shh=(float)(sc*0.7);
            sh.AddEllipse((float)cx-sw/2, (float)(cy+sc*0.95)-shh/2, sw, shh);
            using (var pb = new PathGradientBrush(sh))
            {
                pb.CenterColor = Color.FromArgb(120,0,0,0);
                pb.SurroundColors = new[]{ Color.FromArgb(0,0,0,0) };
                g.FillPath(pb, sh);
            }
        }

        foreach (int f in order)
        {
            var poly = new PointF[faces[f].Length];
            V3 cen = new V3(0,0,0);
            for (int i=0;i<faces[f].Length;i++){ poly[i]=P(vr[faces[f][i]]); cen=new V3(cen.X+vr[faces[f][i]].X,cen.Y+vr[faces[f][i]].Y,cen.Z+vr[faces[f][i]].Z); }

            // Face normal, oriented to point OUT of the cube (centre at origin), then
            // Lambert-shaded. Painter's order already hides the back faces, so no cull.
            V3 n = Norm(Cross(Sub(vr[faces[f][1]],vr[faces[f][0]]), Sub(vr[faces[f][2]],vr[faces[f][0]])));
            if (Dot(n, cen) < 0){ n = new V3(-n.X,-n.Y,-n.Z); }
            double lam = Math.Max(0, Dot(n, light));
            double shade = 0.34 + 0.66*lam;        // ambient + diffuse
            Color face = Scale(Accent, shade);

            using (var br = new SolidBrush(face)) g.FillPolygon(br, poly);
            using (var pen = new Pen(Scale(Accent, Math.Min(1.0, shade+0.28)), S*0.014f){ LineJoin=LineJoin.Round })
                g.DrawPolygon(pen, poly);
        }

        // ── Paint ribbon sweeping across the cube ──────────────────────────────
        using (var ribbon = new GraphicsPath())
        {
            var pts = new[]
            {
                new PointF(S*0.12f, S*0.66f),
                new PointF(S*0.40f, S*0.52f),
                new PointF(S*0.60f, S*0.74f),
                new PointF(S*0.90f, S*0.40f),
            };
            ribbon.AddBezier(pts[0], pts[1], pts[2], pts[3]);
            using (var pen = new Pen(Stroke, S*0.11f){ StartCap=LineCap.Round, EndCap=LineCap.Round, LineJoin=LineJoin.Round })
            {
                // Glow then core, for a painted, slightly luminous stroke.
                using (var glow = new Pen(Color.FromArgb(70, Stroke), S*0.17f){ StartCap=LineCap.Round, EndCap=LineCap.Round })
                    g.DrawPath(glow, ribbon);
                g.DrawPath(pen, ribbon);
                // A lighter highlight along the top of the stroke.
                using (var hi = new Pen(Color.FromArgb(150, 255, 225, 180), S*0.035f){ StartCap=LineCap.Round, EndCap=LineCap.Round })
                {
                    var hp = new GraphicsPath();
                    hp.AddBezier(new PointF(S*0.13f,S*0.635f), new PointF(S*0.40f,S*0.495f),
                                 new PointF(S*0.60f,S*0.715f), new PointF(S*0.89f,S*0.385f));
                    g.DrawPath(hi, hp);
                }
            }
        }
    }

    static int C255(double v){ return Math.Max(0, Math.Min(255, (int)Math.Round(v))); }
    static Color Scale(Color c, double k)
    {
        return Color.FromArgb(c.A, C255(c.R*k), C255(c.G*k), C255(c.B*k));
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d=rad*2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right-d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom-d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Bitmap RenderAt(int size)
    {
        int ss = 4;                                  // supersample
        using (var big = new Bitmap(size*ss, size*ss, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(big)) RenderScene(g, size*ss);
            var outb = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(outb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(big, new Rectangle(0,0,size,size));
            }
            return outb;
        }
    }

    static void WriteIco(string path, int[] sizes)
    {
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
            using (var bmp = RenderAt(s))
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }

        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);  // ICONDIR
            int offset = 6 + 16*sizes.Length;
            for (int i=0;i<sizes.Length;i++)
            {
                int s = sizes[i];
                w.Write((byte)(s>=256?0:s));   // width  (0 = 256)
                w.Write((byte)(s>=256?0:s));   // height
                w.Write((byte)0);              // palette
                w.Write((byte)0);              // reserved
                w.Write((short)1);             // colour planes
                w.Write((short)32);            // bpp
                w.Write(pngs[i].Length);
                w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
    }

    static void Main(string[] args)
    {
        string ico = args.Length>0 ? args[0] : "icon.ico";
        WriteIco(ico, new[]{16,24,32,48,64,128,256});
        if (args.Length>1) using (var b=RenderAt(256)) b.Save(args[1], ImageFormat.Png);
        Console.WriteLine("wrote " + ico);
    }
}
