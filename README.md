# Facet

Facet is a UI engine for Unity mods, built on UI Toolkit: one shared kit of windows, controls, theming, navigation and search, so that a mod's settings screens are a page of data and calls instead of a framework of its own. It ships as a standalone mod and targets any Unity game on Mono, and later IL2CPP.


## Install

Download `Facet-ML.zip` or `Facet-UMM.zip` (built by `./release.sh` into `dist/`) and unzip it into the game folder.

- **MelonLoader:** `Facet-ML.zip` holds `UserLibs/Facet.0.2.dll` and `Mods/Facet.Mod.dll`. Press F8 in game for the Gallery.
- **UnityModManager:** `Facet-UMM.zip` holds `Facet/` (`Facet.Mod.dll`, `Facet.0.2.dll`, `Info.json`), which goes under `Mods/` (or `UMMMods/` on the Mac/Steam layout).

## Use it from a mod

```csharp
using Facet;
using UnityEngine;

static readonly Host Host = Host.Create("MyMod");

class Settings { public bool Enabled; }
static Settings Cfg = new Settings();

static void Open()
{
    var surface = new Surface(Host, "MyModWindow", Layer.Window);
    var window = new Window(surface.Root, "main", "My Mod", new Rect(120, 80, 480, 320));
    Controls.Toggle(window.Body, "Enabled", Bind.To(() => Cfg.Enabled, v => Cfg.Enabled = v));
}

// Every frame, from your mod's update: Escape closes the topmost window, Ctrl+Z undoes, and the wheel
// is polled because a runtime panel does not reliably receive WheelEvent. Each is answered once per
// frame across every mod and Facet version, so every mod can call them.
static void Update()
{
    if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Facet.Input.HandleEscape();
    bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl)
        || UnityEngine.Input.GetKey(KeyCode.LeftCommand) || UnityEngine.Input.GetKey(KeyCode.RightCommand);
    bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
    Facet.Input.HandleUndo(ctrl, shift, UnityEngine.Input.GetKeyDown(KeyCode.Z), UnityEngine.Input.GetKeyDown(KeyCode.Y));
    Facet.Input.TickWheel(UnityEngine.Input.mousePosition, UnityEngine.Input.mouseScrollDelta.y);
}
```

The getter is re-read on every refresh, so replacing `Cfg` wholesale (a profile switch) shows through.
The host also owns the game's side of input: skip the game's keys while `Facet.Input.BlocksGameKeys`
(a window blocks game input, the Escape that just closed a window is still down, or a text field has focus).

A host's per-frame key order is `Input.HandleEscape`, `Input.HandleUndo`, a canvas's `PollKeys`, then
`Menus.HandleKeys` only when `PollKeys` returned false, then its own keys. `HandleUndo(ctrlOrCmd, shift, z, y)`
undoes on Ctrl+Z and redoes on Ctrl+Shift+Z or Ctrl+Y, for the Facet copy whose window is topmost; a second
host polling the same chord in that frame undoes nothing more and gets the first answer. It returns false,
spending nothing, without the chord, while a text field has focus (the field keeps its own undo) or while a
key capture runs; mid-drag it undoes nothing and returns true, and the release records the drag as one step.
When `HandleUndo` returns true, keep Ctrl+Z and Ctrl+Y from the game this frame, as with `HandleEscape`'s
return: the press was an undo, so the game must not undo too.
While a key capture runs, `HandleEscape` takes no window action and returns true: the capture's own poll
(`Input.ReadChord` gives `Input.ChordCancel`) cancels on the same press. A Key row's cancel spends that frame's
Escape too, so no window closes on it whether the capture's poll or a host's `HandleEscape` runs first.
`Facet.Mod/Gallery.cs` has one of every control, `Facet.Mod/GalleryParts.cs` one of every 0.2 part, and
`Facet.Mod/GalleryEditor.cs` the editor parts, a page each.

## Theming a part

A host's `Theme` carries the whole look: a colour per `Role`, `Theme.Sizes` (radius, lines, padding, row
heights, type, control sizes) and `Theme.Looks` (one `Look`, a per-state recipe of roles, per part element).
Each host has its own, a copy of `Theme.Dark` unless it passes one (`o.Theme`); change it before you build.
Colours re-theme open windows: set roles and call `Notify()`, or `Recolour(accent)`. `SetAccent(colour)` changes
only the accent: `Accent` and `Focus` take the colour, `AccentText` the text colour `FromAccent` would give it, and
every other role, the sizes and the looks stay (a skin keeps its panel colours and radius). Sizes and looks apply
when a part builds, so a change shows on the next build. `Theme.Dark` is shared by every mod and frozen:
its `Sizes` and `Looks` hand back a fresh copy on each read, so writing to them changes nothing.
`Looks.Card` is the window shell and popups, `Looks.Group` a card of rows on a page, `Looks.Stage` a
preview's stage. Dark keeps 0.1's look, so `Looks.Rule` is invisible by default; a skin gives it a colour, and
every rule takes it: a card's header and rows, the nav's groups, a tab row.

## Shared across versions

`Anim.Speed` is one value for every loaded Facet from 0.2 on (0.1 keeps its own), so one setting slows or
stops every window's motion; 0 is instant. So is `Anim.SmoothScroll` (0.2 on; unset is off), so one
setting makes every window glide to where the mouse wheel sends it or step at once. `Input.TickWheel` is
counted once per frame: the first call with a delta takes the frame, and the topmost open window of any
version scrolls one notch. `Input.HandleUndo` is counted the same way, with the frame's answer kept for a second
mod's call; a topmost window of a Facet copy older than the undo chord leaves the chord to that copy's own host.

## Languages

A host's `Language` picks the fonts: Korean (`ko`), Chinese (`zh`) and Japanese (`ja`) draw through the
OS's CJK font, loaded on first use. Two CJK languages in one process share Han glyph forms: the first one
loaded wins. A BCP-47 tag works too: `Host.Language` reads only its primary subtag, lower-cased (`ko-KR` and
`KO_kr` are `ko`).

After a language change, `host.RelabelSurfaces()` relabels every surface made for that host (its windows and a
`SettingsWindow`'s) in place; `host.DisposeSurfaces()` disposes them all, and the host stays (`Host.Named` still
returns it).

**Facet's own words** (a Many row's "Mixed", the header's "Undo" and "Undo {0}", a canvas's "Move", a key row's "press a
key…", search's "Nothing matches.") come from Facet's table, which has English and Korean. A host may answer them
for other languages: Facet first asks the host's `Translate` for `"Facet:" + english` (`Facet:Mixed`,
`Facet:Undo {0}`). A null or empty answer, the key itself, or a throw falls back to Facet's table, so a host
answers only the keys it chose to define and its own "Undo" never reaches Facet's. The keys are Facet's English
words exactly as `Facet/Core/Words.cs` lists them (its Korean table holds every one): `Nothing matches.`,
`Search`, `Results for “{0}”`, `{0}: this page failed to build — see the log.`, `Run`, `Undo`, `Redo`,
`Undo {0}`, `Redo {0}`, `Sure?`, `Active`, `unbound`, `press a key…`, `Click the strip to add a stop.`,
`Stop colour`, `Position`, `Add stop`, `Remove stop`, `unavailable, see the log`, `Advanced`,
`Collapse sidebar`, `Expand sidebar`, `Move`, `Resize`, `Nudge`, `Delete`, `Bring forward`, `Send backward`,
`Mixed`, `OK`, `Cancel`. A pattern with `{0}` is filled after it is translated.

## History

`History` is one undo stack for the whole session, by value: every value row records how to put its change
back and how to do it again, and the window header's arrows undo and redo. Changes to one key within
`CoalesceSeconds` (0.9 s) are one step, keeping the first undo and the last redo, so a slider drag is one
step; a null key never coalesces. The undo chord (`Input.HandleUndo`) undoes this Facet copy's one `History`,
whichever mod recorded the step and whichever mod's window is topmost; for an undo scoped to a window, bind
its steps with `valid: () => surface.Visible` (the window's `Surface`), so the chord skips them while it is hidden.

- **Steps.** `History.Step(label, key, valid, translate)` opens a scope: everything recorded until it is
  disposed (`Record`, row writes, `Snapshot`) is one step under its label and key. An undo runs the parts
  last-first, a redo first-last, each on its own, so the ones that throw are logged in one line and the others
  still run. A nested scope joins the outer one and ANDs its `valid` in; an empty scope records nothing. A
  scope opened while recording is off (inside a snapshot's `change`, or during an undo) records nothing and
  leaves the outer step's `valid` alone. **Close a scope in the call that opened it**: always `using`, never
  held across frames or a `yield`. One still open 5 s after it opened is closed by the next record (logged
  once), and `History.Clear()` abandons one (its later `Dispose` does nothing).
- **Snapshots.** `History.Snapshot(label, key, capture, restore, change, valid, translate)` captures, runs
  `change` with recording suppressed, and captures again. Equal captures (`EqualityComparer<T>.Default`)
  record nothing; otherwise undo restores the first capture and redo the second, with `History.Applying`
  true. Same-key snapshots coalesce: the first before, the last after. **Captures compare by value**: return a
  copy or an immutable value, since a list mutated in place equals itself and records nothing. An exception
  from `capture` or `change` is logged and swallowed (whatever `change` did is still recorded when the
  captures differ), so do the work that can fail first and only swap in `change`, as the import below does.
- **Validity.** A step is live while its `valid` returns true. `Undo` and `Redo` skip a stale step (false,
  or a throw, logged once) and apply the nearest live one below it, and return false when none is live.
  `CanUndo`, `CanRedo`, `NextUndo`, `NextRedo` and their `…Text` skip stale steps too, so the header never
  names one. A skipped step stays where it is: once `valid` says true again it is live again.
  `History.BoundTo(() => doc)` reads the document when it is called (when the step is recorded) and stays
  true while it returns that same reference. **`BoundTo` and `UndoDocument` compare by reference**: return
  the document object itself, never an int or a computed string (a fresh box or string is never the same
  reference, so every step would be stale at once). A document whose getter throws gives a step that is
  dropped, not kept, and the throw is logged at most once per 10 s. After replacing a document, call
  `History.Invalidate()`: it raises `Changed`, so headers re-read. `Changed` is not raised when a change only
  coalesces into the top step and the header would show the same.
- **Labels.** History is session-wide, so pass your host's translator (the `HostOptions.Translate` you gave
  your host) as `translate` to `Record`, `Step` and `Snapshot`: the header shows the label through it, and
  never through another host's table. Rows pass their own host's.
- **Rows.** The value rows record (`Toggle`, `Slider`, `IntSlider`, `Number`, `Choice`, `Dropdown`, `Text`,
  `Colour`, `Key`) and so does the switch of a `Tile` or a `PageHeader`; `Undoable` and `UndoDocument` apply
  to those. `ControlOptions.Undoable = false` makes a row record nothing, for a host that records the change
  itself. `ControlOptions.UndoDocument` binds each of the row's steps to the document it was made on. An undo
  or redo of a row's step writes the bind once, shows the value, and raises the row's `Committed` with it:
  `History.Applying` is true inside the handler, and `Changed` is not raised. It shows and raises `Committed`
  only while that row is on a panel: a row whose page was removed or whose window closed is left alone (the
  bind is still written), so work that must follow every undo belongs in the bind's setter, not in `Committed`.
  A step holds the row's bind, and whatever the bind captures, for as long as History keeps it; never the row.

```csharp
// One step for a change to many items, live only while Doc is the document it was made on.
using (History.Step("Move", null, History.BoundTo(() => Doc)))
    foreach (var it in selected)
    {
        float was = it.X, now = was + dx;
        it.X = now;
        History.Record("Move", null, () => it.X = was, () => it.X = now);
    }

// A step that swaps the document: no validity, and its undo brings the old document's steps back.
// Load first: a throw from Load reaches you, and change only swaps.
var d = Load(path);
History.Snapshot("Import", "import", () => Doc, x => Doc = x, () => Doc = d);
```

### Undo contract

Validity is by reference, so a document must keep its identity:

1. **A restore keeps the document's reference.** It loads into the same object, or puts back the very object
   it captured, never a copy: a copy would leave every step bound to the original stale.
2. **Keys survive a restore.** A step that names an item finds it by its key, so an undo that rebuilds the
   items gives them the keys they had.
3. **A step that swaps the document passes `valid: null`.** An import or a profile switch replaces the
   document, so a step bound to it would be stale the moment it was recorded.

## Canvas

The canvas's geometry is plain BCL code (`Facet/Core/CanvasMath.cs`, tested offline), in canvas units with y down.

- **Move.** A drag snaps the moved box's edges or centre line to the canvas centre or to another item's line
  within reach, drawing a guide; on an axis with nothing in reach, that edge (the box's left or top) lands on the
  grid, whose step grows as the view zooms out. A snapped box lands on a whole unit even where the line is a half
  unit (an odd size's centre line); the guide stays on the line. With snapping off, or Alt held, it moves in whole
  units. A drag locked to one axis (Shift) leaves the other where it was, with no guide.
- **Resize.** The opposite edges stay put and sizes come out in whole units, snapping on or off; while snapping
  the dragged side rounds to the grid, then to another item's size within reach. Shift keeps the aspect: on a
  corner the larger relative change drives, and the other side follows in whole units, so it may sit off the
  grid. On an edge the other side grows about the box's centre line rather than from a fixed top or left edge;
  its position stays whole, so it is centred to within half a unit of that line.

`Controls.Canvas<T>(parent, label, o => …)` puts the consumer's items on a stage inside a `Controls.Preview` pane
(`o.Pane` configures it) and lets the user select, move and resize them. Rects are canvas units, y down; Facet
reads and writes them only through `GetRect` and `SetRect`, and every consumer delegate runs under `Safe`.

- **Who draws what.** The consumer draws its items into `Canvas.Content`, a layer in canvas units that the zoom
  scales and the pan moves; it never takes the pointer. Facet paints the chrome over it in stage px, so it keeps
  its size at any zoom: the selection outlines, the eight handles of a lone resizable item, the snap guides
  (`Role.Guide`), the marquee and the grid (`ShowGrid`), with `Sizes.Line` hairlines, in the theme the canvas wears
  when it paints (replacing or recolouring that theme repaints it, no rebuild). The zoom badge is a `Label` over
  them. `Stage` takes the pointer, in stage px; `Root` is the pane.
- **The pane** is a `Controls.Preview`: `Id` names its stage (`Id-pane` the pane), and
  `VisibleWhen`, `EnabledWhen`, `Tooltip`/`TooltipText`, `ContextMenu` and `ContextMenuItems` act on the pane, as on a preview's.
  `EnabledWhen` false also stops the stage taking the pointer. `Help` is not shown. The stage keeps its right and
  middle presses from the pane, so the pane's context menu opens only around the stage; the stage's own is `Menu`.
- **Selection.** A click selects the topmost selectable item, and Ctrl, Cmd or Shift toggles it in or out. A drag
  on empty stage draws a marquee (a modifier adds to the selection); a click there clears it. `Selection`,
  `Primary` (the item a drag snaps by), `SelectionVersion`, `Select`, `SelectAll`, `Clear`.
- **Events.** `SelectionChanged` when the selection changes, including when a `Refresh` maps it onto new objects
  with the same keys. `Changed` on every frame of a drag: redraw the items from their rects. `Committed` at the
  end of a gesture that changed something: save. A gesture with no net change records and commits nothing. After
  an undo or redo see Undo: `Changed` comes once on every canvas on a panel, `Committed` only on the step's.
- **Undo.** A gesture is one step ("Move" or "Resize"), bound to `UndoDocument` when it is set. It holds each
  changed item's key with its rect before and after, and finds the item again through `Find`, else by `Key` in
  `Items()`, else by reference, so an undo still reaches an item after `Items()` switched to another set or the
  page was rebuilt. After an undo or redo, each canvas on a panel whose name (`Id`, else label) is the step's
  `Refresh`es and raises `Changed`, then `Committed`, once each, inside the apply: `History.Applying` is true
  there, as in a row's undo `Committed`, so a handler that also saves from `History.AfterApply` skips the save
  while `Applying`. Every other canvas on a panel `Refresh`es and raises `Changed` once after the apply
  (`Applying` false). Off a panel (its page switched away, its window closed) no canvas re-reads or commits: the
  undo only writes each rect through `SetRect`, with `History.Applying` true. So work that must follow every
  undo (marking the document for saving, updating what the rects drive) belongs in `SetRect` under
  `History.Applying`, not only in `Committed`. `Undoable = false` records no steps; the events still fire.
- **What History keeps.** A canvas step holds each changed item's key and rects (with no `Key`, the item itself),
  the `Items`, `Key`, `Find` and `SetRect` delegates and the `UndoDocument` getter, and whatever those capture, for
  as long as History keeps the step (500 steps, session-long); never the canvas, its page or its options. Keep
  those delegates on the document, not on the page.
- **`Refresh()`** re-reads `Items` and the rects, keeps the selection by key (now as the new objects, with one
  `SelectionChanged` if that changed it) and redraws the chrome. Call it after changing the items or their rects
  outside the canvas; every press calls it first.
- **`Ghosts`** is only for the companion tab: rects that are snap targets, never hit, selected or moved. A hidden
  item is not a ghost: keep it in `Items` and draw it dim, so it can still be selected and shown again.
- **Cursors.** Over a canvas the host's cursor hook (`HostOptions.Cursor`) is asked for `Move` over a movable
  item and `ResizeHorizontal`, `ResizeVertical`, `ResizeCorner` or `ResizeCornerNESW` over a handle. A hook must
  return false for a kind it does not know, so Facet draws that one: `Cursors.Kind` is appended to within a minor,
  and a hook that claims every kind shows its own arrow for the new ones.
- **A lost pointer capture commits.** Losing the capture, or the canvas's window hiding, ends a gesture as a
  release does: one step and one `Committed`. A canvas drag has written every frame's rect to the model already,
  so the loss keeps what the user saw. A drag that changes nothing until its drop (a list reorder) cancels on a
  lost capture instead: there is nothing on screen yet to keep.
- **Draw on `Changed`, save on `Committed`.** `Changed` comes on every move, nudge, delete and z-step (and after an
  undo); `Committed` once at the end of each: a release, an arrow key let go, a delete or a z-step.

### View

- `Zoom` (0.3 to 4; setting it keeps the view centre), `Pan` (the stage position of canvas (0, 0)), `ZoomBy`,
  `Fit` (every item with 40 px padding; with none, `ResetView`), `Centre` (the items at the current zoom),
  `ResetView` (zoom 1, centred), `StageToCanvas`, `CanvasToStage`, `ViewCentre`, `HitTest`. `ViewChanged` fires
  once per change, and the zoom badge follows. The first time the stage has a size the canvas runs `ResetView`, so
  a stage point is `CanvasToStage` of a canvas point, never the canvas point itself.
- **The wheel** over the stage pans 20 stage px a notch on both axes (a trackpad's sideways scroll included); Shift
  turns a vertical-only wheel sideways; Ctrl or Cmd zooms at the pointer by 0.1 a notch. The panel's own
  `WheelEvent` (3 a notch) and the host's polled `Input.TickWheel` (when the stage is what the pointer is on, not an
  element over it, it feeds the canvas, not the page's scrollers; x and the modifiers are read from Unity's input in that call) feed one handler that takes at most one
  of them a frame (the poll also skips the frame after an event's, which can carry the same notch), and the page
  around the stage neither scrolls nor glides.
- **The middle button** drags the view at once. **The right button** under 5 stage px of travel is a click: the
  item under the press is selected (alone, unless it was selected) and `Menu(item)` opens there through
  `Popup.Context` (null on empty stage; a null or empty list opens nothing). From 5 px it drags the view and opens
  no menu.
- **Double-click.** A second press on the same item within 0.3 s raises `Activated(item)` once.

### Keys

- **Focus.** A press on a stage focuses that canvas (and blurs a text field); one canvas of a Facet copy is
  `Focused` at a time. A press anywhere else on a Facet surface, or the canvas's surface hiding, unfocuses it; a
  press in a popup (picking from the canvas's own menu) does not.
  `Hovered` follows the pointer.
- **`PollKeys(down, held)`**, which the host calls every frame with its key reads. Nothing while a text field has
  focus, a key capture or a drag runs, or Ctrl, Cmd or Alt is held (chords are the menus'; Shift is ignored).
  Focused: the arrows nudge the selection 1 canvas unit whatever the snap, repeating after 0.4 s every 0.05 s, as
  one "Nudge" step per selection (so a held or re-pressed arrow within 0.9 s is one step, whose end each repeat moves
  in place, allocating nothing) with `Committed` when the
  key is let go; Delete and Backspace run `DeleteSelection`; `]` and `[` run `StepZ(true/false)`. Focused or
  hovered: `+`/`=` zoom in, `-` out, `0` `ResetView`. It returns true when it acted: an arrow that moved nothing
  (nothing selected, or every item at the limit) returns false, so the host's menus get the key. A `Nudge` call
  commits at once, and ends a key nudge so its release commits nothing more.
- **The host's order:** `PollKeys` first, `Menus.HandleKeys` only when it returned false, so a key that is both a
  canvas key and a menu shortcut (Edit › Delete on Delete) runs once.
- **`Delete` and `ZOrder` must record.** `DeleteSelection` calls `Delete(selection)` and `StepZ` calls
  `ZOrder(the whole list in its new draw order)` inside a step Facet opens ("Delete", "Bring forward", "Send
  backward"; each its own step, never merged with the last); the callback records its change (`History.Snapshot`
  or `Record`) or the step is empty and nothing is undoable. Then the canvas re-reads, raises `Changed` and
  `Committed`. Menu items call `DeleteSelection` and `StepZ` too. Their undo needs no redraw of the consumer's own: the
  canvas re-reads and raises `Changed` after the apply.
- **Escape.** A drag in progress takes the frame's Escape first, whichever mod's host (and Facet version) asked:
  the items (or the view) go back and nothing is recorded. Otherwise, after a focused text field, a focused canvas with a selection clears it, and the window
  stays open; the next Escape reaches the window.

## Menus

One `MenuItem` list is a menu bar's menus, a context menu and the shortcuts a host's key poll runs.

- **`MenuItem`.** `Text` is an English key, translated by the host of the element the menu opens from; `TextOf` is
  read each time the menu opens and shown as returned, and wins. `Shortcut` ("Ctrl+Shift+C", "Delete", "[",
  "Ctrl+0"; Cmd is Ctrl) shows right-aligned on the row, as ⌘⇧⌥ on a Mac and `Ctrl+Shift+Alt+Key` elsewhere.
  `Enabled` and `Checked` are read when the menu opens: a disabled row fades and does not run, and a menu with any
  `Checked` item keeps a check column. `Items` makes a submenu, `Danger` paints the text in the Danger role,
  `MenuItem.Separator` is a rule (a new one on each read), and `Id` names the row.
- **The card.** Picking a row closes every open menu of its chain, then runs `Run` under `Safe`. A submenu row shows
  › and opens beside the row on hover or click. Escape closes the newest menu, once per frame.
- **`Controls.MenuBar(parent, menus)`.** Each top item's `Text` is a title and its `Items` the menu a click opens
  under it. While a menu is open, moving onto another title with a menu opens that one instead; a top item with no
  `Items` is a command title, whose `Run` (when `Enabled` allows) runs from its click alone. `Left` holds leading
  controls before the titles; `Tools` is a `Toolbar` at the right end. `Open(i)`, `Close()`, `IsOpen`.
- **`Popup.Menu(anchor, items)`** opens the same card under any element.
- **Context menus.** `ControlOptions.ContextMenuItems` is asked at each right-click on a row (a card, a preview or
  canvas pane, a tile grid, a toolbar button) and its items open at the pointer; it wins over `ContextMenu`, whose
  names open there too. `Popup.Context(owner, local, items)` opens the card with its top-left at `local`, a point in
  `owner`'s own coordinates (a pointer event's `localPosition`), moved inside the panel when it would overflow.
- **Shortcuts: `Menus.HandleKeys(owner, items, down, ctrlOrCmd, shift, alt)`**, called from the host's per-frame key
  poll with its `GetKeyDown` and the held modifiers (Cmd counts as Ctrl), runs the first enabled item, submenus
  included, whose `Shortcut` key went down with exactly those modifiers, and returns true. It acts only while
  `owner`'s surface is the topmost visible window, menu, popup or modal (so nothing runs under an open menu, dropdown
  or dialog), never while a text field has focus, a key capture runs or a drag is in progress, and never for Ctrl+Z,
  Ctrl+Shift+Z or Ctrl+Y, which the undo chord owns (an Undo item may still show Ctrl+Z). A frame with no key down allocates nothing. **A window whose menus answer keys must raise its
  surface when pressed** (`window.Raised += () => surface.BringToFront();`): otherwise a click into it leaves the
  other window in front, and the keys go on answering there.

## Toolbar

`Controls.Toolbar(parent)` is a row of tools over a canvas or a list, left to right with `Sizes.Gap` between them.
Its edges pad as a card pads its rows, in the same two steps (`Sizes.Line` on `Root`, then `Sizes.Pad` on an inner
row that holds the items), so it lines up with the cards around it to the pixel at any scale.

- `Search(changed, placeholder)`: a search field with a clear button (the window header's).
- `Chips(bind, values, text)`: `Controls.Chips` in the bar.
- `Icon(icon, tooltip, click)`: `Ui.IconButton`, a square button showing a `Background` at `Sizes.Icon`, tinted as text
  (`Paint.Tint`) and lit on hover. `Glyph(glyph, tooltip, click)`: a bare text glyph drawn like the header's ↶ ↷.
  `Button(text, click)`: a button in `Looks.Button`. Their `configure` takes `Id`, `Tooltip`/`TooltipText`,
  `EnabledWhen`, `VisibleWhen` and the context menus, as a button row does.
- `Toggle(text, bind, tooltip)`: a button that shows selected while the bind is true and flips it on a click. A view
  switch (snap, grid): it records nothing in History.
- `Label(text)`: a `Func<string>` re-read on every refresh and shown as returned (a zoom readout).
- `UndoRedo()`: the window header's ↶ ↷, with the same tooltips and enablement.
- `Spacer()`: takes the free width, so what follows sits at the right end.

`Controls.Chips<T>(parent, bind, values, text)` is one segment per value, the bound one selected; a click selects its
value. `text` names a value (an English key, translated like a label). It has no label to name a step, so it records
nothing in History: an edit to the document is a labelled row such as `Choice`.

## Inspector

An editor's inspector edits whatever is selected, one row per property, over every selected item at once.

```csharp
var cols = Controls.Columns(page, o => { o.Left = 36f; o.Right = 64f; o.RightFill = true; });
var right = cols.Right;
var bar = Controls.MenuBar(right, menus);
var canvas = Controls.Canvas<Item>(right, "Canvas", o => { /* ... */ });
var scroll = Ui.Scroll(right);
Controls.Live(scroll, () => canvas.SelectionVersion, box =>
{
    Controls.Slider(box, "Hue", Bind.Many(() => canvas.Selection, i => i.Hue, (i, v) => i.Hue = v,
                                          key: i => i.Key, all: () => Editor.Doc.Items),
                    o => o.UndoDocument = () => Editor.Doc);
});
```

`Editor.Doc` is a static field, not a local: `all` and `UndoDocument` are held by every step, so they must not
capture the page's locals (C# gives the lambdas of one scope one shared closure, and `() => canvas.Selection`
puts the canvas, and with it the page, in that closure).

- **`Bind.Many(items, get, set, key, all)`** reads the first item (`default` with none) and writes every item.
  `Bind<T>.Mixed` is true while any item's value differs from the first (`EqualityComparer<T>.Default`); it is false
  for every other bind. A row's write records one step that captures the items it wrote, or with `key` their keys,
  and each one's own old value: undo puts each value back (a mixed selection comes back mixed) and redo writes the
  same items again, whatever is selected by then. By key, undo and redo find each key again in `all` (else `items`)
  and skip a key that has gone, so items the consumer rebuilt (an undone snapshot, an import) still get their
  values back. Steps coalesce only over the same captured set: two selections' edits are two steps, however close,
  even when the row was rebuilt between them; a drag over one set is one step and its frames allocate nothing (with
  `key`, as long as `key` does not). A step holds `set`, `key`, `all` and the captured items or keys, never `items`
  or the row, so `() => canvas.Selection` does not keep a removed page alive; with `key` and no `all` it finds the
  keys through `items`, which it then holds. While a `Text`, `Number` or slider number box is focused, the row keeps
  writing the items selected when it took focus, so a press on another item commits the typing to the first set.
- **Mixed.** A row over a mixed selection shows it: `Slider`/`IntSlider`/`Number` read "Mixed" (the thumb at the
  first value), `Colour` shows an `Off` swatch and "Mixed", `Text` is empty, `Choice` selects nothing, `Dropdown` and
  `Key` read "Mixed", `Toggle` draws an off track with the knob centred. Any write sets every item (a click on a
  mixed toggle sets them all on); leaving a mixed `Text` without typing writes nothing.
- **`ColumnsOptions.RightFill`**: the right column is as tall as the page's viewport (less whatever sits above the
  columns while it is still in view) and stays pinned to its top while the left column scrolls, until the columns'
  bottom reaches it: then it scrolls up with them, so what follows the columns stays uncovered. Its children stack;
  a `Ui.Scroll` (a skinned vertical `ScrollView`, `flexGrow` 1, `minHeight` 0) takes the height that is left, and the
  wheel over it scrolls it, not the page. Stacked (below `StackBelow`) the column is its content's height again.
- **`Controls.Live(parent, key, build)`** builds once, then rebuilds whenever `key()` changes (`object.Equals`; a
  `Func<int>` key such as a selection's version is compared by value and allocates nothing per pass),
  checked on every refresh pass and every `Surface.Refresh()`. A rebuild runs in the context of the first build (its
  Nav page and tab), so a `Tabs.Pages` inside keeps its chosen tab while the new tabs still have that name and falls
  back to the first otherwise. Rows built inside, including a tab's rows built later on a click, have the rebuild
  count of every enclosing Live region in their history key: rows of two builds never coalesce, even over one
  object.

## Lists

`Controls.Reorder<T>(parent, o => …)` is a group of items the user picks, drags into a new order, renames in place
and right-clicks: a tab strip, or with `Vertical` a list of rows. One `Reorder` is one group; two groups side by side
(hands and feet, say) are two.

```csharp
var tabs = Controls.Reorder<Profile>(row, o =>
{
    o.Id = "profiles"; o.Title = "Profiles"; o.Scroll = true;
    o.Items = () => Doc.Profiles; o.Key = p => p.Id; o.Text = p => p.Name;
    o.Selected = p => p == Doc.Open; o.Active = p => p == Doc.InUse;
    o.Select = p => Doc.Open = p;
    o.Move = (from, to) => { var p = Doc.Profiles[from]; Doc.Profiles.RemoveAt(from); Doc.Profiles.Insert(to, p); };
    o.Rename = (p, name) => p.Name = name;
    o.Menu = p => ProfileMenu(p); o.Add = () => NewProfileMenu();
});
```

- **Keys.** `Key` names an item across refreshes. Left null, the item itself is its key (by `Equals`): a `string`
  item (an id) is its own key, and any other item is the same item while it is the same object, so two items with
  the same text stay two. Set `Key` when `Items` hands back new objects for the same item (a reloaded document).
- **Refresh.** Items are rebuilt only when their keys change; `Text` (shown as returned, not translated), `Selected`
  and `Active` (a ● before the text) are re-read on every refresh pass of the surface and on `Refresh()`, allocating
  nothing when nothing changed. `Build(item, e)` adds content to each item as it is built.
- **Layout.** A strip wraps onto a new line when full; with `Scroll` it stays on one line inside a horizontal
  `ScrollView` that the wheel scrolls sideways (on the panel's own wheel event and on the polled path) and that a drag
  held still near either edge keeps scrolling, every frame, until it leaves the edge or ends. `Vertical` stacks the rows.
- **Click and drag.** A click calls `Select`. A drag past 5 px (with at least two items and a `Move`) shows a drop
  marker at the slot under the pointer; the release calls `Move(from, to)` once when the slot differs, `to` being the
  index in the new order (remove at `from`, insert at `to`). The drag is the frame's gesture, so Escape cancels it
  with no `Move` and the window stays open. A lost pointer capture cancels it too: a canvas drag commits on a lost
  capture because it has written the model on every frame, but a reorder has changed nothing until it drops.
- **Rename.** A double-click, or `BeginRename(item)`, swaps the item's text for a focused field holding its name.
  Enter (or focus leaving) calls `Rename` with a name that is not blank and not unchanged; Escape (the key in the
  field, or the host's `Input.HandleEscape`, which blurs it) and an empty name cancel. When the items are rebuilt
  while the field is open (their keys changed), the field closes with no `Rename`: the typed name never lands on
  another item.
- **Menus.** A right-click opens `Menu(item)` at the pointer. `Add` puts a ＋ after the items that opens its menu
  under it; `TitleMenu` makes the `Title` caption a button that opens its menu under it.
- Of the inherited options only `Id` (the group; its parts are `Id-item`, `Id-text`, `Id-mark`, `Id-title`,
  `Id-add`, `Id-drop` and `Id-rename`) and `Tooltip` apply.

### Repeat

`Controls.Repeat<T>(parent, items, key, build)` is one box per item, keyed by `key`: `build(box, item)` runs once per
new key. Each refresh pass of the surface re-reads `items`; boxes of new keys are built in place, boxes of gone keys
removed, and the rest put in the new order without being rebuilt. A box whose place did not change stays on its panel
and its rows keep their state; a box that moves (the order changed) is taken off its panel and put back, which ends a
key capture or a text edit running inside it. Nothing allocates when the keys did not change. Returns the box that holds them (`repeat`; each box is `repeat-item`).

### Card grid

`Controls.CardGrid<T>(parent, o => …)` is a switcher of cards that wrap, `Sizes.CardWidth` wide: a thumbnail box
(`ThumbAspect`, 16:9 by default) the consumer draws into through `Thumb`, a title with a ● in the accent colour while
`Active`, and a note.

```csharp
var grid = Controls.CardGrid<Layout>(page, o =>
{
    o.Id = "layouts"; o.Items = () => Doc.Layouts; o.Key = l => l.Id;
    o.Title = l => l.Name; o.Note = l => l.Keys + " keys"; o.Active = l => l == Doc.InUse;
    o.Thumb = (box, l) => DrawMiniature(box, l);
    o.Click = l => Doc.Open = l; o.DoubleClick = l => Doc.Use(l);
    o.Menu = l => new List<MenuItem> { new MenuItem { Text = "Rename", Run = () => grid.BeginRename(l) } };
    o.Rename = (l, name) => l.Name = name;
    o.AddLabel = "New layout"; o.Add = () => NewLayoutMenu();
});
```

- **Refresh.** Cards are rebuilt only when the keys change (`Key`, else the item itself, as in a `Reorder`); `Title`
  and `Note` (shown as returned, not translated) and `Active` are re-read on every refresh pass and on `Refresh()`.
- **Thumbnails.** `Thumb(box, item)` draws into the emptied box when the card is built and on `Redraw(item)`, and at
  no other time: call `Redraw` when the item's look changed. A throwing `Thumb` leaves the box empty and is logged
  (at most once per 10 s, as every `Safe` site).
- **Pointer.** A click calls `Click` at once; a second click on the same card within 0.3 s also calls `DoubleClick`.
  A right-click opens `Menu(item)` at the pointer. With `Add`, a trailing "＋ `AddLabel`" card (an English key, the
  host translates it) opens `Add()` under it.
- **Rename.** `BeginRename(item)` (a card menu's "Rename") swaps the title for a focused field, as a `Reorder`'s
  rename does: Enter commits a changed, non-blank name through `Rename`; Escape, focus leaving with no change, or an
  empty name cancel. A rebuild while the field is open closes it with no call.
- Of the inherited options only `Id` (the grid; its parts are `Id-card`, `Id-thumb`, `Id-title`, `Id-mark`,
  `Id-note`, `Id-add` and `Id-rename`) and `Tooltip` apply.

## Rows: scrub and key capture

- **Scrub.** `Controls.Number(…, o => o.Scrub = true)` makes the row's label a handle (the cursor shows
  `ResizeHorizontal` over it): a drag sets the value to its start + (pixels / 4) x unit, the unit being `Step`, else
  `(Max - Min) / 200`, else 1; Shift multiplies by 10 and Alt by 0.1. The value clamps and snaps as a typed one does.
  Each drag is one undo step, even when the next drag follows within the coalescing window.
- **Reading a key.** A host's capture poll calls `Input.ReadChord(down, held, chord, mouse)` every frame while a
  capture runs (`down`/`held` are `UnityEngine.Input.GetKeyDown`/`GetKey`, or the host's own). It returns null until
  a non-modifier key goes down, then the key as `KeyCode` spells it (`K`, `Alpha1`, `F8`); with `chord` the held
  modifiers come first as `Ctrl+Shift+Alt+Key` (Command counts as Ctrl), and a modifier pressed and released with no
  other key returns its own name (`LeftShift`) on the release. Escape returns `Input.ChordCancel`, never "Escape".
  Mouse buttons count only with `mouse` (`Mouse0`-`Mouse6`), and the left one not while the pointer is over a Facet
  window; none while a Key row's `Mouse` prompt is up, whose catcher binds them (one bind per press, whichever runs
  first). The poll after the catcher bound a button returns that button once, so the host's poll ends as it does on a
  key it read itself.
- **Key rows.** `KeyOptions.Capture` gets a callback; handing it `Input.ChordCancel` ends the capture as a cancel: no
  write, and `CancelCapture` runs. `Chord` shows the value the way a menu shows a shortcut (⌘⇧K on a Mac; ⌘1 for `Ctrl+Alpha1`, ⌘Space for `Ctrl+Space`).
  `Prompt` opens a popup under the row while the capture runs ("Press a key", or "Press a key or chord" with
  `Chord`) with Cancel; it closes when the capture ends, and Cancel, an outside click or Escape cancel the capture.
  The row is scrolled into view first and the card placed from where the row is after that scroll (a row with no
  layout yet, as one a `Live` just rebuilt, is waited for; one that gets none within a few frames, as in a folded
  section or a tab not shown, has its prompt opened over its window). With `Prompt` and `Mouse`, the prompt's catcher
  (over every window) binds the button pressed on it, the left one included (`Mouse0`), and nothing beneath sees that
  press; `CancelCapture` is not called (a bind is no cancel), and the host's next `ReadChord` returns that button
  once. Only Cancel and Escape cancel then. `Mouse` needs `Prompt`: on its own it does nothing (Facet logs it once),
  so a click is not bound and also reaches what lies beneath it.
- **`keyRow.BeginCapture()`** does what clicking the row does, for a "Rebind key" command: false when the row is not a
  Key row, has no `Capture`, or is off its panel. A row on a page that is not built yet needs it built first. For an
  editor's "Rebind key": select the item, `Tabs.Select` the tab holding the row, `Surface.Refresh()` (so a `Live`
  inspector rebuilds for the new selection), then `BeginCapture()` on the row that rebuild made.

## Dialogs

`Popup.Confirm(owner, title, body, done, o => …)` asks yes or no in a card on the Modal layer, centred over the
window `owner` sits in (on the screen when `owner` is null), behind a click catcher. `title` and `body` are English
keys translated by the owner's host; `ConfirmOptions.Ok` and `Cancel` are too ("OK" and "Cancel", Facet's own words,
by default). OK is focused, so a submit (Enter) runs it. Escape, Cancel, an outside click, the owner's window hiding or the
owner leaving its panel mean no; the window under the dialog stays open. `done` runs exactly once, through `Safe`,
after the card has closed. `Danger` paints OK in the theme's `DangerButton` look; a null `Cancel` shows OK alone, a
notice (Escape and an outside click still give `done(false)`).

## Status

`Controls.Status(parent)` is one line of feedback under an editor. `Show(key, role, seconds)` shows an English key
translated by the host (again on a relabel), painted in `role` (`TextMuted` by default); `Show(func, role, seconds)`
shows what the func returns, re-read on every refresh pass, for text already formatted or translated (an import's
result). `seconds > 0` fades it out after that long, then the line falls back to the resting line: the last `Show`
with no seconds (a count under the timed "Saved"), or empty if there is none. A later `Show` or `Clear` wins over a
pending or running fade. `Clear` empties it and drops the resting line.

`Controls.Info(…, o => o.Tone = () => role)` paints an Info row's value in the role `Tone` returns, re-read with the
text on every refresh pass (`Danger` for a problem). `Info`'s `configure` takes `InfoOptions`, a `ControlOptions`.

## Hooks and composition

Parts a consumer wires with plain delegates, for code that must not name a UI Toolkit event type.

- **`Bind<T>.Then(after)`** is the same bind with `after(v)` run once each write has landed, and once after each
  undo and redo of a write a row recorded through it, a `Bind.Many`'s included (its undo writes the items directly,
  so `after` gets the first item's restored value). A throwing `after` is logged through `History.Log` and the value
  stays written. `Mixed` and a Many bind's per-item recording are kept; `Then(a).Then(b)` runs `a`, then `b`. An
  undo of a row off every panel still runs `after`: it is part of the write, not of the row. The order is the same
  on a write, an undo and a redo, for both kinds: `after`, then the row's `Committed`, then `AfterChange`.
- **`Ui.Pointer(e, down, move, up)`** reports a press, every move and the release on `e`, at positions local to `e`,
  buttons 0, 1 and 2. A press captures the pointer until its release, so a drag's moves and release arrive outside
  `e`; a lost capture, or `e` leaving its panel, ends it like a release. Disposing the returned handle unregisters.
  Each delegate runs under `Safe`, through fields, so a drag allocates nothing.
- **`Ui.Every(e, ms, tick)`** runs `tick` every `ms` (0: every frame) only while `e` is on a panel and its surface is
  visible. Three throws in a row stop it until `e` is attached again; disposing stops it.
- **`Ui.WhileAttached(e, attached, detached)`** runs `attached` whenever `e` joins a panel (at once if it is on one)
  and `detached` whenever it leaves.
- **`Ui.Search(parent, changed, placeholder)`** is the toolbar's search field on its own (a ✕ that clears it);
  `changed` runs on every keystroke. **`TextOptions.Live`** does the same for a `Text` row, whose bind is still written
  only on Enter or blur; **`TextOptions.Placeholder`** is an English key shown while the field is empty;
  **`TextOptions.MaxLength`** (0: none) is the most characters the field takes, and longer text is cut before `Live`
  or the bind sees it.
- **`Controls.AfterChange(container, after)`** runs `after` once per `Committed` of any row under `container`: once
  per click, typed commit or finished drag (not per drag frame), and once per undo or redo of a row's step while
  that row is on a panel. Nested containers each run theirs, innermost first. A canvas's commits are not rows'.
- **A write refreshes its surface at once.** After a row's write (a click, a drag frame, `Set`) or an `Action`
  button's click, the surface's refresh pass runs in the same frame, so a row whose `VisibleWhen` reads what was
  written shows or hides now, not at the next tick. Writes made inside a write (a `Committed` or `AfterChange`
  handler writing another row, an `Action` writing several rows) join it: one pass runs when the outermost returns,
  so all of them and the outer row's own follow-ups show together. At most one such pass per frame: a second
  outermost write in the same frame waits for the timer, and a write made during a refresh pass starts none.
- **Typed choices.** `Controls.Dropdown<T>(parent, label, bind, values, text)`, `Controls.Choice<T>(…)` and
  `Controls.Enum<TEnum>(parent, label, bind)` take the values themselves; `text` names each one (an English key;
  null: `ToString`, so an enum's names are its keys). Values match by `EqualityComparer<T>.Default`: one not in the
  list shows blank and selects nothing. The values and their names are copied at build, so a list that changes later
  needs a rebuild (a `Controls.Live` region), as the int rows' `Items` do. Over a mixed `Bind.Many` the dropdown
  reads "Mixed" with no item marked and the segments select none. `DropdownOptions<T>.ItemFace` draws each item of
  the open menu in its own `FontAsset`; null or a throw draws that item in the menu's face. It runs for every item
  each time the menu opens, so fonts are not created per open: create each face once and keep it (a `Font` converts
  through `FontAsset.CreateFontAsset`, whose asset the collector never frees), as `Fonts.Mono` does:

  ```csharp
  var faces = new Dictionary<Font, FontAsset>();   // lives as long as the fonts, not the menu
  x.ItemFace = f => faces.TryGetValue(f, out var fa) ? fa : (faces[f] = FontAsset.CreateFontAsset(f));
  ```

  The int rows (`DropdownOptions.Items`/`ItemFont`, `ChoiceOptions.Items`) stay.

## Regions

- **`Controls.When(parent, shown)`** is a box whose rows show only while `shown()` is true, asked on every refresh
  pass of its surface (and after a write, in the same frame).
- **`Controls.Hook(e, configure)`** gives any element a row's options: `Tooltip`/`TooltipText`, `EnabledWhen`,
  `VisibleWhen` (asked while it is on a panel), `ContextMenu`/`ContextMenuItems`, and `Id` naming it.
- **`Controls.ToggleGroup(parent, label, bind, out toggle, configure)`** hands back the group's switch (its `Value`,
  `Changed`, `Committed`); the body still hides while it is off.
- **`Controls.Section(parent, title, o => …)`** takes `SectionOptions`: `Open` is how it starts before the reader
  folds it (default true); `Id` names the header row and keys the stored fold in place of the title, so two sections
  with one title fold apart; `VisibleWhen` hides the header and the body together, and showing it again restores the
  fold (a search hit that unfolds it meanwhile leaves the body hidden until then); `Tooltip`, `EnabledWhen` and
  `ContextMenu` go on the header. The `bool defaultOpen` overload stays.
- **`HostOptions.LabelIds`** (off by default): a value row (`Toggle`, `Slider`, `Number`, `IntSlider`, `Choice`,
  `Dropdown`, `Enum`, `Text`, `Colour`, `Key`, `Info`, `Action`, `Danger`, `DangerButton`) built with no `Id` takes its
  label as `Id`, so its history key reads `…/<label>#<label>` and its element is named by the label. The second row
  with the same label on a page takes `label#2`, the third `label#3`, skipping an `Id` an earlier row on the page
  already has (a row given `Id = "Gap"`, then an unnamed `Gap` row: `Gap#2`); an explicit `Id` is never changed, so
  give it before the label rows it could clash with. The count is per page build, per tab, and a
  `Controls.Live` rebuild counts as its first build did; rows built outside any `Nav` count on through the session,
  whatever pages are built in between. Without it an unnamed row is numbered, as before.

## Settings window

`SettingsWindow.Open(host, id, title, pages, o => …)` builds the whole window in one call:

- a Window-layer surface (shown), the `Window` on it (`id` keys its stored rect), raised when pressed (a
  `Window.Raised` handler of Facet's own; a consumer adds its own beside it);
- a `Nav` of `pages` in its body with the content inset applied (`Nav.InsetChanged += Window.SetContentInset`);
- with `Search` (default true) the title bar's search box filters through `Nav.Filter` (placeholder: the key
  `"Search"`, translated by the host, else Facet's own word);
- `Icon`, a `Background` at the start of the title bar; `CompactRail` starts the rail as its icon strip;
- `RailWidth`, the open rail's width (default 200), passed to the `Nav`: `Nav.Inset` is then `RailWidth + 7`.

**Where it opens.** An id with a stored rect (this session, or the host's `Read` of `window.<id>`) opens there
(`Window.HasStoredGeometry` is true). Otherwise the window is placed once, on the first layout of the surface while
it is visible: at a `Window.Rect` set before then (a rect carried over from another key), else at `Initial` when set,
else `InitialFraction` (default 0.8 × 0.8) of the panel, centred; each is clamped to the panel. Until then
the rect is stored nowhere: a surface hidden in the frame it opened writes nothing to `window.<id>` (nor to the
in-memory cache), so a window built ahead and never shown leaves no rect behind; its first show centres it and
stores that rect. `Tick()` (call it every frame while shown) runs `Nav.Tick`; `Dispose()` disposes the surface.
`Surface`, `Window` and `Nav` are public for everything else (`Surface.Visible`, `EscapeHandler`, `Hidden`,
`Nav.Go`, `Nav.AddSearchSource`, …); `Window` and `Nav` are null when the surface could not be created (its `Root` is
null), which the host's log reports. `Nav.Inset` is public: where the content pane starts (the open rail's width
and its 7 px handle, the handle alone with the rail shut, or the icon strip when compact).

## API in 0.2

| Area | API |
|---|---|
| Animation | `Anim.Speed` (shared); `Anim.Hover`; `Anim.SmoothScroll` (shared, default off): a wheel step glides there (OutCirc, 200 ms at speed 1) instead of jumping |
| Wheel | `Input.TickWheel`, once per frame; `HostOptions.WheelSpeed`, panel px per wheel unit, linear on the polled and the panel's own wheel path (default: Facet's own steps) |
| Fonts | `Fonts.Has`; zh and ja |
| Theme | `Role.Rail/Line/Off/Guide`; `Theme.Sizes`, `Theme.Looks`, `Theme.Notify()`; `Sizes` (with `ControlPad`, `FieldPad`, `ItemHeight`, `RowPadY` above and below a row's text, `TabGap` under a tab row); `Looks` (with `Group`, `Stage`); `Look.SelectedBorder`, `Look.Copy()` |
| Primitives | `Ui.Caps`; `Ui.Text`, `Muted`, `Caps`, `Btn` and `List` take the parent first; `Window.TitleBarLeft`; `Facet.Tokens` removed |
| Hook events | `Window.Raised`, `Nav.InsetChanged` (each handler is told the current `Nav.Inset` as it is added, then each change), `Nav.CompactChanged` |
| Cards | `ControlOptions.Help`; `Controls.Card`/`CardOptions` (`Actions`; `Note`, an English key, or `NoteText`, re-read and untranslated); `Controls.Advanced`; `Form.Build(…, FormOptions)`; `Field.Help`/`Advanced` |
| Page layout | `Controls.PageHeader`/`PageHeaderOptions`; `Controls.Columns(parent, configure)` returns a `Columns` (`Root`, `Left`, `Right`); `ColumnsOptions` (with `StackBelow`) |
| Preview | `Controls.Preview`/`PreviewOptions`: `Show`, `Tick`, `Hide` run only while its page, its window and its own `VisibleWhen` show it; `Id` names the stage, `Id-pane` the pane, which takes `VisibleWhen`, `EnabledWhen`, the tooltips and `ContextMenu` (not `Help`); `NoteText`, a live note (already translated, re-read on every refresh pass, named `Id-note`) that wins over `Note` |
| Tiles | `Controls.Tiles`/`TilesOptions` (`Id`, `VisibleWhen`, `EnabledWhen`, the tooltips and `ContextMenu` act on the grid; in a `Card` it is a padded card row); `Controls.Tile`/`TileOptions` (no `Help`: use `Tooltip`) |
| Switches | every switch (`Toggle`, `PageHeader`, `Tile`) flips on Submit (Enter/Space) as on a click |
| Tabs | `Tabs.Pages`, `Tabs.Content`; `SearchIndex.Hit.Tab` |
| Search | `Nav.AddSearchSource` with `SearchIndex.Entry` (`Label`, `Where`, `Open`, `Tip`; `Label` and `Where` are English keys, translated) for pages Facet did not build; `NavItem.OnHit` |
| Sidebar | Nav draws a rule between groups; a skin gives `Looks.Rule` a colour (on Theme.Dark it is invisible); `Nav.Compact`, an icon strip that widens over the page after a 0.1 s hover; `Nav.CompactButton`, a button at the top of the rail that flips it and raises `Nav.CompactChanged`; `Nav.Collapsible` (default true), false for a host whose setting decides whether the rail shows: the handle stops at `MinRail`, `ToggleRail` never shuts the rail, and a shut rail opens |
| Rail icons | `NavItem.Icon`, a `Background` drawn left of the entry's title and tinted as the title, hover included (default: none); `Sizes.Icon`, its square size (16) |
| Cursors | `HostOptions.Cursor`, a host hook asked first to set the OS cursor for a kind (and again every frame while held); false falls back to Facet's drawn cursor, so a hook returns false for a kind it does not know; `Cursors.Kind.Move`, `ResizeVertical`, `ResizeCornerNESW` (appended) |
| Canvas | `Controls.Canvas<T>`; `CanvasOptions<T>`: `Items`, `Key`, `Find`, `GetRect`, `SetRect`, `Size`, `Selectable`, `Movable`, `Resizable`, `Ghosts`, `Grid`, `SnapDistance`, `SizeSnap`, `MinSize`, `Limit`, `Snap`, `ShowGrid`, `ShowGuides`, `Pane`, `Menu`, `Delete`, `ZOrder`, and from `ControlOptions` `Id`, `VisibleWhen`, `EnabledWhen`, `Tooltip`/`TooltipText`, `ContextMenu` and `ContextMenuItems` (on the pane), `Undoable`, `UndoDocument` (no `Help`); `Canvas<T>`: `Root`, `Stage`, `Content`, `Selection`, `Primary`, `SelectionVersion`, `Select`, `SelectAll`, `Clear`, `Refresh`, `Snap`, `ShowGrid`, `ShowGuides`, `SelectionChanged` (also on a `Refresh` remap), `Changed`, `Committed`, `Zoom`, `Pan`, `ZoomBy`, `Fit`, `Centre`, `ResetView`, `StageToCanvas`, `CanvasToStage`, `ViewCentre`, `HitTest`, `Nudge`, `DeleteSelection`, `StepZ`, `Focused`, `Hovered`, `PollKeys`, `ViewChanged`, `Activated`; `PreviewOptions.MinHeight`, `MaxHeight` (0: none); `Sizes.CanvasHandle`, `CanvasEdgeLong`, `CanvasEdgeShort`, `CanvasHit`, `CanvasOutline`; `Role.Guide` |
| Menus | `MenuItem`; `Controls.MenuBar`/`MenuBar` (`Root`, `Left`, `Tools`, `IsOpen`, `Open`, `Close`); `Popup.Menu(anchor, IList<MenuItem>)`; `Popup.Context`; `ControlOptions.ContextMenuItems` (at the pointer, read at the right-click; also on a canvas's pane, around its stage); `Menus.HandleKeys` |
| Toolbar | `Controls.Toolbar`/`Toolbar` (`Search`, `Chips`, `Icon`, `Glyph`, `Button`, `Toggle`, `Label`, `UndoRedo`, `Spacer`); `Controls.Chips<T>`; `Ui.IconButton`; `Paint.Tint` (public) |
| Inspector | `Bind.Many` (by key), `Bind<T>.Mixed`, `Control<T>.Mixed`, `ColumnsOptions.RightFill`, `Ui.Scroll`, `Controls.Live` |
| Lists | `Controls.Reorder<T>`/`Reorder<T>` (`Root`, `Refresh`, `BeginRename`), `ReorderOptions<T>` (`Items`, `Key`, `Text`, `Selected`, `Active`, `Select`, `Move`, `Rename`, `Menu`, `Add`, `Title`, `TitleMenu`, `Vertical`, `Scroll`, `Build`) |
| Switcher and rows | `Controls.CardGrid<T>`/`CardGrid<T>` (`Root`, `Refresh`, `Redraw`, `BeginRename`), `CardGridOptions<T>` (`Items`, `Key`, `Title`, `Note`, `Active`, `Thumb`, `ThumbAspect`, `Click`, `DoubleClick`, `Menu`, `AddLabel`, `Add`, `Rename`); `Sizes.CardWidth` (168); `Controls.Repeat<T>`; `SliderOptions.Scrub`; `KeyOptions.Chord`, `Prompt`, `Mouse`; `Controls.BeginCapture`; `Input.ReadChord`, `Input.ChordCancel` |
| Dialogs | `Popup.Confirm`, `ConfirmOptions` (`Ok`, `Cancel`, null for a notice, `Danger`); `Sizes.DialogMinWidth`, `DialogMaxWidth` (280, 420) |
| Status | `Controls.Status`/`StatusLine` (`Root`, `Show(key, role, seconds)`, `Show(func, role, seconds)`, `Clear`); `InfoOptions.Tone` (`Controls.Info` takes `Action<InfoOptions>`; its `Action<ControlOptions>` overload stays, so a mod built against it still loads) |
| Window | `Window.Scale` is clamped to 0.6-2.5; `Window.HasStoredGeometry` |
| Settings window | `SettingsWindow.Open` (`Surface`, `Window`, `Nav`, `Tick`, `Dispose`), `SettingsWindowOptions` (`Initial`, `InitialFraction`, `Search`, `CompactRail`, `Icon`, `RailWidth`); `Nav.Inset` (public) |
| Regions | `Controls.When`, `Controls.Hook`, `Controls.ToggleGroup(…, out toggle, …)`, `Controls.Section(…, Action<SectionOptions>)`/`SectionOptions.Open`; `HostOptions.LabelIds` |
| Composition | `Bind<T>.Then`; `Controls.AfterChange`; a row's write or an `Action` click refreshes its surface in the same frame |
| Hooks | `Ui.Pointer`, `Ui.Every`, `Ui.WhileAttached` (delegates only); `Ui.Search`; `TextOptions.Live`, `TextOptions.Placeholder`, `TextOptions.MaxLength` |
| Conveniences | `Theme.SetAccent`; `Host.RelabelSurfaces`, `Host.DisposeSurfaces`; BCP-47 language tags; a host answering Facet's own words under `Facet:` keys (`Words.Own(english, host)`, `Words.OwnF(format, host, args)`) |
| Typed rows | `Controls.Dropdown<T>`/`DropdownOptions<T>` (`ItemFace`), `Controls.Choice<T>`/`ChoiceOptions<T>`, `Controls.Enum<TEnum>` |
| Undo | `History.Step`, `History.BoundTo`, `History.Snapshot<T>`, `History.Invalidate`, `History.AfterApply`; stale steps are skipped; a null key never coalesces; `ControlOptions.Undoable`, `ControlOptions.UndoDocument`; an undo or redo of a row's or canvas's step raises its `Committed` inside the apply (`History.Applying` true) while it is on a panel, and otherwise only writes the bind or `SetRect` |

### Conventions

- **Parent first everywhere.** Every part takes the element it is added to as its first argument, `Ui`'s text and
  list helpers included (`Ui.Text(parent, "Speed")`, `Ui.Btn(parent, "Reset", click)`).
- **Notifications are events; Escape is a `Func`.** Add a handler with `+=`, so one subscriber never drops another:
  `Window.Raised`, `Nav.InsetChanged`, `Nav.CompactChanged`, `History.Changed`, `History.AfterApply` (after each undo
  or redo, once Facet's own refresh has run; each handler under its own guard). `Surface.EscapeHandler` stays a
  `Func<bool>`, because it answers whether it took the key.
- **`Input` is the one home of the input queries** (`AnyWindowOpen`, `BlocksGameInput`, `BlocksGameKeys`,
  `PointerOverWindow`, `TextFocused`, `TickWheel`, `HandleEscape`, `ConsumeEscape`, `HandleUndo`).
- **One form of each.** These changed in place before 0.2.0 shipped, with no older form left beside them: the
  text-first `Ui.Text(text, parent)` family, `Controls.Columns(parent, out right, …)`, the fields `Window.OnRaise`,
  `Nav.OnInsetChanged`, `Nav.OnCompactChanged` and `History.AfterApply` (now an event; `History.Applied` folded
  into it) and `Ui`'s copies of the `Input` queries are gone. `Ui.OpenWindows` is read-only: Facet keeps it.
  A mod built against an earlier 0.2 build is rebuilt against this one.

## Coexistence

Several mods can load Facet at once, so the rules are fixed:

- The DLL name carries the version: `Facet.<major>.<minor>.dll`. Different minors load side by side. They share only what must be one per game (the window stack behind Escape and input, the wheel, `Anim.Speed`, `Anim.SmoothScroll`), through keys whose types never change.
- A patch release adds no public API; any new public member bumps the minor. Append-only within a minor is not safe: the first-loaded copy of a minor wins, so a mod built against a later 0.2.x could be handed an earlier one without the member. Signatures never change and enum members go last. A mod built against 0.2.0 runs on any 0.2.x.
- Ship `Facet.<major>.<minor>.dll` beside your mod, or depend on the standalone Facet mod. Never rename or merge it.
