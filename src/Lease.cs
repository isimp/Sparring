using System;
using HarmonyLib;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// A duel's presence on the network: a short lease on each duelist's own ZDO naming who they
    /// are fighting and until when.
    ///
    /// Deliberately not a flag. A flag has to be cleared, and anything that clears a flag can fail
    /// to run — a disconnect, an alt-F4, a crash, an exception of ours — which would leave someone
    /// permanently unattackable. A lease has to be renewed instead, so every one of those failures
    /// ends the duel by itself within <see cref="LeaseSeconds"/> with nothing left behind. Nothing
    /// here relies on cleanup code getting the chance to run.
    ///
    /// The clock is <c>ZNet.GetTimeSeconds</c>, which is the server's own time broadcast to every
    /// client over the NetTime RPC, so two machines agree on when a lease has run out.
    /// </summary>
    internal static class Lease
    {
        private const string KeyOpponent = "sparring_opponent";
        private const string KeyUntil = "sparring_until";
        private const string KeyStart = "sparring_start";
        private const string KeyCenter = "sparring_center";
        private const string KeyRadius = "sparring_radius";

        /// <summary>How long a stamp stays good for, in seconds. See <see cref="RenewSeconds"/>.</summary>
        public const double LeaseSeconds = 5.0;

        /// <summary>
        /// How often a live duel re-stamps. Comfortably inside the lease so an ordinary network
        /// hiccup does not end a duel, but short enough that a real disconnect ends one quickly.
        /// </summary>
        public const double RenewSeconds = 1.0;

        /// <summary>
        /// Whether a lease may name its holder as its own opponent. Off except during a practice
        /// duel, which is the only case where there is nobody else to name.
        /// </summary>
        internal static bool AllowSelfPair;

        /// <summary>
        /// Server time in seconds, or 0 before there is a network clock. Zero is earlier than any
        /// start time, so no countdown reads as finished then.
        /// </summary>
        public static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        /// <summary>
        /// Writes our half of the lease. Only ever called on our own player, whose ZDO we own.
        /// </summary>
        public static void Stamp(Player me, ZDOID opponent)
        {
            var zdo = ZdoOf(me);
            if (zdo == null || !zdo.IsOwner()) return;

            zdo.Set(KeyOpponent, opponent);
            zdo.Set(KeyUntil, UntilFor(Now));
        }

        /// <summary>When a lease stamped at this server time runs out, in the milliseconds the ZDO stores.</summary>
        internal static long UntilFor(double now) => (long)((now + LeaseSeconds) * 1000.0);

        /// <summary>
        /// The things about a duel both sides have to agree on exactly: where the ring is, how big
        /// it is, and the moment blows start counting. Written once when a duel is struck rather
        /// than on every renewal, and read from the network so neither client can drift from the
        /// other's idea of them.
        /// </summary>
        public struct Terms
        {
            public Vector3 Center;
            public float Radius;
            public long StartAtMs;

            /// <summary>Seconds until blows count, or zero once they do.</summary>
            public double CountdownLeft => CountdownLeftAt(Now);

            /// <summary>Whether blows count by now; see <see cref="BegunAt"/>.</summary>
            public bool Begun => BegunAt(Now);

            /// <summary>Seconds until blows count at this server time, or zero once they do.</summary>
            internal double CountdownLeftAt(double now) => Math.Max(0.0, StartAtMs / 1000.0 - now);

            /// <summary>
            /// Whether blows count at this server time. Terms we never received read as "not
            /// begun", not as "begun long ago". An absent start time is zero, and zero is in the
            /// past — so this asks for a real one first, and a duel whose terms never arrived is one
            /// in which no blow ever counts.
            /// </summary>
            internal bool BegunAt(double now) => StartAtMs > 0L && now * 1000.0 >= StartAtMs;
        }

        /// <summary>
        /// Writes the agreed terms. Separate from <see cref="Stamp"/> because these do not change
        /// for the life of a duel, and rewriting them every second would put three values on the
        /// wire a second for no reason.
        /// </summary>
        public static void SetTerms(Player me, in Terms terms)
        {
            var zdo = ZdoOf(me);
            if (zdo == null || !zdo.IsOwner()) return;

            zdo.Set(KeyCenter, terms.Center);
            zdo.Set(KeyRadius, terms.Radius);
            zdo.Set(KeyStart, terms.StartAtMs);
        }

        /// <summary>The terms as this ZDO has them. Defaults are harmless: a zero radius reads as "outside the ring".</summary>
        public static Terms ReadTerms(ZDO zdo)
        {
            var terms = default(Terms);
            if (zdo == null) return terms;

            terms.Center = zdo.GetVec3(KeyCenter, Vector3.zero);
            terms.Radius = zdo.GetFloat(KeyRadius, 0f);
            terms.StartAtMs = zdo.GetLong(KeyStart, 0L);
            return terms;
        }

        public static Terms ReadTerms(Character c) => ReadTerms(ZdoOf(c));

        /// <summary>
        /// Drops our half. Best effort and not load-bearing: if this never runs the lease lapses on
        /// its own, which is the whole point of storing an expiry rather than a flag.
        /// </summary>
        public static void Clear(Player me)
        {
            var zdo = ZdoOf(me);
            if (zdo == null || !zdo.IsOwner()) return;

            zdo.Set(KeyOpponent, ZDOID.None);
            zdo.Set(KeyUntil, 0L);
            zdo.Set(KeyStart, 0L);
            zdo.Set(KeyRadius, 0f);
        }

        /// <summary>One side's lease as its ZDO holds it. A lease never written reads as naming nobody, with no expiry.</summary>
        private static Claim<ZDOID> ClaimOf(ZDO zdo) => new Claim<ZDOID>(zdo.GetZDOID(KeyOpponent), zdo.GetLong(KeyUntil, 0L));

        /// <summary>
        /// Whether this ZDO is in a duel that its opponent agrees to, read off both ZDOs as this
        /// game has them; see <see cref="Agreed"/>. A ZDO this game cannot resolve has no lease.
        /// </summary>
        public static bool Corroborated(ZDO mine, out ZDOID opponentId)
        {
            opponentId = ZDOID.None;
            if (mine == null || ZDOMan.instance == null) return false;

            return Agreed(mine.m_uid, ClaimOf(mine), id => ZDOMan.instance.GetZDO(id) is ZDO theirs ? ClaimOf(theirs) : (Claim<ZDOID>?)null,
                Now, AllowSelfPair, out opponentId);
        }

        /// <summary>
        /// Whether the holder of <paramref name="mine"/> is in a duel its opponent agrees to: both
        /// leases unexpired at this server time, and each naming the other. A one-sided stamp counts
        /// for nothing, so writing your own ZDO cannot buy you anything on its own.
        ///
        /// Every uncertainty answers false — an opponent <paramref name="find"/> cannot see, a lease
        /// naming nobody, one that has run out. Not knowing means not dueling, which means
        /// attackable as usual. Nobody is the id's default, as it is for a ZDOID.
        /// </summary>
        internal static bool Agreed<TId>(TId holder, in Claim<TId> mine, Func<TId, Claim<TId>?> find, double now, bool allowSelfPair, out TId opponent)
            where TId : struct, IEquatable<TId>
        {
            opponent = default;
            if (!mine.LiveAt(now)) return false;

            // A lease naming its own holder agrees with itself, which would let a single client
            // grant itself everything a duel grants. Only the practice duel, which has nobody else
            // to pair with, is allowed to do it.
            var claimed = mine.Opponent;
            if (claimed.Equals(holder) && !allowSelfPair) return false;

            var theirs = find(claimed);
            if (theirs == null || !theirs.Value.LiveAt(now)) return false;
            if (!theirs.Value.Opponent.Equals(holder)) return false;

            opponent = claimed;
            return true;
        }

        /// <summary>
        /// A start time off the network decides when we may be hit, so it does not get to be
        /// arbitrary. Far in the future would be a duel that never starts and a ring that never
        /// lifts; in the past would skip the countdown entirely. Anything outside a sane window,
        /// from now to ten seconds past a full countdown, becomes a fresh countdown from now.
        /// </summary>
        internal static long SaneStart(long startAtMs, double now, double countdownSeconds)
        {
            var nowMs = now * 1000.0;
            var ceiling = nowMs + (countdownSeconds + 10.0) * 1000.0;

            if (startAtMs < nowMs || startAtMs > ceiling)
                return (long)(nowMs + countdownSeconds * 1000.0);

            return startAtMs;
        }

        /// <summary>Convenience over <see cref="Corroborated(ZDO, out ZDOID)"/> for a live character.</summary>
        public static bool Corroborated(Character c, out ZDOID opponentId)
        {
            opponentId = ZDOID.None;
            return c != null && Corroborated(ZdoOf(c), out opponentId);
        }

        /// <summary>Whether these two specific characters are each other's corroborated opponent.</summary>
        public static bool ArePaired(Character a, Character b)
        {
            if (a == null || b == null || a == b) return false;
            var mine = ZdoOf(a);
            if (mine == null || ZDOMan.instance == null) return false;
            return Paired(mine.m_uid, b.GetZDOID(), Find, Now, AllowSelfPair);
        }

        /// <summary>A lease as the ZDO this game has for the id holds it, or none for a ZDO it cannot resolve.</summary>
        private static Claim<ZDOID>? Find(ZDOID id) => ZDOMan.instance.GetZDO(id) is ZDO zdo ? ClaimOf(zdo) : (Claim<ZDOID>?)null;

        /// <summary>
        /// Whether these two are each other's opponent, by both leases as <paramref name="find"/>
        /// reads them (see <see cref="Agreed"/>). Nobody is paired with themselves this way, not even
        /// in practice, so a practice duel opens no attack on yourself.
        /// </summary>
        internal static bool Paired<TId>(TId a, TId b, Func<TId, Claim<TId>?> find, double now, bool allowSelfPair)
            where TId : struct, IEquatable<TId>
        {
            if (a.Equals(b)) return false;
            var mine = find(a);
            return mine != null && Agreed(a, mine.Value, find, now, allowSelfPair, out var opponent) && opponent.Equals(b);
        }

        /// <summary>
        /// Paired *and* past the countdown — the test for whether blows between these two should
        /// land. Kept apart from <see cref="ArePaired"/> so the countdown fails closed: until the
        /// agreed start time arrives this is false, so a countdown that never finishes is a duel in
        /// which no damage was ever enabled, rather than one that has to be stopped.
        ///
        /// The start time comes off the network and is compared against the server's clock, so both
        /// clients begin on the same instant rather than each on its own timer.
        /// </summary>
        public static bool AreFighting(Character a, Character b)
        {
            return ArePaired(a, b) && ReadTerms(a).Begun;
        }

        /// <summary>
        /// The ZDO behind a character, or null if it has none yet or has been torn down.
        ///
        /// Reads <c>m_nview</c> directly rather than going through GetComponent: this sits under
        /// the IsEnemy patch, which the game calls on every candidate target every tick.
        /// </summary>
        public static ZDO ZdoOf(Character c)
        {
            if (c == null) return null;

            try
            {
                if (_nview == null) _nview = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
                var view = _nview(c);
                return view != null && view.IsValid() ? view.GetZDO() : null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not reach a character's network view: {ex.Message}");
                return null;
            }
        }

        private static AccessTools.FieldRef<Character, ZNetView> _nview;

        /// <summary>The loaded player behind a ZDOID, or null if they are not instantiated here.</summary>
        public static Player FindPlayer(ZDOID id)
        {
            if (id.IsNone()) return null;

            foreach (var player in Player.GetAllPlayers())
            {
                if (player != null && player.GetZDOID() == id) return player;
            }
            return null;
        }

        /// <summary>The peer we would send a message to for the owner of this ZDO.</summary>
        public static long OwnerOf(ZDOID id)
        {
            var zdo = ZDOMan.instance?.GetZDO(id);
            return zdo?.GetOwner() ?? 0L;
        }

    }

    /// <summary>
    /// One fighter's lease as read off their ZDO: whom it names, and until when in the server-time
    /// milliseconds the ZDO stores. Kept apart from the ZDO so the rule that two of them make a
    /// duel (<see cref="Lease.Agreed"/>) can be checked without a game.
    /// </summary>
    internal readonly struct Claim<TId> where TId : struct, IEquatable<TId>
    {
        internal Claim(TId opponent, long untilMs)
        {
            Opponent = opponent;
            UntilMs = untilMs;
        }

        /// <summary>Whom the lease names; the id's default for nobody.</summary>
        internal TId Opponent { get; }

        /// <summary>When the lease runs out, or zero for one never written or cleared.</summary>
        internal long UntilMs { get; }

        /// <summary>
        /// Whether the lease still holds at this server time. Says nothing about whether the other
        /// side agrees; for that, see <see cref="Lease.Agreed"/>.
        /// </summary>
        internal bool LiveAt(double now) => UntilMs > 0L && now * 1000.0 < UntilMs && !Opponent.Equals(default(TId));
    }
}
