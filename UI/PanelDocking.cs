using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace Aplat.UI
{
    // Where a floating card is anchored. The eight positions are the corners and the edge middles of the
    // viewport; None means the card is free (it still keeps its distance from the nearest corner).
    public enum DockAnchor { None, TopLeft, Top, TopRight, Left, Right, BottomLeft, Bottom, BottomRight }

    // One card's placement: either docked to an anchor (Order sorts the cards sharing it, 0 first) or free,
    // stored as the distance from the nearest viewport corner so that it follows that corner when the
    // window is resized or maximised.
    public sealed class PanelPlacement
    {
        public DockAnchor Anchor;
        public int Order;
        public bool FromRight, FromBottom;   // free: which corner the distances are measured from
        public int Dx, Dy;

        public bool Docked { get { return Anchor != DockAnchor.None; } }

        public static PanelPlacement Dock(DockAnchor anchor, int order)
        {
            return new PanelPlacement { Anchor = anchor, Order = order };
        }

        public PanelPlacement Clone()
        {
            return (PanelPlacement)MemberwiseClone();
        }

        // "dock:BottomLeft:1" or "free:BR:120:30" — plain text for the settings file.
        public string Serialize()
        {
            if (Docked) return string.Format(CultureInfo.InvariantCulture, "dock:{0}:{1}", Anchor, Order);
            return string.Format(CultureInfo.InvariantCulture, "free:{0}{1}:{2}:{3}",
                FromBottom ? "B" : "T", FromRight ? "R" : "L", Dx, Dy);
        }

        public static bool TryParse(string text, out PanelPlacement result)
        {
            result = null;
            if (string.IsNullOrEmpty(text)) return false;
            var f = text.Split(':');
            try
            {
                if (f[0] == "dock" && f.Length >= 3)
                {
                    DockAnchor a;
                    if (!Enum.TryParse(f[1], out a) || a == DockAnchor.None || !Enum.IsDefined(typeof(DockAnchor), a)) return false;
                    result = Dock(a, int.Parse(f[2], CultureInfo.InvariantCulture));
                    return true;
                }
                if (f[0] == "free" && f.Length >= 4 && f[1].Length == 2)
                {
                    result = new PanelPlacement
                    {
                        FromBottom = f[1][0] == 'B',
                        FromRight  = f[1][1] == 'R',
                        Dx = int.Parse(f[2], CultureInfo.InvariantCulture),
                        Dy = int.Parse(f[3], CultureInfo.InvariantCulture)
                    };
                    return true;
                }
            }
            catch (FormatException) { }
            catch (OverflowException) { }
            return false;
        }
    }

    // The geometry of docking floating cards over the viewport, kept free of any window code so it can be
    // tested on its own. All rectangles are in viewport coordinates (origin = its top-left corner).
    public static class PanelDocking
    {
        public const int Margin   = 16;   // distance between a docked card and the viewport edge
        public const int Gap      = 8;    // distance between neighbouring docked cards
        public const int MinInset = 8;    // free cards are kept at least this far inside
        public const int Zone     = 28;   // how close to an edge a dragged card must come to dock there

        // ── Layout ─────────────────────────────────────────────────

        // Rectangles of all cards for the given placements. Invisible cards get an empty rectangle and take
        // no room. Docked cards sharing an anchor form a row (or a column) next to the edge; groups that
        // would overlap a corner group slide along their edge to clear it.
        public static Rectangle[] Layout(Size scene, Size[] sizes, PanelPlacement[] places, bool[] visible)
        {
            int n = sizes.Length;
            var rects = new Rectangle[n];
            var placed = new List<Rectangle>();

            // Corners first (they claim their spot), then the edge middles, which dodge them.
            DockAnchor[] order =
            {
                DockAnchor.TopLeft, DockAnchor.TopRight, DockAnchor.BottomLeft, DockAnchor.BottomRight,
                DockAnchor.Top, DockAnchor.Bottom, DockAnchor.Left, DockAnchor.Right
            };
            foreach (var anchor in order)
            {
                var members = Members(places, visible, anchor);
                if (members.Count == 0) continue;
                PlaceGroup(scene, sizes, members, anchor, rects, placed);
            }

            for (int i = 0; i < n; i++)
            {
                if (!visible[i] || places[i].Docked) continue;
                rects[i] = FreeRect(scene, sizes[i], places[i]);
            }
            return rects;
        }

        private static List<int> Members(PanelPlacement[] places, bool[] visible, DockAnchor anchor)
        {
            var list = new List<int>();
            for (int i = 0; i < places.Length; i++)
                if (visible[i] && places[i].Anchor == anchor) list.Add(i);
            list.Sort((a, b) =>
            {
                int c = places[a].Order.CompareTo(places[b].Order);
                return c != 0 ? c : a.CompareTo(b);
            });
            return list;
        }

        private static bool IsVertical(DockAnchor a) { return a == DockAnchor.Left || a == DockAnchor.Right; }
        private static bool IsCentred(DockAnchor a) { return a == DockAnchor.Top || a == DockAnchor.Bottom || a == DockAnchor.Left || a == DockAnchor.Right; }
        private static bool IsRight(DockAnchor a) { return a == DockAnchor.Right || a == DockAnchor.TopRight || a == DockAnchor.BottomRight; }
        private static bool IsBottom(DockAnchor a) { return a == DockAnchor.Bottom || a == DockAnchor.BottomLeft || a == DockAnchor.BottomRight; }

        private static void PlaceGroup(Size scene, Size[] sizes, List<int> members, DockAnchor anchor,
                                       Rectangle[] rects, List<Rectangle> placed)
        {
            bool vertical = IsVertical(anchor);
            int total = 0;
            foreach (int i in members) total += (vertical ? sizes[i].Height : sizes[i].Width);
            total += Gap * (members.Count - 1);

            // Where the run starts along its axis.
            int along;
            if (IsCentred(anchor))
                along = vertical ? (scene.Height - total) / 2 : (scene.Width - total) / 2;
            else
                along = IsRight(anchor) ? scene.Width - Margin - total : Margin;

            var group = new List<Rectangle>();
            foreach (int i in members)
            {
                Size s = sizes[i];
                int x, y;
                if (vertical)
                {
                    x = anchor == DockAnchor.Right ? scene.Width - Margin - s.Width : Margin;
                    y = along;
                    along += s.Height + Gap;
                }
                else
                {
                    x = along;
                    y = IsBottom(anchor) ? scene.Height - Margin - s.Height : Margin;
                    along += s.Width + Gap;
                }
                group.Add(new Rectangle(x, y, s.Width, s.Height));
            }

            // Corner runs grow away from the corner, so on the right side the first card is the rightmost.
            if (!vertical && IsRight(anchor) && !IsCentred(anchor))
            {
                int x = scene.Width - Margin;
                for (int k = 0; k < group.Count; k++)
                {
                    var r = group[k];
                    x -= r.Width;
                    group[k] = new Rectangle(x, r.Y, r.Width, r.Height);
                    x -= Gap;
                }
            }

            if (IsCentred(anchor)) SlideClear(scene, group, placed, vertical);

            for (int k = 0; k < members.Count; k++)
            {
                rects[members[k]] = group[k];
                placed.Add(group[k]);
            }
        }

        // Moves a centred group along its edge until it no longer touches the groups placed before it.
        private static void SlideClear(Size scene, List<Rectangle> group, List<Rectangle> placed, bool vertical)
        {
            for (int pass = 0; pass < 6; pass++)
            {
                Rectangle bounds = Union(group);
                Rectangle blocker = Rectangle.Empty;
                foreach (var p in placed)
                {
                    Rectangle grown = Rectangle.Inflate(p, Gap, Gap);
                    if (grown.IntersectsWith(bounds)) { blocker = p; break; }
                }
                if (blocker.IsEmpty) return;

                int lo = Margin;
                int hi = vertical ? scene.Height - Margin : scene.Width - Margin;
                int start = vertical ? bounds.Top : bounds.Left;
                int end   = vertical ? bounds.Bottom : bounds.Right;
                int bStart = vertical ? blocker.Top : blocker.Left;
                int bEnd   = vertical ? blocker.Bottom : blocker.Right;

                int forward  = bEnd + Gap - start;       // push past the blocker's far side
                int backward = bStart - Gap - end;       // or before its near side
                bool forwardFits  = end + forward <= hi;
                bool backwardFits = start + backward >= lo;
                int shift;
                if (forwardFits && backwardFits) shift = Math.Abs(forward) <= Math.Abs(backward) ? forward : backward;
                else if (forwardFits)  shift = forward;
                else if (backwardFits) shift = backward;
                else return;                                  // no room at all: keep it where it is

                for (int k = 0; k < group.Count; k++)
                {
                    var r = group[k];
                    group[k] = vertical ? new Rectangle(r.X, r.Y + shift, r.Width, r.Height)
                                        : new Rectangle(r.X + shift, r.Y, r.Width, r.Height);
                }
            }
        }

        private static Rectangle Union(List<Rectangle> rects)
        {
            Rectangle u = rects[0];
            for (int i = 1; i < rects.Count; i++) u = Rectangle.Union(u, rects[i]);
            return u;
        }

        // A free card: measured from its stored corner, never leaving the viewport.
        public static Rectangle FreeRect(Size scene, Size size, PanelPlacement p)
        {
            int x = p.FromRight  ? scene.Width  - size.Width  - p.Dx : p.Dx;
            int y = p.FromBottom ? scene.Height - size.Height - p.Dy : p.Dy;
            return ClampInside(new Rectangle(x, y, size.Width, size.Height), scene);
        }

        public static Rectangle ClampInside(Rectangle r, Size scene)
        {
            int maxX = scene.Width  - r.Width  - MinInset;
            int maxY = scene.Height - r.Height - MinInset;
            int x = maxX < MinInset ? MinInset : Math.Max(MinInset, Math.Min(r.X, maxX));
            int y = maxY < MinInset ? MinInset : Math.Max(MinInset, Math.Min(r.Y, maxY));
            return new Rectangle(x, y, r.Width, r.Height);
        }

        // A free placement for a card standing at r: measured from the corner it is nearest to.
        public static PanelPlacement MakeFree(Rectangle r, Size scene)
        {
            bool right  = r.X + r.Width / 2  > scene.Width  / 2;
            bool bottom = r.Y + r.Height / 2 > scene.Height / 2;
            return new PanelPlacement
            {
                FromRight = right, FromBottom = bottom,
                Dx = right  ? scene.Width  - r.Right  : r.Left,
                Dy = bottom ? scene.Height - r.Bottom : r.Top
            };
        }

        // ── Dragging ───────────────────────────────────────────────

        // The anchor a dragged card would dock to, or None when it is not close enough to an edge. Near one
        // edge only, the card's position along that edge picks the corner (outer thirds) or the middle.
        public static DockAnchor HitAnchor(Rectangle r, Size scene, int zone)
        {
            int reach = Margin + zone;
            bool left   = r.Left <= reach;
            bool right  = scene.Width  - r.Right  <= reach;
            bool top    = r.Top <= reach;
            bool bottom = scene.Height - r.Bottom <= reach;
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;

            // Touching opposite edges at once (a tiny viewport): the nearer one wins.
            if (left && right)  { if (cx < scene.Width / 2) right = false; else left = false; }
            if (top && bottom)  { if (cy < scene.Height / 2) bottom = false; else top = false; }

            if (left && top) return DockAnchor.TopLeft;
            if (right && top) return DockAnchor.TopRight;
            if (left && bottom) return DockAnchor.BottomLeft;
            if (right && bottom) return DockAnchor.BottomRight;

            if (left || right)
            {
                if (cy < scene.Height / 3) return left ? DockAnchor.TopLeft : DockAnchor.TopRight;
                if (cy > scene.Height * 2 / 3) return left ? DockAnchor.BottomLeft : DockAnchor.BottomRight;
                return left ? DockAnchor.Left : DockAnchor.Right;
            }
            if (top || bottom)
            {
                if (cx < scene.Width / 3) return top ? DockAnchor.TopLeft : DockAnchor.BottomLeft;
                if (cx > scene.Width * 2 / 3) return top ? DockAnchor.TopRight : DockAnchor.BottomRight;
                return top ? DockAnchor.Top : DockAnchor.Bottom;
            }
            return DockAnchor.None;
        }

        // Magnetism between free cards: pulls r so that its edge meets a neighbour's (side by side with a
        // small gap, or flush with the same edge) when they are within threshold pixels.
        public static Rectangle SnapToCards(Rectangle r, IList<Rectangle> others, int threshold)
        {
            int dx = BestDelta(threshold, others, o => new[] { o.Right + Gap - r.Left, o.Left - Gap - r.Right, o.Left - r.Left, o.Right - r.Right });
            int dy = BestDelta(threshold, others, o => new[] { o.Bottom + Gap - r.Top, o.Top - Gap - r.Bottom, o.Top - r.Top, o.Bottom - r.Bottom });
            // Only a neighbour that is actually beside the card (overlapping in the other axis) pulls it.
            if (dx != 0 && !HasNeighbour(r, others, threshold, true)) dx = 0;
            if (dy != 0 && !HasNeighbour(r, others, threshold, false)) dy = 0;
            r.Offset(dx, dy);
            return r;
        }

        private static int BestDelta(int threshold, IList<Rectangle> others, Func<Rectangle, int[]> candidates)
        {
            int best = 0, bestAbs = threshold + 1;
            foreach (var o in others)
                foreach (int d in candidates(o))
                    if (Math.Abs(d) < bestAbs) { best = d; bestAbs = Math.Abs(d); }
            return bestAbs <= threshold ? best : 0;
        }

        private static bool HasNeighbour(Rectangle r, IList<Rectangle> others, int threshold, bool horizontal)
        {
            foreach (var o in others)
            {
                bool overlap = horizontal
                    ? r.Bottom + threshold >= o.Top && r.Top - threshold <= o.Bottom
                    : r.Right + threshold >= o.Left && r.Left - threshold <= o.Right;
                if (overlap) return true;
            }
            return false;
        }

        // Where a card dropped on `anchor` goes among the cards already docked there: renumbers their Order
        // so the dragged one (index `dragged`) sits where its centre falls along the row / column.
        public static void InsertAt(PanelPlacement[] places, Rectangle[] rects, int dragged, DockAnchor anchor, Point centre)
        {
            bool vertical = IsVertical(anchor);
            bool reversed = !vertical && IsRight(anchor) && !IsCentred(anchor);   // corner run grows leftwards
            var others = new List<int>();
            for (int i = 0; i < places.Length; i++)
                if (i != dragged && places[i].Anchor == anchor) others.Add(i);
            others.Sort((a, b) => places[a].Order.CompareTo(places[b].Order));

            int pos = vertical ? centre.Y : centre.X;
            int slot = others.Count;
            for (int k = 0; k < others.Count; k++)
            {
                Rectangle o = rects[others[k]];
                int mid = vertical ? o.Top + o.Height / 2 : o.Left + o.Width / 2;
                bool before = reversed ? pos > mid : pos < mid;
                if (before) { slot = k; break; }
            }
            others.Insert(slot, dragged);
            for (int k = 0; k < others.Count; k++) places[others[k]].Order = k;
            places[dragged].Anchor = anchor;
        }
    }
}
