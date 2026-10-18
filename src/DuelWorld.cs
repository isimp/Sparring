using System;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// Everything the duel flow (<see cref="DuelFlow{TId}"/>) needs from the game it runs in: the
    /// player, the clock, the rules, the lease on the player's own ZDO, the messages to other
    /// games, and what the player sees, hears and has done to them. The game's own is
    /// <see cref="GameWorld"/>; the unit tests give each simulated player one of their own, which
    /// is what lets two games' duel flows be played against each other without the game.
    /// Nobody is the id's default.
    /// </summary>
    internal interface IDuelWorld<TId> where TId : struct, IEquatable<TId>
    {
        /// <summary>The local player, or nobody when there is no character in the world.</summary>
        TId Me { get; }

        string MyName { get; }

        /// <summary>Server time in seconds.</summary>
        double Now { get; }

        DuelRules Rules { get; }

        /// <summary>What the player's own game knows about whether they are free to duel.</summary>
        Standing Standing { get; }

        /// <summary>Whether the player is dead, asked every tick of a live duel, so on its own and cheaply.</summary>
        bool Dead { get; }

        /// <summary>Whether anything hostile stands within this many metres.</summary>
        bool HostileAround(float radius);

        /// <summary>Where the player stands.</summary>
        Vector3 Here { get; }

        /// <summary>How far another player is, or null for one this game cannot place.</summary>
        float? DistanceTo(TId other);

        /// <summary>The point halfway to another player, or null for one this game cannot place.</summary>
        Vector3? MidpointWith(TId other);

        /// <summary>How far the player stands from a point, flat: height is not distance here.</summary>
        float FromCenter(Vector3 center);

        /// <summary>Another player loaded in this game, with what can be read of them from here.</summary>
        bool TryFind(TId other, out Sighting seen);

        /// <summary>Writes the player's half of the lease, naming the opponent.</summary>
        void Stamp(TId opponent);

        /// <summary>Writes the terms both sides agreed.</summary>
        void SetTerms(in Lease.Terms terms);

        /// <summary>Drops the player's half of the lease and the terms.</summary>
        void ClearLease();

        /// <summary>The opponent both leases agree on, or nobody.</summary>
        TId Agreed();

        /// <summary>Whether a lease may name its own holder, as only a practice duel's does.</summary>
        bool SelfPair { set; }

        void SendChallenge(TId to, float radius);
        void SendReply(TId to, bool accepted, in Lease.Terms terms);
        void SendBusy(TId to, Busy why);
        void SendResult(TId to, EndReason reason);

        /// <summary>Sends the opponent what this side's card holds: what it took from them.</summary>
        void SendTally(TId to);

        /// <summary>Tells every game how a duel ended.</summary>
        void SendAnnounce(TId winner, string winnerName, string loserName, EndReason reason);

        /// <summary>Puts a line in the middle of the player's screen.</summary>
        void Say(string line);

        void Play(Sound.Cue cue);

        /// <summary>The sound that draws attention to a challenge on screen.</summary>
        void ChallengeSound();

        /// <summary>Lifts the player to this share of their health, never lower than they are.</summary>
        void Lift(float fraction);

        /// <summary>Clears what a fight left on the player: burning, poison and the like.</summary>
        void Settle();

        /// <summary>Knocks the player down, away from the opponent.</summary>
        void KnockDown(TId awayFrom);

        /// <summary>The winner's own celebration, which only their game can set going.</summary>
        void Celebrate();

        /// <summary>The ring drawn around duels has to be looked at again.</summary>
        void RingChanged();

        /// <summary>A real duel takes over from any rehearsal standing where you were.</summary>
        void StopRehearsal();

        void CardBegins(string opponent);
        void CardOutcome(DuelOutcome outcome);

        /// <summary>Closes this side's card; it is shown only for a duel somebody won.</summary>
        void CardEnds(bool decided);

        /// <summary>The opponent's half of the card: what they took.</summary>
        void CardHeard(float took, int hits, float biggest);
    }

    /// <summary>The settings a duel follows, as the server or the player set them.</summary>
    internal struct DuelRules
    {
        public double InviteSeconds;
        public double PairGraceSeconds;
        public double CountdownSeconds;
        public float ArenaRadius;
        public float YieldHealth;
        public float VictoryHealth;
        public bool RequireOutOfCombat;
        public bool RequireClearGround;
    }

    /// <summary>Another player as this game can read them: what they are called, and the two parts of their state it knows.</summary>
    internal readonly struct Sighting
    {
        public Sighting(string name, bool dead, bool pvpOn)
        {
            Name = name;
            Dead = dead;
            PvpOn = pvpOn;
        }

        public string Name { get; }
        public bool Dead { get; }
        public bool PvpOn { get; }
    }
}
