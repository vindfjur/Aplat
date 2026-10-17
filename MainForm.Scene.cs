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
        // ── Add primitives ────────────────────────────────────────

        private SceneObject FindCanvas()
        {
            foreach (var o in _scene.Objects)
                if (o.PrimitiveType == "Canvas") return o;
            return null;
        }

        // The sheet can be deleted like any layer; this brings a fresh blank one back
        // (there is only ever one). It goes under everything else, where the first one lived.
        private void AddCanvas()
        {
            if (FindCanvas() != null) { statusLabel.Text = Loc.T("Холст уже есть"); return; }
            PushUndo(Loc.T("Холст"));
            var canvas = NewCanvasObject();
            _scene.Objects.Insert(0, canvas);
            _scene.IsModified = true;
            UpdatePropertyCoordinateFrame();
            sceneView.Refresh3D();
            UpdateList();
            Select(canvas);
            UpdateStatus();
            UpdateTitle();
            statusLabel.Text = Loc.T("Чистый холст добавлен");
        }

        private bool TryGetCanvasFrame(out float left, out float top, out float width, out float height)
        {
            left = -2f; top = 3f; width = 4f; height = 3f;
            var canvas = FindCanvas();
            if (canvas == null || canvas.Mesh == null || canvas.Mesh.Vertices.Count == 0) return false;

            float minX = float.MaxValue, maxX = -float.MaxValue;
            float minY = float.MaxValue, maxY = -float.MaxValue;
            foreach (var v in canvas.Mesh.Vertices)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
            }
            if (minX == float.MaxValue) return false;

            left = canvas.Position.X + minX;
            top = canvas.Position.Y + maxY;
            width = maxX - minX;
            height = maxY - minY;
            return width > 0f && height > 0f;
        }

        private void UpdatePropertyCoordinateFrame()
        {
            if (propPanel == null) return;
            if (TryGetCanvasFrame(out float left, out float top, out float width, out float height))
                propPanel.SetCanvasFrame(left, top, width, height);
        }

        // New shapes spawn in front of the canvas (+Z) so they aren't hidden behind
        // the white sheet in the default front view.
        private float FrontOffset()
        {
            return FindCanvas() != null ? 2f : 0f;
        }

        private void AddPrimitive(PrimitiveKind kind)
        {
            PushUndo(Loc.T("Новая фигура"));
            var mesh = PrimitiveFactory.Create(kind);

            // Local bounds of the fresh mesh (untransformed).
            float minX = float.MaxValue, maxX = -float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue, maxZ = -float.MaxValue;
            foreach (var v in mesh.Vertices)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
                if (v.Y < minY) minY = v.Y;
            }
            if (minY == float.MaxValue) { minX = maxX = minZ = maxZ = minY = 0f; }

            // Lift so the lowest point rests on the ground (y=0), and find a free
            // spot on the XZ plane so the new shape doesn't sit inside another.
            Vec3 spot = FindFreeSpot(minX, maxX, minZ, maxZ, FrontOffset());

            var obj = new SceneObject
            {
                Name          = PrimitiveFactory.KindName(kind),
                PrimitiveType = kind.ToString(),
                Mesh          = mesh,
                Position      = new Vec3(spot.X, -minY, spot.Z),
                Material      = new Material { Color = Palette[_scene.Objects.Count % Palette.Length] }
            };
            _scene.Objects.Add(obj);
            _scene.IsModified = true;
            Select(obj);
            sceneView.Refresh3D();
            UpdateList();
            UpdateTitle();
        }

        // Scan outward on the XZ plane for a position where the new object's
        // footprint (given by its local X/Z extents) clears every existing
        // object's footprint by a small gap, so shapes spawn side by side.
        private Vec3 FindFreeSpot(float minX, float maxX, float minZ, float maxZ, float zBias)
        {
            const float gap  = 0.3f;
            float halfW = (maxX - minX) / 2f + gap;
            float halfD = (maxZ - minZ) / 2f + gap;
            float cx    = (maxX + minX) / 2f;
            float cz    = (maxZ + minZ) / 2f;

            // Existing footprints (world XZ AABBs).
            var boxes = new List<RectangleF>();
            foreach (var o in _scene.Objects)
                if (SceneGeometry.WorldBounds(o, out Vec3 bmin, out Vec3 bmax))
                    boxes.Add(RectangleF.FromLTRB(bmin.X, bmin.Z, bmax.X, bmax.Z));

            float step = Math.Max(halfW, halfD) * 2f + 0.5f;

            // Spiral of candidate centres: origin first, then expanding rings.
            for (int ring = 0; ring < 40; ring++)
            {
                for (int gx = -ring; gx <= ring; gx++)
                for (int gz = -ring; gz <= ring; gz++)
                {
                    if (ring > 0 && Math.Abs(gx) != ring && Math.Abs(gz) != ring)
                        continue;   // only the outer shell of this ring

                    float px = gx * step;
                    float pz = gz * step + zBias;      // bias keeps the footprint check consistent
                    var cand = RectangleF.FromLTRB(
                        px + cx - halfW, pz + cz - halfD,
                        px + cx + halfW, pz + cz + halfD);

                    bool clear = true;
                    foreach (var b in boxes)
                        if (cand.IntersectsWith(b)) { clear = false; break; }

                    if (clear) return new Vec3(px, 0f, pz);
                }
            }
            return Vec3.Zero;
        }

        private void btnCube_Click    (object s, EventArgs e) { AddPrimitive(PrimitiveKind.Cube);        }
        private void btnSphere_Click  (object s, EventArgs e) { AddPrimitive(PrimitiveKind.Sphere);      }
        private void btnCylinder_Click(object s, EventArgs e) { AddPrimitive(PrimitiveKind.Cylinder);    }
        private void btnCone_Click    (object s, EventArgs e) { AddPrimitive(PrimitiveKind.Cone);        }
        private void btnTetra_Click   (object s, EventArgs e) { AddPrimitive(PrimitiveKind.Tetrahedron); }
        private void btnPyramid_Click (object s, EventArgs e) { AddPrimitive(PrimitiveKind.Pyramid);     }
        private void btnTorus_Click   (object s, EventArgs e) { AddPrimitive(PrimitiveKind.Torus);       }
        private void btnCapsule_Click (object s, EventArgs e) { AddPrimitive(PrimitiveKind.Capsule);     }

        // Quick palette swatch → set the brush colour (and arm the brush).
        private void paletteSwatch_Click(object s, EventArgs e)
        {
            var b = s as Button;
            if (b == null) return;
            SetBrushSwatch(b.BackColor);
            sceneView.BrushColor = b.BackColor;
            if (sceneView.CurrentTool != ToolMode.Paint) SetBrush(sceneView.PaintKind);
        }

        private void btnClear3D_Click(object s, EventArgs e)
        {
            var keep = _scene.Objects.Where(o => o.PrimitiveType == "Canvas").ToList();
            if (keep.Count == _scene.Objects.Count) return;
            PushUndo(Loc.T("Очистка сцены"));
            _scene.Objects.Clear();
            _scene.Objects.AddRange(keep);
            _scene.IsModified = true;
            Select(null);
            sceneView.Refresh3D();
            UpdateList();
            UpdateStatus();
            UpdateTitle();
        }

        // ── Edit operations ───────────────────────────────────────

        private void DeleteSelected()
        {
            var sel = sceneView.SelectedObjects.ToList();
            if (sel.Count == 0 && _selected != null) sel.Add(_selected);
            sel = sel.Where(o => o != null && !o.IsLocked).ToList();
            if (sel.Count == 0) return;
            PushUndo(Loc.T("Удаление"));
            foreach (var o in sel) RemoveObject(_scene.Objects, o);
            Select(null);
            _scene.IsModified = true;
            sceneView.Refresh3D();
            UpdateList();
            UpdateTitle();
        }

        private static bool RemoveObject(List<SceneObject> list, SceneObject target)
        {
            if (list.Remove(target)) return true;
            foreach (var o in list) if (RemoveObject(o.Children, target)) return true;
            return false;
        }

        private void CopySelected()
        {
            if (_selected == null) return;
            if (_selected.IsLocked) { statusLabel.Text = Loc.T("Объект заблокирован"); return; }
            PushUndo(Loc.T("Копия"));
            var clone = _selected.Clone();
            // Spawn the copy one grid step away and snapped to round coordinates.
            const float step = 0.5f;
            clone.Position = new Vec3(Snap(_selected.Position.X + step, step),
                                      _selected.Position.Y,
                                      Snap(_selected.Position.Z + step, step));
            int idx = _scene.Objects.IndexOf(_selected);
            if (idx >= 0) _scene.Objects.Insert(idx + 1, clone);
            else          _scene.Objects.Add(clone);
            _scene.IsModified = true;
            Select(clone);
            sceneView.Refresh3D();
            UpdateList();
            UpdateTitle();
        }

        // ── Object clipboard ──────────────────────────────────────
        // Ctrl+C and Ctrl+D used to do the same thing — spawn a copy right next to the
        // original. That broke the expectation: there was no way to copy in one place
        // and paste in another. Now Ctrl+C / Ctrl+X put clones on the clipboard, Ctrl+V
        // takes them out, and Ctrl+D stays a quick "right here" duplicate.
        private readonly List<SceneObject> _clipboard = new List<SceneObject>();

        private void CopyToClipboard() { FillClipboard(false); }
        private void CutToClipboard()  { FillClipboard(true);  }

        private void FillClipboard(bool cut)
        {
            var sel = sceneView.SelectedObjects.ToList();
            if (sel.Count == 0 && _selected != null) sel.Add(_selected);
            sel = sel.Where(o => o != null).ToList();
            if (sel.Count == 0) { statusLabel.Text = Loc.T("Нечего копировать: объект не выбран"); return; }

            if (cut && sel.Any(o => o.IsLocked))
            {
                statusLabel.Text = Loc.T("Объект заблокирован");
                return;
            }

            _clipboard.Clear();
            // The clipboard holds independent copies: if the original is changed or
            // deleted afterwards, exactly what was copied gets pasted.
            foreach (var o in sel) _clipboard.Add(o.Clone());

            if (cut)
            {
                PushUndo(Loc.T("Вырезание"));
                foreach (var o in sel) RemoveObject(_scene.Objects, o);
                Select(null);
                _scene.IsModified = true;
                sceneView.Refresh3D();
                UpdateList();
                UpdateTitle();
            }
            statusLabel.Text = string.Format(cut ? Loc.T("Вырезано объектов: {0}") : Loc.T("Скопировано объектов: {0}"),
                _clipboard.Count);
        }

        private void PasteClipboard()
        {
            if (_clipboard.Count == 0) { statusLabel.Text = Loc.T("Буфер пуст"); return; }
            PushUndo(Loc.T("Вставка"));

            const float step = 0.5f;
            var added = new List<SceneObject>();
            foreach (var src in _clipboard)
            {
                var c = src.Clone();
                // Offset by one grid step so the pasted copy does not hide inside the original.
                c.Position = new Vec3(Snap(c.Position.X + step, step), c.Position.Y,
                                      Snap(c.Position.Z + step, step));
                _scene.Objects.Add(c);
                added.Add(c);
            }
            _scene.IsModified = true;
            sceneView.SetSelection(added);
            _selected = added[added.Count - 1];
            propPanel.SetObject(_selected);
            sceneView.Refresh3D();
            UpdateList();
            UpdateTitle();
            statusLabel.Text = string.Format(Loc.T("Вставлено объектов: {0}"), added.Count);
        }

        private bool HasClipboard { get { return _clipboard.Count > 0; } }

        private static float Snap(float v, float step)
        {
            return step > 0f ? (float)Math.Round(v / step) * step : v;
        }

        // Alt + drag a gizmo duplicates the selection in place, then the drag moves the
        // clones. Called synchronously by SceneView before the transform starts.
        private void DuplicateSelectionInPlace()
        {
            var sel = sceneView.SelectedObjects.ToList();
            if (sel.Count == 0) return;
            if (sel.Any(o => o.IsLocked)) { statusLabel.Text = Loc.T("Объект заблокирован"); return; }
            PushUndo(Loc.T("Дублирование"));
            var clones = new List<SceneObject>();
            foreach (var o in sel)
            {
                var c = o.Clone();
                c.Position = o.Position;     // exact overlap; the drag pulls it out
                int idx = _scene.Objects.IndexOf(o);
                if (idx >= 0) _scene.Objects.Insert(idx + 1, c);
                else          _scene.Objects.Add(c);
                clones.Add(c);
            }
            _scene.IsModified = true;
            sceneView.SetSelection(clones);
            _selected = clones[clones.Count - 1];
            UpdatePropertyCoordinateFrame();
            propPanel.SetObject(_selected);
            UpdateList();
            UpdateTitle();
        }

        // Rename the primary selected object — used by double-click and the
        // right-click "Rename" command.
        private void RenameSelected()
        {
            if (_selected == null) return;
            if (_selected.IsLocked) { statusLabel.Text = Loc.T("Объект заблокирован"); return; }
            string name = PromptText(Loc.T("Переименовать объект"), Loc.T("Новое имя:"), _selected.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            _selected.Name = name.Trim();
            _scene.IsModified = true;
            UpdateList();
            UpdatePropertyCoordinateFrame();
            propPanel.SetObject(_selected);
            UpdateTitle();
        }

        // Small modal text prompt (no extra dialog file needed).
        private string PromptText(string title, string label, string initial)
        {
            using (var dlg = new Form())
            {
                dlg.Text            = title;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition   = FormStartPosition.CenterParent;
                dlg.MaximizeBox     = false;
                dlg.MinimizeBox     = false;
                dlg.ClientSize      = new Size(320, 120);
                Aplat.UI.ThemeManager.StyleDialog(dlg);

                var lbl = new Label { Text = label, Location = new Point(14, 14), AutoSize = true };
                var txt = new TextBox { Text = initial ?? "", Location = new Point(14, 38), Width = 292 };
                var ok = new Button { Text = Loc.T("ОК"), DialogResult = DialogResult.OK,
                    Location = new Point(150, 76), Width = 72, FlatStyle = FlatStyle.System };
                var cancel = new Button { Text = Loc.T("Отмена"), DialogResult = DialogResult.Cancel,
                    Location = new Point(232, 76), Width = 74, FlatStyle = FlatStyle.System };

                dlg.Controls.Add(lbl);
                dlg.Controls.Add(txt);
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                txt.SelectAll();

                return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
            }
        }

        private void GroupSelected()
        {
            var sel = _scene.Objects.Where(o => sceneView.SelectedObjects.Contains(o)).ToList();
            if (sel.Any(o => o.IsLocked))
            {
                statusLabel.Text = Loc.T("Сначала разблокируйте выбранные объекты");
                return;
            }
            if (sel.Count < 2)
            {
                MessageBox.Show(Loc.T("Выберите хотя бы 2 объекта (Ctrl+клик в сцене или в списке)."),
                    Loc.T("Группировка"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            PushUndo(Loc.T("Группировка"));
            var grp = new SceneObject { Name = Loc.T("Группа"), IsGroup = true };
            foreach (var o in sel) { _scene.Objects.Remove(o); grp.Children.Add(o); }
            grp.Position = SceneGeometry.Center(grp);
            _scene.Objects.Add(grp);
            _scene.IsModified = true;
            Select(grp);
            sceneView.Refresh3D();
            UpdateList();
            UpdateTitle();
        }

        private void UngroupSelected()
        {
            var grp = _selected;
            if (grp == null || !grp.IsGroup) return;
            if (grp.IsLocked) { statusLabel.Text = Loc.T("Объект заблокирован"); return; }
            PushUndo(Loc.T("Разгруппировка"));
            int idx = _scene.Objects.IndexOf(grp);
            if (idx < 0) idx = _scene.Objects.Count;
            var kids = grp.Children.ToList();
            foreach (var c in kids) _scene.Objects.Insert(idx++, c);
            _scene.Objects.Remove(grp);
            _scene.IsModified = true;
            sceneView.SetSelection(kids);
            _selected = kids.Count > 0 ? kids[kids.Count - 1] : null;
            UpdatePropertyCoordinateFrame();
            propPanel.SetObject(_selected);
            sceneView.Refresh3D();
            UpdateList();
            UpdateTitle();
            UpdateStatus();
        }

        // "To the floor" — settle the selected objects so they rest on the ground (y=0).
        private void DropToGround()
        {
            var sel = sceneView.SelectedObjects.Where(o => o != null && !o.IsLocked).ToList();
            if (sel.Count == 0) { statusLabel.Text = Loc.T("Нечего опускать — выберите объект"); return; }
            PushUndo(Loc.T("На пол"));
            bool moved = false;
            foreach (var o in sel)
            {
                if (!SceneGeometry.WorldBounds(o, out Vec3 mn, out Vec3 _)) continue;
                if (Math.Abs(mn.Y) < 1e-5f) continue;
                SceneGeometry.Translate(o, new Vec3(0f, -mn.Y, 0f));
                moved = true;
            }
            if (!moved) { DropLastUndo(); statusLabel.Text = Loc.T("Объекты уже на полу"); return; }
            _scene.IsModified = true;
            UpdatePropertyCoordinateFrame();
            propPanel.SetObject(_selected);
            sceneView.Refresh3D();
            UpdateStatus();
            UpdateTitle();
        }

        private static bool IsZero(Vec3 v) { return v.X == 0f && v.Y == 0f && v.Z == 0f; }

        // "Reset rotation" — return the selection to its unrotated orientation.
        private void ResetRotationSelected()
        {
            var sel = sceneView.SelectedObjects.Where(o => o != null && !o.IsLocked).ToList();
            if (sel.Count == 0) return;
            if (sel.All(o => IsZero(o.Rotation) && SceneGeometry.Leaves(o).All(l => IsZero(l.Rotation)))) return;   // nothing to reset: no undo step
            PushUndo(Loc.T("Сброс поворота"));
            foreach (var o in sel)
            {
                o.Rotation = Vec3.Zero;
                foreach (var leaf in SceneGeometry.Leaves(o)) leaf.Rotation = Vec3.Zero;
            }
            _scene.IsModified = true;
            propPanel.SetObject(_selected);
            sceneView.Refresh3D();
            UpdateStatus();
            UpdateTitle();
        }

        // Ctrl+A — select every top-level object in the scene.
        private void SelectAll()
        {
            if (_scene.Objects.Count == 0) return;
            sceneView.SetSelection(_scene.Objects);
            _selected = _scene.Objects[_scene.Objects.Count - 1];
            UpdatePropertyCoordinateFrame();
            propPanel.SetObject(_selected);
            SyncListSelection();
            UpdateLockButton();
            UpdateStatus();
        }

        // [ / ] hotkeys: resize the brush, keeping the slider and label in sync.
        private void AdjustBrushSize(int delta)
        {
            int v = Math.Max(trkBrushSize.Minimum,
                    Math.Min(trkBrushSize.Maximum, trkBrushSize.Value + delta));
            trkBrushSize.Value    = v;
            sceneView.BrushRadius = v;
            UpdateSizeCaption();
            statusLabel.Text      = SizeCaption(v);
        }

        // Files dropped onto the viewport: a .aplat opens as a scene; an image becomes
        // the texture of the selected object (or the canvas when nothing is selected).
        private void HandleDroppedFiles(string[] files)
        {
            string path = files[0];
            string ext  = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".aplat")
            {
                if (!ConfirmDiscard()) return;
                LoadSceneFromFile(path);
                return;
            }

            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
            {
                var target = _selected ?? FindCanvas();
                if (target == null)  { statusLabel.Text = Loc.T("Нет объекта для текстуры — выберите объект или добавьте холст"); return; }
                if (target.IsLocked) { statusLabel.Text = Loc.T("Объект заблокирован"); return; }

                try { using (Material.LoadBitmap(path)) { } }   // verify it decodes
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.T("Не удалось загрузить изображение:\n") + ex.Message,
                        Loc.T("Текстура"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                PushUndo(Loc.T("Текстура"));
                foreach (var leaf in SceneGeometry.Leaves(target))
                    if (leaf.PrimitiveType != "Text")   // text meshes can't be textured
                        leaf.Material.TexturePath = path;
                _scene.IsModified = true;
                propPanel.SetObject(_selected);
                sceneView.Refresh3D();
                UpdateTitle();
                statusLabel.Text = Loc.T("Текстура применена: ") + Path.GetFileName(path);
                return;
            }

            statusLabel.Text = Loc.T("Неподдерживаемый файл: ") + Path.GetFileName(path);
        }

        private void Select(SceneObject obj)
        {
            _selected = obj;
            sceneView.SetSelectedExternal(obj);
            UpdatePropertyCoordinateFrame();
            propPanel.SetObject(obj);
            SyncListSelection();
            UpdateLockButton();
            UpdateStatus();
        }

        // ── File operations ────────────────────────────────────────

        private void NewScene()
        {
            if (!ConfirmNewScene()) return;
            _history.Clear();
            _scene = new Scene();
            ApplyShadowsPreference(_scene);
            sceneView.Scene = _scene;
            SyncBrightnessUI();
            SyncCanvasUI();
            UpdatePropertyCoordinateFrame();
            Select(null);
            UpdateList();
            UpdateTitle();
            UpdateUndoButtons();
            SeedCanvas();
        }

        private void OpenScene()
        {
            if (!ConfirmDiscard()) return;
            using (var d = new OpenFileDialog { Filter = "Aplat Scene (*.aplat)|*.aplat" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                LoadSceneFromFile(d.FileName);
            }
        }

        // Shared by the Open dialog, the recent-files menu and drag&drop.
        private void LoadSceneFromFile(string fileName)
        {
            try
            {
                var history = new UndoHistory();
                _scene = SceneFileManager.Load(fileName, history);   // the file's undo history goes straight in
                _history = history;
                sceneView.Scene = _scene;
                SyncBrightnessUI();
                SyncCanvasUI();
                ApplyViewState();      // restore camera + view toggles
                UpdatePropertyCoordinateFrame();
                Select(null);
                UpdateList();
                UpdateTitle();
                UpdateUndoButtons();
                AddRecentFile(fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("Ошибка открытия:\n") + ex.Message, "Aplat");
            }
        }

        private void SaveScene(bool saveAs = false)
        {
            string path = _scene.FilePath;
            if (string.IsNullOrEmpty(path) || saveAs)
            {
                using (var d = new SaveFileDialog
                {
                    Filter   = "Aplat Scene (*.aplat)|*.aplat",
                    FileName = _scene.Name
                })
                {
                    if (d.ShowDialog() != DialogResult.OK) return;
                    path = d.FileName;
                }
            }
            try
            {
                CaptureViewState();
                WriteSceneFile(path);
                AddRecentFile(path);
                UpdateTitle();
            }
            catch (Exception ex) { MessageBox.Show(Loc.T("Ошибка сохранения:\n") + ex.Message, "Aplat"); }
        }

        private bool ConfirmDiscard()
        {
            if (!_scene.IsModified) return true;
            var r = MessageBox.Show(Loc.T("Сцена изменена. Сохранить?"), "Aplat",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Yes)   { SaveScene(); return !_scene.IsModified; }
            if (r == DialogResult.No)    return true;
            return false;
        }

        // Like ConfirmDiscard, but also asks before replacing a *saved* scene that
        // still contains objects (confirm New even when nothing is unsaved).
        private bool ConfirmNewScene()
        {
            if (_scene.IsModified) return ConfirmDiscard();
            if (_scene.Objects.Count == 0) return true;
            return MessageBox.Show(Loc.T("Создать новую сцену? Текущая сцена будет закрыта."),
                Loc.T("Новая сцена"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        // ── View state persistence ────────────────────────────────
        private void CaptureViewState()
        {
            var c = sceneView.Camera;
            _scene.HasCameraState   = true;
            _scene.CameraMode       = (int)c.Mode;
            _scene.CameraAngleX     = c.AngleX;
            _scene.CameraAngleY     = c.AngleY;
            _scene.CameraDistance   = c.Distance;
            _scene.CameraOrthoScale = c.OrthoScale;
            _scene.CameraTarget     = c.Target;
            _scene.ShowWireframe    = sceneView.ShowWireframe;
            _scene.ShowVertices     = sceneView.ShowVertices;
            _scene.OutlineSelection = sceneView.OutlineSelection;
        }

        private void ApplyViewState()
        {
            sceneView.ShowWireframe    = _scene.ShowWireframe;
            sceneView.ShowVertices     = _scene.ShowVertices;
            sceneView.OutlineSelection = _scene.OutlineSelection;
            tbWireframe.Checked     = _scene.ShowWireframe;
            tbVertices.Checked      = _scene.ShowVertices;

            if (!_scene.HasCameraState) return;
            var c = sceneView.Camera;
            c.Mode       = (ViewMode)_scene.CameraMode;
            c.AngleX     = _scene.CameraAngleX;
            c.AngleY     = _scene.CameraAngleY;
            c.Distance   = _scene.CameraDistance;
            c.OrthoScale = _scene.CameraOrthoScale;
            c.Target     = _scene.CameraTarget;
            tb2D.Checked = c.Mode != ViewMode.Perspective;
            tb3D.Checked = c.Mode == ViewMode.Perspective;
            if (c.Mode == ViewMode.Front || c.Mode == ViewMode.Side || c.Mode == ViewMode.Top)
                _viewOverlayMode = c.Mode;
            CaptureViewZoomBase();
            UpdateViewOverlayState();
            sceneView.Refresh3D();
        }

    }
}
