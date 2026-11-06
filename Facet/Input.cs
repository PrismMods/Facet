using UnityEngine;

namespace Facet
{
    /* Facet's own input state; the host reads it. A surface that has just hidden calls
       HoldThisFrame so the key that closed it (still down for the rest of the frame) does not
       reach the game as well. */
    public static class Input
    {
        private static int _holdFrame = -1;

        public static void HoldThisFrame() => _holdFrame = Time.frameCount;

        public static bool HeldThisFrame => Time.frameCount == _holdFrame;
    }
}
