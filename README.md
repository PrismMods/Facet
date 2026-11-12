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
`Facet.Mod/Gallery.cs` has one of every control, and `Facet.Mod/GalleryParts.cs` one of every 0.2 part.

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
stops every window's motion; 0 is instant. `Input.TickWheel` is counted once per frame: the first call
with a delta takes the frame, and the topmost open window of any version scrolls one notch.

## Languages

A host's `Language` picks the fonts: Korean (`ko`), Chinese (`zh`) and Japanese (`ja`) draw through the
OS's CJK font, loaded on first use. Two CJK languages in one process share Han glyph forms: the first one
loaded wins.

## API in 0.2

| Area | API |
|---|---|
| Animation | `Anim.Speed` (shared); `Anim.Hover` |
| Wheel | `Input.TickWheel`, once per frame |
| Fonts | `Fonts.Has`; zh and ja |
| Theme | `Role.Rail/Line/Off`; `Theme.Sizes`, `Theme.Looks`, `Theme.Notify()`; `Sizes` (with `ControlPad`, `FieldPad`, `ItemHeight`); `Looks` (with `Group`, `Stage`); `Look.SelectedBorder`, `Look.Copy()` |
| Primitives | `Ui.Caps`; `Window.TitleBarLeft`; `Facet.Tokens` removed |
| Cards | `ControlOptions.Help`; `Controls.Card`/`CardOptions` (`Actions`; `Note`, an English key, or `NoteText`, re-read and untranslated); `Controls.Advanced`; `Form.Build(…, FormOptions)`; `Field.Help`/`Advanced` |
| Page layout | `Controls.PageHeader`/`PageHeaderOptions`; `Controls.Columns`/`ColumnsOptions` (with `StackBelow`) |
| Preview | `Controls.Preview`/`PreviewOptions`: `Show`, `Tick`, `Hide` run only while its page, its window and its own `VisibleWhen` show it; `Id` names the stage, `Id-pane` the pane, which takes `VisibleWhen`, `EnabledWhen`, the tooltips and `ContextMenu` (not `Help`) |
| Tiles | `Controls.Tiles`/`TilesOptions` (`Id`, `VisibleWhen`, `EnabledWhen`, the tooltips and `ContextMenu` act on the grid; in a `Card` it is a padded card row); `Controls.Tile`/`TileOptions` (no `Help`: use `Tooltip`) |
| Switches | every switch (`Toggle`, `PageHeader`, `Tile`) flips on Submit (Enter/Space) as on a click |
| Tabs | `Tabs.Pages`, `Tabs.Content`; `SearchIndex.Hit.Tab` |
| Search | `Nav.AddSearchSource` with `SearchIndex.Entry` (`Label`, `Where`, `Open`, `Tip`; `Label` and `Where` are English keys, translated) for pages Facet did not build; `NavItem.OnHit` |
| Sidebar | Nav draws a rule between groups; a skin gives `Looks.Rule` a colour (on Theme.Dark it is invisible) |
| Rail icons | `NavItem.Icon`, a `Background` drawn left of the entry's title and tinted as the title (default: none); `Sizes.Icon`, its square size (16) |
| Window | `Window.Scale` is clamped to 0.6-2.5 |

## Coexistence

Several mods can load Facet at once, so the rules are fixed:

- The DLL name carries the version: `Facet.<major>.<minor>.dll`. Different minors load side by side. They share only what must be one per game (the window stack behind Escape and input, the wheel, `Anim.Speed`), through keys whose types never change.
- A patch release adds no public API; any new public member bumps the minor. Append-only within a minor is not safe: the first-loaded copy of a minor wins, so a mod built against a later 0.2.x could be handed an earlier one without the member. Signatures never change and enum members go last. A mod built against 0.2.0 runs on any 0.2.x.
- Ship `Facet.<major>.<minor>.dll` beside your mod, or depend on the standalone Facet mod. Never rename or merge it.
