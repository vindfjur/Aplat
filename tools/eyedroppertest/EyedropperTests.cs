using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;
using Aplat;
using Aplat.Controls;

// The eyedropper's "return to the previous tool" option, on the real main window (not shown on screen).
//   tools\paneltest\run.ps1 -Source tools\eyedroppertest\EyedropperTests.cs
class EyedropperTests
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int fails;
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a)
    {
        var m = o.GetType().GetMethods(F).First(x => x.Name == n && x.GetParameters().Length == a.Length);
        try { return m.Invoke(o, a); } catch (TargetInvocationException e) { throw e.InnerException; }
    }
    static void Check(bool ok, string msg) { Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }


    // In an interactive session a MessageBox really opens and would wait for a click forever: press its first button.
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    static void StartDialogWatchdog()
    {
        var t = new System.Threading.Thread(() =>
        {
            uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            while (true)
            {
                System.Threading.Thread.Sleep(200);
                EnumWindows((h, l) =>
                {
                    uint pid; GetWindowThreadProcessId(h, out pid);
                    var cls = new StringBuilder(64); GetClassName(h, cls, 64);
                    if (pid == me && cls.ToString() == "#32770" && IsWindowVisible(h))
                    {
                        IntPtr first = IntPtr.Zero;
                        EnumChildWindows(h, (c, l2) => { var cc = new StringBuilder(64); GetClassName(c, cc, 64); if (cc.ToString() == "Button" && first == IntPtr.Zero) first = c; return true; }, IntPtr.Zero);
                        if (first != IntPtr.Zero) { Console.WriteLine("note a dialog was closed by the watchdog"); SendMessage(first, 0x00F5, IntPtr.Zero, IntPtr.Zero); }
                    }
                    return true;
                }, IntPtr.Zero);
            }
        }) { IsBackground = true };
        t.Start();
    }

    [STAThread]
    static int Main()
    {
        try { return Run(); }
        catch (Exception e) { Console.WriteLine("FAIL crashed: " + e); return 2; }
    }

    static int Run()
    {
        Console.Out.Flush(); StartDialogWatchdog();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var form = new MainForm();
        form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-4000, -4000);
        form.Show(); Application.DoEvents();
        var view = (SceneView)Get(form, "sceneView");
        var chk = (CheckBox)Get(form, "chkEyedropperReturn");

        Set(form, "_eyedropperReturn", false);
        Call(form, "SetBrush", BrushKind.Pencil, true);
        Call(form, "SetBrush", BrushKind.Eyedropper, true);
        Check(chk.Visible, "the option is shown with the eyedropper");
        Call(form, "AfterEyedropperPick");
        Check(view.PaintKind == BrushKind.Eyedropper, "option off: the eyedropper stays after a pick");

        Set(form, "_eyedropperReturn", true);
        Call(form, "SetBrush", BrushKind.Marker, true);
        Call(form, "SetBrush", BrushKind.Eyedropper, true);
        Call(form, "AfterEyedropperPick");
        Check(view.PaintKind == BrushKind.Marker && view.CurrentTool == ToolMode.Paint, "option on: back to the marker after a pick (" + view.PaintKind + ")");
        Check(!chk.Visible, "the option is hidden once another tool is active");

        Call(form, "SetBrush", BrushKind.Eyedropper, true);
        Call(form, "AfterEyedropperPick");
        Call(form, "AfterEyedropperPick");
        Check(view.PaintKind == BrushKind.Marker, "a second pick does not jump anywhere else");

        Call(form, "SetTool", ToolMode.Move);
        Call(form, "SetBrush", BrushKind.Eyedropper, true);
        Call(form, "AfterEyedropperPick");
        Check(view.CurrentTool == ToolMode.Move, "from the Move tool the pick returns to Move (" + view.CurrentTool + ")");

        Call(form, "SetBrush", BrushKind.Eyedropper, true);
        Call(form, "SetBrush", BrushKind.Eyedropper, true);
        Call(form, "AfterEyedropperPick");
        Check(view.PaintKind != BrushKind.Eyedropper || view.CurrentTool != ToolMode.Paint, "choosing the eyedropper twice still returns to the earlier tool");

        ((Aplat.Models.Scene)Get(form, "_scene")).IsModified = false;   // closing must not ask to save
        form.Close();
        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
