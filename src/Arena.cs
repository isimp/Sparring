namespace Sparring
{
    /// <summary>
    /// The ring's rules in numbers: how big one may be, how far a challenge reaches, how far apart
    /// two fighters may get, and what leaving it means. Nothing here needs the game, so the unit
    /// tests hold these rules as they are.
    /// </summary>
    internal static class Arena
    {
        /// <summary>
        /// The hard bounds on a ring, in metres. Constants rather than settings: a radius crosses
        /// the network and both fighters clamp it, so configurable bounds could clamp the same
        /// number to two different rings. The proposed size stays configurable, since it only has
        /// to be right on the machine proposing it.
        /// </summary>
        internal const float Floor = 5f;

        /// <inheritdoc cref="Floor"/>
        internal const float Ceiling = 60f;

        /// <summary>
        /// Holds a proposed ring size to what the mod allows. Applied to the number typed locally
        /// and to the one that arrives from the other side, because a radius off the network is
        /// untrusted input: a huge one would protect the fighters across the map, and zero would
        /// put both permanently outside the ring. No size, or one that is no number, is the default
        /// ring (<paramref name="fallback"/>, the ArenaRadius setting).
        /// </summary>
        internal static float Clamp(float radius, float fallback)
        {
            if (radius <= 0f || float.IsNaN(radius)) return fallback;
            if (radius < Floor) return Floor;
            return radius > Ceiling ? Ceiling : radius;
        }

        /// <summary>
        /// How far a pending challenge reaches: the proposed ring, or the default ring when none was
        /// proposed, and never less than the smallest ring.
        /// </summary>
        internal static float Reach(float radius, float fallback)
        {
            var reach = radius > 0f ? radius : fallback;
            return reach < Floor ? Floor : reach;
        }

        /// <summary>
        /// Whether someone this far away is out of a pending challenge's reach. Measured against the
        /// proposed ring, not the larger distance a live duel tolerates: walking out of the proposed
        /// ring is taken as withdrawing.
        /// </summary>
        internal static bool Beyond(float distance, float radius, float fallback) => distance > Reach(radius, fallback);

        /// <summary>
        /// How far apart the fighters may get before the duel is called off, derived from the ring
        /// rather than set on its own.
        ///
        /// Two people at opposite edges of the ring are two radii apart, so any fixed figure below
        /// that ends duels that never left the arena. Derived, it cannot disagree with the ring it
        /// guards.
        /// </summary>
        internal static float OpponentRange(float radius) => radius * 2f + 15f;

        /// <summary>
        /// What leaving the ring means. Once blows count, stepping out concedes: the ring is the
        /// fight, and leaving it is a way of leaving the fight. Before that, during the countdown,
        /// nothing has been fought over, so it only calls the duel off.
        /// </summary>
        internal static EndReason Stray(bool begun) => begun ? EndReason.LeftRing : EndReason.OutOfRange;
    }
}
