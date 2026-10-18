using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace Sparring.Tests
{
    /// <summary>
    /// How a player drives a duel: the yield key has to be held, not tapped, and only once blows
    /// count; /duel's words and their other names; and who "the player you are looking at" is.
    /// </summary>
    public class ControlTests
    {
        // ---- holding the yield key ----

        [Fact]
        public void ATapDoesNotYield()
        {
            var hold = new YieldHold();
            Assert.False(hold.Advance(fighting: true, keyDown: true, 0.25f, holdSeconds: 0.7f));
            Assert.False(hold.Advance(fighting: true, keyDown: false, 0.25f, holdSeconds: 0.7f));
            Assert.False(hold.Held);
        }

        [Fact]
        public void HoldingTheKeyForTheWholeTimeYieldsOnce()
        {
            var hold = new YieldHold();
            Assert.False(hold.Advance(true, true, 0.25f, 0.7f));
            Assert.False(hold.Advance(true, true, 0.25f, 0.7f));
            Assert.True(hold.Advance(true, true, 0.25f, 0.7f));
            Assert.False(hold.Advance(true, true, 0.25f, 0.7f));
        }

        [Fact]
        public void HoldingForExactlyTheHoldTimeIsEnough()
        {
            var hold = new YieldHold();
            Assert.False(hold.Advance(true, true, 0.25f, 0.5f));
            Assert.True(hold.Advance(true, true, 0.25f, 0.5f));
        }

        [Fact]
        public void LettingGoEarlyStartsTheHoldOver()
        {
            var hold = new YieldHold();
            hold.Advance(true, true, 0.25f, 0.7f);
            hold.Advance(true, true, 0.25f, 0.7f);
            hold.Advance(true, false, 0.25f, 0.7f);

            Assert.False(hold.Advance(true, true, 0.25f, 0.7f));
            Assert.False(hold.Advance(true, true, 0.25f, 0.7f));
            Assert.True(hold.Advance(true, true, 0.25f, 0.7f));
        }

        [Fact]
        public void NothingIsHeldOutsideTheFightOrDuringTheCountdown()
        {
            var hold = new YieldHold();
            hold.Advance(true, true, 0.25f, 0.7f);
            hold.Advance(true, true, 0.25f, 0.7f);

            Assert.False(hold.Advance(fighting: false, keyDown: true, 0.25f, 0.7f));
            Assert.False(hold.Held);
            Assert.False(hold.Advance(true, true, 0.25f, 0.7f));
        }

        [Fact]
        public void LettingGoOfInputLetsGoOfTheHold()
        {
            var hold = new YieldHold();
            hold.Advance(true, true, 0.25f, 0.7f);
            hold.Release();

            Assert.False(hold.Held);
            Assert.Equal(0f, hold.Progress(0.7f));
        }

        [Fact]
        public void TheHoldShowsHowFarThroughItIs()
        {
            var hold = new YieldHold();
            hold.Advance(true, true, 0.35f, 0.7f);

            Assert.Equal(0.5f, hold.Progress(0.7f), 3);
            Assert.Equal(0f, hold.Progress(0f));
            Assert.Equal(1f, hold.Progress(0.1f), 3);
        }

        // ---- /duel's words ----

        [Theory]
        [InlineData("", (int)Order.Challenge)]
        [InlineData("accept", (int)Order.Accept)]
        [InlineData("yes", (int)Order.Accept)]
        [InlineData("decline", (int)Order.Decline)]
        [InlineData("no", (int)Order.Decline)]
        [InlineData("cancel", (int)Order.Withdraw)]
        [InlineData("withdraw", (int)Order.Withdraw)]
        [InlineData("yield", (int)Order.Yield)]
        [InlineData("forfeit", (int)Order.Yield)]
        [InlineData("surrender", (int)Order.Yield)]
        [InlineData("rematch", (int)Order.Rematch)]
        [InlineData("again", (int)Order.Rematch)]
        [InlineData("preview", (int)Order.Preview)]
        [InlineData("status", (int)Order.Status)]
        [InlineData("panels", (int)Order.Panels)]
        [InlineData("testyield", (int)Order.TestYield)]
        [InlineData("testwin", (int)Order.TestWin)]
        [InlineData("testmessages", (int)Order.TestMessages)]
        [InlineData("spar", (int)Order.Spar)]
        [InlineData("testannounce", (int)Order.TestAnnounce)]
        [InlineData("testsound", (int)Order.TestSound)]
        public void EachWordAndItsOtherNamesDoOneThing(string word, int order)
        {
            Assert.Equal((Order)order, Commands.Read(word, out _));
        }

        [Theory]
        [InlineData("15", 15f)]
        [InlineData("12.5", 12.5f)]
        [InlineData("-5", -5f)]
        public void ANumberIsARingSize(string word, float metres)
        {
            Assert.Equal(Order.Ring, Commands.Read(word, out var read));
            Assert.Equal(metres, read);
        }

        [Theory]
        [InlineData("12,5")]
        [InlineData("selftest")]
        [InlineData("duel")]
        public void AnythingElseIsLeftForTheSelfTestOrAHint(string word)
        {
            Assert.Equal(Order.Other, Commands.Read(word, out _));
        }

        [Theory]
        [InlineData("30", 30f)]
        [InlineData("500", 60f)]
        [InlineData("2", 5f)]
        [InlineData(null, 20f)]
        [InlineData("wide", 20f)]
        public void APreviewIsTheSizeAskedForWithinTheRingsBoundsOrTheDefault(string typed, float ring)
        {
            Assert.Equal(ring, Commands.Size(typed, fallback: 20f));
        }

        // ---- who you are looking at ----

        private static Vector3 V(float x, float y, float z)
        {
            var v = default(Vector3);
            v.x = x;
            v.y = y;
            v.z = z;
            return v;
        }

        /// <summary>Looking along +z from 1.5 m up at the origin; players stand on the ground.</summary>
        private static string Picked(params (string Who, Vector3 At, bool Dead)[] others) =>
            LookTarget.Pick(V(0f, 1.5f, 0f), V(0f, 0f, 1f), new List<(string, Vector3, bool)>(others), range: 10f, maxAngle: 25f);

        [Fact]
        public void ThePlayerNearestTheMiddleOfYourViewIsPicked()
        {
            Assert.Equal("Bo", Picked(("Ana", V(2f, 0f, 6f), false), ("Bo", V(0.5f, 0f, 6f), false)));
            Assert.Equal("Bo", Picked(("Bo", V(0.5f, 0f, 6f), false), ("Ana", V(2f, 0f, 6f), false)));
        }

        [Fact]
        public void SomeoneCloseInFrontIsPickedByTheirBodyNotTheirFeet()
        {
            Assert.Equal("Bo", Picked(("Bo", V(0f, 0f, 2f), false)));
        }

        [Fact]
        public void SomeoneBeyondReachOrOffToTheSideIsNotPicked()
        {
            Assert.Null(Picked(("Far", V(0f, 0f, 15f), false)));
            Assert.Null(Picked(("Aside", V(6f, 0f, 3f), false)));
        }

        [Fact]
        public void TheDeadAreNotPicked()
        {
            Assert.Null(Picked(("Gone", V(0f, 0f, 5f), true)));
            Assert.Equal("Bo", Picked(("Gone", V(0f, 0f, 5f), true), ("Bo", V(1f, 0f, 5f), false)));
        }

        [Fact]
        public void WithNobodyAboutNobodyIsPicked()
        {
            Assert.Null(Picked());
        }
    }
}
