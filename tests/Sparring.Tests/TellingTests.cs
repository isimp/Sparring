using System;
using Xunit;

namespace Sparring.Tests
{
    /// <summary>
    /// What a duel's end tells: the line in everyone's chat names who yielded to whom, or why a duel
    /// nobody won stopped; and the card is shown only for a duel somebody won with a blow landed,
    /// timed from the moment blows began to count rather than from the handshake.
    /// </summary>
    public class TellingTests
    {
        [Theory]
        [InlineData((int)EndReason.Yielded, "Bo yields to Ana.")]
        [InlineData((int)EndReason.Forfeited, "Bo forfeits to Ana.")]
        [InlineData((int)EndReason.LeftRing, "Bo left the ring and yields to Ana.")]
        [InlineData((int)EndReason.Died, "Bo fell to something else")]
        [InlineData((int)EndReason.OutOfRange, "was called off")]
        [InlineData((int)EndReason.LeaseLapsed, "is over")]
        public void TheAnnouncementSaysWhoYieldedToWhomOrWhyItStopped(int reason, string says)
        {
            var line = Announcer.Compose("Ana", "Bo", (EndReason)reason);
            Assert.Contains(says, line, StringComparison.Ordinal);
        }

        [Fact]
        public void TheCardIsShownOnlyForADuelSomebodyWonWithABlowLanded()
        {
            Assert.True(Scorecard.Shows(decided: true, took: 1, gave: 0));
            Assert.True(Scorecard.Shows(decided: true, took: 0, gave: 3));
            Assert.False(Scorecard.Shows(decided: true, took: 0, gave: 0));
            Assert.False(Scorecard.Shows(decided: false, took: 4, gave: 4));
        }

        [Fact]
        public void TheDuelIsTimedFromWhenBlowsBeganToCount()
        {
            Assert.Equal(40.0, Scorecard.Fought(startAtMs: 1003000L, startedAt: 1000.0, now: 1043.0), 6);
        }

        [Fact]
        public void WithNoStartTimeTheDuelIsTimedFromTheHandshake()
        {
            Assert.Equal(43.0, Scorecard.Fought(startAtMs: 0L, startedAt: 1000.0, now: 1043.0), 6);
        }

        [Fact]
        public void ADuelOverBeforeBlowsCountedLastedNoTime()
        {
            Assert.Equal(0.0, Scorecard.Fought(startAtMs: 1003000L, startedAt: 1000.0, now: 1001.0), 6);
        }
    }
}
