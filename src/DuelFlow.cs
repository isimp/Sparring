using System;

namespace Sparring
{
    /// <summary>
    /// The local player's side of a duel: the handshake, the countdown, the renewal, and every way
    /// one can end, run against a <see cref="IDuelWorld{TId}"/> that stands for the game.
    ///
    /// This class holds intent. It never holds the answer to "is anyone protected right now" —
    /// that is always read fresh from the leases by whoever is asking. Keeping those apart is what
    /// makes a wedged duel impossible: if this state machine got stuck, the leases would still
    /// lapse and everything protective would switch itself off.
    ///
    /// Nothing here needs the game, so the unit tests run two of these against each other, one per
    /// simulated player; the game's own runs in <see cref="Duel"/>. Nobody is the id's default.
    /// </summary>
    internal sealed class DuelFlow<TId> where TId : struct, IEquatable<TId>
    {
        /// <summary>How long to wait for a result to explain a pairing that has gone.</summary>
        private const double UnpairedGrace = 2.0;

        private readonly IDuelWorld<TId> _world;

        private TId _opponent;
        private string _opponentName = "";
        private Lease.Terms _terms;
        private bool _everPaired;
        private double _pairDeadline;
        private double _nextRenew;

        /// <summary>When the agreed pairing was first found missing, or zero while it holds.</summary>
        private double _unpairedSince;

        /// <summary>
        /// The last whole second of countdown a sound was made for. Starts on the second the duel
        /// was struck, which already has its own sound, so the first tick is the one after.
        /// </summary>
        private int _countedTo;

        private TId _outgoing;
        private string _outgoingName = "";
        private double _outgoingUntil;
        private float _outgoingRadius;

        private TId _incoming;
        private string _incomingName = "";
        private double _incomingUntil;
        private float _incomingRadius;

        private TId _lastFoe;
        private string _lastFoeName = "";

        /// <summary>
        /// The ring the last duel was fought in, so a rematch is the same fight again. Taken from
        /// the agreed terms rather than from whatever was typed, so it is the same on both sides
        /// and a rematch asked for by either fighter proposes the same ring.
        /// </summary>
        private float _lastRadius;

        /// <summary>Players we recently had an invitation with that came to nothing, and when each pause runs out.</summary>
        private readonly Pauses<TId> _resting = new Pauses<TId>();

        public DuelFlow(IDuelWorld<TId> world)
        {
            _world = world;
        }

        public bool Active => !None(_opponent);
        public TId Opponent => _opponent;

        /// <summary>
        /// Whether this duel is the solo one. Read by the tally, which has no opponent to
        /// attribute blows to and so counts whatever lands.
        /// </summary>
        public bool Practising { get; private set; }

        /// <summary>How the last duel on this machine ended, so an ending can be told apart afterwards.</summary>
        public EndReason? LastEnd { get; private set; }

        public string OpponentName => _opponentName;
        public bool HasInvite => !None(_incoming);
        public bool HasOutgoing => !None(_outgoing);
        public string InviteFrom => _incomingName;
        public string AwaitingFrom => _outgoingName;
        public Lease.Terms Terms => _terms;

        /// <summary>Seconds left on whichever invitation is on screen, for the HUD to count down.</summary>
        public double InviteSecondsLeft =>
            HasInvite ? Math.Max(0f, (float)(_incomingUntil - _world.Now))
            : HasOutgoing ? Math.Max(0f, (float)(_outgoingUntil - _world.Now))
            : 0.0;

        /// <summary>True between the handshake and the first blow counting.</summary>
        public bool CountingDown => Active && !_terms.BegunAt(_world.Now);

        /// <summary>
        /// The moment the count runs out, held just long enough to read.
        ///
        /// Without this the "Fight!" frame would be unreachable: the countdown reaching zero and
        /// the duel beginning are the same instant, so the panel would switch away before it could
        /// ever draw.
        /// </summary>
        public bool ShowingStart
        {
            get
            {
                var now = _world.Now;
                return Active && _terms.BegunAt(now) && now - _terms.StartAtMs / 1000.0 < 1.0;
            }
        }

        public void Tick()
        {
            if (None(_world.Me))
            {
                // No body to protect and no ZDO to write. Forget everything; the lease on whatever
                // character we had is already lapsing on its own.
                Reset();
                return;
            }

            var now = _world.Now;
            ExpireInvites(now);

            if (!Active) return;

            if (!Valid(now, out var reason))
            {
                if (reason == EndReason.LeftRing) Concede(EndReason.LeftRing);
                else EndLocal(reason, notify: true);
                return;
            }

            if (now >= _nextRenew)
            {
                _world.Stamp(_opponent);
                _nextRenew = now + Lease.RenewSeconds;
            }

            Count();
        }

        /// <summary>
        /// A tick for each second the countdown loses and the bell when it runs out. Read off the
        /// shared start time, so both fighters hear the bell on the same instant without anything
        /// being sent for it.
        /// </summary>
        private void Count()
        {
            var whole = WholeSecondsLeft();
            if (whole == _countedTo) return;

            if (whole > 0) _world.Play(Sound.Cue.CountdownTick);
            else if (_countedTo > 0) _world.Play(Sound.Cue.FightBegins);

            _countedTo = whole;
        }

        private int WholeSecondsLeft() => (int)Math.Ceiling(_terms.CountdownLeftAt(_world.Now));

        /// <summary>
        /// Invitations expire with time and with distance. Walking away from a pending challenge
        /// cancels it, so an unanswered challenge cannot turn into a duel somewhere else later.
        /// </summary>
        private void ExpireInvites(double now)
        {
            if (!None(_outgoing))
            {
                if (now > _outgoingUntil)
                {
                    Rest(_outgoing);
                    ClearOutgoing(Messages.Unanswered, _outgoingName);
                }
                else if (Drifted(_outgoing, _outgoingRadius))
                {
                    // Reaches the challenged player as a withdrawal, so their prompt goes too.
                    _world.SendReply(_outgoing, accepted: false, default(Lease.Terms));
                    Rest(_outgoing);
                    ClearOutgoing(Messages.YouWalkedOff, _outgoingName);
                }
            }

            if (!None(_incoming))
            {
                if (now > _incomingUntil)
                {
                    Rest(_incoming);
                    ClearIncoming(Messages.Expired, _incomingName);
                }
                else if (Drifted(_incoming, _incomingRadius))
                {
                    _world.SendBusy(_incoming, Busy.TooFar);
                    Rest(_incoming);
                    ClearIncoming(Messages.TooFarNow, _incomingName);
                }
            }
        }

        /// <summary>
        /// Whether the other party to a pending challenge has become too far away to fight. An
        /// opponent we can no longer place at all counts as gone, which cancels — the safe way
        /// round for something that has not started yet. Measured against the proposed ring, not
        /// the larger distance a live duel tolerates: walking out of the proposed ring is taken as
        /// withdrawing.
        /// </summary>
        private bool Drifted(TId other, float radius)
        {
            var distance = _world.DistanceTo(other);
            return distance == null || Arena.Beyond(distance.Value, radius, _world.Rules.ArenaRadius);
        }

        /// <summary>
        /// Every condition a live duel has to keep satisfying. Anything unclear ends it: ending a
        /// duel early only costs a rematch, while keeping one alive wrongly grants protection.
        /// </summary>
        private bool Valid(double now, out EndReason reason)
        {
            reason = EndReason.LeaseLapsed;

            if (_world.Dead) { reason = EndReason.Died; return false; }

            var paired = _world.Agreed().Equals(_opponent);
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
            // because they have also gone far enough to trip the check below. What stepping out
            // means depends on whether blows count yet (Arena.Stray).
            if (DistanceFromCenter() > _terms.Radius)
            {
                reason = Arena.Stray(_terms.BegunAt(now));
                return false;
            }

            // The opponent's position, which this client only knows second-hand. Leaving the ring
            // on their side is their client's to concede; this only catches an opponent who has
            // gone somewhere no ring could put them, and calls it off.
            var apart = _world.DistanceTo(_opponent);
            if (apart == null) return false;
            if (apart.Value > Arena.OpponentRange(_terms.Radius))
            {
                reason = EndReason.OutOfRange;
                return false;
            }

            return true;
        }

        /// <summary>How far the local player is from the middle of the ring, flat — height is not straying.</summary>
        private float DistanceFromCenter() => _terms.Radius <= 0f ? 0f : _world.FromCenter(_terms.Center);

        // ---- starting ----

        public void Challenge(TId target, float radius)
        {
            var me = _world.Me;
            if (None(me) || None(target)) return;

            if (Active) { _world.Say(Messages.Named(Messages.AlreadyDueling, _opponentName)); return; }
            if (target.Equals(me)) { _world.Say(Messages.NotYourself); return; }
            if (HasOutgoing) { _world.Say(Messages.Named(Messages.AlreadyChallenged, _outgoingName)); return; }
            if (!_world.TryFind(target, out var seen)) return;

            var name = seen.Name;

            var mine = Readiness(radius);
            if (mine != Busy.None) { _world.Say(YouCannot(mine)); return; }

            if (Resting(target)) { _world.Say(Messages.Named(Messages.WaitBeforeAgain, name)); return; }

            // Someone further off than the proposed ring would cancel on arrival, so it is not
            // sent. This is also what keeps a rematch to someone standing near you.
            if (Drifted(target, radius)) { _world.Say(Messages.Named(Messages.TooFarAway, name)); return; }

            // The two parts of the other player's state that can be read from here. The rest —
            // combat, sitting, sleeping — is only known on their own machine, and is checked there
            // when the challenge arrives.
            if (seen.Dead) { _world.Say(Messages.Named(Messages.TheyBusy, name)); return; }
            if (seen.PvpOn) { _world.Say(Messages.Named(Messages.TheyPvpOn, name)); return; }

            _outgoing = target;
            _outgoingName = name;
            _outgoingRadius = radius;
            _outgoingUntil = _world.Now + _world.Rules.InviteSeconds;

            // No message: the waiting panel appears this frame with the same information.
            _world.SendChallenge(target, radius);
        }

        public void ReceiveChallenge(TId challenger, string name, float radius)
        {
            if (None(_world.Me) || None(challenger)) return;

            radius = Arena.Clamp(radius, _world.Rules.ArenaRadius);

            // Decided here, on the machine it was sent to, before any sound or prompt (Challenges).
            var arrival = Challenges.Arrive(challenger, Active, _incoming, _outgoing, Resting(challenger),
                Drifted(challenger, radius), () => Readiness(radius), out var refusal);
            switch (arrival)
            {
                case Arrival.AlreadyShown:
                    return;

                case Arrival.Crossed:
                    Accept(challenger, name, radius);
                    return;

                case Arrival.Refused:
                    _world.SendBusy(challenger, refusal);
                    return;
            }

            _incoming = challenger;
            _incomingName = Messages.ShownName(name);
            _incomingRadius = radius;
            _incomingUntil = _world.Now + _world.Rules.InviteSeconds;

            // The invite panel shows the name, keys and countdown; the sound draws attention to it.
            _world.ChallengeSound();
        }

        /// <summary>Whether this player is free to start a duel, and if not, why; see <see cref="Challenges.Readiness"/>.</summary>
        public Busy Readiness(float radius)
        {
            var rules = _world.Rules;
            return Challenges.Readiness(_world.Standing, rules.RequireOutOfCombat, rules.RequireClearGround,
                () => _world.HostileAround(radius > 0f ? radius : rules.ArenaRadius));
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

        public void Accept()
        {
            if (None(_incoming)) { _world.Say(Messages.NoChallenge); return; }

            // Accepting is refused, not the challenge: it stays on screen, so it can still be
            // taken once whatever is in the way has cleared.
            if (None(_world.Me)) return;

            var mine = Readiness(_incomingRadius);
            if (mine != Busy.None) { _world.Say(YouCannot(mine)); return; }

            Accept(_incoming, _incomingName, _incomingRadius);
        }

        /// <summary>
        /// The side that accepts settles the terms, because it is the only one that knows where
        /// both fighters are standing at the moment the duel is struck. The ring is centred between
        /// the two of them, and the start time is put on the shared server clock rather than each
        /// client running its own countdown, so the first blow becomes legal on the same instant
        /// for both.
        /// </summary>
        private void Accept(TId challenger, string name, float radius)
        {
            if (None(_world.Me)) return;

            var middle = _world.MidpointWith(challenger);
            if (middle == null)
            {
                ClearIncoming(Messages.Unreachable, Messages.ShownName(name));
                return;
            }

            var rules = _world.Rules;
            var terms = new Lease.Terms
            {
                Center = middle.Value,
                Radius = Arena.Clamp(radius, rules.ArenaRadius),
                StartAtMs = (long)((_world.Now + rules.CountdownSeconds) * 1000.0),
            };

            _world.SendReply(challenger, accepted: true, terms);
            ClearIncoming(null, null);
            ClearOutgoing(null, null);
            BeginWith(challenger, name, terms);
        }

        public void Decline()
        {
            if (None(_incoming)) { _world.Say(Messages.NoChallenge); return; }

            _world.SendReply(_incoming, accepted: false, default(Lease.Terms));
            Rest(_incoming);
            ClearIncoming(Messages.YouDeclined, _incomingName);
        }

        public void Withdraw()
        {
            if (None(_outgoing)) { _world.Say(Messages.NoneOut); return; }

            _world.SendReply(_outgoing, accepted: false, default(Lease.Terms));
            Rest(_outgoing);
            ClearOutgoing(Messages.YouWithdrew, _outgoingName);
        }

        /// <summary>
        /// An answer to a challenge, or a "no" from the player who challenged us. A "no" from
        /// either side travels the same way: the challenged player declining, or the challenger
        /// withdrawing, told apart by which way round the challenge ran.
        /// </summary>
        public void ReceiveReply(TId responder, bool accepted, Lease.Terms terms)
        {
            // A "no" from the player who challenged us: they withdrew, so the prompt goes.
            if (!accepted && !None(_incoming) && _incoming.Equals(responder))
            {
                Rest(responder);
                ClearIncoming(Messages.TheyWithdrew, _incomingName);
                _world.Play(Sound.Cue.CameToNothing);
                return;
            }

            if (None(_outgoing) || !_outgoing.Equals(responder))
            {
                // A "yes" to a challenge we have since withdrawn or let lapse. Their side has
                // already started a countdown for a duel that is not happening, so tell them now
                // rather than leave it to run until the pairing gives up. Only answered for
                // someone we did just have a challenge with, so this cannot be used to make us
                // send messages to anyone at all.
                if (accepted && Resting(responder)) _world.SendResult(responder, EndReason.Withdrew);
                return;
            }

            var name = _outgoingName;
            ClearOutgoing(null, null);

            if (!accepted)
            {
                Rest(responder);
                _world.Say(Messages.Named(Messages.TheyDeclined, name));
                _world.Play(Sound.Cue.CameToNothing);
                return;
            }

            // The terms are adopted as they arrive, not recomputed. Two machines each working out
            // "the middle" from positions a moment apart would draw two different rings.
            var rules = _world.Rules;
            terms.Radius = Arena.Clamp(terms.Radius, rules.ArenaRadius);
            terms.StartAtMs = Lease.SaneStart(terms.StartAtMs, _world.Now, rules.CountdownSeconds);
            _world.Play(Sound.Cue.Accepted);
            BeginWith(responder, name, terms);
        }

        /// <summary>
        /// A challenge of ours turned down before it was shown, with the reason. A refusal for
        /// repeating too soon also starts our own pause, so this client stops asking.
        /// </summary>
        public void ReceiveBusy(TId from, Busy why)
        {
            if (None(_outgoing) || !_outgoing.Equals(from)) return;

            var name = _outgoingName;
            ClearOutgoing(null, null);
            if (why == Busy.Cooldown) Rest(from);

            _world.Say(Messages.Named(TheyCannot(why), name));
            _world.Play(Sound.Cue.CameToNothing);
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

        private void BeginWith(TId opponent, string name, Lease.Terms terms)
        {
            if (None(_world.Me)) return;

            _opponent = opponent;
            _opponentName = string.IsNullOrEmpty(name) ? "your opponent" : Messages.ShownName(name);
            _terms = terms;
            _everPaired = false;
            _unpairedSince = 0.0;
            _pairDeadline = _world.Now + _world.Rules.PairGraceSeconds;
            _nextRenew = 0.0;

            _lastFoe = opponent;
            _lastFoeName = _opponentName;
            _lastRadius = terms.Radius;
            _countedTo = WholeSecondsLeft();

            _world.SetTerms(terms);
            _world.Stamp(opponent);

            // A real duel takes over from any rehearsal standing where you were.
            _world.StopRehearsal();

            _world.CardBegins(_opponentName);
        }

        // ---- ending ----

        /// <summary>Giving up on purpose, with the duel still live.</summary>
        public void Forfeit()
        {
            if (!Active) { _world.Say(Messages.NotDueling); return; }

            Concede(EndReason.Forfeited);
        }

        /// <summary>
        /// Losing without being beaten down: giving up, or leaving the ring. The opponent wins as
        /// they would from a yield, and this side is picked up to the loser's floor.
        /// </summary>
        private void Concede(EndReason reason)
        {
            var name = _opponentName;
            var winner = _opponent;

            _world.CardOutcome(DuelOutcome.Lost);
            EndLocal(reason, notify: true);
            _world.Lift(_world.Rules.YieldHealth);
            _world.SendAnnounce(winner, name, _world.MyName, reason);
        }

        /// <summary>
        /// Whether a killing blow should become a yield instead; see <see cref="Survival.ShouldSurvive{TId}"/>.
        /// The pair is read off the leases as they stand, never from this duel's own state.
        /// </summary>
        public bool ShouldSurvive(bool local, Blow<TId>? lastHit) =>
            Survival.ShouldSurvive(local, _opponent, _world.Agreed(), lastHit);

        /// <summary>
        /// Dropped to nothing at the opponent's hands. Called from the CheckDeath patch, before the
        /// game has marked anyone dead.
        /// </summary>
        public void YieldTo()
        {
            var name = _opponentName;
            var winner = _opponent;

            // Ends first — which drops the lease and clears what the fight left on you — then puts
            // the health back, so the restored value is the last word and nothing can read this
            // player as still protected while they stand up.
            _world.CardOutcome(DuelOutcome.Lost);
            EndLocal(EndReason.Yielded, notify: true);
            _world.Lift(_world.Rules.YieldHealth);
            _world.Play(Sound.Cue.KnockedDown);

            // After the health is back: the blow that would have killed you knocks you down
            // instead, away from the opponent, which only works for a character the game no
            // longer sees as finished.
            _world.KnockDown(winner);

            _world.SendAnnounce(winner, name, _world.MyName, EndReason.Yielded);
        }

        /// <summary>
        /// Killed by something that was not your opponent. The duel ends and is announced, so the
        /// other fighter sees why.
        /// </summary>
        public void DiedOutside()
        {
            if (!Active) return;

            var name = _opponentName;
            var winner = _opponent;
            EndLocal(EndReason.Died, notify: true);
            _world.SendAnnounce(winner, name, _world.MyName, EndReason.Died);
        }

        /// <summary>
        /// The opponent's half of the reckoning. Accepted just after our own side closed, which is
        /// when it normally arrives — so it is not gated on the duel still being live, only on it
        /// having been against this person.
        /// </summary>
        public void ReceiveTally(TId from, float took, int hits, float biggest)
        {
            if (None(from)) return;
            if (!from.Equals(_opponent) && !from.Equals(_lastFoe)) return;

            _world.CardHeard(took, hits, biggest);
        }

        public void ReceiveResult(TId from, EndReason reason)
        {
            if (!Active || !_opponent.Equals(from))
            {
                // A stale result from a duel we have already left. Nothing to undo.
                return;
            }

            // The loser broadcasts the announcement, so the winner only has to take its own side
            // down. Saying anything here as well would double the line on screen.

            // The same reason means the opposite thing on this side: they are telling us they
            // yielded, which is a win here.
            if (reason.Decided()) _world.CardOutcome(DuelOutcome.Won);

            EndLocal(reason, notify: false);

            // Only an ending somebody won restores health; a duel that was merely called off
            // would otherwise be a free heal.
            if (!reason.Decided()) return;

            _world.Lift(_world.Rules.VictoryHealth);

            // The emote is the one part of winning that only the winner can set going: it is
            // written to their own ZDO, and the game replicates it from there. The burst comes
            // separately, off the broadcast, so that spectators get it too.
            _world.Celebrate();
        }

        /// <summary>
        /// Takes our side down: lease dropped, status effects cleared, ring hidden, opponent told.
        /// Every one of those is best effort. The lease lapsing is what actually guarantees the
        /// duel is over.
        /// </summary>
        public void EndLocal(EndReason reason, bool notify)
        {
            var opponent = _opponent;
            var name = _opponentName;

            LastEnd = reason;

            if (Plugin.DebugCommands) Plugin.Log.LogInfo($"Sparring duel ended: {reason} against {name}.");

            // Before the fields are cleared: the card needs the terms for the duration and the
            // opponent for where to send our half. A summary is shown only for a duel somebody won.
            var decided = reason.Decided();
            _world.CardEnds(decided);
            if (decided && !None(opponent)) _world.SendTally(opponent);

            _opponent = default;
            _opponentName = "";
            _everPaired = false;
            _unpairedSince = 0.0;
            _terms = default(Lease.Terms);

            _world.RingChanged();
            Practising = false;
            _world.SelfPair = false;

            // Before the lease goes, not after. Dropping the lease is itself what tells the other
            // side the duel is over, and it says nothing about why: if it reaches them first their
            // own tick ends the duel as a lapsed pairing, which has no winner, so the result is
            // sent first and arrives first.
            if (notify && !None(opponent)) _world.SendResult(opponent, reason);

            if (!None(_world.Me))
            {
                _world.ClearLease();
                _world.Settle();
            }

            // Ends nobody won get their own word here; one somebody won is announced to everyone
            // by the side that lost, so those say nothing extra on this screen.
            if (reason == EndReason.OutOfRange) _world.Say(Messages.OutOfRing);
            else if (reason == EndReason.Withdrew && name.Length > 0) _world.Say(Messages.Named(Messages.TheyWithdrew, name));
            else if (reason == EndReason.LeaseLapsed && name.Length > 0) _world.Say(Messages.Named(Messages.LostTrack, name));

            // A death already makes its own noise, and an ending somebody won has the victory.
            if (!decided && reason != EndReason.Died) _world.Play(Sound.Cue.CameToNothing);
        }

        /// <summary>
        /// A duel against nobody, for testing the parts that do not need a second player.
        ///
        /// Everything local runs for real: the ring, the panel, the countdown, the tally, the
        /// ending, the card, the healing. The lease names this player as their own opponent, which
        /// is the only way to pair alone, and is why that is allowed only here.
        ///
        /// Nothing shields you from a creature's killing blow, so this is as dangerous as standing
        /// there normally, and nothing that needs two machines is covered: neither the handshake
        /// nor the order the ending messages arrive in.
        /// </summary>
        public void Practice()
        {
            var me = _world.Me;
            if (None(me)) return;

            if (Active) { _world.Say(Messages.Named(Messages.AlreadyDueling, _opponentName)); return; }

            Practising = true;
            _world.SelfPair = true;

            var rules = _world.Rules;
            var terms = new Lease.Terms
            {
                Center = _world.Here,
                Radius = rules.ArenaRadius,
                StartAtMs = (long)((_world.Now + rules.CountdownSeconds) * 1000.0),
            };

            BeginWith(me, "Practice", terms);
            _world.Say("Practice duel. Take a few hits, then yield or step out of the ring.");
            Plugin.Log.LogInfo(FormattableString.Invariant($"Sparring practice: duel begun, ring {terms.Radius:0.#}m."));
        }

        /// <summary>Re-challenge whoever you last fought, if they are still around.</summary>
        public void Rematch()
        {
            if (None(_lastFoe)) { _world.Say(Messages.NeverDueled); return; }

            if (!_world.TryFind(_lastFoe, out _)) { _world.Say(Messages.Named(Messages.NotNearby, _lastFoeName)); return; }

            Challenge(_lastFoe, _lastRadius > 0f ? _lastRadius : _world.Rules.ArenaRadius);
        }

        public void Reset()
        {
            _opponent = default;
            _opponentName = "";
            _everPaired = false;
            _unpairedSince = 0.0;
            _pairDeadline = 0.0;
            _nextRenew = 0.0;
            _countedTo = 0;
            _terms = default(Lease.Terms);
            ClearOutgoing(null, null);
            ClearIncoming(null, null);
            _world.RingChanged();

            // This is the other way out of a practice duel: dying in one, which it does not protect
            // against. Leaving these set would mean the next real duel counted every blow that
            // landed and left the fighters worth attacking.
            Practising = false;
            _world.SelfPair = false;

            // The pauses are deliberately kept. This runs whenever there is no body, including
            // between a death and the respawn, and clearing them then would hand anyone who was
            // just declined a free way to start again. They expire on their own.
        }

        /// <summary>Starts the pause before this player and we can exchange another challenge.</summary>
        private void Rest(TId other) => _resting.Start(other, _world.Now);

        private bool Resting(TId other) => _resting.Holds(other, _world.Now);

        private void ClearOutgoing(string format, string arg)
        {
            _outgoing = default;
            _outgoingName = "";
            _outgoingRadius = 0f;
            _outgoingUntil = 0.0;
            if (format != null) _world.Say(Messages.Named(format, arg));
        }

        private void ClearIncoming(string format, string arg)
        {
            _incoming = default;
            _incomingName = "";
            _incomingRadius = 0f;
            _incomingUntil = 0.0;
            if (format != null) _world.Say(Messages.Named(format, arg));
        }

        private static bool None(TId id) => id.Equals(default(TId));
    }
}
