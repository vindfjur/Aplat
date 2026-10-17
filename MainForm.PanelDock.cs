using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Aplat.UI;

namespace Aplat
{
    // Docking of the floating cards (tools, recent tool, objects, view) and the one Panels model that
    // covers them together with the toolbars:
    //  - a card is docked to one of eight anchors (corners and edge middles of the viewport) or free;
    //  - while it is dragged, a translucent outline shows where it would dock, and it is pulled towards
    //    neighbouring cards (hold Alt to move without any magnetism);
    //  - docked cards re-flow on every resize, so they stay anchored when the window is maximised;
    //  - the choice (shown, anchor / free position, transparency) is remembered in panels.txt.
    // The geometry itself lives in UI/PanelDocking.cs so it can be tested without windows.
    public partial class MainForm
    {
        private static string PanelsPath { get { return Path.Combine(SettingsDir, "panels.txt"); } }

        // Ids in the order the cards are created (see InitOverlays), and where each one starts.
        private static readonly string[] OverlayKeys = { "tools", "recent", "objects", "view", "spin" };
        private static readonly DockAnchor[] OverlayDefaults = { DockAnchor.Top, DockAnchor.Left, DockAnchor.BottomLeft, DockAnchor.Bottom, DockAnchor.BottomRight };

        private const double DefaultOverlayOpacity = 0.92;
        private const int CardSnap = 10;   // how close to a neighbouring card the magnetism reaches

        private DockPreview _dockPreview;
        private OverlayInfo _dragged;
        private DockAnchor _dragAnchor;

        // ── Layout ────────────────────────────────────────────────

        private Size SceneSize()
        {
            if (panelMain == null || panelRight == null) return Size.Empty;
            return new Size(Math.Max(0, panelMain.ClientSize.Width - panelRight.Width), panelMain.ClientSize.Height);
        }

        // A card is on screen when the user has not hidden it and, for a conditional card, its condition holds.
        private static bool OverlayShown(OverlayInfo ov)
        {
            return ov.Enabled && (ov.Active == null || ov.Active());
        }

        // Rectangles (viewport coordinates) of every card; `hidden` is left out as if it were off. With
        // `places` the cards are laid out as if those were their placements (drop preview).
        private Rectangle[] ComputeOverlayRects(Size scene, OverlayInfo hidden, PanelPlacement[] places = null)
        {
            int n = _overlays.Count;
            var sizes = new Size[n];
            var visible = new bool[n];
            var used = new PanelPlacement[n];
            for (int i = 0; i < n; i++)
            {
                var ov = _overlays[i];
                sizes[i] = ov.Window.Size;
                used[i] = places != null ? places[i] : ov.Placement;
                visible[i] = OverlayShown(ov) && (places != null || ov != hidden);
            }
            return PanelDocking.Layout(scene, sizes, used, visible);
        }

        // Puts every card where its placement says, inside the viewport. Runs on every resize / maximise.
        private void PositionOverlays()
        {
            PositionPerfHud();
            if (!_overlaysReady || panelMain == null || panelRight == null) return;
            if (!panelMain.IsHandleCreated) return;
            Size scene = SceneSize();
            if (scene.Width < 40 || scene.Height < 40) return;

            var rects = ComputeOverlayRects(scene, _dragged);
            Point origin = panelMain.PointToScreen(Point.Empty);
            for (int i = 0; i < _overlays.Count; i++)
            {
                var ov = _overlays[i];
                if (ov.Window == null || ov.Window.IsDisposed || ov == _dragged) continue;
                bool shown = OverlayShown(ov);
                if (ov.Window.Visible != shown) ov.Window.Visible = shown;
                if (!shown) continue;
                ov.Window.Location = new Point(origin.X + rects[i].X, origin.Y + rects[i].Y);
            }
        }

        // ── Dragging with snap guides ─────────────────────────────

        private void WireOverlayDrag(OverlayInfo info)
        {
            var w = info.Window;
            w.DragStarted      += (s, e) => { _dragged = info; };
            w.Dragging         += (s, e) => OnOverlayDragging(info, e);
            w.DragEnded        += (s, e) => OnOverlayDropped(info);
            w.ContextRequested += (s, at) => ShowOverlayMenu(info, at);
            w.SizeChanged      += (s, e) => { if (_overlaysReady && _dragged != info) SchedulePositionOverlays(); };
        }

        private void OnOverlayDragging(OverlayInfo info, OverlayDragEventArgs e)
        {
            Size scene = SceneSize();
            if (scene.IsEmpty || panelMain == null) return;
            Point origin = panelMain.PointToScreen(Point.Empty);
            var r = new Rectangle(e.Location.X - origin.X, e.Location.Y - origin.Y, info.Window.Width, info.Window.Height);
            bool magnetic = (Control.ModifierKeys & Keys.Alt) == 0;

            _dragAnchor = magnetic ? PanelDocking.HitAnchor(r, scene, PanelDocking.Zone) : DockAnchor.None;
            if (_dragAnchor != DockAnchor.None)
            {
                // Preview: lay the cards out as they would be after the drop.
                var rects = ComputeOverlayRects(scene, info);
                var hypo = DropPlacements(info, _dragAnchor, r, rects);
                var preview = ComputeOverlayRects(scene, info, hypo)[_overlays.IndexOf(info)];
                if (_dockPreview == null) _dockPreview = new DockPreview { Owner = this };
                _dockPreview.ShowAt(new Rectangle(origin.X + preview.X, origin.Y + preview.Y, preview.Width, preview.Height));
                return;
            }

            HideDockPreview();
            if (!magnetic) return;
            var others = new List<Rectangle>();
            var now = ComputeOverlayRects(scene, info);
            for (int i = 0; i < _overlays.Count; i++)
                if (_overlays[i] != info && OverlayShown(_overlays[i])) others.Add(now[i]);
            r = PanelDocking.SnapToCards(r, others, CardSnap);
            e.Location = new Point(origin.X + r.X, origin.Y + r.Y);
        }

        // The placements of all cards if `info` were dropped on `anchor` with its body at `body`.
        private PanelPlacement[] DropPlacements(OverlayInfo info, DockAnchor anchor, Rectangle body, Rectangle[] rects)
        {
            var places = new PanelPlacement[_overlays.Count];
            for (int i = 0; i < places.Length; i++) places[i] = _overlays[i].Placement.Clone();
            int d = _overlays.IndexOf(info);
            places[d] = PanelPlacement.Dock(anchor, 0);
            PanelDocking.InsertAt(places, rects, d, anchor, new Point(body.X + body.Width / 2, body.Y + body.Height / 2));
            return places;
        }

        private void OnOverlayDropped(OverlayInfo info)
        {
            HideDockPreview();
            Size scene = SceneSize();
            if (!scene.IsEmpty && panelMain != null)
            {
                Point origin = panelMain.PointToScreen(Point.Empty);
                var body = new Rectangle(info.Window.Left - origin.X, info.Window.Top - origin.Y, info.Window.Width, info.Window.Height);
                if (_dragAnchor != DockAnchor.None)
                {
                    var rects = ComputeOverlayRects(scene, info);
                    var places = DropPlacements(info, _dragAnchor, body, rects);
                    for (int i = 0; i < places.Length; i++) _overlays[i].Placement = places[i];
                }
                else
                {
                    info.Placement = PanelDocking.MakeFree(PanelDocking.ClampInside(body, scene), scene);
                }
            }
            _dragged = null;
            _dragAnchor = DockAnchor.None;
            PositionOverlays();
            SavePanelLayout();
        }

        private void HideDockPreview()
        {
            if (_dockPreview != null && _dockPreview.Visible) _dockPreview.Hide();
        }

        // ── Commands on a card ────────────────────────────────────

        // Docks a card to an anchor (at the end of the cards already there), or frees it where it stands.
        private void SetOverlayAnchor(OverlayInfo info, DockAnchor anchor)
        {
            Size scene = SceneSize();
            if (anchor == DockAnchor.None)
            {
                if (info.Placement.Docked && !scene.IsEmpty)
                {
                    var rects = ComputeOverlayRects(scene, null);
                    info.Placement = PanelDocking.MakeFree(rects[_overlays.IndexOf(info)], scene);
                }
            }
            else
            {
                int last = -1;
                foreach (var ov in _overlays)
                    if (ov != info && ov.Placement.Anchor == anchor) last = Math.Max(last, ov.Placement.Order);
                info.Placement = PanelPlacement.Dock(anchor, last + 1);
            }
            PositionOverlays();
            SavePanelLayout();
        }

        private void SetOverlayShown(OverlayInfo info, bool shown)
        {
            info.Enabled = shown;
            PositionOverlays();   // also shows / hides the window
            RefreshCardMenuChecks();
            SavePanelLayout();
        }

        private void ShowOverlayMenu(OverlayInfo info, Point screenAt)
        {
            var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.Professional, Renderer = new ModernToolStripRenderer(), Font = Theme.Font(9.25f) };
            var dock = new ToolStripMenuItem(Loc.T("Закрепить"));
            foreach (var a in DockAnchors)
            {
                DockAnchor anchor = a;
                dock.DropDownItems.Add(new ToolStripMenuItem(AnchorName(anchor), null, (s, e) => SetOverlayAnchor(info, anchor))
                { Checked = info.Placement.Anchor == anchor });
            }
            menu.Items.Add(dock);
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Свободно"), null, (s, e) => SetOverlayAnchor(info, DockAnchor.None))
            { Checked = !info.Placement.Docked });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Loc.T("Скрыть панель"), null, (s, e) => SetOverlayShown(info, false));
            menu.Items.Add(Loc.T("Положение и прозрачность..."), null, (s, e) => menuPanels_Click(s, e));
            menu.Closed += (s, e) => menu.BeginInvoke((Action)menu.Dispose);
            menu.Show(screenAt);
        }

        // Display order of the eight anchors in menus and the dialog (None = "free" comes first there).
        private static readonly DockAnchor[] DockAnchors =
        {
            DockAnchor.TopLeft, DockAnchor.Top, DockAnchor.TopRight, DockAnchor.Left,
            DockAnchor.Right, DockAnchor.BottomLeft, DockAnchor.Bottom, DockAnchor.BottomRight
        };

        private static string AnchorName(DockAnchor a)
        {
            switch (a)
            {
                case DockAnchor.TopLeft:     return Loc.T("Сверху слева");
                case DockAnchor.Top:         return Loc.T("Сверху");
                case DockAnchor.TopRight:    return Loc.T("Сверху справа");
                case DockAnchor.Left:        return Loc.T("Слева");
                case DockAnchor.Right:       return Loc.T("Справа");
                case DockAnchor.BottomLeft:  return Loc.T("Снизу слева");
                case DockAnchor.Bottom:      return Loc.T("Снизу");
                case DockAnchor.BottomRight: return Loc.T("Снизу справа");
                default:                     return Loc.T("Свободно");
            }
        }

        // ── View → Panels ─────────────────────────────────────────

        // Adds a check item per card to the Panels submenu (above the separator), in card order.
        private void AddCardMenuItems()
        {
            int at = menuPanels.DropDownItems.IndexOf(menuPanelsSep);
            foreach (var ov in _overlays)
            {
                var info = ov;
                info.MenuItem = new ToolStripMenuItem(info.Name, null, (s, e) => SetOverlayShown(info, !info.Enabled));
                menuPanels.DropDownItems.Insert(at++, info.MenuItem);
            }
            menuPanels.DropDownItems.Insert(at, new ToolStripSeparator());
            RefreshCardMenuChecks();
        }

        private void RefreshCardMenuChecks()
        {
            foreach (var ov in _overlays)
                if (ov.MenuItem != null) ov.MenuItem.Checked = ov.Enabled;
        }

        // "View → Panels → Position and transparency…": the one dialog for every panel of the window.
        private void menuPanels_Click(object sender, EventArgs e)
        {
            if (!_overlaysReady) return;
            using (var dlg = new Aplat.Dialogs.PanelSettingsDialog())
            {
                dlg.AddSection(Loc.T("Панели инструментов"));
                string[] bar = { Loc.T("Сверху"), Loc.T("Снизу"), Loc.T("Слева"), Loc.T("Справа"), Loc.T("Отдельное окно") };
                foreach (var strip in new[] { toolStrip, toolStrip2 })
                {
                    ToolStrip s = strip;
                    dlg.AddRow(s.Text, s.Visible, on => { toolbarDock.SetVisible(s, on); SavePanelLayout(); },
                        bar, toolbarDock.PositionIndex(s), i => { toolbarDock.SetPosition(s, i); SavePanelLayout(); });
                }

                dlg.AddSection(Loc.T("Плавающие панели"));
                var names = new List<string> { AnchorName(DockAnchor.None) };
                foreach (var a in DockAnchors) names.Add(AnchorName(a));
                foreach (var ov in _overlays)
                {
                    OverlayInfo info = ov;
                    dlg.AddRow(info.Name, info.Enabled, on => SetOverlayShown(info, on),
                        names.ToArray(), info.Placement.Docked ? 1 + Array.IndexOf(DockAnchors, info.Placement.Anchor) : 0,
                        i => SetOverlayAnchor(info, i == 0 ? DockAnchor.None : DockAnchors[i - 1]));
                }

                dlg.OpacityPercent = (int)Math.Round(_overlayOpacity * 100);
                dlg.OpacityChanged += pct =>
                {
                    _overlayOpacity = Math.Max(0.3, Math.Min(1.0, pct / 100.0));
                    foreach (var ov in _overlays) ov.Window.Opacity = _overlayOpacity;
                };
                dlg.ResetRequested += (s, a) => { ResetPanels(); dlg.Close(); };
                dlg.ShowDialog(this);
            }
            RefreshToolbarMenuChecks();
            SavePanelLayout();
        }

        // Every panel back to its default place: toolbars across the top, cards on their default anchors.
        private void ResetPanels()
        {
            toolbarDock.ResetLayout();
            _overlayOpacity = DefaultOverlayOpacity;
            foreach (var ov in _overlays)
            {
                ov.Enabled = true;
                ov.Placement = ov.Default.Clone();
                ov.Window.Opacity = _overlayOpacity;
            }
            RefreshCardMenuChecks();
            RefreshToolbarMenuChecks();
            PositionOverlays();
            SavePanelLayout();
        }

        // ── Remembered between runs (panels.txt; toolbars keep their own toolbars.txt) ──
        // Lines:  opacity|0.92   and   card|<key>|<1 shown / 0 hidden>|<placement>
        // Reading is forgiving: unknown keys, bad numbers and a missing file leave the defaults alone.

        private void LoadPanelLayout()
        {
            string[] lines;
            try { if (!File.Exists(PanelsPath)) return; lines = File.ReadAllLines(PanelsPath); }
            catch { return; }

            foreach (var line in lines)
            {
                var f = line.Split('|');
                if (f.Length >= 2 && f[0] == "opacity")
                {
                    double v;
                    if (double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        _overlayOpacity = Math.Max(0.3, Math.Min(1.0, v));
                    continue;
                }
                if (f.Length < 4 || f[0] != "card") continue;
                var ov = _overlays.Find(o => o.Key == f[1]);
                PanelPlacement p;
                if (ov == null || !PanelPlacement.TryParse(f[3], out p)) continue;
                ov.Enabled = f[2] != "0";
                ov.Placement = p;
            }
        }

        private void SavePanelLayout()
        {
            try { toolbarDock.Save(ToolbarsPath); } catch { }
            if (!_overlaysReady) return;
            try
            {
                var lines = new List<string> { "opacity|" + _overlayOpacity.ToString("0.##", CultureInfo.InvariantCulture) };
                foreach (var ov in _overlays)
                    lines.Add(string.Join("|", "card", ov.Key, ov.Enabled ? "1" : "0", ov.Placement.Serialize()));
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllLines(PanelsPath, lines.ToArray());
            }
            catch { }
        }
    }
}
