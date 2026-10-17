using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Reflection;
using Aplat.Controls;
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;
class RulerTests
{
    static BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(object v,string n,object x){v.GetType().GetField(n,Flags).SetValue(v,x);}
    static object Get(object v,string n){return v.GetType().GetField(n,Flags).GetValue(v);}
    static object Call(object v,string n,params object[] args){return v.GetType().GetMethod(n,Flags).Invoke(v,args);}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static float Distance(PointF a,PointF b){float x=a.X-b.X,y=a.Y-b.Y;return (float)Math.Sqrt(x*x+y*y);}
    [STAThread] static void Main()
    {
        var view=new SceneView();view.Size=new Size(1000,800);view.PerformLayout();
        var gl=(System.Windows.Forms.Control)Get(view,"_gl");
        var canvas=new SceneObject {PrimitiveType="Canvas",Mesh=CanvasFactory.CreateSheet(4,3)};
        var scene=new Scene();scene.Objects.Add(canvas);Set(view,"_scene",scene);
        view.Camera.Mode=ViewMode.Front;view.RulerVisible=true;
        var layer=canvas.Material.PaintLayer;float w=layer.Width;
        int count=0;
        foreach(float ratio in new[]{0.5f,0.75f,1f,1.6f})
        {
            canvas.Mesh=CanvasFactory.CreateSheet(4,4*ratio);
            float k=(float)Call(view,"RulerK");
            foreach(bool perspective in new[]{false,true})
            {
                view.Camera.Mode=perspective?ViewMode.Perspective:ViewMode.Front;
                view.Camera.AngleX=17;view.Camera.AngleY=23;
                PointF sh=new PointF(w*0.32f,w*ratio*0.61f);
                PointF screen=(PointF)Call(view,"SheetToScreen",sh,k);
                var expected=view.Camera.Project(new Vec3(-2+4*0.32f,2*ratio-4*ratio*0.61f,0),gl.Width,gl.Height);
                Check(Distance(screen,expected)<0.02f,"sheet mapping stretches ruler twice");
                object[] args={Point.Round(screen),k,PointF.Empty};
                Check((bool)Call(view,"ScreenToSheet",args),"inverse mapping");
                var again=(PointF)Call(view,"SheetToScreen",(PointF)args[2],k);
                Check(Distance(again,Point.Round(screen))<0.02f,"inverse mapping must round trip");
                foreach(float angle in new[]{0f,15f,33f,45f,78f,90f,135f,-37f})
                foreach(int side in new[]{-1,1})
                {
                    Set(view,"_rulerC",new PointF(w/2,w*ratio/2));Set(view,"_rulerAngle",angle);
                    Set(view,"_strokeRadius",18);view.PaintKind=BrushKind.Brush;
                    Call(view,"RulerBeginStroke");
                    double a=angle*Math.PI/180;float nx=-(float)Math.Sin(a),ny=(float)Math.Cos(a);
                    float thick=(float)Get(view,"_rulerThick");
                    var points=(List<PointF>)Get(view,"_strokePts");points.Clear();
                    for(int i=0;i<8;i++)
                    {
                        var sheet=(PointF)Call(view,"RulerLocal",(i-4)*30f,side*(thick/2+5+i%2));
                        Set(view,"_lastMouse",Point.Round((PointF)Call(view,"SheetToScreen",sheet,k)));
                        var result=(PointF)Call(view,"RulerConstrain",new PointF(sheet.X,sheet.Y/k),canvas);
                        Call(view,"AddCanvasStrokePoint",result,18,1f);
                        float gap=18*(float)Math.Sqrt(nx*nx+k*k*ny*ny);
                        float off=(result.X-w/2)*nx+(result.Y*k-w*ratio/2)*ny;
                        Check(Math.Abs(off-side*(thick/2+gap))<0.02,"angled stroke is not tangent to ruler");
                    }
                    foreach(var point in points)
                    {
                        float gap=18*(float)Math.Sqrt(nx*nx+k*k*ny*ny);
                        float off=(point.X-w/2)*nx+(point.Y*k-w*ratio/2)*ny;
                        Check(Math.Abs(off-side*(thick/2+gap))<0.02,"stabilizer bends constrained stroke");
                    }
                    count++;
                }
            }
        }
        view.Camera.Mode=ViewMode.Perspective;view.Camera.AngleX=0;view.Camera.AngleY=0;
        Check((bool)Call(view,"RulerFacesCamera"),"front must show ruler");
        view.Camera.AngleY=180;
        Check(!(bool)Call(view,"RulerFacesCamera"),"the sheet faces away from the camera");
        Check(Call(view,"RulerHit",new Point(500,400)).ToString()!="None","the ruler is two-sided: it can be grabbed from behind too");
        var raw=new PointF(123,456);
        Check((PointF)Call(view,"RulerConstrain",raw,canvas)==raw,"back constrains paint");
        // ── End handles: stretch, and pin an end with a double click ──
        view.Camera.Mode=ViewMode.Front;view.Camera.AngleX=0;view.Camera.AngleY=0;
        canvas.Mesh=CanvasFactory.CreateSheet(4,4);
        Set(view,"_tool",ToolMode.Paint);
        int undoSteps=0;view.TransformStarted+=(o,e)=>undoSteps++;
        int changes=0;view.RulerChanged+=(o,e)=>changes++;
        Func<PointF,Point> scr=sh=>Point.Round((PointF)Call(view,"SheetToScreen",sh,(float)Call(view,"RulerK")));
        Func<float,float,PointF> loc=(lx,ly)=>(PointF)Call(view,"RulerLocal",lx,ly);
        Func<bool,PointF> endOf=b=>(PointF)Call(view,"RulerEnd",b);
        Func<float> len=()=>(float)Get(view,"_rulerLen");
        Func<float> ang=()=>(float)Get(view,"_rulerAngle");
        Func<string,bool> flag=n=>(bool)Get(view,n);
        Action<float,float,float> reset=(cx,cy,a)=>{Set(view,"_rulerC",new PointF(cx,cy));Set(view,"_rulerAngle",a);Set(view,"_rulerLen",w*0.5f);Set(view,"_rulerPinA",false);Set(view,"_rulerPinB",false);Set(view,"_rulerDrag",Enum.ToObject(Get(view,"_rulerDrag").GetType(),0));};
        Func<Point,int,bool> down=(p,clicks)=>(bool)Call(view,"RulerMouseDown",new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,clicks,p.X,p.Y,0));
        Action<Point> move=p=>Call(view,"RulerMouseMove",new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,0,p.X,p.Y,0));
        Action<Point> up=p=>Call(view,"RulerMouseUp",new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,0,p.X,p.Y,0));
        Action<string,PointF,PointF> near=(m,a,b)=>Check(Distance(a,b)<1.5f,m+": "+a+" vs "+b);
        float hl;
        Func<float,Point> knob=h=>{var p=scr(loc(h,0));return new Point(p.X+18,p.Y);};   // the turning knob sits 18 screen px beyond the end
        string hit;
        // hit zones: the short edges stretch, the knobs turn, the middle moves
        reset(w/2,w/2,0);hl=len()/2;
        Func<Point,string> hitAt=p=>Call(view,"RulerHit",p).ToString();
        Check(hitAt(scr(loc(hl-3,0)))=="StretchB","right edge stretches");
        Check(hitAt(scr(loc(-hl+3,0)))=="StretchA","left edge stretches");
        // the ruler is not paper: a plain press on its body draws nothing and moves nothing (crossed-out cursor);
        // with Ctrl the body carries the ruler
        Check(hitAt(scr(loc(0,0)))=="Body","body: nothing is drawn on it");
        Check(Call(view,"RulerCursor",Enum.Parse(typeof(SceneView).GetNestedType("RulerDrag",BindingFlags.NonPublic),"Body"))==System.Windows.Forms.Cursors.No,"over the body the cursor is crossed out");
        {
            PointF c00=(PointF)Get(view,"_rulerC");
            Check(down(scr(loc(0,0)),1),"a press on the body is the ruler's (no paint under it)");move(scr(loc(200,150)));up(Point.Empty);
            Check(Distance((PointF)Get(view,"_rulerC"),c00)<0.01f,"and it does not move");
        }
        Set(view,"_rulerMoveHeld",true);
        Check(hitAt(scr(loc(0,0)))=="Move","with Ctrl the body moves");
        // stretching B keeps A where it is
        PointF a0=endOf(false);float l0=len();
        Check(down(scr(loc(hl-3,0)),1),"press on the end is the ruler's");
        move(scr(loc(hl-3+120,0)));up(scr(loc(hl-3+120,0)));
        near("A stays while B stretches",endOf(false),a0);
        Check(Math.Abs(len()-(l0+120))<30,"B stretched by the drag: "+(len()-l0));
        Check(Math.Abs(ang())<0.01f,"stretch keeps the angle");
        // stretching A keeps B
        reset(w/2,w/2,30);hl=len()/2;PointF b0=endOf(true);l0=len();
        down(scr(loc(-hl+3,0)),1);move(scr(loc(-hl+3-90,0)));up(scr(loc(-hl+3-90,0)));
        near("B stays while A stretches",endOf(true),b0);
        Check(Math.Abs(len()-(l0+90))<30,"A stretched: "+(len()-l0));
        // limits: it cannot collapse or grow without bound
        reset(w/2,w/2,0);hl=len()/2;
        down(scr(loc(hl-3,0)),1);move(scr(loc(-hl*3,0)));up(Point.Empty);
        Check(len()>=(float)Call(view,"get_RulerMinLen")-0.01f,"minimum length");
        reset(w/2,w/2,0);hl=len()/2;
        down(scr(loc(hl-3,0)),1);move(scr(loc(hl+w*6,0)));up(Point.Empty);
        Check(len()<=(float)Call(view,"get_RulerMaxLen")+0.01f,"maximum length");
        // a double click pins an end and a second one lets it go; no undo step is taken
        reset(w/2,w/2,0);hl=len()/2;changes=0;
        down(scr(loc(-hl,0)),1);up(scr(loc(-hl,0)));
        Check(down(scr(loc(-hl,0)),2),"double click is the ruler's");up(Point.Empty);
        Check(flag("_rulerPinA")&&!flag("_rulerPinB"),"double click on A pins A");
        near("a click does not move the ruler",endOf(false),loc(-hl,0));
        down(knob(hl),1);up(Point.Empty);down(knob(hl),2);up(Point.Empty);
        Check(flag("_rulerPinB"),"double click on the knob pins B too");
        down(knob(hl),2);up(Point.Empty);
        Check(!flag("_rulerPinB"),"second double click releases B");
        down(scr(loc(-hl,0)),2);up(Point.Empty);
        Check(!flag("_rulerPinA"),"second double click releases A");
        Check(changes==4,"pin toggles raise RulerChanged: "+changes);
        // jitter inside a double click does not nudge the ruler
        reset(w/2,w/2,0);hl=len()/2;a0=endOf(false);b0=endOf(true);
        down(scr(loc(hl-3,0)),1);move(new Point(scr(loc(hl-3,0)).X+1,scr(loc(hl-3,0)).Y+1));up(Point.Empty);
        near("jitter keeps A",endOf(false),a0);near("jitter keeps B",endOf(true),b0);
        // A pinned: the body hinges on it, the length is kept, B follows the mouse
        reset(w/2,w/2,0);hl=len()/2;l0=len();
        down(scr(loc(-hl,0)),2);up(Point.Empty);a0=endOf(false);
        down(scr(loc(0,0)),1);move(scr(new PointF(a0.X,a0.Y+l0)));up(Point.Empty);
        near("pinned A does not move",endOf(false),a0);
        Check(Math.Abs(len()-l0)<0.5f,"pivot keeps the length");
        Check(Math.Abs(ang()-90)<0.01f,"B follows the mouse to 90°: "+ang());
        // turning with B's knob also hinges on A
        reset(w/2,w/2,0);Set(view,"_rulerPinA",true);hl=len()/2;l0=len();a0=endOf(false);
        down(knob(hl),1);move(scr(new PointF(a0.X+l0,a0.Y+l0)));up(Point.Empty);
        near("A fixed under the knob turn",endOf(false),a0);Check(Math.Abs(ang()-45)<0.01f,"turned to 45°: "+ang());
        // the pinned end's own handles do nothing but still belong to the ruler
        reset(w/2,w/2,0);Set(view,"_rulerPinA",true);hl=len()/2;a0=endOf(false);b0=endOf(true);
        Check(down(scr(loc(-hl+3,0)),1),"pinned end still takes the press");move(scr(loc(-hl-200,50)));up(Point.Empty);
        near("pinned A stays",endOf(false),a0);near("B unchanged",endOf(true),b0);
        // stretching the free end of a pinned ruler leaves the pin alone
        down(scr(loc(hl-3,0)),1);move(scr(loc(hl-3+100,0)));up(Point.Empty);
        near("pinned A stays during stretch",endOf(false),a0);Check(len()>w*0.5f+50,"B stretched");
        // pinned at both ends: nothing moves
        reset(w/2,w/2,0);hl=len()/2;Set(view,"_rulerPinA",true);Set(view,"_rulerPinB",true);a0=endOf(false);b0=endOf(true);
        Check(down(scr(loc(0,0)),1),"pinned ruler still eats the press");move(scr(loc(300,300)));up(Point.Empty);
        near("A locked",endOf(false),a0);near("B locked",endOf(true),b0);
        Check(undoSteps==0,"the ruler is a helper: no undo steps ("+undoSteps+")");
        using(var tex=(Bitmap)typeof(SceneView).GetMethod("BuildRulerTexture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{900f,200f,true,2f}))
            Check(tex.Width>0,"ruler scale builds in size-slider units");
        // ── The ruler is a scene object ──
        {
            view.Camera.Mode=ViewMode.Front;view.Camera.AngleX=0;view.Camera.AngleY=0;
            canvas.Mesh=CanvasFactory.CreateSheet(4,3);
            view.RulerVisible=false;
            Check(!scene.Objects.Any(o=>o.PrimitiveType=="Ruler"),"switched off: no ruler object");
            view.RulerVisible=true;
            var grp=scene.Objects.FirstOrDefault(o=>o.PrimitiveType=="Ruler");
            Check(grp!=null&&grp.IsGroup&&grp.Children.Count==3,"switched on: a group (a bar and two plates) is in the scene");
            Check(grp.Children[0].Material.HasTexture&&grp.Children[1].Material.HasTexture&&grp.Children[2].Mesh.Faces.Count==6,"two printed plates and a bar with real thickness");
            float k0=(float)Call(view,"RulerK");
            Set(view,"_rulerC",new PointF(w*0.4f,w*k0*0.3f));Set(view,"_rulerAngle",30f);Set(view,"_rulerLen",w*0.5f);
            Call(view,"RulerApply");
            object[] rd={grp.Children[0],PointF.Empty,0f,0f};
            Call(view,"RulerReadParams",rd);
            PointF rc=(PointF)rd[1];
            Check(Distance(rc,new PointF(w*0.4f,w*k0*0.3f))<0.5f&&Math.Abs((float)rd[2]-30f)<0.05f&&Math.Abs((float)rd[3]-w*0.5f)<0.5f,"the handles' result lands in the object ("+rc+", "+rd[2]+", "+rd[3]+")");
            // the back plate lies under the face and faces the other way
            Vec3 nf=SceneGeometry.Xform.For(grp.Children[0]).ApplyNormal(new Vec3(0,0,1)),nb=SceneGeometry.Xform.For(grp.Children[1]).ApplyNormal(new Vec3(0,0,1));
            Check(Vec3.Dot(nf,nb)<-0.99f,"the back plate faces away from the face");
            // moved from outside (the Move tool): the sheet params follow
            foreach(var pl in grp.Children)pl.Position=pl.Position+new Vec3(0.5f,0.25f,0f);
            Call(view,"RulerSync");
            PointF now=(PointF)Get(view,"_rulerC");
            float wpp=4f/w;
            Check(Math.Abs(now.X-(w*0.4f+0.5f/wpp))<1f,"moving the object moves the ruler on the sheet ("+now+")");
            // lifted off the sheet it still guides along its projection; a stretch from outside is read too
            foreach(var pl in grp.Children)pl.Scale=new Vec3(pl.Scale.X*1.5f,pl.Scale.Y,pl.Scale.Z);
            Call(view,"RulerSync");
            Check(Math.Abs((float)Get(view,"_rulerLen")-w*0.75f)<1f,"scaling the object changes the ruler's length");
            // a click with the selecting tools picks the ruler from the front and from behind
            foreach(float ay in new[]{0f,16f,25f,180f,200f})
            {
                view.Camera.Mode=ViewMode.Perspective;view.Camera.AngleX=12;view.Camera.AngleY=ay;view.Camera.Distance=7f;
                Vec3 mid=grp.Children[2].Position;
                var at=Point.Round(view.Camera.Project(mid,gl.Width,gl.Height));
                var picked=(SceneObject)Call(view,"PickTopLevel",at);
                Check(picked==grp,"a click on the ruler picks it (camera "+ay+"°: got "+(picked==null?"nothing":picked.Name)+")");
            }
            view.Camera.Mode=ViewMode.Front;view.Camera.AngleX=0;view.Camera.AngleY=0;
            // lifted off the sheet with the 3D tools it stops guiding the pen, and guides again when put back
            Check((bool)Get(view,"_rulerOnSheet"),"lying on the sheet it guides");
            foreach(var pl in grp.Children)pl.Position=pl.Position+new Vec3(0f,0f,0.5f);
            Call(view,"RulerSync");
            Check(!(bool)Get(view,"_rulerOnSheet")&&Call(view,"RulerHit",new Point(500,400)).ToString()=="None","lifted away from the sheet it does not guide");
            foreach(var pl in grp.Children)pl.Position=pl.Position-new Vec3(0f,0f,0.5f);
            Call(view,"RulerSync");
            Check((bool)Get(view,"_rulerOnSheet"),"put back, it guides again");
            // it is a helper: not exported; it survives a save and a load
            Check(!Aplat.IO.ExportManager.ToSTL(scene.Objects).Contains("Ruler")&&Aplat.IO.ExportManager.ToSTL(new[]{grp}).Trim().Length<40,"the ruler is not exported");
            string tmp=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"ruler-"+Guid.NewGuid().ToString("N")+".aplat");
            try
            {
                Aplat.IO.SceneFileManager.Save(scene,tmp);
                var back=Aplat.IO.SceneFileManager.Load(tmp);
                var bg=back.Objects.FirstOrDefault(o=>o.PrimitiveType=="Ruler");
                Check(bg!=null&&bg.Children.Count==3&&bg.Children[0].Material.HasTexture&&bg.Children[1].Material.HasTexture,"the ruler comes back from a saved file with its pictures");
            }
            finally { try{System.IO.File.Delete(tmp);}catch{} }
            view.RulerVisible=false;
            Check(!scene.Objects.Any(o=>o.PrimitiveType=="Ruler"),"switched off again: the object is gone");
        }
        Console.WriteLine("PASS: "+count+" stroke cases (aspects, perspective, angles, both edges); projection round trips; front/back visibility and picking; end-handle stretch and double-click pins.");
    }
}
