using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Plain data the Parts window's pages write into.
    public sealed class PartsModel
    {
        public bool Glow;
        public float Size = 1f;
        public int Style;
        public bool Extra;
        public float Depth = 0.5f;
        /// The Look card's note reads On + " / 3"; All sets it to 3, None to 0.
        public int On;
        public int AllRuns;
        /// VisibleWhen of the Depth row and of the whole Look card.
        public bool HideDepth, HideCard;
        /// The Feature page's header switch and its second header's description.
        public bool Feature;
        public string Desc = "one";
        /// The Feature page's preview: how often each callback ran.
        public int Shows, Hides, Ticks, Links;
        /// The Tiles page: seven feature tiles (0..6), one judgement tile, how often Open ran, and which tiles VisibleWhen hides.
        public bool[] Tile = new bool[7];
        public bool Judge;
        public int Opens;
        public bool HideTiles;
        /// The Tabs page: builds per tab (General, Combo, Judgement), General's throw switch, Combo's flag, and General's preview.
        public int[] TabBuilds = new int[3];
        public bool BreakGeneral, ComboFlag;
        public int TabShows, TabHides, TabTicks;
    }

    // The parts window: one Nav page per part this plan adds, on the Gallery's host.
    public static class GalleryParts
    {
        public static Surface Surface { get; private set; }
        public static Nav Nav { get; private set; }
        /// Replaced wholesale by the autotest, like Gallery.Model.
        public static PartsModel Model { get; set; } = new PartsModel();
        static Window _win;
        /// The Feature page, for the autotest to go to.
        public static NavItem FeatureItem { get; private set; }
        public static NavItem CardsItem { get; private set; }
        public static NavItem TilesItem { get; private set; }
        public static NavItem TabsItem { get; private set; }
        /// The Tabs page's row and Combo's unnamed toggle, as last built.
        public static Tabs TabsRow { get; private set; }
        public static Control<bool> ComboToggle { get; private set; }

        public static Window Open()
        {
            if (Surface != null) { Surface.Visible = true; return _win; }
            Surface = new Surface(Gallery.Host, "FacetGalleryParts", Layer.Window);
            _win = new Window(Surface.Root, "parts", "Facet Parts", new Rect(100, 60, 960, 600));
            _win.CloseRequested += _ => Close();
            Nav = new Nav(_win.Body, new[]
            {
                NavItem.Heading(null, CardsItem = new NavItem("Cards", Cards), FeatureItem = new NavItem("Feature", Feature)),
                NavItem.Heading(null, TilesItem = new NavItem("Tiles", Tiles), TabsItem = new NavItem("Tabs", TabPages)),
            });
            return _win;
        }

        static void Cards(VisualElement p)
        {
            var card = Controls.Card(p, "Look", o =>
            {
                o.Id = "card-look";
                o.Actions = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("All", () => { Model.AllRuns++; Model.On = 3; }),
                    new KeyValuePair<string, Action>("None", () => Model.On = 0),
                };
                o.Note = () => Model.On + " / 3";
                o.VisibleWhen = () => !Model.HideCard;
            });
            Controls.Toggle(card, "Glow", Bind.To(() => Model.Glow, v => Model.Glow = v), o => { o.Id = "look-glow"; o.Help = "Adds a soft halo"; });
            Controls.Slider(card, "Size", Bind.To(() => Model.Size, v => Model.Size = v), o => { o.Id = "look-size"; o.Min = 0.5f; o.Max = 2f; });
            Controls.Dropdown(card, "Style", Bind.To(() => Model.Style, v => Model.Style = v), o => { o.Id = "look-style"; o.Items = new[] { "Flat", "Raised" }; });
            var adv = Controls.Advanced(card);
            Controls.Toggle(adv, "Extra", Bind.To(() => Model.Extra, v => Model.Extra = v), o => o.Id = "look-extra");
            Controls.Slider(adv, "Depth", Bind.To(() => Model.Depth, v => Model.Depth = v), o => { o.Id = "look-depth"; o.VisibleWhen = () => !Model.HideDepth; });
            // Review I2: a second card sharing the label "Size", far enough down that only a right reveal shows it.
            Ui.Box(p).style.height = 900f;
            var other = Controls.Card(p, "Other");
            Controls.Slider(other, "Size", Bind.To(() => Model.Size, v => Model.Size = v), o => { o.Id = "other-size"; o.Min = 0.5f; o.Max = 2f; });
        }

        // Task 9: a page header with the feature's switch, then two columns with a sticky right pane.
        static void Feature(VisualElement p)
        {
            Controls.PageHeader(p, "Judgement counter", Bind.To(() => Model.Feature, v => Model.Feature = v), o => { o.Id = "head"; o.Description = "Counts each judgement"; });
            Controls.PageHeader(p, "Other feature", null, o => { o.Id = "head2"; o.DescriptionText = () => Model.Desc; });
            var left = Controls.Columns(p, out var right);
            left.name = "feat-left"; right.name = "feat-right";
            var card = Controls.Card(left, "Rows");
            for (int i = 0; i < 30; i++) Controls.Toggle(card, "Row " + i, Bind.To(() => false, v => { }));
            var tick = Ui.Text("", null, null, Role.Text, true);
            Controls.Preview(right, "Preview", o =>
            {
                o.Id = "feat-preview";
                o.Note = "Live view";
                o.Link = "Edit layout →";
                o.OnLink = () => Model.Links++;
                o.Show = st => { Model.Shows++; st.Add(tick); };
                o.Tick = st => { Model.Ticks++; tick.text = Model.Ticks.ToString(); };
                o.Hide = () => Model.Hides++;
            });
            var box = Ui.Box(right);
            box.name = "feat-box";
            box.style.height = 200f;
        }

        // Task 11: feature tiles (switch + Open) in a 5-column grid, and judgement tiles (no switch, coloured).
        static void Tiles(VisualElement p)
        {
            var grid = Controls.Tiles(p);
            grid.name = "tiles";
            for (int i = 0; i < 7; i++)
            {
                int k = i;
                Controls.Tile(grid, i == 0 ? "Judgement counter" : i == 1 ? new string('W', 200) : i == 2 ? new string('한', 80) : "Feature " + i,
                    Bind.To(() => Model.Tile[k], v => Model.Tile[k] = v), o =>
                    {
                        o.Id = "tile" + k;
                        o.Open = () => Model.Opens++;
                        if (k == 0) o.Tooltip = "Counts each judgement";
                        if (k < 3) o.VisibleWhen = () => !Model.HideTiles;
                    });
            }
            var j = Controls.Tiles(p, o => o.Columns = 4);
            Controls.Tile(j, "Perfect", Bind.To(() => Model.Judge, v => Model.Judge = v), o => { o.Id = "judge"; o.Switch = false; o.Colour = new Color(1f, 0f, 0f); });
        }

        // Task 12: tab pages. General holds a preview, Combo an unnamed toggle and a subpage, Judgement a row far down.
        static void TabPages(VisualElement p)
        {
            TabsRow = Tabs.Pages(p, new List<NavItem>
            {
                new NavItem("General", g =>
                {
                    Model.TabBuilds[0]++;
                    if (Model.BreakGeneral) throw new InvalidOperationException("General is broken");
                    Controls.Preview(g, "Tab preview", o => { o.Show = _ => Model.TabShows++; o.Tick = _ => Model.TabTicks++; o.Hide = () => Model.TabHides++; });
                }),
                new NavItem("Combo", c =>
                {
                    Model.TabBuilds[1]++;
                    ComboToggle = Controls.Toggle(c, "Combo flag", Bind.To(() => Model.ComboFlag, v => Model.ComboFlag = v));
                    Controls.SubPage(c, "Combo extras", q => Controls.Info(q, "Inside combo", () => "x"), o => o.Id = "combo-sub");
                }) { Tooltip = "Combo settings" },
                new NavItem("Judgement", j =>
                {
                    Model.TabBuilds[2]++;
                    Ui.Box(j).style.height = 900f;
                    Controls.Slider(j, "Size", Bind.To(() => Model.Size, v => Model.Size = v), o => { o.Id = "judge-size"; o.Min = 0.5f; o.Max = 2f; });
                }),
            });
        }

        public static void Close()
        {
            if (Surface == null) return;
            Surface.Dispose();
            Surface = null; Nav = null; _win = null;
        }
    }
}
