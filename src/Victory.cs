using System;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// What winning looks like: the burst the game already plays when a skill goes up, pulsed a
    /// few times over the winner, and an emote.
    ///
    /// Both are the game's own. <c>Player.m_skillLevelupEffects</c> is a public EffectList sitting
    /// on every player — the same one <c>OnSkillLevelup</c> fires — so reusing it costs no assets,
    /// matches the game's look exactly, and cannot go missing. The emote goes through
    /// <c>StartEmote</c>, which writes to the player's ZDO rather than playing an animation
    /// locally, so every client sees it without us sending anything.
    ///
    /// The pulse runs on every client that hears the result, on the winner's own character, so
    /// spectators see it too — not just the two who fought.
    /// </summary>
    public static class Victory
    {
        private const int Pulses = 3;
        private const float PulseGap = 0.45f;

        private static ZDOID _winner = ZDOID.None;
        private static int _left;
        private static float _next;

        private static ZDOID _emoteFor = ZDOID.None;
        private static float _emoteUntil;

        /// <summary>
        /// Starts the celebration on this client. Called wherever the result is heard, which is
        /// every client running Sparring, so it needs no permission and touches nothing shared.
        /// </summary>
        public static void Celebrate(ZDOID winner)
        {
            if (winner.IsNone() || !Plugin.WinnerEffect) return;

            _winner = winner;
            _left = Pulses;
            _next = 0f;
        }

        /// <summary>
        /// The winner's own side. Kept apart from the pulse because an emote can only be started by
        /// the player it belongs to, and because it can be refused — <c>StartEmote</c> returns
        /// false mid-swing, which is exactly the state someone is in a moment after landing the
        /// blow that ended the duel. So it is attempted for a second or so rather than once.
        /// </summary>
        public static void Emote(Player me)
        {
            if (me == null || string.IsNullOrEmpty(Plugin.WinnerEmote)) return;

            _emoteFor = me.GetZDOID();
            _emoteUntil = Time.realtimeSinceStartup + 2f;
        }

        public static void Tick()
        {
            TickPulse();
            TickEmote();
        }

        private static void TickPulse()
        {
            if (_left <= 0 || _winner.IsNone()) return;
            if (Time.realtimeSinceStartup < _next) return;

            _next = Time.realtimeSinceStartup + PulseGap;
            _left--;

            var player = Lease.FindPlayer(_winner);
            if (player == null)
            {
                // Out of sight, or gone. Nothing to celebrate over here.
                _left = 0;
                _winner = ZDOID.None;
                return;
            }

            try
            {
                var anchor = player.m_eye != null ? player.m_eye : player.transform;
                player.m_skillLevelupEffects.Create(anchor.position, anchor.rotation, anchor, 1f, -1, player.GetZDOID());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not play the victory effect: {ex.Message}");
                _left = 0;
            }

            if (_left <= 0) _winner = ZDOID.None;
        }

        private static void TickEmote()
        {
            if (_emoteFor.IsNone()) return;

            var me = Player.m_localPlayer;
            if (me == null || me.GetZDOID() != _emoteFor || Time.realtimeSinceStartup > _emoteUntil)
            {
                _emoteFor = ZDOID.None;
                return;
            }

            try
            {
                if (me.StartEmote(Plugin.WinnerEmote)) _emoteFor = ZDOID.None;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not play the victory emote: {ex.Message}");
                _emoteFor = ZDOID.None;
            }
        }

        public static void Stop()
        {
            _left = 0;
            _winner = ZDOID.None;
            _emoteFor = ZDOID.None;
        }
    }
}
