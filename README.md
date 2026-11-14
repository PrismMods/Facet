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

// Every frame, from your mod's update: Escape closes the topmost window, and the wheel is polled
// because a runtime panel does not reliably receive WheelEvent. Both are answered once per frame
// across every mod and Facet version, so every mod can call them.
static void Update()
{
    if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Facet.Input.HandleEscape();
    Facet.Input.TickWheel(UnityEngine.Input.mousePosition, UnityEngine.Input.mouseScrollDelta.y);
}
```

The getter is re-read on every refresh, so replacing `Cfg` wholesale (a profile switch) shows through.
The host also owns the game's side of input: skip the game's keys while `Facet.Input.BlocksGameInput`
or `Facet.Input.HeldThisFrame` (the Escape that just closed a window is still down).
`Facet.Mod/Gallery.cs` has one of every control, `Facet.Mod/GalleryParts.cs` one of every 0.2 part, and
`Facet.Mod/GalleryEditor.cs` the editor parts, a page each.

## Theming a part

A host's `Theme` carries the whole look: a colour per `Role`, `Theme.Sizes` (radius, lines, padding, row
heights, type, control sizes) and `Theme.Looks` (one `Look`, a per-state recipe of roles, per part element).
Each host has its own, a copy of `Theme.Dark` unless it passes one (`o.Theme`); change it before you build.
Colours re-theme open windows: set roles and call `Notify()`, or `Recolour(accent)`. Sizes and looks apply
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
version scrolls one notch.

## Languages

A host's `Language` picks the fonts: Korean (`ko`), Chinese (`zh`) and Japanese (`ja`) draw through the
OS's CJK font, loaded on first use. Two CJK languages in one process share Han glyph forms: the first one
loaded wins.

## History

`History` is one undo stack for the whole session, by value: every value row records how to put its change
back and how to do it again, and the window header's arrows undo and redo. Changes to one key within
`CoalesceSeconds` (0.9 s) are one step, keeping the first undo and the last redo, so a slider drag is one
step; a null key never coalesces.

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
  `EnabledWhen` false also stops the stage taking the pointer. `Help` is not shown.
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
  surface when pressed** (`window.OnRaise = () => surface.BringToFront();`): otherwise a click into it leaves the
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

## API in 0.2

| Area | API |
|---|---|
| Animation | `Anim.Speed` (shared); `Anim.Hover`; `Anim.SmoothScroll` (shared, default off): a wheel step glides there (OutCirc, 200 ms at speed 1) instead of jumping |
| Wheel | `Input.TickWheel`, once per frame; `HostOptions.WheelSpeed`, panel px per wheel unit, linear on the polled and the panel's own wheel path (default: Facet's own steps) |
| Fonts | `Fonts.Has`; zh and ja |
| Theme | `Role.Rail/Line/Off/Guide`; `Theme.Sizes`, `Theme.Looks`, `Theme.Notify()`; `Sizes` (with `ControlPad`, `FieldPad`, `ItemHeight`, `RowPadY` above and below a row's text, `TabGap` under a tab row); `Looks` (with `Group`, `Stage`); `Look.SelectedBorder`, `Look.Copy()` |
| Primitives | `Ui.Caps`; `Window.TitleBarLeft`; `Facet.Tokens` removed |
| Cards | `ControlOptions.Help`; `Controls.Card`/`CardOptions` (`Actions`; `Note`, an English key, or `NoteText`, re-read and untranslated); `Controls.Advanced`; `Form.Build(…, FormOptions)`; `Field.Help`/`Advanced` |
| Page layout | `Controls.PageHeader`/`PageHeaderOptions`; `Controls.Columns`/`ColumnsOptions` (with `StackBelow`) |
| Preview | `Controls.Preview`/`PreviewOptions`: `Show`, `Tick`, `Hide` run only while its page, its window and its own `VisibleWhen` show it; `Id` names the stage, `Id-pane` the pane, which takes `VisibleWhen`, `EnabledWhen`, the tooltips and `ContextMenu` (not `Help`) |
| Tiles | `Controls.Tiles`/`TilesOptions` (`Id`, `VisibleWhen`, `EnabledWhen`, the tooltips and `ContextMenu` act on the grid; in a `Card` it is a padded card row); `Controls.Tile`/`TileOptions` (no `Help`: use `Tooltip`) |
| Switches | every switch (`Toggle`, `PageHeader`, `Tile`) flips on Submit (Enter/Space) as on a click |
| Tabs | `Tabs.Pages`, `Tabs.Content`; `SearchIndex.Hit.Tab` |
| Search | `Nav.AddSearchSource` with `SearchIndex.Entry` (`Label`, `Where`, `Open`, `Tip`; `Label` and `Where` are English keys, translated) for pages Facet did not build; `NavItem.OnHit` |
| Sidebar | Nav draws a rule between groups; a skin gives `Looks.Rule` a colour (on Theme.Dark it is invisible); `Nav.Compact`, an icon strip that widens over the page after a 0.1 s hover; `Nav.CompactButton`, a button at the top of the rail that flips it and raises `Nav.OnCompactChanged`; `Nav.Collapsible` (default true), false for a host whose setting decides whether the rail shows: the handle stops at `MinRail`, `ToggleRail` never shuts the rail, and a shut rail opens |
| Rail icons | `NavItem.Icon`, a `Background` drawn left of the entry's title and tinted as the title, hover included (default: none); `Sizes.Icon`, its square size (16) |
| Cursors | `HostOptions.Cursor`, a host hook asked first to set the OS cursor for a kind (and again every frame while held); false falls back to Facet's drawn cursor, so a hook returns false for a kind it does not know; `Cursors.Kind.Move`, `ResizeVertical`, `ResizeCornerNESW` (appended) |
| Canvas | `Controls.Canvas<T>`; `CanvasOptions<T>`: `Items`, `Key`, `Find`, `GetRect`, `SetRect`, `Size`, `Selectable`, `Movable`, `Resizable`, `Ghosts`, `Grid`, `SnapDistance`, `SizeSnap`, `MinSize`, `Limit`, `Snap`, `ShowGrid`, `ShowGuides`, `Pane`, and from `ControlOptions` `Id`, `VisibleWhen`, `EnabledWhen`, `Tooltip`/`TooltipText`, `ContextMenu` and `ContextMenuItems` (on the pane), `Undoable`, `UndoDocument` (no `Help`); `Canvas<T>`: `Root`, `Stage`, `Content`, `Selection`, `Primary`, `SelectionVersion`, `Select`, `SelectAll`, `Clear`, `Refresh`, `Snap`, `ShowGrid`, `ShowGuides`, `SelectionChanged` (also on a `Refresh` remap), `Changed`, `Committed`; `PreviewOptions.MinHeight`, `MaxHeight` (0: none); `Sizes.CanvasHandle`, `CanvasEdgeLong`, `CanvasEdgeShort`, `CanvasHit`, `CanvasOutline`; `Role.Guide` |
| Menus | `MenuItem`; `Controls.MenuBar`/`MenuBar` (`Root`, `Left`, `Tools`, `IsOpen`, `Open`, `Close`); `Popup.Menu(anchor, IList<MenuItem>)`; `Popup.Context`; `ControlOptions.ContextMenuItems` (at the pointer, read at the right-click; also on a canvas's pane); `Menus.HandleKeys` |
| Toolbar | `Controls.Toolbar`/`Toolbar` (`Search`, `Chips`, `Icon`, `Glyph`, `Button`, `Toggle`, `Label`, `UndoRedo`, `Spacer`); `Controls.Chips<T>`; `Ui.IconButton`; `Paint.Tint` (public) |
| Window | `Window.Scale` is clamped to 0.6-2.5 |
| Undo | `History.Step`, `History.BoundTo`, `History.Snapshot<T>`, `History.Invalidate`; stale steps are skipped; a null key never coalesces; `ControlOptions.Undoable`, `ControlOptions.UndoDocument`; an undo or redo of a row's or canvas's step raises its `Committed` inside the apply (`History.Applying` true) while it is on a panel, and otherwise only writes the bind or `SetRect` |

## Coexistence

Several mods can load Facet at once, so the rules are fixed:

- The DLL name carries the version: `Facet.<major>.<minor>.dll`. Different minors load side by side. They share only what must be one per game (the window stack behind Escape and input, the wheel, `Anim.Speed`, `Anim.SmoothScroll`), through keys whose types never change.
- A patch release adds no public API; any new public member bumps the minor. Append-only within a minor is not safe: the first-loaded copy of a minor wins, so a mod built against a later 0.2.x could be handed an earlier one without the member. Signatures never change and enum members go last. A mod built against 0.2.0 runs on any 0.2.x.
- Ship `Facet.<major>.<minor>.dll` beside your mod, or depend on the standalone Facet mod. Never rename or merge it.
