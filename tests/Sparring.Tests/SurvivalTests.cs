using Xunit;
using HitType = HitData.HitType;

namespace Sparring.Tests
{
    /// <summary>
    /// What a duel survives. Only the opponent's killing blow, or the fire and poison they leave
    /// behind, which arrive later as damage over time with nobody named on them, becomes a yield.
    /// A creature, a fall or drowning still kills, and so does everything outside a duel both
    /// leases agree on. Then how a duel ends: who won, and who gets back up with what.
    /// </summary>
    public class SurvivalTests
    {
        private const int Nobody = 0, Ana = 1, Bo = 2, Cy = 3, Troll = 50;

        /// <summary>A blow from someone this game has loaded.</summary>
        private static Blow<int> By(int who, HitType type = HitType.PlayerHit) => new Blow<int>(named: true, who, type);

        /// <summary>A hit with nobody named on it.</summary>
        private static Blow<int> Unnamed(HitType type) => new Blow<int>(named: false, Nobody, type);

        /// <summary>Whether Ana, duelling Bo with both leases agreeing, gets up from this last hit.</summary>
        private static bool AnaSurvives(Blow<int>? lastHit, bool local = true, int opponent = Bo, int agreedWith = Bo) =>
            Survival.ShouldSurvive(local, opponent, agreedWith, lastHit);

        [Fact]
        public void TheOpponentsKillingBlowBecomesAYield()
        {
            Assert.True(AnaSurvives(By(Bo)));
        }

        [Fact]
        public void AnotherPlayersKillingBlowStillKills()
        {
            Assert.False(AnaSurvives(By(Cy)));
        }

        [Fact]
        public void ACreaturesKillingBlowStillKills()
        {
            Assert.False(AnaSurvives(By(Troll, HitType.EnemyHit)));
        }

        [Theory]
        [InlineData(HitType.Burning)]
        [InlineData(HitType.Poisoned)]
        public void FireOrPoisonWithNobodyNamedIsTheOpponents(HitType type)
        {
            Assert.True(AnaSurvives(Unnamed(type)));
        }

        [Theory]
        [InlineData(HitType.Fall)]
        [InlineData(HitType.Drowning)]
        [InlineData(HitType.Freezing)]
        [InlineData(HitType.Water)]
        [InlineData(HitType.Smoke)]
        [InlineData(HitType.EdgeOfWorld)]
        [InlineData(HitType.Impact)]
        [InlineData(HitType.Tree)]
        [InlineData(HitType.Structural)]
        [InlineData(HitType.CinderFire)]
        [InlineData(HitType.AshlandsOcean)]
        [InlineData(HitType.AshlandsLava)]
        [InlineData(HitType.Undefined)]
        public void HarmWithNobodyNamedOtherThanFireAndPoisonStillKills(HitType type)
        {
            Assert.False(AnaSurvives(Unnamed(type)));
        }

        [Theory]
        [InlineData(HitType.PlayerHit)]
        [InlineData(HitType.Burning)]
        public void ABlowNamingSomeoneThisGameCannotFindStillKills(HitType type)
        {
            Assert.False(AnaSurvives(new Blow<int>(named: true, Nobody, type)));
        }

        [Fact]
        public void OutsideADuelEveryKillingBlowKills()
        {
            Assert.False(AnaSurvives(By(Bo), opponent: Nobody, agreedWith: Nobody));
            Assert.False(AnaSurvives(Unnamed(HitType.Burning), opponent: Nobody, agreedWith: Nobody));
        }

        [Fact]
        public void ADuelTheLeasesNoLongerAgreeOnIsNoReasonToSurvive()
        {
            Assert.False(AnaSurvives(By(Bo), agreedWith: Nobody));
        }

        [Fact]
        public void LeasesAgreeingOnSomeoneElseAreNoReasonToSurvive()
        {
            Assert.False(AnaSurvives(By(Cy), agreedWith: Cy));
            Assert.False(AnaSurvives(By(Bo), agreedWith: Cy));
        }

        [Fact]
        public void OnlyThePlayersOwnGameSparesThem()
        {
            Assert.False(AnaSurvives(By(Bo), local: false));
        }

        [Fact]
        public void WithNoRecordOfTheLastHitTheDeathStands()
        {
            Assert.False(AnaSurvives(null));
        }

        // ---- how a duel ends ----

        [Fact]
        public void LeavingTheRingAfterTheFightBeganConcedesIt()
        {
            var reason = Arena.Stray(begun: true);
            Assert.Equal(EndReason.LeftRing, reason);
            Assert.True(reason.Decided());
        }

        [Fact]
        public void LeavingTheRingDuringTheCountdownOnlyCallsTheDuelOff()
        {
            var reason = Arena.Stray(begun: false);
            Assert.Equal(EndReason.OutOfRange, reason);
            Assert.False(reason.Decided());
        }

        [Theory]
        [InlineData((int)EndReason.Yielded, true)]
        [InlineData((int)EndReason.Forfeited, true)]
        [InlineData((int)EndReason.LeftRing, true)]
        [InlineData((int)EndReason.Declined, false)]
        [InlineData((int)EndReason.Withdrew, false)]
        [InlineData((int)EndReason.OutOfRange, false)]
        [InlineData((int)EndReason.Died, false)]
        [InlineData((int)EndReason.LeaseLapsed, false)]
        public void OnlyAYieldAForfeitOrLeavingTheRingHasAWinner(int ending, bool decided)
        {
            Assert.Equal(decided, ((EndReason)ending).Decided());
        }

        [Fact]
        public void FightersAtOppositeEdgesOfTheRingAreNotCalledOff()
        {
            for (var radius = Arena.Floor; radius <= Arena.Ceiling; radius += 0.5f)
                Assert.True(Arena.OpponentRange(radius) > 2f * radius, $"a {radius} m ring");
        }

        // ---- getting back up ----

        [Fact]
        public void TheFighterIsLiftedToTheirShareOfHealth()
        {
            Assert.True(Recovery.Lifted(0f, 200f, 0.3f, out var health));
            Assert.Equal(60f, health, 3);
        }

        [Fact]
        public void NobodyIsLoweredByTheEndOfADuel()
        {
            Assert.False(Recovery.Lifted(150f, 200f, 0.5f, out _));
            Assert.False(Recovery.Lifted(100f, 200f, 0.5f, out _));
        }

        [Fact]
        public void AtLeastOnePointOfHealthComesBack()
        {
            Assert.True(Recovery.Lifted(0f, 2f, 0.05f, out var health));
            Assert.Equal(1f, health, 3);
            Assert.True(Recovery.Lifted(0f, 200f, 0f, out health));
            Assert.Equal(1f, health, 3);
        }

        [Fact]
        public void AShareOutsideNoneToWholeIsHeldToIt()
        {
            Assert.True(Recovery.Lifted(10f, 200f, 1.5f, out var health));
            Assert.Equal(200f, health, 3);
            Assert.True(Recovery.Lifted(0f, 200f, -1f, out health));
            Assert.Equal(1f, health, 3);
        }

        // ---- the card ----

        [Fact]
        public void TheCardCountsTheOpponentsBlowsAndNothingElse()
        {
            Assert.True(Scorecard.Counts(12f, active: true, practising: false, fromOpponent: true));
            Assert.False(Scorecard.Counts(12f, active: true, practising: false, fromOpponent: false));
        }

        [Fact]
        public void TheCardCountsNothingOutsideADuelAndNoBlowThatDidNoHarm()
        {
            Assert.False(Scorecard.Counts(12f, active: false, practising: false, fromOpponent: true));
            Assert.False(Scorecard.Counts(0f, active: true, practising: false, fromOpponent: true));
            Assert.False(Scorecard.Counts(-3f, active: true, practising: false, fromOpponent: true));
        }

        [Fact]
        public void APracticeDuelsCardCountsEveryBlow()
        {
            Assert.True(Scorecard.Counts(12f, active: true, practising: true, fromOpponent: false));
        }

        [Fact]
        public void TheCardAndTheYieldCreditTheSameBlowsToTheOpponent()
        {
            Assert.True(Survival.FromOpponent(By(Bo), Bo));
            Assert.True(Survival.FromOpponent(Unnamed(HitType.Burning), Bo));
            Assert.False(Survival.FromOpponent(By(Cy), Bo));
            Assert.False(Survival.FromOpponent(Unnamed(HitType.Fall), Bo));
        }
    }
}
