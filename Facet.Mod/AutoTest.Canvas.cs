using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using EventType = UnityEngine.EventType;

namespace Facet.Mod
{
    // The Editor window's Canvas page: selection, marquee, move, resize, one step per gesture, items replaced underneath.
    sealed partial class AutoTest
    {
        /* Every stage point in these sections goes through here, from a rect in canvas units on canvas c (the Editor's
           by default): the canvas centres its items the first time its stage has a size, so the pan is not 0. */
        static Rect StageOf(Rect canvas, Canvas<GalleryBox> c = null)
        {
            c = c ?? Cv;
            return new Rect(c.CanvasToStage(canvas.position), canvas.size * c.Zoom);
        }

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

        static void Send(EventType t, Vector2 stage, EventModifiers m = EventModifiers.None, int button = 0)
            => PointerAt(t, Cv.Stage.LocalToWorld(stage), GalleryEditor.Surface.Root, button, m);

        // Down and up in one frame, as a still click.
        static IEnumerator Tap(Vector2 stage, EventModifiers m = EventModifiers.None)
        {
            Send(EventType.MouseDown, stage, m);
            Send(EventType.MouseUp, stage, m); yield return null;
        }

        // A drag in `steps` moves, one a frame; atEnd runs after the last move, before the release.
        static IEnumerator Pull(Vector2 from, Vector2 to, EventModifiers m = EventModifiers.None, int steps = 6, Action atEnd = null, int button = 0)
        {
            Send(EventType.MouseDown, from, m, button); yield return null;
            for (int i = 1; i <= steps; i++) { Send(EventType.MouseDrag, Vector2.Lerp(from, to, i / (float)steps), m, button); yield return null; }
            if (atEnd != null) atEnd();
            Send(EventType.MouseUp, to, m, button); yield return null;
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
            try
            {
                yield return FreshCanvas();
                var c = Cv;
                Check(c != null && c.Stage.panel != null, "the Editor window's Canvas page builds a canvas on the panel");
                if (c == null) yield break;
                var empty = StageOf(new Rect(95, 80, 0, 0)).position;   // between boxes 1, 2, 5 and 6
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
                yield return Tap(Mid("k5"));
                Send(EventType.MouseDown, band0); yield return null;
                Send(EventType.MouseDrag, (band0 + band1) / 2f); yield return null;
                Send(EventType.MouseDrag, band1); yield return null;
                string inBand = Keys(c);
                var bandCancel = Input.Gesture;
                if (bandCancel != null) bandCancel();
                Check(inBand == "k1,k2" && Keys(c) == "k5" && c.Marquee == null, "cancelling a band puts back the selection it started from", inBand + " -> " + Keys(c));
                Send(EventType.MouseUp, band1); yield return null;
                Check(Keys(c) == "k5", "...and the release after it leaves that selection", Keys(c));
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
                try
                {
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
                }
                finally { Application.logMessageReceived -= hear; }
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
                var a = StageOf(boxes[0].Rect, cc);
                PointerAt(EventType.MouseDown, at(a.center), s.Root); PointerAt(EventType.MouseUp, at(a.center), s.Root); yield return null;
                calls.Clear();
                MoveTo(s.Root, at(new Vector2(a.xMax, a.yMin))); yield return null;
                Func<Cursors.Kind?> last = () => calls.Count > 0 ? calls[calls.Count - 1] : (Cursors.Kind?)null;
                Check(last() == Cursors.Kind.ResizeCornerNESW, "the host cursor hook records ResizeCornerNESW over the top-right handle", string.Join(",", calls));
                MoveTo(s.Root, at(StageOf(boxes[1].Rect, cc).center)); yield return null;
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
                var bc = at(StageOf(boxes[1].Rect, cc).center);
                PointerAt(EventType.MouseDown, bc, s.Root); PointerAt(EventType.MouseUp, bc, s.Root); yield return null;
                Check(cc.Root.style.display != DisplayStyle.None && !cc.Stage.enabledInHierarchy && Keys(cc) == "a",
                    "a canvas's EnabledWhen disables its pane: a click on box B leaves the selection [A]", cc.Root.style.display + " / " + cc.Stage.enabledInHierarchy + " / " + Keys(cc));
                enabled = true; yield return new WaitForSeconds(0.3f);

                History.Clear();
                var bm = StageOf(boxes[1].Rect, cc).center;
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
                var nm = StageOf(nb[0].Rect, nc).center;
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

        // ---- the view, the right and middle buttons, keys, Escape and double-click

        static bool NearV(Vector2 a, Vector2 b, float within) => (a - b).magnitude <= within;

        // The panel's own wheel event at a stage point: delta as InputForUI sends it (3 per notch, y down).
        static void WheelOn(Vector2 stage, Vector2 delta, EventModifiers m = EventModifiers.None)
        {
            var ev = new Event { type = EventType.ScrollWheel, delta = delta, mousePosition = Cv.Stage.LocalToWorld(stage), modifiers = m };
            using (var e = WheelEvent.GetPooled(ev)) GalleryEditor.Surface.Root.panel.visualTree.SendEvent(e);
        }

        // A stage point in screen pixels (origin bottom-left), as the host passes Input.mousePosition.
        static Vector2 ScreenOf(Vector2 stage)
        {
            var root = GalleryEditor.Surface.Root.panel.visualTree;
            float k = root.layout.width / Screen.width;
            var p = Cv.Stage.LocalToWorld(stage);
            return new Vector2(p.x / k, Screen.height - p.y / k);
        }

        static int CountLabels(string text)
        {
            int n = 0;
            foreach (var d in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (d.rootVisualElement != null)
                    foreach (var l in d.rootVisualElement.Query<Label>().ToList()) if (l.text == text && l.panel != null) n++;
            return n;
        }

        static Rect Union(List<GalleryBox> boxes)
        {
            var u = boxes[0].Rect;
            foreach (var b in boxes) u = Rect.MinMaxRect(Mathf.Min(u.xMin, b.Rect.xMin), Mathf.Min(u.yMin, b.Rect.yMin), Mathf.Max(u.xMax, b.Rect.xMax), Mathf.Max(u.yMax, b.Rect.yMax));
            return u;
        }

        IEnumerator SectionCanvasView()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            GalleryEditor.HostKeys = false;   // this section drives the canvas's keys itself
            try
            {
                yield return FreshCanvas();
                GalleryEditor.Surface.BringToFront(); yield return null;
                yield return CanvasWheel();
                yield return FreshCanvas();
                yield return CanvasRight();
                yield return FreshCanvas();
                yield return CanvasViewCalls();
                yield return FreshCanvas();
                yield return CanvasKeys(win);
                yield return FreshCanvas();
                yield return CanvasEscape();
                yield return FreshCanvas();
                yield return CanvasDoubleClick();
            }
            finally { GalleryEditor.HostKeys = true; Popup.CloseAll(); History.Clear(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }
        }

        IEnumerator CanvasWheel()
        {
            var c = Cv;
            Check(c.Zoom == 1f && c.Pan != Vector2.zero, "the first time the stage has a size the canvas resets its view: zoom 1, items centred", c.Zoom + " / " + c.Pan);
            // A tall spacer under the canvas, so the page around the stage could scroll and "it does not" means something.
            var spacer = new VisualElement();
            spacer.style.height = 1500f; spacer.style.flexShrink = 0f;
            GalleryEditor.Page.Add(spacer);
            try
            {
                yield return null; yield return null;
                var sv = c.Stage.GetFirstAncestorOfType<ScrollView>();
                Check(sv != null && sv.verticalScroller.highValue > 0f, "the page around the stage can scroll", sv == null ? "no ScrollView" : sv.verticalScroller.highValue.ToString());
                Func<float> off = () => sv == null ? 0f : sv.scrollOffset.y;
                var at = StageOf(BoxOf("k6").Rect).center;
                int vc = 0; c.ViewChanged += () => vc++;

                float o0 = off(); var p0 = c.Pan;
                WheelOn(at, new Vector2(0f, 3f)); yield return null; yield return null;
                Check(Mathf.Abs(c.Pan.y - p0.y) == 20f && c.Pan.x == p0.x && off() == o0 && vc == 1,
                    "one notch of the panel's wheel event over the stage pans 20 px on y, once, and the page does not scroll", (c.Pan - p0) + " / " + (off() - o0) + " / " + vc);
                using (Motion.Hold(1f, true))
                {
                    p0 = c.Pan; o0 = off();
                    WheelOn(at, new Vector2(0f, 3f));
                    for (int i = 0; i < 20; i++) yield return null;
                    Check(Mathf.Abs(c.Pan.y - p0.y) == 20f && off() == o0, "...and with smooth scrolling on, the page's glide leaves it to the canvas", (c.Pan - p0) + " / " + (off() - o0));
                }
                yield return null; yield return null;
                p0 = c.Pan;
                Input.TickWheel(ScreenOf(at), 1f); yield return null;
                Check(Mathf.Abs(c.Pan.y - p0.y) == 20f && c.Pan.x == p0.x && off() == o0, "the polled wheel (Input.TickWheel, delta 1) over the stage pans 20 px on y too",
                    (c.Pan - p0) + " / " + (off() - o0));
                yield return null; yield return null;
                p0 = c.Pan;
                WheelOn(at, new Vector2(0f, 3f));
                Input.TickWheel(ScreenOf(at), -1f); yield return null;
                Check(Mathf.Abs(c.Pan.y - p0.y) == 20f, "both in one frame pan once", (c.Pan - p0).ToString());
                yield return null; yield return null;
                p0 = c.Pan;
                WheelOn(at, new Vector2(0f, 3f)); yield return null;
                Input.TickWheel(ScreenOf(at), -1f); yield return null;
                Check(Mathf.Abs(c.Pan.y - p0.y) == 20f, "a notch as the panel's event, then polled on the next frame, pans once", (c.Pan - p0).ToString());

                // Covered stage: the polled wheel goes to the canvas only when the stage is what the pointer is on.
                yield return null; yield return null;
                var cover = new VisualElement { name = "test-cover" };
                var wat = c.Stage.LocalToWorld(at);
                cover.style.position = Position.Absolute;
                cover.style.left = wat.x - 20f; cover.style.top = wat.y - 20f; cover.style.width = 40f; cover.style.height = 40f;
                GalleryEditor.Surface.Root.Add(cover);
                yield return null; yield return null;
                p0 = c.Pan;
                Input.TickWheel(ScreenOf(at), 1f); yield return null;
                Check(c.Pan == p0, "the polled wheel over an element of the window that covers the stage leaves the canvas alone", (c.Pan - p0).ToString());
                cover.RemoveFromHierarchy();
                yield return null; yield return null;
                var pop = Popup.Context(c.Stage, at, new List<MenuItem> { new MenuItem { Text = "Wheel cover" } });
                yield return null; yield return null;
                var row = LiveLabel("Wheel cover");
                p0 = c.Pan;
                var popAt = row == null ? at : (Vector2)c.Stage.WorldToLocal(row.worldBound.center);
                bool overStage = row != null && c.Stage.ContainsPoint(popAt);
                if (overStage) { Input.TickWheel(ScreenOf(popAt), 1f); yield return null; }
                Check(overStage && c.Pan == p0, "...nor over a popup that covers the stage", overStage + " / " + (c.Pan - p0));
                pop?.Close(); Popup.CloseAll();
                yield return null; yield return null;
                yield return null; yield return null;
                p0 = c.Pan;
                WheelOn(at, new Vector2(0f, 3f), EventModifiers.Shift); yield return null;
                Check(Mathf.Abs(c.Pan.x - p0.x) == 20f && c.Pan.y == p0.y, "with Shift it pans on x", (c.Pan - p0).ToString());
                p0 = c.Pan;
                WheelOn(at, new Vector2(3f, 3f)); yield return null;
                Check(Mathf.Abs(c.Pan.x - p0.x) == 20f && Mathf.Abs(c.Pan.y - p0.y) == 20f, "a wheel with both axes (a trackpad) pans on both", (c.Pan - p0).ToString());

                var under = c.StageToCanvas(at);
                WheelOn(at, new Vector2(0f, -3f), EventModifiers.Control); yield return null;
                Check(Mathf.Abs(c.Zoom - 1.1f) < 1e-4f && NearV(c.CanvasToStage(under), at, 0.5f), "Ctrl+wheel zooms by 0.1 and keeps the canvas point under the pointer within 0.5 px",
                    c.Zoom + " / " + c.CanvasToStage(under) + " vs " + at);
                for (int i = 0; i < 40; i++) WheelOn(at, new Vector2(0f, -3f), EventModifiers.Control);
                yield return null;
                float top = c.Zoom;
                for (int i = 0; i < 60; i++) WheelOn(at, new Vector2(0f, 3f), EventModifiers.Control);
                yield return null;
                Check(top == 4f && c.Zoom == 0.3f, "zoom stops at 4 and at 0.3", top + " / " + c.Zoom);
                c.ResetView(); yield return null;

                // The middle button pans at once.
                var keys = Keys(c); History.Clear(); p0 = c.Pan;
                yield return Pull(at, at + new Vector2(40f, 10f), EventModifiers.None, 4, null, 2);
                Check(NearV(c.Pan - p0, new Vector2(40f, 10f), 1e-3f) && Keys(c) == keys && !History.CanUndo, "a middle drag of (40, 10) pans by exactly that, selects and records nothing",
                    (c.Pan - p0) + " / " + Keys(c));
            }
            finally { spacer.RemoveFromHierarchy(); }
        }

        IEnumerator CanvasRight()
        {
            var c = Cv; var m = GalleryEditor.Model;
            c.Clear();
            var a4 = Mid("k4");
            yield return Pull(a4, a4 + new Vector2(4f, 0f), EventModifiers.None, 2, null, 1);
            yield return null;
            int menus = CountLabels("Bring forward");
            Check(menus == 1 && m.MenuFor == "k4" && Keys(c) == "k4", "a right press on box 4 moved 4 px and released opens one popup of Menu(box 4), and selects box 4",
                menus + " / " + m.MenuFor + " / " + Keys(c));
            // Picking a row of the canvas's own menu keeps the canvas focused.
            int zs = m.ZSteps;
            var pick = LiveLabel("Bring forward");
            if (pick != null) { PressAt(pick.worldBound.center, pick); yield return null; yield return null; }
            if (LiveLabel("Bring forward") != null) { Release(LiveLabel("Bring forward").parent); yield return null; yield return null; }
            Check(m.ZSteps == zs + 1 && CountLabels("Bring forward") == 0 && c.Focused, "picking Bring forward in the canvas's menu runs it and leaves the canvas focused",
                (m.ZSteps - zs) + " / " + c.Focused);
            bool esc = Input.HandleEscape();
            Check(esc && Keys(c) == "" && GalleryEditor.Surface.Visible, "...so the next Escape clears the selection and the window stays open",
                esc + " / " + Keys(c) + " / " + GalleryEditor.Surface.Visible);
            if (!GalleryEditor.Surface.Visible) { GalleryEditor.Open(); GalleryEditor.Surface.BringToFront(); yield return null; yield return null; c = Cv; }
            Popup.CloseAll(); yield return null;

            var p0 = c.Pan; m.MenuFor = null;
            yield return Pull(a4, a4 + new Vector2(20f, 0f), EventModifiers.None, 4, null, 1);
            yield return null;
            Check(NearV(c.Pan - p0, new Vector2(20f, 0f), 1e-3f) && CountLabels("Bring forward") == 0 && m.MenuFor == null,
                "a right drag of 20 px pans by 20 and opens no menu", (c.Pan - p0) + " / " + m.MenuFor);

            m.NoMenu = true; c.Clear();
            var a5 = Mid("k5");
            yield return Pull(a5, a5, EventModifiers.None, 1, null, 1);
            yield return null;
            Check(m.MenuFor == "k5" && CountLabels("Bring forward") == 0 && CountLabels("Fit") == 0 && Keys(c) == "k5", "a Menu returning null opens nothing (the box is still selected)",
                m.MenuFor + " / " + Keys(c));
            m.NoMenu = false; Popup.CloseAll();
        }

        IEnumerator CanvasViewCalls()
        {
            var c = Cv; var m = GalleryEditor.Model;
            int vc = 0; c.ViewChanged += () => vc++;
            c.ZoomBy(0.5f); vc = 0;
            c.Fit();
            var u = Union(m.Boxes);
            float z, px, py;
            CanvasMath.Fit(new Box(u.x, u.y, u.width, u.height), c.Stage.layout.width, c.Stage.layout.height, 40f, out z, out px, out py);
            Check(Mathf.Abs(c.Zoom - z) < 1e-3f && NearV(c.Pan, new Vector2(px, py), 1e-3f) && vc == 1, "Fit gives CanvasMath.Fit's zoom and pan for the items' union, with one ViewChanged",
                c.Zoom + " vs " + z + " / " + c.Pan + " vs " + px + "," + py + " / " + vc);
            vc = 0; c.Centre();
            Check(vc == 0, "Centre at the fitted view changes nothing and raises nothing", vc.ToString());
            c.Zoom = 1.7f; vc = 0;
            var p = new Vector2(123.4f, -56.7f);
            Check(NearV(c.StageToCanvas(c.CanvasToStage(p)), p, 1e-3f), "StageToCanvas(CanvasToStage(p)) is p", c.StageToCanvas(c.CanvasToStage(p)).ToString());
            var mid = c.ViewCentre; c.Zoom = 2f;
            Check(NearV(c.ViewCentre, mid, 1e-3f) && vc == 1, "setting Zoom keeps the view centre, with one ViewChanged", c.ViewCentre + " vs " + mid + " / " + vc);
            vc = 0; c.ResetView();
            Check(c.Zoom == 1f && vc == 1, "ResetView: zoom 1, one ViewChanged", c.Zoom + " / " + vc);
            Check(c.BadgeText == "100%", "...and the badge reads 100%", c.BadgeText);
            var badge = c.BadgeText;
            c.Pan = c.Pan + new Vector2(7f, 3f);
            Check(ReferenceEquals(badge, c.BadgeText), "a pan at the same zoom keeps the badge's text (no new string a frame)");
            vc = 0; c.ZoomBy(0.1f);
            Check(vc == 1, "ZoomBy raises one ViewChanged", vc.ToString());
            vc = 0; c.Pan = c.Pan + new Vector2(30f, -10f);
            Check(vc == 1, "setting Pan raises one ViewChanged", vc.ToString());
            var before = c.Pan; vc = 0; c.Centre();
            Check(c.Pan != before && vc == 1, "a Centre that moves the view raises one ViewChanged", (c.Pan - before) + " / " + vc);
            Check(c.HitTest(Mid("k3")) == BoxOf("k3") && c.HitTest(StageOf(new Rect(95, 80, 0, 0)).position) == null, "HitTest finds box 3 at its middle, and nothing between boxes");
            m.Boxes.Clear(); c.Refresh(); c.ZoomBy(1f);
            c.Fit();
            Check(c.Zoom == 1f && !float.IsNaN(c.Pan.x) && !float.IsInfinity(c.Pan.x) && !float.IsNaN(c.Pan.y) && !float.IsInfinity(c.Pan.y), "Fit with no items gives zoom 1 and a finite pan",
                c.Zoom + " / " + c.Pan);
            yield return null;
        }

        IEnumerator CanvasKeys(Window win)
        {
            var c = Cv; var m = GalleryEditor.Model;
            var down = new HashSet<KeyCode>(); var held = new HashSet<KeyCode>();
            Func<KeyCode, bool> D = k => down.Contains(k), H = k => held.Contains(k) || down.Contains(k);
            int committed = 0; c.Committed += () => committed++;
            Func<string, float> X = k => BoxOf(k).Rect.x;
            float x6 = X("k6");

            yield return Tap(Mid("k6"));
            Check(c.Focused && Keys(c) == "k6", "a press on the stage focuses the canvas", c.Focused + " / " + Keys(c));
            History.Clear(); committed = 0;
            down.Add(KeyCode.RightArrow);
            bool acted = c.PollKeys(D, H); down.Clear(); yield return null;
            c.PollKeys(D, H);
            Check(acted && X("k6") == x6 + 1f && History.NextUndo == "Nudge" && committed == 1, "focused, Right nudges box 6 by 1 as a step Nudge, committed on release",
                acted + " / " + X("k6") + " / " + History.NextUndo + " / " + committed);
            float before = X("k6");
            down.Add(KeyCode.RightArrow); c.PollKeys(D, H); down.Clear(); held.Add(KeyCode.RightArrow);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.5f) { yield return null; c.PollKeys(D, H); }
            held.Clear(); c.PollKeys(D, H);
            float more = X("k6") - before - 1f;
            Check(more >= 2f && more <= 4f, "held for 0.5 s it repeats 3 +- 1 more times", more.ToString());
            down.Add(KeyCode.RightArrow); c.PollKeys(D, H); down.Clear(); c.PollKeys(D, H);
            float xEnd = X("k6");
            Check(History.Undo() && X("k6") == x6 && !History.CanUndo, "the nudges, released and pressed again within 0.9 s, are one step", X("k6") + " / " + History.NextUndo);
            Check(History.Redo() && X("k6") == xEnd, "redo puts box 6 where the run's last nudge left it", X("k6") + " / " + xEnd);
            History.Undo();
            committed = 0;
            c.Nudge(1f, 0f); c.PollKeys(D, H);
            Check(committed == 1, "Nudge(1, 0), then a key poll with no key held, commits once", committed.ToString());
            Check(History.Undo() && X("k6") == x6 && !History.CanUndo, "a nudge after an undo is a step of its own", X("k6") + " / " + History.NextUndo);

            History.Clear();
            down.Add(KeyCode.RightArrow); c.PollKeys(D, H); down.Clear(); c.PollKeys(D, H);
            yield return Tap(Mid("k7"));
            float x7 = X("k7");
            down.Add(KeyCode.RightArrow); c.PollKeys(D, H); down.Clear(); c.PollKeys(D, H);
            Check(History.Undo() && X("k7") == x7 && X("k6") == x6 + 1f && History.CanUndo, "a nudge on a new selection is a step of its own",
                X("k7") + " / " + X("k6") + " / " + History.NextUndo);
            History.Clear();
            c.Clear(); committed = 0;
            down.Add(KeyCode.RightArrow); bool none = c.PollKeys(D, H); down.Clear(); c.PollKeys(D, H);
            Check(c.Focused && !none && committed == 0, "focused with nothing selected, an arrow does nothing and returns false, so the host's menus get it",
                c.Focused + " / " + none + " / " + committed);

            // Delete and z-order, each its own step.
            yield return Tap(Mid("k1"));
            down.Add(KeyCode.Delete); c.PollKeys(D, H); down.Clear();
            Check(m.Deletes == 1 && BoxOf("k1") == null && History.NextUndo == "Delete" && Keys(c) == "", "Delete calls Delete with the selection, as a step Delete",
                m.Deletes + " / " + History.NextUndo + " / " + Keys(c));
            yield return new WaitForSeconds(0.1f);
            yield return Tap(Mid("k2"));
            down.Add(KeyCode.Backspace); c.PollKeys(D, H); down.Clear();
            Check(m.Deletes == 2 && BoxOf("k2") == null, "...and so does Backspace", m.Deletes.ToString());
            int changes = 0; Action onChange = () => changes++;
            c.Changed += onChange;
            bool undo2 = History.Undo(); int afterOne = changes;
            bool k2back = BoxOf("k2") != null && BoxOf("k1") == null && History.CanUndo;
            bool undo1 = History.Undo();
            c.Changed -= onChange;
            Check(undo2 && k2back && undo1 && BoxOf("k1") != null && !History.CanUndo, "two deletes 0.1 s apart are two undo steps");
            Check(afterOne == 1 && changes == 2 && c.Content.Q("ed-box-k1") != null && c.Content.Q("ed-box-k2") != null,
                "...each undo raises the canvas's Changed once, and the page draws both boxes again", afterOne + " / " + changes);
            yield return null;
            History.Clear();
            yield return Tap(Mid("k1"));
            down.Add(KeyCode.RightBracket); c.PollKeys(D, H); down.Clear();
            Check(m.ZSteps == 1 && m.Boxes[0].Key == "k2" && m.Boxes[1].Key == "k1" && History.NextUndo == "Bring forward", "] calls ZOrder with the StepZ order, as a step Bring forward",
                m.Boxes[0].Key + "," + m.Boxes[1].Key + " / " + History.NextUndo);
            down.Add(KeyCode.LeftBracket); c.PollKeys(D, H); down.Clear();
            Check(m.ZSteps == 2 && m.Boxes[0].Key == "k1" && History.NextUndo == "Send backward", "[ sends it back, as a step Send backward", m.Boxes[0].Key + " / " + History.NextUndo);
            History.Clear();

            // Chords are the menus': a Ctrl+] item runs and StepZ does not.
            int z = m.ZSteps;
            down.Add(KeyCode.RightBracket); held.Add(KeyCode.LeftControl);
            GalleryEditor.PollKeys(D, H);
            down.Clear(); held.Clear();
            Check(m.Fronts == 1 && m.ZSteps == z, "with Ctrl held, ] runs the Ctrl+] menu item and not the canvas's StepZ", m.Fronts + " / " + (m.ZSteps - z));

            // The host's order: the canvas first, the menus only when it did nothing.
            yield return Tap(Mid("k3"));
            History.Clear(); int d0 = m.Deletes, md0 = m.MenuDeletes;
            down.Add(KeyCode.Delete); bool canvasTook = GalleryEditor.PollKeys(D, H); down.Clear();
            Check(canvasTook && m.Deletes == d0 + 1 && m.MenuDeletes == md0 && History.Undo() && !History.CanUndo,
                "one Delete press in the host order, with Edit > Delete on the same key, is the canvas's alone: deletes once as one step",
                canvasTook + " / " + (m.Deletes - d0) + " / " + (m.MenuDeletes - md0));
            yield return null;

            // Hover alone: zoom keys only.
            var foot = win.Footer.worldBound;
            PressAt(new Vector2(foot.xMax - 40f, foot.center.y), win.Root); yield return null;
            Check(!c.Focused, "a press elsewhere in the window unfocuses the canvas");
            MoveTo(GalleryEditor.Surface.Root, c.Stage.LocalToWorld(Mid("k6"))); yield return null;
            Check(c.Hovered, "the pointer over the stage hovers it");
            yield return Tap(Mid("k6")); PressAt(new Vector2(foot.xMax - 40f, foot.center.y), win.Root); yield return null;
            MoveTo(GalleryEditor.Surface.Root, c.Stage.LocalToWorld(Mid("k6"))); yield return null;
            float z0 = c.Zoom; float x6b = X("k6");
            down.Add(KeyCode.Equals); bool zoomed = c.PollKeys(D, H); down.Clear();
            down.Add(KeyCode.RightArrow); bool nudged = c.PollKeys(D, H); down.Clear(); c.PollKeys(D, H);
            Check(zoomed && Mathf.Abs(c.Zoom - z0 - 0.1f) < 1e-4f && !nudged && X("k6") == x6b, "only hovered, + zooms and the arrows do nothing",
                zoomed + " / " + c.Zoom + " / " + nudged + " / " + X("k6"));
            down.Add(KeyCode.Minus); c.PollKeys(D, H); down.Clear();
            down.Add(KeyCode.Alpha0); c.PollKeys(D, H); down.Clear();
            Check(c.Zoom == 1f, "- and 0 zoom back", c.Zoom.ToString());
            MoveTo(GalleryEditor.Surface.Root, foot.center); yield return null;
            z0 = c.Zoom;
            down.Add(KeyCode.Equals); bool any = c.PollKeys(D, H); down.Clear();
            down.Add(KeyCode.RightArrow); any |= c.PollKeys(D, H); down.Clear(); c.PollKeys(D, H);
            Check(!c.Hovered && !any && c.Zoom == z0 && X("k6") == x6b, "neither focused nor hovered, the keys do nothing", c.Hovered + " / " + any);

            // Mid-drag the keys and the menus' shortcuts do nothing.
            yield return Tap(Mid("k5"));
            int copies = m.Copies; d0 = m.Deletes;
            var m5 = Mid("k5");
            Send(EventType.MouseDown, m5); yield return null;
            Send(EventType.MouseDrag, m5 + new Vector2(20f, 0f)); yield return null;
            down.Add(KeyCode.Delete); bool del = c.PollKeys(D, H); down.Clear();
            bool cp = Menus.HandleKeys(GalleryEditor.Bar.Root, GalleryEditor.MenuItems, k => k == KeyCode.C, true, false, false);
            Check(!del && !cp && m.Deletes == d0 && m.Copies == copies, "mid-drag, PollKeys with Delete and Menus.HandleKeys with Ctrl+C do nothing", del + " / " + cp);
            Send(EventType.MouseUp, m5 + new Vector2(20f, 0f)); yield return null;
        }

        IEnumerator CanvasEscape()
        {
            var c = Cv;
            var was5 = BoxOf("k5").Rect;
            yield return Tap(Mid("k6"));
            yield return null;
            bool took = Input.HandleEscape();
            Check(took && Keys(c) == "" && GalleryEditor.Surface.Visible, "focused with a selection, Escape clears it and the window stays open", took + " / " + Keys(c));
            yield return null;
            Input.HandleEscape();
            Check(!GalleryEditor.Surface.Visible, "...and the next frame's Escape hides the window");
            GalleryEditor.Open(); GalleryEditor.Surface.BringToFront(); yield return null; yield return null;

            c = Cv; History.Clear();
            yield return Tap(Mid("k5"));
            var m5 = Mid("k5");
            Send(EventType.MouseDown, m5); yield return null;
            Send(EventType.MouseDrag, m5 + new Vector2(20f, 0f)); yield return null;
            bool moved = BoxOf("k5").Rect != was5;
            took = Input.HandleEscape();
            Check(moved && took && BoxOf("k5").Rect == was5 && !History.CanUndo && Keys(c) == "k5" && Input.Gesture == null,
                "Escape mid-drag puts the box back, records nothing and keeps the selection", moved + " / " + BoxOf("k5").Rect + " / " + Keys(c));
            Send(EventType.MouseUp, m5 + new Vector2(20f, 0f)); yield return null;
            Check(!History.CanUndo && GalleryEditor.Surface.Visible, "...and the release after it records nothing");

            // Another Facet copy's host may ask first: its Escape goes through the shared entries.
            History.Clear(); yield return null;
            Send(EventType.MouseDown, m5); yield return null;
            Send(EventType.MouseDrag, m5 + new Vector2(20f, 0f)); yield return null;
            moved = BoxOf("k5").Rect != was5;
            took = Coord.TakeEscape(Input.Entries, null);
            Check(moved && took && BoxOf("k5").Rect == was5 && Keys(c) == "k5" && Input.Gesture == null && GalleryEditor.Surface.Visible,
                "Escape through the shared entries (another copy's host) mid-drag puts the box back and keeps the selection", moved + " / " + BoxOf("k5").Rect + " / " + Keys(c));
            Send(EventType.MouseUp, m5 + new Vector2(20f, 0f)); yield return null;
            Check(!History.CanUndo, "...and records nothing");

            // A 0.1 host knows no drag slot: it calls the top entry's Escape (slot 1) alone.
            History.Clear(); yield return null;
            Send(EventType.MouseDown, m5); yield return null;
            Send(EventType.MouseDrag, m5 + new Vector2(20f, 0f)); yield return null;
            moved = BoxOf("k5").Rect != was5;
            Func<bool> slot1 = null;
            foreach (var en in Input.Entries)
                if (en.Length > 6 && en[6] as string == typeof(Input).Assembly.FullName) slot1 = en[1] as Func<bool>;
            took = slot1 != null && slot1();
            Check(moved && took && BoxOf("k5").Rect == was5 && Keys(c) == "k5" && Input.Gesture == null && GalleryEditor.Surface.Visible,
                "this copy's Escape slot, called alone mid-drag (as a 0.1 host does), cancels the drag first", moved + " / " + took + " / " + BoxOf("k5").Rect);
            Send(EventType.MouseUp, m5 + new Vector2(20f, 0f)); yield return null;
            Check(!History.CanUndo, "...and records nothing");
        }

        IEnumerator CanvasDoubleClick()
        {
            var c = Cv;
            var hits = new List<string>();
            c.Activated += b => hits.Add(b.Key);
            yield return Tap(Mid("k6"));
            yield return new WaitForSeconds(0.2f);
            yield return Tap(Mid("k6"));
            Check(hits.Count == 1 && hits[0] == "k6", "two clicks on box 6 0.2 s apart raise Activated once", string.Join(",", hits));
            yield return new WaitForSeconds(0.5f);
            hits.Clear();
            yield return Tap(Mid("k7"));
            yield return new WaitForSeconds(0.4f);
            yield return Tap(Mid("k7"));
            Check(hits.Count == 0, "0.4 s apart, none", string.Join(",", hits));

            // A pressed item that is then deleted is not kept alive by the canvas.
            yield return new WaitForSeconds(0.5f);
            yield return Tap(Mid("k8"));
            var weak = WeakBox("k8");
            c.DeleteSelection(); History.Clear(); yield return null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(BoxOf("k8") == null && !weak.IsAlive, "a box pressed once, then deleted, can be collected", "alive " + weak.IsAlive);
        }

        static WeakReference WeakBox(string key) => new WeakReference(BoxOf(key));

        // ---- the 108-box budget

        sealed class DragRun { public double Mean, Worst; public float FrameMean, IdleMean; public long Grew, Junk; public float Moved; }

        // 120 one-pixel moves, one a frame, each dispatched move timed with the page's redraw on Changed.
        IEnumerator TimedDrag(Canvas<GalleryBox> c, string key, DragRun r)
        {
            var box = BoxOf(key);
            float x0 = box.Rect.x;
            float idle = 0f;
            for (int i = 0; i < 60; i++) { yield return null; idle += Time.unscaledDeltaTime; }
            r.IdleMean = idle / 60f * 1000f;
            var p = StageOf(box.Rect, c).center;
            Send(EventType.MouseDown, p); yield return null;
            for (int i = 1; i <= 4; i++) { Send(EventType.MouseDrag, p + new Vector2(2f * i, 0f)); yield return null; }
            p += new Vector2(8f, 0f);
            var sw = new Stopwatch();
            double sum = 0, worst = 0; float frames = 0f;
            for (int i = 1; i <= 120; i++)
            {
                sw.Reset(); sw.Start();
                Send(EventType.MouseDrag, p + new Vector2(i, 0f));
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                sum += ms; if (ms > worst) worst = ms;
                yield return null;
                frames += Time.unscaledDeltaTime;
            }
            r.Mean = sum / 120.0; r.Worst = worst; r.FrameMean = frames / 120f * 1000f;
            r.Moved = box.Rect.x - x0;

            // Bytes per move: the canvas's own move (snap, SetRect per item, Changed with the page's redraw, the chrome), heap growth with the collector paused.
            var mi = typeof(Canvas<GalleryBox>).GetMethod("DoMove", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null)
            {
                var move = (Action<Vector2, bool, bool>)Delegate.CreateDelegate(typeof(Action<Vector2, bool, bool>), c, mi);
                var at = c.StageToCanvas(p);
                move(at, false, false);
                object keep = null;
                var mode = GarbageCollector.GCMode;
                try
                {
                    GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                    long a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) keep = new byte[128];
                    r.Junk = GC.GetTotalMemory(false) - a;
                    a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) move(at + new Vector2(i % 40, 0f), false, false);
                    r.Grew = GC.GetTotalMemory(false) - a;
                }
                finally { GarbageCollector.GCMode = mode; }
                GC.KeepAlive(keep);
            }
            Send(EventType.MouseUp, p + new Vector2(120f, 0f)); yield return null;
        }

        static void NudgeProbe(Canvas<GalleryBox> c, out long grew, out long junk)
        {
            object keep = null;
            var mode = GarbageCollector.GCMode;
            try
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                long a = GC.GetTotalMemory(false);
                for (int i = 0; i < 2000; i++) keep = new byte[128];
                junk = GC.GetTotalMemory(false) - a;
                a = GC.GetTotalMemory(false);
                for (int i = 0; i < 2000; i++) c.Nudge(i % 2 == 0 ? 1f : -1f, 0f);
                grew = GC.GetTotalMemory(false) - a;
            }
            finally { GarbageCollector.GCMode = mode; }
            GC.KeepAlive(keep);
        }

        IEnumerator SectionCanvasPerf()
        {
            GalleryEditor.Model = new EditorModel();
            GalleryEditor.Open(); yield return null; yield return null;
            try
            {
                GalleryEditor.Model = new EditorModel { Boxes = GalleryEditor.ManyBoxes() };
                GalleryEditor.Nav.Go(GalleryEditor.CanvasItem);
                for (int i = 0; i < 3; i++) yield return null;
                var c = Cv;
                Check(c.Content.Query<VisualElement>(name: null).ToList().Count >= 108 && GalleryEditor.Model.Boxes.Count == 108, "the 108-box page draws its boxes",
                    GalleryEditor.Model.Boxes.Count.ToString());
                c.Fit(); c.Snap = true; c.SelectAll(); yield return null;
                var all = new DragRun();
                yield return TimedDrag(c, "k50", all);
                Main.Log("[perf] canvas drag 108: mean " + all.Mean.ToString("0.000") + " ms, worst " + all.Worst.ToString("0.000") + " ms");
                Main.Log("[perf] canvas drag 108: frame " + all.FrameMean.ToString("0.0") + " ms during the drag, " + all.IdleMean.ToString("0.0") + " ms idle (logged)");
                Main.Log("[perf] canvas drag 108: heap grew " + all.Grew + " bytes over 2000 moves, " + (all.Grew / 2000f).ToString("0.0") + " per move (2000 x 128-byte arrays grew it " + all.Junk + ")");
                Check(all.Moved > 0f && c.Selection.Count == 108, "the drag moved all 108", all.Moved + " / " + c.Selection.Count);
                Check(all.Mean <= 1.0 && all.Worst <= 3.0, "a move of 108 selected boxes, snapping on: mean <= 1 ms, worst <= 3 ms",
                    all.Mean.ToString("0.000") + " / " + all.Worst.ToString("0.000"));
                Check(all.Junk > 0, "the heap probe sees allocations", all.Junk + " bytes");

                History.Clear();
                yield return Tap(StageOf(BoxOf("k40").Rect, c).center);
                Check(Keys(c) == "k40", "one box selected among 108", Keys(c));
                var one = new DragRun();
                yield return TimedDrag(c, "k40", one);
                Main.Log("[perf] canvas drag 1 of 108: mean " + one.Mean.ToString("0.000") + " ms, worst " + one.Worst.ToString("0.000") + " ms");
                Main.Log("[perf] canvas drag 1 of 108: frame " + one.FrameMean.ToString("0.0") + " ms during the drag, " + one.IdleMean.ToString("0.0") + " ms idle (logged)");
                Main.Log("[perf] canvas drag 1 of 108: heap grew " + one.Grew + " bytes over 2000 moves, " + (one.Grew / 2000f).ToString("0.0") + " per move (2000 x 128-byte arrays grew it " + one.Junk + ")");
                Check(one.Moved != 0f, "the drag moved the box", one.Moved.ToString());
                Check(one.Mean <= 1.0 && one.Worst <= 3.0, "a move of 1 box among 107 snap targets: mean <= 1 ms, worst <= 3 ms",
                    one.Mean.ToString("0.000") + " / " + one.Worst.ToString("0.000"));

                // Bytes per nudge step (a held arrow repeats one every 0.05 s): the public Nudge, which records and commits each call.
                History.Clear(); yield return null;
                c.Nudge(1f, 0f); c.Nudge(-1f, 0f);
                long ngrew, njunk;
                NudgeProbe(c, out ngrew, out njunk);
                Main.Log("[perf] canvas nudge 1 of 108: heap grew " + ngrew + " bytes over 2000 nudges, " + (ngrew / 2000f).ToString("0.0") + " per nudge (2000 x 128-byte arrays grew it " + njunk + ")");
                Check(njunk > 0 && ngrew == 0, "a nudge that extends the step on top allocates nothing", ngrew + " bytes over 2000 nudges");
                Check(History.Undo() && !History.CanUndo, "2000 nudges of one selection within the coalescing window are one step", History.NextUndo);
            }
            finally { History.Clear(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }
        }
    }
}
