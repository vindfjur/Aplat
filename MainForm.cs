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
        private Scene _scene = new Scene();
        private SceneObject _selected;
        private bool _syncing;
        private bool _savingModifiedScene;

        private UndoHistory _history = new UndoHistory();

        private readonly System.Windows.Forms.Timer _autosaveTimer = new System.Windows.Forms.Timer();

        private ContextMenuStrip _ctxScene;   // right-click object actions (viewport + list)
        private readonly List<BrushKind> _recentBrushTools = new List<BrushKind>();
        // Floating overlay cards, each hosted in its own layered OverlayWindow.
        private sealed class OverlayInfo
        {
            public Aplat.UI.OverlayWindow Window;
            public string Key;       // stable id in panels.txt
            public string Name;      // label for the menu and the Panels dialog
            public bool  Enabled = true;
            public Aplat.UI.PanelPlacement Placement;   // docked to an anchor, or free
            public Aplat.UI.PanelPlacement Default;     // what "reset layout" returns to
            public ToolStripMenuItem MenuItem;          // View → Panels check item
            public Func<bool> Active;                   // card appears only while this holds (null: always)
        }
        private readonly List<OverlayInfo> _overlays = new List<OverlayInfo>();
        private Aplat.UI.OverlayWindow _recentWindow;
        private bool _overlaysReady;
        private double _overlayOpacity = 0.92;
        private float _viewZoomBase = 1f;
        private bool _syncingViewZoom;
        private ViewMode _viewOverlayMode = ViewMode.Front;
        private bool _guidesVisible = true;   // smart guides: on by default

        private static readonly Color[] Palette =
        {
            Color.DodgerBlue, Color.Tomato, Color.MediumSeaGreen,
            Color.Gold, Color.MediumPurple, Color.DarkOrange,
            Color.SteelBlue, Color.Coral
        };

        public MainForm()
        {
            InitializeComponent();
            BuildLayout();              // hand-built UI (kept out of InitializeComponent
                                        // so the WinForms designer stays error-free)
            InstallEditorShortcuts();
            LoadRecentFiles();
            LoadWindowPlacement();      // open where the user left the window last time
            toolbarDock.Load(ToolbarsPath);   // toolbars where the user left them
            ApplyShadowsPreference(_scene);   // the remembered Shadows switch
            sceneView.Scene = _scene;
            UpdateKeyHints();
            sceneView.FilesDropped += (s, files) => HandleDroppedFiles(files);
            sceneView.BrushWheel   += (s, d) => AdjustBrushSize(d);
            sceneView.ShowGuides    = _guidesVisible;   // smart guides on by default
            sceneView.ObjectSelected    += OnObjectSelected;
            sceneView.TransformStarted  += (s, e) => PushUndo(ViewportActionLabel());
            sceneView.ObjectTransformed += (s, e) => { _scene.IsModified = true; UpdatePropertyCoordinateFrame(); propPanel.SetObject(_selected); UpdateStatus(); UpdateTitle(); };
            sceneView.ViewChanged       += (s, e) => UpdateViewZoomDisplay();
            sceneView.DuplicateForDrag  += (s, e) => DuplicateSelectionInPlace();
            propPanel.PropertiesChanged += OnPropertiesChanged;
            propPanel.EditStarting      += (s, e) => PushUndo(Loc.T("Свойства"));   // one undo step per inspector gesture
            propPanel.Text3DEdited      += OnText3DEdited;
            sceneView.Notify += (s, msg) =>
            {
                statusLabel.Text = msg;
                UpdateViewSpeedLabel();
            };
            sceneView.BrushColorPicked += (s, c) => { SetBrushSwatch(c); AfterEyedropperPick(); };
            SetBrushSwatch(sceneView.BrushColor);   // swatch + palette ring match the real brush
            sceneView.Text2DClicked += OnText2DClicked;
            // Live shapes / text / cut-outs (editable until merged into the canvas).
            sceneView.LiveChanged += (s, e) =>
            {
                var live = sceneView.Live;
                if (live != null && live.IsClosedShape) SyncFillButtons(live.Fill);
                SyncTextSection();
                UpdateShapeStyleRow();
                UpdateKeyHints();
                UpdateTitle();
                UpdateSelectionBar();
            };
            sceneView.LiveDragStarted += (s, e) => HideSelectionBar();
            sceneView.RulerChanged += (s, e) => { UpdateList(); };
            sceneView.LiveCancelled         += (s, e) => DropLastUndo();
            sceneView.ViewAxisClicked += (s, mode) => ApplyView(mode);   // navigation gizmo: click
            sceneView.NavLeftOrthographic += (s, e) =>                   // navigation gizmo: drag
            {
                tb2D.Checked = false;
                tb3D.Checked = true;
                CaptureViewZoomBase();
                UpdateViewOverlayState();
            };
            SyncBrightnessUI();
            SyncCanvasUI();
            SyncTextSection();
            UpdatePropertyCoordinateFrame();
            UpdateList();
            UpdateTitle();
            UpdateUndoButtons();
            SeedCanvas();          // canvas-first: start on a sheet, brush ready
            CaptureViewZoomBase();
            RememberRecentTool(BrushKind.Brush);
            UpdateViewOverlayState();

            this.Shown += (s, e) => { InitOverlays(); toolbarDock.ShowFloats(); StartUpdateCheck(); };

            // Theme: colour this window now and on every switch; follow-ups that
            // depend on scene state (viewport colour, data-coloured buttons) run in
            // OnThemeChanged.
            Aplat.UI.ThemeManager.Attach(this);
            Aplat.UI.Theme.Changed += OnThemeChanged;
            SyncThemeMenu();
            this.LocationChanged += (s, e) => PositionOverlays();        // moves: follow immediately
            this.SizeChanged     += (s, e) => SchedulePositionOverlays(); // resizes/maximize: after layout settles
            this.Resize          += (s, e) => SchedulePositionOverlays();

            BuildContextMenu();
            sceneView.ContextMenuRequested += pt => _ctxScene.Show(sceneView, pt);
            // Layer rows have inline eye/lock affordances; right-click still opens
            // the object context menu after selecting the row.
            listObjects.MouseDown += (s, e) =>
            {
                var it = listObjects.GetItemAt(e.X, e.Y);
                if (it == null) return;

                if (e.Button == MouseButtons.Left)
                {
                    var hit = HitLayerGlyph(e.X, it.Bounds);
                    if (hit != LayerGlyph.None)
                    {
                        ToggleLayerGlyph(it.Index, hit);
                        return;
                    }
                }

                if (e.Button != MouseButtons.Right) return;
                if (it != null && !it.Selected)
                {
                    foreach (System.Windows.Forms.ListViewItem x in listObjects.SelectedItems) x.Selected = false;
                    it.Selected = true;
                }
            };
            listObjects.ContextMenuStrip = _ctxScene;

            // Autosave: once the scene has a file, changes are written
            // automatically — no manual Ctrl+S needed.
            _autosaveTimer.Interval = 10000;   // 10 s
            _autosaveTimer.Tick += Autosave;
            _autosaveTimer.Start();
        }

        // Quietly persist the scene (with view state + undo history) if it has a file
        // and unsaved changes. Errors are swallowed so autosave never interrupts work.
        private void Autosave(object sender, EventArgs e)
        {
            if (SaveModifiedSceneNow())
            {
                statusLabel.Text = Loc.T("Автосохранено в ") + DateTime.Now.ToString("HH:mm:ss");
                UpdateTitle();
            }
        }

        // An element still being edited is already in the canvas's pixels but not yet
        // in its object journal: it is entered for the duration of the write, so the
        // file stays consistent without ending the edit (autosave runs every 10 s).
        private void WriteSceneFile(string path)
        {
            sceneView.JournalLiveForSave();
            try { SceneFileManager.Save(_scene, path, _history); }
            finally { sceneView.UnjournalLiveAfterSave(); }
        }

        private bool SaveModifiedSceneNow()
        {
            if (_savingModifiedScene || !_scene.IsModified || string.IsNullOrEmpty(_scene.FilePath)) return false;
            _savingModifiedScene = true;
            try
            {
                CaptureViewState();
                WriteSceneFile(_scene.FilePath);
                return true;
            }
            catch { return false; /* ignore transient autosave failures */ }
            finally { _savingModifiedScene = false; }
        }

        private static ToolStripMenuItem CtxItem(string text, Aplat.UI.UiIcon icon, EventHandler onClick)
        {
            // The bitmap only reserves the image slot; the renderer draws the glyph
            // from Tag in the current theme colour.
            return new ToolStripMenuItem(text, Aplat.UI.IconFactory.Create(icon, Aplat.UI.Theme.Text, 18), onClick)
            {
                Tag = icon,
                ImageScaling = ToolStripItemImageScaling.None
            };
        }

        // Right-click menu of object actions, shared by the viewport and the scene list
        private void BuildContextMenu()
        {
            _ctxScene = new ContextMenuStrip
            {
                RenderMode = ToolStripRenderMode.Professional,
                Renderer   = new Aplat.UI.ModernToolStripRenderer(),
                Font       = Aplat.UI.Theme.Font(9.25f)
            };
            // Names and order match the "Edit" menu: the same action must not be
            // called differently in two places.
            _ctxScene.Items.Add(CtxItem(Loc.T("Копировать"), Aplat.UI.UiIcon.Copy, (s, e) => CopyToClipboard()));
            _ctxScene.Items.Add(CtxItem(Loc.T("Вставить"), Aplat.UI.UiIcon.Paste, (s, e) => PasteClipboard()));
            _ctxScene.Items.Add(CtxItem(Loc.T("Дублировать"), Aplat.UI.UiIcon.Duplicate, (s, e) => CopySelected()));
            _ctxScene.Items.Add(CtxItem(Loc.T("Переименовать"), Aplat.UI.UiIcon.Rename, (s, e) => RenameSelected()));
            if (Features.Text3D)
                _ctxScene.Items.Add(CtxItem(Loc.T("Изменить текст…"), Aplat.UI.UiIcon.Text, (s, e) => EditText3D()));
            _ctxScene.Items.Add(CtxItem(Loc.T("Скрыть / Показать"), Aplat.UI.UiIcon.Eye, (s, e) => ToggleVisibilitySelected()));
            _ctxScene.Items.Add(new ToolStripSeparator());
            _ctxScene.Items.Add(CtxItem(Loc.T("На пол"), Aplat.UI.UiIcon.Ground, (s, e) => DropToGround()));
            _ctxScene.Items.Add(CtxItem(Loc.T("Сбросить поворот"), Aplat.UI.UiIcon.Rotate, (s, e) => ResetRotationSelected()));
            _ctxScene.Items.Add(new ToolStripSeparator());
            _ctxScene.Items.Add(CtxItem(Loc.T("Группировать"), Aplat.UI.UiIcon.Group, (s, e) => GroupSelected()));
            _ctxScene.Items.Add(CtxItem(Loc.T("Разгруппировать"), Aplat.UI.UiIcon.Ungroup, (s, e) => UngroupSelected()));
            _ctxScene.Items.Add(new ToolStripSeparator());
            _ctxScene.Items.Add(CtxItem(Loc.T("Удалить"), Aplat.UI.UiIcon.Clear, (s, e) => DeleteSelected()));
        }

        // The paint-tool buttons laid out as a 2-column tile grid.
        private System.Windows.Forms.Button[] PaintTiles()
        {
            return new[] { btnBrush, btnPencil, btnPen, btnMarker, btnEyedropper, btnFill, btnEraser, btnHand,
                           btnText2D, btnSelect2D, btnShapeLine, btnShapeRect, btnShapeEllipse, btnShapeTriangle };
        }

        // Canvas-first startup: drop a blank white sheet, face it head-on and arm
        // the brush so the app opens ready to draw (draw first, add 3-D later).
        private const float DefaultCanvasHeight = 3f;

        // A blank white sheet, resting on the grid (y=0), not half under it.
        private static SceneObject NewCanvasObject()
        {
            return new SceneObject
            {
                Name          = Loc.T("Холст"),
                PrimitiveType = "Canvas",
                Mesh          = CanvasFactory.CreateSheet(4f, DefaultCanvasHeight),
                Position      = new Vec3(0f, DefaultCanvasHeight / 2f, 0f),
                Material      = new Material { Color = Color.White }
            };
        }

        private void SeedCanvas()
        {
            const float ch = DefaultCanvasHeight;
            _scene.Objects.Add(NewCanvasObject());
            UpdatePropertyCoordinateFrame();
            _scene.ShowGrid        = false;
            _scene.BackgroundColor = Aplat.UI.Theme.SceneBackground;   // follows the theme
            _scene.IsModified      = false;
            SyncCanvasUI();
            ApplyView(ViewMode.Front);
            sceneView.Camera.Target = new Vec3(0f, ch / 2f, 0f);     // centre the canvas in view
            SetBrush(sceneView.PaintKind, false);   // arm the brush *and* light its tile
            UpdateList();
            Select(null);
            UpdateTitle();
        }

        // ── Events ────────────────────────────────────────────────

        private void OnObjectSelected(object sender, SceneObject obj)
        {
            _selected = obj;
            UpdatePropertyCoordinateFrame();
            propPanel.SetObject(obj);
            SyncListSelection();
            UpdateLockButton();
            UpdateStatus();
        }

        private void OnPropertiesChanged(object sender, EventArgs e)
        {
            _scene.IsModified = true;
            sceneView.Refresh3D();
            UpdateList();
            UpdateStatus();
            UpdateTitle();
        }

        // ── Undo / Redo → see MainForm.History.cs ─────────────────

        // ── Export ────────────────────────────────────────────────

        // ── Model export (OBJ / STL / FBX) → see MainForm.Export.cs ──

        // ── Menu handlers ─────────────────────────────────────────

        private void menuNew_Click       (object s, EventArgs e) { NewScene();              }
        private void menuOpen_Click      (object s, EventArgs e) { OpenScene();             }
        private void menuSave_Click      (object s, EventArgs e) { SaveScene();             }
        private void menuSaveAs_Click    (object s, EventArgs e) { SaveScene(true);         }
        private void menuExit_Click      (object s, EventArgs e) { Close();                 }
        private void menuExpOBJ_Click    (object s, EventArgs e) { ExportAs("OBJ");         }
        private void menuExpSTL_Click    (object s, EventArgs e) { ExportAs("STL");         }
        private void menuExpFBX_Click    (object s, EventArgs e) { ExportAs("FBX");         }
        private void menuExpCanvasPng_Click(object s, EventArgs e) { ExportCanvasImage("PNG"); }
        private void menuExpCanvasJpg_Click(object s, EventArgs e) { ExportCanvasImage("JPG"); }
        private void menuExpFramePng_Click (object s, EventArgs e) { ExportFrame("PNG");       }
        private void menuExpFrameJpg_Click (object s, EventArgs e) { ExportFrame("JPG");       }

        // ── Help: all hotkeys ─────────────────────────────────────
        private void menuHotkeys_Click(object s, EventArgs e) { ShowHotkeys(); }

        private void ShowAbout()
        {
            using (var dlg = new Aplat.Dialogs.AboutDialog(() => CheckForUpdates(true))) dlg.ShowDialog(this);
        }

        private void ShowHotkeys()
        {
            using (var dlg = new Aplat.Dialogs.HotkeysDialog()) dlg.ShowDialog(this);
        }

    }
}
