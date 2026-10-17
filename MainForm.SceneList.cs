using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using Aplat.Controls;
using Aplat.Dialogs;
using Aplat.IO;
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;

namespace Aplat
{
    public partial class MainForm : Form
    {
        // ── Scene list ────────────────────────────────────────────

        private enum LayerGlyph { None, Eye, Lock }

        // Toggle visibility of the currently selected objects (hidden ones aren't drawn).
        private void ToggleVisibilitySelected()
        {
            var sel = sceneView.SelectedObjects;
            if (sel.Count == 0) return;
            bool hide = sel.Exists(o => o.IsVisible);   // any visible → hide all, else show all
            foreach (var o in sel) o.IsVisible = !hide;
            _scene.IsModified = true;
            UpdateList();
            sceneView.Refresh3D();
            UpdateTitle();
        }

        private void listObjects_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_syncing) return;
            var objs = new List<SceneObject>();
            foreach (int i in listObjects.SelectedIndices)
                if (i >= 0 && i < _scene.Objects.Count)
                    objs.Add(_scene.Objects[i]);
            sceneView.SetSelection(objs);
            _selected = objs.Count > 0 ? objs[objs.Count - 1] : null;
            UpdatePropertyCoordinateFrame();
            propPanel.SetObject(_selected);
            UpdateLockButton();
            UpdateStatus();
        }

        // ── UI helpers ────────────────────────────────────────────

        private void UpdateTitle()
        {
            // NB: no SaveModifiedSceneNow() here — UpdateTitle runs on every mouse-move
            // during a drag/stroke, and a full scene save (XML + paint PNGs + history)
            // per move froze the UI. The 10 s autosave timer and OnFormClosing cover it.
            string name = string.IsNullOrEmpty(_scene.FilePath)
                ? _scene.Name
                : Path.GetFileNameWithoutExtension(_scene.FilePath);
            Text = string.Format("Aplat — {0}{1}", name, _scene.IsModified ? " *" : "");
        }

        private void UpdateList()
        {
            SyncRulerButton();   // the ruler is an object: deleting or undoing it changes the button
            _syncing = true;
            listObjects.BeginUpdate();
            listObjects.Items.Clear();
            foreach (var o in _scene.Objects)
            {
                string label = o.IsGroup && o.PrimitiveType != Aplat.Primitives.RulerFactory.Type ? Loc.T("[Группа] ") + o.Name : o.Name;
                var item = new ListViewItem(label);
                listObjects.Items.Add(item);
            }
            listObjects.EndUpdate();
            _syncing = false;
            SyncListSelection();
        }

        private void SyncListSelection()
        {
            if (_syncing) return;
            _syncing = true;
            foreach (ListViewItem it in listObjects.Items) it.Selected = false;
            var sel = sceneView.SelectedObjects;
            for (int i = 0; i < _scene.Objects.Count && i < listObjects.Items.Count; i++)
                if (sel.Contains(_scene.Objects[i]))
                    listObjects.Items[i].Selected = true;
            _syncing = false;
            UpdateLockButton();
        }

        // Legacy checkbox path kept inert; rows now use inline eye/lock icons.
        private void listObjects_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_syncing) return;
            int idx = e.Item.Index;
            if (idx < 0 || idx >= _scene.Objects.Count) return;
            var o = _scene.Objects[idx];
            if (o.IsVisible == e.Item.Checked) return;
            o.IsVisible = e.Item.Checked;
            _scene.IsModified = true;
            sceneView.Refresh3D();
            UpdateTitle();
        }

        // Double-click a row → rename that object.
        private void listObjects_DoubleClick(object sender, EventArgs e)
        {
            if (_selected != null) RenameSelected();
        }

        private void listObjects_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            e.DrawBackground();
        }

        // One row: [type glyph] name ··· [eye] [lock], on the same 12px inset as the
        // card header. Selection is a soft accent fill with a 2px accent bar at the
        // left edge — the outliner idiom, no boxed rows.
        private void listObjects_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= _scene.Objects.Count) { e.DrawDefault = true; return; }
            var obj = _scene.Objects[e.ItemIndex];
            var g = e.Graphics;
            var b = e.Bounds;
            bool selected = e.Item.Selected;
            Color fg = obj.IsVisible ? Aplat.UI.Theme.Text : Aplat.UI.Theme.TextMuted;

            using (var brush = new SolidBrush(selected ? Aplat.UI.Theme.AccentSoft : Aplat.UI.Theme.Card))
                g.FillRectangle(brush, b);
            if (selected)
                using (var bar = new SolidBrush(Aplat.UI.Theme.Accent))
                    g.FillRectangle(bar, b.Left, b.Top, 2, b.Height);

            var typeRect = new Rectangle(b.Left + 12, b.Top, 16, b.Height);
            DrawLayerIcon(g, LayerTypeIcon(obj), selected ? Aplat.UI.Theme.Accent : Aplat.UI.Theme.TextMuted, typeRect);

            Rectangle lockRect = LayerLockRect(b);
            Rectangle eyeRect  = LayerEyeRect(b);
            var textRect = Rectangle.FromLTRB(typeRect.Right + 8, b.Top, eyeRect.Left - 4, b.Bottom);
            Aplat.UI.TextKit.Draw(g, e.Item.Text, listObjects.Font, fg, textRect,
                Aplat.UI.HAlign.Left, ellipsis: true);

            DrawLayerIcon(g, obj.IsVisible ? Aplat.UI.UiIcon.Eye : Aplat.UI.UiIcon.EyeOff,
                obj.IsVisible ? Aplat.UI.Theme.TextSoft : Aplat.UI.Theme.TextMuted, eyeRect);
            DrawLayerIcon(g, obj.IsLocked ? Aplat.UI.UiIcon.Lock : Aplat.UI.UiIcon.Unlock,
                obj.IsLocked ? Aplat.UI.Theme.Accent : Aplat.UI.Theme.TextMuted, lockRect);
        }

        private static Aplat.UI.UiIcon LayerTypeIcon(SceneObject o)
        {
            if (o.IsGroup) return Aplat.UI.UiIcon.Group;
            switch (o.PrimitiveType)
            {
                case "Canvas":      return Aplat.UI.UiIcon.Canvas;
                case "Text":        return Aplat.UI.UiIcon.Text;
                case "Sphere":      return Aplat.UI.UiIcon.Sphere;
                case "Cylinder":    return Aplat.UI.UiIcon.Cylinder;
                case "Cone":        return Aplat.UI.UiIcon.Cone;
                case "Tetrahedron": return Aplat.UI.UiIcon.Tetrahedron;
                case "Pyramid":     return Aplat.UI.UiIcon.Pyramid;
                case "Torus":       return Aplat.UI.UiIcon.Torus;
                case "Capsule":     return Aplat.UI.UiIcon.Capsule;
                default:            return Aplat.UI.UiIcon.Cube;
            }
        }

        // "+" in the Objects header: the same shapes as Object → Add shape.
        private ContextMenuStrip _addShapeMenu;
        private ToolStripItem _addCanvasSeparator, _addCanvasItem;

        private void btnLayersAdd_Click(object sender, EventArgs e)
        {
            if (_addShapeMenu == null)
            {
                _addShapeMenu = new ContextMenuStrip
                {
                    RenderMode = ToolStripRenderMode.Professional,
                    Renderer   = new Aplat.UI.ModernToolStripRenderer(),
                    Font       = Aplat.UI.Theme.Font(9.25f)
                };
                _addShapeMenu.Items.AddRange(new ToolStripItem[] {
                    CtxItem(Loc.T("Куб"),            Aplat.UI.UiIcon.Cube,        btnCube_Click),
                    CtxItem(Loc.T("Сфера"),          Aplat.UI.UiIcon.Sphere,      btnSphere_Click),
                    CtxItem(Loc.T("Цилиндр"),        Aplat.UI.UiIcon.Cylinder,    btnCylinder_Click),
                    CtxItem(Loc.T("Конус"),          Aplat.UI.UiIcon.Cone,        btnCone_Click),
                    CtxItem(Loc.T("Пирамида"),       Aplat.UI.UiIcon.Pyramid,     btnPyramid_Click),
                    CtxItem(Loc.T("Тетраэдр"),       Aplat.UI.UiIcon.Tetrahedron, btnTetra_Click),
                    CtxItem(Loc.T("Тор"),            Aplat.UI.UiIcon.Torus,       btnTorus_Click),
                    CtxItem(Loc.T("Капсула"),        Aplat.UI.UiIcon.Capsule,     btnCapsule_Click) });
                if (Features.Text3D)
                    _addShapeMenu.Items.AddRange(new ToolStripItem[] {
                        new ToolStripSeparator(),
                        CtxItem(Loc.T("Объёмный текст…"), Aplat.UI.UiIcon.Text, btnText_Click) });

                // The sheet is an object like the others and can be deleted; this is the way
                // back. Offered only while there is none.
                _addCanvasSeparator = new ToolStripSeparator();
                _addCanvasItem = CtxItem(Loc.T("Холст"), Aplat.UI.UiIcon.Canvas, (s2, e2) => AddCanvas());
                _addShapeMenu.Items.AddRange(new ToolStripItem[] { _addCanvasSeparator, _addCanvasItem });
                _addShapeMenu.Opening += (s2, e2) =>
                    _addCanvasSeparator.Visible = _addCanvasItem.Visible = FindCanvas() == null;
            }
            _addShapeMenu.Show(btnLayersAdd, new Point(0, btnLayersAdd.Height + 2));
        }

        private void UpdateLockButton()
        {
            if (listObjects != null) listObjects.Invalidate();
        }

        private LayerGlyph HitLayerGlyph(int x, Rectangle itemBounds)
        {
            var row = new Rectangle(0, itemBounds.Top, listObjects.ClientSize.Width, itemBounds.Height);
            if (LayerEyeRect(row).Contains(x, itemBounds.Top + itemBounds.Height / 2)) return LayerGlyph.Eye;
            if (LayerLockRect(row).Contains(x, itemBounds.Top + itemBounds.Height / 2)) return LayerGlyph.Lock;
            return LayerGlyph.None;
        }

        private static Rectangle LayerEyeRect(Rectangle row)
        {
            return new Rectangle(row.Right - 58, row.Top + 4, 24, Math.Max(18, row.Height - 8));
        }

        private static Rectangle LayerLockRect(Rectangle row)
        {
            return new Rectangle(row.Right - 30, row.Top + 4, 24, Math.Max(18, row.Height - 8));
        }

        // 16px glyph centred in `rect`, drawn straight from the vector icon (crisp at
        // any DPI, always in the current theme colour).
        private static void DrawLayerIcon(Graphics g, Aplat.UI.UiIcon icon, Color color, Rectangle rect)
        {
            const int size = 16;
            Aplat.UI.IconFactory.Draw(g, icon, color,
                new RectangleF(rect.Left + (rect.Width - size) / 2f, rect.Top + (rect.Height - size) / 2f, size, size));
        }

        private void ToggleLayerGlyph(int index, LayerGlyph glyph)
        {
            if (index < 0 || index >= _scene.Objects.Count) return;
            var obj = _scene.Objects[index];
            if (glyph == LayerGlyph.Eye) obj.IsVisible = !obj.IsVisible;
            else if (glyph == LayerGlyph.Lock) obj.IsLocked = !obj.IsLocked;
            else return;

            _scene.IsModified = true;
            listObjects.Invalidate();
            if (glyph == LayerGlyph.Lock && sceneView.SelectedObjects.Contains(obj))
            {
                UpdatePropertyCoordinateFrame();
                propPanel.SetObject(_selected);
            }
            sceneView.Refresh3D();
            UpdateStatus();
            UpdateTitle();
        }

    }
}
