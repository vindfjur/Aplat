using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using Aplat.Controls;
using Aplat.Models;
using Aplat.UI;

// Headless regression / boundary tests of the Object inspector (PropertiesPanel).
// Run on Windows against the built Release exe: see run.ps1.
class PanelTests
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int fails, checks;
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static void Call(object o, string n, params object[] a) { o.GetType().GetMethod(n, F).Invoke(o, a); }
    static void Check(bool ok, string msg) { checks++; if (!ok) { fails++; Console.WriteLine("FAIL " + msg); } else Console.WriteLine("ok   " + msg); }
    static bool Near(float a, float b, float eps = 1e-4f) { return Math.Abs(a - b) <= eps; }

    static SceneObject Shape(string name, float x = 0, float y = 0)
    {
        var o = new SceneObject { Name = name, PrimitiveType = "Cube", Position = new Vec3(x, y, 0) };
        return o;
    }

    [STAThread]
    static int Main()
    {
        var panel = new PropertiesPanel();
        int changed = 0, started = 0;
        panel.PropertiesChanged += (s, e) => changed++;
        panel.EditStarting += (s, e) => started++;
        var name = (TextBox)Get(panel, "txtName");
        var numRX = (DragNumber)Get(panel, "numRX");
        var numPX = (DragNumber)Get(panel, "numPX");

        // 1. Leaving the name box must not touch the transform (values are shown rounded / clamped).
        var o = Shape("A", -3.3333f, 1.2345f);
        o.Rotation = new Vec3(400f, 12.4f, 0f);
        o.Scale = new Vec3(1.2345f, 12f, 0.005f);
        var p0 = o.Position; var r0 = o.Rotation; var s0 = o.Scale;
        panel.SetObject(o);
        Call(panel, "txtName_Leave", null, EventArgs.Empty);
        Check(Near(o.Position.X, p0.X) && Near(o.Position.Y, p0.Y), "focus leaving the name box keeps position (incl. outside the canvas)");
        Check(Near(o.Rotation.X, r0.X) && Near(o.Rotation.Y, r0.Y), "...keeps rotation (400 deg is out of the field's range)");
        Check(Near(o.Scale.X, s0.X) && Near(o.Scale.Y, s0.Y) && Near(o.Scale.Z, s0.Z), "...keeps scale (1.2345, 12, 0.005)");
        name.Text = "Renamed";
        Check(o.Name == "Renamed", "renaming sets the name");
        Check(Near(o.Scale.X, s0.X) && Near(o.Position.X, p0.X), "...and nothing else");

        // 2. Editing one field changes only that field.
        panel.SetObject(o);
        numRX.Value = 90;
        Check(Near(o.Rotation.X, 90f), "rotation X edit applies");
        Check(Near(o.Rotation.Y, r0.Y) && Near(o.Scale.X, s0.X) && Near(o.Position.Y, p0.Y), "...without rewriting the other fields");

        // 3. Group: touching one slider must not flatten the other properties of the leaves.
        var a = Shape("a"); a.Material.Roughness = 0.2f; a.Material.Metallic = 0.9f; a.Material.Opacity = 1f;
        var b = Shape("b"); b.Material.Roughness = 0.8f; b.Material.Metallic = 0.1f; b.Material.Opacity = 1f;
        var g = new SceneObject { Name = "G", IsGroup = true, PrimitiveType = "Group" };
        g.Children.Add(a); g.Children.Add(b);
        panel.SetObject(g);
        var opacity = (ModernSlider)Get(panel, "trkOpacity");
        opacity.Value = 50;
        Call(panel, "OnPbrTrack", opacity, EventArgs.Empty);
        Check(Near(a.Material.Opacity, 0.5f) && Near(b.Material.Opacity, 0.5f), "group: opacity slider reaches every leaf");
        Check(Near(b.Material.Roughness, 0.8f) && Near(b.Material.Metallic, 0.1f), "group: other leaf keeps its own roughness / metallic");

        // 4. Odd objects never throw nor leave the panel stuck in its loading state.
        var empty = new SceneObject { Name = "empty", IsGroup = true, PrimitiveType = "Group", Material = null };
        bool threw = false;
        try { panel.SetObject(empty); } catch (Exception) { threw = true; }
        Check(!threw, "an empty group with no material loads");
        panel.SetObject(o);
        numRX.Value = 45;
        Check(Near(o.Rotation.X, 45f), "the panel still works after an odd object (not stuck locked)");
        panel.SetObject(null);
        panel.SetObject(o);
        Check(Get(panel, "_obj") == o, "null selection then an object");

        // 5. Locked objects are read-only.
        var locked = Shape("L"); locked.IsLocked = true; locked.Rotation = new Vec3(10, 0, 0);
        panel.SetObject(locked);
        numRX.Value = 77;
        name.Text = "x";
        Check(Near(locked.Rotation.X, 10f) && locked.Name == "L", "locked object ignores edits");
        Check(!((Control)Get(panel, "_alphaMode")).Enabled && !((Control)Get(panel, "_pointLight")).Enabled, "locked: surface menu disabled");

        // 6. Transparency / light menu.
        var obj = Shape("S");
        panel.SetObject(obj);
        var alpha = (ComboBox)Get(panel, "_alphaMode");
        var cutoff = (DragNumber)Get(panel, "_cutoff");
        var pointLight = (CheckBox)Get(panel, "_pointLight");
        var power = (DragNumber)Get(panel, "_power");
        var range = (DragNumber)Get(panel, "_range");
        var flicker = (CheckBox)Get(panel, "_flicker");
        var lightColor = (Control)Get(panel, "_lightColor");
        Check(alpha.SelectedIndex == 0 && !cutoff.Enabled, "defaults: blend mode, cutoff disabled");
        Check(!power.Enabled && !range.Enabled && !flicker.Enabled && !lightColor.Enabled, "defaults: light controls disabled until the light is on");
        Check(lightColor.BackColor == Theme.BtnFace, "light off: the colour button has the neutral disabled look, not a bright swatch");
        alpha.SelectedIndex = 2;
        Check(obj.Material.AlphaMode == TextureAlphaMode.Cutout && cutoff.Enabled, "cut-out mode enables the threshold");
        cutoff.Value = 250;
        Check(cutoff.Value == 100 && Near(obj.Material.AlphaCutoff, 1f), "threshold above 100 % is clamped");
        cutoff.Value = -5;
        Check(cutoff.Value == 0 && Near(obj.Material.AlphaCutoff, 0f), "threshold below 0 % is clamped");
        alpha.SelectedIndex = 1;
        Check(!cutoff.Enabled, "opaque mode disables the threshold");
        pointLight.Checked = true;
        Check(obj.Material.LightEnabled && power.Enabled && range.Enabled && flicker.Enabled && lightColor.Enabled, "light on enables its controls");
        power.Value = 99; range.Value = 0;
        Check(Near(obj.Material.LightPower, 8f) && Near(obj.Material.LightRange, 0.1f), "power / radius are clamped to 0..8 and 0.1..30");
        Check(lightColor.BackColor != Theme.BtnFace || obj.Material.LightColor == Theme.BtnFace, "light on: the colour button shows the light's colour");
        flicker.Checked = true;
        Check(obj.Material.LightFlicker, "flicker applies");
        panel.SetObject(Shape("other"));
        Check(!pointLight.Checked && !flicker.Checked && !power.Enabled, "selecting another object reloads the light state");
        panel.SetObject(obj);
        Check(pointLight.Checked && flicker.Checked && power.Enabled, "...and back again");
        var before = started;
        pointLight.Checked = false;
        Check(!obj.Material.LightEnabled && !power.Enabled && started > before, "light off disables controls and records one undo step");

        // 7. Group light: a toggle reaches every leaf, a leaf already lit is the reference.
        panel.SetObject(g);
        pointLight.Checked = true;
        Check(a.Material.LightEnabled && b.Material.LightEnabled, "group: light on for every leaf");
        pointLight.Checked = false;
        Check(!a.Material.LightEnabled && !b.Material.LightEnabled, "group: light off for every leaf");

        // 8. Position out of the canvas frame stays out of it after unrelated edits.
        panel.SetCanvasFrame(-2f, 1.5f, 4f, 3f);
        var far = Shape("far", -5f, 4f);
        panel.SetObject(far);
        numRX.Value = 10;
        Check(Near(far.Position.X, -5f, 0.01f) && Near(far.Position.Y, 4f, 0.01f), "object outside the canvas frame is not pulled to its edge by a rotation edit");

        // 9. A stored 3-D text object (the feature is hidden, old scenes may still hold it) must still load.
        var t = new SceneObject { Name = "Текст: hi", PrimitiveType = "Text", TextContent = "hi", TextFontName = "NoSuchFont", TextWorldSize = 1000f, TextDepth = 1e-6f };
        threw = false;
        try { panel.SetObject(t); } catch (Exception) { threw = true; }
        Check(!threw, "a stored 3-D text object with absurd size / depth loads");

        // 10. Layout: at several panel widths nothing overlaps and nothing sticks out sideways.
        var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
        host.Controls.Add(panel);
        panel.Dock = DockStyle.Fill;
        foreach (int w in new[] { 240, 280, 320, 400 })
        foreach (int h in new[] { 300, 700 })
        {
            host.ClientSize = new System.Drawing.Size(w, h);
            host.Show(); panel.SetObject(Shape("L" + w)); host.PerformLayout(); Application.DoEvents();
            var content = ((ScrollHost)Get(panel, "panelProps")).Content;
            var list = new List<Control>();
            foreach (Control c in content.Controls) if (c.Visible && c.Width > 0 && c.Height > 0) list.Add(c);
            bool overlap = false, outside = false; string who = "";
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Right > content.ClientSize.Width + 1 || list[i].Left < 0) { outside = true; who = list[i].GetType().Name + ":" + list[i].Text; }
                for (int j = i + 1; j < list.Count; j++)
                    if (list[i].Bounds.IntersectsWith(list[j].Bounds)) { overlap = true; who = list[i].GetType().Name + "/" + list[j].GetType().Name + " " + list[i].Text + "|" + list[j].Text; }
            }
            Check(!overlap, "panel " + w + "x" + h + ": no controls overlap " + who);
            Check(!outside, "panel " + w + "x" + h + ": nothing sticks out sideways " + who);
        }
        host.Controls.Remove(panel); host.Close();

        // 11. Screenshots of the panel (only when APLAT_SHOTS names a folder) for eyeballing.
        string shots = Environment.GetEnvironmentVariable("APLAT_SHOTS");
        if (!string.IsNullOrEmpty(shots))
        {
            System.IO.Directory.CreateDirectory(shots);
            host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
            host.Controls.Add(panel); panel.Dock = DockStyle.Fill;
            host.ClientSize = new System.Drawing.Size(340, 1500);
            host.Show();
            var lit = Shape("lit"); lit.Material.AlphaMode = TextureAlphaMode.Cutout; lit.Material.LightEnabled = true; lit.Material.LightFlicker = true;
            var lockedShot = Shape("locked"); lockedShot.IsLocked = true;
            var scenarios = new Dictionary<string, SceneObject> { { "cube", Shape("cube") }, { "lit", lit }, { "locked", lockedShot }, { "group", g } };
            foreach (var kv in scenarios)
            {
                panel.SetObject(kv.Value); host.PerformLayout(); Application.DoEvents();
                var content = ((ScrollHost)Get(panel, "panelProps")).Content;
                host.ClientSize = new System.Drawing.Size(340, Math.Max(400, Math.Min(2600, content.PreferredSize.Height + 80)));
                Application.DoEvents();
                using (var bmp = new System.Drawing.Bitmap(host.ClientSize.Width, host.ClientSize.Height))
                {
                    panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(System.IO.Path.Combine(shots, kv.Key + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            host.Controls.Remove(panel); host.Close();
        }

        Console.WriteLine(fails == 0 ? "ALL OK (" + checks + " checks)" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }
}
