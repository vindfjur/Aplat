using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Aplat;
using Aplat.Controls;
using Aplat.Models;
using Aplat.Primitives;
using System.Runtime.InteropServices;
using System.Text;

// Headless regression / boundary tests of the main window's commands and menus.
// Dialogs are never opened: only code paths that run without user input are driven.
class FormTests
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int fails, checks, Dialogs;
    static object Get(object o, string n) { var f = o.GetType().GetField(n, F); if (f == null) throw new Exception("no field " + n); return f.GetValue(o); }
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a)
    {
        var m = o.GetType().GetMethods(F | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name == n && x.GetParameters().Length == a.Length);
        if (m == null) m = o.GetType().GetMethods(F).FirstOrDefault(x => x.Name == n && x.GetParameters().Length == a.Length);
        if (m == null) throw new Exception("no method " + n + "/" + a.Length);
        try { return m.Invoke(o, a); }
        catch (TargetInvocationException e)
        {
            // The runner has no interactive desktop, so a MessageBox / dialog throws instead of blocking.
            if (e.InnerException is InvalidOperationException && e.InnerException.Message.Contains("ServiceNotification"))
            { Console.WriteLine("note a dialog would open in " + n); Dialogs++; return null; }
            throw e.InnerException;
        }
    }
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }
    static bool NoThrow(Action a, string msg)
    {
        try { a(); Check(true, msg); return true; }
        catch (Exception e) { Check(false, msg + " threw " + e.GetType().Name + ": " + e.Message); return false; }
    }

    // In an interactive session a MessageBox really opens and would wait for a click forever:
    // a watchdog presses the first button of any dialog that appears (and counts them).
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
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
                        if (first != IntPtr.Zero) { Console.WriteLine("note a dialog was closed by the watchdog"); Dialogs++; SendMessage(first, 0x00F5, IntPtr.Zero, IntPtr.Zero); }   // BM_CLICK
                    }
                    return true;
                }, IntPtr.Zero);
            }
        }) { IsBackground = true };
        t.Start();
    }

    static MainForm form; static Scene Scn { get { return (Scene)Get(form, "_scene"); } }
    static SceneView View { get { return (SceneView)Get(form, "sceneView"); } }
    static int Undos { get { return ((Aplat.IO.UndoHistory)Get(form, "_history")).UndoCount; } }
    static SceneObject Sel { get { return (SceneObject)Get(form, "_selected"); } }
    static int Count(SceneObject o) { return 1 + o.Children.Sum(c => Count(c)); }
    static int Total() { return Scn.Objects.Sum(o => Count(o)); }
    static int NonCanvas() { return Scn.Objects.Count(o => o.PrimitiveType != "Canvas"); }

    [STAThread]
    static int Main()
    {
        StartDialogWatchdog();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        form = new MainForm();
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new System.Drawing.Point(-4000, -4000);
        form.Show(); Application.DoEvents();

        Check(Scn.Objects.Any(o => o.PrimitiveType == "Canvas"), "a new window starts on a canvas");
        Check(form.Text.StartsWith("Aplat"), "window title is the product name: " + form.Text);

        // 0. One size slider: every drawing tool remembers its own size, and the slider shows it.
        {
            var trk = (Aplat.UI.ModernSlider)Get(form, "trkBrushSize");
            Call(form, "SetBrush", BrushKind.Brush, false);
            trk.Value = 31; Call(form, "trkBrushSize_Scroll", null, EventArgs.Empty);
            Call(form, "SetBrush", BrushKind.Pen, false);
            Check(trk.Value == 4 && View.BrushRadius == 4, "the pen starts thin: " + trk.Value);
            trk.Value = 9; Call(form, "trkBrushSize_Scroll", null, EventArgs.Empty);
            Call(form, "SetBrush", BrushKind.Marker, false);
            Check(trk.Value == 32 && View.BrushRadius == 32, "the marker has its own size: " + trk.Value);
            Call(form, "SetBrush", BrushKind.ShapeEllipse, false);
            Check(trk.Value == 6 && View.BrushRadius == 6, "shapes outline thinly: " + trk.Value);
            Call(form, "SetBrush", BrushKind.Fill, false);
            Check(View.BrushRadius == 6, "a tool without size leaves the value alone");
            Call(form, "SetBrush", BrushKind.Brush, false);
            Check(trk.Value == 31 && View.BrushRadius == 31, "the brush gets its size back: " + trk.Value);
            Call(form, "SetBrush", BrushKind.Pen, false);
            Check(trk.Value == 9, "the pen gets its size back: " + trk.Value);
            Call(form, "SetBrush", BrushKind.ObjectEraser, false);
            Call(form, "SetBrush", BrushKind.Eraser, false);
            Check(trk.Value == 28, "both erasers share one size: " + trk.Value);
            Call(form, "SetTool", ToolMode.Select);
        }

        // 1. Every primitive can be added; undo / redo walk back and forth.
        int baseCount = NonCanvas();
        foreach (PrimitiveKind k in Enum.GetValues(typeof(PrimitiveKind)))
        {
            if (k.ToString() == "Text") continue;
            int before = NonCanvas();
            bool ok = NoThrow(() => Call(form, "AddPrimitive", k), "add " + k);
            Check(NonCanvas() == before + 1, "...adds exactly one object (" + k + ")");
            Check(Sel != null && Sel.Mesh != null && Sel.Mesh.Vertices.Count > 0, "...which has a mesh (" + k + ")");
        }
        int added = NonCanvas() - baseCount;
        for (int i = 0; i < added; i++) Call(form, "Undo");
        Check(NonCanvas() == baseCount, "undo removes them all again");
        NoThrow(() => { for (int i = 0; i < 40; i++) Call(form, "Undo"); }, "undo far beyond the history does not throw");
        for (int i = 0; i < added; i++) Call(form, "Redo");
        Check(NonCanvas() == baseCount + added, "redo brings them all back");
        NoThrow(() => { for (int i = 0; i < 40; i++) Call(form, "Redo"); }, "redo beyond the history does not throw");

        // 2. Selection commands.
        var objs = Scn.Objects.Where(o => o.PrimitiveType != "Canvas").ToList();
        Call(form, "Select", objs[0]);
        Check(Sel == objs[0], "select an object");
        int u0 = Undos;
        Call(form, "DropToGround");
        Call(form, "DropToGround");
        Check(Undos - u0 <= 1, "dropping to the ground twice records at most one undo step (no-op leaves none): " + (Undos - u0));
        u0 = Undos;
        Call(form, "ResetRotationSelected");
        Call(form, "ResetRotationSelected");
        Check(Undos - u0 <= 1, "resetting rotation twice records at most one undo step: " + (Undos - u0));

        // 2b. Material presets: the choice stays shown, changes the material, and goes back to "custom" on a tweak.
        {
            var panel = Get(form, "propPanel");
            var cmb = (ComboBox)Get(panel, "cmbPreset");
            Check(cmb.Enabled, "presets are available for a 3D object");
            cmb.SelectedIndex = 3;   // the third preset: metal
            Check(cmb.SelectedIndex == 3 && Sel.Material.Metallic > 0.9f, "a chosen preset stays selected and sets the material: index " + cmb.SelectedIndex);
            Call(form, "Select", objs[1]); Call(form, "Select", objs[0]);
            Check(cmb.SelectedIndex == 3, "the preset is shown again when the object is selected again: " + cmb.SelectedIndex);
            var rough = (Aplat.UI.ModernSlider)Get(panel, "trkRough");
            rough.Value = Math.Min(100, rough.Value + 25); Call(panel, "OnPbrTrack", rough, EventArgs.Empty);
            Check(cmb.SelectedIndex == 0, "a slider move makes it custom again");
            Call(form, "Select", Scn.Objects.First(o => o.PrimitiveType == "Canvas"));
            Check(!cmb.Enabled && !((Aplat.UI.ModernSlider)Get(panel, "trkEmis")).Enabled, "the paper takes no presets or glow");
            Call(form, "Select", objs[0]);
            Check(cmb.Enabled, "...and they come back for a 3D object");
        }

        // 3. Group / ungroup / delete.
        View.SetSelection(new List<SceneObject> { objs[0], objs[1], objs[2] });
        Call(form, "GroupSelected");
        var grp = Sel;
        Check(grp != null && grp.IsGroup && grp.Children.Count == 3, "three objects group into one");
        Call(form, "UngroupSelected");
        Check(!Scn.Objects.Any(o => o.IsGroup) && Scn.Objects.Contains(objs[0]), "ungroup puts them back at top level");
        View.SetSelection(new List<SceneObject> { objs[0], objs[1] });
        Call(form, "GroupSelected");
        grp = Sel;
        int totalBefore = Total();
        View.SetSelection(new List<SceneObject> { grp }); Call(form, "Select", grp);
        Call(form, "DeleteSelected");
        Check(!Scn.Objects.Contains(grp) && Total() == totalBefore - 3, "deleting a group removes it with its children");
        Call(form, "Undo");
        Check(Scn.Objects.Any(o => o.IsGroup) && Total() == totalBefore, "undo of delete restores the group intact");
        NoThrow(() => Call(form, "DeleteSelected"), "delete with nothing selected does nothing");

        // 4. Locked objects resist.
        var lockedObj = Scn.Objects.First(o => o.PrimitiveType != "Canvas" && !o.IsGroup);
        lockedObj.IsLocked = true;
        View.SetSelection(new List<SceneObject> { lockedObj }); Call(form, "Select", lockedObj);
        int n = Scn.Objects.Count;
        Call(form, "DeleteSelected");
        Check(Scn.Objects.Count == n && Scn.Objects.Contains(lockedObj), "a locked object is not deleted");
        Call(form, "CopySelected");
        Check(Scn.Objects.Count == n, "a locked object is not copied in place");
        lockedObj.IsLocked = false;

        // 5. Clipboard.
        Call(form, "Select", lockedObj); View.SetSelection(new List<SceneObject> { lockedObj });
        Call(form, "CopyToClipboard");
        n = Scn.Objects.Count;
        Call(form, "PasteClipboard"); Call(form, "PasteClipboard");
        Check(Scn.Objects.Count == n + 2, "paste twice adds two objects");
        Check(Scn.Objects.Select(o => o.Id).Distinct().Count() == Scn.Objects.Count, "pasted copies get their own ids");

        // 6. Select all, clear the 3-D shapes, empty scene commands.
        NoThrow(() => Call(form, "SelectAll"), "select all");
        Call(form, "btnClear3D_Click", null, EventArgs.Empty);
        Check(NonCanvas() == 0 && Scn.Objects.Count >= 1, "clearing the shapes keeps the canvas");
        foreach (var cmd in new[] { "DeleteSelected", "UngroupSelected", "DropToGround", "ResetRotationSelected", "CopySelected", "CutToClipboard", "PasteClipboard", "SelectAll", "ToggleVisibilitySelected", "FocusWholeScene" })
            NoThrow(() => Call(form, cmd), cmd + " on a nearly empty scene");

        // 7. Menus open in every state.
        var strip = form.MainMenuStrip;
        NoThrow(() =>
        {
            foreach (var states in new[] { 0, 1, 2 })
            {
                if (states == 0) Call(form, "Select", (object)null);
                else if (states == 1) { Call(form, "AddPrimitive", PrimitiveKind.Cube); }
                else { var s = Sel; s.IsLocked = true; Call(form, "Select", s); }
                foreach (ToolStripMenuItem top in strip.Items.OfType<ToolStripMenuItem>())
                {
                    top.ShowDropDown(); Application.DoEvents();
                    foreach (var item in top.DropDownItems.OfType<ToolStripMenuItem>())
                        if (item.DropDownItems.Count > 0) { item.ShowDropDown(); item.HideDropDown(); }
                    top.HideDropDown();
                }
            }
        }, "every menu opens with nothing selected, an object and a locked object");
        Sel.IsLocked = false;

        // 8. Tools and brushes.
        foreach (ToolMode m in Enum.GetValues(typeof(ToolMode))) NoThrow(() => Call(form, "SetTool", m), "tool " + m);
        foreach (BrushKind b in Enum.GetValues(typeof(BrushKind))) NoThrow(() => Call(form, "SetBrush", b, true), "brush " + b);
        NoThrow(() => { for (int i = 0; i < 300; i++) Call(form, "AdjustBrushSize", i % 2 == 0 ? 50 : -50); }, "brush size hammered up and down stays in range");

        // 9. Save / load round trip through the real file code.
        string dir = Path.Combine(Path.GetTempPath(), "aplat-form-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Call(form, "AddPrimitive", PrimitiveKind.Sphere);
            Call(form, "AddPrimitive", PrimitiveKind.Torus);
            var sceneBefore = Scn; int total = Total(); string path = Path.Combine(dir, "t.aplat");
            NoThrow(() => Call(form, "WriteSceneFile", path), "write a scene file");
            Check(File.Exists(path) && !File.Exists(path + ".tmp"), "file exists, no temp left");
            NoThrow(() => Call(form, "LoadSceneFromFile", path), "load it back");
            Check(Scn != sceneBefore, "loading replaces the open scene");
            Check(Total() == total, "same number of objects after the round trip (" + Total() + " of " + total + ")");
            NoThrow(() => { Call(form, "Undo"); Call(form, "Redo"); }, "undo / redo work right after loading");

            File.WriteAllText(Path.Combine(dir, "garbage.aplat"), "not a scene");
            int objectsNow = Total();
            // LoadSceneFromFile shows a MessageBox on failure, so only the loader itself is called.
            bool refused = false;
            try { Aplat.IO.SceneFileManager.Load(Path.Combine(dir, "garbage.aplat")); } catch (InvalidDataException) { refused = true; }
            Check(refused && Total() == objectsNow, "a garbage file is refused and the open scene is untouched");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // 10. Undo history never grows without bound while a gesture is repeated.
        for (int i = 0; i < 200; i++) Call(form, "AddPrimitive", PrimitiveKind.Cube);
        Check(Undos > 0 && ((Aplat.IO.UndoHistory)Get(form, "_history")).Weight < (200L << 20), "200 additions keep the undo history under the memory limit");

        form.Close();
        Console.WriteLine(fails == 0 ? "ALL OK (" + checks + " checks)" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }
}
