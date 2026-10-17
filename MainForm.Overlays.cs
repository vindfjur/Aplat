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
        // View → Toolbars → File / Modes.
        private void ToggleToolbar(ToolStrip strip)
        {
            toolbarDock.SetVisible(strip, !strip.Visible);
        }

        private void panelMain_ResizeOverlays(object sender, EventArgs e)
        {
            SchedulePositionOverlays();
        }

        // Re-runs PositionOverlays after the current layout pass completes, so the scene
        // size is final. Without this, a maximize repositions cards against the old
        // (smaller) size and the bottom card is left stranded mid-window.
        private bool _repositionQueued;
        private void SchedulePositionOverlays()
        {
            if (!IsHandleCreated) { PositionOverlays(); return; }
            if (_repositionQueued) return;                 // already coalesced for this turn
            _repositionQueued = true;
            BeginInvoke((Action)(() => { _repositionQueued = false; PositionOverlays(); }));
        }

        // Creates the four floating overlay windows (layered, translucent, drag/resize)
        // once the form is shown — owned by this form so they sit above it, hide/close
        // with it and stay off the Alt-Tab list.
        private void InitOverlays()
        {
            if (_overlaysReady || panelMain == null) return;

            // Fixed-size cards (no resize grip): the user can drag them, but never
            // shrink them into a clipped, ragged state. Placement is computed centrally in
            // PositionOverlays (MainForm.PanelDock.cs); the order here is the order of OverlayKeys.
            AddOverlay(Loc.T("Инструменты"), floatingToolsPanel,
                new Control[] { floatingToolsFlow });

            AddOverlay(Loc.T("Недавний инструмент"), recentToolsPanel,
                new Control[] { recentToolsPanel, recentToolsFlow });

            AddOverlay(Loc.T("Объекты"), layersPanel,
                new Control[] { layersHeader });

            AddOverlay(Loc.T("Управление видом"), viewPanel,
                new Control[] { viewPanel, viewFlow });

            // Auto-rotate: pause / stop / off, shown only while the spin is on.
            AddOverlay(Loc.T("Автовращение"), BuildSpinCard(),
                new Control[] { spinPanel, spinFlow, lblSpinState });
            _overlays[4].Active = () => _scene != null && _scene.AutoRotate;

            _recentWindow = _overlays[1].Window;
            LoadPanelLayout();   // shown / anchored where the user left them
            _overlaysReady = true;

            foreach (var ov in _overlays)
            {
                ov.Window.Opacity = _overlayOpacity;
                ov.Window.Owner   = this;   // minimise / restore / close together
                if (OverlayShown(ov)) ov.Window.Show();
            }
            AddCardMenuItems();
            PositionOverlays();
            RenderRecentTools();   // size the recent card to its current content
        }

        private void AddOverlay(string name, Panel content, Control[] dragHandles)
        {
            var natural = new Size(content.Width + 2, content.Height + 2);   // +2 for the border ring
            var w = new Aplat.UI.OverlayWindow(natural);
            w.SetContent(content);
            Aplat.UI.ThemeManager.Attach(w);   // re-colours with the main window on a theme switch
            foreach (var d in dragHandles) w.RegisterDragHandle(d);

            int slot = _overlays.Count;
            var info = new OverlayInfo
            {
                Window = w, Name = name, Key = OverlayKeys[slot],
                Default = Aplat.UI.PanelPlacement.Dock(OverlayDefaults[slot], 0),
                Placement = Aplat.UI.PanelPlacement.Dock(OverlayDefaults[slot], 0)
            };
            WireOverlayDrag(info);
            _overlays.Add(info);
        }

        private void trkViewZoom_Scroll(object sender, EventArgs e)
        {
            if (_syncingViewZoom || sceneView == null || trkViewZoom == null) return;
            int value = Math.Max(trkViewZoom.Minimum, Math.Min(trkViewZoom.Maximum, trkViewZoom.Value));
            float newScale = _viewZoomBase * 100f / value;
            var c = sceneView.Camera;
            c.Distance   = Math.Max(Camera.MinDistance, Math.Min(Camera.MaxDistance, newScale));
            c.OrthoScale = Math.Max(Camera.MinDistance, Math.Min(Camera.MaxDistance, newScale));
            UpdateViewZoomDisplay();
            sceneView.Refresh3D();
        }

        private void btnViewFit_Click(object sender, EventArgs e)
        {
            sceneView.FocusSelection();
            CaptureViewZoomBase();
            UpdateViewZoomDisplay();
        }

        private void btnViewGrid_Click(object sender, EventArgs e)
        {
            ToggleGrid();
            UpdateViewOverlayState();
        }

        private void btnViewGuides_Click(object sender, EventArgs e)
        {
            _guidesVisible = !_guidesVisible;
            if (sceneView != null)
            {
                sceneView.ShowGuides = _guidesVisible;
                sceneView.Refresh3D();
            }
            SetButtonActive(btnViewGuides, _guidesVisible);
            statusLabel.Text = _guidesVisible
                ? Loc.T("Умные направляющие включены: при перетаскивании объект примагничивается к центрам соседей")
                : Loc.T("Умные направляющие выключены");
        }

        private void btnViewCenter_Click(object sender, EventArgs e)
        {
            ViewMode next = sceneView.Camera.Mode == ViewMode.Perspective
                ? ViewMode.Front
                : _viewOverlayMode == ViewMode.Front
                ? ViewMode.Side
                : _viewOverlayMode == ViewMode.Side ? ViewMode.Top : ViewMode.Front;
            ApplyView(next);
            statusLabel.Text = next == ViewMode.Front
                ? Loc.T("Вид: спереди")
                : next == ViewMode.Side ? Loc.T("Вид: сбоку") : Loc.T("Вид: сверху");
        }

        private float CurrentCameraScale()
        {
            if (sceneView == null) return 1f;
            var c = sceneView.Camera;
            return c.Mode == ViewMode.Perspective ? c.Distance : c.OrthoScale;
        }

        private void CaptureViewZoomBase()
        {
            _viewZoomBase = Math.Max(Camera.MinDistance, CurrentCameraScale());
        }

        private void UpdateViewZoomDisplay()
        {
            if (sceneView == null || lblViewZoom == null || trkViewZoom == null) return;
            float scale = Math.Max(Camera.MinDistance, CurrentCameraScale());
            int zoom = (int)Math.Round(_viewZoomBase * 100f / scale);
            if (zoom < 1) zoom = 1;
            lblViewZoom.Text = zoom + "%";

            _syncingViewZoom = true;
            int sliderValue = Math.Max(trkViewZoom.Minimum, Math.Min(trkViewZoom.Maximum, zoom));
            trkViewZoom.Value = sliderValue;
            _syncingViewZoom = false;
        }

        private void UpdateViewOverlayState()
        {
            SetButtonActive(btnViewGrid, _scene != null && _scene.ShowGrid);
            SetButtonActive(btnViewGuides, _guidesVisible);
            UpdateViewModeIcon();
            UpdateViewZoomDisplay();
            UpdateViewSpeedLabel();
        }

        private void UpdateViewModeIcon()
        {
            var rb = btnViewCenter as Aplat.UI.RoundedButton;
            if (rb == null) return;
            if (sceneView != null && sceneView.Camera.Mode == ViewMode.Perspective)
            {
                rb.Icon = Aplat.UI.UiIcon.ViewPerspective;
                rb.Tag = Loc.T("Перспектива");
                return;
            }
            switch (_viewOverlayMode)
            {
                case ViewMode.Side:
                    rb.Icon = Aplat.UI.UiIcon.ViewSide;
                    rb.Tag = Loc.T("Вид сбоку");
                    break;
                case ViewMode.Top:
                    rb.Icon = Aplat.UI.UiIcon.ViewTop;
                    rb.Tag = Loc.T("Вид сверху");
                    break;
                default:
                    rb.Icon = Aplat.UI.UiIcon.ViewFront;
                    rb.Tag = Loc.T("Вид спереди");
                    break;
            }
        }

        private void UpdateViewSpeedLabel()
        {
            if (lblViewSpeed == null || sceneView == null) return;
            // Short form — the speedometer icon next to the label says the rest.
            lblViewSpeed.Text = string.Format("{0:0.0}x", sceneView.FlySpeed);
        }

        private static void SetButtonActive(Button button, bool active)
        {
            var rb = button as Aplat.UI.RoundedButton;
            if (rb != null) rb.Active = active;
        }

        private void UpdateStatus()
        {
            int n = sceneView != null ? sceneView.SelectedObjects.Count : 0;
            if (n > 1)
                statusLabel.Text = string.Format(Loc.T("Выбрано объектов: {0}   |   Объектов на сцене: {1}"), n, _scene.Objects.Count);
            else if (_selected != null)
                statusLabel.Text = string.Format(
                    Loc.T("Выбран: {0}   |   X: {1}px  Y: {2}px  Z: {3}px   |   Масштаб: {4}%   |   Объектов: {5}"),
                    _selected.Name,
                    ToCanvasX(_selected.Position.X), ToCanvasY(_selected.Position.Y), ToPx(_selected.Position.Z),
                    ToPercent(_selected.Scale.X), _scene.Objects.Count);
            else
                statusLabel.Text = string.Format(Loc.T("Объектов на сцене: {0}"), _scene.Objects.Count);
        }

        private static int ToPx(float value)
        {
            return (int)Math.Round(value * 256f);
        }

        private int ToCanvasX(float worldX)
        {
            if (TryGetCanvasFrame(out float left, out float top, out float width, out float height))
                return ToPx(worldX - left);
            return ToPx(worldX);
        }

        private int ToCanvasY(float worldY)
        {
            if (TryGetCanvasFrame(out float left, out float top, out float width, out float height))
                return ToPx(top - worldY);
            return ToPx(worldY);
        }

        private static int ToPercent(float value)
        {
            return (int)Math.Round(value * 100f);
        }

    }
}
