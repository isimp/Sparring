using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sparring
{
    /// <summary>Why a duel stopped. Sent between clients so both sides say the same thing.</summary>
    public enum EndReason
    {
        Yielded,
        Forfeited,
        Declined,
        Withdrew,
        OutOfRange,
        Died,
        LeaseLapsed,

        /// <summary>A fighter stepped outside the ring after the fight began, which concedes it.</summary>
        LeftRing,
    }

    public static class EndReasons
    {
        /// <summary>
        /// Whether a duel ending this way has a winner. These are the endings that restore health,
        /// show the scorecard and celebrate; the rest simply call the duel off.
        /// </summary>
        public static bool Decided(this EndReason reason)
        {
            return reason == EndReason.Yielded || reason == EndReason.Forfeited || reason == EndReason.LeftRing;
        }
    }

    /// <summary>Why a challenge was turned down without being shown. Sent back to the challenger.</summary>
    public enum Busy
    {
        None,
        InCombat,
        PvpOn,
        CreaturesNear,
        Occupied,
        Dueling,
        Answering,
        Cooldown,
        TooFar,
    }

    /// <summary>
    /// The messages two clients exchange to agree a duel is starting or over. Only the handshake
    /// travels this way; the duel's actual truth is the pair of ZDO leases, which every client
    /// reads for itself. A dropped message here can therefore never strand anybody — it can only
    /// mean a duel does not start, or ends a few seconds later than it might have.
    /// </summary>
    public static class Link
    {
        private const string Challenge = "Sparring_Challenge";
        private const string Reply = "Sparring_Reply";
        private const string Result = "Sparring_Result";
        private const string Announce = "Sparring_Announce";
        private const string Tally = "Sparring_Tally";
        private const string Refuse = "Sparring_Busy";

        /// <summary>
        /// Shortest gap between two announcements from the same sender. A duel produces one when
        /// it ends, and a countdown alone keeps two duels further apart than this, so only a flood
        /// is cut.
        /// </summary>
        private const float AnnounceGap = 3f;

        private static readonly Dictionary<long, float> _lastAnnounce = new Dictionary<long, float>();

        /// <summary>
        /// The shape of these messages, bumped whenever one of them changes.
        ///
        /// Carried in the challenge so two builds that cannot understand each other find out at
        /// the handshake and refuse *the duel*, rather than one of them being refused the server.
        /// That is the whole cost of a mismatch: these messages pass between two people who
        /// chose to fight, and nothing else in the mod touches the network.
        ///
        /// <b>Keep the challenge's signature stable.</b> It is the one message that has to survive
        /// a version gap intact, because it is where the gap is detected; anything that changed its
        /// parameters would leave an older client unable to read the very message telling it so.
        /// Everything else may change freely, guarded by this number.
        /// </summary>
        public const int Protocol = 1;

        /// <summary>
        /// The mod version <see cref="Protocol"/> was introduced in, and so the oldest build a
        /// server will admit. Raise it in step with <see cref="Protocol"/>, never on its own — the
        /// point of it being separate from the current version is that releases which do not change
        /// how clients talk cost nobody their connection.
        /// </summary>
        public const string ProtocolSince = "0.1.0";

        private static ZRoutedRpc _registeredOn;

        /// <summary>
        /// Hooks our handlers up to the current session. ZRoutedRpc is rebuilt on each connect, so
        /// this re-registers whenever the instance changes rather than once at load.
        /// </summary>
        public static void EnsureRegistered()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            rpc.Register<ZDOID, string, float, int>(Challenge, OnChallenge);
            rpc.Register<ZDOID, bool, Vector3, float, long>(Reply, OnReply);
            rpc.Register<ZDOID, int>(Result, OnResult);
            rpc.Register<ZDOID, string, string, int>(Announce, OnAnnounce);
            rpc.Register<ZDOID, float, int, float>(Tally, OnTally);
            rpc.Register<ZDOID, int>(Refuse, OnBusy);
            _registeredOn = rpc;
            _lastAnnounce.Clear();
        }

        public static void SendChallenge(Player target, float radius)
        {
            var me = Player.m_localPlayer;
            if (me == null || target == null) return;

            var peer = Lease.OwnerOf(target.GetZDOID());
            if (peer == 0L) return;

            ZRoutedRpc.instance?.InvokeRoutedRPC(peer, Challenge, me.GetZDOID(), me.GetPlayerName(), radius, Protocol);
        }

        /// <summary>
        /// The answer, carrying the terms. The side that accepts decides them — it is the one that
        /// knows both positions at the moment the duel is struck — and the challenger adopts what
        /// comes back rather than computing its own, so there is one authority and no drift.
        /// </summary>
        public static void SendReply(ZDOID challenger, bool accepted, in Lease.Terms terms)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            var peer = Lease.OwnerOf(challenger);
            if (peer == 0L) return;

            ZRoutedRpc.instance?.InvokeRoutedRPC(peer, Reply,
                me.GetZDOID(), accepted, terms.Center, terms.Radius, terms.StartAtMs);
        }

        /// <summary>
        /// A "no" to the other party of a pending challenge, from either side: the challenged
        /// player declining, or the challenger withdrawing. The receiver tells the two apart by
        /// which way round the challenge ran.
        /// </summary>
        public static void SendDecline(ZDOID challenger)
        {
            SendReply(challenger, accepted: false, default(Lease.Terms));
        }

        /// <summary>
        /// Turns a challenge down before it is shown, and says why. A new message rather than a
        /// field on the reply, so the reply keeps its shape; an older build has no handler for it
        /// and its challenge simply waits out its timer.
        /// </summary>
        public static void SendBusy(ZDOID challenger, Busy why)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            var peer = Lease.OwnerOf(challenger);
            if (peer == 0L) return;

            ZRoutedRpc.instance?.InvokeRoutedRPC(peer, Refuse, me.GetZDOID(), (int)why);
        }

        private static void OnBusy(long sender, ZDOID from, int why)
        {
            var reason = Enum.IsDefined(typeof(Busy), why) ? (Busy)why : Busy.Occupied;
            Duel.ReceiveBusy(from, reason);
        }

        public static void SendResult(ZDOID opponent, EndReason reason)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            var peer = Lease.OwnerOf(opponent);
            if (peer == 0L) return;

            ZRoutedRpc.instance?.InvokeRoutedRPC(peer, Result, me.GetZDOID(), (int)reason);
        }

        /// <summary>
        /// Tells everyone how it ended. Sent by the side that lost, which is the one that knows: a
        /// yield is decided on the yielder's own machine.
        ///
        /// The peer id must be passed explicitly. The short overload that takes only a method name
        /// routes to <c>GetServerPeerID()</c>, not to everybody — so it reaches the host alone, and
        /// on a hosted game that means the announcement lands for exactly one player. Addressed to
        /// <c>Everybody</c> the router both sends it out and runs it here, so the loser renders the
        /// same line by the same path as everyone else.
        /// </summary>
        public static void SendAnnounce(ZDOID winnerId, string winner, string loser, EndReason reason)
        {
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, Announce, winnerId, winner, loser, (int)reason);
        }

        private static void OnChallenge(long sender, ZDOID challenger, string name, float radius, int protocol)
        {
            if (protocol != Protocol)
            {
                // Their Sparring speaks a different dialect. Turn the offer down and say why, to
                // whichever of us can be told — the decline reaches them, the reason reaches us.
                SendDecline(challenger);
                Duel.Say(Messages.VersionGap);
                return;
            }

            Duel.ReceiveChallenge(challenger, name, radius);
        }

        private static void OnReply(long sender, ZDOID responder, bool accepted, Vector3 center, float radius, long startAt)
        {
            var terms = new Lease.Terms { Center = center, Radius = radius, StartAtMs = startAt };
            Duel.ReceiveReply(responder, accepted, terms);
        }

        private static void OnResult(long sender, ZDOID from, int reason)
        {
            Duel.ReceiveResult(from, Clamp(reason));
        }

        /// <summary>
        /// Hands the opponent our half of the reckoning: what we took, from them.
        ///
        /// A new message rather than a field added to an existing one, on purpose. Changing a
        /// signature would make older clients unable to read messages they otherwise understand —
        /// and with the version gate in place, that means being kept off the server entirely. A new
        /// name simply has no handler on an older build, so the message is dropped and the card
        /// shows one half. A summary is not worth anybody's connection.
        /// </summary>
        public static void SendTally(ZDOID opponent, float took, int hits, float biggest)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            var peer = Lease.OwnerOf(opponent);
            if (peer == 0L) return;

            ZRoutedRpc.instance?.InvokeRoutedRPC(peer, Tally, me.GetZDOID(), took, hits, biggest);
        }

        private static void OnTally(long sender, ZDOID from, float took, int hits, float biggest)
        {
            Duel.ReceiveTally(from, took, hits, biggest);
        }

        private static void OnAnnounce(long sender, ZDOID winnerId, string winner, string loser, int reason)
        {
            // An announcement is a broadcast any client can send, and it lands in everyone's chat.
            // Rate-limited per sender so it cannot be used to flood it.
            var now = Time.realtimeSinceStartup;
            if (_lastAnnounce.TryGetValue(sender, out var at) && now - at < AnnounceGap) return;
            _lastAnnounce[sender] = now;

            var why = Clamp(reason);
            Announcer.Show(winner, loser, why);

            // Only a duel somebody actually won gets a celebration. Ending because one of them
            // fell down a cliff is not a victory and should not look like one.
            if (why.Decided()) Victory.Celebrate(winnerId);
        }

        /// <summary>
        /// Anything off the wire is somebody else's data, so a reason outside the enum becomes the
        /// vaguest one rather than an undefined value cast into it.
        /// </summary>
        private static EndReason Clamp(int reason)
        {
            return Enum.IsDefined(typeof(EndReason), reason) ? (EndReason)reason : EndReason.LeaseLapsed;
        }
    }
}
