using System;
using System.Linq;
using Xunit;
using HitType = HitData.HitType;

namespace Sparring.Tests
{
    /// <summary>
    /// A duel under way between two players' games, from the first blow that counts to its end:
    /// who survives what, who wins, who gets back up with what, what both cards say, and what the
    /// duel's end does when a game stops, a message is slow or one side never agrees.
    /// </summary>
    public class FightTests
    {
        private const int Ana = 1, Bo = 2, Cy = 3, Troll = 50;

        private readonly Server _server = new Server();
        private readonly Game _ana, _bo;

        /// <summary>Ana and Bo, 6 m apart, in a duel whose countdown has run out; the ring's middle is at x 3.</summary>
        public FightTests()
        {
            _ana = _server.Join(Ana, "Ana", x: 0f);
            _bo = _server.Join(Bo, "Bo", x: 6f);
        }

        private void Fighting(float radius = 20f, bool begun = true)
        {
            _ana.Challenge(_bo, radius);
            _server.Deliver();
            _bo.Flow.Accept();
            _server.Deliver();
            if (begun) _server.Pass(3.25);
            Assert.True(_ana.Flow.Active && _bo.Flow.Active);
        }

        [Fact]
        public void TheOpponentsKillingBlowIsAYieldAndBothGetBackUp()
        {
            Fighting();
            _ana.Take(30f);
            _bo.Take(40f);
            _bo.Fall(by: _ana);
            _server.Deliver();

            Assert.False(_bo.Flow.Active);
            Assert.False(_ana.Flow.Active);
            Assert.DoesNotContain("died", _bo.Events);
            Assert.Equal(30f, _bo.Health, 3);
            Assert.Equal(70f, _ana.Health, 3);
            Assert.Equal(1, _bo.KnockDowns);
            Assert.Contains(Sound.Cue.KnockedDown, _bo.Heard);
            Assert.Equal(1, _ana.Celebrations);
            Assert.Equal(EndReason.Yielded, _bo.Flow.LastEnd);
        }

        [Fact]
        public void EveryoneIsToldWhoYieldedToWhom()
        {
            var cy = _server.Join(Cy, "Cy", x: 30f);
            Fighting();
            _bo.Fall(by: _ana);
            _server.Deliver();

            foreach (var game in new[] { _ana, _bo, cy })
                Assert.Equal(("Ana", "Bo", EndReason.Yielded), Assert.Single(game.Announced));
        }

        [Fact]
        public void BothCardsShowBothHalves()
        {
            Fighting();
            _ana.Take(30f);
            _bo.Take(40f);
            _bo.Fall(by: _ana);
            _server.Deliver();

            Assert.Equal(DuelOutcome.Won, _ana.CardOutcome);
            Assert.Equal(DuelOutcome.Lost, _bo.CardOutcome);
            Assert.True(_ana.CardDecided);
            Assert.True(_bo.CardDecided);
            Assert.Equal(40f, _ana.CardGave);
            Assert.Equal(30f, _bo.CardGave);
        }

        [Theory]
        [InlineData((int)HitType.Burning)]
        [InlineData((int)HitType.Poisoned)]
        public void FireOrPoisonTheOpponentLeftIsAYieldToo(int type)
        {
            Fighting();
            _bo.Fall(type: (HitType)type);
            _server.Deliver();

            Assert.DoesNotContain("died", _bo.Events);
            Assert.Equal(EndReason.Yielded, _bo.Flow.LastEnd);
        }

        [Fact]
        public void ACreaturesKillingBlowStillKillsAndNobodyWins()
        {
            Fighting();
            _bo.Fall(type: HitType.EnemyHit, creature: Troll);
            _server.Deliver();

            Assert.Contains("died", _bo.Events);
            Assert.False(_ana.Flow.Active);
            Assert.Equal(EndReason.Died, _ana.Flow.LastEnd);
            Assert.Equal(0, _ana.Celebrations);
            Assert.DoesNotContain("lift", _ana.Events);
            Assert.DoesNotContain(Sound.Cue.CameToNothing, _ana.Heard);
            Assert.Equal(("Ana", "Bo", EndReason.Died), Assert.Single(_ana.Announced));
        }

        [Fact]
        public void AnotherPlayersKillingBlowStillKills()
        {
            var cy = _server.Join(Cy, "Cy", x: 4f);
            Fighting();
            _bo.Fall(by: cy);

            Assert.Contains("died", _bo.Events);
        }

        [Fact]
        public void AFallStillKillsInADuel()
        {
            Fighting();
            _bo.Fall(type: HitType.Fall);

            Assert.Contains("died", _bo.Events);
        }

        [Fact]
        public void YieldingHandsTheOpponentTheWinWithoutAKnockDown()
        {
            Fighting();
            _bo.Health = 80f;
            _bo.Flow.Forfeit();
            _server.Deliver();

            Assert.Equal(DuelOutcome.Lost, _bo.CardOutcome);
            Assert.Equal(DuelOutcome.Won, _ana.CardOutcome);
            Assert.Equal(80f, _bo.Health, 3);
            Assert.Equal(0, _bo.KnockDowns);
            Assert.DoesNotContain(Sound.Cue.KnockedDown, _bo.Heard);
            Assert.Equal(("Ana", "Bo", EndReason.Forfeited), Assert.Single(_ana.Announced));
        }

        [Fact]
        public void LeavingTheRingAfterTheFightBeganConcedesIt()
        {
            Fighting();
            _bo.Health = 10f;
            _bo.X = 40f;
            _server.Pass(0.25);

            Assert.Equal(EndReason.LeftRing, _bo.Flow.LastEnd);
            Assert.Equal(30f, _bo.Health, 3);
            Assert.Equal(DuelOutcome.Won, _ana.CardOutcome);
            Assert.Equal(("Ana", "Bo", EndReason.LeftRing), Assert.Single(_ana.Announced));
        }

        [Fact]
        public void LeavingTheRingDuringTheCountdownOnlyCallsTheDuelOff()
        {
            Fighting(begun: false);
            _bo.Health = 40f;
            _bo.X = 40f;
            _server.Pass(0.25);

            Assert.False(_ana.Flow.Active);
            Assert.False(_bo.Flow.Active);
            Assert.Equal(EndReason.OutOfRange, _bo.Flow.LastEnd);
            Assert.Equal(40f, _bo.Health, 3);
            Assert.Equal(0, _ana.Celebrations);
            Assert.Contains(Messages.OutOfRing, _bo.Said);
            Assert.Contains(Messages.OutOfRing, _ana.Said);
            Assert.Contains(Sound.Cue.CameToNothing, _ana.Heard);
            Assert.False(_ana.CardDecided);
        }

        [Fact]
        public void AFighterFarBeyondTheRingConcedesRatherThanCallingTheDuelOff()
        {
            Fighting();
            _bo.X = 500f;

            // Bo's own game judges first; the far side may call it off for the distance meanwhile.
            _server.Now += 0.25;
            _bo.Flow.Tick();

            Assert.Equal(EndReason.LeftRing, _bo.Flow.LastEnd);
        }

        [Fact]
        public void AnOpponentWhoIsBeyondAnyRingIsCalledOffAndNobodyWins()
        {
            Fighting();
            _ana.Health = 40f;
            _bo.X = 500f;

            // Ana's own game judges first; Bo's may meanwhile concede for leaving the ring.
            _server.Now += 0.25;
            _ana.Flow.Tick();

            Assert.False(_ana.Flow.Active);
            Assert.Equal(EndReason.OutOfRange, _ana.Flow.LastEnd);
            Assert.Contains(Messages.OutOfRing, _ana.Said);
            Assert.Equal(40f, _ana.Health, 3);
            Assert.Contains("send result " + EndReason.OutOfRange, _ana.Events);
        }

        [Fact]
        public void FightersAtOppositeEdgesOfTheRingFightOn()
        {
            Fighting(radius: 30f);
            _ana.X = 3f - 29f;
            _bo.X = 3f + 29f;
            _server.Pass(2);

            Assert.True(_ana.Flow.Active);
            Assert.True(_bo.Flow.Active);
        }

        [Fact]
        public void TheResultIsSentBeforeTheLeaseIsDropped()
        {
            Fighting();
            _bo.Flow.Forfeit();

            var result = _bo.Events.IndexOf("send result " + EndReason.Forfeited);
            var cleared = _bo.Events.LastIndexOf("lease cleared");
            Assert.True(result >= 0 && cleared > result, string.Join(" | ", _bo.Events));
        }

        [Fact]
        public void APairingLostAheadOfItsResultWaitsForTheResultToNameTheWinner()
        {
            Fighting();
            _server.Holding = true;
            _bo.Flow.Forfeit();
            _server.Pass(1);

            Assert.True(_ana.Flow.Active);

            _server.Holding = false;
            _server.Deliver();
            Assert.False(_ana.Flow.Active);
            Assert.Equal(DuelOutcome.Won, _ana.CardOutcome);
        }

        [Fact]
        public void AnOpponentWhoseGameStopsLosesTheDuelForNobody()
        {
            Fighting();
            _ana.Health = 40f;
            _bo.Crash();
            _server.Pass(4.5);
            Assert.True(_ana.Flow.Active);

            _server.Pass(3);
            Assert.False(_ana.Flow.Active);
            Assert.Equal(EndReason.LeaseLapsed, _ana.Flow.LastEnd);
            Assert.Contains(Messages.Named(Messages.LostTrack, "Bo"), _ana.Said);
            Assert.Equal(40f, _ana.Health, 3);
            Assert.Equal(0, _ana.Celebrations);
        }

        [Fact]
        public void AnAcceptThatNeverPairsIsCalledOffOnceTheGraceRunsOut()
        {
            _ana.Challenge(_bo);
            _server.Deliver();
            _bo.Flow.Accept();
            _server.InFlight.Clear();

            _server.Pass(9.75);
            Assert.True(_bo.Flow.Active);

            _server.Pass(0.5);
            Assert.False(_bo.Flow.Active);
            Assert.Equal(EndReason.LeaseLapsed, _bo.Flow.LastEnd);
        }

        [Fact]
        public void AResultFromADuelAlreadyOverChangesNothing()
        {
            Fighting();
            _bo.Flow.Forfeit();
            _server.Deliver();
            var health = _ana.Health;

            _server.Post(Ana, () => _ana.Flow.ReceiveResult(Bo, EndReason.Yielded));
            _server.Deliver();

            Assert.Equal(1, _ana.Celebrations);
            Assert.Equal(health, _ana.Health);
        }

        [Fact]
        public void AnEndingFromTheOtherSideThatNobodyWonHealsNobody()
        {
            Fighting();
            _ana.Health = 20f;
            _bo.Flow.EndLocal(EndReason.Withdrew, notify: true);
            _server.Deliver();

            Assert.False(_ana.Flow.Active);
            Assert.Equal(20f, _ana.Health, 3);
            Assert.Equal(0, _ana.Celebrations);
        }

        [Fact]
        public void AWinnerAtFullHealthIsNotLowered()
        {
            Fighting();
            _bo.Fall(by: _ana);
            _server.Deliver();

            Assert.Equal(100f, _ana.Health, 3);
        }

        [Fact]
        public void AWinnerLowOnHealthIsLiftedToHalf()
        {
            Fighting();
            _ana.Health = 20f;
            _bo.Fall(by: _ana);
            _server.Deliver();

            Assert.Equal(50f, _ana.Health, 3);
        }

        [Fact]
        public void AFighterFoundDeadOnTheirOwnTickEndsTheDuel()
        {
            Fighting();
            _bo.Standing.Dead = true;
            _server.Now += 0.25;
            _bo.Flow.Tick();

            Assert.False(_bo.Flow.Active);
            Assert.Equal(EndReason.Died, _bo.Flow.LastEnd);
        }

        [Fact]
        public void AnOpponentThisGameCannotPlaceEndsTheDuel()
        {
            Fighting();
            _bo.InWorld = false;
            _server.Now += 0.25;
            _ana.Flow.Tick();

            Assert.False(_ana.Flow.Active);
            Assert.Equal(EndReason.LeaseLapsed, _ana.Flow.LastEnd);
        }

        [Fact]
        public void OnceTheLeasesNoLongerAgreeNothingIsSurvivedThoughTheDuelIsStillWindingDown()
        {
            Fighting();
            _bo.Crash();
            _server.Pass(5.5);
            Assert.True(_ana.Flow.Active);

            _ana.Fall(type: HitType.Burning);
            Assert.Contains("died", _ana.Events);
        }

        [Fact]
        public void ADeathOutsideTheDuelsReachEndsItAtOnceOnBothSides()
        {
            Fighting();
            _bo.Fall(type: HitType.Drowning);
            _server.Deliver();

            Assert.False(_bo.Flow.Active);
            Assert.False(_ana.Flow.Active);
            Assert.Equal(EndReason.Died, _bo.Flow.LastEnd);
        }

        [Fact]
        public void WhileFightingTheLeaseIsRenewedEverySecond()
        {
            Fighting();
            var stamps = _ana.Events.Count(e => e == "stamp");
            _server.Pass(5);

            Assert.InRange(_ana.Events.Count(e => e == "stamp") - stamps, 4, 6);
            Assert.True(_server.Lands(_ana, _bo));
        }

        [Fact]
        public void ADuelEndsCleanlyWhateverTheEnding()
        {
            Fighting();
            _bo.Flow.Forfeit();
            _server.Deliver();

            foreach (var game in new[] { _ana, _bo })
            {
                Assert.False(game.Flow.Active);
                Assert.Contains("settle", game.Events);
                Assert.Contains("lease cleared", game.Events);
                Assert.Equal(0, game.Agreed());
            }
            Assert.False(_server.Lands(_ana, _bo));
        }
    }
}
