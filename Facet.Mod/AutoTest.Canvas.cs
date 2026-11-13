using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using EventType = UnityEngine.EventType;

namespace Facet.Mod
{
    // The Editor window's Canvas page: selection, marquee, move, resize, one step per gesture, items replaced underneath.
    sealed partial class AutoTest
    {
        /* Every stage point in these sections goes through here, from a rect in canvas units. The view is zoom 1 and
           pan 0, so a canvas rect is where it is drawn. */
        static Rect StageOf(Rect canvas) => canvas;

        static Canvas<GalleryBox> Cv => GalleryEditor.Canvas;

        static GalleryBox BoxOf(string key)
        {
            var m = GalleryEditor.Model;
            return m.Boxes.Find(b => b.Key == key) ?? m.Other.Find(b => b.Key == key);
        }

        static Vector2 Mid(string key) => StageOf(BoxOf(key).Rect).center;

        static string Keys(Canvas<GalleryBox> c)
        {
            var s = "";
            for (int i = 0; i < c.Selection.Count; i++) s += (i > 0 ? "," : "") + c.Selection[i].Key;
            return s;
        }

        static void Send(EventType t, Vector2 stage, EventModifiers m = EventModifiers.None)
            => PointerAt(t, Cv.Stage.LocalToWorld(stage), GalleryEditor.Surface.Root, 0, m);

        // Down and up in one frame, as a still click.
        static IEnumerator Tap(Vector2 stage, EventModifiers m = EventModifiers.None)
        {
            Send(EventType.MouseDown, stage, m);
            Send(EventType.MouseUp, stage, m); yield return null;
        }

        // A drag in `steps` moves, one a frame; atEnd runs after the last move, before the release.
        static IEnumerator Pull(Vector2 from, Vector2 to, EventModifiers m = EventModifiers.None, int steps = 6, Action atEnd = null)
        {
            Send(EventType.MouseDown, from, m); yield return null;
            for (int i = 1; i <= steps; i++) { Send(EventType.MouseDrag, Vector2.Lerp(from, to, i / (float)steps), m); yield return null; }
            if (atEnd != null) atEnd();
            Send(EventType.MouseUp, to, m); yield return null;
        }

        // A fresh model and a fresh Canvas page, with an empty history.
        static IEnumerator FreshCanvas()
        {
            GalleryEditor.Model = new EditorModel();
            GalleryEditor.Nav.Go(GalleryEditor.CanvasItem);
            History.Clear();
            for (int i = 0; i < 3; i++) yield return null;
        }

        static long LogLength() { try { return new FileInfo(Main.LogPath).Length; } catch { return 0; } }

        static string LogSince(long at)
        {
            try
            {
                using (var f = new FileStream(Main.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    f.Seek(at, SeekOrigin.Begin);
                    return new StreamReader(f).ReadToEnd();
                }
            }
            catch { return ""; }
        }

        static float Left(Canvas<GalleryBox> c, string key)
        {
            var e = c.Content.Q("ed-box-" + key);
            return e == null ? float.NaN : e.style.left.value.value;
        }

        IEnumerator SectionCanvasEdit()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            var start = GalleryEditor.NewBoxes();
            Func<string, Rect> was = k => start.Find(b => b.Key == k).Rect;
            var empty = StageOf(new Rect(95, 80, 0, 0)).position;   // between boxes 1, 2, 5 and 6
            try
            {
                yield return FreshCanvas();
                var c = Cv;
                Check(c != null && c.Stage.panel != null, "the Editor window's Canvas page builds a canvas on the panel");
                if (c == null) yield break;
                Main.Log("[canvas] stage " + c.Stage.layout.width + " x " + c.Stage.layout.height);
                Check(c.Stage.layout.width >= 480f && c.Stage.layout.height >= 230f, "the stage shows every box and ghost at zoom 1",
                    c.Stage.layout.width + " x " + c.Stage.layout.height);

                // Selection.
                int sc = 0;
                c.SelectionChanged += () => sc++;
                yield return Tap(Mid("k3"));
                Check(Keys(c) == "k3" && sc == 1, "clicking box 3 selects [3] with one SelectionChanged", Keys(c) + " / " + sc);
                yield return Tap(Mid("k5"), EventModifiers.Control);
                Check(Keys(c) == "k3,k5", "Ctrl-clicking box 5 gives [3, 5]", Keys(c));
                yield return Tap(Mid("k3"), EventModifiers.Shift);
                Check(Keys(c) == "k5", "Shift-clicking box 3 gives [5]", Keys(c));
                yield return Tap(empty);
                Check(Keys(c) == "", "clicking empty stage gives []", Keys(c));

                // Marquee. A 4 px drag whose band would reach box 1 is still a click.
                bool banded = false;
                var near1 = StageOf(new Rect(17, 30, 0, 0)).position;
                yield return Pull(near1, near1 + new Vector2(4f, 0f), EventModifiers.None, 2, () => banded = c.Marquee != null);
                Check(Keys(c) == "" && !banded, "a 4 px drag on empty stage draws no marquee and selects nothing", Keys(c) + " / " + banded);
                yield return Tap(Mid("k5"));
                yield return Pull(near1, near1 + new Vector2(4f, 0f), EventModifiers.Control, 2, () => banded = c.Marquee != null);
                Check(Keys(c) == "k5" && !banded, "...and with Ctrl held it leaves [5] alone", Keys(c) + " / " + banded);
                yield return Tap(empty);
                var band0 = StageOf(new Rect(10, 10, 0, 0)).position; var band1 = StageOf(new Rect(175, 65, 0, 0)).position;
                yield return Pull(band0, band1);
                Check(Keys(c) == "k1,k2", "a drag over boxes 1 and 2 selects [1, 2]", Keys(c));
                yield return Tap(Mid("k5"));
                yield return Pull(band0, band1, EventModifiers.Control);
                Check(Keys(c) == "k5,k1,k2", "...and with Ctrl held it adds them to [5]", Keys(c));
                yield return Tap(empty);
                yield return Pull(StageOf(new Rect(285, 10, 0, 0)).position, StageOf(new Rect(420, 50, 0, 0)).position);
                Check(Keys(c) == "k4", "a ghost under the band is never selected", Keys(c));

                // Move.
                yield return FreshCanvas(); c = Cv;
                var b7 = BoxOf("k7");
                yield return Pull(Mid("k7"), Mid("k7") + new Vector2(30f, 0f));
                bool still = true;
                foreach (var b in GalleryEditor.Model.Boxes) if (b.Key != "k7" && b.Rect != was(b.Key)) still = false;
                Check(b7.Rect == new Rect(230, 100, 60, 40) && still, "dragging unselected box 7 by (30, 0) moves only box 7", b7.Rect.ToString());
                Check(History.NextUndo == "Move" && History.Undo() && !History.CanUndo && b7.Rect == was("k7"), "...as one step named Move", History.NextUndo);

                yield return FreshCanvas(); c = Cv;
                var b5 = BoxOf("k5");
                float left2 = BoxOf("k2").Rect.x, gx = float.NaN;
                var to = Mid("k5") + new Vector2(left2 + 6f - b5.Rect.x, 0f);
                yield return Pull(Mid("k5"), to, EventModifiers.None, 6, () => gx = c.Guides.x);
                Check(b5.Rect.x == left2, "box 5 released 6 from box 2's left lands exactly on it", b5.Rect.ToString());
                Check(gx == left2, "...with Guides.x on box 2's left while dragging", gx.ToString());

                yield return FreshCanvas(); c = Cv; b5 = BoxOf("k5");
                yield return Pull(Mid("k5"), to, EventModifiers.Alt);
                Check(b5.Rect.x == left2 + 6f, "with Alt held it does not snap", b5.Rect.ToString());

                // Alt too: boxes 5, 6 and 8 share box 7's top, so with snapping on y would snap back to it unlatched.
                yield return FreshCanvas(); c = Cv; b7 = BoxOf("k7");
                yield return Pull(Mid("k7"), Mid("k7") + new Vector2(-30f, 4f), EventModifiers.Shift | EventModifiers.Alt);
                Check(b7.Rect.y == was("k7").y && b7.Rect.x == was("k7").x - 30f, "a Shift drag of (-30, +4) leaves y unchanged", b7.Rect.ToString());

                yield return FreshCanvas(); c = Cv; b7 = BoxOf("k7");
                c.Snap = false;
                yield return Pull(Mid("k7"), Mid("k7") + new Vector2(7f, 3f));
                Check(b7.Rect.position == new Vector2(207, 103), "with Snap off it moves in 1-unit steps", b7.Rect.ToString());

                yield return FreshCanvas();
                GalleryEditor.Model.Limit = 50f;
                GalleryEditor.Nav.Go(GalleryEditor.CanvasItem);
                for (int i = 0; i < 3; i++) yield return null;
                c = Cv;
                var b1 = BoxOf("k1");
                yield return Pull(Mid("k1"), Mid("k1") + new Vector2(100f, 0f));
                Check(b1.Rect.x == 50f, "the page rebuilt with Limit = 50: box 1 dragged +100 ends at x 50", b1.Rect.ToString());

                // Resize.
                yield return FreshCanvas(); c = Cv;
                var b6 = BoxOf("k6");
                var br = StageOf(was("k6")).max;
                yield return Tap(Mid("k6"));
                Check(c.HandleRects.Count == 8, "one selection draws 8 handles", c.HandleRects.Count.ToString());
                yield return Pull(br, br + new Vector2(37f, 23f));
                Check(b6.Rect.position == was("k6").position && b6.Rect.width > 60f && b6.Rect.width % 5f == 0f && b6.Rect.height % 5f == 0f,
                    "dragging the bottom-right handle by (37, 23) gives a size on the 5-unit grid", b6.Rect.ToString());
                Check(History.NextUndo == "Resize", "...as one step named Resize", History.NextUndo);

                yield return FreshCanvas(); c = Cv; b6 = BoxOf("k6");
                yield return Tap(Mid("k6"));
                yield return Pull(br, br + new Vector2(37f, 23f), EventModifiers.Shift);
                Check(Mathf.Abs(b6.Rect.height - b6.Rect.width / 1.5f) <= 0.5f && b6.Rect.width > 60f && Keys(c) == "k6",
                    "with Shift the aspect is kept", b6.Rect.ToString());

                yield return FreshCanvas(); c = Cv; b6 = BoxOf("k6");
                yield return Tap(Mid("k6"));
                var right = StageOf(was("k6")); var edge = new Vector2(right.xMax, right.center.y);
                yield return Pull(edge, edge + new Vector2(22f, 0f));
                Check(b6.Rect.width == BoxOf("k12").Rect.width && b6.Rect.height == 40f, "it snaps to another box's width within 4", b6.Rect.ToString());

                yield return FreshCanvas(); c = Cv; b6 = BoxOf("k6");
                yield return Tap(Mid("k6"));
                yield return Pull(br, br + new Vector2(-100f, -100f));
                Check(b6.Rect == new Rect(110, 100, 10, 10), "it stops at 10", b6.Rect.ToString());
                yield return Tap(Mid("k7"), EventModifiers.Control);
                Check(c.HandleRects.Count == 0, "with two selected there are no handles", c.HandleRects.Count.ToString());

                // One step per gesture, and the events around it.
                yield return FreshCanvas(); c = Cv;
                yield return Tap(Mid("k1"));
                yield return Tap(Mid("k2"), EventModifiers.Control);
                yield return Tap(Mid("k3"), EventModifiers.Control);
                int changed = 0, committed = 0;
                c.Changed += () => changed++;
                c.Committed += () => committed++;
                var from = Mid("k2");
                Send(EventType.MouseDown, from); yield return null;
                for (int i = 1; i <= 40; i++) { Send(EventType.MouseDrag, from + new Vector2(3f * i, 0f)); yield return null; }
                Send(EventType.MouseUp, from + new Vector2(120f, 0f)); yield return null;
                var moved = new[] { BoxOf("k1").Rect, BoxOf("k2").Rect, BoxOf("k3").Rect };
                Check(changed >= 40 && committed == 1, "a 40-move drag of 3 boxes raises Changed every move and Committed once", changed + " / " + committed);
                Check(moved[0].x > was("k1").x && moved[1].x > was("k2").x && moved[2].x > was("k3").x && History.NextUndo == "Move",
                    "...and moves all 3", moved[0] + " " + moved[1] + " " + moved[2]);
                // C/K: Changed/Committed raised while History.Applying is true; c/k: after the apply.
                string trace = null;
                c.Changed += () => { if (trace != null) trace += History.Applying ? "C" : "c"; };
                c.Committed += () => { if (trace != null) trace += History.Applying ? "K" : "k"; };
                trace = "";
                Check(History.Undo() && !History.CanUndo && BoxOf("k1").Rect == was("k1") && BoxOf("k2").Rect == was("k2") && BoxOf("k3").Rect == was("k3"),
                    "one History.Undo restores all 3", BoxOf("k1").Rect + " " + BoxOf("k2").Rect + " " + BoxOf("k3").Rect);
                Check(trace == "CK", "...raising Changed, then Committed, once each while History.Applying is true, as a row's undo commits", trace);
                trace = null;
                yield return null;
                Check(Left(c, "k1") == was("k1").x && Left(c, "k2") == was("k2").x && Left(c, "k3") == was("k3").x,
                    "...and each drawn box's style.left is its restored x (the undo raised Changed)", Left(c, "k1") + " " + Left(c, "k2") + " " + Left(c, "k3"));
                Check(History.Redo() && BoxOf("k1").Rect == moved[0] && BoxOf("k2").Rect == moved[1] && BoxOf("k3").Rect == moved[2], "Redo moves them again");

                History.Clear(); committed = 0;
                var m5 = Mid("k5");
                Send(EventType.MouseDown, m5); yield return null;
                Send(EventType.MouseDrag, m5 + new Vector2(20f, 0f)); yield return null;
                Send(EventType.MouseDrag, m5); yield return null;
                Send(EventType.MouseUp, m5); yield return null;
                Check(!History.CanUndo && committed == 0 && BoxOf("k5").Rect == was("k5"), "a drag back to the start records nothing and does not commit",
                    History.NextUndo + " / " + committed);

                History.Clear(); committed = 0;
                Send(EventType.MouseDown, m5); yield return null;
                Send(EventType.MouseDrag, m5 + new Vector2(10f, 0f)); yield return null;
                Send(EventType.MouseDrag, m5 + new Vector2(20f, 0f)); yield return null;
                ReleasePointer(GalleryEditor.Surface.Root, c.Stage.LocalToWorld(m5 + new Vector2(20f, 0f))); yield return null;
                Check(History.NextUndo == "Move" && committed == 1 && Input.Gesture == null,
                    "a drag ended by ReleasePointer records one step and one Committed, and Input.Gesture is null after it",
                    History.NextUndo + " / " + committed + " / " + (Input.Gesture != null));
                Check(History.Undo() && !History.CanUndo && BoxOf("k5").Rect == was("k5"), "...one step, which an undo puts back");

                History.Clear(); committed = 0;
                Send(EventType.MouseDown, m5); yield return null;
                Send(EventType.MouseDrag, m5 + new Vector2(20f, 0f)); yield return null;
                var cancel = Input.Gesture;
                Check(cancel != null, "a drag in progress holds its cancel in Input.Gesture");
                if (cancel != null) cancel();
                Check(BoxOf("k5").Rect == was("k5") && Input.Gesture == null && !History.CanUndo, "running it puts the start back, clears Input.Gesture and records nothing");
                Send(EventType.MouseUp, m5 + new Vector2(20f, 0f)); yield return null;
                Check(!History.CanUndo && committed == 0, "...and the release after it records and commits nothing", committed.ToString());

                // Items replaced or removed under the canvas.
                yield return FreshCanvas(); c = Cv;
                yield return Tap(Mid("k3"));
                yield return Pull(Mid("k3"), Mid("k3") + new Vector2(30f, 0f));
                var old3 = BoxOf("k3");
                var replace = win.Body.Q<Button>("ed-replace");
                Check(replace != null, "the Canvas page has its Replace button");
                if (replace != null) Submit(replace);
                yield return null;
                var new3 = BoxOf("k3");
                Check(!ReferenceEquals(old3, new3) && c.Selection.Count == 1 && ReferenceEquals(c.Selection[0], new3),
                    "after Replace the same key is selected, now as the new object", Keys(c));
                Check(History.Undo() && new3.Rect == was("k3"), "...and an undo moves the new object", new3.Rect.ToString());

                yield return FreshCanvas(); c = Cv;
                yield return Tap(Mid("k2"));
                yield return Tap(Mid("k4"), EventModifiers.Control);
                sc = 0; c.SelectionChanged += () => sc++;
                int v0 = c.SelectionVersion;
                GalleryEditor.Model.Boxes.RemoveAll(b => b.Key == "k2");
                c.Refresh();
                Check(Keys(c) == "k4" && sc == 1 && c.SelectionVersion == v0 + 1, "removing box 2 prunes it with one SelectionChanged, and SelectionVersion grows by 1",
                    Keys(c) + " / " + sc + " / " + (c.SelectionVersion - v0));

                yield return FreshCanvas(); c = Cv;
                yield return Tap(Mid("k1"));
                yield return Tap(Mid("k3"), EventModifiers.Control);
                // Facet's own lines go to Unity's log through Host.Fallback; the editor host's go to the Gallery log.
                long logAt = LogLength();
                var unityLines = new List<string>();
                Application.LogCallback hear = (line, _, __) => { if (line.Contains("[Facet]")) unityLines.Add(line); };
                Application.logMessageReceived += hear;
                var m1 = Mid("k1");
                Send(EventType.MouseDown, m1); yield return null;
                Send(EventType.MouseDrag, m1 + new Vector2(10f, 0f)); yield return null;
                Send(EventType.MouseDrag, m1 + new Vector2(20f, 0f)); yield return null;
                GalleryEditor.Model.Boxes.RemoveAll(b => b.Key == "k3");
                c.Refresh();
                Send(EventType.MouseDrag, m1 + new Vector2(30f, 0f)); yield return null;
                Send(EventType.MouseUp, m1 + new Vector2(30f, 0f)); yield return null;
                Check(BoxOf("k1").Rect.x != was("k1").x && History.NextUndo == "Move", "removing a box mid-drag still records one step", BoxOf("k1").Rect.ToString());
                Check(History.Undo() && !History.CanUndo && BoxOf("k1").Rect == was("k1"), "...over the rest, which an undo puts back", BoxOf("k1").Rect.ToString());
                Application.logMessageReceived -= hear;
                var since = LogSince(logAt);
                Check(unityLines.Count == 0 && !since.Contains("[Gallery]"), "...and no error line is logged", string.Join(" | ", unityLines) + " / " + since.Trim());

                yield return FreshCanvas(); c = Cv;
                b7 = BoxOf("k7");
                yield return Pull(Mid("k7"), Mid("k7") + new Vector2(30f, 0f));
                GalleryEditor.Model.BoxDoc = new object(); History.Invalidate();
                bool did = History.Undo(); yield return null;
                Check(!did && b7.Rect.x == was("k7").x + 30f, "after the document is replaced, an undo leaves the boxes alone", did + " / " + b7.Rect);
                yield return Pull(Mid("k7"), Mid("k7") + new Vector2(30f, 0f));
                Check(History.Undo() && b7.Rect.x == was("k7").x + 30f, "a move on the new document is undone", b7.Rect.ToString());

                // Undo after the canvas shows another set, and after its page is rebuilt.
                yield return FreshCanvas(); c = Cv;
                var b3 = BoxOf("k3");
                yield return Pull(Mid("k3"), Mid("k3") + new Vector2(30f, 0f));
                GalleryEditor.Model.Tab = 1;
                c.Refresh(); yield return null;
                changed = 0; c.Changed += () => changed++;
                Check(History.Undo() && b3.Rect == was("k3"), "an undo after Items switched to another set still puts the box back, through Find", b3.Rect.ToString());
                Check(changed == 1, "...and the canvas raises Changed once", changed.ToString());
                GalleryEditor.Model.Tab = 0;

                yield return FreshCanvas(); c = Cv;
                b3 = BoxOf("k3");
                yield return Pull(Mid("k3"), Mid("k3") + new Vector2(30f, 0f));
                GalleryEditor.Nav.Go(GalleryEditor.CanvasItem);
                for (int i = 0; i < 3; i++) yield return null;
                var c2 = Cv;
                committed = 0; c2.Committed += () => committed++;
                Check(!ReferenceEquals(c, c2) && Left(c2, "k3") == was("k3").x + 30f, "the rebuilt page draws the moved box", Left(c2, "k3").ToString());
                History.Undo(); yield return null;
                Check(b3.Rect == was("k3") && Left(c2, "k3") == was("k3").x && committed == 1,
                    "after the page is rebuilt, an undo moves the box and the new page's drawn box, and the new canvas commits",
                    b3.Rect + " / " + Left(c2, "k3") + " / " + committed);

                // Off a panel only SetRect runs: no canvas re-reads or commits.
                yield return FreshCanvas(); c = Cv;
                b3 = BoxOf("k3");
                yield return Pull(Mid("k3"), Mid("k3") + new Vector2(30f, 0f));
                bool dragged = b3.Rect.x == was("k3").x + 30f;
                changed = 0; committed = 0; c.Changed += () => changed++; c.Committed += () => committed++;
                GalleryEditor.Nav.Go(GalleryEditor.UndoItem);
                for (int i = 0; i < 3; i++) yield return null;
                History.Undo(); yield return null;
                Check(dragged && c.Stage.panel == null && b3.Rect == was("k3") && changed == 0 && committed == 0,
                    "with the Canvas page switched away, an undo still puts the box back through SetRect, and raises no Changed or Committed",
                    dragged + " / " + (c.Stage.panel != null) + " / " + b3.Rect + " / " + changed + " / " + committed);

                // Chrome.
                yield return FreshCanvas(); c = Cv;
                yield return Tap(Mid("k1"));
                int one = c.Stage.hierarchy.childCount;
                c.SelectAll(); yield return null;
                Check(c.Selection.Count == 12 && c.Stage.hierarchy.childCount == one, "the stage's child count is the same with 1 and with 12 selected",
                    one + " / " + c.Stage.hierarchy.childCount);
                c.Clear();
                Rect? mq = null;
                yield return Pull(band0, band1, EventModifiers.None, 3, () => mq = c.Marquee);
                Check(mq != null && c.Marquee == null, "a marquee drag sets Marquee while it runs and null after", mq + " / " + c.Marquee);
                Check(c.BadgeText == "100%", "the zoom badge reads 100%", c.BadgeText);
                Check(Theme.Dark[Role.Guide].a > 0f, "Theme.Dark's Role.Guide colour has alpha > 0", Theme.Dark[Role.Guide].ToString());
            }
            finally { History.Clear(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }

            /* The host's cursor hook over a canvas (it declines Move, as a hook written before Move existed would), the
               chrome following its surface's theme, the inherited row options, and a canvas that records nothing. */
            var calls = new List<Cursors.Kind>();
            var h = Host.Create("CanvasCursor", o => { o.Log = m => Main.Log("[canvascursor] " + m); o.Cursor = k => { calls.Add(k); return k != Cursors.Kind.Move; }; });
            var s = new Surface(h, "FacetCanvasCursor", Layer.Window);
            try
            {
                var w = new Window(s.Root, "canvascursor", "Canvas cursor", new Rect(80, 80, 560, 420));
                var boxes = new List<GalleryBox> { new GalleryBox("a", "A", new Rect(40, 40, 60, 40)), new GalleryBox("b", "B", new Rect(160, 40, 60, 40)) };
                int commits = 0;
                bool shown = true, enabled = true;
                var cc = Controls.Canvas<GalleryBox>(w.Body, "Cursor canvas", o =>
                {
                    o.Items = () => boxes; o.Key = b => b.Key; o.GetRect = b => b.Rect; o.SetRect = (b, r) => b.Rect = r; o.Undoable = false;
                    o.VisibleWhen = () => shown; o.EnabledWhen = () => enabled;
                });
                cc.Committed += () => commits++;
                for (int i = 0; i < 3; i++) yield return null;
                Func<Vector2, Vector2> at = p => cc.Stage.LocalToWorld(p);
                var a = StageOf(boxes[0].Rect);
                PointerAt(EventType.MouseDown, at(a.center), s.Root); PointerAt(EventType.MouseUp, at(a.center), s.Root); yield return null;
                calls.Clear();
                MoveTo(s.Root, at(new Vector2(a.xMax, a.yMin))); yield return null;
                Func<Cursors.Kind?> last = () => calls.Count > 0 ? calls[calls.Count - 1] : (Cursors.Kind?)null;
                Check(last() == Cursors.Kind.ResizeCornerNESW, "the host cursor hook records ResizeCornerNESW over the top-right handle", string.Join(",", calls));
                MoveTo(s.Root, at(StageOf(boxes[1].Rect).center)); yield return null;
                Check(last() == Cursors.Kind.Move, "...Move over a box", string.Join(",", calls));
                Check(Equals(CursorField("_drawn"), true), "...which the hook declines, so Facet draws its own", CursorField("_drawn") + "");
                MoveTo(s.Root, w.Footer.worldBound.center); yield return null;
                Check(last() == Cursors.Kind.Default && Equals(CursorField("_drawn"), false), "...and Default after leaving", string.Join(",", calls));

                var t1 = Theme.FromAccent(new Color(.9f, .3f, .2f));
                s.Theme = t1; yield return null; yield return null;
                Check(Keys(cc) == "a" && Near(cc.DrawnAccent, t1[Role.Accent]), "replacing the surface's Theme repaints the canvas chrome in it, with no rebuild",
                    cc.DrawnAccent + " / " + t1[Role.Accent]);
                t1.Recolour(new Color(.2f, .5f, 1f)); yield return null; yield return null;
                Check(Near(cc.DrawnAccent, t1[Role.Accent]), "...and recolouring that theme repaints it again", cc.DrawnAccent + " / " + t1[Role.Accent]);

                shown = false; yield return new WaitForSeconds(0.3f);
                Check(cc.Root.style.display == DisplayStyle.None, "a canvas's VisibleWhen hides its pane", cc.Root.style.display.ToString());
                shown = true; enabled = false; yield return new WaitForSeconds(0.3f);
                var bc = at(StageOf(boxes[1].Rect).center);
                PointerAt(EventType.MouseDown, bc, s.Root); PointerAt(EventType.MouseUp, bc, s.Root); yield return null;
                Check(cc.Root.style.display != DisplayStyle.None && !cc.Stage.enabledInHierarchy && Keys(cc) == "a",
                    "a canvas's EnabledWhen disables its pane: a click on box B leaves the selection [A]", cc.Root.style.display + " / " + cc.Stage.enabledInHierarchy + " / " + Keys(cc));
                enabled = true; yield return new WaitForSeconds(0.3f);

                History.Clear();
                var bm = StageOf(boxes[1].Rect).center;
                PointerAt(EventType.MouseDown, at(bm), s.Root); yield return null;
                for (int i = 1; i <= 3; i++) { PointerAt(EventType.MouseDrag, at(bm + new Vector2(10f * i, 0f)), s.Root); yield return null; }
                PointerAt(EventType.MouseUp, at(bm + new Vector2(30f, 0f)), s.Root); yield return null;
                Check(boxes[1].Rect.x == 190f && !History.CanUndo && commits == 1, "a canvas with Undoable = false records no step, and still commits",
                    boxes[1].Rect + " / " + History.NextUndo + " / " + commits);

                // A canvas with no Id and a null label, in a window over the first: C/K inside the apply, c/k after it.
                var nw = new Window(s.Root, "canvasnull", "Unnamed canvas", new Rect(80, 80, 560, 420));
                var nb = new List<GalleryBox> { new GalleryBox("n", "N", new Rect(40, 40, 60, 40)) };
                var nc = Controls.Canvas<GalleryBox>(nw.Body, null, o => { o.Items = () => nb; o.Key = b => b.Key; o.GetRect = b => b.Rect; o.SetRect = (b, r) => b.Rect = r; });
                string own = null, other = null;
                nc.Changed += () => { if (own != null) own += History.Applying ? "C" : "c"; };
                nc.Committed += () => { if (own != null) own += History.Applying ? "K" : "k"; };
                cc.Changed += () => { if (other != null) other += History.Applying ? "C" : "c"; };
                cc.Committed += () => { if (other != null) other += History.Applying ? "K" : "k"; };
                for (int i = 0; i < 3; i++) yield return null;
                History.Clear();
                var nm = StageOf(nb[0].Rect).center;
                Func<Vector2, Vector2> nat = p => nc.Stage.LocalToWorld(p);
                PointerAt(EventType.MouseDown, nat(nm), s.Root); yield return null;
                for (int i = 1; i <= 3; i++) { PointerAt(EventType.MouseDrag, nat(nm + new Vector2(10f * i, 0f)), s.Root); yield return null; }
                PointerAt(EventType.MouseUp, nat(nm + new Vector2(30f, 0f)), s.Root); yield return null;
                bool nMoved = nb[0].Rect.x == 70f;
                own = ""; other = "";
                bool nUndone = History.Undo();
                Check(nMoved && nUndone && nb[0].Rect.x == 40f && own == "CK" && other == "c",
                    "a canvas with no Id and a null label raises Changed, then Committed, once each inside its undo's apply; the canvas under it, Changed once after",
                    nMoved + " / " + nUndone + " / " + nb[0].Rect + " / " + own + " / " + other);
                own = null; other = null;
            }
            finally { s.Dispose(); Cursors.Apply(Cursors.Kind.Default); History.Clear(); }
        }
    }
}
