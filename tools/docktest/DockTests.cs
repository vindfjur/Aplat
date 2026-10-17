using System;
using System.Collections.Generic;
using System.Drawing;
using Aplat.UI;

// Tests of the card docking geometry (UI/PanelDocking.cs). Pure maths, no windows.
// Windows: tools/paneltest/run.ps1 -Source tools/docktest/DockTests.cs (after a Release build).
class DockTests
{
    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; if (!ok) { fails++; Console.WriteLine("FAIL " + msg); } else Console.WriteLine("ok   " + msg); }

    static readonly Size Scene = new Size(1000, 600);

    static Rectangle[] Lay(Size scene, Size[] sizes, PanelPlacement[] pl, bool[] vis = null)
    {
        if (vis == null) { vis = new bool[sizes.Length]; for (int i = 0; i < vis.Length; i++) vis[i] = true; }
        return PanelDocking.Layout(scene, sizes, pl, vis);
    }

    static int Main()
    {
        var card = new Size(200, 50);

        // Docked positions: all eight anchors sit the margin away from the edges.
        var s8 = new Size[8]; var p8 = new PanelPlacement[8];
        DockAnchor[] all = { DockAnchor.TopLeft, DockAnchor.Top, DockAnchor.TopRight, DockAnchor.Left, DockAnchor.Right, DockAnchor.BottomLeft, DockAnchor.Bottom, DockAnchor.BottomRight };
        for (int i = 0; i < 8; i++) { s8[i] = card; p8[i] = PanelPlacement.Dock(all[i], 0); }
        var r8 = Lay(Scene, s8, p8);
        Check(r8[0].Location == new Point(16, 16), "TopLeft hugs the corner");
        Check(r8[1].X == (1000 - 200) / 2 && r8[1].Y == 16, "Top is centred along the top edge");
        Check(r8[2].Right == 1000 - 16 && r8[2].Y == 16, "TopRight hugs its corner");
        Check(r8[3].X == 16 && r8[3].Y == (600 - 50) / 2, "Left is centred on the left edge");
        Check(r8[4].Right == 1000 - 16, "Right sits at the right edge");
        Check(r8[5].X == 16 && r8[5].Bottom == 600 - 16, "BottomLeft hugs its corner");
        Check(r8[6].Bottom == 600 - 16, "Bottom sits at the bottom edge");
        Check(r8[7].Right == 984 && r8[7].Bottom == 584, "BottomRight hugs its corner");

        // Stacking: cards on one anchor line up with a gap, in Order.
        var two = Lay(Scene, new[] { card, card }, new[] { PanelPlacement.Dock(DockAnchor.TopLeft, 1), PanelPlacement.Dock(DockAnchor.TopLeft, 0) });
        Check(two[1].X == 16 && two[0].X == 16 + 200 + PanelDocking.Gap, "Order 0 is nearest the corner, the next one beside it");
        var twoR = Lay(Scene, new[] { card, card }, new[] { PanelPlacement.Dock(DockAnchor.BottomRight, 0), PanelPlacement.Dock(DockAnchor.BottomRight, 1) });
        Check(twoR[0].Right == 984 && twoR[1].Right == 984 - 200 - PanelDocking.Gap, "right corner runs grow leftwards");
        var col = Lay(Scene, new[] { card, card }, new[] { PanelPlacement.Dock(DockAnchor.Left, 0), PanelPlacement.Dock(DockAnchor.Left, 1) });
        Check(col[1].Y == col[0].Bottom + PanelDocking.Gap, "edge-middle cards stack as a column");
        Check(col[0].Y + (col[1].Bottom - col[0].Y) / 2 == 300, "...centred as a group");

        // Maximise: a corner card follows its corner, an edge card stays centred.
        var big = new Size(1920, 1000);
        var rBig = Lay(big, s8, p8);
        Check(rBig[7].Right == 1920 - 16 && rBig[7].Bottom == 1000 - 16, "after maximise BottomRight is at the new corner");
        Check(rBig[6].X == (1920 - 200) / 2, "after maximise Bottom is centred on the new width");

        // Overlap: Bottom (centred) dodges BottomLeft when the viewport is narrow.
        var narrow = new Size(600, 400);
        var dodge = Lay(narrow, new[] { new Size(232, 212), new Size(300, 46) },
            new[] { PanelPlacement.Dock(DockAnchor.BottomLeft, 0), PanelPlacement.Dock(DockAnchor.Bottom, 0) });
        Check(!dodge[0].IntersectsWith(dodge[1]), "a centred card slides clear of a corner card");
        var squeezed = Lay(new Size(300, 400), new[] { new Size(232, 212), new Size(300, 46) },
            new[] { PanelPlacement.Dock(DockAnchor.BottomLeft, 0), PanelPlacement.Dock(DockAnchor.Bottom, 0) });
        Check(squeezed[1].Width == 300, "when nothing fits the card is left alone (no exception, no resize)");

        // Hidden cards take no room.
        var hid = Lay(Scene, new[] { card, card }, new[] { PanelPlacement.Dock(DockAnchor.Top, 0), PanelPlacement.Dock(DockAnchor.Top, 1) }, new[] { false, true });
        Check(hid[0].IsEmpty && hid[1].X == (1000 - 200) / 2, "a hidden card is skipped and the other stays centred");

        // Free cards keep their distance from the nearest corner.
        var free = PanelDocking.MakeFree(new Rectangle(700, 400, 200, 50), Scene);
        Check(free.FromRight && free.FromBottom && free.Dx == 100 && free.Dy == 150, "free placement is measured from the nearest corner");
        var fr = PanelDocking.FreeRect(big, card, free);
        Check(fr.Right == 1920 - 100 && fr.Bottom == 1000 - 150, "a free card follows its corner when the window grows");
        var offscreen = PanelDocking.FreeRect(new Size(300, 200), card, new PanelPlacement { Dx = 900, Dy = 900 });
        Check(offscreen.Right <= 292 && offscreen.Bottom <= 192 && offscreen.X >= 8, "a free card is pulled back inside a small viewport");

        // Hit test: edges, corners, thirds.
        Check(PanelDocking.HitAnchor(new Rectangle(20, 20, 200, 50), Scene, 28) == DockAnchor.TopLeft, "near both edges → corner");
        Check(PanelDocking.HitAnchor(new Rectangle(400, 10, 200, 50), Scene, 28) == DockAnchor.Top, "near the top, middle third → Top");
        Check(PanelDocking.HitAnchor(new Rectangle(40, 10, 200, 50), Scene, 28) == DockAnchor.TopLeft, "near the top, left third → TopLeft");
        Check(PanelDocking.HitAnchor(new Rectangle(790, 300, 200, 50), Scene, 28) == DockAnchor.Right, "near the right edge, middle → Right");
        Check(PanelDocking.HitAnchor(new Rectangle(790, 500, 200, 50), Scene, 28) == DockAnchor.BottomRight, "near the right edge, low → BottomRight");
        Check(PanelDocking.HitAnchor(new Rectangle(400, 250, 200, 50), Scene, 28) == DockAnchor.None, "in the middle → free");
        Check(PanelDocking.HitAnchor(new Rectangle(0, 0, 1000, 600), Scene, 28) != DockAnchor.None, "a card as big as the viewport does not throw");

        // Magnetism between cards.
        var other = new Rectangle(300, 200, 200, 50);
        var snapped = PanelDocking.SnapToCards(new Rectangle(512, 205, 150, 50), new[] { other }, 12);
        Check(snapped.X == other.Right + PanelDocking.Gap && snapped.Y == 200, "snaps beside the neighbour and flush with its edge");
        var far = PanelDocking.SnapToCards(new Rectangle(700, 400, 150, 50), new[] { other }, 12);
        Check(far.Location == new Point(700, 400), "far from everything nothing moves");
        var nonNeighbour = PanelDocking.SnapToCards(new Rectangle(508, 500, 150, 50), new[] { other }, 12);
        Check(nonNeighbour.X == 508, "a card far below does not pull sideways");

        // Insertion order.
        var pl = new[] { PanelPlacement.Dock(DockAnchor.TopLeft, 0), PanelPlacement.Dock(DockAnchor.TopLeft, 1), new PanelPlacement() };
        var rects = Lay(Scene, new[] { card, card, card }, pl, new[] { true, true, false });
        PanelDocking.InsertAt(pl, rects, 2, DockAnchor.TopLeft, new Point(rects[0].Right + 2, 30));
        Check(pl[0].Order == 0 && pl[2].Order == 1 && pl[1].Order == 2 && pl[2].Anchor == DockAnchor.TopLeft, "dropped between two cards, it takes the middle slot");

        // Persistence.
        PanelPlacement q;
        Check(PanelPlacement.TryParse(PanelPlacement.Dock(DockAnchor.BottomRight, 3).Serialize(), out q) && q.Anchor == DockAnchor.BottomRight && q.Order == 3, "docked placement round-trips");
        Check(PanelPlacement.TryParse(free.Serialize(), out q) && !q.Docked && q.FromRight && q.FromBottom && q.Dx == 100 && q.Dy == 150, "free placement round-trips");
        Check(!PanelPlacement.TryParse("dock:Nowhere:1", out q), "unknown anchor is rejected");
        Check(!PanelPlacement.TryParse("dock:None:0", out q), "'None' is not a dock anchor");
        Check(!PanelPlacement.TryParse("free:XX:a:b", out q), "garbage numbers are rejected");
        Check(!PanelPlacement.TryParse("", out q) && !PanelPlacement.TryParse(null, out q), "empty text is rejected");

        Console.WriteLine(fails == 0 ? "ALL " + checks + " OK" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }
}
