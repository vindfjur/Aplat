using System;
using System.Collections.Generic;
using System.Linq;
using Aplat.Rendering;

namespace Aplat.Models
{
    public enum MeshPick { None, Vertex, Edge, Face }

    // Shape editing of one mesh: pick a vertex, an edge or a face and move it. Only positions
    // change — the vertex and face lists keep their size and order, so the per-face paint
    // atlas, the saved file and the exports stay valid.
    //
    // Primitives repeat a point at the same place: a cube corner is one vertex per face, a
    // sphere's seam and poles are duplicated. Such vertices are "welded" by position into one
    // point; picking a point moves every vertex of it together, otherwise the mesh tears.
    //
    // No UI here: the view hands in a projection function for picking and world-space deltas
    // for moving, so the logic runs (and is tested) without a window.
    public sealed class MeshEditor
    {
        public const float MaxCoord = 100000f;     // a vertex never goes beyond this (keeps bounds, camera and export finite)
        private const float WeldScale = 10000f;    // the same 1e-4 tolerance as the renderer's normal smoothing

        private struct Key : IEquatable<Key>
        {
            public int X, Y, Z;
            public bool Equals(Key o) { return X == o.X && Y == o.Y && Z == o.Z; }
            public override bool Equals(object o) { return o is Key && Equals((Key)o); }
            public override int GetHashCode() { unchecked { return (X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791); } }
        }

        private readonly Mesh _mesh;
        private readonly int[] _pointOf;         // vertex → welded point (-1: not used by any valid face / out of range)
        private readonly int[] _start;           // welded point → its vertices (CSR)
        private readonly int[] _verts;
        private readonly int _points;
        private readonly int[] _edgeA, _edgeB;   // unique edges between welded points
        private readonly bool[] _picked;
        private readonly int[] _feStart, _fe;    // face → the edges around it (CSR; -1 where a face index is out of range)
        private readonly int[] _efStart, _efFaces;   // edge → the faces on it (CSR)
        private readonly bool[] _flat;           // edge → lies inside a flat patch (the diagonal of a quad): not shown or picked
        private const float FlatCos = 0.9998f;   // two neighbouring faces within ~1° are one flat patch

        private int[] _patch = new int[4];       // the faces of the picked flat patch
        private int _patchCount;

        private int[] _sel = new int[8];
        private int _selCount;
        private MeshPick _kind;
        private int _face = -1;

        private int[] _moveIdx;                  // vertices being dragged and where they started
        private Vec3[] _moveBase;
        private bool _dragging;

        private float[] _sx, _sy, _sz;           // screen scratch of a pick, reused
        private bool[] _seenP, _seenE;           // what the last pick found on a face turned towards the viewer

        public MeshEditor(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException("mesh");
            _mesh = mesh;
            int vc = mesh.Vertices.Count;

            _pointOf = new int[vc];
            var seen = new Dictionary<Key, int>(vc);
            for (int i = 0; i < vc; i++)
            {
                Vec3 v = mesh.Vertices[i];
                var key = new Key { X = Quant(v.X), Y = Quant(v.Y), Z = Quant(v.Z) };
                int id;
                if (!seen.TryGetValue(key, out id)) { id = seen.Count; seen[key] = id; }
                _pointOf[i] = id;
            }
            _points = seen.Count;

            var count = new int[_points + 1];
            for (int i = 0; i < vc; i++) count[_pointOf[i] + 1]++;
            for (int i = 0; i < _points; i++) count[i + 1] += count[i];
            _start = count;
            _verts = new int[vc];
            var fill = (int[])count.Clone();
            for (int i = 0; i < vc; i++) _verts[fill[_pointOf[i]]++] = i;

            var edges = new Dictionary<long, int>();
            var ea = new List<int>();
            var eb = new List<int>();
            var fe = new List<int>();
            _feStart = new int[mesh.Faces.Count + 1];
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                _feStart[fi] = fe.Count;
                var f = mesh.Faces[fi];
                if (f == null) continue;
                for (int k = 0; k < f.Length; k++)
                {
                    int i0 = f[k], i1 = f[(k + 1) % f.Length];
                    if (i0 < 0 || i0 >= vc || i1 < 0 || i1 >= vc) continue;
                    int a = _pointOf[i0], b = _pointOf[i1];
                    if (a == b) continue;
                    if (a > b) { int t = a; a = b; b = t; }
                    long key = (long)a * _points + b;
                    int id;
                    if (!edges.TryGetValue(key, out id)) { id = ea.Count; edges[key] = id; ea.Add(a); eb.Add(b); }
                    fe.Add(id);
                }
            }
            _feStart[mesh.Faces.Count] = fe.Count;
            _fe = fe.ToArray();
            _edgeA = ea.ToArray();
            _edgeB = eb.ToArray();
            _picked = new bool[_points];

            _efStart = new int[_edgeA.Length + 1];
            foreach (int e in _fe) _efStart[e + 1]++;
            for (int e = 0; e < _edgeA.Length; e++) _efStart[e + 1] += _efStart[e];
            _efFaces = new int[_fe.Length];
            var efill = (int[])_efStart.Clone();
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
                for (int j = _feStart[fi]; j < _feStart[fi + 1]; j++) _efFaces[efill[_fe[j]]++] = fi;
            _flat = new bool[_edgeA.Length];
            RefreshFlat();
        }

        private static int Quant(float v)
        {
            double q = Math.Round(v * WeldScale);
            if (double.IsNaN(q)) return 0;
            return q > int.MaxValue ? int.MaxValue : (q < int.MinValue ? int.MinValue : (int)q);
        }

        public Mesh Mesh { get { return _mesh; } }

        // True while the mesh still has the shape this editor was built for.
        public bool Matches(Mesh mesh)
        {
            return ReferenceEquals(mesh, _mesh) && mesh.Vertices.Count == _pointOf.Length;
        }

        // ── Points and edges ──────────────────────────────────────

        public int PointCount { get { return _points; } }
        public int EdgeCount  { get { return _edgeA.Length; } }
        public int PointOf(int vertex) { return _pointOf[vertex]; }
        public bool EdgeShown(int edge) { return !_flat[edge]; }
        public int EdgeA(int edge) { return _edgeA[edge]; }
        public int EdgeB(int edge) { return _edgeB[edge]; }
        public int VertexCountOf(int point) { return _start[point + 1] - _start[point]; }
        public Vec3 PointAt(int point) { return _mesh.Vertices[_verts[_start[point]]]; }
        public bool IsPicked(int point) { return _picked[point]; }

        // ── The selection ─────────────────────────────────────────

        public MeshPick Kind       { get { return _kind; } }
        public int      Face       { get { return _face; } }
        public int      PatchCount { get { return _patchCount; } }
        public int      PatchFace(int i) { return _patch[i]; }
        public int      PickedCount { get { return _selCount; } }
        public int      PickedPoint(int i) { return _sel[i]; }
        public bool     HasSelection { get { return _selCount > 0; } }

        public void ClearSelection()
        {
            for (int i = 0; i < _selCount; i++) _picked[_sel[i]] = false;
            _selCount = 0;
            _kind = MeshPick.None;
            _face = -1;
            _patchCount = 0;
            _dragging = false;
        }

        private void Add(int point)
        {
            if (_picked[point]) return;
            if (_selCount == _sel.Length) Array.Resize(ref _sel, _sel.Length * 2);
            _sel[_selCount++] = point;
            _picked[point] = true;
        }

        public void SelectPoint(int point)
        {
            ClearSelection();
            if (point < 0 || point >= _points) return;
            Add(point);
            _kind = MeshPick.Vertex;
        }

        public void SelectEdge(int edge)
        {
            ClearSelection();
            if (edge < 0 || edge >= _edgeA.Length) return;
            Add(_edgeA[edge]);
            Add(_edgeB[edge]);
            _kind = MeshPick.Edge;
        }

        // A face is picked together with the neighbouring faces that lie in the same plane: the
        // two triangles of a cube's side, a cylinder's cap. Dragging it then moves the whole flat
        // patch, not half of it.
        public void SelectFace(int face)
        {
            ClearSelection();
            if (face < 0 || face >= _mesh.Faces.Count || _mesh.Faces[face] == null) return;
            var seen = new bool[_mesh.Faces.Count];
            var stack = new Stack<int>();
            Vec3 n = FaceNormal(face);
            bool valid = n.Length > 0.5f;
            seen[face] = true; stack.Push(face);
            while (stack.Count > 0)
            {
                int f = stack.Pop();
                AddPatch(f);
                if (!valid) continue;
                for (int j = _feStart[f]; j < _feStart[f + 1]; j++)
                {
                    int e = _fe[j];
                    for (int k = _efStart[e]; k < _efStart[e + 1]; k++)
                    {
                        int g = _efFaces[k];
                        if (seen[g]) continue;
                        Vec3 ng = FaceNormal(g);
                        if (ng.Length > 0.5f && Vec3.Dot(n, ng) >= FlatCos) { seen[g] = true; stack.Push(g); }
                    }
                }
            }
            foreach (int pf in _patch.Take(_patchCount))
                foreach (int v in _mesh.Faces[pf])
                    if (v >= 0 && v < _pointOf.Length) Add(_pointOf[v]);
            if (_selCount == 0) return;
            _kind = MeshPick.Face;
            _face = face;
        }

        private void AddPatch(int f)
        {
            if (_patchCount == _patch.Length) Array.Resize(ref _patch, _patch.Length * 2);
            _patch[_patchCount++] = f;
        }

        // Unit normal of a face (Newell's method), or zero for a collapsed one.
        private Vec3 FaceNormal(int face)
        {
            var f = _mesh.Faces[face];
            if (f == null || f.Length < 3) return Vec3.Zero;
            Vec3 n = Vec3.Zero;
            for (int i = 0; i < f.Length; i++)
            {
                int ia = f[i], ib = f[(i + 1) % f.Length];
                if (ia < 0 || ia >= _pointOf.Length || ib < 0 || ib >= _pointOf.Length) return Vec3.Zero;
                Vec3 a = _mesh.Vertices[ia], b = _mesh.Vertices[ib];
                n = new Vec3(n.X + (a.Y - b.Y) * (a.Z + b.Z), n.Y + (a.Z - b.Z) * (a.X + b.X), n.Z + (a.X - b.X) * (a.Y + b.Y));
            }
            float len = n.Length;
            return len < 1e-9f || float.IsNaN(len) ? Vec3.Zero : n * (1f / len);
        }

        // Edges between two faces of one plane (the diagonal of a quad) are not real edges of
        // the shape: they are neither drawn nor picked. Re-evaluated after every drag.
        private void RefreshFlat()
        {
            for (int e = 0; e < _flat.Length; e++)
            {
                bool flat = false;
                if (_efStart[e + 1] - _efStart[e] == 2)
                {
                    Vec3 a = FaceNormal(_efFaces[_efStart[e]]), b = FaceNormal(_efFaces[_efStart[e] + 1]);
                    flat = a.Length > 0.5f && b.Length > 0.5f && Vec3.Dot(a, b) >= FlatCos;
                }
                _flat[e] = flat;
            }
        }

        // The middle of the picked points (where the axis handles stand).
        public Vec3 PickedCenter()
        {
            if (_selCount == 0) return Vec3.Zero;
            Vec3 sum = Vec3.Zero;
            for (int i = 0; i < _selCount; i++) sum = sum + PointAt(_sel[i]);
            return sum * (1f / _selCount);
        }

        // ── Picking by screen position ────────────────────────────

        // `project` turns a local point into screen x, y (y down) and the camera-space z (larger is
        // nearer to the viewer); x < -9000 means the point cannot be shown. Points and edges that
        // belong only to faces turned away from the viewer (the far side of a solid) are not
        // pickable, as they are not drawn; faces wind counter-clockwise seen from outside. Order: the nearest
        // point within pointRadius, else the nearest edge within edgeRadius, else the face under
        // the pointer (the nearest one where they overlap). Where two candidates are about as
        // close to the pointer, the one nearer the viewer wins. Does not change the selection.
        public MeshPick Pick(Func<Vec3, Vec3> project, float x, float y, float pointRadius, float edgeRadius, out int index)
        {
            index = -1;
            if (project == null || _points == 0) return MeshPick.None;
            if (_sx == null) { _sx = new float[_points]; _sy = new float[_points]; _sz = new float[_points]; }
            for (int p = 0; p < _points; p++)
            {
                Vec3 s = project(PointAt(p));
                bool ok = s.X > -9000f && !float.IsNaN(s.X + s.Y + s.Z) && !float.IsInfinity(s.X + s.Y + s.Z);
                _sx[p] = ok ? s.X : float.NaN; _sy[p] = s.Y; _sz[p] = s.Z;
            }

            MarkFacing();

            // Points.
            float best = pointRadius; float bestZ = float.NegativeInfinity;
            for (int p = 0; p < _points; p++)
            {
                if (float.IsNaN(_sx[p]) || !_seenP[p]) continue;
                float d = Dist(_sx[p], _sy[p], x, y);
                if (d > pointRadius) continue;
                if (Better(d, _sz[p], best, bestZ, index >= 0)) { best = d; bestZ = _sz[p]; index = p; }
            }
            if (index >= 0) return MeshPick.Vertex;

            // Edges.
            best = edgeRadius; bestZ = float.NegativeInfinity;
            for (int e = 0; e < _edgeA.Length; e++)
            {
                if (_flat[e] || !_seenE[e]) continue;
                int a = _edgeA[e], b = _edgeB[e];
                if (float.IsNaN(_sx[a]) || float.IsNaN(_sx[b])) continue;
                float d = DistSeg(_sx[a], _sy[a], _sx[b], _sy[b], x, y);
                if (d > edgeRadius) continue;
                float z = (_sz[a] + _sz[b]) * 0.5f;
                if (Better(d, z, best, bestZ, index >= 0)) { best = d; bestZ = z; index = e; }
            }
            if (index >= 0) return MeshPick.Edge;

            // Faces: the fan of triangles from the first corner; the depth is interpolated.
            bestZ = float.NegativeInfinity;
            for (int fi = 0; fi < _mesh.Faces.Count; fi++)
            {
                var f = _mesh.Faces[fi];
                if (f == null || f.Length < 3) continue;
                int p0 = Point(f[0]);
                if (p0 < 0 || float.IsNaN(_sx[p0])) continue;
                for (int k = 1; k + 1 < f.Length; k++)
                {
                    int p1 = Point(f[k]), p2 = Point(f[k + 1]);
                    if (p1 < 0 || p2 < 0 || float.IsNaN(_sx[p1]) || float.IsNaN(_sx[p2])) continue;
                    float z;
                    if (InTriangle(x, y, p0, p1, p2, out z) && z > bestZ) { bestZ = z; index = fi; }
                }
            }
            return index >= 0 ? MeshPick.Face : MeshPick.None;
        }

        // Marks the points and edges of every face turned towards the viewer (clockwise on a
        // y-down screen). A mesh with no such face at all (wound the other way round) is all marked.
        private void MarkFacing()
        {
            if (_seenP == null) { _seenP = new bool[_points]; _seenE = new bool[_edgeA.Length]; }
            Array.Clear(_seenP, 0, _seenP.Length);
            Array.Clear(_seenE, 0, _seenE.Length);
            bool any = false;
            for (int fi = 0; fi < _mesh.Faces.Count; fi++)
            {
                var f = _mesh.Faces[fi];
                if (f == null || f.Length < 3) continue;
                float area = 0f; bool ok = true;
                for (int k = 0; k < f.Length && ok; k++)
                {
                    int a = Point(f[k]), b = Point(f[(k + 1) % f.Length]);
                    if (a < 0 || b < 0 || float.IsNaN(_sx[a]) || float.IsNaN(_sx[b])) { ok = false; break; }
                    area += _sx[a] * _sy[b] - _sx[b] * _sy[a];
                }
                if (!ok || !(area < -1e-3f)) continue;
                any = true;
                foreach (int v in f) _seenP[Point(v)] = true;
                for (int j = _feStart[fi]; j < _feStart[fi + 1]; j++) _seenE[_fe[j]] = true;
            }
            if (!any)
            {
                for (int i = 0; i < _seenP.Length; i++) _seenP[i] = true;
                for (int i = 0; i < _seenE.Length; i++) _seenE[i] = true;
            }
        }

        private int Point(int vertex) { return vertex >= 0 && vertex < _pointOf.Length ? _pointOf[vertex] : -1; }

        // `d` beats the current best when it is clearly closer, or about as close and nearer the viewer.
        private static bool Better(float d, float z, float best, float bestZ, bool have)
        {
            if (!have) return true;
            if (d < best - 2f) return true;
            return d <= best + 2f && z > bestZ;
        }

        private static float Dist(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx, dy = ay - by;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float DistSeg(float ax, float ay, float bx, float by, float px, float py)
        {
            float dx = bx - ax, dy = by - ay;
            float l2 = dx * dx + dy * dy;
            if (l2 < 1e-6f) return Dist(ax, ay, px, py);
            float t = ((px - ax) * dx + (py - ay) * dy) / l2;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return Dist(ax + t * dx, ay + t * dy, px, py);
        }

        private bool InTriangle(float x, float y, int a, int b, int c, out float z)
        {
            z = 0f;
            float x0 = _sx[a], y0 = _sy[a], x1 = _sx[b], y1 = _sy[b], x2 = _sx[c], y2 = _sy[c];
            float den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (Math.Abs(den) < 1e-6f) return false;               // edge-on or collapsed on the screen
            float l0 = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / den;
            float l1 = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / den;
            float l2 = 1f - l0 - l1;
            if (l0 < 0f || l1 < 0f || l2 < 0f) return false;
            z = l0 * _sz[a] + l1 * _sz[b] + l2 * _sz[c];
            return true;
        }

        // ── Moving ────────────────────────────────────────────────

        // Starts a drag of the selection; false when nothing is picked.
        public bool BeginDrag()
        {
            if (_selCount == 0) return false;
            int n = 0;
            for (int i = 0; i < _selCount; i++) n += VertexCountOf(_sel[i]);
            _moveIdx = new int[n];
            _moveBase = new Vec3[n];
            int k = 0;
            for (int i = 0; i < _selCount; i++)
            {
                int p = _sel[i];
                for (int j = _start[p]; j < _start[p + 1]; j++)
                {
                    _moveIdx[k] = _verts[j];
                    _moveBase[k] = _mesh.Vertices[_verts[j]];
                    k++;
                }
            }
            _dragging = true;
            return true;
        }

        // Where the first picked point stood when the drag began.
        public Vec3 DragStartOfFirst { get { return _dragging && _moveBase.Length > 0 ? _moveBase[0] : Vec3.Zero; } }

        // Puts the picked vertices at their drag-start position plus `delta` (local space).
        // A delta that is NaN or infinite is ignored; coordinates are kept within ±MaxCoord.
        // Returns whether the mesh was changed.
        public bool MoveBy(Vec3 delta)
        {
            if (!_dragging) return false;
            float s = delta.X + delta.Y + delta.Z;
            if (float.IsNaN(s) || float.IsInfinity(s)) return false;
            for (int i = 0; i < _moveIdx.Length; i++)
            {
                Vec3 b = _moveBase[i];
                _mesh.Vertices[_moveIdx[i]] = new Vec3(Clamp(b.X + delta.X), Clamp(b.Y + delta.Y), Clamp(b.Z + delta.Z));
            }
            return true;
        }

        public void EndDrag() { _dragging = false; RefreshFlat(); }

        // Puts the dragged vertices back where they were.
        public void CancelDrag()
        {
            if (!_dragging) return;
            for (int i = 0; i < _moveIdx.Length; i++) _mesh.Vertices[_moveIdx[i]] = _moveBase[i];
            _dragging = false;
            RefreshFlat();
        }

        public bool Dragging { get { return _dragging; } }

        private static float Clamp(float v)
        {
            if (float.IsNaN(v)) return 0f;
            return v > MaxCoord ? MaxCoord : (v < -MaxCoord ? -MaxCoord : v);
        }

        // ── Space conversion ──────────────────────────────────────

        // A displacement in world space as the displacement in the object's local space
        // (undoing rotation and per-axis scale). False when the object is flattened by a
        // zero scale — the local space cannot be reached then.
        public static bool TryLocalDelta(SceneObject obj, Vec3 worldDelta, out Vec3 local)
        {
            local = Vec3.Zero;
            var xf = SceneGeometry.Xform.For(obj);
            Vec3 o  = xf.Apply(Vec3.Zero);
            Vec3 cx = xf.Apply(new Vec3(1, 0, 0)) - o;
            Vec3 cy = xf.Apply(new Vec3(0, 1, 0)) - o;
            Vec3 cz = xf.Apply(new Vec3(0, 0, 1)) - o;
            // world = cx·x + cy·y + cz·z  →  Cramer's rule.
            float det = Vec3.Dot(cx, Vec3.Cross(cy, cz));
            if (Math.Abs(det) < 1e-9f || float.IsNaN(det)) return false;
            float inv = 1f / det;
            local = new Vec3(
                Vec3.Dot(worldDelta, Vec3.Cross(cy, cz)) * inv,
                Vec3.Dot(cx, Vec3.Cross(worldDelta, cz)) * inv,
                Vec3.Dot(cx, Vec3.Cross(cy, worldDelta)) * inv);
            return !(float.IsNaN(local.X + local.Y + local.Z) || float.IsInfinity(local.X + local.Y + local.Z));
        }

        public static float Snap(float v, float step)
        {
            return step > 1e-6f ? (float)Math.Round(v / step) * step : v;
        }
    }
}
