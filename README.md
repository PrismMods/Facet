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

## API in 0.2

| Area | API |
|---|---|
| Animation | `Anim.Speed` (shared); `Anim.Hover`; `Anim.SmoothScroll` (shared, default off): a wheel step glides there (OutCirc, 200 ms at speed 1) instead of jumping |
| Wheel | `Input.TickWheel`, once per frame; `HostOptions.WheelSpeed`, panel px per wheel unit, linear on the polled and the panel's own wheel path (default: Facet's own steps) |
| Fonts | `Fonts.Has`; zh and ja |
| Theme | `Role.Rail/Line/Off`; `Theme.Sizes`, `Theme.Looks`, `Theme.Notify()`; `Sizes` (with `ControlPad`, `FieldPad`, `ItemHeight`, `RowPadY` above and below a row's text, `TabGap` under a tab row); `Looks` (with `Group`, `Stage`); `Look.SelectedBorder`, `Look.Copy()` |
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
| Cursors | `HostOptions.Cursor`, a host hook asked first to set the OS cursor for a kind (and again every frame while held); false falls back to Facet's drawn cursor |
| Window | `Window.Scale` is clamped to 0.6-2.5 |
| Undo | `History.Step`, `History.BoundTo`, `History.Snapshot<T>`, `History.Invalidate`; stale steps are skipped; a null key never coalesces; `ControlOptions.Undoable`, `ControlOptions.UndoDocument`; an undo or redo of a row's step raises its `Committed` while the row is on a panel |

## Coexistence

Several mods can load Facet at once, so the rules are fixed:

- The DLL name carries the version: `Facet.<major>.<minor>.dll`. Different minors load side by side. They share only what must be one per game (the window stack behind Escape and input, the wheel, `Anim.Speed`, `Anim.SmoothScroll`), through keys whose types never change.
- A patch release adds no public API; any new public member bumps the minor. Append-only within a minor is not safe: the first-loaded copy of a minor wins, so a mod built against a later 0.2.x could be handed an earlier one without the member. Signatures never change and enum members go last. A mod built against 0.2.0 runs on any 0.2.x.
- Ship `Facet.<major>.<minor>.dll` beside your mod, or depend on the standalone Facet mod. Never rename or merge it.
