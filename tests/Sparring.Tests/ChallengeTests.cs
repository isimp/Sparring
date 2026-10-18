using System;
using Xunit;

namespace Sparring.Tests
{
    /// <summary>
    /// Who may duel whom. A duel starts only between two free players: out of combat by the game's
    /// own ten second rule, PvP off, not sitting, sleeping or otherwise occupied, and with nothing
    /// hostile around the ring. The challenged player's game decides, before anything is shown, and
    /// turns down what it cannot take with the reason; it also refuses a challenge from further away
    /// than the proposed ring, a second one while one is on screen, and the same two players again
    /// within fifteen seconds of an invitation that came to nothing.
    /// </summary>
    public class ChallengeTests
    {
        private const int Nobody = 0, Ana = 1, Bo = 2, Cy = 3;
        private const double At = 1000.0;

        private static Busy Readiness(Standing standing, bool requireOutOfCombat = true, bool requireClearGround = true, bool hostile = false) =>
            Challenges.Readiness(standing, requireOutOfCombat, requireClearGround, () => hostile);

        [Fact]
        public void AFreePlayerMayDuel()
        {
            Assert.Equal(Busy.None, Readiness(default));
        }

        [Theory]
        [InlineData("dead")]
        [InlineData("in a cutscene")]
        [InlineData("teleporting")]
        [InlineData("sitting")]
        [InlineData("in bed")]
        [InlineData("asleep")]
        public void AnOccupiedPlayerIsNotFree(string how)
        {
            var standing = default(Standing);
            switch (how)
            {
                case "dead": standing.Dead = true; break;
                case "in a cutscene": standing.InCutscene = true; break;
                case "teleporting": standing.Teleporting = true; break;
                case "sitting": standing.Attached = true; break;
                case "in bed": standing.InBed = true; break;
                case "asleep": standing.Sleeping = true; break;
            }
            Assert.Equal(Busy.Occupied, Readiness(standing));
        }

        [Fact]
        public void APlayerWithPvpOnIsNotFree()
        {
            Assert.Equal(Busy.PvpOn, Readiness(new Standing { PvpOn = true }));
        }

        [Fact]
        public void APlayerStillInCombatIsNotFreeWhileTheRuleIsOn()
        {
            Assert.Equal(Busy.InCombat, Readiness(new Standing { InCombat = true }));
            Assert.Equal(Busy.None, Readiness(new Standing { InCombat = true }, requireOutOfCombat: false));
        }

        [Fact]
        public void SomethingHostileAroundTheRingStopsADuelWhileTheRuleIsOn()
        {
            Assert.Equal(Busy.CreaturesNear, Readiness(default, hostile: true));
            Assert.Equal(Busy.None, Readiness(default, requireClearGround: false, hostile: true));
        }

        [Fact]
        public void WithTheGroundRuleOffTheGroundIsNotSearched()
        {
            var searched = false;
            Challenges.Readiness(default, requireOutOfCombat: true, requireClearGround: false, () => searched = true);
            Assert.False(searched);
        }

        // ---- a challenge arriving ----

        /// <summary>What the challenged player's game makes of a challenge from Bo.</summary>
        private static Arrival Arrive(out Busy why, bool dueling = false, int incoming = Nobody, int outgoing = Nobody,
            bool resting = false, bool tooFar = false, Busy readiness = Busy.None) =>
            Challenges.Arrive(Bo, dueling, incoming, outgoing, resting, tooFar, () => readiness, out why);

        [Fact]
        public void AChallengeToAFreePlayerIsShown()
        {
            Assert.Equal(Arrival.Shown, Arrive(out var why));
            Assert.Equal(Busy.None, why);
        }

        [Fact]
        public void AChallengeWhileDuellingIsTurnedDown()
        {
            Assert.Equal(Arrival.Refused, Arrive(out var why, dueling: true));
            Assert.Equal(Busy.Dueling, why);
        }

        [Fact]
        public void AChallengeWhileAnotherIsOnScreenIsTurnedDown()
        {
            Assert.Equal(Arrival.Refused, Arrive(out var why, incoming: Cy));
            Assert.Equal(Busy.Answering, why);
        }

        [Fact]
        public void TheChallengeAlreadyOnScreenIsNotShownAgain()
        {
            Assert.Equal(Arrival.AlreadyShown, Arrive(out var why, incoming: Bo));
            Assert.Equal(Busy.None, why);
        }

        [Fact]
        public void AChallengeTooSoonAfterTheLastCameToNothingIsTurnedDown()
        {
            Assert.Equal(Arrival.Refused, Arrive(out var why, resting: true));
            Assert.Equal(Busy.Cooldown, why);
        }

        [Fact]
        public void AChallengeFromFurtherThanTheRingIsTurnedDown()
        {
            Assert.Equal(Arrival.Refused, Arrive(out var why, tooFar: true));
            Assert.Equal(Busy.TooFar, why);
        }

        [Theory]
        [InlineData((int)Busy.Occupied)]
        [InlineData((int)Busy.PvpOn)]
        [InlineData((int)Busy.InCombat)]
        [InlineData((int)Busy.CreaturesNear)]
        public void AChallengeToAPlayerWhoIsNotFreeIsTurnedDownWithTheReason(int sent)
        {
            var reason = (Busy)sent;
            Assert.Equal(Arrival.Refused, Arrive(out var why, readiness: reason));
            Assert.Equal(reason, why);
        }

        [Fact]
        public void TwoPlayersChallengingEachOtherAtOnceStartTheDuel()
        {
            Assert.Equal(Arrival.Crossed, Arrive(out var why, outgoing: Bo));
            Assert.Equal(Busy.None, why);
        }

        [Fact]
        public void ACrossedChallengeIsStillTurnedDownWhenThePlayerIsNotFree()
        {
            Assert.Equal(Arrival.Refused, Arrive(out var why, outgoing: Bo, readiness: Busy.InCombat));
            Assert.Equal(Busy.InCombat, why);
        }

        // ---- the pause after an invitation came to nothing ----

        [Fact]
        public void TheSameTwoPlayersWaitFifteenSecondsAfterAnInvitationCameToNothing()
        {
            var pauses = new Pauses<int>();
            pauses.Start(Bo, At);

            Assert.True(pauses.Holds(Bo, At + 14.9));
            Assert.False(pauses.Holds(Bo, At + 15));
        }

        [Fact]
        public void APauseWithOnePlayerLeavesEveryoneElseFree()
        {
            var pauses = new Pauses<int>();
            pauses.Start(Bo, At);

            Assert.False(pauses.Holds(Cy, At + 1));
            Assert.False(pauses.Holds(Ana, At + 1));
        }

        [Fact]
        public void NobodyIsNeverPaused()
        {
            var pauses = new Pauses<int>();
            pauses.Start(Nobody, At);

            Assert.Equal(0, pauses.Count);
            Assert.False(pauses.Holds(Nobody, At + 1));
        }

        [Fact]
        public void ManyPausesDropTheOnesThatRanOutAndKeepTheRest()
        {
            var pauses = new Pauses<int>();
            for (var player = 100; player < 117; player++) pauses.Start(player, At);
            Assert.Equal(17, pauses.Count);

            // By now the seventeen started at At have run out; Cy's has not.
            pauses.Start(Cy, At + 10);
            pauses.Start(Bo, At + 16);

            Assert.Equal(2, pauses.Count);
            Assert.True(pauses.Holds(Cy, At + 16));
            Assert.True(pauses.Holds(Bo, At + 16));
        }

        // ---- how far a challenge reaches ----

        [Fact]
        public void AChallengeReachesAsFarAsTheProposedRing()
        {
            Assert.False(Arena.Beyond(19.9f, radius: 20f, fallback: 30f));
            Assert.False(Arena.Beyond(20f, radius: 20f, fallback: 30f));
            Assert.True(Arena.Beyond(20.1f, radius: 20f, fallback: 30f));
        }

        [Fact]
        public void WithNoRingProposedTheDefaultRingIsTheReach()
        {
            Assert.False(Arena.Beyond(29f, radius: 0f, fallback: 30f));
            Assert.True(Arena.Beyond(31f, radius: 0f, fallback: 30f));
        }

        [Fact]
        public void ASmallRingStillReachesFiveMetres()
        {
            Assert.False(Arena.Beyond(4.9f, radius: 2f, fallback: 30f));
            Assert.True(Arena.Beyond(5.1f, radius: 2f, fallback: 30f));
        }
    }
}
