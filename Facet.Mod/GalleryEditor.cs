using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Plain data the Editor window's pages write into.
    public sealed class EditorModel
    {
        /// The Undo page: a toggle and a slider over plain fields, a row that records nothing, and a
        /// row whose steps are bound to Doc. AmountWrites counts the slider's setter runs.
        public bool Flag;
        public float Amount = 0.2f;
        public int AmountWrites;
        public bool Quiet;
        public float Bound;
        public object Doc = new object();
    }

    // The Editor window: one Nav page per editor part, on the Gallery's host.
    public static class GalleryEditor
    {
        public static Surface Surface { get; private set; }
        public static Nav Nav { get; private set; }
        /// Replaced wholesale by the autotest, like Gallery.Model.
        public static EditorModel Model { get; set; } = new EditorModel();
        static Window _win;
        public static NavItem UndoItem { get; private set; }
        /// The Undo page's slider and document-bound row, as last built.
        public static Control<float> AmountRow { get; private set; }
        public static Control<float> BoundRow { get; private set; }

        public static Window Open()
        {
            if (Surface != null) { Surface.Visible = true; return _win; }
            Surface = new Surface(Gallery.Host, "FacetGalleryEditor", Layer.Window);
            _win = new Window(Surface.Root, "editor", "Facet Editor", new Rect(140, 70, 900, 600));
            _win.CloseRequested += _ => Close();
            Nav = new Nav(_win.Body, new[] { NavItem.Heading(null, UndoItem = new NavItem("Undo", Undo)) });
            return _win;
        }

        static void Undo(VisualElement p)
        {
            Controls.Toggle(p, "Flag", Bind.To(() => Model.Flag, v => Model.Flag = v), o => o.Id = "ed-flag");
            AmountRow = Controls.Slider(p, "Amount", Bind.To(() => Model.Amount, v => { Model.Amount = v; Model.AmountWrites++; }), o => o.Id = "ed-amount");
            Controls.Toggle(p, "Quiet", Bind.To(() => Model.Quiet, v => Model.Quiet = v), o => { o.Id = "ed-quiet"; o.Undoable = false; });
            BoundRow = Controls.Slider(p, "Bound", Bind.To(() => Model.Bound, v => Model.Bound = v), o => { o.Id = "ed-bound"; o.UndoDocument = () => Model.Doc; });
        }

        public static void Close()
        {
            if (Surface == null) return;
            Surface.Dispose();
            Surface = null; Nav = null; _win = null; AmountRow = null; BoundRow = null;
        }
    }
}
