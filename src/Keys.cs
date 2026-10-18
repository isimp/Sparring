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
    internal static class Keys
    {
        private static readonly YieldHold _yield = new YieldHold();

        /// <summary>How far through the yield hold we are, 0 to 1, for the HUD to draw.</summary>
        public static float YieldProgress => _yield.Progress(Plugin.YieldHoldSeconds);

        public static bool YieldHeld => _yield.Held;

        public static void Tick()
        {
            if (!Accepting())
            {
                _yield.Release();
                return;
            }

            if (Duel.HasInvite)
            {
                if (Input.GetKeyDown(Plugin.AcceptKey)) Duel.Accept();
                else if (Input.GetKeyDown(Plugin.DeclineKey)) Duel.Decline();
            }

            var fighting = Duel.Active && !Duel.CountingDown;
            if (_yield.Advance(fighting, Input.GetKey(Plugin.YieldKey), Time.unscaledDeltaTime, Plugin.YieldHoldSeconds)) Duel.Forfeit();
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

    /// <summary>
    /// The yield key's hold: held, not tapped, and only once the fight is actually on. A single
    /// keypress deciding a duel is one mis-key away from handing someone the win, and letting go
    /// before the hold completes abandons it — so there is always a way back out of a press you
    /// did not mean. Nothing here needs the game, so the unit tests hold it as it is.
    /// </summary>
    internal sealed class YieldHold
    {
        private float _heldFor;

        /// <summary>Whether the key is being held towards a yield.</summary>
        internal bool Held => _heldFor > 0f;

        /// <summary>How far through the hold it is, 0 to 1; nothing for a hold of no length.</summary>
        internal float Progress(float holdSeconds)
        {
            if (holdSeconds <= 0f) return 0f;

            var share = _heldFor / holdSeconds;
            return share < 0f ? 0f : share > 1f ? 1f : share;
        }

        /// <summary>
        /// One frame of the key: held for <paramref name="dt"/> more seconds while
        /// <paramref name="fighting"/>, or let go. True on the frame the hold completes, which
        /// starts it over.
        /// </summary>
        internal bool Advance(bool fighting, bool keyDown, float dt, float holdSeconds)
        {
            if (!fighting || !keyDown)
            {
                _heldFor = 0f;
                return false;
            }

            _heldFor += dt;
            if (_heldFor < holdSeconds) return false;

            _heldFor = 0f;
            return true;
        }

        /// <summary>Lets go, as when a keystroke stops belonging to Sparring.</summary>
        internal void Release() => _heldFor = 0f;
    }
}
