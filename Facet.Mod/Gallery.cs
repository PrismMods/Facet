using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Plain data the Gallery's controls write into.
    public sealed class Model
    {
        public bool Flag;
        public float Size = 1f;
        public int Count = 3;
        public string Name = "Facet";
        public int Pick;
        public int Mode;
        public float Amount = 4f;
        public Color Tint = Color.cyan;
        public string Key = "F8";
    }

    // One of each control. Later tasks add pages.
    public static class Gallery
    {
        public static Surface Surface { get; private set; }
        /// Replaced wholesale by the autotest, the way a profile switch replaces a mod's settings object.
        public static Model Model { get; set; } = new Model();
        static Window _win;

        /// What the Gallery's host was asked to persist, by key.
        public static readonly Dictionary<string, string> Store = new Dictionary<string, string>();

        /// The Gallery host's language; the autotest flips it to prove a window relabels in place.
        public static string Lang = "en";

        public static readonly Host Host = Host.Create("Gallery", o =>
        {
            o.Language = () => Lang;
            o.Translate = s => Lang == "ko" && s == "Size" ? "\uD06C\uAE30" : null;
            o.Log = m => Main.Log("[ui] " + m);
            o.Read = k => Store.TryGetValue(k, out var v) ? v : null;
            o.Write = (k, v) => Store[k] = v;
        });

        public static Window Open()
        {
            if (Surface != null) { Surface.Visible = true; return _win; }
            Surface = new Surface(Host, "FacetGallery", Layer.Window);
            _win = new Window(Surface.Root, "Gallery", "Facet Gallery", new Rect(120, 80, 640, 640), Close);
            var scroll = new ScrollView();
            Skin.Scroll(scroll);
            scroll.style.flexGrow = 1f; scroll.style.minHeight = 0f;
            _win.Body.Add(scroll);
            var p = scroll.contentContainer;
            Controls.Toggle(p, "Toggle", Bind.To(() => Model.Flag, v => Model.Flag = v), o => { o.Id = "flag"; o.Default = false; });
            Controls.Slider(p, "Size", Bind.To(() => Model.Size, v => Model.Size = v), o => { o.Id = "size"; o.Min = 0.5f; o.Max = 2f; o.Default = 1f; });
            Controls.IntSlider(p, "Int slider", Bind.To(() => Model.Count, v => Model.Count = v), o => { o.Id = "count"; o.Default = 3; });
            Controls.Number(p, "Number", Bind.To(() => Model.Amount, v => Model.Amount = v), o => { o.Id = "amount"; o.Min = 0f; o.Max = 100f; o.Default = 4f; });
            Controls.Choice(p, "Choice", Bind.To(() => Model.Mode, v => Model.Mode = v), o => { o.Id = "mode"; o.Items = new[] { "One", "Two", "Three" }; });
            Controls.Dropdown(p, "Dropdown", Bind.To(() => Model.Pick, v => Model.Pick = v), o => { o.Id = "pick"; o.Items = new[] { "Alpha", "Beta", "Gamma" }; });
            Controls.Text(p, "Text", Bind.To(() => Model.Name, v => Model.Name = v), o => o.Id = "name");
            Controls.Colour(p, "Colour", Bind.To(() => Model.Tint, v => Model.Tint = v), o => o.Id = "tint");
            Controls.Key(p, "Key", Bind.To(() => Model.Key, v => Model.Key = v), o => { o.Id = "key"; o.Capture = done => done(Model.Key); });
            Controls.Action(p, "Action", "Run", () => { });
            Controls.Danger(p, "Danger", "Wipe", () => { });
            Controls.Info(p, "Info", () => "Facet " + "0.1");
            return _win;
        }

        public static void Close()
        {
            if (Surface == null) return;
            Surface.Dispose();
            Surface = null; _win = null;
        }
    }
}
