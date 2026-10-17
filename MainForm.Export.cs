using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Aplat.IO;

namespace Aplat
{
    // Model export (OBJ / STL / FBX) and the ZIP packaging helpers. OBJ always ships
    // as a ZIP of .obj + .mtl + baked textures; STL (binary) and FBX are plain files.
    // Every export is written to a temp file first and swapped in.
    public partial class MainForm
    {
        // Default export file name: the saved project's file name, else the scene name.
        private string ProjectBaseName()
        {
            if (!string.IsNullOrEmpty(_scene.FilePath))
                return Path.GetFileNameWithoutExtension(_scene.FilePath);
            return string.IsNullOrEmpty(_scene.Name) ? "model" : _scene.Name;
        }

        private void ExportAs(string format)
        {
            if (_scene.Objects.Count == 0)
            {
                MessageBox.Show(Loc.T("Сцена пуста — нечего экспортировать."), Loc.T("Экспорт"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Encode paint layers so the export sees the latest strokes.
            _scene.PrepareForSerialize();

            if (format == "OBJ") { ExportObj(); return; }

            // STL / FBX are plain files (OBJ needs the ZIP for its .mtl and textures).
            bool isStl = format == "STL";
            if (!isStl && format != "FBX") return;

            // The canvas sheet is a zero-thickness plane: bad for printing, so STL asks.
            bool includeCanvas = true;
            if (isStl && ExportManager.HasCanvas(_scene.Objects))
            {
                var answer = MessageBox.Show(
                    Loc.T("Включить лист холста в STL?\n\nЭто плоскость нулевой толщины — для 3D-печати её лучше не включать."),
                    Loc.T("Экспорт"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer == DialogResult.Cancel) return;
                includeCanvas = answer == DialogResult.Yes;
            }

            string ext = isStl ? ".stl" : ".fbx";
            using (var d = new SaveFileDialog
            {
                Filter       = isStl ? Loc.T("Двоичный STL (*.stl)|*.stl|Все файлы (*.*)|*.*")
                                     : Loc.T("Модель FBX (*.fbx)|*.fbx|Все файлы (*.*)|*.*"),
                FileName     = ProjectBaseName() + ext,
                DefaultExt   = ext.TrimStart('.'),
                AddExtension = true
            })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                string path = d.FileName;
                try
                {
                    byte[] data = isStl
                        ? ExportManager.ToBinarySTL(_scene.Objects, includeCanvas)
                        : ExportManager.ToFBX(_scene.Objects);
                    if (isStl && data.Length <= 84)
                    {
                        MessageBox.Show(Loc.T("Сцена пуста — нечего экспортировать."), Loc.T("Экспорт"),
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    ExportManager.WriteFileAtomic(path, fs => fs.Write(data, 0, data.Length));
                    statusLabel.Text = Loc.T("Экспортировано: ") + path;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.T("Ошибка экспорта:\n") + ex.Message, Loc.T("Экспорт"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // OBJ export always yields a ZIP with .obj + .mtl + baked textures.
        private void ExportObj()
        {
            const string ext = ".zip";
            using (var d = new SaveFileDialog
            {
                Filter       = Loc.T("ZIP-архив OBJ (*.zip)|*.zip|Все файлы (*.*)|*.*"),
                FileName     = ProjectBaseName() + "_obj" + ext,
                DefaultExt   = ext.TrimStart('.'),
                AddExtension = true
            })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                string path = d.FileName;
                if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path += ext;

                string baseName = Path.GetFileNameWithoutExtension(path);
                string assetBase = ExportManager.SafeAssetName(baseName, "model");
                string mtlName  = assetBase + ".mtl";
                ObjExport result = null;

                try
                {
                    result = ExportManager.BuildOBJ(_scene.Objects, mtlName);
                    WriteObjZip(path, assetBase, mtlName, result);
                    statusLabel.Text = Loc.T("Экспортировано: ") + path;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.T("Ошибка экспорта:\n") + ex.Message, Loc.T("Экспорт"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (result != null)
                        foreach (var kv in result.Textures) kv.Value.Dispose();
                }
            }
        }

        private static void WriteObjZip(string zipPath, string baseName, string mtlName, ObjExport result)
        {
            ExportManager.WriteFileAtomic(zipPath, fs =>
            {
                using (var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    ZipText(archive, baseName + ".obj", result.Obj);
                    ZipText(archive, mtlName, result.Mtl);
                    foreach (var kv in result.Textures) ZipImage(archive, kv.Key, kv.Value);
                }
            });
        }

        private static void ZipText(System.IO.Compression.ZipArchive archive, string name, string text)
        {
            var entry = archive.CreateEntry(name);
            using (var w = new StreamWriter(entry.Open(), Encoding.ASCII)) w.Write(text);
        }

        private static void ZipImage(System.IO.Compression.ZipArchive archive, string name, Bitmap bmp)
        {
            var entry = archive.CreateEntry(name);
            using (var es = entry.Open())
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                ms.CopyTo(es);
            }
        }

        // ── Canvas image export (PNG / JPG) ───────────────────────
        private void ExportCanvasImage(string format)
        {
            var canvas = FindCanvas();
            if (canvas == null)
            {
                MessageBox.Show(Loc.T("Холст не найден в сцене."), Loc.T("Экспорт холста"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Determine the canvas world-unit dimensions from its mesh so the exported
            // image has the correct aspect ratio (e.g. 4:3 → 1024×768, not 1024×1024).
            float cW = 4f, cH = 4f;
            if (canvas.Mesh != null && canvas.Mesh.Vertices.Count > 0)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var v in canvas.Mesh.Vertices)
                {
                    if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
                }
                if (maxX > minX) cW = maxX - minX;
                if (maxY > minY) cH = maxY - minY;
            }

            // The sheet's own resolution (2048 today, 1024 in old projects), not a fixed cap.
            int MaxSide = canvas.Material != null && canvas.Material.PaintLayer != null
                ? Math.Max(canvas.Material.PaintLayer.Width, canvas.Material.PaintLayer.Height)
                : Aplat.Models.Material.CanvasPaintSize;
            int exportW, exportH;
            if (cW >= cH) { exportW = MaxSide; exportH = Math.Max(1, (int)Math.Round(MaxSide * cH / cW)); }
            else          { exportH = MaxSide; exportW = Math.Max(1, (int)Math.Round(MaxSide * cW / cH)); }

            _scene.PrepareForSerialize();
            bool isPng = string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase);
            string ext = isPng ? ".png" : ".jpg";
            string filter = isPng
                ? Loc.T("PNG Image (*.png)|*.png|Все файлы (*.*)|*.*")
                : Loc.T("JPEG Image (*.jpg)|*.jpg|Все файлы (*.*)|*.*");

            using (var d = new SaveFileDialog
            {
                Filter       = filter,
                FileName     = ProjectBaseName() + "_canvas" + ext,
                DefaultExt   = ext.TrimStart('.'),
                AddExtension = true
            })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    using (var raw = ExportManager.BakeCanvasImage(canvas))
                    {
                        // Scale from the square texture to the canvas aspect ratio.
                        Bitmap bmp = (exportW == raw.Width && exportH == raw.Height)
                            ? raw
                            : new Bitmap(raw, exportW, exportH);
                        try
                        {
                            if (isPng)
                                bmp.Save(d.FileName, ImageFormat.Png);
                            else
                            {
                                var encParams = new EncoderParameters(1);
                                encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                                var jpgCodec = GetEncoder(ImageFormat.Jpeg);
                                if (jpgCodec != null)
                                    bmp.Save(d.FileName, jpgCodec, encParams);
                                else
                                    bmp.Save(d.FileName, ImageFormat.Jpeg);
                            }
                        }
                        finally { if (!ReferenceEquals(bmp, raw)) bmp.Dispose(); }
                    }
                    statusLabel.Text = string.Format(Loc.T("Экспортировано ({0}×{1}): {2}"), exportW, exportH, d.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.T("Ошибка экспорта:\n") + ex.Message, Loc.T("Экспорт холста"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ── Frame (viewport screenshot) export (PNG / JPG) ────────
        private void ExportFrame(string format)
        {
            bool isPng = string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase);
            string ext = isPng ? ".png" : ".jpg";
            string filter = isPng
                ? Loc.T("PNG Image (*.png)|*.png|Все файлы (*.*)|*.*")
                : Loc.T("JPEG Image (*.jpg)|*.jpg|Все файлы (*.*)|*.*");

            using (var d = new SaveFileDialog
            {
                Filter       = filter,
                FileName     = ProjectBaseName() + "_frame" + ext,
                DefaultExt   = ext.TrimStart('.'),
                AddExtension = true
            })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    using (var bmp = sceneView.CaptureFrame())
                    {
                        if (bmp == null)
                        {
                            MessageBox.Show(Loc.T("Не удалось захватить кадр."), Loc.T("Экспорт кадра"),
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        if (isPng)
                            bmp.Save(d.FileName, ImageFormat.Png);
                        else
                        {
                            var encParams = new EncoderParameters(1);
                            encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                            var jpgCodec = GetEncoder(ImageFormat.Jpeg);
                            if (jpgCodec != null)
                                bmp.Save(d.FileName, jpgCodec, encParams);
                            else
                                bmp.Save(d.FileName, ImageFormat.Jpeg);
                        }
                    }
                    statusLabel.Text = Loc.T("Экспортировано: ") + d.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.T("Ошибка экспорта:\n") + ex.Message, Loc.T("Экспорт кадра"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ── Turntable export: a series of PNG frames ──────────────
        // The camera circles the scene through 360°; every frame is written as a separate
        // PNG with its number in the file name — ready-made frames for a GIF / video (ffmpeg, Photoshop).
        private void menuExpTurntable_Click(object s, EventArgs e) { ExportTurntable(); }

        private void ExportTurntable()
        {
            if (_scene.Objects.Count == 0)
            {
                MessageBox.Show(Loc.T("Сцена пуста — нечего вращать."), Loc.T("Экспорт оборота"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int frames = AskFrameCount();
            if (frames <= 0) return;

            string folder;
            using (var fb = new FolderBrowserDialog
            {
                Description = Loc.T("Папка для кадров оборота"),
                ShowNewFolderButton = true
            })
            {
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                folder = fb.SelectedPath;
            }

            string baseName = ProjectBaseName();
            int written = 0;
            Exception failure = null;
            var previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                sceneView.CaptureTurntable(frames, (index, bmp) =>
                {
                    using (bmp)
                    {
                        if (failure != null) return;
                        try
                        {
                            string file = Path.Combine(folder,
                                string.Format("{0}_{1:D3}.png", baseName, index));
                            bmp.Save(file, ImageFormat.Png);
                            written++;
                        }
                        catch (Exception ex) { failure = ex; }
                    }
                });
            }
            finally { Cursor = previousCursor; }

            if (failure != null)
            {
                MessageBox.Show(Loc.T("Ошибка экспорта:\n") + failure.Message, Loc.T("Экспорт оборота"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            statusLabel.Text = string.Format(Loc.T("Сохранено кадров: {0} → {1}"), written, folder);
        }

        // A small "how many frames" dialog — 36 frames give a 10° step.
        private int AskFrameCount()
        {
            using (var dlg = new Form())
            {
                dlg.Text = Loc.T("Экспорт оборота");
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ClientSize = new Size(300, 128);
                Aplat.UI.ThemeManager.StyleDialog(dlg);

                var lbl = new Label
                {
                    Text = Loc.T("Кадров на полный оборот:"),
                    Location = new Point(16, 20), AutoSize = true
                };
                var num = new NumericUpDown
                {
                    Location = new Point(180, 17), Width = 100,
                    Minimum = 4, Maximum = 360, Value = 36, Increment = 4
                };
                var hint = new Label
                {
                    Text = Loc.T("Кадры пишутся PNG-серией в выбранную папку."),
                    Location = new Point(16, 50), AutoSize = true,
                    ForeColor = Aplat.UI.Theme.TextSoft
                };
                var ok = new Button
                {
                    Text = Loc.T("Далее"), DialogResult = DialogResult.OK,
                    Location = new Point(126, 84), Width = 76, FlatStyle = FlatStyle.System
                };
                var cancel = new Button
                {
                    Text = Loc.T("Отмена"), DialogResult = DialogResult.Cancel,
                    Location = new Point(210, 84), Width = 76, FlatStyle = FlatStyle.System
                };
                dlg.Controls.AddRange(new Control[] { lbl, num, hint, ok, cancel });
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                return dlg.ShowDialog(this) == DialogResult.OK ? (int)num.Value : 0;
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat fmt)
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == fmt.Guid) return c;
            return null;
        }
    }
}
