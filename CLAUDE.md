# Facet

A game-agnostic UI engine for Unity mods, on runtime UI Toolkit. The landmines below were found in
PrismLib.UI (source: PrismLib commit db70474) and bind every change here. This file may name the
game where a landmine was verified; nothing under `Facet/` may (`test.sh` greps for it).

## UI Toolkit at runtime

Unity 6000.3 verified in game (2026-09-24): `ScriptableObject.CreateInstance<PanelSettings>()` + an
empty `ThemeStyleSheet` + `UIDocument` gives a live `rootVisualElement`. The real constraint: **USS
and UXML cannot be authored at runtime** (the importer is editor-only), so styling is inline C# and
theme values live in code (`Theme`).

- **Never show/hide a panel with `SetActive`.** `UIDocument` rebuilds its visual tree in `OnEnable`,
  so the second show hands back a different `rootVisualElement` and everything built into the old
  one is orphaned: the panel comes back empty with no error. Use `display`.
- **Before Unity 6 a font does not inherit.** This is Unity 2022 behaviour as PrismLib.UI met it,
  unverified in any game Facet has run on. A font definition set on the root never reached the labels: each resolved no font and laid out 0px tall, so the window drew
  shapes and no text. Set per element, both the SDF asset and a legacy Font draw.
  `Surface.HandDownFonts` stamps the nearest ancestor's font onto every text element on a 200ms
  schedule there (rows recycle, popups add elements later). Unity 2022 also has no 5-arg SDF family
  overload, so on macOS the font comes from files (Arial, Menlo, which also carries the panel icons
  Arial lacks, hence it is the sans face's fallback).
- **A panel with no font draws no text.** `TMP_FontAsset.sourceFontFile` is an editor-time reference
  and is null in a player build, so a mod generally cannot supply a `Font`; `Surface` falls back to
  `Font.CreateDynamicFontFromOSFont` (on demand, not safe at `Time.frameCount == 0`).
- **Fonts load from FILES, then by family name** (`Surface.FileFontAsset`). A family+style FontAsset
  only works while TextCore can find that family again: it reloads the face by name whenever it adds
  glyphs, and on Steam Linux Runtime that reload asks for "DejaVu Sans" / "Regular", which
  fontconfig's "Book"-tagged DejaVu does not answer to. The asset built fine, then every label drew
  nothing. Every panel also gets one shared `PanelTextSettings` whose default font is ours and whose
  fallback list is empty; left null, UI Toolkit falls back to a built-in default that does the same
  failing OS lookup.
- **Fonts are sampled at 64px, not at the size they draw.** 14-16px SDF masters with 9px padding were
  magnified by the panel scale (1.4 at 1440p) and a Retina backing scale, and the text came out
  blurry; 64/6 fixed it (confirmed in game 2026-11-05). Keep padding near a tenth of the size.
- **Bold is a real face, not faux bold.** The bold file is attached as weight 700
  (`fontWeightTable[7].regularTypeface`); without it UI Toolkit dilates the regular SDF glyphs, which
  came out smeared on every header.
- **A scrolling container's ANCESTORS must be allowed to shrink.** `Window.Body` and `Nav`'s panes
  set `flexShrink = 1` back on themselves, against the `Ui.Box` default, because they have to become
  whatever the window is rather than whatever their content is. Left at 0, the body grew to its
  content and the window merely clipped it: a log's viewport came out 2700px tall inside a 580px
  window, so the list reported that everything fitted and nothing scrolled. `minHeight = 0` alone
  is not enough; both are needed.
- **Flex items shrink by default, and `Ui.Box` turns that off.** CSS `flexShrink: 1` would rather
  squash than overflow; a settings page wants the opposite: something too tall should SCROLL, not
  compress until its text prints through the row below. Three overlaps came from it, so every
  `Ui.Box` and everything built on it defaults to `flexShrink = 0`. Anything that should absorb slack
  sets it back to 1 on itself. A `ScrollView`'s `contentContainer` is not a `Box`, so set it by hand.
- **A Button or Label that gains a child loses its text measure.** UI Toolkit's `Hierarchy.Insert` removes
  the measure function, so the box collapses to its padding (a 13px label came out 12px tall and a long item
  overflowed instead of widening it). Never add a decoration (a tab's underline, a dropdown's arrow) to a
  text element: wrap it in a `Ui.Box` cell and pin the decoration in the cell. A check is `MeasureTextSize`
  against `contentRect` (the tab-row and dropdown autotests).
- **A flex item's minimum size is its CONTENT, which silently disables scrolling.** A list taller
  than the window makes its container taller than the window too, so the window just clips it: no
  scrollbar, no wheel, nothing in any log. Every scroll container AND every ancestor up to the
  fixed-height window needs `minHeight = 0` (`Window.Body`, the `ScrollView`, the `ListView`).
  An absolutely positioned element with only `left`/`top` is sized by its CONTENT: pin all four
  offsets.
- **Nothing clips without the theme.** The default style sheet is what gives a `ScrollView`'s
  viewport `overflow: hidden`; at runtime, scrolled rows drew over the search box and title bar.
  `Skin.Scroll` sets it on the view and its `contentViewport`, so build every `ScrollView` through
  it. A single-line `TextField` cuts long text off at its width: long read-only text goes in an
  `Info` row, a wrapped label. Verified 2026-11-06.
- **Built-in controls have NO appearance.** UI Toolkit styles `Slider`, `TextField` and `ScrollView`
  from the default theme style sheet, an asset a runtime mod cannot have. `Skin` sets inline styles
  on their named parts (`unity-base-slider__tracker`, `unity-base-slider__dragger`,
  `unity-base-text-field__input`). Those names are Unity's: a rename makes a control invisible
  again, so every lookup is null-checked, and `Skin.When` waits for `AttachToPanelEvent` because the
  parts do not exist until the control joins a panel. Both a slider's tracker AND its dragger are
  laid out by the default style sheet, so each is placed by hand against the drag container's
  midline; fixing only the dragger moves the mismatch instead of removing it.
- **Drawn by hand rather than using UI Toolkit's own:** `Toggle` (its `Toggle` is a tick box),
  `Choice` (its `DropdownField` menu) and `Colour`. The first two are styled by the theme style
  sheet a runtime mod does not have, so they would come out unstyled.
- **The first window drawn in a session costs ~85 ms more than any after it**: the first run of the
  window, widget and UI Toolkit code, not glyphs (with every glyph pre-rasterised it was still 91 ms;
  a second window took 17). So the first surface schedules `WarmUp.Run`: one throwaway window drawn
  off screen for 100 ms, half a second after load. Measured 2026-11-06. It builds one of each control
  and of each 0.2 part (header, columns, card with Advanced, preview, tile, tab pages): without the
  parts, a suite page opened straight after it took 19-42 ms against 11-18 ms warm (2026-11-07). It runs
  once per minor and session, from whichever mod's surface comes first. Unity's own UI Toolkit code is
  warmed by the first Facet of ANY minor (0.2's warm-up took 34 ms after 0.1's 185 on the rig), so with
  0.1 mods loaded the 0.2 figures understate a 0.2-only player's first open.
- **Cursors are drawn, and the swap is GLOBAL.** USS `cursor` keywords are editor-only and a runtime
  mod has no cursor assets, so `Cursors` draws them in code and swaps through
  `UnityEngine.Cursor.SetCursor` on pointer enter. There is no per-element cursor at runtime, so
  every element that sets one must clear it on exit AND on `DetachFromPanelEvent`: a panel hidden
  while the pointer is over it would otherwise strand the cursor. Only the ambiguous ones get one
  (a resize edge, a text field).

## Wheel and scrolling

`WheelEvent` delivery differs BETWEEN panels, so the question is per frame, not ever. A single "the
host sends wheel events" flag, set by the first panel to see one, switched polling off for a list
that never got them. `Ui.TickWheel` stands down only on frames where one actually arrived.
**The wheel is counted once per frame across mods and versions** (0.2): the first `Input.TickWheel`
with a delta stamps the frame (`Facet.WheelStamp`, an `int[1]` frame number: a float loses frames past
2^24) and the topmost visible window of any version scrolls through its Coord slot 9. A zero delta (a
mod whose read was gated) leaves the frame unspent; a 0.1 window on top has no slot 9 and takes the
frame by doing nothing.
`Skin.WatchWheel` registers with `TrickleDown` because a `ScrollView` that handles the wheel stops
it bubbling. Three causes hid behind "scrolling is broken", all found by logging numbers:

- **A trackpad reports fractions.** The measured delta was `-0.05`; at 40px per unit that moved the
  page two pixels, which looks exactly like nothing.
- **`RuntimePanelUtils.ScreenToPanel` divides by the panel scale and does NOT flip y.** Screen
  `(1606,300)` came back as panel `(1141,213)`, exactly `/1.405`. The flip belongs to the caller.
- **A virtualised `ListView` never grows its content container**, so measuring
  `contentContainer.height` says a 53-row log has nothing to scroll. Ask the list for its item count.

## State and text

- **Undo/redo (`History`) is session-wide and by VALUE.** Every widget records how to put its change
  back and how to do it again; nothing snapshots a settings object. `History.Applying` must suppress
  recording while an undo runs, or the widget's own change handler pushes a fresh entry and undo
  becomes inescapable. Consecutive changes to the same key within `CoalesceSeconds` merge, keeping
  the FIRST undo and the LAST redo, so a slider drag is one step. `History.AfterApply` is how a
  panel re-reads what it shows. `History.Clock` is injectable so the offline tests set their own.
- **Window geometry** persists through the Host's `Read`/`Write` (`HostOptions.Read`/`Write`,
  host-supplied delegates: Facet cannot own a file). Hosts flush at most once a second because a drag writes every frame, and keep
  it out of the real settings file: a mid-write crash must not corrupt settings.
- **Keybind capture is polled by the HOST, not read from a UI event.** Tab, the arrows and Escape are
  all worth binding and all get eaten as navigation inside a panel.
- **A window's `Theme` carries its colours, sizes and looks; `Facet.Tokens` is gone (0.2).** An
  element records the ROLE it draws with (`Paint.Bg/Fg/Border/Look`), never a `Color`, so
  `Theme.Recolour` (or role writes then `Notify`) re-themes open windows: each surface wearing the theme
  re-applies (`Paint.Apply`) inside `Safe`. `Sizes` and `Looks` are read when a part builds
  (`Theme.For(e).Sizes/.Looks`, once per build), so a change shows on the next build. `Theme.For`
  is the nearest themed ancestor (a Surface, a popup card that took its anchor's), else the
  `Theme.Use` scope, else the host's theme. `Theme.Dark` is ONE frozen instance shared by every mod:
  its roles refuse writes, and its `Sizes`/`Looks` getters return a fresh copy per read, so a write to
  `Theme.Dark.Sizes` goes nowhere. A copy is 23 Looks: never read them on a per-frame, drag, refresher,
  tick or bind path. A host without a theme gets its own copy, and only `Host.Fallback` wears Dark
  itself. An element painted before it joins the tree is repainted on `AttachToPanelEvent`.
- **`ColumnsOptions.StackBelow`** (default 560) stacks `Controls.Columns` below that width, with 40 px hysteresis
  (back to columns at StackBelow + 40).
- **`Sizes.ControlPad`, `FieldPad` and `ItemHeight`** are the control, field and list-row paddings/heights; read at build like the rest of `Sizes`.
- **The first re-theme of a session is cold** (about 7-10 ms, within the 16 ms budget, not warmed); later ones take about 0.5 ms.
- **`Looks.Card` is the window shell and popups, `Looks.Group` a `Controls.Card` body, `Looks.Stage` a
  preview's stage**, so a skin can put cards on a fill of their own. Dark keeps 0.1's look (within 1px):
  `Looks.Rule` is invisible by default (`new Look()`) because 0.1 drew no rules, so Dark shows no card
  rules (header or rows), no nav group rule and no tab row rule; a skin gives Rule a colour (its `Bg`; a
  card row's rule is a `Sizes.Line` top border in it, and none at all while Rule has no `Bg`).
- **`Look` presets are copies.** `Look.Button` and the rest return a fresh `Look` on every read, so a
  mod restyling the one it was handed restyles nothing else. A part never paints a preset: it takes
  the theme's `Looks`, or a skin could not restyle it.
- **`Paint.Forget(e)` is the opt-out.** Paint re-applies recorded roles on attach, hover, focus and
  re-theme, so a literal colour set on a Facet-built element is reverted; after `Paint.Forget` it sticks.
- **There is no `:hover` at runtime; `Paint` tracks hover, pressed and focus.** A `Button`'s
  clickable stops the mouse's `PointerDownEvent` before any bubbling callback, so Paint registers
  pointer down/up with `TrickleDown`, which Unity 6 runs first (verified 2026-11-06). In a synthetic
  test, read a pressed colour in the same frame: the panel sends a `MouseLeaveEvent` next frame
  because the real pointer is elsewhere, and leave clears pressed.
- **Text is stored as its English key and translated through the element's Host** (`Words.Label`,
  `Words.Tip`; `Ui.Text`, `Ui.Btn` and row labels call them), so page code never translates. **A
  string is translated only by the table of whoever wrote it**: English keys carry no context, so one
  mod's "Undo" (remove a camera patch) once came out as the editor's "실행 취소". The host that
  answers is `Host.For(element)`, re-checked when the element joins a panel. Facet's own literals go
  through `Words.Own`/`OwnF` with the host's language, never through a host's table. Relabel is
  live: `Surface.Relabel` re-translates every keyed text in place after a language switch.
  A duplicate key in the table's initializer throws from the static constructor and takes every
  string with it: check with `grep -o '{ "[^"]*",' Facet/Core/Words.cs | sort | uniq -d`.

## Parts (0.2)

- **Options are named after their part** (`PageHeaderOptions`, `CardOptions`, `ColumnsOptions`,
  `PreviewOptions`, `TilesOptions`, `TileOptions`, `FormOptions`; Ruling Q26), and text options come in
  pairs: an English key (`Note`, `Description`, `Tooltip`) that is translated and relabels, and a `…Text`
  func re-read on refresh and shown as returned, which wins over the key.
- **`Anim.Speed` is shared by every Facet from 0.2 on** (`Facet.AnimSpeed`; 0.1 keeps its own field), so
  one suite setting moves every window. The next fold, tab switch and hover use the new speed; 0 leaves
  the end state in the same call.
- **Tab pages build fresh.** `Tabs.Pages` builds only the selected page, anew on every select like a Nav
  page, and REMOVES the page it replaces (not `display:none`), so what it ran (a preview) stops. While a
  search index builds, every visible tab page is built for it, and a hit carries its tab (`Hit.Tab`).
  The picked tab is kept under `tab.<page>#<n>`, n the row's index among the Pages rows on its page (not
  counting rows inside another row's tabs), so two rows on a page, or rows outside a Nav, never share one.
- **A preview costs nothing while hidden.** `Show` runs when its stage is on a panel, its pane is not
  hidden by its own `VisibleWhen` (the pane, `Id-pane`, is a Control like a card; the stage is `Id`) and
  its surface is visible, `Tick` while that holds (every frame at `EveryMs` 0, allocation-free), `Hide`
  once when any of them stops, the surface is disposed included. All under `Safe`; three `Tick` throws in
  a row pause it until the next `Show`. Known gap: a `display:none` ANCESTOR inside a shown page (a
  `VisibleWhen`-hidden card, a folded Section) still counts as shown.
- **A tile grid in a card is a card row** (`Sizes.Pad` at the sides, `Gap` on top, the next row ruled);
  tile widths come from the grid's `contentRect`. `Help` is dropped on a tile (fixed height, clipped).
- **`Controls.Switch` takes the toggle action** and answers both `ClickEvent` and `NavigationSubmitEvent`,
  so every switch built on it is keyboard-operable; a tile's track is not focusable (the tile is the stop).
- **Search reaches pages Facet did not build.** `Nav.AddSearchSource` returns `SearchIndex.Entry`s
  (`Label`, `Where`, `Open`, `Tip`), asked each time the index builds; choosing one runs `Open`. `Label`
  and `Where` are English keys, shown through the host's table (unknown text shows as given).
  `NavItem.OnHit` runs once a hit lands on that page or tab.

## Layers, popups, Escape

- `Surface` takes a `Layer` (Hud, Window, Popup, Modal, Tooltip); each owns a band of
  `PanelSettings.sortingOrder`, so raising a window never lifts it over an open popup. Separate
  `UIDocument` panels order by that number and nothing else. `Ui.Stack` is in (layer, raise) order.
  A uGUI canvas sorted higher than the panel still draws on top.
- **Popups** share one Popup-layer surface, sit under their anchor (above when there is no room),
  take the anchor's window scale, and close on an outside click (a transparent catcher under the
  card), on Escape, when their window hides, or when the anchor leaves the panel. Place from the
  PANEL's size: a just-shown surface has no layout yet.
- **Escape is answered once** (`Ui.HandleEscape`): the topmost visible surface takes it. A focused
  text field in it blurs first, then its `EscapeHandler` (the popup host closes only its newest
  popup), else it hides. At most once per frame, because every mod polls Escape; a mod that spent
  the key on a keybind capture says so with `Ui.ConsumeEscape`.
- **The key that closes a window is still down for the rest of that frame.** A hiding surface calls
  `Input.HoldThisFrame()` so the host keeps it from the game, or Escape closed the window and then
  paused the game behind it.

## Dev loop and packaging

- `./test.sh`: offline tests plus the grep rules (no loader or game names in `Facet/`, no `RegisterCallback`
  outside `Events.cs`, no Unity-derived public types, colour only through `Paint` except the pickers'
  swatches and a tile's data colour, no size literal in a part (`SetRadius(e, 4f)`, `fontSize = 13`),
  and no `Look` preset painted by a part).
- `./build.sh`: tests, then installs the UMM build to `UMMMods/Facet`. `tools/ingame.sh` runs the autotest
  through it and wants `AUTOTEST DONE ... fail=0`. `tools/ingame.sh --ml` does the same through the
  MelonLoader entry (log and trigger in `UserData/Facet/`): unzip `dist/Facet-ML.zip` into the game first.
- `./release.sh` builds both targets (`-p:LoaderTarget=ML|UMM`), stages `dist/Facet-ML.zip` and
  `dist/Facet-UMM.zip` and fails unless the entries match the expected lists. It publishes nothing.
- Rig: one in-game test at a time; wait about 5 s after a quit; `ingame.sh` refuses to run while another
  suite's trigger (LayoutProbe `autotest`, CamDisplay `selftest`) is armed. Never run the UMM and ML copies
  together: remove one before installing the other, and `./build.sh` afterwards to restore the default.
- Packaging rule: only `Facet.Mod/` names a loader. The assembly is `Facet.<major>.<minor>.dll`, shipped
  beside every consumer or via the standalone mod. A patch release adds no public API; any new public
  member bumps the minor (R21: append-only within a minor is unsafe because the first-loaded copy of a
  minor wins).
- Cross-version shared state (`Shared`, `Coord`'s entries): a key's value type never changes, array slots
  are only appended, and a reader checks `Length` before reading a slot. 0.2 added `Facet.AnimSpeed`
  (`float[1]`), `Facet.WheelStamp` (`int[1]`) and Coord slot 9 (the wheel); a bad shared value falls back
  to a private one and is logged once, never kills motion or input.

## Rulings that shaped the code

- Facet targets netstandard2.1 (Unity 2022.3's reference assemblies reference it).
- Colours are per host (`Theme`); a host without a theme gets a copy, `Theme.Dark` itself is frozen.
- R13: a bound control is "being edited" (skipped by the refresher) while a text field in its row has focus
  or anything in the row holds pointer capture, not merely while its own input has focus.
- R14: `History.Apply` calls an internal Facet-only refresh hook before the public `AfterApply`, so a
  consumer assigning `AfterApply` cannot clobber Facet's in-place refresh.
- R21: a patch release adds no public API; any new public member bumps the minor.
- **Two CJK languages in one process share Han glyph forms.** `Fonts.ForLanguage` appends one OS CJK
  face per script to a face set's single fallback table, and Chinese, Japanese and Korean share Han
  code points, so the first-loaded file's forms win (zh then ja draws kanji in Chinese shapes; ko first
  draws zh/ja Han in Korean ones). One language per process is exact. Per-script face sets would fix
  it; not built.
- **PingFang is not where the font table looks on macOS 14+.** It lives under
  `/System/Library/AssetsV2/<hash>/`, not `/System/Library/Fonts/PingFang.ttc`, so zh falls through to
  Hiragino Sans GB there (verified on macOS 15).
