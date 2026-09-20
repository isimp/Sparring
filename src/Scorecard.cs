using System;
using UnityEngine;

namespace Sparring
{
    /// <summary>How a duel went for the person reading the card.</summary>
    public enum DuelOutcome
    {
        Undecided,
        Won,
        Lost,
    }

    /// <summary>
    /// What the duel actually cost, tallied while it happens and shown when it ends.
    ///
    /// Each side counts only what it took, and the two swap totals at the finish. A blow's damage
    /// is decided on the victim's machine: <c>ApplyDamage</c> runs armour, resistances and the
    /// difficulty scale over it, so the attacker's number is not the damage that landed.
    ///
    /// The amount counted is always the game's own figure rather than one worked out here, so
    /// nothing in this file has to be kept in step with how damage is calculated. It arrives from
    /// <see cref="Patches.ApplyDamageFinalizer"/>, which is where a blow is heard about.
    /// </summary>
    public static class Scorecard
    {
        private const float ShowSeconds = 8f;

        private static double _startedAt;
        private static float _showUntil;

        public static float TookTotal { get; private set; }
        public static int TookHits { get; private set; }
        public static float TookBiggest { get; private set; }

        public static float GaveTotal { get; private set; }
        public static int GaveHits { get; private set; }
        public static float GaveBiggest { get; private set; }

        /// <summary>
        /// Whether the other side has told us what it took. Until it does — or if it never does,
        /// because they left, or are on a build without this — the card shows our half alone
        /// rather than a zero that would read as "you never landed one".
        /// </summary>
        public static bool HeardBack { get; private set; }

        public static string Against { get; private set; } = "";
        public static float Seconds { get; private set; }
        public static bool Showing => Time.realtimeSinceStartup < _showUntil;

        /// <summary>
        /// Which way it went. Set by whichever path decided it rather than worked out from the end
        /// reason, because the reason alone cannot say: a duel ending in <c>Yielded</c> is a loss
        /// on the machine that yielded and a win on the one that was yielded to.
        /// </summary>
        public static DuelOutcome Outcome { get; private set; }

        public static void SetOutcome(DuelOutcome outcome) => Outcome = outcome;

        public static void Begin(string opponent)
        {
            Clear();
            Against = opponent;
            _startedAt = Lease.Now;
            _showUntil = 0f;
        }

        /// <summary>
        /// Closes the tally and puts it on screen. The clock runs from the first blow counting, not
        /// from the handshake, so the countdown is not billed as fighting.
        /// </summary>
        public static void End(bool show)
        {
            var begun = Duel.Terms.StartAtMs > 0L ? Duel.Terms.StartAtMs / 1000.0 : _startedAt;
            Seconds = (float)Math.Max(0.0, Lease.Now - begun);

            _showUntil = show && (TookHits > 0 || GaveHits > 0)
                ? Time.realtimeSinceStartup + ShowSeconds
                : 0f;

            // A card with nothing on it is withheld, which looks exactly like a duel nobody landed
            // a hit in. Anyone who has turned the rehearsal commands on is testing something, and
            // is owed the difference in writing.
            if (Plugin.DebugCommands)
            {
                Plugin.Log.LogInfo($"Sparring card: {Outcome} vs {Against}, took {TookTotal:0.#} over {TookHits}, " +
                                   $"{Seconds:0.#}s, " + (_showUntil > 0f ? "shown." : "withheld."));
            }
        }

        /// <summary>Their tally of what they took is our tally of what we dealt.</summary>
        public static void ReceiveOpponent(float total, int hits, float biggest)
        {
            GaveTotal = Mathf.Max(0f, total);
            GaveHits = Mathf.Max(0, hits);
            GaveBiggest = Mathf.Max(0f, biggest);
            HeardBack = true;

            // It usually lands a moment after our own side closed, so keep the card up long enough
            // to be read with both halves on it rather than only the half we had first.
            if (Showing) _showUntil = Time.realtimeSinceStartup + ShowSeconds * 0.6f;

            // Always after the card was written down, which is why that line cannot carry it.
            if (Plugin.DebugCommands)
            {
                Plugin.Log.LogInfo($"Sparring card: dealt {GaveTotal:0.#} over {GaveHits}.");
            }
        }

        /// <summary>
        /// Records a blow that landed on us. Called for every hit the local player takes while a
        /// duel is on, and decides here whether it belongs on the card.
        /// </summary>
        public static void Count(float amount, Character attacker, HitData hit)
        {
            if (amount <= 0f || !Duel.Active) return;

            // A practice duel has no opponent but itself, so there everything that lands counts.
            if (!Duel.Practising && !FromOpponent(attacker, hit)) return;

            TookTotal += amount;
            TookHits++;
            if (amount > TookBiggest) TookBiggest = amount;
        }

        /// <summary>
        /// Whether a blow was the opponent's doing. Their own hits name them; what they set alight
        /// arrives later as a tick with nobody named on it, read as theirs by the same rule that
        /// decides whether such a tick is allowed to kill.
        /// </summary>
        private static bool FromOpponent(Character attacker, HitData hit)
        {
            return attacker != null
                ? attacker.GetZDOID() == Duel.Opponent
                : Duel.OpponentTick(hit);
        }

        /// <summary>Fills the card with plausible numbers, for looking at it without a duel.</summary>
        public static void Rehearse(string opponent, DuelOutcome outcome)
        {
            Clear();
            Against = opponent;
            Outcome = outcome;
            TookTotal = 218f; TookHits = 9; TookBiggest = 41f;
            GaveTotal = 263f; GaveHits = 12; GaveBiggest = 58f;
            HeardBack = true;
            Seconds = 47f;
            _showUntil = Time.realtimeSinceStartup + ShowSeconds;
        }

        private static void Clear()
        {
            TookTotal = 0f; TookHits = 0; TookBiggest = 0f;
            GaveTotal = 0f; GaveHits = 0; GaveBiggest = 0f;
            HeardBack = false;
            Outcome = DuelOutcome.Undecided;
            Against = "";
            Seconds = 0f;
        }

        /// <summary>Takes the card off the screen.</summary>
        public static void Stop()
        {
            _showUntil = 0f;
        }
    }
}
