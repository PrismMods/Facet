using System;

namespace Facet
{
    /* One keybind capture at a time, across every loaded Facet version and every mod: a control that
       starts capturing cancels whichever was waiting before. The slot is [owner, cancel] in Shared
       (BCL types only). */
    public static class KeyCapture
    {
        private static readonly object[] _slot = Shared.Get("Facet.KeyCapture", () => new object[2]);

        /// The current owner's name, or null.
        public static string Owner { get { lock (_slot) return (string)_slot[0]; } }

        /// Take the capture; the previous owner's cancel runs (guarded) unless it is the same owner.
        public static void Claim(string owner, Action cancel, Action<string> log = null)
        {
            string prev; Action prevCancel;
            lock (_slot)
            {
                prev = (string)_slot[0]; prevCancel = (Action)_slot[1];
                _slot[0] = owner; _slot[1] = cancel;
            }
            if (prev != null && prev != owner && prevCancel != null) Safe.Run(prev, "KeyCapture cancel", prevCancel, log);
        }

        /// Let go; ignored when someone else owns it now.
        public static void Release(string owner)
        {
            lock (_slot) if ((string)_slot[0] == owner) { _slot[0] = null; _slot[1] = null; }
        }
    }
}
