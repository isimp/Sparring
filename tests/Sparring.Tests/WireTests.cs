using System;
using Xunit;

namespace Sparring.Tests
{
    /// <summary>
    /// What arrives from another player's game is their data, not Sparring's: a reason outside the
    /// known ones reads as the vaguest one, a name carries no markup and no more than a line's worth,
    /// and an announcement, which any game can broadcast into everyone's chat, is held to one every
    /// three seconds from each sender.
    /// </summary>
    public class WireTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(8)]
        [InlineData(99)]
        [InlineData(int.MaxValue)]
        public void AnEndingOutsideTheKnownOnesReadsAsALapsedDuelWithNoWinner(int sent)
        {
            var reason = Link.ReasonFromWire(sent);
            Assert.Equal(EndReason.LeaseLapsed, reason);
            Assert.False(reason.Decided());
        }

        [Fact]
        public void EveryKnownEndingArrivesAsSent()
        {
            foreach (EndReason reason in Enum.GetValues(typeof(EndReason)))
                Assert.Equal(reason, Link.ReasonFromWire((int)reason));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(9)]
        [InlineData(1000)]
        public void ARefusalOutsideTheKnownOnesReadsAsOccupied(int sent)
        {
            Assert.Equal(Busy.Occupied, Link.BusyFromWire(sent));
        }

        [Fact]
        public void EveryKnownRefusalArrivesAsSent()
        {
            foreach (Busy why in Enum.GetValues(typeof(Busy)))
                Assert.Equal(why, Link.BusyFromWire((int)why));
        }

        [Fact]
        public void ANameCarriesNoMarkup()
        {
            var name = Messages.WireName("<color=red>Bo</color>");
            Assert.DoesNotContain("<", name, StringComparison.Ordinal);
            Assert.DoesNotContain(">", name, StringComparison.Ordinal);
            Assert.Contains("Bo", name, StringComparison.Ordinal);
        }

        [Fact]
        public void ANameIsCutToThirtyTwoCharacters()
        {
            Assert.Equal(new string('a', 32), Messages.WireName(new string('a', 200)));
            Assert.Equal("Bo", Messages.WireName("  Bo  "));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("<>")]
        public void ANamelessChallengerShowsAsSomeone(string sent)
        {
            Assert.Equal("Someone", Messages.ShownName(sent));
            Assert.Equal("", Messages.WireName(sent));
        }

        [Fact]
        public void AnnouncementsFromOneSenderAreHeldToOneEveryThreeSeconds()
        {
            var gate = new Throttle(3f);

            Assert.True(gate.Allows(7L, 10f));
            Assert.False(gate.Allows(7L, 11f));
            Assert.False(gate.Allows(7L, 12.9f));
            Assert.True(gate.Allows(7L, 13f));
        }

        [Fact]
        public void AnotherSendersAnnouncementIsNotHeldUp()
        {
            var gate = new Throttle(3f);

            Assert.True(gate.Allows(7L, 10f));
            Assert.True(gate.Allows(8L, 10.5f));
        }

        [Fact]
        public void ANewSessionHoldsNothingBack()
        {
            var gate = new Throttle(3f);
            gate.Allows(7L, 10f);
            gate.Clear();

            Assert.True(gate.Allows(7L, 10.5f));
        }
    }
}
