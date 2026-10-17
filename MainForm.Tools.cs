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
        // ── View ──────────────────────────────────────────────────

        private void tb2D_Click(object s, EventArgs e) { ApplyView(ViewMode.Front);       }
        private void tb3D_Click(object s, EventArgs e) { ApplyView(ViewMode.Perspective); }

        // Single entry point so the 2D/3D toggle always reflects the camera mode,
        // whether changed via the buttons or the NumPad shortcuts.
        private void ApplyView(ViewMode mode)
        {
            sceneView.SetViewMode(mode);
            tb2D.Checked = mode != ViewMode.Perspective;   // Front/Top/Side are all "2D"
            tb3D.Checked = mode == ViewMode.Perspective;
            if (mode == ViewMode.Front || mode == ViewMode.Side || mode == ViewMode.Top)
                _viewOverlayMode = mode;
            CaptureViewZoomBase();
            UpdateViewOverlayState();
        }

        // ── Canvas / grid (canvas size, grid format, background) ──

        private void OpenCanvasSettings()
        {
            var canvas = FindCanvas();

            // Current canvas size (world units) from its mesh, if any.
            float curW = 4f, curH = 3f;
            Color curColor = Color.White;
            if (canvas != null)
            {
                float minX = float.MaxValue, maxX = -float.MaxValue;
                float minY = float.MaxValue, maxY = -float.MaxValue;
                foreach (var v in canvas.Mesh.Vertices)
                {
                    if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
                }
                curW = maxX - minX; curH = maxY - minY;
                curColor = canvas.Material.Color;
            }

            using (var d = new CanvasSettingsDialog(
                curW, curH, curColor,
                _scene.ShowGrid, _scene.GridExtent, _scene.GridSpacing,
                _scene.BackgroundColor, _scene.GridColor, _scene.FixedGrid))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                PushUndo(Loc.T("Холст и сетка"));

                // Canvas (size / colour) — only if a sheet exists.
                if (canvas != null)
                {
                    canvas.Mesh           = CanvasFactory.CreateSheet(d.CanvasWidth, d.CanvasHeight);
                    canvas.Position       = new Vec3(canvas.Position.X, d.CanvasHeight / 2f, canvas.Position.Z);
                    canvas.Material.Color = d.CanvasColor;
                    sceneView.Camera.Target = new Vec3(0f, d.CanvasHeight / 2f, 0f);
                }

                // Grid / background.
                _scene.GridExtent      = d.GridExtent;
                _scene.GridSpacing     = d.GridSpacing;
                _scene.BackgroundColor = d.BackgroundColor;
                _scene.GridColor       = d.GridColor;
                _scene.ShowGrid        = d.ShowGrid;
                _scene.FixedGrid       = d.FixedGrid;
                _scene.IsModified      = true;

                SyncCanvasUI();
                UpdatePropertyCoordinateFrame();
                sceneView.Refresh3D();
                UpdateList();
                UpdateTitle();
            }
        }

        private void ToggleGrid()
        {
            _scene.ShowGrid   = !_scene.ShowGrid;
            _scene.IsModified = true;
            SyncCanvasUI();
            UpdateTitle();
        }

        // Push the scene's canvas/grid settings onto the live view and the UI.
        private void SyncCanvasUI()
        {
            sceneView.ShowGrid        = _scene.ShowGrid;
            sceneView.GridExtent      = _scene.GridExtent;
            sceneView.GridSpacing     = _scene.GridSpacing;
            sceneView.BackgroundColor = _scene.BackgroundColor;
            sceneView.GridColor       = _scene.GridColor;
            sceneView.FixedGrid       = _scene.FixedGrid;
            // Light and auto-rotation live in the same scene — sync them here as well, so
            // that after opening a file / undoing an action the view matches the data.
            if (_scene.Light == null) _scene.Light = new LightSettings();
            sceneView.OutlineSelection = _scene.OutlineSelection;
            SyncAutoRotateUI();
            SyncGridUI();
        }

        // Keep the toolbar button and the menu check in step with the grid state.
        private void SyncGridUI()
        {
            tbGrid.Checked = _scene.ShowGrid;
            SetButtonActive(btnViewGrid, _scene.ShowGrid);
        }

        private void menuCanvas_Click(object s, EventArgs e) { OpenCanvasSettings(); }

        // ── Menu item state ───────────────────────────────────────
        // The menu opens rarely, so its state is computed at the moment it opens: no
        // need to poke a dozen items on every selection change, and an item is never
        // wrong about being available.

        private void menuEdit_DropDownOpening(object s, EventArgs e)
        {
            int selCount = sceneView.SelectedObjects.Count;
            bool has     = selCount > 0 || _selected != null;

            menuUndo.Enabled = _history.UndoCount > 0;
            menuRedo.Enabled = _history.RedoCount > 0;

            menuCut.Enabled       = has;
            menuCopy.Enabled      = has;
            menuDuplicate.Enabled = has;
            menuDelete.Enabled    = has;
            menuDeselect.Enabled  = has;
            menuPaste.Enabled     = HasClipboard;
            menuSelectAll.Enabled = _scene.Objects.Count > 0;

            menuGroup.Enabled   = selCount > 1;
            menuUngroup.Enabled = sceneView.SelectedObjects.Any(o => o != null && o.IsGroup)
                                  || (_selected != null && _selected.IsGroup);
        }

        private void menuObject_DropDownOpening(object s, EventArgs e)
        {
            bool has = _selected != null || sceneView.SelectedObjects.Count > 0;
            bool isText = _selected != null && _selected.PrimitiveType == "Text";

            menuEditText.Enabled   = isText;
            menuRename.Enabled     = _selected != null;
            menuDropGround.Enabled = has;
            menuResetRot.Enabled   = has;
            menuToggleVis.Enabled  = has;
        }

        private void menuView_DropDownOpening(object s, EventArgs e)
        {
            menuWire.Checked     = sceneView.ShowWireframe;
            menuVerts.Checked    = sceneView.ShowVertices;
            menuGridItem.Checked = _scene.ShowGrid;
            menuOutline.Checked  = _scene.OutlineSelection;

            ViewMode m = sceneView.Camera.Mode;
            menuViewFront.Checked = m == ViewMode.Front;
            menuViewSide.Checked  = m == ViewMode.Side;
            menuViewTop.Checked   = m == ViewMode.Top;
            menuViewPersp.Checked = m == ViewMode.Perspective;

            menuFocusSel.Enabled = sceneView.SelectedObjects.Count > 0 || _selected != null;
            menuFocusAll.Enabled = _scene.Objects.Count > 0;
        }

        private void ToggleOutlineSelection()
        {
            _scene.OutlineSelection = !_scene.OutlineSelection;
            sceneView.OutlineSelection = _scene.OutlineSelection;
            _scene.IsModified = true;
            statusLabel.Text = _scene.OutlineSelection
                ? Loc.T("Выделение: обводка по каждой грани")
                : Loc.T("Выделение: габаритная рамка вместо обводки граней");
        }

        // "Show the whole scene" — same as the Home key: frame the scene and bring the
        // zoom caption in line with the new camera position.
        private void FocusWholeScene()
        {
            sceneView.FocusAll();
            CaptureViewZoomBase();
            UpdateViewZoomDisplay();
        }

        // ── Lighting (the "sun") ──────────────────────────────────

        // The dialog edits the scene settings in place and redraws the view on every
        // slider move, so the sun is visible live. OK commits the result to the
        // history; Cancel rolls the values back by itself.
        private void menuLight_Click(object s, EventArgs e)
        {
            if (_scene.Light == null) _scene.Light = new LightSettings();
            var before = _scene.Light.Clone();

            using (var d = new LightSettingsDialog(_scene.Light, () => sceneView.Refresh3D()))
            {
                if (d.ShowDialog(this) != DialogResult.OK)
                {
                    sceneView.Refresh3D();
                    return;
                }
            }

            if (SameLight(before, _scene.Light)) return;

            // The undo snapshot is taken after the dialog, so the "before" state goes on the
            // stack: temporarily restore the old light, push, then put the new one back.
            var after = _scene.Light.Clone();
            _scene.Light = before;
            PushUndo(Loc.T("Освещение"));
            _scene.Light = after;

            _scene.IsModified = true;
            sceneView.Refresh3D();
            UpdateTitle();
            statusLabel.Text = _scene.Light.Enabled
                ? string.Format(Loc.T("Солнце: высота {0:0}°, азимут {1:0}°{2}"),
                    _scene.Light.Elevation, _scene.Light.Azimuth,
                    _scene.Light.Shadows ? Loc.T(", тень включена") : "")
                : Loc.T("Источник света выключен");
        }

        private static bool SameLight(LightSettings a, LightSettings b)
        {
            if (a == null || b == null) return a == b;
            return a.Enabled == b.Enabled && a.Specular == b.Specular &&
                   a.Shadows == b.Shadows && a.ShowSun == b.ShowSun &&
                   a.ColorArgb == b.ColorArgb && a.AmbientColorArgb == b.AmbientColorArgb &&
                   Near(a.Azimuth, b.Azimuth) && Near(a.Elevation, b.Elevation) &&
                   Near(a.Intensity, b.Intensity) && Near(a.Ambient, b.Ambient) &&
                   Near(a.ShadowStrength, b.ShadowStrength);
        }

        private static bool Near(float a, float b) { return Math.Abs(a - b) < 1e-4f; }

        // ── Auto-rotation ─────────────────────────────────────────

        private void menuSpin_Click(object s, EventArgs e) { ToggleAutoRotate(); }

        private void ToggleAutoRotate()
        {
            // Rotating the object changes the scene itself — the "before" snapshot goes
            // into the history so the accumulated degrees can be undone with one Ctrl+Z.
            if (!_scene.AutoRotate && _scene.AutoRotateObjects)
                PushUndo(Loc.T("Автовращение"));

            _scene.AutoRotate = !_scene.AutoRotate;

            // A camera orbit only makes sense in perspective: in 2D views the angle is
            // fixed by the view and there is nothing to rotate — switch automatically.
            if (_scene.AutoRotate && !_scene.AutoRotateObjects &&
                sceneView.Camera.Mode != ViewMode.Perspective)
                ApplyView(ViewMode.Perspective);

            SyncAutoRotateUI();
            _scene.IsModified = true;
            UpdateTitle();

            if (!_scene.AutoRotate)
                statusLabel.Text = Loc.T("Автовращение выключено");
            else if (_scene.AutoRotateObjects && sceneView.SelectedObjects.Count == 0)
                statusLabel.Text = Loc.T("Автовращение: крутится вся сцена (выберите объект, чтобы крутить его одного)");
            else
                statusLabel.Text = string.Format(Loc.T("Автовращение включено ({0}, {1:0}°/с)"),
                    _scene.AutoRotateObjects ? Loc.T("объект") : Loc.T("камера вокруг объекта"),
                    _scene.AutoRotateSpeed);
        }

        // Two mutually exclusive submenu items instead of a single inverted checkbox:
        // the menu shows which mode is active without guesswork.
        private void menuSpinCamera_Click(object s, EventArgs e) { SetSpinMode(false); }
        private void menuSpinObject_Click(object s, EventArgs e) { SetSpinMode(true);  }

        private void SetSpinMode(bool rotateObjects)
        {
            if (_scene.AutoRotateObjects == rotateObjects) return;
            _scene.AutoRotateObjects = rotateObjects;
            if (_scene.AutoRotate && !rotateObjects &&
                sceneView.Camera.Mode != ViewMode.Perspective)
                ApplyView(ViewMode.Perspective);
            SyncAutoRotateUI();
            _scene.IsModified = true;
            statusLabel.Text = rotateObjects
                ? Loc.T("Автовращение: крутится выбранный объект")
                : Loc.T("Автовращение: камера обходит объект");
        }

        private void menuSpinSpeed_Click(object s, EventArgs e)
        {
            var item = s as ToolStripMenuItem;
            if (item == null || !(item.Tag is float)) return;
            _scene.AutoRotateSpeed = (float)item.Tag;
            SyncAutoRotateUI();
            _scene.IsModified = true;
            statusLabel.Text = string.Format(Loc.T("Скорость вращения: {0:0}°/с"), _scene.AutoRotateSpeed);
        }

        // The only place where the scene settings are carried over to the live view and menu.
        private void SyncAutoRotateUI()
        {
            sceneView.AutoRotate        = _scene.AutoRotate;
            sceneView.AutoRotateSpeed   = _scene.AutoRotateSpeed;
            sceneView.AutoRotateObjects = _scene.AutoRotateObjects;

            menuSpin.Checked       = _scene.AutoRotate;
            menuSpinOn.Checked     = _scene.AutoRotate;
            menuSpinCamera.Checked = !_scene.AutoRotateObjects;
            menuSpinObject.Checked = _scene.AutoRotateObjects;
            foreach (ToolStripItem it in menuSpinSpeed.DropDownItems)
            {
                var mi = it as ToolStripMenuItem;
                if (mi != null && mi.Tag is float)
                    mi.Checked = Near((float)mi.Tag, _scene.AutoRotateSpeed);
            }
            UpdateSpinCard();      // the on-canvas card follows the menu / F9
            PositionOverlays();    // ...and appears or disappears with the spin
        }
        private void tbGrid_Click        (object s, EventArgs e) { ToggleGrid(); }

        // ── Undo/Redo buttons ─────────────────────────────────────

        private void tbUndo_Click(object s, EventArgs e) { Undo(); }
        private void tbRedo_Click(object s, EventArgs e) { Redo(); }

        // ── Tool mode ─────────────────────────────────────────────

        private void SetTool(ToolMode mode)
        {
            // Leaving 2D tools for a transform tool merges the live element; the cursor and the hand keep it
            // editable (it can still be picked and dragged, like in any editor).
            if (mode != ToolMode.Paint && mode != ToolMode.Select && mode != ToolMode.Hand) sceneView.CommitLive();
            sceneView.CurrentTool = mode;
            UpdateShapeStyleRow();
            tbSelect.Checked = mode == ToolMode.Select;
            tbMove.Checked   = mode == ToolMode.Move;
            tbRotate.Checked = mode == ToolMode.Rotate;
            tbScale.Checked  = mode == ToolMode.Scale;
            tbEditShape.Checked = mode == ToolMode.EditShape;
            SetButtonActive(btnCursor, mode == ToolMode.Select);

            // Leaving paint/hand for a transform tool clears the active-tile accent.
            if (mode == ToolMode.Select || mode == ToolMode.Move ||
                mode == ToolMode.Rotate || mode == ToolMode.Scale || mode == ToolMode.EditShape)
                HighlightPaintTile(null);
            UpdateKeyHints();
        }

        // Paint the chosen drawing tile in the accent colour; clears the others.
        private void HighlightPaintTile(System.Windows.Forms.Button active)
        {
            foreach (var t in PaintTiles())
            {
                var rb = t as Aplat.UI.RoundedButton;
                if (rb != null) rb.Active = (t == active);
            }
            SyncRecentToolButtons();
        }

        private System.Windows.Forms.Button PaintButtonFor(BrushKind k)
        {
            switch (k)
            {
                case BrushKind.Brush:        return btnBrush;
                case BrushKind.Pencil:       return btnPencil;
                case BrushKind.Pen:          return btnPen;
                case BrushKind.Marker:       return btnMarker;
                case BrushKind.Eyedropper:   return btnEyedropper;
                case BrushKind.Fill:         return btnFill;
                case BrushKind.Eraser:
                case BrushKind.ObjectEraser: return btnEraser;
                case BrushKind.Text2D:       return btnText2D;
                case BrushKind.Select2D:
                case BrushKind.Lasso:        return btnSelect2D;
                case BrushKind.ShapeLine:    return btnShapeLine;
                case BrushKind.ShapeRect:    return btnShapeRect;
                case BrushKind.ShapeEllipse: return btnShapeEllipse;
                case BrushKind.ShapeTriangle: return btnShapeTriangle;
                default:                     return null;
            }
        }

        // ── Arrow-key nudging of the selection ────────────────────
        private int _lastNudgeTick;

        private void NudgeSelected(int sx, int sy, bool fine)
        {
            if (sceneView.SelectedObjects.Count == 0) return;

            // Coalesce a burst of taps into a single undo entry: only push a fresh
            // snapshot when this nudge starts after a short pause.
            int now = Environment.TickCount;
            if (now - _lastNudgeTick > 500) PushUndo(Loc.T("Сдвиг"));
            _lastNudgeTick = now;

            if (sceneView.NudgeSelection(sx, sy, fine))
            {
                _scene.IsModified = true;
                UpdatePropertyCoordinateFrame();
                propPanel.SetObject(_selected);
                UpdateStatus();
                UpdateTitle();
            }
        }

        // PgUp/PgDn — vertical nudge along world Y, with the same undo coalescing
        // as the arrow keys (a burst of taps becomes one undo entry).
        private void NudgeVertical(int dir, bool fine)
        {
            var sel = sceneView.SelectedObjects;
            if (sel.Count == 0) return;
            if (sel.Any(o => o.IsLocked)) { statusLabel.Text = Loc.T("Объект заблокирован"); return; }

            int now = Environment.TickCount;
            if (now - _lastNudgeTick > 500) PushUndo(Loc.T("Сдвиг"));
            _lastNudgeTick = now;

            float step = fine ? 0.1f : 0.5f;
            foreach (var o in sel)
                SceneGeometry.Translate(o, new Vec3(0f, dir * step, 0f));
            _scene.IsModified = true;
            sceneView.Refresh3D();
            UpdatePropertyCoordinateFrame();
            propPanel.SetObject(_selected);
            UpdateStatus();
            UpdateTitle();
        }

        private void tbSelect_Click(object s, EventArgs e) { SetTool(ToolMode.Select); }
        private void tbMove_Click  (object s, EventArgs e) { SetTool(ToolMode.Move);   }
        private void tbRotate_Click(object s, EventArgs e) { SetTool(ToolMode.Rotate); }
        private void tbScale_Click (object s, EventArgs e) { SetTool(ToolMode.Scale);  }
        private void tbEditShape_Click(object s, EventArgs e) { SetTool(ToolMode.EditShape); }
        private void tbPaint_Click (object s, EventArgs e) { SetTool(ToolMode.Paint);  }

        // ── Drawing tools (brush / pencil / pen / eyedropper / fill) ──
        private void SetBrush(BrushKind kind, bool rememberRecent = true)
        {
            // A live shape turns into the picked shape (rectangle → ellipse …) and stays
            // editable; any other tool merges the live element into the canvas first.
            var live = sceneView.Live;
            if (live != null)
            {
                if (live.IsShape && IsShapeKind(kind))
                    sceneView.EditLive(i => i.Kind = Aplat.Controls.LiveItem.KindFor(kind));
                else if (Aplat.Controls.LiveItem.BrushFor(live.Kind) != kind)
                    sceneView.CommitLive();
            }
            RememberToolBeforeEyedropper(kind);
            SwitchToolSize(kind);
            sceneView.PaintKind = kind;
            SyncEraserKind(kind);
            SyncSelectKind(kind);
            UpdateShapeStyleRow();
            // Fill, Eyedropper, Text2D and the selection tool don't use the size slider — hide it.
            lblSize.Visible = trkBrushSize.Visible =
                kind != BrushKind.Fill && kind != BrushKind.Text2D && kind != BrushKind.Eyedropper && !IsSelectKind(kind);
            lblFill.Visible = trkFill.Visible = kind == BrushKind.Fill;
            SetTool(ToolMode.Paint);
            if (rememberRecent && IsRecentBrushTool(kind)) RememberRecentTool(kind);
            HighlightPaintTile(PaintButtonFor(kind));

            // Discoverability: tools with hidden mechanics announce them right away.
            if (IsShapeKind(kind))
                statusLabel.Text = Loc.T("Тяните по холсту; потом фигуру можно подогнать за точки. Shift — квадрат / круг / 45°");
            else if (kind == BrushKind.Select2D)
                statusLabel.Text = Loc.T("Выделите область рамкой — её можно двигать, растягивать и вращать. Enter — закрепить, Esc — вернуть");
            else if (kind == BrushKind.Lasso)
                statusLabel.Text = Loc.T("Обведите петлёй штрихи, фигуры или текст — они выделятся; потом двигайте, тяните за точки, вращайте. Enter — закрепить, Esc — вернуть");
            else if (kind == BrushKind.Marker)
                statusLabel.Text = Loc.T("Маркер: широкий полупрозрачный штрих — отлично для выделения");
            else if (kind == BrushKind.ObjectEraser)
                statusLabel.Text = Loc.T("Коснитесь штриха, фигуры, текста или заливки — исчезнет целиком. Ctrl+Z вернёт");
        }

        // One size slider, but every tool keeps its own value: the same px width means the
        // same thickness everywhere, yet a pen is naturally set thinner than a marker.
        private static readonly Dictionary<BrushKind, int> DefaultToolSize = new Dictionary<BrushKind, int>
        {
            { BrushKind.Brush, 28 }, { BrushKind.Pencil, 8 }, { BrushKind.Pen, 4 },
            { BrushKind.Marker, 32 }, { BrushKind.Eraser, 28 }, { BrushKind.ShapeLine, 6 },
        };
        private readonly Dictionary<BrushKind, int> _toolSize = new Dictionary<BrushKind, int>(DefaultToolSize);
        private BrushKind _sizeKey = BrushKind.Brush;

        private static BrushKind SizeKeyOf(BrushKind k)
        {
            if (k == BrushKind.ObjectEraser) return BrushKind.Eraser;
            if (k == BrushKind.ShapeRect || k == BrushKind.ShapeEllipse || k == BrushKind.ShapeTriangle) return BrushKind.ShapeLine;
            return k;
        }

        private void SwitchToolSize(BrushKind kind)
        {
            BrushKind key = SizeKeyOf(kind);
            if (!DefaultToolSize.ContainsKey(key) || key == _sizeKey) return;   // no size, or same group
            _toolSize[_sizeKey] = trkBrushSize.Value;
            _sizeKey = key;
            int v = Math.Max(trkBrushSize.Minimum, Math.Min(trkBrushSize.Maximum, _toolSize[key]));
            trkBrushSize.Value    = v;
            sceneView.BrushRadius = v;
            lblSize.Text          = string.Format(Loc.T("Размер кисти: {0}px"), v);
        }

        private void btnBrush_Click       (object s, EventArgs e) { SetBrush(BrushKind.Brush);       }
        private void btnPencil_Click      (object s, EventArgs e) { SetBrush(BrushKind.Pencil);      }
        private void btnPen_Click         (object s, EventArgs e) { SetBrush(BrushKind.Pen);         }
        private void btnMarker_Click      (object s, EventArgs e) { SetBrush(BrushKind.Marker);      }
        private void btnSelect2D_Click    (object s, EventArgs e) { SetBrush(_selectKind);           }
        private void btnEyedropper_Click  (object s, EventArgs e) { SetBrush(BrushKind.Eyedropper);  }
        private void btnFill_Click        (object s, EventArgs e) { SetBrush(BrushKind.Fill);        }
        private void btnEraser_Click      (object s, EventArgs e) { SetBrush(_eraserKind);           }

        // Which eraser the tile brings back: the one used last.
        private BrushKind _eraserKind = BrushKind.Eraser;

        // E: the eraser; pressed again while it is active, the other kind.
        private bool ToggleEraser()
        {
            // With a mouse button held, E is the camera's "fly up" (orbit + E), not a tool key.
            if (Control.MouseButtons != MouseButtons.None) return false;
            bool active = sceneView.CurrentTool == ToolMode.Paint &&
                          (sceneView.PaintKind == BrushKind.Eraser || sceneView.PaintKind == BrushKind.ObjectEraser);
            SetBrush(!active ? _eraserKind
                             : sceneView.PaintKind == BrushKind.Eraser ? BrushKind.ObjectEraser : BrushKind.Eraser);
            return true;
        }

        // The floating tile and the panel switch show the kind in use.
        private void SyncEraserKind(BrushKind kind)
        {
            if (kind != BrushKind.Eraser && kind != BrushKind.ObjectEraser) return;
            _eraserKind = kind;
            var rb = btnEraser as Aplat.UI.RoundedButton;
            if (rb != null) rb.Icon = BrushIconFor(kind);
            if (panelTips != null)
                panelTips.SetToolTip(btnEraser, kind == BrushKind.ObjectEraser
                    ? Loc.T("Ластик объектов (E): стирает штрих, фигуру, текст или заливку целиком")
                    : Loc.T("Ластик (E): стирает пиксели под кистью"));
            if (segEraserKind != null) segEraserKind.SelectedIndex = kind == BrushKind.ObjectEraser ? 1 : 0;
        }

        // One half of the eraser switch: the glyph and its word, centred together.
        private static void PaintEraserSegment(Graphics g, Rectangle r, bool selected, Aplat.UI.UiIcon icon, string label)
        {
            var font = new Font(FontName, 8.75F);
            try
            {
                const int glyph = 16, gap = 7;
                int text = Aplat.UI.TextKit.InkWidth(label, font);
                int x = r.X + (r.Width - (glyph + gap + text)) / 2;
                Color c = selected ? Aplat.UI.Theme.Accent : Aplat.UI.Theme.TextSoft;
                Aplat.UI.IconFactory.Draw(g, icon, c, new RectangleF(x, r.Y + (r.Height - glyph) / 2f, glyph, glyph));
                Aplat.UI.TextKit.Draw(g, label, font, c, new Rectangle(x + glyph + gap, r.Y, text, r.Height));
            }
            finally { font.Dispose(); }
        }
        private void btnHand_Click        (object s, EventArgs e) { SetTool(ToolMode.Hand); HighlightPaintTile(btnHand); }
        private void btnCursor_Click      (object s, EventArgs e) { SetTool(ToolMode.Select); }
        private void btnText2D_Click      (object s, EventArgs e) { SetBrush(BrushKind.Text2D);      }
        private void btnShapeLine_Click   (object s, EventArgs e) { SetBrush(BrushKind.ShapeLine);   }
        private void btnShapeRect_Click   (object s, EventArgs e) { SetBrush(BrushKind.ShapeRect);   }
        private void btnShapeEllipse_Click(object s, EventArgs e) { SetBrush(BrushKind.ShapeEllipse); }
        private void btnShapeTriangle_Click(object s, EventArgs e) { SetBrush(BrushKind.ShapeTriangle); }
        private void trkOpacity_Scroll(object s, EventArgs e)
        {
            sceneView.BrushOpacity = trkOpacity.Value;
            lblOpacity.Text = string.Format(Loc.T("Непрозрачность: {0}%"), trkOpacity.Value);
        }

        private void trkFill_Scroll(object s, EventArgs e)
        {
            sceneView.FillStrength = trkFill.Value;
            lblFill.Text = string.Format(Loc.T("Сила заливки: {0}%"), trkFill.Value);
        }

        private void btnClearCanvas_Click(object s, EventArgs e)
        {
            var canvas = FindCanvas();
            if (canvas == null) return;
            if (!canvas.Material.HasPaint)
            {
                statusLabel.Text = Loc.T("Холст уже пуст");
                return;
            }
            if (MessageBox.Show(Loc.T("Очистить весь рисунок на холсте?"), Loc.T("Очистить холст"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            PushUndo(Loc.T("Очистка холста"));
            canvas.Material.ClearPaint();
            _scene.IsModified = true;
            sceneView.Refresh3D();
            UpdateTitle();
        }

        private void btnBrushColor_Click(object s, EventArgs e)
        {
            using (var d = new ColorDialog { Color = sceneView.BrushColor, FullOpen = true })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                SetBrushSwatch(d.Color);
                sceneView.BrushColor = d.Color;
            }
        }

        // Reflect the active brush colour on the swatch button (contrasting text) and
        // ring the matching palette swatch; a custom colour rings the hue wheel.
        private void SetBrushSwatch(Color c)
        {
            segShapeStyle?.Invalidate();   // its swatches are drawn in the brush colour
            btnBrushColor.BackColor = c;
            btnBrushColor.ForeColor = Aplat.UI.Theme.ContrastText(c);

            Aplat.UI.ColorSwatch custom = null;
            bool matched = false;
            foreach (Control ctl in palettePanel.Controls)
            {
                var sw = ctl as Aplat.UI.ColorSwatch;
                if (sw == null) continue;
                if (sw.IsCustom) { custom = sw; continue; }
                bool hit = !matched && sw.BackColor.ToArgb() == c.ToArgb();
                sw.Selected = hit;
                matched |= hit;
            }
            if (custom != null) custom.Selected = !matched;
        }

        private void trkBrushSize_Scroll(object s, EventArgs e)
        {
            sceneView.BrushRadius = trkBrushSize.Value;
            UpdateSizeCaption();
        }

        // The size slider drives different things per tool, so its caption names the right one: the line or
        // outline thickness for shapes, the size of the brush / pencil / pen / marker / eraser otherwise.
        private string SizeCaption(int value)
        {
            var live = sceneView.Live;
            BrushKind k = live != null && live.IsShape ? Aplat.Controls.LiveItem.BrushFor(live.Kind) : sceneView.PaintKind;
            string format;
            switch (k)
            {
                case BrushKind.ShapeLine:                      format = Loc.T("Толщина линии: {0}px"); break;
                case BrushKind.ShapeRect:
                case BrushKind.ShapeEllipse:
                case BrushKind.ShapeTriangle:                  format = Loc.T("Толщина контура: {0}px"); break;
                case BrushKind.Pencil:                         format = Loc.T("Размер карандаша: {0}px"); break;
                case BrushKind.Pen:                            format = Loc.T("Размер пера: {0}px"); break;
                case BrushKind.Marker:                         format = Loc.T("Размер маркера: {0}px"); break;
                case BrushKind.Eraser:
                case BrushKind.ObjectEraser:                   format = Loc.T("Размер ластика: {0}px"); break;
                default:                                       format = Loc.T("Размер кисти: {0}px"); break;
            }
            return string.Format(format, value);
        }

        private void UpdateSizeCaption()
        {
            if (lblSize == null || trkBrushSize == null) return;
            lblSize.Text = SizeCaption(trkBrushSize.Value);
        }

        // Contour / fill segmented control: applies to the next shapes and restyles
        // the live one at once.
        private void SetShapeFill(Aplat.Controls.ShapeFill fill)
        {
            sceneView.ShapeFillMode = fill;
            SyncFillButtons(fill);
        }

        private void SyncFillButtons(Aplat.Controls.ShapeFill fill)
        {
            segShapeStyle.SelectedIndex = (int)fill;
            lblShapeStyle.Text = Loc.T("Вид фигуры: ") + (fill == Aplat.Controls.ShapeFill.Fill ? Loc.T("Заливка")
                               : fill == Aplat.Controls.ShapeFill.Both ? Loc.T("Контур + тон") : Loc.T("Контур"));
        }

        // The shape-look row appears only when it matters: a closed-shape tool is
        // picked, or a live rectangle / ellipse / triangle is being adjusted.
        private void UpdateShapeStyleRow()
        {
            var live = sceneView.Live;
            var k = sceneView.PaintKind;
            bool show = (live != null && live.IsClosedShape) ||
                        (live == null && sceneView.CurrentTool == ToolMode.Paint &&
                         (k == BrushKind.ShapeRect || k == BrushKind.ShapeEllipse || k == BrushKind.ShapeTriangle));
            lblShapeStyle.Visible = segShapeStyle.Visible = show;
            UpdateSizeCaption();
            if (chkEyedropperReturn != null)
                chkEyedropperReturn.Visible = live == null && sceneView.CurrentTool == ToolMode.Paint && k == BrushKind.Eyedropper;

            // ERASER: both kinds share the section (the switch between them).
            bool eraser = live == null && sceneView.CurrentTool == ToolMode.Paint &&
                          (k == BrushKind.Eraser || k == BrushKind.ObjectEraser);
            lblEraserSection.Visible = segEraserKind.Visible = eraser;

            // SELECTION: the two selection tools share the tile and this switch.
            bool select = sceneView.CurrentTool == ToolMode.Paint && IsSelectKind(k);
            lblSelectSection.Visible = segSelectKind.Visible = select;

            // TEXT: with the text tool, or while a text is live (typed or selected).
            bool text = (live != null && live.IsText) ||
                        (live == null && sceneView.CurrentTool == ToolMode.Paint && k == BrushKind.Text2D);
            foreach (Control c in new Control[] { lblTextSection, cmbTextFont, numTextSize, tglTextStyle, segTextAlign })
                c.Visible = text;
        }

        // ── TEXT panel section ⇄ the canvas text ──────────────────
        private bool _syncingText;

        // Shows the live text's format (or the format the next text will get).
        private void SyncTextSection()
        {
            if (cmbTextFont == null) return;
            string family; float pt; FontStyle style; StringAlignment align;
            sceneView.TryGetLiveTextFormat(out family, out pt, out style, out align);
            _syncingText = true;
            try
            {
                int fi = cmbTextFont.Items.IndexOf(family);
                if (fi < 0) fi = Aplat.Dialogs.TextInputDialog.FallbackFontIndex(cmbTextFont.Items, family);
                if (fi >= 0 && cmbTextFont.SelectedIndex != fi) cmbTextFont.SelectedIndex = fi;
                numTextSize.Value = (decimal)Math.Round(pt);
                tglTextStyle[0] = (style & FontStyle.Bold) != 0;
                tglTextStyle[1] = (style & FontStyle.Italic) != 0;
                tglTextStyle[2] = (style & FontStyle.Underline) != 0;
                segTextAlign.SelectedIndex = align == StringAlignment.Center ? 1 : align == StringAlignment.Far ? 2 : 0;
            }
            finally { _syncingText = false; }
        }

        // Panel → canvas: the live text restyles at once; the next text inherits it.
        private void PushTextFormat()
        {
            if (_syncingText || cmbTextFont == null) return;
            FontStyle style = (tglTextStyle[0] ? FontStyle.Bold : 0)
                            | (tglTextStyle[1] ? FontStyle.Italic : 0)
                            | (tglTextStyle[2] ? FontStyle.Underline : 0);
            StringAlignment align = segTextAlign.SelectedIndex == 1 ? StringAlignment.Center
                                  : segTextAlign.SelectedIndex == 2 ? StringAlignment.Far : StringAlignment.Near;
            sceneView.SetTextFormat(cmbTextFont.SelectedItem as string, (float)numTextSize.Value, style, align);
        }

        // A segment's picture: the shape style as a small swatch in the brush colour.
        private void PaintShapeSwatch(Graphics g, Rectangle seg, Aplat.Controls.ShapeFill mode)
        {
            Color c = Color.FromArgb(255, sceneView.BrushColor);
            var r = new Rectangle(seg.X + (seg.Width - 24) / 2, seg.Y + (seg.Height - 16) / 2, 24, 16);
            // "Both" is previewed as it looks on the white sheet: a light tint under the
            // outline (a bare 30% tint would vanish on the dark theme's track).
            if (mode != Aplat.Controls.ShapeFill.Outline)
                using (var b = new SolidBrush(mode == Aplat.Controls.ShapeFill.Both
                           ? Color.FromArgb(255, 255 - (255 - c.R) * 30 / 100, 255 - (255 - c.G) * 30 / 100, 255 - (255 - c.B) * 30 / 100)
                           : c))
                    g.FillRectangle(b, r);
            if (mode != Aplat.Controls.ShapeFill.Fill)
                using (var p = new Pen(c, 2f))
                    g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            // A near-white brush on a light pill would vanish: give it a hairline.
            if (!Aplat.UI.Theme.IsDark && c.GetBrightness() > 0.9f)
                using (var p = new Pen(Aplat.UI.Theme.Border))
                    g.DrawRectangle(p, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
        }

        private static bool IsShapeKind(BrushKind kind)
        {
            return kind == BrushKind.ShapeLine || kind == BrushKind.ShapeRect ||
                   kind == BrushKind.ShapeEllipse || kind == BrushKind.ShapeTriangle;
        }

        private static bool IsRecentBrushTool(BrushKind kind)
        {
            switch (kind)
            {
                case BrushKind.Brush:
                case BrushKind.Pencil:
                case BrushKind.Pen:
                case BrushKind.Marker:
                case BrushKind.Eyedropper:
                case BrushKind.Fill:
                case BrushKind.Eraser:
                case BrushKind.ObjectEraser:
                case BrushKind.Text2D:
                    return true;
                default:
                    return false;
            }
        }

        private void RememberRecentTool(BrushKind kind)
        {
            if (recentToolsFlow == null || !IsRecentBrushTool(kind)) return;
            _recentBrushTools.Remove(kind);
            _recentBrushTools.Insert(0, kind);
            while (_recentBrushTools.Count > 5)
                _recentBrushTools.RemoveAt(_recentBrushTools.Count - 1);
            RenderRecentTools();
        }

        private void RenderRecentTools()
        {
            if (recentToolsFlow == null || recentToolsPanel == null) return;
            recentToolsFlow.SuspendLayout();
            var stale = new List<Control>();
            foreach (Control old in recentToolsFlow.Controls) stale.Add(old);
            recentToolsFlow.Controls.Clear();
            foreach (var old in stale)
            {
                if (panelTips != null) panelTips.SetToolTip(old, null);   // release the tooltip's reference
                old.Dispose();
            }
            for (int i = 0; i < _recentBrushTools.Count; i++)
            {
                var b = CreateRecentToolButton(_recentBrushTools[i]);
                if (i == _recentBrushTools.Count - 1) b.Margin = new Padding(0);   // even inset at the bottom
                recentToolsFlow.Controls.Add(b);
                if (panelTips != null) panelTips.SetToolTip(b, RecentToolName(_recentBrushTools[i]));
            }
            recentToolsFlow.ResumeLayout();

            // 36px buttons with 4px gaps inside the 5px card inset.
            int count = Math.Max(1, _recentBrushTools.Count);
            int h = count * RecentButton + (count - 1) * RecentGap + recentToolsPanel.Padding.Vertical;
            if (_recentWindow != null)
                _recentWindow.Size = new Size(recentToolsPanel.Width + 2, h + 2); // window drives the docked panel
            else
                recentToolsPanel.Height = h;                                      // pre-init: size the panel directly
            SyncRecentToolButtons();
            PositionOverlays();
        }

        private const int RecentButton = 36, RecentGap = 4;

        private static string RecentToolName(BrushKind kind)
        {
            switch (kind)
            {
                case BrushKind.Brush:      return Loc.T("Кисть");
                case BrushKind.Pencil:     return Loc.T("Карандаш");
                case BrushKind.Pen:        return Loc.T("Ручка");
                case BrushKind.Marker:     return Loc.T("Маркер");
                case BrushKind.Eyedropper: return Loc.T("Пипетка");
                case BrushKind.Fill:       return Loc.T("Заливка");
                case BrushKind.Eraser:     return Loc.T("Ластик");
                case BrushKind.ObjectEraser: return Loc.T("Ластик объектов");
                case BrushKind.Text2D:     return Loc.T("Текст");
                default:                   return kind.ToString();
            }
        }

        private System.Windows.Forms.Button CreateRecentToolButton(BrushKind kind)
        {
            var b = new Aplat.UI.RoundedButton
            {
                Width      = RecentButton,
                Height     = RecentButton,
                IconSize   = 18,
                Icon       = BrushIconFor(kind),
                Tag        = kind,
                Radius     = Aplat.UI.Theme.RadiusButton,
                BackColor  = Aplat.UI.Theme.BtnFace,
                ForeColor  = Aplat.UI.Theme.Text,
                Flat       = true,
                IconOnly   = true,
                Text       = RecentToolName(kind),
                Margin     = new Padding(0, 0, 0, RecentGap),
                Padding    = new Padding(0),
                TextAlign  = ContentAlignment.MiddleCenter
            };
            b.Click += (s, e) => SetBrush(kind, false);
            return b;
        }

        private void SyncRecentToolButtons()
        {
            if (recentToolsFlow == null || sceneView == null) return;
            foreach (Control c in recentToolsFlow.Controls)
            {
                var b = c as Aplat.UI.RoundedButton;
                if (b == null || !(b.Tag is BrushKind)) continue;
                b.Active = sceneView.CurrentTool == ToolMode.Paint && (BrushKind)b.Tag == sceneView.PaintKind;
            }
        }

        private static Aplat.UI.UiIcon BrushIconFor(BrushKind kind)
        {
            switch (kind)
            {
                case BrushKind.Pencil:     return Aplat.UI.UiIcon.Pencil;
                case BrushKind.Pen:        return Aplat.UI.UiIcon.Pen;
                case BrushKind.Marker:     return Aplat.UI.UiIcon.Marker;
                case BrushKind.Eyedropper: return Aplat.UI.UiIcon.Eyedropper;
                case BrushKind.Fill:       return Aplat.UI.UiIcon.Fill;
                case BrushKind.Eraser:     return Aplat.UI.UiIcon.Eraser;
                case BrushKind.ObjectEraser: return Aplat.UI.UiIcon.EraserObject;
                case BrushKind.Text2D:     return Aplat.UI.UiIcon.Text;
                case BrushKind.Lasso:      return Aplat.UI.UiIcon.Lasso;
                default:                   return Aplat.UI.UiIcon.Brush;
            }
        }

        // ── 3D volumetric text ────────────────────────────────────
        private void AddText()
        {
            using (var d = new TextInputDialog())
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;

                var mesh = TextMeshBuilder.Build(d.Text3D, d.FontName, d.TextHeight);
                if (mesh.Vertices.Count == 0)
                {
                    MessageBox.Show(Loc.T("Не удалось построить объёмный текст из этой строки."),
                        Loc.T("3D текст"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                PushUndo(Loc.T("3D текст"));

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

                Vec3 spot = FindFreeSpot(minX, maxX, minZ, maxZ, FrontOffset());
                var obj = new SceneObject
                {
                    Name          = Loc.TextPrefix + d.Text3D,
                    PrimitiveType = "Text",
                    Mesh          = mesh,
                    Position      = new Vec3(spot.X, -minY, spot.Z),
                    Material      = new Material { Color = Palette[_scene.Objects.Count % Palette.Length] },
                    TextContent   = d.Text3D,
                    TextFontName  = d.FontName,
                    TextWorldSize = d.TextHeight,
                    TextDepth     = TextMeshBuilder.DefaultDepthRatio
                };
                _scene.Objects.Add(obj);
                _scene.IsModified = true;
                Select(obj);
                sceneView.Refresh3D();
                UpdateList();
                UpdateTitle();
            }
        }

        private void tbText_Click (object s, EventArgs e) { AddText(); }
        private void btnText_Click(object s, EventArgs e) { AddText(); }

        // "Edit the text…" — re-open the dialog for an existing 3D-text object and
        // rebuild its mesh, keeping position / rotation / scale / material.
        private void EditText3D()
        {
            var obj = _selected;
            if (obj == null || obj.PrimitiveType != "Text")
            {
                statusLabel.Text = Loc.T("Выберите объект «Текст», чтобы изменить его");
                return;
            }
            if (obj.IsLocked) { statusLabel.Text = Loc.T("Объект заблокирован"); return; }

            // Old files may lack the stored parameters — fall back to the name.
            string curText = !string.IsNullOrEmpty(obj.TextContent)
                ? obj.TextContent
                : Loc.TextFromName(obj.Name);

            using (var d = new TextInputDialog())
            {
                d.Preset(curText, obj.TextFontName, obj.TextWorldSize);
                if (d.ShowDialog(this) != DialogResult.OK) return;

                PushUndo(Loc.T("3D текст"));
                if (!RebuildText3D(obj, d.Text3D, d.FontName, d.TextHeight, obj.TextDepth))
                {
                    DropLastUndo();
                    MessageBox.Show(Loc.T("Не удалось построить объёмный текст из этой строки."),
                        Loc.T("3D текст"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _scene.IsModified = true;
                propPanel.SetObject(obj);
                sceneView.Refresh3D();
                UpdateList();
                UpdateTitle();
            }
        }

        // The Object tab's TEXT section edits the selected 3-D text live. Typing is
        // coalesced (the mesh is rebuilt 180 ms after the last change) and applied to
        // the object that was edited, even if the selection moves meanwhile.
        private Aplat.Controls.Text3DEdit _pendingText3D;
        private SceneObject _pendingText3DObj;
        private Timer _text3DTimer;

        private void OnText3DEdited(object sender, Aplat.Controls.Text3DEdit e)
        {
            _pendingText3D    = e;
            _pendingText3DObj = _selected;
            if (_text3DTimer == null)
            {
                _text3DTimer = new Timer { Interval = 180 };
                _text3DTimer.Tick += (s2, e2) => { _text3DTimer.Stop(); ApplyPendingText3D(); };
            }
            _text3DTimer.Stop();
            _text3DTimer.Start();
        }

        private void ApplyPendingText3D()
        {
            var e = _pendingText3D;
            var obj = _pendingText3DObj;
            _pendingText3D = null; _pendingText3DObj = null;
            if (e == null || obj == null || obj.PrimitiveType != "Text" || obj.IsLocked) return;
            if (!RebuildText3D(obj, e.Text, e.Font, e.Height, e.Depth))
            {
                statusLabel.Text = Loc.T("Этим шрифтом текст не строится — выберите другой");
                return;
            }
            _scene.IsModified = true;
            sceneView.Refresh3D();
            UpdateList();
            UpdateTitle();
        }

        // New mesh from the text parameters; position, rotation, scale and material stay.
        private static bool RebuildText3D(SceneObject obj, string text, string font, float height, float depth)
        {
            var mesh = TextMeshBuilder.Build(text, font, height, depth);
            if (mesh.Vertices.Count == 0) return false;
            obj.Mesh          = mesh;
            obj.Name          = Loc.TextPrefix + text;
            obj.TextContent   = text;
            obj.TextFontName  = font;
            obj.TextWorldSize = height;
            obj.TextDepth     = depth;
            return true;
        }

        private void tbWireframe_Click(object s, EventArgs e)
        {
            tbWireframe.Checked   = !tbWireframe.Checked;
            sceneView.ShowWireframe = tbWireframe.Checked;
        }

        private void tbVertices_Click(object s, EventArgs e)
        {
            tbVertices.Checked   = !tbVertices.Checked;
            sceneView.ShowVertices = tbVertices.Checked;
        }

        // ── Lighting ──────────────────────────────────────────────
        // Lighting control was removed from the UI; brightness is kept at the
        // scene default (maximum). This just pushes it to the renderer.
        private void SyncBrightnessUI()
        {
            sceneView.Brightness = _scene.Brightness;
        }

    }
}
