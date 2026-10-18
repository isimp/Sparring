using System.Linq;
using Xunit;

namespace Sparring.Tests
{
    /// <summary>
    /// Two players' games agreeing on a duel: a challenge, its answer, and every way one comes to
    /// nothing, played through each game's own duel flow with the messages passing between them.
    /// The challenged player's game decides, before anything is shown; withdrawing, declining or
    /// walking away clears the challenge on both sides; and the same two cannot exchange another for
    /// fifteen seconds after one came to nothing.
    /// </summary>
    public class HandshakeTests
    {
        private const int Ana = 1, Bo = 2, Cy = 3;

        private readonly Server _server = new Server();
        private readonly Game _ana, _bo;

        public HandshakeTests()
        {
            _ana = _server.Join(Ana, "Ana", x: 0f);
            _bo = _server.Join(Bo, "Bo", x: 6f);
        }

        private void Challenged()
        {
            _ana.Challenge(_bo);
            _server.Deliver();
        }

        [Fact]
        public void AChallengeIsShownToThePlayerChallengedWithASound()
        {
            Challenged();

            Assert.True(_bo.Flow.HasInvite);
            Assert.Equal("Ana", _bo.Flow.InviteFrom);
            Assert.Equal(1, _bo.ChallengeSounds);
            Assert.True(_ana.Flow.HasOutgoing);
            Assert.Equal("Bo", _ana.Flow.AwaitingFrom);
        }

        [Fact]
        public void AnAcceptedChallengeStartsTheSameDuelOnBothSides()
        {
            Challenged();
            _bo.Flow.Accept();
            _server.Deliver();

            Assert.True(_ana.Flow.Active);
            Assert.True(_bo.Flow.Active);
            Assert.Equal(Bo, _ana.Flow.Opponent);
            Assert.Equal(Ana, _bo.Flow.Opponent);
            Assert.Equal(_bo.Flow.Terms.StartAtMs, _ana.Flow.Terms.StartAtMs);
            Assert.Equal(_bo.Flow.Terms.Radius, _ana.Flow.Terms.Radius);
            Assert.Equal(3f, _ana.Flow.Terms.Center.x, 3);
            Assert.Contains(Sound.Cue.Accepted, _ana.Heard);
            Assert.DoesNotContain(Sound.Cue.Accepted, _bo.Heard);
            Assert.False(_bo.Flow.HasInvite);
            Assert.False(_ana.Flow.HasOutgoing);
        }

        [Fact]
        public void NoBlowLandsUntilTheCountdownEnds()
        {
            Challenged();
            _bo.Flow.Accept();
            _server.Deliver();
            _server.Pass(1);

            Assert.True(_ana.Flow.CountingDown);
            Assert.False(_server.Lands(_ana, _bo));
            Assert.False(_server.Lands(_bo, _ana));

            _server.Pass(2.25);
            Assert.False(_ana.Flow.CountingDown);
            Assert.True(_server.Lands(_ana, _bo));
            Assert.True(_server.Lands(_bo, _ana));
        }

        [Fact]
        public void TheCountdownTicksEachSecondAfterTheAcceptAndRingsTheBellOnBothSides()
        {
            Challenged();
            _bo.Flow.Accept();
            _server.Deliver();
            _server.Pass(4);

            foreach (var game in new[] { _ana, _bo })
            {
                Assert.Equal(2, game.Heard.Count(c => c == Sound.Cue.CountdownTick));
                Assert.Equal(1, game.Heard.Count(c => c == Sound.Cue.FightBegins));
            }
        }

        [Fact]
        public void ADeclineIsToldToTheChallenger()
        {
            Challenged();
            _bo.Flow.Decline();
            _server.Deliver();

            Assert.False(_bo.Flow.HasInvite);
            Assert.False(_ana.Flow.HasOutgoing);
            Assert.Contains(Messages.Named(Messages.YouDeclined, "Ana"), _bo.Said);
            Assert.Contains(Messages.Named(Messages.TheyDeclined, "Bo"), _ana.Said);
            Assert.Contains(Sound.Cue.CameToNothing, _ana.Heard);
        }

        [Fact]
        public void AWithdrawnChallengeLeavesTheChallengedPlayersScreen()
        {
            Challenged();
            _ana.Flow.Withdraw();
            _server.Deliver();

            Assert.False(_ana.Flow.HasOutgoing);
            Assert.False(_bo.Flow.HasInvite);
            Assert.Contains(Messages.Named(Messages.YouWithdrew, "Bo"), _ana.Said);
            Assert.Contains(Messages.Named(Messages.TheyWithdrew, "Ana"), _bo.Said);
            Assert.Contains(Sound.Cue.CameToNothing, _bo.Heard);
        }

        [Fact]
        public void WalkingAwayFromAChallengeWithdrawsItOnBothSides()
        {
            Challenged();
            _ana.X = 50f;

            // Only Ana's game has noticed yet; Bo's learns it from the withdrawal.
            _server.Now += 0.25;
            _ana.Flow.Tick();
            _server.Deliver();

            Assert.False(_ana.Flow.HasOutgoing);
            Assert.False(_bo.Flow.HasInvite);
            Assert.Contains(Messages.Named(Messages.YouWalkedOff, "Bo"), _ana.Said);
            Assert.Contains(Messages.Named(Messages.TheyWithdrew, "Ana"), _bo.Said);
        }

        [Fact]
        public void WalkingAwayFromAChallengeOnScreenTellsTheChallenger()
        {
            Challenged();
            _bo.X = 50f;

            // Only Bo's game has noticed yet; Ana's learns it from Bo's.
            _server.Now += 0.25;
            _bo.Flow.Tick();
            _server.Deliver();

            Assert.False(_bo.Flow.HasInvite);
            Assert.False(_ana.Flow.HasOutgoing);
            Assert.Contains(Messages.Named(Messages.TooFarAway, "Bo"), _ana.Said);
        }

        [Fact]
        public void AChallengeWhoseChallengerHasGoneOutOfReachIsDroppedOnScreenToo()
        {
            Challenged();
            _ana.X = 50f;
            _server.Pass(0.25);

            Assert.False(_ana.Flow.HasOutgoing);
            Assert.False(_bo.Flow.HasInvite);
            Assert.Contains(Messages.Named(Messages.TooFarNow, "Ana"), _bo.Said);
        }

        [Fact]
        public void AChallengeLeftUnansweredRunsOutOnBothSidesWithoutTheCameToNothingSound()
        {
            Challenged();
            _server.Pass(31);

            Assert.False(_ana.Flow.HasOutgoing);
            Assert.False(_bo.Flow.HasInvite);
            Assert.Contains(Messages.Named(Messages.Unanswered, "Bo"), _ana.Said);
            Assert.Contains(Messages.Named(Messages.Expired, "Ana"), _bo.Said);
            Assert.DoesNotContain(Sound.Cue.CameToNothing, _ana.Heard);
            Assert.DoesNotContain(Sound.Cue.CameToNothing, _bo.Heard);
        }

        [Fact]
        public void AYesCrossingAWithdrawalEndsTheDuelItStarted()
        {
            Challenged();
            _bo.Flow.Accept();
            _ana.Flow.Withdraw();
            _server.Deliver();

            Assert.False(_ana.Flow.Active);
            Assert.False(_bo.Flow.Active);
            Assert.Contains(Messages.Named(Messages.TheyWithdrew, "Ana"), _bo.Said);
        }

        [Fact]
        public void AChallengeToAPlayerWhoIsNotFreeIsNeverShownAndTheChallengerHearsWhy()
        {
            _bo.Standing.InCombat = true;
            Challenged();

            Assert.False(_bo.Flow.HasInvite);
            Assert.Equal(0, _bo.ChallengeSounds);
            Assert.False(_ana.Flow.HasOutgoing);
            Assert.Contains(Messages.Named(Messages.TheyInCombat, "Bo"), _ana.Said);
            Assert.Contains(Sound.Cue.CameToNothing, _ana.Heard);
        }

        [Fact]
        public void TheSameTwoWaitFifteenSecondsAfterAChallengeCameToNothing()
        {
            Challenged();
            _bo.Flow.Decline();
            _server.Deliver();

            _ana.Challenge(_bo);
            Assert.Contains(Messages.Named(Messages.WaitBeforeAgain, "Bo"), _ana.Said);
            Assert.False(_ana.Flow.HasOutgoing);

            _server.Pass(15.25);
            Challenged();
            Assert.True(_bo.Flow.HasInvite);
        }

        [Fact]
        public void AChallengerIgnoringTheirOwnPauseIsTurnedDownUnseen()
        {
            Challenged();
            _bo.Flow.Decline();
            _server.Deliver();

            // A game that skips its own pause sends the challenge anyway.
            _server.Post(Bo, () => _bo.Flow.ReceiveChallenge(Ana, "Ana", 20f));
            _server.Deliver();

            Assert.False(_bo.Flow.HasInvite);
            Assert.Equal(1, _bo.ChallengeSounds);
            Assert.Contains("send busy " + Busy.Cooldown, _bo.Events);
        }

        [Fact]
        public void TwoPlayersChallengingEachOtherAtOnceStartTheDuel()
        {
            _ana.Challenge(_bo);
            _bo.Challenge(_ana);
            _server.Deliver();

            Assert.True(_ana.Flow.Active);
            Assert.True(_bo.Flow.Active);
            Assert.Equal(Bo, _ana.Flow.Opponent);
            Assert.Equal(Ana, _bo.Flow.Opponent);
        }

        [Fact]
        public void AChallengeWhileAnotherIsOnScreenIsTurnedDown()
        {
            var cy = _server.Join(Cy, "Cy", x: 3f);
            Challenged();
            cy.Challenge(_bo);
            _server.Deliver();

            Assert.Equal("Ana", _bo.Flow.InviteFrom);
            Assert.Contains(Messages.Named(Messages.TheyAnswering, "Bo"), cy.Said);
        }

        [Fact]
        public void ARingSizeFromTheOtherSideIsHeldToSixtyMetresOnBothSides()
        {
            _ana.Flow.Challenge(Bo, 500f);
            _server.Deliver();
            _bo.Flow.Accept();
            _server.Deliver();

            Assert.Equal(Arena.Ceiling, _bo.Flow.Terms.Radius);
            Assert.Equal(Arena.Ceiling, _ana.Flow.Terms.Radius);
        }

        [Fact]
        public void AStartTimeFromTheOtherSideOutsideTheWindowBecomesAFreshCountdown()
        {
            Challenged();

            // A game that sends a start time an hour away, and a ring of 500 m.
            var terms = new Lease.Terms { Center = Server.At(3f, 0f), Radius = 500f, StartAtMs = (long)((_server.Now + 3600) * 1000) };
            _server.Post(Ana, () => _ana.Flow.ReceiveReply(Bo, true, terms));
            _server.Deliver();

            Assert.Equal((long)((_server.Now + 3) * 1000), _ana.Flow.Terms.StartAtMs);
            Assert.Equal(Arena.Ceiling, _ana.Flow.Terms.Radius);
        }

        [Fact]
        public void AYesFromSomeoneNeverChallengedSendsNothing()
        {
            var terms = new Lease.Terms { Radius = 20f, StartAtMs = (long)((_server.Now + 3) * 1000) };
            _server.Post(Ana, () => _ana.Flow.ReceiveReply(Cy, true, terms));
            _server.Deliver();

            Assert.False(_ana.Flow.Active);
            Assert.DoesNotContain(_ana.Events, e => e.StartsWith("send", System.StringComparison.Ordinal));
        }

        [Fact]
        public void AChallengeToSomeoneBeyondTheRingIsNotSent()
        {
            _bo.X = 30f;
            _ana.Challenge(_bo);

            Assert.Contains(Messages.Named(Messages.TooFarAway, "Bo"), _ana.Said);
            Assert.DoesNotContain("send challenge", _ana.Events);
        }

        [Fact]
        public void YouCannotChallengeYourselfSomeoneWithPvpOnOrWhileInADuel()
        {
            _ana.Flow.Challenge(Ana, 20f);
            Assert.Contains(Messages.NotYourself, _ana.Said);

            _bo.Standing.PvpOn = true;
            _ana.Challenge(_bo);
            Assert.Contains(Messages.Named(Messages.TheyPvpOn, "Bo"), _ana.Said);
            Assert.DoesNotContain("send challenge", _ana.Events);
        }

        [Fact]
        public void YouCannotChallengeWhileInADuelOrWithAChallengeAlreadyOut()
        {
            var cy = _server.Join(Cy, "Cy", x: 3f);
            Challenged();
            _ana.Challenge(cy);
            Assert.Contains(Messages.Named(Messages.AlreadyChallenged, "Bo"), _ana.Said);

            _bo.Flow.Accept();
            _server.Deliver();
            _ana.Challenge(cy);
            Assert.Contains(Messages.Named(Messages.AlreadyDueling, "Bo"), _ana.Said);
            Assert.Equal(1, _ana.Events.Count(e => e == "send challenge"));
        }

        [Fact]
        public void AChallengeToSomeoneDeadIsNotSent()
        {
            _bo.Standing.Dead = true;
            _ana.Challenge(_bo);

            Assert.Contains(Messages.Named(Messages.TheyBusy, "Bo"), _ana.Said);
            Assert.DoesNotContain("send challenge", _ana.Events);
        }

        [Fact]
        public void AHugeRingProposedFromFarOffIsHeldToSixtyMetresAndTurnedDown()
        {
            _bo.X = 100f;
            _server.Post(Bo, () => _bo.Flow.ReceiveChallenge(Ana, "Ana", 500f));
            _server.Deliver();

            Assert.False(_bo.Flow.HasInvite);
            Assert.Contains("send busy " + Busy.TooFar, _bo.Events);
        }

        [Fact]
        public void AcceptingAChallengerWhoHasGoneSaysSo()
        {
            Challenged();
            _ana.InWorld = false;
            _bo.Flow.Accept();

            Assert.False(_bo.Flow.Active);
            Assert.False(_bo.Flow.HasInvite);
            Assert.Contains(Messages.Named(Messages.Unreachable, "Ana"), _bo.Said);
        }

        [Fact]
        public void AChallengeToSomeoneWhoHasGoneIsWithdrawn()
        {
            Challenged();
            _bo.InWorld = false;
            _server.Now += 0.25;
            _ana.Flow.Tick();

            Assert.False(_ana.Flow.HasOutgoing);
            Assert.Contains(Messages.Named(Messages.YouWalkedOff, "Bo"), _ana.Said);
        }

        [Fact]
        public void BeingTurnedDownAsTooSoonStartsTheChallengersOwnPause()
        {
            Challenged();
            _bo.Flow.Decline();
            _server.Deliver();

            // Ana's game starts afresh, with no pause of its own, while Bo's still holds.
            var ana = _server.Join(Ana, "Ana", x: 0f);
            ana.Challenge(_bo);
            _server.Deliver();
            Assert.Contains(Messages.Named(Messages.WaitBeforeAgain, "Bo"), ana.Said);

            ana.Said.Clear();
            ana.Challenge(_bo);
            Assert.Contains(Messages.Named(Messages.WaitBeforeAgain, "Bo"), ana.Said);
            Assert.Equal(1, ana.Events.Count(e => e == "send challenge"));
        }

        [Fact]
        public void YouCannotChallengeWhileYouAreNotFree()
        {
            _ana.Standing.PvpOn = true;
            _ana.Challenge(_bo);

            Assert.Contains(Messages.PvpOn, _ana.Said);
            Assert.DoesNotContain("send challenge", _ana.Events);
        }

        [Fact]
        public void AcceptingWhileNotFreeKeepsTheChallengeOnScreen()
        {
            Challenged();
            _bo.Hostile = true;
            _bo.Flow.Accept();

            Assert.Contains(Messages.CreaturesAbout, _bo.Said);
            Assert.True(_bo.Flow.HasInvite);
            Assert.False(_bo.Flow.Active);
        }
    }
}
