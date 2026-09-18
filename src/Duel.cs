using System;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// The local player's side of a duel: the handshake, the countdown, the renewal, and every way
    /// one can end.
    ///
    /// This class holds intent. It never holds the answer to "is anyone protected right now" —
    /// that is always read fresh from <see cref="Lease"/> by whoever is asking. Keeping those apart
    /// is what makes a wedged duel impossible: if this state machine got stuck, the leases would
    /// still lapse and everything protective would switch itself off.
    /// </summary>
    public static class Duel
    {
        private static ZDOID _opponent = ZDOID.None;
        private static string _opponentName = "";
        private static Lease.Terms _terms;
        private static bool _everPaired;
        private static double _pairDeadline;
        private static double _nextRenew;

        private static ZDOID _outgoing = ZDOID.None;
        private static string _outgoingName = "";
        private static double _outgoingUntil;
        private static float _outgoingRadius;

        private static ZDOID _incoming = ZDOID.None;
        private static string _incomingName = "";
        private static double _incomingUntil;
        private static float _incomingRadius;

        private static ZDOID _lastFoe = ZDOID.None;
        private static string _lastFoeName = "";

        public static bool Active => !_opponent.IsNone();
        public static ZDOID Opponent => _opponent;
        public static string OpponentName => _opponentName;
        public static bool HasInvite => !_incoming.IsNone();
        public static bool HasOutgoing => !_outgoing.IsNone();
        public static string InviteFrom => _incomingName;
        public static string AwaitingFrom => _outgoingName;
        public static Lease.Terms Terms => _terms;

        /// <summary>Seconds left on whichever invitation is on screen, for the HUD to count down.</summary>
        public static double InviteSecondsLeft =>
            HasInvite ? Mathf.Max(0f, (float)(_incomingUntil - Lease.Now))
            : HasOutgoing ? Mathf.Max(0f, (float)(_outgoingUntil - Lease.Now))
            : 0.0;

        /// <summary>True between the handshake and the first blow counting.</summary>
        public static bool CountingDown => Active && !_terms.Begun;

        /// <summary>
        /// The moment the count runs out, held just long enough to read.
        ///
        /// Without this the "Fight!" frame would be unreachable: the countdown reaching zero and
        /// the duel beginning are the same instant, so the panel would switch away before it could
        /// ever draw.
        /// </summary>
        public static bool ShowingStart =>
            Active && _terms.Begun && Lease.Now - _terms.StartAtMs / 1000.0 < 1.0;

        public static void Tick()
        {
            Link.EnsureRegistered();

            var me = Player.m_localPlayer;
            if (me == null)
            {
                // No body to protect and no ZDO to write. Forget everything; the lease on whatever
                // character we had is already lapsing on its own.
                Reset();
                return;
            }

            var now = Lease.Now;
            ExpireInvites(me, now);

            if (!Active) return;

            if (!Valid(me, now, out var reason))
            {
                EndLocal(reason, notify: true);
                return;
            }

            if (now >= _nextRenew)
            {
                Lease.Stamp(me, _opponent);
                _nextRenew = now + Lease.RenewSeconds;
            }
        }

        /// <summary>
        /// Invitations expire with time and with distance. Walking away from a pending challenge
        /// cancels it, so an unanswered challenge cannot turn into a duel somewhere else later.
        /// </summary>
        private static void ExpireInvites(Player me, double now)
        {
            if (!_outgoing.IsNone())
            {
                if (now > _outgoingUntil)
                    ClearOutgoing(Messages.Unanswered, _outgoingName);
                else if (Drifted(me, _outgoing, _outgoingRadius))
                {
                    Link.SendDecline(_outgoing);
                    ClearOutgoing(Messages.YouWalkedOff, _outgoingName);
                }
            }

            if (!_incoming.IsNone())
            {
                if (now > _incomingUntil)
                    ClearIncoming(Messages.Expired, _incomingName);
                else if (Drifted(me, _incoming, _incomingRadius))
                {
                    Link.SendDecline(_incoming);
                    ClearIncoming(Messages.TooFarNow, _incomingName);
                }
            }
        }

        /// <summary>
        /// Whether the other party to a pending challenge has become too far away to fight. An
        /// opponent we can no longer resolve at all counts as gone, which cancels — the safe way
        /// round for something that has not started yet.
        /// </summary>
        private static bool Drifted(Player me, ZDOID other, float radius)
        {
            var zdo = ZDOMan.instance?.GetZDO(other);
            if (zdo == null) return true;

            // Measured against the proposed ring, not the larger distance a live duel tolerates:
            // walking out of the proposed ring is taken as withdrawing.
            var reach = radius > 0f ? radius : Plugin.ArenaRadius;
            if (reach < Plugin.RadiusFloor) reach = Plugin.RadiusFloor;
            return Vector3.Distance(me.transform.position, zdo.GetPosition()) > reach;
        }

        /// <summary>
        /// Every condition a live duel has to keep satisfying. Anything unclear ends it: ending a
        /// duel early only costs a rematch, while keeping one alive wrongly grants protection.
        /// </summary>
        private static bool Valid(Player me, double now, out EndReason reason)
        {
            reason = EndReason.LeaseLapsed;

            if (me.IsDead()) { reason = EndReason.Died; return false; }

            var paired = Lease.Corroborated(me, out var confirmed) && confirmed == _opponent;
            if (paired)
            {
                _everPaired = true;
            }
            else
            {
                // Right after a handshake the other side's stamp may not have reached us yet, so
                // allow a short window to line up. Once we have seen the pair agree even once,
                // losing it means the duel is genuinely over.
                if (_everPaired || now > _pairDeadline) return false;
                return true;
            }

            var theirs = ZDOMan.instance?.GetZDO(_opponent);
            if (theirs == null) return false;

            if (Vector3.Distance(me.transform.position, theirs.GetPosition()) > Plugin.OpponentRangeFor(_terms.Radius))
            {
                reason = EndReason.OutOfRange;
                return false;
            }

            if (DistanceFromCenter(me) > _terms.Radius)
            {
                reason = EndReason.OutOfRange;
                return false;
            }

            return true;
        }

        /// <summary>How far the local player is from the middle of the ring, flat — height is not straying.</summary>
        public static float DistanceFromCenter(Player me)
        {
            if (me == null || _terms.Radius <= 0f) return 0f;

            var here = me.transform.position;
            var there = _terms.Center;
            return new Vector2(here.x - there.x, here.z - there.z).magnitude;
        }

        // ---- starting ----

        public static void Challenge(Player target, float radius)
        {
            var me = Player.m_localPlayer;
            if (me == null || target == null) return;

            if (Active) { Say(string.Format(Messages.AlreadyDueling, _opponentName)); return; }
            if (target == me) { Say(Messages.NotYourself); return; }
            if (HasOutgoing) { Say(string.Format(Messages.AlreadyChallenged, _outgoingName)); return; }

            // The game's own rule for switching PvP: ten seconds out of combat. A duel cannot be
            // opened as an escape from a fight already in progress.
            if (Plugin.RequireOutOfCombat && !me.CanSwitchPVP())
            {
                Say(Messages.InCombat);
                return;
            }

            if (me.IsPVPEnabled())
            {
                Say(Messages.PvpOn);
                return;
            }

            if (!GroundIsClear(me, radius)) { Say(Messages.CreaturesAbout); return; }

            _outgoing = target.GetZDOID();
            _outgoingName = target.GetPlayerName();
            _outgoingRadius = radius;
            _outgoingUntil = Lease.Now + Plugin.InviteSeconds;

            // No message: the waiting panel appears this frame with the same information.
            Link.SendChallenge(target, radius);
        }

        public static void ReceiveChallenge(ZDOID challenger, string name, float radius)
        {
            var me = Player.m_localPlayer;
            if (me == null || challenger.IsNone()) return;

            if (Active)
            {
                // A reply, not a result: the challenger is waiting for an answer, and would ignore
                // a result about a duel they are not in.
                Link.SendDecline(challenger);
                return;
            }

            radius = Plugin.ClampRadius(radius);

            // They challenged us while we were challenging them. Take it as both saying yes.
            if (_outgoing == challenger)
            {
                Accept(challenger, name, radius);
                return;
            }

            if (HasInvite && _incoming != challenger)
            {
                // One offer on screen at a time; a second one is declined rather than replacing it.
                Link.SendDecline(challenger);
                return;
            }

            _incoming = challenger;
            _incomingName = Clean(name);
            _incomingRadius = radius;
            _incomingUntil = Lease.Now + Plugin.InviteSeconds;

            // The invite panel shows the name, keys and countdown; the sound draws attention to it.
            Sound.Challenge();
        }

        public static void Accept()
        {
            if (_incoming.IsNone()) { Say(Messages.NoChallenge); return; }

            var me = Player.m_localPlayer;
            if (me != null && Plugin.RequireOutOfCombat && !me.CanSwitchPVP())
            {
                Say(Messages.InCombat);
                return;
            }

            Accept(_incoming, _incomingName, _incomingRadius);
        }

        /// <summary>
        /// Whether there is anything hostile standing around the ground a duel would be fought on.
        ///
        /// <c>RequireOutOfCombat</c> only knows whether you were recently hit, so a hostile creature
        /// nearby that has not attacked yet would pass it. This uses the game's own
        /// <c>HaveEnemyInRange</c>, which decides by faction, so tames and dvergr do not count.
        /// Measured over the ring, since that is the ground the fight is confined to.
        /// </summary>
        private static bool GroundIsClear(Player me, float radius)
        {
            if (!Plugin.RequireClearGround) return true;

            try
            {
                var reach = radius > 0f ? radius : Plugin.ArenaRadius;
                return !BaseAI.HaveEnemyInRange(me, me.transform.position, reach);
            }
            catch (Exception ex)
            {
                // Not being able to tell is not a reason to refuse a duel.
                Plugin.Log.LogDebug($"Sparring could not check for creatures: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// The side that accepts settles the terms, because it is the only one that knows where
        /// both fighters are standing at the moment the duel is struck. The ring is centred between
        /// the two of them, and the start time is put on the shared server clock rather than each
        /// client running its own countdown, so the first blow becomes legal on the same instant
        /// for both.
        /// </summary>
        private static void Accept(ZDOID challenger, string name, float radius)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            if (!GroundIsClear(me, radius)) { Say(Messages.CreaturesAbout); return; }

            var theirs = ZDOMan.instance?.GetZDO(challenger);
            if (theirs == null)
            {
                ClearIncoming(Messages.Unreachable, Clean(name));
                return;
            }

            var terms = new Lease.Terms
            {
                Center = Vector3.Lerp(me.transform.position, theirs.GetPosition(), 0.5f),
                Radius = Plugin.ClampRadius(radius),
                StartAtMs = (long)((Lease.Now + Plugin.CountdownSeconds) * 1000.0),
            };

            Link.SendReply(challenger, accepted: true, terms);
            ClearIncoming(null, null);
            ClearOutgoing(null, null);
            BeginWith(challenger, name, terms);
        }

        public static void Decline()
        {
            if (_incoming.IsNone()) { Say(Messages.NoChallenge); return; }

            Link.SendDecline(_incoming);
            ClearIncoming(Messages.YouDeclined, _incomingName);
        }

        public static void Withdraw()
        {
            if (_outgoing.IsNone()) { Say(Messages.NoneOut); return; }

            Link.SendDecline(_outgoing);
            ClearOutgoing(Messages.YouWithdrew, _outgoingName);
        }

        public static void ReceiveReply(ZDOID responder, bool accepted, Lease.Terms terms)
        {
            if (_outgoing.IsNone() || _outgoing != responder) return;

            var name = _outgoingName;
            ClearOutgoing(null, null);

            if (!accepted) { Say(string.Format(Messages.TheyDeclined, name)); return; }

            // The terms are adopted as they arrive, not recomputed. Two machines each working out
            // "the middle" from positions a moment apart would draw two different rings.
            terms.Radius = Plugin.ClampRadius(terms.Radius);
            terms.StartAtMs = SaneStart(terms.StartAtMs);
            BeginWith(responder, name, terms);
        }

        /// <summary>
        /// A start time off the network decides when we may be hit, so it does not get to be
        /// arbitrary. Far in the future would be a duel that never starts and a ring that never
        /// lifts; in the past would skip the countdown entirely. Anything outside a sane window
        /// becomes a fresh local countdown instead.
        /// </summary>
        private static long SaneStart(long startAtMs)
        {
            var nowMs = Lease.Now * 1000.0;
            var ceiling = nowMs + (Plugin.CountdownSeconds + 10.0) * 1000.0;

            if (startAtMs < nowMs || startAtMs > ceiling)
                return (long)(nowMs + Plugin.CountdownSeconds * 1000.0);

            return startAtMs;
        }

        private static void BeginWith(ZDOID opponent, string name, Lease.Terms terms)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            _opponent = opponent;
            _opponentName = string.IsNullOrEmpty(name) ? "your opponent" : Clean(name);
            _terms = terms;
            _everPaired = false;
            _pairDeadline = Lease.Now + Plugin.PairGraceSeconds;
            _nextRenew = 0.0;

            _lastFoe = opponent;
            _lastFoeName = _opponentName;

            Lease.SetTerms(me, terms);
            Lease.Stamp(me, opponent);

            // A real duel takes over from any rehearsal standing where you were.
            Preview.StopSparring();
            Preview.ClearRing();

            Scorecard.Begin(_opponentName);
        }

        // ---- ending ----

        /// <summary>Giving up on purpose, with the duel still live.</summary>
        public static void Forfeit()
        {
            if (!Active) { Say(Messages.NotDueling); return; }

            var name = _opponentName;
            var winner = _opponent;
            var me = Player.m_localPlayer;

            Scorecard.SetOutcome(DuelOutcome.Lost);
            EndLocal(EndReason.Forfeited, notify: true);
            Recovery.Restore(me, Plugin.YieldHealth);
            Link.SendAnnounce(winner, name, MyName(), EndReason.Forfeited);
        }

        /// <summary>
        /// Dropped to nothing at the opponent's hands. Called from the CheckDeath patch, before the
        /// game has marked anyone dead.
        /// </summary>
        public static void YieldTo(Player me)
        {
            var name = _opponentName;
            var winner = _opponent;

            // The blow that would have killed you knocks you down instead. Sampled before the duel
            // is torn down, because afterwards there is no opponent left to be knocked away from.
            var away = KnockDirection(me);

            // Ends first — which drops the lease and clears what the fight left on you — then puts
            // the health back, so the restored value is the last word and nothing can read this
            // player as still protected while they stand up.
            Scorecard.SetOutcome(DuelOutcome.Lost);
            EndLocal(EndReason.Yielded, notify: true);
            Recovery.Restore(me, Plugin.YieldHealth);

            // After the health is back: Stagger does nothing for a character the game still sees
            // as finished, and the animation is driven through ZSyncAnimation, so the winner sees
            // it too rather than watching someone simply stop.
            me.Stagger(away);

            Link.SendAnnounce(winner, name, MyName(), EndReason.Yielded);
        }

        /// <summary>
        /// Killed by something that was not your opponent. The duel ends and is announced, so the
        /// other fighter sees why.
        /// </summary>
        public static void DiedOutside()
        {
            if (!Active) return;

            var name = _opponentName;
            var winner = _opponent;
            EndLocal(EndReason.Died, notify: true);
            Link.SendAnnounce(winner, name, MyName(), EndReason.Died);
        }

        /// <summary>
        /// Which way a yielding fighter is pushed: away from their opponent. Stagger turns the
        /// character to look back along this vector, so it is the push, not the facing. Falls back
        /// to their own forward when the opponent cannot be located.
        /// </summary>
        private static Vector3 KnockDirection(Player me)
        {
            var theirs = ZDOMan.instance?.GetZDO(_opponent);
            if (theirs == null) return me.transform.forward;

            var away = me.transform.position - theirs.GetPosition();
            away.y = 0f;
            return away.sqrMagnitude > 0.01f ? away.normalized : me.transform.forward;
        }

        /// <summary>
        /// The opponent's half of the reckoning. Accepted just after our own side closed, which is
        /// when it normally arrives — so it is not gated on the duel still being live, only on it
        /// having been against this person.
        /// </summary>
        public static void ReceiveTally(ZDOID from, float took, int hits, float biggest)
        {
            if (from.IsNone()) return;
            if (from != _opponent && from != _lastFoe) return;

            Scorecard.ReceiveOpponent(took, hits, biggest);
        }

        public static void ReceiveResult(ZDOID from, EndReason reason)
        {
            if (!Active || _opponent != from)
            {
                // A stale result from a duel we have already left. Nothing to undo.
                return;
            }

            // The loser broadcasts the announcement, so the winner only has to take its own side
            // down. Saying anything here as well would double the line on screen.
            var me = Player.m_localPlayer;

            // The same reason means the opposite thing on this side: they are telling us they
            // yielded, which is a win here.
            if (reason == EndReason.Yielded || reason == EndReason.Forfeited)
            {
                Scorecard.SetOutcome(DuelOutcome.Won);
            }

            EndLocal(reason, notify: false);

            // Only an ending somebody won restores health; otherwise walking out of the ring would
            // be a free heal.
            if (reason != EndReason.Yielded && reason != EndReason.Forfeited) return;

            Recovery.Restore(me, Plugin.VictoryHealth);

            // The emote is the one part of winning that only the winner can set going: it is
            // written to their own ZDO, and the game replicates it from there. The burst comes
            // separately, off the broadcast, so that spectators get it too.
            Victory.Emote(me);
        }

        /// <summary>
        /// Takes our side down: lease dropped, status effects cleared, ring hidden, opponent told.
        /// Every one of those is best effort. The lease lapsing is what actually guarantees the
        /// duel is over.
        /// </summary>
        public static void EndLocal(EndReason reason, bool notify)
        {
            var me = Player.m_localPlayer;
            var opponent = _opponent;
            var name = _opponentName;

            // Before the fields are cleared: the card needs the terms for the duration and the
            // opponent for where to send our half. A summary is shown only for a duel somebody won.
            var decided = reason == EndReason.Yielded || reason == EndReason.Forfeited;
            Scorecard.End(decided);
            if (decided && !opponent.IsNone())
            {
                Link.SendTally(opponent, Scorecard.TookTotal, Scorecard.TookHits, Scorecard.TookBiggest);
            }

            _opponent = ZDOID.None;
            _opponentName = "";
            _everPaired = false;
            _terms = default(Lease.Terms);

            ArenaRing.Refresh();

            if (me != null)
            {
                Lease.Clear(me);
                Recovery.Settle(me);
            }

            if (notify && !opponent.IsNone()) Link.SendResult(opponent, reason);

            // Ends nobody won get their own word here; a yield or a forfeit is announced to
            // everyone by the side that lost, so those say nothing extra on this screen.
            if (reason == EndReason.OutOfRange) Say(Messages.OutOfRing);
            else if (reason == EndReason.LeaseLapsed && name.Length > 0) Say(string.Format(Messages.LostTrack, name));
        }

        /// <summary>
        /// Whether a killing blow should become a yield instead. Only the opponent's own hits count:
        /// anything else, such as creatures, falls or drowning, kills as usual.
        /// </summary>
        public static bool ShouldSurvive(Player me)
        {
            if (me == null || !Active) return false;
            if (me != Player.m_localPlayer) return false;

            // Re-read the pair rather than trusting our own state, so a duel this client thinks is
            // running but the network does not is never a reason to survive a hit.
            if (!Lease.Corroborated(me, out var confirmed) || confirmed != _opponent) return false;

            var attacker = Patches.LastAttacker(me);
            return attacker != null && attacker.GetZDOID() == _opponent;
        }

        /// <summary>Re-challenge whoever you last fought, if they are still around.</summary>
        public static void Rematch()
        {
            if (_lastFoe.IsNone()) { Say(Messages.NeverDueled); return; }

            var zdo = ZDOMan.instance?.GetZDO(_lastFoe);
            if (zdo == null) { Say(string.Format(Messages.NotNearby, _lastFoeName)); return; }

            foreach (var player in Player.GetAllPlayers())
            {
                if (player != null && player.GetZDOID() == _lastFoe)
                {
                    Challenge(player, Plugin.ArenaRadius);
                    return;
                }
            }
            Say(string.Format(Messages.NotNearby, _lastFoeName));
        }

        public static void Reset()
        {
            _opponent = ZDOID.None;
            _opponentName = "";
            _everPaired = false;
            _terms = default(Lease.Terms);
            _outgoing = ZDOID.None;
            _incoming = ZDOID.None;
            ArenaRing.Refresh();
        }

        private static string MyName()
        {
            var me = Player.m_localPlayer;
            return me != null ? me.GetPlayerName() : "someone";
        }

        /// <summary>A name off the wire is somebody else's text. Strip markup and cap it.</summary>
        private static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Someone";

            name = name.Replace('<', ' ').Replace('>', ' ').Trim();
            if (name.Length == 0) return "Someone";
            return name.Length > 32 ? name.Substring(0, 32) : name;
        }

        private static void ClearOutgoing(string format, string arg)
        {
            _outgoing = ZDOID.None;
            _outgoingName = "";
            _outgoingRadius = 0f;
            if (format != null) Say(string.Format(format, arg));
        }

        private static void ClearIncoming(string format, string arg)
        {
            _incoming = ZDOID.None;
            _incomingName = "";
            _incomingRadius = 0f;
            if (format != null) Say(string.Format(format, arg));
        }

        internal static void Say(string text, MessageHud.MessageType type = MessageHud.MessageType.Center)
        {
            Player.m_localPlayer?.Message(type, text);
        }
    }
}
