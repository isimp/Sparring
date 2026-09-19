using System;
using HarmonyLib;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// The one line everybody nearby reads when a duel ends.
    ///
    /// Written into each viewer's own chat window rather than sent as speech. A shout would carry
    /// the winner's name as if they had said it, would reach further than the duel did, and would
    /// give anyone with the mod a way to put words in another player's mouth. Printing locally on
    /// each client that has Sparring costs unmodded players the message and buys an announcement
    /// that cannot be abused.
    /// </summary>
    public static class Announcer
    {
        private static string _last = "";
        private static float _lastAt;

        public static void Show(string winner, string loser, EndReason reason)
        {
            try
            {
                if (Chat.instance == null) return;

                winner = Clean(winner);
                loser = Clean(loser);
                if (winner.Length == 0 || loser.Length == 0) return;

                var line = Compose(winner, loser, reason);

                // The same end can reach us more than once — a re-sent RPC, both sides reporting a
                // draw. Identical text within a couple of seconds is the same event, not a second
                // one, and printing it twice would read as two duels ending.
                if (line == _last && Time.realtimeSinceStartup - _lastAt < 2f) return;
                _last = line;
                _lastAt = Time.realtimeSinceStartup;

                Chat.instance.AddString(line);
                Reveal(Chat.instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not announce a result: {ex.Message}");
            }
        }

        /// <summary>
        /// Brings the chat window up so the line can actually be read.
        ///
        /// Adding to the buffer is only half of saying something. <c>Chat.Update</c> keeps the
        /// window switched off unless <c>m_hideTimer</c> is under the hide delay, and the game
        /// resets that timer itself before adding an incoming message — <c>Chat.cs:308</c> does
        /// exactly this, one line above its own <c>AddString</c>. Without it the announcement is
        /// delivered, buffered and never seen, which looks from the outside exactly like a message
        /// that was never sent.
        /// </summary>
        private static void Reveal(Chat chat)
        {
            try
            {
                if (_hideTimer == null) _hideTimer = AccessTools.FieldRefAccess<Chat, float>("m_hideTimer");
                _hideTimer(chat) = 0f;
            }
            catch (Exception ex)
            {
                // The line is in the buffer either way; it just waits for chat to be opened.
                Plugin.Log.LogDebug($"Sparring could not raise the chat window: {ex.Message}");
            }
        }

        private static AccessTools.FieldRef<Chat, float> _hideTimer;

        private static string Compose(string winner, string loser, EndReason reason)
        {
            switch (reason)
            {
                case EndReason.Yielded:
                    return $"<color=#d8c38a>{loser} yields to {winner}.</color>";
                case EndReason.Forfeited:
                    return $"<color=#d8c38a>{loser} forfeits to {winner}.</color>";
                case EndReason.LeftRing:
                    return $"<color=#d8c38a>{loser} left the ring and yields to {winner}.</color>";
                case EndReason.Died:
                    return $"<color=#b9bfc4>The duel between {winner} and {loser} ended — {loser} fell to something else.</color>";
                case EndReason.OutOfRange:
                    return $"<color=#b9bfc4>The duel between {winner} and {loser} was called off — they left the ring.</color>";
                default:
                    return $"<color=#b9bfc4>The duel between {winner} and {loser} is over.</color>";
            }
        }

        /// <summary>
        /// Names arrive over the network, so they are somebody else's text. Rich-text brackets come
        /// out — the chat window renders markup, and a name carrying its own tags could otherwise
        /// recolour or hide the rest of the line — and the length is capped.
        /// </summary>
        private static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            name = name.Replace('<', ' ').Replace('>', ' ').Trim();
            return name.Length > 32 ? name.Substring(0, 32) : name;
        }
    }
}
