using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;

namespace Facet
{
    /* A UI Toolkit panel created entirely at runtime.

       The game is Unity 6000.3 and ships UIElementsModule, so UI Toolkit is available to mods:
       retained-mode elements, flexbox layout, and ListView's row recycling, none of which the uGUI
       framework in Topaz and Sapphire gets without hand-rolling it.

       What is NOT available is the authoring half. USS and UXML are imported by the editor, so a
       mod cannot compile a stylesheet at runtime — every style here is set in C#: colours by
       role (Theme, Paint), sizes as Tokens. That is also why themeStyleSheet gets an empty instance and
       disableNoThemeWarning is set: a panel with no theme renders fine, it just has no built-in
       control styling, which we do not want anyway since we style our own elements. It does log one
       "no theme style sheet" warning per panel — disableNoThemeWarning exists but is not public.

       One Surface per window. Sorting order decides what sits above what; the update toast's uGUI
       canvas is at 32700, so panels stay well below it. */
    /* Hud draws under every window and is not a window: never in Ui.Stack, never counted as open,
       never closed by Escape, never withholding input — a readout like the FPS counter. */
    public enum Layer { Hud, Window, Popup, Modal, Tooltip }

    public sealed class Surface : IDisposable
    {
        private GameObject _go;
        private PanelSettings _settings;
        // Re-reads the bound controls under Root while visible (Control finds it through Data).
        private Refresher _refresher;

        public VisualElement Root { get; private set; }

        public Layer Layer { get; }

        /// The consumer this surface was made for; everything under Root finds it with Host.For.
        public Host Host { get; }

        private void L(string m) { Host.Log(m); }

        public string Name { get; }

        private Theme _theme;
        /// This surface's colour scheme: the Theme.Use scope it was made in, else its host's. Never
        /// null. Replacing it, or recolouring it, redraws everything already built (Paint.Apply).
        public Theme Theme
        {
            get { return _theme; }
            set
            {
                value = value ?? Host.Theme;
                if (ReferenceEquals(value, _theme)) return;
                _theme.Changed -= Retheme;
                _theme = value;
                if (Root == null) return;   // disposed, or never drew
                _theme.Changed += Retheme;
                Theme.Set(Root, value);
                Retheme();
            }
        }

        /* Inside Safe, so one surface's failure leaves the others wearing the scheme re-themed.
           The [perf] line is throttled per surface: dragging an accent picker re-themes every frame. */
        private void Retheme()
        {
            var root = Root;
            if (root == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Safe.Run(Host, "Surface.Retheme", () => Paint.Apply(root));
            if (Throttle.Allow(Host.Name + "/retheme/" + Name, Time.realtimeSinceStartupAsDouble))
                Host.Log("[perf] retheme " + Name + " " + sw.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
        }

        /// While visible, keep keys and the wheel away from the game (Input). On by default:
        /// a window you are typing or scrolling in should not also drive the world behind it.
        public bool BlocksGameInput { get; set; } = true;

        /* Called instead of hiding when Escape reaches this surface. The popup host uses it to
           close only its newest popup rather than every one at once. Return true when consumed. */
        internal Func<bool> EscapeHandler;

        /// Raised when the surface hides or is disposed — a popup closes with the window it came from.
        internal event Action Hidden;

        /// name is used for the GameObject, so it shows up in a hierarchy dump.
        public Surface(Host host, string name, Layer layer, UnityEngine.TextCore.Text.FontAsset font = null)
        {
            Host = host ?? Host.Fallback;
                        Layer = layer;
            Name = name;
            L("Surface '" + name + "': creating UI Toolkit panel");
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = name + "PanelSettings";
            // Empty rather than null: the panel works either way, but a null theme logs a warning
            // every time and there is no asset to point at from inside a mod.
            _settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution = new Vector2Int(1920, 1080);
            _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _settings.match = 0.5f;
            _settings.textSettings = Fonts.TextSettings(Host, font);

            // Starts visible (as the GameObject does), so it counts as open until hidden — the
            // count used to start one short, and the first hide took OpenWindows negative.
            if (Layer != Layer.Hud) Ui.OpenWindows++;
            else BlocksGameInput = false;
            BringToFront();
            _go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(_go);
            var doc = _go.AddComponent<UIDocument>();
            doc.panelSettings = _settings;
            Root = doc.rootVisualElement;
            if (Root != null) Host.Attach(Root);
            // Built inside Theme.Use: keep that scheme, or the panel's later pages fall back to the host's.
            _theme = Theme.Building ?? Host.Theme;
            if (Root != null) { _theme.Changed += Retheme; Theme.Set(Root, _theme); }
            /* Null here is the failure that looks like nothing happening: the panel exists, the
               hotkey "works", and not one element is ever drawn. Worth its own line. */
            L("Surface '" + name + "': root " + (Root == null ? "NULL — nothing will render" : "ok")
                   + ", font " + (font == null ? "none (text may be invisible)" : font.name));
            if (Root != null)
            {
                Root.style.flexGrow = 1f;
                // The panel covers the screen; only what we draw inside it should catch the mouse,
                // or the root would swallow every click in the game.
                Root.pickingMode = PickingMode.Ignore;
                // UI Toolkit draws through TextCore, not TMP: FontDefinition wants a
                // TextCore FontAsset, and TMP_FontAsset is NOT one. A mod that only has the TMP
                // asset should pass the plain Font it was built from instead — see SetFont.
                Skin.WatchWheel(Root);
                if (font != null)
                {
                    Fonts.Apply(Root, font);
                    L("Surface '" + name + "': SDF font " + font.name);
                }
                else SetFont(null);   // never leave a panel fontless; see Fallback
                Fonts.ForLanguage(Host);
                _refresher = Data.Get<Refresher>(Root);
                _refresher.Start(this);
                if (!WarmUpStarted)
                {
                    WarmUpStarted = true;   // before scheduling: the warm-up window is a surface too
                    var first = Host;
                    Root.schedule.Execute(() => WarmUp.Run(first)).StartingIn(500);
                }
            }
        }

        /// Re-translates every keyed text under Root (labels, buttons, tooltips, the title) in place.
        public void Relabel() { if (Root != null) Words.Relabel(Root); }

        /// Re-reads every bound control under Root now rather than at the next tick. Undo and redo
        /// do this for every visible surface.
        public void Refresh() { if (_refresher != null) _refresher.Now(); }

        /// Set by the first surface, which schedules WarmUp.Run.
        internal static bool WarmUpStarted;

        /* Fallback for hosts that only have a legacy Font. Passing null is fine and normal: a mod
           holding a TMP_FontAsset usually CANNOT produce one, because TMP_FontAsset.sourceFontFile
           is an editor-time reference and comes back null in a player build. That is what happened
           the first time this panel opened, and a panel with no font draws no text at all. */
        public void SetFont(Font font)
        {
            if (Root == null) { L("Surface: SetFont ignored, no root"); return; }
            if (font != null)
            {
                Root.style.unityFontDefinition = FontDefinition.FromFont(font);
                Fonts.KeepHanded(Root);
                L("Surface: font " + font.name);
                return;
            }
            var fa = Fonts.Regular(Host);
            if (fa == null) { L("Surface: NO FONT AVAILABLE — labels will be blank"); return; }
            Fonts.Apply(Root, fa);
            L("Surface: font " + fa.name + " (OS fallback)");
        }

        /* Shown and hidden with display, NOT by deactivating the GameObject.

           UIDocument rebuilds its visual tree in OnEnable, so a SetActive(false)/SetActive(true)
           cycle hands back a DIFFERENT rootVisualElement and everything built into the old one is
           orphaned — the panel reappears empty, with no error anywhere. Keeping the document
           enabled and flipping display costs nothing: a hidden subtree is not laid out or drawn. */
        private bool _visible = true;

        public bool Visible
        {
            // Backed by a field rather than read back from style.display: an unset StyleEnum
            // compares unequal to None, which would report a brand-new panel as visible.
            get { return _visible; }
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (Layer != Layer.Hud) Ui.OpenWindows += value ? 1 : -1;
                if (value) BringToFront();
                if (Root != null) Root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (_refresher != null) _refresher.Show(value);   // hidden reads nothing
                if (!value)
                {
                    if (BlocksGameInput) Input.HoldThisFrame();
                    RaiseHidden();
                }
            }
        }

        private void RaiseHidden()
        {
            var h = Hidden;
            if (h == null) return;
            Safe.Run(Host, "Surface.Hidden", h);
        }

        /// One-shot geometry report: proves layout actually ran and where the element landed.
        /// Resolved styles are NaN until the first layout pass, so this waits for the event.
        public static void LogGeometryOnce(VisualElement e, string label)
        {
            if (e == null) return;
            EventCallback<GeometryChangedEvent> cb = null;
            cb = _ =>
            {
                var r = e.worldBound;
                Host.Fallback.Log(label + ": laid out at " + Mathf.RoundToInt(r.x) + "," + Mathf.RoundToInt(r.y)
                       + " size " + Mathf.RoundToInt(r.width) + "x" + Mathf.RoundToInt(r.height)
                       + " (screen " + Screen.width + "x" + Screen.height + ")");
                Events.Off(e, cb);
            };
            Events.On(e, cb);
        }

        /* Raise above everything else in this surface's layer. sortingOrder is what orders separate
           UIDocument panels, so raising means taking a number above the highest in use — a counter
           rather than a swap, because two panels sharing a number order arbitrarily.

           Each layer owns a band of numbers, so a window raised a thousand times still sits under
           an open popup, and nothing climbs into the update toast's canvas at 32700. A band that
           runs out renumbers its surfaces in stacking order. Ui.Stack is kept in the same order
           (layer, then raise), so walking it backwards always meets the topmost surface first. */
        public void BringToFront()
        {
            if (Layer == Layer.Hud) { if (_settings != null) _settings.sortingOrder = _bandBase[0]; return; }
            Ui.Stack.Remove(this);
            int at = Ui.Stack.Count;
            while (at > 0 && Ui.Stack[at - 1].Layer > Layer) at--;
            Ui.Stack.Insert(at, this);

            int b = (int)Layer;
            if (_bandNext[b] + 1f >= _bandBase[b] + _bandSpan[b])
            {
                // Park this surface above the band so the renumber (all versions) ranks it topmost.
                Order = _bandBase[b] + _bandSpan[b];
                _bandNext[b] = Coord.Renumber(Input.Entries, b, _bandBase[b], Host.Fallback.LogFn);
            }
            else if (_settings != null) _settings.sortingOrder = _bandNext[b] += 1f;
        }

        private static readonly float[] _bandBase = { 29000f, 30000f, 31000f, 31500f, 32000f };
        private static readonly float[] _bandSpan = { 1000f, 1000f, 500f, 500f, 500f };
        // Shared across loaded Facet versions so their windows stack in one order.
        // Shared across loaded Facet versions so their windows stack in one order.
        private static readonly float[] _bandNext = Input.BandNext;

        internal float Order
        {
            get { return _settings != null ? _settings.sortingOrder : -1f; }
            set { if (_settings != null) _settings.sortingOrder = value; }
        }

        public void Dispose()
        {
            _theme.Changed -= Retheme;
            if (_refresher != null) { _refresher.Stop(); _refresher = null; }
            Ui.Stack.Remove(this);
            if (_visible) { _visible = false; if (Layer != Layer.Hud) Ui.OpenWindows--; RaiseHidden(); }
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            _go = null; _settings = null; Root = null;
        }
    }
}
