# Facet

Facet is a UI engine for Unity mods, built on UI Toolkit: one shared kit of windows, controls, theming, navigation and search, so that a mod's settings screens are a page of data and calls instead of a framework of its own. It ships as a standalone mod and targets any Unity game on Mono, and later IL2CPP.


## Install

Download `Facet-ML.zip` or `Facet-UMM.zip` (built by `./release.sh` into `dist/`) and unzip it into the game folder.

- **MelonLoader:** `Facet-ML.zip` holds `UserLibs/Facet.0.1.dll` and `Mods/Facet.Mod.dll`. Press F8 in game for the Gallery.
- **UnityModManager:** `Facet-UMM.zip` holds `Facet/` (`Facet.Mod.dll`, `Facet.0.1.dll`, `Info.json`), which goes under `Mods/` (or `UMMMods/` on the Mac/Steam layout).

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
// because a runtime panel does not reliably receive WheelEvent.
static void Update()
{
    if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Facet.Input.HandleEscape();
    Facet.Input.TickWheel(UnityEngine.Input.mousePosition, UnityEngine.Input.mouseScrollDelta.y);
}
```

The getter is re-read on every refresh, so replacing `Cfg` wholesale (a profile switch) shows through.
The host also owns the game's side of input: skip the game's keys while `Facet.Input.BlocksGameInput`
or `Facet.Input.HeldThisFrame` (the Escape that just closed a window is still down).
`Facet.Mod/Gallery.cs` has one of every control.

## Coexistence

Several mods can load Facet at once, so the rules are fixed:

- The DLL name carries the version: `Facet.<major>.<minor>.dll`. Different minors load side by side and never share state.
- Within a minor the API is append-only: enum members go last, signatures never change. A mod built against 0.1.0 runs on any 0.1.x.
- Ship `Facet.<major>.<minor>.dll` beside your mod, or depend on the standalone Facet mod. Never rename or merge it.
