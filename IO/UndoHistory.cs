using System;
using System.Collections.Generic;
using System.IO;

namespace Aplat.IO
{
    // One undo step: the scene as XML in which every picture is a reference to a blob in the
    // history's store (the same references the .aplat file uses).
    public sealed class HistoryStep
    {
        internal string Xml;
        internal string[] Blobs;
        // The action this step takes back (what the history menu shows); null for a step read from a file.
        public string Label { get; internal set; }
    }

    // The undo / redo history. A step is the whole scene's XML with its pictures taken out into
    // a content-addressed store (see BlobPacker): what two steps have in common — an untouched
    // tile of a paint layer, an embedded texture, a canvas's base picture — is held once, so a
    // step costs what it changed. There is no limit on the number of steps; the history is
    // trimmed from its oldest end when its weight (the unique bytes it holds) goes over
    // MemoryLimit, always keeping the newest MinSteps so even a huge edit stays undoable.
    public sealed class UndoHistory
    {
        public const long DefaultMemoryLimit = 160L << 20;   // what the history may hold in memory
        public const long DefaultFileLimit   = 48L << 20;    // what is written into a .aplat file
        public const int  MinSteps = 5;

        private sealed class Blob { public byte[] Bytes; public int Refs; }

        private readonly List<HistoryStep> _undo = new List<HistoryStep>();   // oldest first
        private readonly List<HistoryStep> _redo = new List<HistoryStep>();   // the next redo is last
        private readonly Dictionary<string, Blob> _blobs = new Dictionary<string, Blob>();
        private long _blobBytes, _xmlBytes;

        public long MemoryLimit = DefaultMemoryLimit;

        public int UndoCount { get { return _undo.Count; } }
        public int RedoCount { get { return _redo.Count; } }
        public int BlobCount { get { return _blobs.Count; } }

        // Unique picture bytes plus the steps' XML (UTF-16 in memory).
        public long Weight { get { return _blobBytes + _xmlBytes; } }

        public void Clear()
        {
            _undo.Clear(); _redo.Clear(); _blobs.Clear();
            _blobBytes = _xmlBytes = 0;
        }

        // ── Recording ─────────────────────────────────────────────

        // A new action: `before` is the scene as it was; the redo branch is gone.
        public void Push(string before, string label = null)
        {
            var step = Capture(before);
            step.Label = label;
            _undo.Add(step);
            DropRedo();
            Trim();
        }

        // An abandoned action takes back the step recorded when it began.
        public void DropLast()
        {
            if (_undo.Count == 0) return;
            var step = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Release(step);
        }

        // ── Moving ────────────────────────────────────────────────

        // The scene to go back to (null if there is none); `current` becomes the next redo.
        public string Undo(string current)
        {
            if (_undo.Count == 0) return null;
            var step = _undo[_undo.Count - 1];
            var redo = Capture(current);
            redo.Label = step.Label;          // a redo entry carries the action that leads to its state
            _redo.Add(redo);
            _undo.RemoveAt(_undo.Count - 1);
            return Take(step);
        }

        public string Redo(string current)
        {
            if (_redo.Count == 0) return null;
            var step = _redo[_redo.Count - 1];
            var undo = Capture(current);
            undo.Label = step.Label;
            _undo.Add(undo);
            _redo.RemoveAt(_redo.Count - 1);
            string xml = Take(step);
            Trim();
            return xml;
        }

        // Back (or forward) to any listed step; the states in between stay reachable through redo.
        public string JumpTo(HistoryStep target, string current)
        {
            int k = _undo.IndexOf(target);
            if (k < 0) return null;
            var now = Capture(current);
            now.Label = _undo[_undo.Count - 1].Label;
            _redo.Add(now);
            for (int i = _undo.Count - 1; i > k; i--)
            {
                var step = _undo[i];
                step.Label = _undo[i - 1].Label;   // the action that led to the state this step holds
                _redo.Add(step);
            }
            _undo.RemoveRange(k, _undo.Count - k);
            return Take(target);
        }

        // The steps, newest first (the history menu).
        public List<HistoryStep> Steps()
        {
            var list = new List<HistoryStep>(_undo);
            list.Reverse();
            return list;
        }

        // ── Files ─────────────────────────────────────────────────

        // Adds a step known only as XML with pictures inline (an old file) as the newest one.
        public void Append(string xml)
        {
            _undo.Add(Capture(xml));
        }

        // Adds a step whose XML already holds references; `read` gives the bytes of a blob.
        public void AppendReferenced(string xml, Func<string, byte[]> read)
        {
            var names = BlobPacker.References(xml);
            foreach (string name in names)
            {
                if (_blobs.ContainsKey(name)) continue;
                byte[] bytes = read(name);
                if (bytes == null) throw new InvalidDataException(Loc.T("В файле не хватает данных: ") + name);
                _blobs[name] = new Blob { Bytes = bytes };
                _blobBytes += bytes.Length;
            }
            var step = new HistoryStep { Xml = xml, Blobs = names.ToArray() };
            foreach (string name in step.Blobs) _blobs[name].Refs++;
            _xmlBytes += (long)xml.Length * 2;
            _undo.Add(step);
        }

        // Call after a batch of Append / AppendReferenced.
        public void Settle() { Trim(); }

        // What goes into a file: the newest steps (oldest-first) that fit `limit`, their XML and
        // the blobs they use. A file's size is the pictures plus the XML, which deflates ~4:1.
        public List<string> ExportTo(Dictionary<string, byte[]> blobs, long limit)
        {
            var take = new List<HistoryStep>();
            var seen = new HashSet<string>();
            long total = 0;
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                var step = _undo[i];
                long add = step.Xml.Length / 4;
                foreach (string name in step.Blobs)
                    if (!seen.Contains(name)) add += _blobs[name].Bytes.Length;
                if (take.Count >= MinSteps && total + add > limit) break;
                total += add;
                foreach (string name in step.Blobs) seen.Add(name);
                take.Add(step);
            }
            take.Reverse();
            var xmls = new List<string>();
            foreach (var step in take)
            {
                xmls.Add(step.Xml);
                foreach (string name in step.Blobs) blobs[name] = _blobs[name].Bytes;
            }
            return xmls;
        }

        // ── Store ─────────────────────────────────────────────────

        private HistoryStep Capture(string xml)
        {
            var found = new Dictionary<string, byte[]>();
            var names = new List<string>();
            string packed = BlobPacker.Externalize(xml, found, names);
            foreach (string name in names)
            {
                Blob b;
                if (_blobs.TryGetValue(name, out b)) b.Refs++;
                else
                {
                    _blobs[name] = new Blob { Bytes = found[name], Refs = 1 };
                    _blobBytes += found[name].Length;
                }
            }
            _xmlBytes += (long)packed.Length * 2;
            return new HistoryStep { Xml = packed, Blobs = names.ToArray() };
        }

        private void Release(HistoryStep step)
        {
            foreach (string name in step.Blobs)
            {
                Blob b;
                if (!_blobs.TryGetValue(name, out b)) continue;
                if (--b.Refs > 0) continue;
                _blobs.Remove(name);
                _blobBytes -= b.Bytes.Length;
            }
            _xmlBytes -= (long)step.Xml.Length * 2;
        }

        // The XML with its pictures back in place, and the step is spent.
        private string Take(HistoryStep step)
        {
            string xml = Materialize(step);
            Release(step);
            return xml;
        }

        private string Materialize(HistoryStep step)
        {
            return BlobPacker.Inline(step.Xml, name => _blobs[name].Bytes, new Dictionary<string, string>());
        }

        private void DropRedo()
        {
            foreach (var step in _redo) Release(step);
            _redo.Clear();
        }

        private void Trim()
        {
            int drop = 0;
            while (_undo.Count - drop > MinSteps && Weight > MemoryLimit)
            {
                Release(_undo[drop]);
                drop++;
            }
            if (drop > 0) _undo.RemoveRange(0, drop);
        }
    }
}
