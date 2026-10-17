namespace Aplat
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        // ── Palette & font: single source of truth is Aplat.UI.Theme ───
        // Live aliases (properties, not snapshots) so anything built after a theme
        // switch gets the current palette.
        private static System.Drawing.Color ClBg { get { return Aplat.UI.Theme.Bg; } }
        private static System.Drawing.Color ClSurface { get { return Aplat.UI.Theme.Surface; } }
        private static System.Drawing.Color ClCard { get { return Aplat.UI.Theme.Card; } }
        private static System.Drawing.Color ClBorder { get { return Aplat.UI.Theme.Border; } }
        private static System.Drawing.Color ClBorderSoft { get { return Aplat.UI.Theme.BorderSoft; } }
        private static System.Drawing.Color ClText { get { return Aplat.UI.Theme.Text; } }
        private static System.Drawing.Color ClTextSoft { get { return Aplat.UI.Theme.TextSoft; } }
        private static System.Drawing.Color ClAccent { get { return Aplat.UI.Theme.Accent; } }
        private static System.Drawing.Color ClAccentSoft { get { return Aplat.UI.Theme.AccentSoft; } }
        private static System.Drawing.Color ClBtnFace { get { return Aplat.UI.Theme.BtnFace; } }
        private static System.Drawing.Color ClBtnHover { get { return Aplat.UI.Theme.BtnHover; } }

        private static readonly string FontName = Aplat.UI.Theme.FontFamily;

        // ── Strips ────────────────────────────────────────────────
        private System.Windows.Forms.MenuStrip   menuStrip;
        private System.Windows.Forms.ToolStrip   toolStrip;   // row 1: file / history
        private System.Windows.Forms.ToolStrip   toolStrip2;  // row 2: views / modes
        private Aplat.UI.ToolbarDock           toolbarDock; // dock panels for both toolbars
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;
        private Aplat.UI.KeyHintLabel          keyHints;

        // ── Menu items ────────────────────────────────────────────
        private System.Windows.Forms.ToolStripMenuItem menuApp, menuFile, menuEdit, menuExport, menuView, menuHelp;
        private System.Windows.Forms.ToolStripMenuItem menuUndo, menuRedo;
        private System.Windows.Forms.ToolStripMenuItem menuNew, menuOpen, menuSave, menuSaveAs, menuExit;
        private System.Windows.Forms.ToolStripMenuItem menuRecent;
        private System.Windows.Forms.ToolStripMenuItem menuExpOBJ, menuExpSTL, menuExpFBX;
        private System.Windows.Forms.ToolStripMenuItem menuExpCanvasPng, menuExpCanvasJpg;
        private System.Windows.Forms.ToolStripMenuItem menuExpFramePng,  menuExpFrameJpg;
        private System.Windows.Forms.ToolStripMenuItem menuCanvas, menuHotkeys, menuPanels, menuCommands;
        private System.Windows.Forms.ToolStripSeparator menuPanelsSep;   // cards are inserted above it
        private System.Windows.Forms.ToolStripMenuItem menuLight, menuShadows, menuSpin, menuSpinOn, menuSpinSpeed;
        private System.Windows.Forms.ToolStripMenuItem menuSpinCamera, menuSpinObject;
        private System.Windows.Forms.ToolStripMenuItem menuQuickClose;
        private System.Windows.Forms.ToolStripMenuItem menuExpTurntable;
        // Edit: real commands, not just two history items.
        private System.Windows.Forms.ToolStripMenuItem menuDuplicate, menuDelete;
        private System.Windows.Forms.ToolStripMenuItem menuSelectAll, menuDeselect;
        private System.Windows.Forms.ToolStripMenuItem menuGroup, menuUngroup;
        private System.Windows.Forms.ToolStripMenuItem menuCopy, menuCut, menuPaste;
        // Object: creating shapes and acting on the selection.
        private System.Windows.Forms.ToolStripMenuItem menuObject, menuShapes;
        private System.Windows.Forms.ToolStripMenuItem menuAddText, menuEditText;
        private System.Windows.Forms.ToolStripMenuItem menuDropGround, menuResetRot, menuToggleVis, menuRename;
        // View: toggles and the standard viewpoints.
        private System.Windows.Forms.ToolStripMenuItem menuWire, menuVerts, menuGridItem, menuOutline;
        private System.Windows.Forms.ToolStripMenuItem menuViewFront, menuViewSide, menuViewTop, menuViewPersp;
        private System.Windows.Forms.ToolStripMenuItem menuFocusSel, menuFocusAll;
        private System.Windows.Forms.ToolStripMenuItem menuTheme, menuThemeLight, menuThemeDark, menuThemeSystem;
        private System.Windows.Forms.ToolStripMenuItem menuLanguage, menuLangRu, menuLangEn;
        private System.Windows.Forms.ToolStripMenuItem menuToolbarFile, menuToolbarModes;

        // ── Toolbar buttons ───────────────────────────────────────
        private System.Windows.Forms.ToolStripButton    tbNew, tbOpen, tbSave;
        private System.Windows.Forms.ToolStripButton    tbUndo, tbRedo;
        private System.Windows.Forms.ToolStripDropDownButton tbHistory;
        private System.Windows.Forms.ToolStripButton    tb2D, tb3D;
        private System.Windows.Forms.ToolStripButton    tbSelect, tbMove, tbRotate, tbScale, tbEditShape;
        private System.Windows.Forms.ToolStripButton    tbWireframe, tbVertices, tbGrid;
        private System.Windows.Forms.ToolStripSeparator tbSep3, tbSep4, tbSep5;

        // ── Panels ────────────────────────────────────────────────
        private System.Windows.Forms.Panel panelRight;
        private System.Windows.Forms.Panel panelMain;
        // The four overlay cards are plain content panels; each is hosted inside a
        // floating Aplat.UI.OverlayWindow (created in MainForm.InitOverlays) that
        // supplies the rounded card paint, drag, resize and translucency.
        private System.Windows.Forms.Panel floatingToolsPanel;
        private System.Windows.Forms.Panel recentToolsPanel;
        private System.Windows.Forms.Panel layersPanel;
        private System.Windows.Forms.Panel viewPanel;
        private System.Windows.Forms.Panel layersHeader;     // drag handle for the layers card
        private System.Windows.Forms.Button btnLayersAdd;    // "+" → add-shape menu
        private System.Windows.Forms.FlowLayoutPanel floatingToolsFlow;
        private System.Windows.Forms.FlowLayoutPanel recentToolsFlow;
        private System.Windows.Forms.FlowLayoutPanel viewFlow; // drag handle for the view card

        // ── Right panel: tabs ─────────────────────────────────────
        private System.Windows.Forms.TabControl tabsTools;
        private System.Windows.Forms.TabPage    tabPaint, tab3D, tabObject;
        private Aplat.UI.InspectorPanel       inspPaint, insp3D;
        private System.Windows.Forms.ToolTip    panelTips;

        // Drawing tools
        private System.Windows.Forms.Label  lblColor, lblSize, lblOpacity, lblFill, lblShapes2D, lblCanvas;
        private System.Windows.Forms.Button btnBrush, btnPencil, btnPen, btnEyedropper, btnFill, btnEraser, btnHand, btnCursor;
        private System.Windows.Forms.Button btnMarker, btnSelect2D;
        private System.Windows.Forms.Button btnText2D, btnShapeLine, btnShapeRect, btnShapeEllipse, btnShapeTriangle;
        private System.Windows.Forms.Label       lblShapeStyle;   // "Shape style ···· Outline"
        private Aplat.UI.SegmentedControl      segShapeStyle;
        // ERASER: pixels (precise) or whole objects.
        private System.Windows.Forms.Label       lblEraserSection;
        private Aplat.UI.SegmentedControl      segEraserKind;
        // TEXT: format of the text being typed (and of the next one).
        private System.Windows.Forms.Label       lblTextSection;
        private System.Windows.Forms.ComboBox    cmbTextFont;
        private Aplat.UI.DragNumber            numTextSize;
        private Aplat.UI.ToggleStrip           tglTextStyle;
        private Aplat.UI.SegmentedControl      segTextAlign;
        private System.Windows.Forms.Button btnClearCanvas;
        private System.Windows.Forms.Button btnBrushColor;
        private Aplat.UI.ModernSlider trkBrushSize;
        private Aplat.UI.ModernSlider trkOpacity;
        private Aplat.UI.ModernSlider trkFill;
        private Aplat.UI.ModernSlider trkViewZoom;
        private System.Windows.Forms.Label lblViewZoom, lblViewSpeed;
        private System.Windows.Forms.Button btnViewFit, btnViewGrid, btnViewGuides, btnViewCenter;

        // 3D tools
        private System.Windows.Forms.Label  lblShapes, lblScene3D;
        private System.Windows.Forms.Button btnCube, btnSphere, btnCylinder, btnCone, btnTetra, btnText, btnClear3D;
        private System.Windows.Forms.Button btnPyramid, btnTorus, btnCapsule;
        private Aplat.UI.InspectorPanel palettePanel;
        private Aplat.Controls.PropertiesPanel propPanel;

        // Scene tab — checkable, renamable, multi-select list
        private System.Windows.Forms.ListView listObjects;

        // ── Center ────────────────────────────────────────────────
        private Aplat.Controls.SceneView sceneView;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        // The WinForms designer only parses InitializeComponent. Keeping it as a tiny,
        // designer-safe stub (and doing the real, hand-coded layout in BuildLayout)
        // stops the designer from choking on the helper calls/lambdas below, so the
        // form's error list stays clean. BuildLayout() is called from the constructor.
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.ClientSize          = new System.Drawing.Size(1040, 660);
            this.Name                = "MainForm";
            this.Text                = "Aplat";
        }

        private void BuildLayout()
        {
            this.SuspendLayout();

            // ═══════════════════════════════════════════════════════
            // MenuStrip
            // ═══════════════════════════════════════════════════════
            this.menuStrip = new System.Windows.Forms.MenuStrip();

            this.menuApp = new System.Windows.Forms.ToolStripMenuItem();
            this.menuApp.Text = Loc.T("Меню");
            this.menuApp.Image = Aplat.UI.IconFactory.Create(Aplat.UI.UiIcon.Menu, ClText, 18);
            this.menuApp.Tag = Aplat.UI.UiIcon.Menu;
            this.menuApp.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.menuApp.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.menuApp.ToolTipText = Loc.T("Быстрое меню");
            // The quick menu mirrors the main one: file → scene settings → help → exit.
            // "Close" comes last and is set apart — it is the only irreversible item in
            // the list.
            this.menuQuickClose = MI(Loc.T("Закрыть"), menuExit_Click,
                System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.F4, Aplat.UI.UiIcon.Close);
            this.menuApp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                MI(Loc.T("Создать"), menuNew_Click, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.N, Aplat.UI.UiIcon.New),
                MI(Loc.T("Открыть..."), menuOpen_Click, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O, Aplat.UI.UiIcon.Open),
                MI(Loc.T("Сохранить"), menuSave_Click, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S, Aplat.UI.UiIcon.Save),
                new System.Windows.Forms.ToolStripSeparator(),
                MI(Loc.T("Холст и сетка..."), menuCanvas_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Canvas),
                MI(Loc.T("Освещение и солнце..."), menuLight_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Light),
                new System.Windows.Forms.ToolStripSeparator(),
                MI(Loc.T("Горячие клавиши"), menuHotkeys_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Keyboard),
                new System.Windows.Forms.ToolStripSeparator(),
                this.menuQuickClose
            });

            this.menuFile   = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Файл"));
            this.menuNew    = MI(Loc.T("Создать"),          menuNew_Click,    System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.N, Aplat.UI.UiIcon.New);
            this.menuOpen   = MI(Loc.T("Открыть..."),       menuOpen_Click,   System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O, Aplat.UI.UiIcon.Open);
            this.menuSave   = MI(Loc.T("Сохранить"),        menuSave_Click,   System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S, Aplat.UI.UiIcon.Save);
            this.menuSaveAs = MI(Loc.T("Сохранить как..."), menuSaveAs_Click, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.S, Aplat.UI.UiIcon.Save);
            this.menuExit   = MI(Loc.T("Выход"),            menuExit_Click,   System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Close);

            // "Recent" — rebuilt on every open from the persisted MRU list. The
            // placeholder item makes the submenu arrow render and DropDownOpening fire.
            this.menuRecent = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Недавние"));
            this.menuRecent.Image = Aplat.UI.IconFactory.Create(Aplat.UI.UiIcon.History, ClText, 18);
            this.menuRecent.Tag   = Aplat.UI.UiIcon.History;
            this.menuRecent.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem(Loc.T("Список пуст")) { Enabled = false });
            this.menuRecent.DropDownOpening += new System.EventHandler(menuRecent_DropDownOpening);

            this.menuExport      = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Экспорт"));
            this.menuExport.Image = Aplat.UI.IconFactory.Create(Aplat.UI.UiIcon.Export, ClText, 18);
            this.menuExport.Tag = Aplat.UI.UiIcon.Export;
            this.menuExpOBJ       = MI(Loc.T("Экспортировать OBJ..."),    menuExpOBJ_Click,       System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Export);
            this.menuExpSTL       = MI(Loc.T("Экспортировать STL..."),    menuExpSTL_Click,       System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Export);
            this.menuExpFBX       = MI(Loc.T("Экспортировать FBX..."),    menuExpFBX_Click,       System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Export);
            this.menuExpCanvasPng = MI(Loc.T("Экспорт холста PNG..."),    menuExpCanvasPng_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Canvas);
            this.menuExpCanvasJpg = MI(Loc.T("Экспорт холста JPG..."),    menuExpCanvasJpg_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Canvas);
            this.menuExpFramePng  = MI(Loc.T("Экспорт кадра PNG..."),     menuExpFramePng_Click,  System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Export);
            this.menuExpFrameJpg  = MI(Loc.T("Экспорт кадра JPG..."),     menuExpFrameJpg_Click,  System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Export);
            this.menuExpTurntable = MI(Loc.T("Экспорт оборота (серия PNG)..."), menuExpTurntable_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Rotate);
            this.menuExport.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                menuExpOBJ, menuExpSTL, menuExpFBX,
                new System.Windows.Forms.ToolStripSeparator(),
                menuExpCanvasPng, menuExpCanvasJpg,
                new System.Windows.Forms.ToolStripSeparator(),
                menuExpFramePng,  menuExpFrameJpg,
                new System.Windows.Forms.ToolStripSeparator(),
                menuExpTurntable });
            this.menuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                menuNew, menuOpen, menuRecent, menuSave, menuSaveAs,
                new System.Windows.Forms.ToolStripSeparator(), menuExport,
                new System.Windows.Forms.ToolStripSeparator(), menuExit });

            // "Edit" — the familiar name with the familiar contents. It used to hold only
            // Undo / Redo, while copy, group and delete existed purely as hotkeys that
            // could only be discovered from the help.
            //
            // The shortcuts of the new items are shown as text but not registered as
            // ShortcutKeys: ProcessCmdKey already intercepts these keys, and a second
            // registration would take them away from input fields.
            this.menuEdit = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Правка"));
            this.menuUndo = MI(Loc.T("Отменить"),  tbUndo_Click, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z, Aplat.UI.UiIcon.Undo);
            this.menuRedo = MI(Loc.T("Повторить"), tbRedo_Click, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Y, Aplat.UI.UiIcon.Redo);
            this.menuCut       = MID(Loc.T("Вырезать"),        (s2, e2) => CutToClipboard(),   "Ctrl+X", Aplat.UI.UiIcon.Cut);
            this.menuCopy      = MID(Loc.T("Копировать"),      (s2, e2) => CopyToClipboard(),  "Ctrl+C", Aplat.UI.UiIcon.Copy);
            this.menuPaste     = MID(Loc.T("Вставить"),        (s2, e2) => PasteClipboard(),   "Ctrl+V", Aplat.UI.UiIcon.Paste);
            this.menuDuplicate = MID(Loc.T("Дублировать"),     (s2, e2) => CopySelected(),     "Ctrl+D", Aplat.UI.UiIcon.Duplicate);
            this.menuDelete    = MID(Loc.T("Удалить"),         (s2, e2) => DeleteSelected(),   "Delete", Aplat.UI.UiIcon.Clear);
            this.menuSelectAll = MID(Loc.T("Выделить всё"),    (s2, e2) => SelectAll(),        "Ctrl+A", Aplat.UI.UiIcon.Select);
            this.menuDeselect  = MID(Loc.T("Снять выделение"), (s2, e2) => Select(null),       "Esc",    Aplat.UI.UiIcon.Deselect);
            this.menuGroup     = MID(Loc.T("Группировать"),    (s2, e2) => GroupSelected(),    "Ctrl+G", Aplat.UI.UiIcon.Group);
            this.menuUngroup   = MID(Loc.T("Разгруппировать"), (s2, e2) => UngroupSelected(),  "Ctrl+Shift+G", Aplat.UI.UiIcon.Ungroup);
            this.menuEdit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                menuUndo, menuRedo,
                new System.Windows.Forms.ToolStripSeparator(),
                menuCut, menuCopy, menuPaste, menuDuplicate, menuDelete,
                new System.Windows.Forms.ToolStripSeparator(),
                menuSelectAll, menuDeselect,
                new System.Windows.Forms.ToolStripSeparator(),
                menuGroup, menuUngroup });
            this.menuEdit.DropDownOpening += new System.EventHandler(menuEdit_DropDownOpening);

            // ── "Object": what to create and what to do with the selection ──
            // Shapes used to exist only as tiles in the right-hand panel: with the panel
            // collapsed or unnoticed there was no way to add a cube.
            this.menuObject = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Объект"));
            this.menuShapes = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Добавить фигуру"));
            this.menuShapes.Image = Aplat.UI.IconFactory.Create(Aplat.UI.UiIcon.Cube, ClText, 18);
            this.menuShapes.Tag   = Aplat.UI.UiIcon.Cube;
            this.menuShapes.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                MI(Loc.T("Куб"),       btnCube_Click,     System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Cube),
                MI(Loc.T("Сфера"),     btnSphere_Click,   System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Sphere),
                MI(Loc.T("Цилиндр"),   btnCylinder_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Cylinder),
                MI(Loc.T("Конус"),     btnCone_Click,     System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Cone),
                MI(Loc.T("Пирамида"),  btnPyramid_Click,  System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Pyramid),
                MI(Loc.T("Тетраэдр"),  btnTetra_Click,    System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Tetrahedron),
                MI(Loc.T("Тор"),       btnTorus_Click,    System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Torus),
                MI(Loc.T("Капсула"),   btnCapsule_Click,  System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Capsule) });
            this.menuAddText   = MI(Loc.T("Объёмный текст..."), btnText_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Text);
            this.menuEditText  = MID(Loc.T("Изменить текст..."), (s2, e2) => EditText3D(), "", Aplat.UI.UiIcon.Text);
            this.menuAddText.Visible = this.menuEditText.Visible = Features.Text3D;
            this.menuRename    = MID(Loc.T("Переименовать"),     (s2, e2) => RenameSelected(), "", Aplat.UI.UiIcon.Rename);
            this.menuDropGround = MID(Loc.T("Поставить на пол"), (s2, e2) => DropToGround(), "", Aplat.UI.UiIcon.Ground);
            this.menuResetRot   = MID(Loc.T("Сбросить поворот"), (s2, e2) => ResetRotationSelected(), "", Aplat.UI.UiIcon.Rotate);
            this.menuToggleVis  = MID(Loc.T("Скрыть / показать"), (s2, e2) => ToggleVisibilitySelected(), "", Aplat.UI.UiIcon.Eye);
            this.menuObject.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                menuShapes, menuAddText,
                new System.Windows.Forms.ToolStripSeparator(),
                menuEditText, menuRename,
                new System.Windows.Forms.ToolStripSeparator(),
                menuDropGround, menuResetRot, menuToggleVis });
            this.menuObject.DropDownOpening += new System.EventHandler(menuObject_DropDownOpening);

            this.menuView        = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Вид"));
            this.menuCanvas      = MI(Loc.T("Холст и сетка..."), menuCanvas_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Canvas);
            this.menuLight       = MI(Loc.T("Освещение и солнце..."), menuLight_Click, System.Windows.Forms.Keys.F7, Aplat.UI.UiIcon.Light);
            BuildPerfMenu();
            this.menuShadows     = MID(Loc.T("Тени"), (s2, e2) => ToggleShadows(), "", Aplat.UI.UiIcon.Shadows);

            // Everything about auto-rotation lives in one submenu rather than three
            // neighbouring items: first the switch, below it what is being rotated, then
            // the speed.
            this.menuSpin = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Автовращение"));
            this.menuSpin.Image = Aplat.UI.IconFactory.Create(Aplat.UI.UiIcon.Rotate, ClText, 18);
            this.menuSpin.Tag   = Aplat.UI.UiIcon.Rotate;
            this.menuSpinOn     = MI(Loc.T("Включить"), menuSpin_Click, System.Windows.Forms.Keys.F9, Aplat.UI.UiIcon.None);
            this.menuSpinCamera = MI(Loc.T("Камера вокруг объекта"), menuSpinCamera_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.None);
            this.menuSpinObject = MI(Loc.T("Сам объект вокруг своей оси"), menuSpinObject_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.None);
            this.menuSpinSpeed  = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Скорость"));
            this.menuSpinSpeed.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                SpinSpeedItem(Loc.T("Медленно"),       10f),
                SpinSpeedItem(Loc.T("Обычно"),         24f),
                SpinSpeedItem(Loc.T("Быстро"),         60f),
                SpinSpeedItem(Loc.T("Очень быстро"),  120f) });
            this.menuSpin.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuSpinOn,
                new System.Windows.Forms.ToolStripSeparator(),
                this.menuSpinCamera, this.menuSpinObject,
                new System.Windows.Forms.ToolStripSeparator(),
                this.menuSpinSpeed });

            // Grid / wireframe display and the standard viewpoints used to live only on the
            // tool panel and on hotkeys. People look for them in the menu, which also shows
            // the shortcut. ProcessCmdKey registers the keys; only the text is set here.
            this.menuWire     = MID(Loc.T("Каркас"),   (s2, e2) => tbWireframe_Click(null, System.EventArgs.Empty), "F5", Aplat.UI.UiIcon.Wireframe);
            this.menuVerts    = MID(Loc.T("Вершины"),  (s2, e2) => tbVertices_Click(null, System.EventArgs.Empty),  "F6", Aplat.UI.UiIcon.Vertices);
            this.menuGridItem = MID(Loc.T("Сетка"),    (s2, e2) => ToggleGrid(),                                    "F8", Aplat.UI.UiIcon.Grid);
            // On a dense mesh the per-object selection outline turns into an orange
            // mess; when off, it is replaced by a bounding rectangle.
            this.menuOutline  = MID(Loc.T("Обводить полигоны выделенного"), (s2, e2) => ToggleOutlineSelection(), "", Aplat.UI.UiIcon.Vertices);

            this.menuViewFront = MID(Loc.T("Спереди"),     (s2, e2) => ApplyView(Aplat.Rendering.ViewMode.Front),       "1", Aplat.UI.UiIcon.ViewFront);
            this.menuViewSide  = MID(Loc.T("Сбоку"),       (s2, e2) => ApplyView(Aplat.Rendering.ViewMode.Side),        "3", Aplat.UI.UiIcon.ViewSide);
            this.menuViewTop   = MID(Loc.T("Сверху"),      (s2, e2) => ApplyView(Aplat.Rendering.ViewMode.Top),         "7", Aplat.UI.UiIcon.ViewTop);
            this.menuViewPersp = MID(Loc.T("Перспектива"), (s2, e2) => ApplyView(Aplat.Rendering.ViewMode.Perspective), "0", Aplat.UI.UiIcon.ViewPerspective);
            this.menuFocusSel  = MID(Loc.T("Фокус на выбранном"), (s2, e2) => sceneView.FocusSelection(), "F",    Aplat.UI.UiIcon.ZoomFit);
            this.menuFocusAll  = MID(Loc.T("Показать всю сцену"), (s2, e2) => FocusWholeScene(),          "Home", Aplat.UI.UiIcon.ZoomFit);

            // Theme: light, "Graphite" (dark) or follow Windows. The choice applies
            // immediately, without a restart, and is remembered.
            this.menuTheme = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Тема"));
            this.menuTheme.Image = Aplat.UI.IconFactory.Create(Aplat.UI.UiIcon.Palette, ClText, 18);
            this.menuTheme.Tag   = Aplat.UI.UiIcon.Palette;
            this.menuThemeLight  = ThemeItem(Loc.T("Светлая"),          Aplat.UI.ThemeMode.Light,    Aplat.UI.UiIcon.ThemeLight);
            this.menuThemeDark   = ThemeItem(Loc.T("Графит (тёмная)"),  Aplat.UI.ThemeMode.Graphite, Aplat.UI.UiIcon.ThemeDark);
            this.menuThemeSystem = ThemeItem(Loc.T("Как в системе"),    Aplat.UI.ThemeMode.System,   Aplat.UI.UiIcon.ThemeSystem);
            this.menuTheme.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuThemeLight, this.menuThemeDark,
                new System.Windows.Forms.ToolStripSeparator(),
                this.menuThemeSystem });

            // Language: applies after a restart (windows are built with their text). The names of the
            // languages are shown in themselves, so they can always be found.
            this.menuLanguage = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Язык"));
            this.menuLanguage.Image = Aplat.UI.IconFactory.Create(Aplat.UI.UiIcon.Text, ClText, 18);
            this.menuLanguage.Tag   = Aplat.UI.UiIcon.Text;
            this.menuLangRu = new System.Windows.Forms.ToolStripMenuItem("Русский", null, (s2, e2) => ChooseLanguage(Lang.Russian));
            this.menuLangEn = new System.Windows.Forms.ToolStripMenuItem("English", null, (s2, e2) => ChooseLanguage(Lang.English));
            this.menuLangRu.Checked = Loc.Current == Lang.Russian;
            this.menuLangEn.Checked = Loc.Current == Lang.English;
            this.menuLanguage.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { this.menuLangRu, this.menuLangEn });

            // Panels: one list for the toolbars and the floating cards — show / hide here, position
            // and transparency in the dialog. The card items are added when the cards exist (InitOverlays).
            this.menuToolbarFile  = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Файл"),   null, (s2, e2) => ToggleToolbar(this.toolStrip));
            this.menuToolbarModes = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Режимы"), null, (s2, e2) => ToggleToolbar(this.toolStrip2));
            this.menuPanels = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Панели"));
            this.menuPanels.Image = Aplat.UI.IconFactory.Create(Aplat.UI.UiIcon.Panels, ClText, 18);
            this.menuPanels.Tag   = Aplat.UI.UiIcon.Panels;
            this.menuPanelsSep = new System.Windows.Forms.ToolStripSeparator();
            this.menuPanels.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuToolbarFile, this.menuToolbarModes,
                this.menuPanelsSep,
                MI(Loc.T("Положение и прозрачность..."), menuPanels_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.None),
                new System.Windows.Forms.ToolStripMenuItem(Loc.T("Сбросить расположение"), null, (s2, e2) => ResetPanels()) });
            this.menuPanels.DropDownOpening += (s2, e2) => RefreshToolbarMenuChecks();

            // Display → viewpoint → framing → scene and light → motion → interface.
            this.menuView.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuWire, this.menuVerts, this.menuGridItem, this.menuOutline,
                new System.Windows.Forms.ToolStripSeparator(),
                this.menuViewFront, this.menuViewSide, this.menuViewTop, this.menuViewPersp,
                new System.Windows.Forms.ToolStripSeparator(),
                this.menuFocusSel, this.menuFocusAll,
                new System.Windows.Forms.ToolStripSeparator(),
                this.menuCanvas,
                this.menuLight,
                this.menuShadows,
                this.menuPerf,
                new System.Windows.Forms.ToolStripSeparator(),
                this.menuSpin,
                new System.Windows.Forms.ToolStripSeparator(),
                this.menuTheme,
                this.menuLanguage,
                this.menuPanels });
            this.menuView.DropDownOpening += new System.EventHandler(menuView_DropDownOpening);
            this.menuView.DropDownOpening += (s2, e2) => SyncShadowsMenu();

            this.menuHelp     = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Справка"));
            this.menuHotkeys  = MI(Loc.T("Горячие клавиши"), menuHotkeys_Click, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Keyboard);
            // Ctrl+Q is handled in ProcessCmdKey (works from any panel), only shown here.
            this.menuCommands = MID(Loc.T("Поиск команд"), (s2, e2) => ShowCommandPalette(), "Ctrl+Q", Aplat.UI.UiIcon.Command);
            this.menuHelp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuCommands, this.menuHotkeys });
            BuildUpdateMenu();
            this.menuHelp.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            this.menuHelp.DropDownItems.Add(MI(Loc.T("О программе"), (s2, e2) => ShowAbout(), System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Help));

            // File → Edit → Object → View → Help: left to right from "what the file is"
            // to "what to do with the scene", as in any editor.
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                menuApp, menuFile, menuEdit, menuObject, menuView, menuHelp });
            this.menuStrip.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.menuStrip.Renderer    = new Aplat.UI.ModernToolStripRenderer();
            this.menuStrip.BackColor  = ClCard;
            this.menuStrip.ForeColor  = ClText;
            this.menuStrip.Font       = new System.Drawing.Font(FontName, 9.25F);
            this.menuStrip.Padding    = new System.Windows.Forms.Padding(14, 5, 0, 4);

            // ═══════════════════════════════════════════════════════
            // ToolStrip row 2 — quick file actions
            // ═══════════════════════════════════════════════════════
            this.toolStrip = new System.Windows.Forms.ToolStrip();
            this.tbNew   = TB(Loc.T("Создать"),  Loc.T("Новая сцена (Ctrl+N)"), Aplat.UI.UiIcon.New);
            this.tbOpen  = TB(Loc.T("Открыть"),    Loc.T("Открыть сцену (Ctrl+O)"), Aplat.UI.UiIcon.Open);
            this.tbSave  = TB(Loc.T("Сохранить"),  Loc.T("Сохранить сцену (Ctrl+S)"), Aplat.UI.UiIcon.Save);
            this.tbUndo  = TB(Loc.T("Отменить"),   Loc.T("Отменить (Ctrl+Z)"), Aplat.UI.UiIcon.Undo);
            this.tbRedo  = TB(Loc.T("Повторить"),  Loc.T("Повторить (Ctrl+Y)"), Aplat.UI.UiIcon.Redo);
            this.tbHistory = TBDrop(Loc.T("История"), Loc.T("История последних правок"), Aplat.UI.UiIcon.History);
            this.tbNew.Click += new System.EventHandler(menuNew_Click);
            this.tbOpen.Click+= new System.EventHandler(menuOpen_Click);
            this.tbSave.Click+= new System.EventHandler(menuSave_Click);
            this.tbUndo.Click+= new System.EventHandler(tbUndo_Click);
            this.tbRedo.Click+= new System.EventHandler(tbRedo_Click);
            this.tbHistory.DropDownOpening += new System.EventHandler(tbHistory_DropDownOpening);
            // Undo / Redo / History live on this (uncrowded) row, right-aligned, so the
            // narrow-window modes row can never clip them. tbHistory added first → it
            // sits rightmost; visual order left→right is Undo, Redo, History.
            this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                tbNew, tbOpen, tbSave, tbHistory, tbRedo, tbUndo });
            this.toolStrip.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.toolStrip.Renderer    = new Aplat.UI.ModernToolStripRenderer();
            this.toolStrip.GripStyle  = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip.CanOverflow = false;
            this.toolStrip.BackColor  = ClCard;
            this.toolStrip.ForeColor  = ClText;
            this.toolStrip.Font       = new System.Drawing.Font(FontName, 9.25F);
            this.toolStrip.Padding    = new System.Windows.Forms.Padding(6, 6, 12, 5);
            this.toolStrip.AutoSize   = false;
            this.toolStrip.Height     = 42;

            // ═══════════════════════════════════════════════════════
            // ToolStrip row 3 — modes plus undo/history on the right
            // ═══════════════════════════════════════════════════════
            this.toolStrip2 = new System.Windows.Forms.ToolStrip();
            this.tb2D = TBCheckText("2D", Loc.T("Плоский вид — рисование на холсте (1 или Num1)"), Aplat.UI.UiIcon.Canvas);
            this.tb3D = TBCheckText("3D", Loc.T("Перспектива — орбита сцены (0 или Num0)"), Aplat.UI.UiIcon.Cube);
            this.tbSep3  = new System.Windows.Forms.ToolStripSeparator();
            this.tbSep4  = new System.Windows.Forms.ToolStripSeparator();
            this.tbSep5  = new System.Windows.Forms.ToolStripSeparator();
            this.tbSelect = TBCheckText(Loc.T("Выбор"),        Loc.T("Выбор объектов (F1, либо Esc)"), Aplat.UI.UiIcon.Select);
            this.tbMove   = TBCheckText(Loc.T("Переместить"),  Loc.T("Перемещение выбранного по осям (F2)"), Aplat.UI.UiIcon.Move);
            this.tbRotate = TBCheckText(Loc.T("Вращение"),     Loc.T("Поворот выбранного вокруг осей (F3)"), Aplat.UI.UiIcon.Rotate);
            this.tbScale     = TBCheckText(Loc.T("Масштаб"),   Loc.T("Изменение размера выбранного (F4)"), Aplat.UI.UiIcon.Scale);
            this.tbEditShape = TBCheckText(Loc.T("Форма"),     Loc.T("Тянуть вершины, рёбра и грани выбранного объекта (V)"), Aplat.UI.UiIcon.EditShape);
            this.tbWireframe = TBCheckText(Loc.T("Каркас"),    Loc.T("Показать рёбра сетки поверх фигур (F5)"), Aplat.UI.UiIcon.Wireframe);
            this.tbVertices  = TBCheckText(Loc.T("Вершины"),   Loc.T("Показать узлы сетки точками (F6)"), Aplat.UI.UiIcon.Vertices);
            this.tbGrid      = TBCheckText(Loc.T("Сетка"),     Loc.T("Пол сцены с разметкой (F8)"), Aplat.UI.UiIcon.Grid);
            this.tbGrid.Checked = true;
            this.tb2D.Checked = true;
            this.tb2D.Click      += new System.EventHandler(tb2D_Click);
            this.tb3D.Click      += new System.EventHandler(tb3D_Click);
            this.tbSelect.Click  += new System.EventHandler(tbSelect_Click);
            this.tbMove.Click    += new System.EventHandler(tbMove_Click);
            this.tbRotate.Click  += new System.EventHandler(tbRotate_Click);
            this.tbScale.Click   += new System.EventHandler(tbScale_Click);
            this.tbEditShape.Click += new System.EventHandler(tbEditShape_Click);
            this.tbWireframe.Click += new System.EventHandler(tbWireframe_Click);
            this.tbVertices.Click  += new System.EventHandler(tbVertices_Click);
            this.tbGrid.Click      += new System.EventHandler(tbGrid_Click);
            this.tbSelect.Checked = true;
            this.tbHistory.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.tbRedo.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.tbUndo.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.toolStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                tb2D, tb3D, tbSep3,
                tbSelect, tbMove, tbRotate, tbScale, tbEditShape, tbSep4,
                tbWireframe, tbVertices, tbSep5, tbGrid });
            this.toolStrip2.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.toolStrip2.Renderer    = new Aplat.UI.ModernToolStripRenderer();
            this.toolStrip2.GripStyle  = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip2.CanOverflow = false;   // never tuck Undo/Redo under an overflow chevron
            this.toolStrip2.BackColor  = ClCard;
            this.toolStrip2.ForeColor  = ClText;
            this.toolStrip2.Font       = new System.Drawing.Font(FontName, 9.1F);
            this.toolStrip2.Padding    = new System.Windows.Forms.Padding(6, 4, 12, 4);
            this.toolStrip2.AutoSize   = false;
            this.toolStrip2.Height     = 38;

            // ═══════════════════════════════════════════════════════
            // StatusStrip
            // ═══════════════════════════════════════════════════════
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusStrip.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.statusStrip.Renderer   = new Aplat.UI.ModernToolStripRenderer();
            this.statusStrip.Padding    = new System.Windows.Forms.Padding(10, 3, 10, 3);
            this.statusStrip.BackColor = ClBg;
            this.statusStrip.SizingGrip = false;
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel(Loc.T("Готово"));
            this.statusLabel.Spring    = true;
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.statusLabel.ForeColor = ClTextSoft;
            this.statusLabel.Font      = new System.Drawing.Font(FontName, 9.1F);
            this.statusStrip.Items.Add(this.statusLabel);
            // Right side: input hints for the active tool (MainForm.UpdateKeyHints).
            this.keyHints = new Aplat.UI.KeyHintLabel { Font = new System.Drawing.Font(FontName, 8.5F) };
            this.statusStrip.Items.Add(this.keyHints);

            // ═══════════════════════════════════════════════════════
            // Right panel — tabbed inspector
            // ═══════════════════════════════════════════════════════
            // Each tab is an InspectorPanel (sections, full-width rows, tile grids)
            // inside a ScrollHost: exact widths, symmetric 16px insets, a thin
            // themed scrollbar — and no horizontal scrolling, by construction.

            // Drawing tools live on the floating bar (styled further below).
            this.btnBrush      = TileBtn(Loc.T("Кисть"),     Aplat.UI.UiIcon.Brush,      btnBrush_Click);
            this.btnPencil     = TileBtn(Loc.T("Карандаш"),  Aplat.UI.UiIcon.Pencil,     btnPencil_Click);
            this.btnPen        = TileBtn(Loc.T("Ручка"),     Aplat.UI.UiIcon.Pen,        btnPen_Click);
            this.btnMarker     = TileBtn(Loc.T("Маркер"),    Aplat.UI.UiIcon.Marker,     btnMarker_Click);
            this.btnSelect2D   = TileBtn(Loc.T("Выделить"),  Aplat.UI.UiIcon.SelectRect, btnSelect2D_Click);
            this.btnEyedropper = TileBtn(Loc.T("Пипетка"),   Aplat.UI.UiIcon.Eyedropper, btnEyedropper_Click);
            this.btnFill       = TileBtn(Loc.T("Заливка"),   Aplat.UI.UiIcon.Fill,       btnFill_Click);
            this.btnEraser     = TileBtn(Loc.T("Ластик"),    Aplat.UI.UiIcon.Eraser,     btnEraser_Click);
            this.btnHand       = TileBtn(Loc.T("Рука"),      Aplat.UI.UiIcon.Hand,       btnHand_Click);
            this.btnCursor     = TileBtn(Loc.T("Выбор"),     Aplat.UI.UiIcon.Select,     btnCursor_Click);
            this.btnText2D     = TileBtn(Loc.T("Текст"),     Aplat.UI.UiIcon.Text,       btnText2D_Click);
            this.btnRuler      = TileBtn(Loc.T("Линейка"),   Aplat.UI.UiIcon.Ruler,      btnRuler_Click);

            // ── "2D" tab: brush · shapes · canvas ─────────────────
            this.lblColor      = SectionLabel(Loc.T("Кисть"));
            this.btnBrushColor = LB(Loc.T("Цвет кисти"), Aplat.UI.UiIcon.Color, btnBrushColor_Click);
            this.btnBrushColor.BackColor = ClAccent;
            this.btnBrushColor.ForeColor = System.Drawing.Color.White;
            Aplat.UI.ThemeManager.KeepColors(this.btnBrushColor);   // shows the brush colour, not a theme role

            // Quick palette: the hue wheel ("any colour") + 13 presets → two rows of 7.
            this.palettePanel = new Aplat.UI.InspectorPanel
            {
                Padding   = new System.Windows.Forms.Padding(0),
                ColumnGap = 0,
                RowGap    = 4,
                Margin    = new System.Windows.Forms.Padding(0, 12, 0, 0)
            };
            System.Drawing.Color[] quickPalette = new System.Drawing.Color[]
            {
                System.Drawing.Color.Black,
                System.Drawing.Color.FromArgb(107, 114, 128),   // grey
                System.Drawing.Color.White,
                System.Drawing.Color.FromArgb(146, 64, 14),     // brown
                System.Drawing.Color.FromArgb(220, 38, 38),     // red
                System.Drawing.Color.FromArgb(249, 115, 22),    // orange
                System.Drawing.Color.FromArgb(250, 204, 21),    // yellow
                System.Drawing.Color.FromArgb(34, 197, 94),     // green
                System.Drawing.Color.FromArgb(20, 184, 166),    // teal
                System.Drawing.Color.FromArgb(56, 189, 248),    // sky
                System.Drawing.Color.FromArgb(37, 99, 235),     // blue
                System.Drawing.Color.FromArgb(124, 58, 237),    // violet
                System.Drawing.Color.FromArgb(236, 72, 153)     // pink
            };
            var swatches = new System.Collections.Generic.List<System.Windows.Forms.Control>();
            var swCustom = new Aplat.UI.ColorSwatch { Height = 30, Diameter = 26, IsCustom = true };
            swCustom.Click += new System.EventHandler(btnBrushColor_Click);
            swatches.Add(swCustom);
            foreach (System.Drawing.Color swc in quickPalette)
            {
                var sw = new Aplat.UI.ColorSwatch { Height = 30, Diameter = 26, BackColor = swc };
                sw.Click += new System.EventHandler(paletteSwatch_Click);
                swatches.Add(sw);
            }
            this.palettePanel.AddGrid(7, swatches.ToArray());

            this.lblSize      = SmallLabel(Loc.T("Размер кисти: 28px"));
            this.trkBrushSize = PanelSlider(1, 120, 28, 1, 10);
            this.trkBrushSize.Scroll += new System.EventHandler(trkBrushSize_Scroll);

            this.lblOpacity   = SmallLabel(Loc.T("Непрозрачность: 100%"));
            this.trkOpacity   = PanelSlider(1, 100, 100, 1, 10);
            this.trkOpacity.Scroll += new System.EventHandler(trkOpacity_Scroll);

            this.lblFill = SmallLabel(Loc.T("Сила заливки: 19%"));
            this.trkFill = PanelSlider(0, 100, 19, 1, 10);
            this.trkFill.Scroll += new System.EventHandler(trkFill_Scroll);
            this.lblFill.Visible = this.trkFill.Visible = false;

            this.lblShapes2D      = SectionLabel(Loc.T("Фигуры"));
            this.btnShapeLine     = TileBtn(Loc.T("Линия"),          Aplat.UI.UiIcon.Line,     btnShapeLine_Click);
            this.btnShapeRect     = TileBtn(Loc.T("Прямоугольник"),  Aplat.UI.UiIcon.Rect,     btnShapeRect_Click);
            this.btnShapeEllipse  = TileBtn(Loc.T("Эллипс"),         Aplat.UI.UiIcon.Ellipse,  btnShapeEllipse_Click);
            this.btnShapeTriangle = TileBtn(Loc.T("Треугольник"),    Aplat.UI.UiIcon.Triangle, btnShapeTriangle_Click);

            // Look of closed shapes (for the next one and the live one): a segmented
            // control of shape swatches in the brush colour — outline · solid · tinted.
            // Shown only while a rectangle / ellipse / triangle is in play.
            this.lblShapeStyle = SmallLabel(Loc.T("Вид фигуры: Контур"));
            this.segShapeStyle = new Aplat.UI.SegmentedControl
            {
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0),
            };
            this.segShapeStyle.AddSegment(Loc.T("Контур"),
                (g2, r2, sel) => PaintShapeSwatch(g2, r2, Aplat.Controls.ShapeFill.Outline));
            this.segShapeStyle.AddSegment(Loc.T("Заливка"),
                (g2, r2, sel) => PaintShapeSwatch(g2, r2, Aplat.Controls.ShapeFill.Fill));
            this.segShapeStyle.AddSegment(Loc.T("Контур и лёгкая заливка тем же цветом"),
                (g2, r2, sel) => PaintShapeSwatch(g2, r2, Aplat.Controls.ShapeFill.Both));
            this.segShapeStyle.SelectedIndexChanged += (s2, e2) =>
                SetShapeFill((Aplat.Controls.ShapeFill)this.segShapeStyle.SelectedIndex);
            this.lblShapeStyle.Visible = this.segShapeStyle.Visible = false;

            this.lblCanvas      = SectionLabel(Loc.T("Холст"));
            this.btnClearCanvas = LB(Loc.T("Очистить холст"), Aplat.UI.UiIcon.Clear, btnClearCanvas_Click);

            this.inspPaint = new Aplat.UI.InspectorPanel();
            this.inspPaint.AddRow(lblColor);
            this.inspPaint.AddRow(btnBrushColor);
            this.inspPaint.AddRow(palettePanel);
            // The eyedropper's option sits right under the colours, where the eye is, not at the foot of a long panel.
            BuildEyedropperRow();
            this.inspPaint.AddRow(chkEyedropperReturn);
            this.inspPaint.AddRow(lblSize);
            this.inspPaint.AddRow(trkBrushSize);
            this.inspPaint.AddRow(lblOpacity);
            this.inspPaint.AddRow(trkOpacity);
            this.inspPaint.AddRow(lblFill);
            this.inspPaint.AddRow(trkFill);
            // Right under the brush: with the eraser / text tool it is what you reach for.
            BuildEraserSection();
            this.inspPaint.AddRow(lblEraserSection);
            this.inspPaint.AddRow(segEraserKind);
            BuildSelectSection();
            this.inspPaint.AddRow(lblSelectSection);
            this.inspPaint.AddRow(segSelectKind);
            BuildTextSection();
            this.inspPaint.AddRow(lblTextSection);
            this.inspPaint.AddRow(cmbTextFont);
            this.inspPaint.AddGrid(2, numTextSize, tglTextStyle);
            this.inspPaint.AddRow(segTextAlign);
            this.inspPaint.AddRow(lblShapes2D);
            this.inspPaint.AddGrid(2, btnShapeLine, btnShapeRect, btnShapeEllipse, btnShapeTriangle);
            this.inspPaint.AddRow(lblShapeStyle);
            this.inspPaint.AddRow(segShapeStyle);
            this.inspPaint.AddRow(lblCanvas);
            this.inspPaint.AddRow(btnClearCanvas);

            // Hover hints for the paint tools (hotkeys + drag modifiers).
            var tips = new System.Windows.Forms.ToolTip(this.components);
            Aplat.UI.ThemeManager.Style(tips);
            this.panelTips = tips;
            tips.SetToolTip(this.btnBrush,        Loc.T("Кисть — мягкий мазок (B)"));
            tips.SetToolTip(this.btnPencil,       Loc.T("Карандаш — тонкая жёсткая линия"));
            tips.SetToolTip(this.btnPen,          Loc.T("Ручка — тонкая точная линия"));
            tips.SetToolTip(this.btnMarker,       Loc.T("Маркер — широкий полупрозрачный штрих"));
            tips.SetToolTip(this.btnSelect2D,     Loc.T("Выделить область — двигать, растягивать, вращать"));
            tips.SetToolTip(this.btnEyedropper,   Loc.T("Пипетка — взять цвет с холста"));
            tips.SetToolTip(this.btnFill,         Loc.T("Заливка области одним цветом"));
            tips.SetToolTip(this.btnEraser,       Loc.T("Ластик (E): точный или объектами"));
            tips.SetToolTip(this.btnHand,         Loc.T("Рука — перемещение по холсту. Выбирать объекты она не умеет — для этого «Выбор» (F1)"));
            tips.SetToolTip(this.btnCursor,       Loc.T("Выбор — обычный курсор: выделять объекты и живые элементы, не рисуя (F1, Esc)"));
            tips.SetToolTip(this.btnText2D,       Loc.T("Текст на холсте"));
            tips.SetToolTip(this.btnRuler,        Loc.T("Линейка (R): рисуйте по её краю — линия выйдет ровной"));
            tips.SetToolTip(this.btnShapeLine,    Loc.T("Линия. Shift — ровно по горизонтали / вертикали / 45°"));
            tips.SetToolTip(this.btnShapeRect,    Loc.T("Прямоугольник. Shift — квадрат; после — подгонка за точки"));
            tips.SetToolTip(this.btnShapeEllipse, Loc.T("Эллипс. Shift — круг; после — подгонка за точки"));
            tips.SetToolTip(this.btnShapeTriangle,Loc.T("Треугольник. Shift — правильный; после — подгонка за точки"));
            tips.SetToolTip(this.btnBrushColor,   Loc.T("Цвет кисти (быстрые цвета — в палитре ниже)"));
            tips.SetToolTip(swCustom,             Loc.T("Любой цвет…"));
            tips.SetToolTip(this.trkBrushSize,    Loc.T("Размер кисти или толщина линии — также клавиши [ и ]"));
            tips.SetToolTip(this.trkFill,         Loc.T("Насколько цвет может отличаться от выбранного и всё равно закрасится: 0 — только такой же, 100 — всё подряд"));
            tips.SetToolTip(this.trkOpacity,      Loc.T("Непрозрачность мазка и фигур"));

            this.tabPaint = ToolTab("2D");
            this.tabPaint.Controls.Add(Scroller(this.inspPaint));

            // ── "3D" tab: primitives · scene ──────────────────────
            this.lblShapes    = SectionLabel(Loc.T("Фигуры"));
            this.btnCube      = TileBtn(Loc.T("Куб"),            Aplat.UI.UiIcon.Cube,        btnCube_Click);
            this.btnSphere    = TileBtn(Loc.T("Сфера"),          Aplat.UI.UiIcon.Sphere,      btnSphere_Click);
            this.btnCylinder  = TileBtn(Loc.T("Цилиндр"),        Aplat.UI.UiIcon.Cylinder,    btnCylinder_Click);
            this.btnCone      = TileBtn(Loc.T("Конус"),          Aplat.UI.UiIcon.Cone,        btnCone_Click);
            this.btnTetra     = TileBtn(Loc.T("Тетраэдр"),       Aplat.UI.UiIcon.Tetrahedron, btnTetra_Click);
            this.btnPyramid   = TileBtn(Loc.T("Пирамида"),       Aplat.UI.UiIcon.Pyramid,     btnPyramid_Click);
            this.btnTorus     = TileBtn(Loc.T("Тор"),            Aplat.UI.UiIcon.Torus,       btnTorus_Click);
            this.btnCapsule   = TileBtn(Loc.T("Капсула"),        Aplat.UI.UiIcon.Capsule,     btnCapsule_Click);
            this.btnText      = TileBtn(Loc.T("Объёмный текст"), Aplat.UI.UiIcon.Text,        btnText_Click);
            this.btnText.Visible = Features.Text3D;
            this.lblScene3D   = SectionLabel(Loc.T("Сцена"));
            this.btnClear3D   = LB(Loc.T("Очистить все фигуры"), Aplat.UI.UiIcon.Clear,   btnClear3D_Click);

            this.insp3D = new Aplat.UI.InspectorPanel();
            this.insp3D.AddRow(lblShapes);
            this.insp3D.AddGrid(2, btnCube, btnSphere, btnCylinder, btnCone, btnTetra, btnPyramid, btnTorus, btnCapsule, btnText);
            this.insp3D.AddRow(lblScene3D);
            this.insp3D.AddRow(btnClear3D);

            this.tab3D = ToolTab("3D");
            this.tab3D.Controls.Add(Scroller(this.insp3D));

            // The shape tiles say what will appear and where: they used to have no
            // tooltips at all, and a caption like "Tetrahedron" explains little.
            tips.SetToolTip(this.btnCube,     Loc.T("Куб — базовый примитив, появляется в центре сцены"));
            tips.SetToolTip(this.btnSphere,   Loc.T("Сфера — сглаженная, удобна для проверки материалов"));
            tips.SetToolTip(this.btnCylinder, Loc.T("Цилиндр — труба, колонна, ось"));
            tips.SetToolTip(this.btnCone,     Loc.T("Конус — круглое основание, одна вершина"));
            tips.SetToolTip(this.btnTetra,    Loc.T("Тетраэдр — четыре треугольные грани"));
            tips.SetToolTip(this.btnPyramid,  Loc.T("Пирамида — квадратное основание, одна вершина"));
            tips.SetToolTip(this.btnTorus,    Loc.T("Тор — кольцо («бублик»)"));
            tips.SetToolTip(this.btnText,     Loc.T("Объёмная надпись; текст потом можно изменить (ПКМ по объекту)"));
            tips.SetToolTip(this.btnClear3D,  Loc.T("Удалить все фигуры сцены. Отменяется через Ctrl+Z"));
            tips.SetToolTip(this.btnClearCanvas, Loc.T("Стереть весь рисунок с холста. Отменяется через Ctrl+Z"));

            // ── "Object" tab — selected-object properties editor ──
            // PropertiesPanel scrolls itself (its own ScrollHost).
            this.propPanel = new Aplat.Controls.PropertiesPanel();
            this.propPanel.BackColor = ClCard;
            this.propPanel.Dock      = System.Windows.Forms.DockStyle.Fill;

            this.tabObject = ToolTab(Loc.T("Объект"));
            this.tabObject.Controls.Add(this.propPanel);

            // ── "Layers" — checkable, renamable, multi-select object list. The
            //    right-click action menu is built in MainForm.BuildContextMenu.
            this.listObjects = new System.Windows.Forms.ListView();
            this.listObjects.Dock          = System.Windows.Forms.DockStyle.Fill;
            this.listObjects.BorderStyle   = System.Windows.Forms.BorderStyle.None;
            this.listObjects.BackColor     = ClCard;
            this.listObjects.ForeColor     = ClText;
            this.listObjects.Font          = new System.Drawing.Font(FontName, 9.25F);
            this.listObjects.View          = System.Windows.Forms.View.Details;
            this.listObjects.HeaderStyle   = System.Windows.Forms.ColumnHeaderStyle.None;
            this.listObjects.CheckBoxes    = false;
            this.listObjects.OwnerDraw     = true;
            this.listObjects.MultiSelect   = true;   // Ctrl/Shift multi-select for grouping
            this.listObjects.FullRowSelect = true;
            this.listObjects.HideSelection = false;
            this.listObjects.LabelEdit     = false;  // rename via double-click / right-click menu
            // A 1×30 image list is the standard way to give a ListView a fixed row
            // height: 30px rows fit the 24px eye / lock hit targets with even air.
            this.listObjects.SmallImageList = new System.Windows.Forms.ImageList
            {
                ImageSize = new System.Drawing.Size(1, LayerRowHeight)
            };
            this.listObjects.Columns.Add(Loc.T("Объект"), 260);
            this.listObjects.SelectedIndexChanged += new System.EventHandler(listObjects_SelectedIndexChanged);
            this.listObjects.DoubleClick          += new System.EventHandler(listObjects_DoubleClick);
            this.listObjects.Resize               += new System.EventHandler(listObjects_ResizeForDesigner);
            this.listObjects.DrawColumnHeader     += new System.Windows.Forms.DrawListViewColumnHeaderEventHandler(listObjects_DrawColumnHeader);
            this.listObjects.DrawSubItem          += new System.Windows.Forms.DrawListViewSubItemEventHandler(listObjects_DrawSubItem);

            this.tabsTools = new Aplat.UI.ModernTabControl();
            this.tabsTools.Dock      = System.Windows.Forms.DockStyle.Fill;
            this.tabsTools.BackColor = ClCard;
            this.tabsTools.Font      = new System.Drawing.Font(FontName, 9.15F);
            this.tabsTools.TabPages.Add(this.tabPaint);
            this.tabsTools.TabPages.Add(this.tab3D);
            this.tabsTools.TabPages.Add(this.tabObject);

            // Docked flush to the right edge; the 1px left padding shows the panel's
            // BorderSoft background as the divider against the viewport.
            this.panelRight = new System.Windows.Forms.Panel();
            this.panelRight.Dock      = System.Windows.Forms.DockStyle.Right;
            this.panelRight.Width     = 296;
            this.panelRight.BackColor = ClBorderSoft;
            this.panelRight.Padding   = new System.Windows.Forms.Padding(1, 0, 0, 0);
            this.panelRight.Controls.Add(this.tabsTools);

            // ── Floating overlay: frequently used paint tools ─────
            StyleFloatingToolButton(this.btnBrush);
            StyleFloatingToolButton(this.btnPencil);
            StyleFloatingToolButton(this.btnPen);
            StyleFloatingToolButton(this.btnMarker);
            StyleFloatingToolButton(this.btnSelect2D);
            StyleFloatingToolButton(this.btnText2D);
            StyleFloatingToolButton(this.btnFill);
            StyleFloatingToolButton(this.btnEyedropper);
            StyleFloatingToolButton(this.btnEraser);
            StyleFloatingToolButton(this.btnHand);
            StyleFloatingToolButton(this.btnCursor);
            StyleFloatingToolButton(this.btnRuler);

            this.floatingToolsFlow = HudFlow(new System.Windows.Forms.Padding(6, 7, 4, 7));
            // Groups: mark-making | colour & erase | navigate & select.
            this.floatingToolsFlow.Controls.AddRange(new System.Windows.Forms.Control[] {
                btnBrush, btnPencil, btnPen, btnMarker, btnText2D, ToolDivider(40, 5, 7),
                btnFill, btnEyedropper, btnEraser, ToolDivider(40, 5, 7),
                btnCursor, btnHand, btnSelect2D, ToolDivider(40, 5, 7), btnRuler });
            this.floatingToolsPanel = OverlayPanel(FlowWidth(this.floatingToolsFlow), 54);
            this.floatingToolsPanel.Controls.Add(this.floatingToolsFlow);

            // ── Floating overlay: recent tools on the left ────────
            // Sized by RenderRecentTools: 36px buttons, 4px gaps, 5px inset all round.
            this.recentToolsPanel = OverlayPanel(46, 46);
            this.recentToolsPanel.Padding = new System.Windows.Forms.Padding(5);
            this.recentToolsFlow = new System.Windows.Forms.FlowLayoutPanel
            {
                Dock          = System.Windows.Forms.DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
                WrapContents  = false,
                AutoScroll    = false,
                BackColor     = ClCard,
                Padding       = new System.Windows.Forms.Padding(0)
            };
            this.recentToolsPanel.Controls.Add(this.recentToolsFlow);

            // ── Floating overlay: layers ──────────────────────────
            // Header and rows share one grid: 12px text inset; the "+" sits centred
            // over the lock column so the right edge lines up too.
            this.layersPanel = OverlayPanel(232, 212);
            this.layersPanel.Padding = new System.Windows.Forms.Padding(0);
            this.layersHeader = new System.Windows.Forms.Panel
            {
                Dock      = System.Windows.Forms.DockStyle.Top,
                Height    = 40,
                BackColor = ClCard,
                Padding   = new System.Windows.Forms.Padding(12, 6, 4, 6),
                Cursor    = System.Windows.Forms.Cursors.SizeAll
            };
            var layersHeader = this.layersHeader;
            var layersTitle = new System.Windows.Forms.Label
            {
                Text      = Loc.T("Объекты"),
                Dock      = System.Windows.Forms.DockStyle.Fill,
                ForeColor = ClText,
                Font      = new System.Drawing.Font(FontName, 9.2F, System.Drawing.FontStyle.Bold),
                Padding   = new System.Windows.Forms.Padding(0),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            this.btnLayersAdd = OverlayIconButton(Aplat.UI.UiIcon.Plus, Loc.T("Добавить фигуру"), btnLayersAdd_Click);
            this.btnLayersAdd.Dock   = System.Windows.Forms.DockStyle.Right;
            this.btnLayersAdd.Width  = 28;
            this.btnLayersAdd.Margin = new System.Windows.Forms.Padding(0);
            tips.SetToolTip(this.btnLayersAdd, Loc.T("Добавить фигуру"));
            var layersRule = new System.Windows.Forms.Panel
            {
                Dock      = System.Windows.Forms.DockStyle.Top,
                Height    = 1,
                BackColor = ClBorderSoft
            };
            layersHeader.Controls.Add(layersTitle);
            layersHeader.Controls.Add(this.btnLayersAdd);
            this.listObjects.Columns[0].Width = 230;
            this.layersPanel.Controls.Add(this.listObjects);
            this.layersPanel.Controls.Add(layersRule);
            this.layersPanel.Controls.Add(layersHeader);

            // ── Floating overlay: view controls ───────────────────
            // zoom readout + slider | fit · grid · guides · view | fly speed
            this.viewPanel = OverlayPanel(400, 46);
            this.viewPanel.Padding = new System.Windows.Forms.Padding(0);
            this.viewFlow = HudFlow(new System.Windows.Forms.Padding(8, 8, 8, 8));
            var viewFlow = this.viewFlow;
            this.lblViewZoom = new Aplat.UI.HudReadout(Aplat.UI.UiIcon.Zoom)
            {
                Text   = "100%",
                Width  = 62,
                Height = 30,
                Margin = new System.Windows.Forms.Padding(0)
            };
            this.trkViewZoom = new Aplat.UI.ModernSlider
            {
                Width         = 84,
                Height        = 30,
                Minimum       = 25,
                Maximum       = 400,
                Value         = 100,
                BackColor     = ClCard,
                Margin        = new System.Windows.Forms.Padding(0)
            };
            this.trkViewZoom.Scroll += new System.EventHandler(trkViewZoom_Scroll);
            this.btnViewFit    = OverlayIconButton(Aplat.UI.UiIcon.ZoomFit, Loc.T("Показать всё (Home)"), btnViewFit_Click);
            this.btnViewGrid   = OverlayIconButton(Aplat.UI.UiIcon.Grid, Loc.T("Сетка (F8)"), btnViewGrid_Click);
            this.btnViewGuides = OverlayIconButton(Aplat.UI.UiIcon.Guides, Loc.T("Умные направляющие — примагничивание к центрам соседних объектов"), btnViewGuides_Click);
            this.btnViewCenter = OverlayIconButton(Aplat.UI.UiIcon.ViewFront, Loc.T("Ракурс: спереди / сбоку / сверху (1 / 3 / 7), перспектива — 0"), btnViewCenter_Click);
            this.btnViewCenter.Margin = new System.Windows.Forms.Padding(0);
            this.lblViewSpeed = new Aplat.UI.HudReadout(Aplat.UI.UiIcon.Speed)
            {
                Text   = "1.0x",
                Width  = 58,
                Height = 30,
                Margin = new System.Windows.Forms.Padding(0)
            };
            viewFlow.Controls.AddRange(new System.Windows.Forms.Control[] {
                lblViewZoom, trkViewZoom, ToolDivider(30, 8, 8),
                btnViewFit, btnViewGrid, btnViewGuides, btnViewCenter, ToolDivider(30, 8, 8),
                lblViewSpeed });
            foreach (System.Windows.Forms.Control c in viewFlow.Controls)
                if (c.Tag is string) tips.SetToolTip(c, (string)c.Tag);
            tips.SetToolTip(this.lblViewZoom,  Loc.T("Масштаб вида (Ctrl + колесо)"));
            tips.SetToolTip(this.trkViewZoom,  Loc.T("Масштаб вида"));
            tips.SetToolTip(this.lblViewSpeed, Loc.T("Скорость полёта камеры (WASD / QE; колесо во время полёта)"));
            this.viewPanel.Width = FlowWidth(viewFlow);
            this.viewPanel.Controls.Add(viewFlow);

            // ═══════════════════════════════════════════════════════
            // SceneView — center fill
            // ═══════════════════════════════════════════════════════
            this.sceneView      = new Aplat.Controls.SceneView();
            this.sceneView.Dock = System.Windows.Forms.DockStyle.Fill;

            // ═══════════════════════════════════════════════════════
            // Main panel (holds left + scene + right, insulated from strips)
            // ═══════════════════════════════════════════════════════
            this.panelMain = new System.Windows.Forms.Panel();
            this.panelMain.Dock      = System.Windows.Forms.DockStyle.Fill;
            this.panelMain.BackColor = ClSurface;
            // Add in correct dock order: Fill first, then Right. The four overlay cards
            // are NOT added here — MainForm.InitOverlays hosts them in floating
            // OverlayWindows positioned over the scene area.
            this.panelMain.Controls.Add(this.sceneView);
            this.panelMain.Controls.Add(this.panelRight);
            this.panelMain.Resize += new System.EventHandler(panelMain_ResizeOverlays);

            // ═══════════════════════════════════════════════════════
            // Form
            // ═══════════════════════════════════════════════════════
            // Add in order: Fill first, then Bottom, then Top strips last (last added = topmost).
            // toolStrip2 before toolStrip so row 1 is above row 2.
            // Toolbars live in four dock panels (Visual-Studio-style): draggable by the
            // grip to any edge, floatable, persisted. Default: both across the top.
            this.toolbarDock = new Aplat.UI.ToolbarDock(this, new Aplat.UI.ModernToolStripRenderer());
            this.toolbarDock.Register(this.toolStrip,  "file",  Loc.T("Файл"),   0);
            this.toolbarDock.Register(this.toolStrip2, "modes", Loc.T("Режимы"), 1);

            this.Controls.Add(this.panelMain);
            this.Controls.Add(this.toolbarDock.Left);
            this.Controls.Add(this.toolbarDock.Right);
            this.Controls.Add(this.toolbarDock.Bottom);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolbarDock.Top);
            this.Controls.Add(this.menuStrip);

            // Drop-downs share their strip's renderer (menus, history, submenus).
            Aplat.UI.ThemeManager.ShareRenderer(this.menuStrip);
            Aplat.UI.ThemeManager.ShareRenderer(this.toolStrip);

            this.MainMenuStrip       = this.menuStrip;
            this.BackColor           = ClBg;
            this.ClientSize          = new System.Drawing.Size(1040, 660);
            this.MinimumSize         = new System.Drawing.Size(940, 600);
            this.Text                = "Aplat";
            this.StartPosition       = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;

            // Load application icon from the embedded Win32 resources (set by <ApplicationIcon>).
            Aplat.UI.AppIcon.Apply(this);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // ── Helpers ───────────────────────────────────────────────

        // The tab's scrolling viewport around an inspector.
        private static Aplat.UI.ScrollHost Scroller(System.Windows.Forms.Control content)
        {
            return new Aplat.UI.ScrollHost
            {
                Dock      = System.Windows.Forms.DockStyle.Fill,
                BackColor = ClCard,
                Content   = content
            };
        }

        // A full-width inspector slider under its ValueLabel caption.
        private static Aplat.UI.ModernSlider PanelSlider(int min, int max, int value, int small, int large)
        {
            return new Aplat.UI.ModernSlider
            {
                Minimum     = min,
                Maximum     = max,
                Value       = value,
                SmallChange = small,
                LargeChange = large,
                Height      = 26,
                BackColor   = ClCard,
                Margin      = new System.Windows.Forms.Padding(0, 0, 0, 0)
            };
        }

        private static System.Windows.Forms.TabPage ToolTab(string text)
        {
            return new System.Windows.Forms.TabPage(text)
            {
                BackColor               = ClCard,
                UseVisualStyleBackColor = false,
                Padding                 = new System.Windows.Forms.Padding(0)
            };
        }

        private static System.Windows.Forms.ToolStripMenuItem MI(string text,
            System.EventHandler handler, System.Windows.Forms.Keys shortcut, Aplat.UI.UiIcon icon)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(text);
            item.Click += handler;
            if (shortcut != System.Windows.Forms.Keys.None) item.ShortcutKeys = shortcut;
            if (icon != Aplat.UI.UiIcon.None)
            {
                item.Image = Aplat.UI.IconFactory.Create(icon, ClText, 18);
                item.Tag = icon;
            }
            return item;
        }

        // A menu item whose shortcut is only DISPLAYED. The key itself is handled in
        // ProcessCmdKey; registering it as ShortcutKeys too would let the menu take it
        // away from input fields (Delete in an object-name box would delete the object).
        private static System.Windows.Forms.ToolStripMenuItem MID(string text,
            System.EventHandler handler, string keyText, Aplat.UI.UiIcon icon)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(text);
            item.Click += handler;
            if (!string.IsNullOrEmpty(keyText))
            {
                item.ShowShortcutKeys = true;
                item.ShortcutKeyDisplayString = keyText;
            }
            if (icon != Aplat.UI.UiIcon.None)
            {
                item.Image = Aplat.UI.IconFactory.Create(icon, ClText, 18);
                item.Tag = icon;
            }
            return item;
        }

        // A theme item. The mode is kept in _themeItems: the item's Tag holds its icon.
        private System.Windows.Forms.ToolStripMenuItem ThemeItem(string text, Aplat.UI.ThemeMode mode, Aplat.UI.UiIcon icon)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(text);
            item.Image = Aplat.UI.IconFactory.Create(icon, ClText, 18);
            item.Tag   = icon;
            item.Click += (s, e) => SetThemeMode(mode);
            _themeItems[item] = mode;
            return item;
        }

        // A "rotation speed" item: the value hides in Tag, one handler serves them all.
        private System.Windows.Forms.ToolStripMenuItem SpinSpeedItem(string text, float degPerSec)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(text);
            item.Tag = degPerSec;
            item.Click += new System.EventHandler(menuSpinSpeed_Click);
            return item;
        }

        private static System.Windows.Forms.ToolStripButton TB(string text, string tip, Aplat.UI.UiIcon icon)
        {
            return new System.Windows.Forms.ToolStripButton
            {
                Text         = text,
                ToolTipText  = tip,
                ForeColor    = ClText,
                Image        = Aplat.UI.IconFactory.Create(icon, ClText, 18),
                Tag          = icon,
                ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None,
                TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText,
                Padding      = new System.Windows.Forms.Padding(9, 3, 9, 3),
                Margin       = new System.Windows.Forms.Padding(1, 0, 3, 0),
                DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText
            };
        }

        private static System.Windows.Forms.ToolStripButton TBCheck(string text, string tip, Aplat.UI.UiIcon icon)
        {
            return new System.Windows.Forms.ToolStripButton
            {
                Text         = text,
                ToolTipText  = tip,
                ForeColor    = ClText,
                Image        = Aplat.UI.IconFactory.Create(icon, ClText, 18),
                Tag          = icon,
                ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None,
                TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText,
                Padding      = new System.Windows.Forms.Padding(9, 3, 9, 3),
                Margin       = new System.Windows.Forms.Padding(1, 0, 3, 0),
                DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText,
                CheckOnClick = false
            };
        }

        // Text-only mode button; its icon (Tag) is shown instead of the text when the
        // toolbar is docked vertically.
        private static System.Windows.Forms.ToolStripButton TBCheckText(string text, string tip, Aplat.UI.UiIcon icon)
        {
            return new System.Windows.Forms.ToolStripButton
            {
                Text         = text,
                ToolTipText  = tip,
                ForeColor    = ClText,
                Image        = Aplat.UI.IconFactory.Create(icon, ClText, 18),
                Tag          = icon,
                ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None,
                Padding      = new System.Windows.Forms.Padding(10, 3, 10, 3),
                Margin       = new System.Windows.Forms.Padding(1, 0, 3, 0),
                DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text,
                CheckOnClick = false
            };
        }

        private static System.Windows.Forms.ToolStripDropDownButton TBDrop(string text, string tip, Aplat.UI.UiIcon icon)
        {
            return new System.Windows.Forms.ToolStripDropDownButton
            {
                Text         = text,
                ToolTipText  = tip,
                ForeColor    = ClText,
                Image        = Aplat.UI.IconFactory.Create(icon, ClText, 18),
                Tag          = icon,
                ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None,
                TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText,
                Padding      = new System.Windows.Forms.Padding(8, 3, 8, 3),
                Margin       = new System.Windows.Forms.Padding(3, 0, 1, 0),
                DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image
            };
        }

        // Row height of the layers list (see the ImageList trick where it's built).
        private const int LayerRowHeight = 30;

        // ERASER: the two ways to erase, one tap apart — "Precise" wipes pixels under the
        // brush, "Object" takes a whole stroke / shape / text / fill (the iPhone eraser).
        private void BuildEraserSection()
        {
            this.lblEraserSection = SectionLabel(Loc.T("Ластик"));
            this.segEraserKind = new Aplat.UI.SegmentedControl
            {
                Height = 34,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0),
            };
            this.segEraserKind.AddSegment(Loc.T("Точный — стирает пиксели под кистью"),
                (g2, r2, sel) => PaintEraserSegment(g2, r2, sel, Aplat.UI.UiIcon.Eraser, Loc.T("Точный")));
            this.segEraserKind.AddSegment(Loc.T("Объект — стирает штрих, фигуру, текст или заливку целиком (E)"),
                (g2, r2, sel) => PaintEraserSegment(g2, r2, sel, Aplat.UI.UiIcon.EraserObject, Loc.T("Объект")));
            this.segEraserKind.SelectedIndexChanged += (s2, e2) =>
                SetBrush(this.segEraserKind.SelectedIndex == 1 ? Aplat.Controls.BrushKind.ObjectEraser
                                                               : Aplat.Controls.BrushKind.Eraser);
            this.lblEraserSection.Visible = this.segEraserKind.Visible = false;   // with the eraser (UpdateShapeStyleRow)
        }

        // TEXT: font (each family shown in its own face in the list), size in points,
        // B / I / U toggles and alignment.
        private void BuildTextSection()
        {
            this.lblTextSection = SectionLabel(Loc.T("Текст"));

            this.cmbTextFont = new System.Windows.Forms.ComboBox
            {
                DropDownStyle    = System.Windows.Forms.ComboBoxStyle.DropDownList,
                FlatStyle        = System.Windows.Forms.FlatStyle.Flat,
                Font             = new System.Drawing.Font(FontName, 9F),
                BackColor        = Aplat.UI.Theme.Input,
                ForeColor        = ClText,
                MaxDropDownItems = 16,
                Margin           = new System.Windows.Forms.Padding(0),
            };
            foreach (var ff in System.Drawing.FontFamily.Families) this.cmbTextFont.Items.Add(ff.Name);
            Aplat.UI.ThemeManager.ThemeCombo(this.cmbTextFont);
            var faces = new System.Collections.Generic.Dictionary<string, System.Drawing.Font>();
            Aplat.UI.ThemeManager.SetComboRowFont(this.cmbTextFont, name =>
            {
                System.Drawing.Font f;
                if (faces.TryGetValue(name, out f)) return f;
                try { f = new System.Drawing.Font(name, 10F); } catch { f = null; }
                faces[name] = f;
                return f;
            });
            this.cmbTextFont.SelectedIndexChanged += (s2, e2) => PushTextFormat();

            this.numTextSize = new Aplat.UI.DragNumber
            {
                Caption = Loc.T("Размер"), Suffix = Loc.T("пт"),
                Font    = new System.Drawing.Font(FontName, 8.75F),
                Height  = 28,
                Margin  = new System.Windows.Forms.Padding(0, 8, 0, 0),
            };
            this.numTextSize.Minimum = 4m; this.numTextSize.Maximum = 400m;
            this.numTextSize.DecimalPlaces = 0; this.numTextSize.Increment = 1m;
            this.numTextSize.DragStep = 0.5m; this.numTextSize.SnapStep = 4m;
            this.numTextSize.Value = 24m;
            this.numTextSize.ValueChanged += (s2, e2) => PushTextFormat();

            this.tglTextStyle = new Aplat.UI.ToggleStrip { Margin = new System.Windows.Forms.Padding(0, 8, 0, 0) };
            this.tglTextStyle.Add(Aplat.UI.UiIcon.Bold,      Loc.T("Жирный (Ctrl+B)"));
            this.tglTextStyle.Add(Aplat.UI.UiIcon.Italic,    Loc.T("Курсив (Ctrl+I)"));
            this.tglTextStyle.Add(Aplat.UI.UiIcon.Underline, Loc.T("Подчёркнутый (Ctrl+U)"));
            this.tglTextStyle.Toggled += (s2, e2) => PushTextFormat();

            this.segTextAlign = new Aplat.UI.SegmentedControl
            {
                Height = 30,
                Margin = new System.Windows.Forms.Padding(0, 8, 0, 0),
            };
            foreach (var a in new[] {
                new { Icon = Aplat.UI.UiIcon.AlignLeft,   Tip = Loc.T("По левому краю (Ctrl+L)") },
                new { Icon = Aplat.UI.UiIcon.AlignCenter, Tip = Loc.T("По центру (Ctrl+E)") },
                new { Icon = Aplat.UI.UiIcon.AlignRight,  Tip = Loc.T("По правому краю (Ctrl+R)") } })
            {
                var icon = a.Icon;
                this.segTextAlign.AddSegment(a.Tip, (g2, r2, sel) =>
                    Aplat.UI.IconFactory.Draw(g2, icon, sel ? Aplat.UI.Theme.Accent : Aplat.UI.Theme.TextSoft,
                        new System.Drawing.RectangleF(r2.X + (r2.Width - 16) / 2f, r2.Y + (r2.Height - 16) / 2f, 16, 16)));
            }
            this.segTextAlign.SelectedIndexChanged += (s2, e2) => PushTextFormat();

            foreach (System.Windows.Forms.Control c in new System.Windows.Forms.Control[] {
                lblTextSection, cmbTextFont, numTextSize, tglTextStyle, segTextAlign })
                c.Visible = false;   // shown with the text tool / a live text (UpdateShapeStyleRow)
        }

        private static System.Windows.Forms.Label SectionLabel(string text)
        {
            return new Aplat.UI.SectionHeader(text)
            {
                Margin = new System.Windows.Forms.Padding(0, 24, 0, 10)
            };
        }

        private static System.Windows.Forms.Label SmallLabel(string text)
        {
            // Slider caption "Name ···· value"; 16px from the row above, the slider
            // hangs directly under it.
            return new Aplat.UI.ValueLabel
            {
                Text      = text,
                Height    = 18,
                ForeColor = ClTextSoft,
                Font      = new System.Drawing.Font(FontName, 8.25F),
                Margin    = new System.Windows.Forms.Padding(0, 16, 0, 2),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
        }

        // Full-width action button (brush colour, "clear" actions).
        private static System.Windows.Forms.Button LB(string text, Aplat.UI.UiIcon icon,
            System.EventHandler handler)
        {
            var b = new Aplat.UI.RoundedButton
            {
                Text      = text,
                Icon      = icon,
                Height    = 40,
                Radius    = Aplat.UI.Theme.RadiusButton,
                Margin    = new System.Windows.Forms.Padding(0),
                BackColor = ClBtnFace,
                ForeColor = ClText,
                Font      = new System.Drawing.Font(FontName, 9.35F),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding   = new System.Windows.Forms.Padding(16, 0, 0, 0),
                TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText
            };
            b.Click += handler;
            return b;
        }

        // Tile: icon over label, for the 2-column shape grids (width comes from the grid).
        private static System.Windows.Forms.Button TileBtn(string text, Aplat.UI.UiIcon icon,
            System.EventHandler handler)
        {
            var b = new Aplat.UI.RoundedButton
            {
                Text      = text,
                Icon      = icon,
                IconSize  = 22,
                Width     = 120,
                Height    = 68,
                Radius    = Aplat.UI.Theme.RadiusTile,
                Margin    = new System.Windows.Forms.Padding(0),
                BackColor = ClBtnFace,
                ForeColor = ClText,
                Font      = new System.Drawing.Font(FontName, 8.6F),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Padding   = new System.Windows.Forms.Padding(0),
                TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText
            };
            b.Click += handler;
            return b;
        }

        // Plain content panel for an overlay card. The rounded silhouette, border and
        // translucency are provided by the hosting Aplat.UI.OverlayWindow.
        private static System.Windows.Forms.Panel OverlayPanel(int width, int height)
        {
            return new System.Windows.Forms.Panel
            {
                Width     = width,
                Height    = height,
                BackColor = ClCard
            };
        }

        // Floating tool: a 40px icon-only HUD button. The label stays in Text (for
        // accessibility) and in the tooltip, which already names the hotkey.
        private static void StyleFloatingToolButton(System.Windows.Forms.Button b)
        {
            b.Width      = 40;
            b.Height     = 40;
            b.Margin     = new System.Windows.Forms.Padding(0, 0, 2, 0);
            b.TextAlign  = System.Drawing.ContentAlignment.MiddleCenter;
            b.Padding    = new System.Windows.Forms.Padding(0);
            b.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            var rb = b as Aplat.UI.RoundedButton;
            if (rb != null)
            {
                rb.Radius   = Aplat.UI.Theme.RadiusButton;
                rb.Flat     = true;
                rb.IconOnly = true;
                rb.IconSize = 20;
            }
        }

        // Hairline between groups on a HUD bar, vertically centred in a row of
        // `rowHeight`; left/right spacing is given so both sides of the rule match
        // (the button before it may carry its own right margin).
        private static System.Windows.Forms.Control ToolDivider(int rowHeight, int left, int right)
        {
            int h = (int)System.Math.Round(rowHeight * 0.55);
            int top = (rowHeight - h) / 2;
            return new System.Windows.Forms.Panel
            {
                Width     = 1,
                Height    = h,
                BackColor = ClBorderSoft,
                Margin    = new System.Windows.Forms.Padding(left, top, right, rowHeight - h - top)
            };
        }

        // A single-row HUD strip (no wrap, no scroll) with the given inset.
        private static System.Windows.Forms.FlowLayoutPanel HudFlow(System.Windows.Forms.Padding padding)
        {
            return new System.Windows.Forms.FlowLayoutPanel
            {
                Dock          = System.Windows.Forms.DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                WrapContents  = false,
                AutoScroll    = false,
                BackColor     = ClCard,
                Padding       = padding
            };
        }

        // Exact width of a single-row flow: its padding plus every child with margins.
        // HUD cards are sized from their content, so both ends get the same inset.
        private static int FlowWidth(System.Windows.Forms.FlowLayoutPanel flow)
        {
            int w = flow.Padding.Horizontal;
            foreach (System.Windows.Forms.Control c in flow.Controls) w += c.Width + c.Margin.Horizontal;
            return w;
        }

        private static System.Windows.Forms.Button OverlayIconButton(Aplat.UI.UiIcon icon, string tip,
            System.EventHandler handler)
        {
            var b = new Aplat.UI.RoundedButton
            {
                Width     = 30,
                Height    = 30,
                Icon      = icon,
                IconSize  = 18,
                Radius    = Aplat.UI.Theme.RadiusButton,
                Flat      = true,
                Margin    = new System.Windows.Forms.Padding(0, 0, 2, 0),
                BackColor = ClBtnFace,
                ForeColor = ClText,
                Padding   = new System.Windows.Forms.Padding(0),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter
            };
            b.Click += handler;
            b.Tag = tip;
            return b;
        }

        private void listObjects_ResizeForDesigner(object sender, System.EventArgs e)
        {
            if (this.listObjects.Columns.Count > 0)
                this.listObjects.Columns[0].Width = this.listObjects.ClientSize.Width;
        }
    }
}
