using System;

namespace Sparring
{
    /// <summary>A hit as far as this game can tell who it came from.</summary>
    internal readonly struct Blow<TId> where TId : struct, IEquatable<TId>
    {
        internal Blow(bool named, TId from, HitData.HitType type)
        {
            Named = named;
            From = from;
            Type = type;
        }

        /// <summary>Whether the hit names an attacker at all.</summary>
        internal bool Named { get; }

        /// <summary>
        /// The attacker, when the one the hit names is loaded in this game and so known; nobody,
        /// the id's default, otherwise.
        /// </summary>
        internal TId From { get; }

        internal HitData.HitType Type { get; }
    }

    /// <summary>
    /// What a duel survives. Only the opponent counts, by their own hand or by what they set alight:
    /// anything else, such as creatures, falls or drowning, kills as usual. Everything but
    /// <see cref="BlowOf"/>, which reads the game's hit, needs no game, so the unit tests hold
    /// these rules as they are.
    /// </summary>
    internal static class Survival
    {
        /// <summary>
        /// Whether a hit carrying no attacker should be read as the opponent's.
        ///
        /// Fire, spirit and poison do not land with the blow that carried them. They are put on as
        /// a status effect, whose periodic damage builds a fresh hit with nobody named on it and
        /// applies it straight to the victim. Frost and lightning do no damage over time at all, so
        /// between them <c>Burning</c> — which carries spirit as well as fire — and <c>Poisoned</c>
        /// are every way an opponent's weapon can still be hurting you after the swing is over.
        ///
        /// Inside a duel there is little else either is likely to be, so both are taken as theirs.
        /// That covers the case this exists for: an arrow that sets you alight and then finishes
        /// you. The imprecision is that a duel fought standing in a fire credits the fire to your
        /// opponent, which costs a wrong line on a card and a yield that should have been a death.
        ///
        /// Everything else that burns or drowns a person — smoke, lava, cinders, cold, deep water,
        /// a long fall — keeps its own hit type and is not covered, so none of them stops killing.
        /// </summary>
        internal static bool OpponentTick(bool named, HitData.HitType type) =>
            !named && (type == HitData.HitType.Burning || type == HitData.HitType.Poisoned);

        /// <summary>
        /// Whether a blow was the opponent's doing. Their own hits name them; what they set alight
        /// arrives later as a tick with nobody named on it (<see cref="OpponentTick"/>). A hit naming
        /// someone this game cannot find is nobody's tick and nobody known, so it is not theirs.
        /// The yield and the card both read a blow by this one rule.
        /// </summary>
        internal static bool FromOpponent<TId>(in Blow<TId> blow, TId opponent) where TId : struct, IEquatable<TId> =>
            !blow.From.Equals(default(TId)) ? blow.From.Equals(opponent) : OpponentTick(blow.Named, blow.Type);

        /// <summary>
        /// Whether a killing blow should become a yield instead: on the player's own game
        /// (<paramref name="local"/>), in a duel against <paramref name="opponent"/> (nobody, the
        /// id's default, outside one), and only while both leases agree on that same opponent
        /// (<paramref name="agreedWith"/>, nobody when they agree on none). The pair is re-read
        /// rather than trusted from the duel's own state, so a duel this client thinks is running
        /// but the network does not is never a reason to survive a hit. With no record of the last
        /// hit, the death stands.
        /// </summary>
        internal static bool ShouldSurvive<TId>(bool local, TId opponent, TId agreedWith, Blow<TId>? lastHit)
            where TId : struct, IEquatable<TId>
        {
            if (!local || opponent.Equals(default(TId))) return false;
            if (!agreedWith.Equals(opponent)) return false;
            return lastHit != null && FromOpponent(lastHit.Value, opponent);
        }

        /// <summary>The game's hit read as a <see cref="Blow{TId}"/>, or none for no hit.</summary>
        internal static Blow<ZDOID>? BlowOf(HitData hit)
        {
            if (hit == null) return null;

            var attacker = hit.GetAttacker();
            return new Blow<ZDOID>(hit.HaveAttacker(), attacker != null ? attacker.GetZDOID() : ZDOID.None, hit.m_hitType);
        }
    }
}
