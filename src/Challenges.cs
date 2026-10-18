using System;
using System.Collections.Generic;

namespace Sparring
{
    /// <summary>
    /// What a player's own game knows about whether they are free to start a duel. Most of it is
    /// only known on that machine, which is why the challenged player's game is the one that
    /// decides.
    /// </summary>
    internal struct Standing
    {
        public bool Dead;
        public bool InCutscene;
        public bool Teleporting;

        /// <summary>Sitting on something: a chair, a bench, a boat's helm.</summary>
        public bool Attached;

        public bool InBed;
        public bool Sleeping;
        public bool PvpOn;

        /// <summary>Not yet clear of the game's own ten seconds without a fight, the rule for switching PvP.</summary>
        public bool InCombat;
    }

    /// <summary>What becomes of a challenge as it reaches the player challenged.</summary>
    internal enum Arrival
    {
        /// <summary>Put on screen, with its sound.</summary>
        Shown,

        /// <summary>The same challenge is on screen already, and showing it again would only replay the sound.</summary>
        AlreadyShown,

        /// <summary>They challenged us while we were challenging them, which is taken as both saying yes.</summary>
        Crossed,

        /// <summary>Turned down before it is shown, with the reason sent back.</summary>
        Refused,
    }

    /// <summary>
    /// Who may duel whom. Everything that can turn a challenge down is decided on the machine it was
    /// sent to, before any sound or prompt: the challenger's own checks are a courtesy that spares a
    /// round trip, and these are the ones a modified client cannot skip. A refusal costs the player
    /// being challenged nothing, since they never see it. Nothing here needs the game, so the unit
    /// tests hold these rules as they are.
    /// </summary>
    internal static class Challenges
    {
        /// <summary>
        /// Whether this player is free to start a duel, and if not, why. The same test whether
        /// they are challenging, being challenged, or accepting, so the three cannot disagree.
        ///
        /// Being out of combat is the game's own rule for switching PvP, ten seconds without a
        /// fight, so a duel cannot be opened as an escape from one already under way. Having PvP
        /// on is refused because it would expose the fighters to everyone, which is the thing a
        /// duel is meant to avoid. The ground is searched last and only while its rule is on, since
        /// it is the one check that costs anything.
        /// </summary>
        internal static Busy Readiness(in Standing me, bool requireOutOfCombat, bool requireClearGround, Func<bool> hostileAround)
        {
            if (me.Dead || me.InCutscene || me.Teleporting) return Busy.Occupied;
            if (me.Attached || me.InBed || me.Sleeping) return Busy.Occupied;
            if (me.PvpOn) return Busy.PvpOn;
            if (requireOutOfCombat && me.InCombat) return Busy.InCombat;
            if (requireClearGround && hostileAround()) return Busy.CreaturesNear;
            return Busy.None;
        }

        /// <summary>
        /// What becomes of a challenge from <paramref name="challenger"/>, given what the challenged
        /// player's game holds: the duel it is in, the challenge on screen (<paramref name="incoming"/>)
        /// and its own challenge out (<paramref name="outgoing"/>), nobody being the id's default.
        /// <paramref name="refusal"/> says why one is turned down.
        /// </summary>
        internal static Arrival Arrive<TId>(TId challenger, bool dueling, TId incoming, TId outgoing, bool resting, bool tooFar, Func<Busy> readiness, out Busy refusal)
            where TId : struct, IEquatable<TId>
        {
            refusal = Busy.None;
            var answering = !incoming.Equals(default(TId));
            if (answering && incoming.Equals(challenger)) return Arrival.AlreadyShown;

            refusal = Refusal(dueling, answering, resting, tooFar, readiness);
            if (refusal != Busy.None) return Arrival.Refused;

            return outgoing.Equals(challenger) ? Arrival.Crossed : Arrival.Shown;
        }

        /// <summary>Why a challenge should not be shown, or <see cref="Busy.None"/> if it should.</summary>
        private static Busy Refusal(bool dueling, bool answering, bool resting, bool tooFar, Func<Busy> readiness)
        {
            if (dueling) return Busy.Dueling;

            // One offer on screen at a time; a second one is refused rather than replacing it.
            if (answering) return Busy.Answering;

            if (resting) return Busy.Cooldown;
            if (tooFar) return Busy.TooFar;

            return readiness();
        }
    }

    /// <summary>
    /// The pause after an invitation ends without a duel, before the same two players can exchange
    /// another. Every shown challenge plays a sound and puts a prompt on screen, so without a pause
    /// a declined challenger could repeat it as fast as they can type. Kept on both sides: the
    /// challenger's copy stops an ordinary client at the source, and the challenged player's copy
    /// stops one that ignores its own. Times are server seconds.
    /// </summary>
    internal sealed class Pauses<TId> where TId : struct, IEquatable<TId>
    {
        /// <summary>How long a pause lasts.</summary>
        internal const double Seconds = 15.0;

        private readonly Dictionary<TId, double> _until = new Dictionary<TId, double>();

        /// <summary>How many players a pause is held with, running or run out.</summary>
        internal int Count => _until.Count;

        /// <summary>Starts the pause before this player and we can exchange another challenge. Nobody is never paused.</summary>
        internal void Start(TId other, double now)
        {
            if (other.Equals(default(TId))) return;

            _until[other] = now + Seconds;

            // A handful of entries at most in normal play; drop the lapsed ones as new ones arrive.
            if (_until.Count <= 16) return;
            foreach (var key in new List<TId>(_until.Keys))
            {
                if (_until[key] <= now) _until.Remove(key);
            }
        }

        /// <summary>Whether a pause with this player is still running.</summary>
        internal bool Holds(TId other, double now) => _until.TryGetValue(other, out var until) && now < until;
    }
}
