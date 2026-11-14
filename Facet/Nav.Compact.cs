using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* The compact rail: an icon strip that widens while the pointer rests on it. A spacer keeps the
       strip's place in the row and the rail itself floats above the page, so widening covers the page
       instead of pushing it over, and leaving puts it back as it was. */
    public sealed partial class Nav
    {
        /// A rail entry's left padding; the compact strip is this, the icon, and this again.
        private const float ItemInset = 10f;
        /// How long the pointer rests on the strip before it widens, so passing over it does not.
        private const int HoverDwellMs = 100;

        private VisualElement _handle, _railSpace;
        private float _compactWidth;
        private bool _compact, _compactButton, _widened, _armed = true;
        private IVisualElementScheduledItem _dwell;

        /// The rail as an icon strip (an entry's padding, its icon, the padding again) that widens over the
        /// page, which does not move, once the pointer has rested on it for 0.1 s, and narrows when it leaves.
        /// Titles fade with it; rules and the open entry's marker stay. Applies at once. An entry with no
        /// Icon shows only its marker in the strip, and a nested entry's icon is cut off.
        public bool Compact
        {
            get { return _compact; }
            set { if (value != _compact) { _compact = value; ApplyCompact(); } }
        }

        /// A button at the top of the rail that flips Compact and raises CompactChanged.
        public bool CompactButton
        {
            get { return _compactButton; }
            set { if (value != _compactButton) { _compactButton = value; RebuildRail(); } }
        }

        /// Raised after the rail's own button flips Compact, with the new value (the host keeps it);
        /// not when the host sets Compact.
        public event Action<bool> CompactChanged;

        /// Where the content pane starts, in the window body: the icon strip when Compact, else the open rail's width
        /// and its handle (7), or the handle alone with the rail shut. Window.SetContentInset takes it as it is.
        public float Inset => _compact ? _compactWidth : (_railOpen ? _railWidth : 0f) + 7f;

        private void BuildCompact(VisualElement split, VisualElement handle, Sizes sz)
        {
            _handle = handle;
            _compactWidth = 2f * ItemInset + sz.Icon;
            _railSpace = Ui.Box();
            _railSpace.name = "rail-space";
            _railSpace.style.width = _compactWidth;
            _railSpace.style.display = DisplayStyle.None;
            split.Insert(0, _railSpace);
            Events.On<MouseEnterEvent>(_railScroll, _ => Hovered(true));
            Events.On<MouseLeaveEvent>(_railScroll, _ => Hovered(false));
        }

        private void ApplyCompact()
        {
            if (_dwell != null) _dwell.Pause();
            _widened = false;
            var rs = _railScroll;
            if (_compact)
            {
                _railOpen = true;   // a rail dragged shut comes back as the strip
                rs.style.display = DisplayStyle.Flex;
                rs.style.position = Position.Absolute;
                rs.style.left = 0f; rs.style.top = 0f; rs.style.bottom = 0f;
                rs.BringToFront();   // drawn over the page when it widens
                _railSpace.style.display = DisplayStyle.Flex;
                _handle.style.display = DisplayStyle.None;   // nothing to drag: the strip has one width
                Anim.Width(rs, _compactWidth, Anim.Fast);
            }
            else
            {
                rs.style.position = Position.Relative;
                rs.style.left = StyleKeyword.Null; rs.style.top = StyleKeyword.Null; rs.style.bottom = StyleKeyword.Null;
                rs.SendToBack();
                _railSpace.style.display = DisplayStyle.None;
                _handle.style.display = DisplayStyle.Flex;
                Anim.Width(rs, _railWidth, Anim.Fast);
            }
            Titles(_compact ? 0f : 1f, true);
            DrawRailButton();
            PaintHandle();
            ReportInset();
        }

        private void Hovered(bool on)
        {
            if (!on) _armed = true;
            if (!_compact || (on && !_armed)) return;
            if (on)
            {
                if (_dwell == null) _dwell = _railScroll.schedule.Execute(() => Widen(true));
                _dwell.ExecuteLater(HoverDwellMs);   // cancels a dwell already waiting
            }
            else
            {
                if (_dwell != null) _dwell.Pause();
                Widen(false);
            }
        }

        private void Widen(bool on)
        {
            if (on == _widened || !_compact) return;
            _widened = on;
            Anim.Width(_railScroll, on ? _railWidth : _compactWidth, Anim.Fast);
            Titles(on ? 1f : 0f, true);
        }

        // Every label in the rail: entry titles, group headings, branch chevrons (the button's glyph is an image).
        private void Titles(float opacity, bool animate)
        {
            _rail.Query<Label>().ForEach(l =>
            {
                if (animate) Anim.Fade(l, opacity, Anim.Fast); else l.style.opacity = opacity;
            });
        }

        // At the top of the rail, its glyph in the icons' column: » widens the strip for good, « makes it one.
        private void AddRailButton(Sizes sz, Looks k)
        {
            var row = Ui.Row(_rail);
            row.name = "rail-button";
            row.style.minHeight = sz.NavRowHeight;
            row.style.paddingLeft = ItemInset;
            row.pickingMode = PickingMode.Position;
            Paint.Look(row, k.RailItem);
            Ui.SetRadius(row, sz.Radius);
            // Drawn chevrons at the icons' size and tint: a » in text drew a third of an icon's height.
            var g = Ui.Box(row);
            g.name = "rail-glyph";
            g.style.width = sz.Icon; g.style.height = sz.Icon;
            g.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            g.pickingMode = PickingMode.Ignore;
            TintAsTitle(row, g, k.RailItem, false);
            Events.On<ClickEvent>(row, _ =>
            {
                if (!_compact) _armed = false;   // made compact under the pointer: it widens again only after the pointer leaves
                Compact = !_compact;
                bool now = _compact;
                var e = CompactChanged;
                if (e != null) Safe.Run(Host.For(_host), "Nav.CompactChanged", () => e(now));
            });
        }

        private void DrawRailButton()
        {
            var b = _rail.childCount > 0 && _rail[0].name == "rail-button" ? _rail[0] : null;
            var g = b == null ? null : b.Q("rail-glyph");
            if (g == null) return;
            g.style.backgroundImage = Background.FromTexture2D(Tex.Chevrons(!_compact));
            Words.Tip(b, _compact ? "Expand sidebar" : "Collapse sidebar", true);
        }
    }
}
