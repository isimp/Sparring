using System;
using System.Collections.Generic;
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

        /// <summary>When the agreed pairing was first found missing, or zero while it holds.</summary>
        private static double _unpairedSince;

        /// <summary>How long to wait for a result to explain a pairing that has gone.</summary>
        private const double UnpairedGrace = 2.0;

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

        /// <summary>
        /// The ring the last duel was fought in, so a rematch is the same fight again. Taken from
        /// the agreed terms rather than from whatever was typed, so it is the same on both sides
        /// and a rematch asked for by either fighter proposes the same ring.
        /// </summary>
        private static float _lastRadius;

        /// <summary>
        /// How long after an invitation ends without a duel before the same two players can
        /// exchange another. Every shown challenge plays a sound and puts a prompt on screen, so
        /// without a pause a declined challenger could repeat it as fast as they can type.
        /// </summary>
        private const double RepeatSeconds = 15.0;

        /// <summary>
        /// Players we recently had an invitation with, and when that pause runs out. Kept on both
        /// sides: the challenger's copy stops an ordinary client at the source, and the challenged
        /// player's copy stops one that ignores its own.
        /// </summary>
        private static readonly Dictionary<ZDOID, double> _resting = new Dictionary<ZDOID, double>();

        public static bool Active => !_opponent.IsNone();
        public static ZDOID Opponent => _opponent;

        /// <summary>
        /// Whether this duel is the solo one. Two things read it: the tally, which has no opponent
        /// to attribute blows to, and creature neutrality, which is suspended so that there is
        /// something willing to hit you.
        /// </summary>
        public static bool Practising { get; private set; }
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
                if (reason == EndReason.LeftRing) Concede(me, EndReason.LeftRing);
                else EndLocal(reason, notify: true);
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
                {
                    Rest(_outgoing);
                    ClearOutgoing(Messages.Unanswered, _outgoingName);
                }
                else if (Drifted(me, _outgoing, _outgoingRadius))
                {
                    // Reaches the challenged player as a withdrawal, so their prompt goes too.
                    Link.SendDecline(_outgoing);
                    Rest(_outgoing);
                    ClearOutgoing(Messages.YouWalkedOff, _outgoingName);
                }
            }

            if (!_incoming.IsNone())
            {
                if (now > _incomingUntil)
                {
                    Rest(_incoming);
                    ClearIncoming(Messages.Expired, _incomingName);
                }
                else if (Drifted(me, _incoming, _incomingRadius))
                {
                    Link.SendBusy(_incoming, Busy.TooFar);
                    Rest(_incoming);
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
                _unpairedSince = 0.0;
            }
            else
            {
                // Right after a handshake the other side's stamp may not have reached us yet, so
                // allow a short window to line up.
                if (!_everPaired) return now <= _pairDeadline;

                // The pairing has gone after being agreed, which means the duel is over, but not
                // why. The other side drops its lease as part of ending, and a result explaining
                // the ending is on its way; waiting briefly lets it arrive and name a winner
                // rather than recording a duel that merely lost track of itself.
                //
                // This holds no protection open. Everything protective reads the lease directly,
                // so it has already stopped; only this bookkeeping waits.
                if (_unpairedSince <= 0.0) _unpairedSince = now;
                return now - _unpairedSince < UnpairedGrace;
            }

            // Our own position first. It is the one thing this client knows for certain, and a
            // fighter who has left the ring must not have that read as the other kind of end just
            // because they have also gone far enough to trip the check below.
            //
            // Once blows count, stepping out concedes: the ring is the fight, and leaving it is a
            // way of leaving the fight. Before that, during the countdown, nothing has been fought
            // over, so it only calls the duel off.
            if (DistanceFromCenter(me) > _terms.Radius)
            {
                reason = _terms.Begun ? EndReason.LeftRing : EndReason.OutOfRange;
                return false;
            }

            var theirs = ZDOMan.instance?.GetZDO(_opponent);
            if (theirs == null) return false;

            // The opponent's position, which this client only knows second-hand. Leaving the ring
            // on their side is their client's to concede; this only catches an opponent who has
            // gone somewhere no ring could put them, and calls it off.
            if (Vector3.Distance(me.transform.position, theirs.GetPosition()) > Plugin.OpponentRangeFor(_terms.Radius))
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

            var name = target.GetPlayerName();
            var id = target.GetZDOID();

            var mine = Readiness(me, radius);
            if (mine != Busy.None) { Say(YouCannot(mine)); return; }

            if (Resting(id)) { Say(string.Format(Messages.WaitBeforeAgain, name)); return; }

            // Someone further off than the proposed ring would cancel on arrival, so it is not
            // sent. This is also what keeps a rematch to someone standing near you.
            if (Drifted(me, id, radius)) { Say(string.Format(Messages.TooFarAway, name)); return; }

            // The two parts of the other player's state that can be read from here. The rest —
            // combat, sitting, sleeping — is only known on their own machine, and is checked there
            // when the challenge arrives.
            if (target.IsDead()) { Say(string.Format(Messages.TheyBusy, name)); return; }
            if (target.IsPVPEnabled()) { Say(string.Format(Messages.TheyPvpOn, name)); return; }

            _outgoing = id;
            _outgoingName = name;
            _outgoingRadius = radius;
            _outgoingUntil = Lease.Now + Plugin.InviteSeconds;

            // No message: the waiting panel appears this frame with the same information.
            Link.SendChallenge(target, radius);
        }

        public static void ReceiveChallenge(ZDOID challenger, string name, float radius)
        {
            var me = Player.m_localPlayer;
            if (me == null || challenger.IsNone()) return;

            radius = Plugin.ClampRadius(radius);

            // Already on screen. Showing it again would only replay the sound.
            if (HasInvite && _incoming == challenger) return;

            // Everything that can turn a challenge down is decided here, on the machine it was
            // sent to, before any sound or prompt. The challenger's own checks are a courtesy that
            // spares a round trip; these are the ones a modified client cannot skip, and a refusal
            // here costs the player being challenged nothing — they never see it.
            var refusal = Refusal(me, challenger, radius);
            if (refusal == Busy.None && _outgoing == challenger)
            {
                // They challenged us while we were challenging them. Take it as both saying yes.
                Accept(challenger, name, radius);
                return;
            }

            if (refusal != Busy.None)
            {
                Link.SendBusy(challenger, refusal);
                return;
            }

            _incoming = challenger;
            _incomingName = Clean(name);
            _incomingRadius = radius;
            _incomingUntil = Lease.Now + Plugin.InviteSeconds;

            // The invite panel shows the name, keys and countdown; the sound draws attention to it.
            Sound.Challenge();
        }

        /// <summary>
        /// Why a challenge from this player should not be shown, or <see cref="Busy.None"/> if it
        /// should.
        /// </summary>
        private static Busy Refusal(Player me, ZDOID challenger, float radius)
        {
            if (Active) return Busy.Dueling;

            // One offer on screen at a time; a second one is refused rather than replacing it.
            if (HasInvite && _incoming != challenger) return Busy.Answering;

            if (Resting(challenger)) return Busy.Cooldown;
            if (Drifted(me, challenger, radius)) return Busy.TooFar;

            return Readiness(me, radius);
        }

        /// <summary>
        /// Whether this player is free to start a duel, and if not, why. The same test whether
        /// they are challenging, being challenged, or accepting, so the three cannot disagree.
        ///
        /// Being out of combat is the game's own rule for switching PvP, ten seconds without a
        /// fight, so a duel cannot be opened as an escape from one already under way. Having PvP
        /// on is refused because it would expose the fighters to everyone, which is the thing a
        /// duel is meant to avoid.
        /// </summary>
        private static Busy Readiness(Player me, float radius)
        {
            if (me.IsDead() || me.InCutscene() || me.IsTeleporting()) return Busy.Occupied;
            if (me.IsAttached() || me.InBed() || me.IsSleeping()) return Busy.Occupied;
            if (me.IsPVPEnabled()) return Busy.PvpOn;
            if (Plugin.RequireOutOfCombat && !me.CanSwitchPVP()) return Busy.InCombat;
            if (!GroundIsClear(me, radius)) return Busy.CreaturesNear;
            return Busy.None;
        }

        /// <summary>What to tell this player when they are the one who is not free.</summary>
        private static string YouCannot(Busy why)
        {
            switch (why)
            {
                case Busy.InCombat: return Messages.InCombat;
                case Busy.PvpOn: return Messages.PvpOn;
                case Busy.CreaturesNear: return Messages.CreaturesAbout;
                default: return Messages.YouAreBusy;
            }
        }

        public static void Accept()
        {
            if (_incoming.IsNone()) { Say(Messages.NoChallenge); return; }

            // Accepting is refused, not the challenge: it stays on screen, so it can still be
            // taken once whatever is in the way has cleared.
            var me = Player.m_localPlayer;
            if (me == null) return;

            var mine = Readiness(me, _incomingRadius);
            if (mine != Busy.None) { Say(YouCannot(mine)); return; }

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
            Rest(_incoming);
            ClearIncoming(Messages.YouDeclined, _incomingName);
        }

        public static void Withdraw()
        {
            if (_outgoing.IsNone()) { Say(Messages.NoneOut); return; }

            Link.SendDecline(_outgoing);
            Rest(_outgoing);
            ClearOutgoing(Messages.YouWithdrew, _outgoingName);
        }

        public static void ReceiveReply(ZDOID responder, bool accepted, Lease.Terms terms)
        {
            // A "no" from the player who challenged us: they withdrew, so the prompt goes.
            if (!accepted && !_incoming.IsNone() && _incoming == responder)
            {
                Rest(responder);
                ClearIncoming(Messages.TheyWithdrew, _incomingName);
                return;
            }

            if (_outgoing.IsNone() || _outgoing != responder)
            {
                // A "yes" to a challenge we have since withdrawn or let lapse. Their side has
                // already started a countdown for a duel that is not happening, so tell them now
                // rather than leave it to run until the pairing gives up. Only answered for
                // someone we did just have a challenge with, so this cannot be used to make us
                // send messages to anyone at all.
                if (accepted && Resting(responder)) Link.SendResult(responder, EndReason.Withdrew);
                return;
            }

            var name = _outgoingName;
            ClearOutgoing(null, null);

            if (!accepted)
            {
                Rest(responder);
                Say(string.Format(Messages.TheyDeclined, name));
                return;
            }

            // The terms are adopted as they arrive, not recomputed. Two machines each working out
            // "the middle" from positions a moment apart would draw two different rings.
            terms.Radius = Plugin.ClampRadius(terms.Radius);
            terms.StartAtMs = SaneStart(terms.StartAtMs);
            BeginWith(responder, name, terms);
        }

        /// <summary>
        /// A challenge of ours turned down before it was shown, with the reason. A refusal for
        /// repeating too soon also starts our own pause, so this client stops asking.
        /// </summary>
        public static void ReceiveBusy(ZDOID from, Busy why)
        {
            if (_outgoing.IsNone() || _outgoing != from) return;

            var name = _outgoingName;
            ClearOutgoing(null, null);
            if (why == Busy.Cooldown) Rest(from);

            Say(string.Format(TheyCannot(why), name));
        }

        /// <summary>What to tell a challenger about why the other player is not free.</summary>
        private static string TheyCannot(Busy why)
        {
            switch (why)
            {
                case Busy.InCombat: return Messages.TheyInCombat;
                case Busy.PvpOn: return Messages.TheyPvpOn;
                case Busy.CreaturesNear: return Messages.TheyCreaturesAbout;
                case Busy.Dueling: return Messages.TheyDueling;
                case Busy.Answering: return Messages.TheyAnswering;
                case Busy.Cooldown: return Messages.WaitBeforeAgain;
                case Busy.TooFar: return Messages.TooFarAway;
                default: return Messages.TheyBusy;
            }
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
            _unpairedSince = 0.0;
            _pairDeadline = Lease.Now + Plugin.PairGraceSeconds;
            _nextRenew = 0.0;

            _lastFoe = opponent;
            _lastFoeName = _opponentName;
            _lastRadius = terms.Radius;

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

            Concede(Player.m_localPlayer, EndReason.Forfeited);
        }

        /// <summary>
        /// Losing without being beaten down: giving up, or leaving the ring. The opponent wins as
        /// they would from a yield, and this side is picked up to the loser's floor.
        /// </summary>
        private static void Concede(Player me, EndReason reason)
        {
            var name = _opponentName;
            var winner = _opponent;

            Scorecard.SetOutcome(DuelOutcome.Lost);
            EndLocal(reason, notify: true);
            Recovery.Restore(me, Plugin.YieldHealth);
            Link.SendAnnounce(winner, name, MyName(), reason);
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
            if (reason.Decided()) Scorecard.SetOutcome(DuelOutcome.Won);

            EndLocal(reason, notify: false);

            // Only an ending somebody won restores health; a duel that was merely called off
            // would otherwise be a free heal.
            if (!reason.Decided()) return;

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
            var decided = reason.Decided();
            Scorecard.End(decided);
            if (decided && !opponent.IsNone())
            {
                Link.SendTally(opponent, Scorecard.TookTotal, Scorecard.TookHits, Scorecard.TookBiggest);
            }

            _opponent = ZDOID.None;
            _opponentName = "";
            _everPaired = false;
            _unpairedSince = 0.0;
            _terms = default(Lease.Terms);

            ArenaRing.Refresh();
            Practising = false;
            Lease.AllowSelfPair = false;

            // Before the lease goes, not after. Dropping the lease is itself what tells the other
            // side the duel is over, and it says nothing about why: if it reaches them first their
            // own tick ends the duel as a lapsed pairing, which has no winner, so the result is
            // sent first and arrives first.
            if (notify && !opponent.IsNone()) Link.SendResult(opponent, reason);

            if (me != null)
            {
                Lease.Clear(me);
                Recovery.Settle(me);
            }

            // Ends nobody won get their own word here; one somebody won is announced to everyone
            // by the side that lost, so those say nothing extra on this screen.
            if (reason == EndReason.OutOfRange) Say(Messages.OutOfRing);
            else if (reason == EndReason.Withdrew && name.Length > 0) Say(string.Format(Messages.TheyWithdrew, name));
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

        /// <summary>
        /// Lands a real blow on yourself, so a practice card can be filled without waiting for
        /// something to wander past and take an interest.
        ///
        /// It goes through <c>ApplyDamage</c>, which is where the game subtracts the health and
        /// fires the callback the tally listens on, so what lands is a genuine hit rather than a
        /// number written into the card. Armour and resistances are not applied — those happen in
        /// <c>Damage</c>, further up — so the amount asked for is close to the amount that lands.
        ///
        /// A lethal one is worth trying: the hit is recorded as coming from your opponent, which in
        /// a practice duel is you, so it takes the same route a fatal blow takes in a real duel and
        /// should come out as a yield rather than a death.
        /// </summary>
        public static void SelfHit(float amount)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            if (!Practising) { Say("Only during a practice duel. Start one with /duel practice."); return; }

            var hit = new HitData();
            hit.m_damage.m_blunt = Mathf.Clamp(amount, 1f, 1000f);
            hit.m_point = me.GetCenterPoint();
            hit.m_dir = me.transform.forward;
            hit.SetAttacker(me);

            // The damage path is one other mods patch heavily, and a throw from any of them would
            // otherwise leave the chat window stuck on the command that caused it.
            try
            {
                me.ApplyDamage(hit, showDamageText: true, triggerEffects: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Sparring took a hit but something in the damage path threw: {ex}");
            }
        }

        /// <summary>
        /// A duel against nobody, for testing the parts that do not need a second player.
        ///
        /// Everything local runs for real: the ring, the panel, the countdown, the tally, the
        /// ending, the card, the healing. The lease names this player as their own opponent, which
        /// is the only way to pair alone, and is why that is allowed only here.
        ///
        /// Two things differ. Creature neutrality is suspended, because a tally needs something
        /// willing to hit you, and nothing shields you from a killing blow, so this is as dangerous
        /// as standing there normally. Nothing that needs two machines is covered: neither the
        /// handshake nor the order the ending messages arrive in.
        /// </summary>
        public static void Practice()
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            if (Active) { Say(string.Format(Messages.AlreadyDueling, _opponentName)); return; }

            Practising = true;
            Lease.AllowSelfPair = true;

            var terms = new Lease.Terms
            {
                Center = me.transform.position,
                Radius = Plugin.ArenaRadius,
                StartAtMs = (long)((Lease.Now + Plugin.CountdownSeconds) * 1000.0),
            };

            BeginWith(me.GetZDOID(), "Practice", terms);
            Say("Practice duel. Creatures will still fight you, and can still kill you. " +
                "Take a few hits, then yield or step out of the ring.");
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
                    Challenge(player, _lastRadius > 0f ? _lastRadius : Plugin.ArenaRadius);
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
            _unpairedSince = 0.0;
            _terms = default(Lease.Terms);
            _outgoing = ZDOID.None;
            _incoming = ZDOID.None;
            ArenaRing.Refresh();

            // This is the other way out of a practice duel: dying in one, which it does not protect
            // against. Leaving these set would mean the next real duel counted every blow that
            // landed and left the fighters worth attacking.
            Practising = false;
            Lease.AllowSelfPair = false;

            // The pauses are deliberately kept. This runs whenever there is no body, including
            // between a death and the respawn, and clearing them then would hand anyone who was
            // just declined a free way to start again. They expire on their own.
        }

        /// <summary>Starts the pause before this player and we can exchange another challenge.</summary>
        private static void Rest(ZDOID other)
        {
            if (other.IsNone()) return;

            var now = Lease.Now;
            _resting[other] = now + RepeatSeconds;

            // A handful of entries at most in normal play; drop the lapsed ones as new ones arrive.
            if (_resting.Count <= 16) return;
            foreach (var key in new List<ZDOID>(_resting.Keys))
            {
                if (_resting[key] <= now) _resting.Remove(key);
            }
        }

        private static bool Resting(ZDOID other)
        {
            return _resting.TryGetValue(other, out var until) && Lease.Now < until;
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
