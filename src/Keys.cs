using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// The three keys the HUD offers: accept, decline, and hold-to-yield.
    ///
    /// Bound as plain <c>KeyCode</c> and read through legacy <c>Input</c>, not as a BepInEx
    /// <c>KeyboardShortcut</c>. A KeyboardShortcut does not fire while any other keyboard key is
    /// held, so a yield bound that way would do nothing while you were holding W. A raw KeyCode
    /// has no modifier logic and fires regardless. It is also a setting type keybind tools can
    /// read and rebind.
    /// </summary>
    public static class Keys
    {
        private static float _yieldHeldFor;

        /// <summary>How far through the yield hold we are, 0 to 1, for the HUD to draw.</summary>
        public static float YieldProgress =>
            Plugin.YieldHoldSeconds <= 0f ? 0f : Mathf.Clamp01(_yieldHeldFor / Plugin.YieldHoldSeconds);

        public static bool YieldHeld => _yieldHeldFor > 0f;

        public static void Tick()
        {
            if (!Accepting())
            {
                _yieldHeldFor = 0f;
                return;
            }

            if (Duel.HasInvite)
            {
                if (Input.GetKeyDown(Plugin.AcceptKey)) Duel.Accept();
                else if (Input.GetKeyDown(Plugin.DeclineKey)) Duel.Decline();
            }

            TickYield();
        }

        /// <summary>
        /// Held, not tapped, and only once the fight is actually on. A single keypress deciding a
        /// duel is one mis-key away from handing someone the win, and letting go before the hold
        /// completes abandons it — so there is always a way back out of a press you did not mean.
        /// </summary>
        private static void TickYield()
        {
            if (!Duel.Active || Duel.CountingDown)
            {
                _yieldHeldFor = 0f;
                return;
            }

            if (!Input.GetKey(Plugin.YieldKey))
            {
                _yieldHeldFor = 0f;
                return;
            }

            _yieldHeldFor += Time.unscaledDeltaTime;
            if (_yieldHeldFor < Plugin.YieldHoldSeconds) return;

            _yieldHeldFor = 0f;
            Duel.Forfeit();
        }

        /// <summary>
        /// Whether a keystroke belongs to us at all. Typing in chat, a console line, an open menu
        /// and being dead are all times when a letter means something else, and a hotkey that
        /// fires anyway is the classic way a mod eats someone's sentence.
        /// </summary>
        private static bool Accepting()
        {
            var me = Player.m_localPlayer;
            if (me == null || me.IsDead() || me.InCutscene()) return false;

            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Console.IsVisible()) return false;
            if (Menu.IsVisible()) return false;
            if (TextInput.IsVisible()) return false;

            return true;
        }
    }
}
