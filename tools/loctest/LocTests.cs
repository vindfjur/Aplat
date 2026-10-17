using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Aplat;
using Aplat.Controls;
using Aplat.Dialogs;
using Aplat.Models;

// The interface in English: no Russian text may be left anywhere the user can see it, and Russian
// must stay exactly as written. Windows are built (not opened) and every control, menu item,
// tool tip, list column and combo item is read.
class LocTests
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }

    // The names of the languages are shown in themselves, on purpose.
    static bool Allowed(string s) { return s == "Русский"; }

    static void Collect(ToolStripItem item, List<string> sink, ToolTip tips)
    {
        sink.Add(item.Text); sink.Add(item.ToolTipText);
        var menu = item as ToolStripDropDownItem;
        if (menu != null) foreach (ToolStripItem sub in menu.DropDownItems) Collect(sub, sink, tips);
    }

    static void Collect(Control c, List<string> sink)
    {
        sink.Add(c.Text);
        var combo = c as ComboBox; if (combo != null) foreach (var it in combo.Items) sink.Add(it as string);
        var list = c as ListView; if (list != null) foreach (ColumnHeader h in list.Columns) sink.Add(h.Text);
        var strip = c as ToolStrip; if (strip != null) foreach (ToolStripItem it in strip.Items) Collect(it, sink, null);
        var menuStrip = c as MenuStrip; if (menuStrip != null) foreach (ToolStripItem it in menuStrip.Items) Collect(it, sink, null);
        foreach (Control ch in c.Controls) Collect(ch, sink);
        // tool tips live in ToolTip components kept in fields of the form
        foreach (var f in c.GetType().GetFields(F | BindingFlags.DeclaredOnly))
        {
            var tip = f.GetValue(c) as ToolTip;
            if (tip == null) continue;
            foreach (Control x in AllControls(c)) { try { sink.Add(tip.GetToolTip(x)); } catch { } }
        }
    }

    static IEnumerable<Control> AllControls(Control root)
    {
        yield return root;
        foreach (Control c in root.Controls) foreach (var d in AllControls(c)) yield return d;
    }

    static void NoRussian(string what, IEnumerable<string> texts)
    {
        var bad = texts.Where(t => !string.IsNullOrEmpty(t) && Loc.HasCyrillic(t) && !Allowed(t)).Distinct().ToList();
        Check(bad.Count == 0, what + ": no Russian text left" + (bad.Count == 0 ? "" : " — " + string.Join(" | ", bad.Take(8))));
    }

    // With the environment variable SHOTS_OUT naming an existing folder, pictures of the English windows are
    // written there.
    static readonly string ShotDir = Environment.GetEnvironmentVariable("SHOTS_OUT");
    static void Shot(Control c, string name)
    {
        if (string.IsNullOrEmpty(ShotDir) || !System.IO.Directory.Exists(ShotDir)) return;
        try
        {
            using (var bmp = new Bitmap(c.Width, c.Height))
            {
                c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                bmp.Save(System.IO.Path.Combine(ShotDir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch (Exception e) { Console.WriteLine("note no picture of " + name + ": " + e.Message); }
    }


    // ── layout: does a text fit its control? (measured the way the control would draw it) ──
    static List<string> Overflows(Control root)
    {
        var res = new List<string>();
        foreach (Control c in AllControls(root))
        {
            if (!c.Visible || string.IsNullOrWhiteSpace(c.Text) || c is Form || c is TextBoxBase || c is TabPage || c is TabControl) continue;
            if (c.Width < 4 || c.Height < 4) continue;
            var box = c.ClientSize;
            var need = TextRenderer.MeasureText(c.Text, c.Font, new Size(Math.Max(1, box.Width - 6), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            var one = TextRenderer.MeasureText(c.Text, c.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (Environment.GetEnvironmentVariable("LOC_DEBUG") != null && one.Width >= box.Width - 8) Console.WriteLine("dbg " + c.GetType().Name + " \"" + c.Text + "\" text " + one.Width + " box " + box.Width + " autosize " + c.AutoSize);
            bool wide = !c.Text.Contains(" ") && one.Width > box.Width;   // one long word cannot wrap, it is cut
            if (need.Height > box.Height + 3 || wide) res.Add(c.GetType().Name + " \"" + c.Text + "\" needs " + (wide ? one.Width + "px wide, has " + box.Width : need.Height + "px, has " + box.Height));
        }
        return res;
    }

    // The window is built and laid out in the language; every right-hand tab and several selections are visited.
    static List<string> LayoutProblems(string tag)
    {
        var all = new List<string>();
        MainForm form = new MainForm();
        try
        {
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0, 0); form.ClientSize = new Size(1400, 900);
            form.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
            var tabs = (TabControl)typeof(MainForm).GetField("tabsTools", F).GetValue(form);
            var panel = (PropertiesPanel)typeof(MainForm).GetField("propPanel", F).GetValue(form);
            // the 2D tab changes with the brush: every kind that brings its own controls
            var setTool = typeof(MainForm).GetMethods(F).First(m => m.Name == "SetTool" && m.GetParameters().Length == 1);
            var setBrush = typeof(MainForm).GetMethods(F).First(m => m.Name == "SetBrush" && m.GetParameters().Length == 2);
            tabs.SelectedIndex = 0;
            foreach (var kind in new[] { BrushKind.Brush, BrushKind.Eraser, BrushKind.ObjectEraser, BrushKind.Text2D, BrushKind.Select2D, BrushKind.Lasso, BrushKind.ShapeRect, BrushKind.Fill })
            {
                setTool.Invoke(form, new object[] { ToolMode.Paint }); setBrush.Invoke(form, new object[] { kind, true }); Application.DoEvents();
                Shot(form, tag + "-brush-" + kind);
                foreach (var o in Overflows(form)) all.Add("brush " + kind + ": " + o);
            }
            var objs = new Dictionary<string, SceneObject>();
            objs["cube"] = new SceneObject { Name = "Cube", PrimitiveType = "Cube", Mesh = Aplat.Primitives.PrimitiveFactory.CreateCube(1f), Material = new Material() };
            var lit = new SceneObject { Name = "Lamp", PrimitiveType = "Sphere", Mesh = Aplat.Primitives.PrimitiveFactory.CreateCube(1f), Material = new Material { LightEnabled = true } };
            objs["lit"] = lit;
            objs["locked"] = new SceneObject { Name = "Locked", PrimitiveType = "Cube", IsLocked = true, Mesh = Aplat.Primitives.PrimitiveFactory.CreateCube(1f), Material = new Material() };
            objs["text"] = new SceneObject { Name = Loc.TextPrefix + "Hi", PrimitiveType = "Text3D", TextContent = "Hi", Mesh = Aplat.Primitives.PrimitiveFactory.CreateCube(1f), Material = new Material() };
            var grp = new SceneObject { Name = "Group", IsGroup = true }; grp.Children.Add(objs["cube"].Clone());
            objs["group"] = grp;
            objs["none"] = null;
            foreach (var kv in objs)
                for (int tab = 0; tab < tabs.TabPages.Count; tab++)
                {
                    panel.SetObject(kv.Value); tabs.SelectedIndex = tab; Application.DoEvents();
                    string name = tag + "-" + kv.Key + "-tab" + tab;
                    Shot(form, name);
                    if (tab == 2 && kv.Value != null)
                    {
                        var pp = typeof(PropertiesPanel).GetField("panelProps", F).GetValue(panel);
                        var content = pp.GetType().GetProperty("Content").GetValue(pp, null) as Control;
                        if (content != null) Shot(content, name + "-full");
                    }
                    foreach (var o in Overflows(form)) all.Add(kv.Key + "/tab" + tab + ": " + o);
                }
        }
        finally { form.Dispose(); }
        return all.Distinct().ToList();
    }

    [STAThread]
    static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // ── Russian is untouched ──
        Loc.Set(Lang.Russian);
        Check(Loc.T("Файл") == "Файл" && Loc.F("Объектов: {0}", 3) == "Объектов: 3", "in Russian the text is returned as written");

        // ── the table itself ──
        Loc.Set(Lang.English);
        var table = Loc.EnglishEntries;
        Check(table.Count > 400, "the English table is filled (" + table.Count + " entries)");
        Check(table.All(kv => !string.IsNullOrWhiteSpace(kv.Value)), "no English entry is empty");
        var stillRussian = table.Where(kv => Loc.HasCyrillic(kv.Value)).Select(kv => kv.Key).ToList();
        Check(stillRussian.Count == 0, "no English entry contains Russian letters" + (stillRussian.Count == 0 ? "" : ": " + string.Join(" | ", stillRussian.Take(5))));
        Check(Loc.T("Файл") == "File" && Loc.F("Объектов на сцене: {0}", 3) == "Objects in the scene: 3", "a known text is translated, a format works");
        Check(Loc.T("нет такого текста") == "нет такого текста" && Loc.Missed.Contains("нет такого текста"), "an unknown text falls back and is recorded as missed");
        Check(Loc.T("") == "" && Loc.T(null) == null, "empty and null pass through");
        Check(Loc.TextFromName("Text: abc") == "abc" && Loc.TextFromName("Текст: abc") == "abc" && Loc.TextFromName("Cube") == "" && Loc.TextFromName(null) == "",
            "a text object's words are found under either name prefix");

        // ── the main window ──
        MainForm form = null;
        try
        {
            form = new MainForm();
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0, 0); form.ClientSize = new Size(1400, 900);
            form.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(400); Application.DoEvents();
            Shot(form, "main");
            var texts = new List<string>(); Collect(form, texts);
            Check(texts.Count(t => !string.IsNullOrEmpty(t)) > 150, "the main window has plenty of text to check (" + texts.Count(t => !string.IsNullOrEmpty(t)) + ")");
            NoRussian("main window", texts);
            var items = new List<string>();
            foreach (var f in typeof(MainForm).GetFields(F))
            {
                var tsi = f.GetValue(form) as ToolStripItem; if (tsi != null) Collect(tsi, items, null);
                var tip = f.GetValue(form) as ToolStrip; if (tip != null) foreach (ToolStripItem it in tip.Items) Collect(it, items, null);
            }
            NoRussian("menus and toolbars", items);
            var lang = typeof(MainForm).GetField("menuLangEn", F).GetValue(form) as ToolStripMenuItem;
            Check(lang != null && lang.Checked, "English is ticked in the Language menu");
            var panel = typeof(MainForm).GetField("propPanel", F).GetValue(form) as Control;
            var panelTexts = new List<string>(); Collect(panel, panelTexts);
            NoRussian("inspector panel", panelTexts);
            var sel = new SceneObject { Name = "Cube", PrimitiveType = "Cube", Mesh = Aplat.Primitives.PrimitiveFactory.CreateCube(1f), Material = new Material() };
            ((PropertiesPanel)panel).SetObject(sel);
            var panelTexts2 = new List<string>(); Collect(panel, panelTexts2);
            NoRussian("inspector panel with an object", panelTexts2);
            Application.DoEvents(); Shot(form, "main-with-object");
            Check(Aplat.Models.Material.PresetNames.Length == 12 && ((ComboBox)AllControls(panel).First(c => c is ComboBox && ((ComboBox)c).Items.Contains(Loc.T("Пластик")))).Items.Count == 13,
                "the material presets are shown in English, and keep their stable ids");
        }
        finally { if (form != null) form.Dispose(); }

        // ── layout: English must not overflow where Russian does not ──
        Loc.Set(Lang.Russian); var ruLayout = LayoutProblems("ru");
        Loc.Set(Lang.English); var enLayout = LayoutProblems("en");
        var strip = new Func<string, string>(x => x.Substring(x.IndexOf(": ") + 2));
        var ruSet = new HashSet<string>(ruLayout.Select(strip));
        var onlyEn = enLayout.Where(x => !ruSet.Contains(strip(x))).ToList();
        Console.WriteLine("note layout: " + ruLayout.Count + " tight texts in Russian, " + enLayout.Count + " in English");
        foreach (var x in onlyEn) Console.WriteLine("LAYOUT " + x);
        Check(onlyEn.Count == 0, "no text overflows its control in English that fits in Russian" + (onlyEn.Count == 0 ? "" : " (" + onlyEn.Count + ")"));

        // ── dialogs ──
        var dialogs = new Dictionary<string, Func<Form>>
        {
            { "hotkeys", () => new HotkeysDialog() },
            { "panels", () => new PanelSettingsDialog() },
            { "text input", () => new TextInputDialog() },
            { "canvas and grid", () => new CanvasSettingsDialog(10, 10, Color.White, true, 5, 0.5f, Color.Gray, Color.Black, false) },
            { "light", () => new LightSettingsDialog(new LightSettings(), null) },
        };
        foreach (var d in dialogs)
        {
            Form dlg = null;
            try { dlg = d.Value(); dlg.Show(); Application.DoEvents(); Shot(dlg, "dlg-" + d.Key.Replace(" ", "-")); dlg.Hide(); var t = new List<string>(); Collect(dlg, t); NoRussian("dialog " + d.Key, t); }
            catch (Exception e) { Check(false, "dialog " + d.Key + " builds: " + e.GetType().Name + " " + e.Message); }
            finally { if (dlg != null) dlg.Dispose(); }
        }

        // ── everything that was asked for had a translation ──
        var missed = Loc.Missed.Where(m => m != "нет такого текста").ToList();
        Check(missed.Count == 0, "no text was missing from the table" + (missed.Count == 0 ? "" : ": " + string.Join(" | ", missed.Take(10))));

        Console.WriteLine(fails == 0 ? "ALL OK (" + checks + " checks)" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }
}
