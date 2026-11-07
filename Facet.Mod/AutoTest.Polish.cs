using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Small layout and motion details of rows and the rail, one section each.
    sealed partial class AutoTest
    {
        /* A page rebuilt into a shown window builds its switches on a live panel before any layout: the
           knob must start at its value, not slide in from off. A later change still slides. */
        IEnumerator SectionSwitchSnap()
        {
            Anim.Speed = 1f;   // the slide only exists with motion on; Run puts the speed back
            var s = new Surface(Host.Create("Snap", o => o.Log = m => Main.Log("[snap] " + m)), "FacetSnap", Layer.Window);
            try
            {
                var w = new Window(s.Root, "snap", "Snap", new Rect(60, 60, 420, 200));
                yield return null; yield return null;
                bool v = true;
                var c = Controls.Toggle(w.Body, "Snap", Bind.To(() => v, x => v = x), o => o.Id = "snap");
                var knob = c.Input.hierarchy[0];
                var sz = Theme.For(c.Input).Sizes;
                float size = sz.SwitchHeight - 4f, off = 2f - sz.Line, on = sz.SwitchWidth - size - 2f - sz.Line;
                Check(knob.panel != null && Px(knob.style.left) == on, "a switch built on into a shown window has its knob at on at once",
                    "panel " + (knob.panel != null) + ", left " + Px(knob.style.left) + " vs " + on);
                yield return null;
                // Resolved positions snap to device pixels: within one panel pixel is "at".
                Check(Mathf.Abs(knob.resolvedStyle.left - on) < 1f, "...and after one layout pass it resolves at on, with nothing sliding",
                    knob.resolvedStyle.left + " vs " + on);
                c.Set(false);
                Check(Px(knob.style.left) != off, "switching it off once laid out slides the knob rather than jumping", Px(knob.style.left).ToString());
                var ms = new double[1];
                yield return Until(() => Mathf.Abs(knob.resolvedStyle.left - off) < 1f, ms, 600);
                Check(ms[0] >= 0, "...and the knob comes to rest at off", knob.resolvedStyle.left + " vs " + off);
            }
            finally { s.Dispose(); }
        }
    }
}
