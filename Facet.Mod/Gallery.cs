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
        public Color Tint = Color.cyan;
        public string Key = "F8";
    }

    // One of each control. Later tasks add pages.
    public static class Gallery
    {
        public static Surface Surface { get; private set; }
        public static Model Model { get; private set; } = new Model();
        static Window _win;

        public static Window Open()
        {
            if (Surface != null) { Surface.Visible = true; return _win; }
            Surface = new Surface("FacetGallery", Layer.Window);
            _win = new Window(Surface.Root, "facet-gallery", "Facet Gallery", new Rect(120, 80, 640, 640), Close);
            var scroll = new ScrollView();
            Skin.Scroll(scroll);
            scroll.style.flexGrow = 1f; scroll.style.minHeight = 0f;
            _win.Body.Add(scroll);
            var p = scroll.contentContainer;
            var m = Model;
            Widgets.Toggle(p, "Toggle", m.Flag, v => m.Flag = v);
            Widgets.Slider(p, "Slider", m.Size, 0.5f, 2f, v => m.Size = v);
            Widgets.IntSlider(p, "Int slider", m.Count, 0, 10, v => m.Count = v);
            Widgets.Choice(p, "Choice", new[] { "One", "Two", "Three" }, m.Pick, v => m.Pick = v);
            Widgets.Text(p, "Text", m.Name, v => m.Name = v);
            Widgets.Colour(p, "Colour", m.Tint, v => m.Tint = v);
            Widgets.Key(p, "Key", m.Key, done => done(m.Key));
            Widgets.Action(p, "Action", "Run", () => { });
            Widgets.Info(p, "Info", "Facet " + "0.1");
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
