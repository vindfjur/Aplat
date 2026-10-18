using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Aplat;
using Aplat.Controls;
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;

// Windows / interactive OpenGL regression: screen-space tools must cross atlas
// islands without broken shapes, and projection must stop at the visible surface.
// tools\paneltest\run.ps1 -Source tools\texttest\SurfacePaintingTests.cs
class SurfacePaintingTests
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static string Out = Environment.GetEnvironmentVariable("SHOTS_OUT") ?? Path.Combine(Path.GetTempPath(), "aplat-surface-tests");
    static MainForm form; static SceneView view; static Control gl; static Scene scene;
    static int failed, checks;
    static object Get(object o, string name) { return o.GetType().GetField(name, F).GetValue(o); }
    static object Call(object o, string name, params object[] args)
    {
        try { return o.GetType().GetMethods(F).First(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(o, args); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }
    static void Check(bool ok, string text) { checks++; if (!ok) failed++; Console.WriteLine((ok ? "OK " : "FAIL ") + text); }
    static void Pump() { for (int i = 0; i < 12; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
    static Bitmap Grab()
    {
        view.Refresh3D(); Pump();
        // Read the rendered viewport without UI overlays such as the blue axis gizmo.
        return view.CaptureFrame();
    }
    static bool Blue(Color c) { return c.B > c.R + 35 && c.B > c.G + 35; }
    static bool Orange(Color c) { return c.R > c.B + 45 && c.G > c.B + 15; }
    static int Ink(Bitmap b) { int n = 0; for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++) if (b.GetPixel(x,y).A > 20) n++; return n; }
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static int Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Directory.CreateDirectory(Out); Loc.Set(Lang.English);
        form = new MainForm();
        try { Run(); } catch (Exception e) { Check(false, e.ToString()); }
        finally { scene.IsModified = false; form.Close(); form.Dispose(); }
        Console.WriteLine(failed + " failed / " + checks + " checks"); return failed == 0 ? 0 : 1;
    }
    static void Run()
    {
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0,0); form.Size = new Size(1450,900);
        form.Show(); form.TopMost = true; Pump();
        view = (SceneView)Get(form,"sceneView"); gl = (Control)Get(view,"_gl"); scene = (Scene)Get(form,"_scene");
        Call(form,"ApplyView", ViewMode.Perspective); Call(form,"SetTool", ToolMode.Paint);
        scene.Objects.Clear(); scene.ShowGrid = false; view.OutlineSelection = false;
        var obj = new SceneObject { Mesh = PrimitiveFactory.CreateCylinder(), PrimitiveType = "Cylinder", Position = Vec3.Zero };
        obj.Material.Color = Color.FromArgb(240,150,45); scene.Objects.Add(obj);
        view.Camera.Target = Vec3.Zero; view.Camera.AngleX = 15; view.Camera.AngleY = -25; view.Camera.Distance = 2.6f;
        view.BrushColor = Color.Blue; view.BrushOpacity = 100; view.BrushRadius = 8;
        var center = Point.Round(view.Camera.Project(Vec3.Zero,gl.Width,gl.Height));
        int half = (int)(Math.Min(gl.Width,gl.Height) * 0.72 / 2.6 * 0.32);
        Point a = new Point(center.X-half,center.Y-half), b = new Point(center.X+half,center.Y+half);
        var kinds = new[]{BrushKind.ShapeRect, BrushKind.ShapeEllipse, BrushKind.ShapeLine, BrushKind.ShapeTriangle};
        foreach (var kind in kinds)
        {
            obj.Material.ClearPaint(); view.PaintKind = kind;
            using (var before = Grab())
            {
                Check((bool)Call(view,"BeginLiveShape",obj,a), kind+": live editing starts on a mesh");
                Call(view,"LiveMouseMove",new MouseEventArgs(MouseButtons.Left,0,b.X,b.Y,0));
                Call(view,"LiveMouseUp",new MouseEventArgs(MouseButtons.Left,1,b.X,b.Y,0));
                Check(view.HasLiveItem && Math.Abs(view.Live.P2.X-b.X)<1,kind+": drag follows screen coordinates");
                // Compare the actual viewport with an independent 2D raster reference.
                using (var expected = new Bitmap(gl.Width,gl.Height))
                {
                    using (var g = Graphics.FromImage(expected)) using (var pen = new Pen(Color.Blue,8))
                    {
                        var r = Rectangle.FromLTRB(a.X,a.Y,b.X,b.Y);
                        if (kind == BrushKind.ShapeRect) g.DrawRectangle(pen,r);
                        else if (kind == BrushKind.ShapeEllipse) g.DrawEllipse(pen,r);
                        else if (kind == BrushKind.ShapeLine) g.DrawLine(pen,a,b);
                        else g.DrawPolygon(pen,new[]{new Point(center.X,a.Y),b,new Point(a.X,b.Y)});
                    }
                    view.CommitLive();
                    using (var actual = Grab())
                    {
                        actual.Save(Path.Combine(Out,kind+".png"));
                        int wanted=0,found=0,stray=0,total=0;
                        for (int y=4;y<gl.Height-4;y++) for (int x=4;x<gl.Width-4;x++)
                        {
                            if (Blue(expected.GetPixel(x,y)) && Orange(before.GetPixel(x,y))) { wanted++; if (Blue(actual.GetPixel(x,y))) found++; }
                            if (!Blue(actual.GetPixel(x,y))) continue;
                            total++; bool near=false;
                            // Allow three screen pixels for atlas filtering and antialiasing.
                            for (int dy=-3;dy<=3 && !near;dy++) for(int dx=-3;dx<=3;dx++) if(Blue(expected.GetPixel(x+dx,y+dy))) {near=true;break;}
                            if(!near) stray++;
                        }
                        Check(wanted>100 && found>wanted*0.8,kind+": continuous projected outline ("+found+"/"+wanted+")");
                        Check(total>100 && stray<total*0.08,kind+": no atlas streaks ("+stray+"/"+total+")");
                    }
                }
            }
            // Esc restores the pre-element picture.
            int ink = Ink(obj.Material.PaintLayer);
            Call(view,"BeginLiveShape",obj,a); view.EditLive(i=>i.P2=new PointF(b.X,b.Y)); view.CancelLive();
            Check(Ink(obj.Material.PaintLayer)==ink,kind+": cancel restores paint");
        }
        // A fully hidden surface must receive no text, shapes, or brush paint.
        obj.Material.ClearPaint();
        var blocker = new SceneObject { Mesh=PrimitiveFactory.CreateCube(), PrimitiveType="Cube", Position=view.Camera.GetPosition()*0.4f, Scale=new Vec3(1.4f,1.4f,1.4f) };
        scene.Objects.Add(blocker);
        Call(view,"BeginSurfaceText",obj,center); view.EditLive(i=>{i.Text="TEST"; i.FontPx=60;}); view.CommitLive();
        Check(Ink(obj.Material.PaintLayer)==0,"projection does not paint through a foreground object");
        scene.Objects.Remove(blocker);
        // Two facing overlapping planes in a single mesh: rear-facing orientation alone
        // cannot reject the hidden plane. Its atlas cells must stay transparent.
        obj.Mesh = new Mesh(); obj.PrimitiveType="Mesh";
        foreach(float z in new[]{0.2f,-0.2f})
        {
            int n=obj.Mesh.Vertices.Count;
            obj.Mesh.Vertices.AddRange(new[]{new Vec3(-0.5f,-0.5f,z),new Vec3(0.5f,-0.5f,z),new Vec3(0.5f,0.5f,z),new Vec3(-0.5f,0.5f,z)});
            obj.Mesh.Faces.Add(new[]{n,n+1,n+2,n+3});
        }
        view.Camera.AngleX=0; view.Camera.AngleY=0; obj.Material.ClearPaint();
        view.PaintKind=BrushKind.ShapeRect; view.ShapeFillMode=ShapeFill.Fill;
        Call(view,"BeginLiveShape",obj,a); view.EditLive(i=>i.P2=new PointF(b.X,b.Y)); view.CommitLive();
        int front=0,back=0; var layer=obj.Material.PaintLayer;
        for(int y=0;y<layer.Height/2;y++) for(int x=0;x<layer.Width;x++) if(layer.GetPixel(x,y).A>20) {if(x<layer.Width/2) front++;else back++;}
        Check(front>100 && back==0,"self-occluded front-facing triangles remain unpainted ("+front+", "+back+")");
    }
}
