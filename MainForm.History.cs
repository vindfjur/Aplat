using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using Aplat.Controls;
using Aplat.IO;
using Aplat.Models;

namespace Aplat
{
    // Undo / redo: snapshot-based history (XML serialization of the whole scene) kept in an
    // UndoHistory — pictures held once in a content-addressed store, so a step costs what it
    // changed (strokes of a canvas, tiles of a 3-D shape's paint). The number of steps is not
    // limited; the history's weight is (UndoHistory.MemoryLimit). It is written into the scene file.
    public partial class MainForm
    {
        private const int HistoryMenuItems = 30;   // the menu lists the newest; older ones are reached with Undo

        // `label` names the action for the history menu; the step holds the scene as it was before it.
        private void PushUndo(string label = null)
        {
            // Any new action first merges a live shape / text into the canvas (its own
            // undo step was taken when it was created) and ends the "reopen" chance.
            sceneView.CommitLive();
            sceneView.ForgetReopen();
            _history.Push(SerializeScene(_scene), label);
            UpdateUndoButtons();
        }

        // The action a stroke / drag / click in the viewport is about to make, named by the active tool.
        private string ViewportActionLabel()
        {
            switch (sceneView.CurrentTool)
            {
                case ToolMode.Move:      return Loc.T("Переместить");
                case ToolMode.Rotate:    return Loc.T("Вращение");
                case ToolMode.Scale:     return Loc.T("Масштаб");
                case ToolMode.EditShape: return Loc.T("Форма");
                case ToolMode.Paint:
                    switch (sceneView.PaintKind)
                    {
                        case BrushKind.Brush:         return Loc.T("Кисть");
                        case BrushKind.Pencil:        return Loc.T("Карандаш");
                        case BrushKind.Pen:           return Loc.T("Ручка");
                        case BrushKind.Marker:        return Loc.T("Маркер");
                        case BrushKind.Fill:          return Loc.T("Заливка");
                        case BrushKind.Eraser:        return Loc.T("Ластик");
                        case BrushKind.ObjectEraser:  return Loc.T("Ластик объектов");
                        case BrushKind.Text2D:        return Loc.T("Текст");
                        case BrushKind.Select2D:      return Loc.T("Выделение");
                        case BrushKind.Lasso:         return Loc.T("Лассо");
                        case BrushKind.ShapeLine:     return Loc.T("Линия");
                        case BrushKind.ShapeRect:     return Loc.T("Прямоугольник");
                        case BrushKind.ShapeEllipse:  return Loc.T("Эллипс");
                        case BrushKind.ShapeTriangle: return Loc.T("Треугольник");
                    }
                    break;
            }
            return Loc.T("Правка");
        }

        // An action that was abandoned (a live shape removed with Esc, an empty
        // selection) takes back the undo step recorded when it began.
        private void DropLastUndo()
        {
            _history.DropLast();
            UpdateUndoButtons();
        }

        private void Undo()
        {
            if (sceneView.TextHistoryStep(false)) return;   // typing: undo within the text
            // Ctrl+Z on a live element undoes its creation (snapshot below); right after
            // a commit it first reopens the element for editing.
            if (sceneView.HasLiveItem)
            {
                // The redo snapshot must hold the element as a committed object of the
                // canvas (its journal entry), exactly as its pixels already show it.
                sceneView.CommitLive();
                sceneView.ForgetReopen();
            }
            else if (sceneView.ReopenLastLive()) return;
            if (_history.UndoCount == 0) return;
            RestoreScene(_history.Undo(SerializeScene(_scene)));
            UpdateUndoButtons();
        }

        private void Redo()
        {
            if (sceneView.TextHistoryStep(true)) return;
            if (sceneView.HasLiveItem) return;   // a live edit is the newest action
            sceneView.ForgetReopen();
            if (_history.RedoCount == 0) return;
            RestoreScene(_history.Redo(SerializeScene(_scene)));
            UpdateUndoButtons();
        }

        // PAINT3D_VERIFY_INK=1: at every snapshot, check that each canvas's journal still replays
        // to its layer (the invariant undo now rests on) and say so on stderr if it does not.
        private static readonly bool VerifyInk = Environment.GetEnvironmentVariable("PAINT3D_VERIFY_INK") == "1";

        private static string SerializeScene(Scene s)
        {
            if (VerifyInk) foreach (var o in s.Objects) VerifyJournals(o);
            // A snapshot holds no picture of a canvas whose journal can rebuild it: a step costs
            // the strokes made since the last one, not a PNG of the whole sheet.
            s.PrepareForSerialize(snapshot: true);
            var savedHistory = s.History;
            s.History = null;          // never embed the undo history inside a snapshot
            Material.SnapshotMode = true;
            try
            {
                var sb = new StringBuilder();
                new XmlSerializer(typeof(Scene)).Serialize(new StringWriter(sb), s);
                return sb.ToString();
            }
            finally { Material.SnapshotMode = false; s.History = savedHistory; }
        }

        private static void VerifyJournals(SceneObject o)
        {
            if (o.Material != null)
            {
                int bad = o.Material.JournalMismatch();
                Console.Error.WriteLine("INKVERIFY " + o.Name + ": " + (bad == 0 ? "journal matches the layer" : "MISMATCH " + bad + " px"));
            }
            foreach (var ch in o.Children) VerifyJournals(ch);
        }

        private void RestoreScene(string xml)
        {
            var s = SceneFileManager.DeserializeScene(new StringReader(xml));
            // The canvases of the state we are leaving give their layers to the ones coming back.
            var before = new Dictionary<string, Material>();
            CollectMaterials(_scene.Objects, before);
            AdoptLayers(s.Objects, before);
            s.FilePath   = _scene.FilePath;
            s.IsModified = true;
            _scene = s;
            sceneView.Scene = _scene;
            SyncBrightnessUI();
            SyncCanvasUI();
            Select(null);
            UpdateList();
            UpdateTitle();
        }

        private static void CollectMaterials(List<SceneObject> objects, Dictionary<string, Material> into)
        {
            foreach (var o in objects)
            {
                if (o.Material != null && o.Id != null) into[o.Id] = o.Material;
                CollectMaterials(o.Children, into);
            }
        }

        private static void AdoptLayers(List<SceneObject> objects, Dictionary<string, Material> before)
        {
            foreach (var o in objects)
            {
                Material previous;
                if (o.Material != null && o.Id != null && before.TryGetValue(o.Id, out previous))
                    o.Material.AdoptLayerFrom(previous);
                AdoptLayers(o.Children, before);
            }
        }

        private void UpdateUndoButtons()
        {
            if (tbUndo != null) tbUndo.Enabled = _history.UndoCount > 0;
            if (tbRedo != null) tbRedo.Enabled = _history.RedoCount > 0;
            if (tbHistory != null) tbHistory.Enabled = true;
        }

        private void tbHistory_DropDownOpening(object sender, EventArgs e)
        {
            if (tbHistory == null) return;
            tbHistory.DropDownItems.Clear();

            var steps = _history.Steps(); // newest first
            if (steps.Count == 0)
            {
                tbHistory.DropDownItems.Add(new ToolStripMenuItem(Loc.T("История пуста")) { Enabled = false });
                return;
            }

            for (int i = 0; i < Math.Min(steps.Count, HistoryMenuItems); i++)
            {
                var item = new ToolStripMenuItem(steps[i].Label ?? string.Format(Loc.T("Состояние {0}"), i + 1));
                item.Tag = steps[i];
                item.Click += tbHistoryItem_Click;
                tbHistory.DropDownItems.Add(item);
            }
            if (steps.Count > HistoryMenuItems)
                tbHistory.DropDownItems.Add(new ToolStripMenuItem(string.Format(Loc.T("и ещё {0} ранее…"), steps.Count - HistoryMenuItems)) { Enabled = false });
        }

        private void tbHistoryItem_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            var step = item != null ? item.Tag as HistoryStep : null;
            if (step == null) return;

            string xml = _history.JumpTo(step, SerializeScene(_scene));
            if (xml == null) return;
            RestoreScene(xml);
            UpdateUndoButtons();
        }
    }
}
