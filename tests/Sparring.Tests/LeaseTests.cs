using System.Collections.Generic;
using Xunit;

namespace Sparring.Tests
{
    /// <summary>
    /// A duel is two leases, one on each fighter, each naming the other until a moment in server
    /// time. Only both together, unexpired and naming each other, make a duel; anything less is no
    /// duel at all, and a lease that is not renewed ends it by itself.
    /// </summary>
    public class LeaseTests
    {
        private const int Nobody = 0, Ana = 1, Bo = 2, Cy = 3;

        /// <summary>Server time, in seconds, when the leases below were stamped.</summary>
        private const double At = 1000.0;

        /// <summary>The leases this game can read, by holder; a holder missing here has no lease this game can see.</summary>
        private sealed class World
        {
            private readonly Dictionary<int, Claim<int>> _leases = new Dictionary<int, Claim<int>>();

            public World Stamp(int holder, int names, double at = At)
            {
                _leases[holder] = new Claim<int>(names, Lease.UntilFor(at));
                return this;
            }

            public World Never(int holder)
            {
                _leases[holder] = default;
                return this;
            }

            public bool Duel(int holder, double now, out int opponent, bool practice = false)
            {
                _leases.TryGetValue(holder, out var mine);
                return Lease.Agreed(holder, mine, id => _leases.TryGetValue(id, out var claim) ? claim : (Claim<int>?)null, now, practice, out opponent);
            }

            public bool Duel(int holder, double now, bool practice = false) => Duel(holder, now, out _, practice);
        }

        [Fact]
        public void TwoFightersWhoseLeasesNameEachOtherAreInADuel()
        {
            var world = new World().Stamp(Ana, Bo).Stamp(Bo, Ana);

            Assert.True(world.Duel(Ana, At + 1, out var anasOpponent));
            Assert.Equal(Bo, anasOpponent);
            Assert.True(world.Duel(Bo, At + 1, out var bosOpponent));
            Assert.Equal(Ana, bosOpponent);
        }

        [Fact]
        public void ALeaseTheOtherSideDoesNotReturnGrantsNothing()
        {
            Assert.False(new World().Stamp(Ana, Bo).Stamp(Bo, Cy).Duel(Ana, At + 1));
            Assert.False(new World().Stamp(Ana, Bo).Stamp(Bo, Nobody).Duel(Ana, At + 1));
        }

        [Fact]
        public void ALeaseOnSomeoneWithoutOneGrantsNothing()
        {
            Assert.False(new World().Stamp(Ana, Bo).Duel(Ana, At + 1));
            Assert.False(new World().Stamp(Ana, Bo).Never(Bo).Duel(Ana, At + 1));
        }

        [Fact]
        public void ALeaseRunsOutFiveSecondsAfterItWasStamped()
        {
            var world = new World().Stamp(Ana, Bo).Stamp(Bo, Ana);

            Assert.True(world.Duel(Ana, At + 4.999));
            Assert.False(world.Duel(Ana, At + 5));
            Assert.False(world.Duel(Ana, At + 60));
        }

        [Fact]
        public void ADuelEndsByItselfWhenEitherSideStopsRenewing()
        {
            // Ana's game keeps renewing; Bo's stopped at At, as a crash or a lost connection would.
            var world = new World().Stamp(Ana, Bo, At + 3).Stamp(Bo, Ana, At);

            Assert.False(world.Duel(Ana, At + 5.5));
            Assert.False(world.Duel(Bo, At + 5.5));
        }

        [Fact]
        public void RenewingEverySecondKeepsADuelThroughAStallOfAFewSeconds()
        {
            // The renewal due a second later arrives three seconds late: both leases still hold.
            var world = new World().Stamp(Ana, Bo).Stamp(Bo, Ana);
            var late = At + Lease.RenewSeconds + 3;

            Assert.True(world.Duel(Ana, late));
            Assert.True(world.Duel(Bo, late));
        }

        [Fact]
        public void AClearedOrUnexpiredEmptyLeaseGrantsNothing()
        {
            // Cleared: nobody named and no expiry, as a duel leaves it once it ends.
            Assert.False(new World().Never(Ana).Stamp(Bo, Ana).Duel(Bo, At + 1));
            Assert.False(new World().Never(Ana).Stamp(Bo, Ana).Duel(Ana, At + 1));

            // Running, but naming nobody.
            Assert.False(new World().Stamp(Ana, Nobody).Stamp(Bo, Ana).Duel(Bo, At + 1));
            Assert.False(new World().Stamp(Ana, Nobody).Duel(Ana, At + 1));
        }

        [Fact]
        public void ALeaseNamingItsOwnHolderGrantsNothingOutsidePractice()
        {
            var world = new World().Stamp(Ana, Ana);

            Assert.False(world.Duel(Ana, At + 1));
            Assert.True(world.Duel(Ana, At + 1, out var opponent, practice: true));
            Assert.Equal(Ana, opponent);
        }

        [Fact]
        public void PracticeLetsNoOneSidedLeaseThrough()
        {
            Assert.False(new World().Stamp(Ana, Bo).Stamp(Bo, Cy).Duel(Ana, At + 1, practice: true));
            Assert.False(new World().Stamp(Ana, Bo).Duel(Ana, At + 1, practice: true));
        }

        [Fact]
        public void NoDuelNamesNoOpponent()
        {
            Assert.False(new World().Stamp(Ana, Bo).Stamp(Bo, Cy).Duel(Ana, At + 1, out var opponent));
            Assert.Equal(Nobody, opponent);
        }

        // ---- the countdown ----

        private static Lease.Terms StartingAt(double seconds) => new Lease.Terms { Radius = 20f, StartAtMs = (long)(seconds * 1000.0) };

        [Fact]
        public void NoBlowCountsBeforeTheCountdownEnds()
        {
            var terms = StartingAt(At + 3);

            Assert.False(terms.BegunAt(At + 2.999));
            Assert.True(terms.BegunAt(At + 3));
            Assert.True(terms.BegunAt(At + 30));
        }

        [Fact]
        public void TheCountdownShowsTheSecondsLeftAndNeverLessThanNone()
        {
            var terms = StartingAt(At + 3);

            Assert.Equal(2.0, terms.CountdownLeftAt(At + 1), 6);
            Assert.Equal(0.0, terms.CountdownLeftAt(At + 3), 6);
            Assert.Equal(0.0, terms.CountdownLeftAt(At + 10), 6);
        }

        [Fact]
        public void TermsThatNeverArrivedNeverLetABlowCount()
        {
            var never = default(Lease.Terms);

            Assert.False(never.BegunAt(At));
            Assert.False(never.BegunAt(double.MaxValue));
        }

        [Fact]
        public void BeforeTheServersClockArrivesNoCountdownHasFinished()
        {
            Assert.False(StartingAt(At + 3).BegunAt(0.0));
        }

        // ---- a start time off the network ----

        [Fact]
        public void AStartTimeInsideTheCountdownWindowIsKept()
        {
            var now = At;
            Assert.Equal((long)((now + 3) * 1000), Lease.SaneStart((long)((now + 3) * 1000), now, countdownSeconds: 3));
            Assert.Equal((long)(now * 1000), Lease.SaneStart((long)(now * 1000), now, countdownSeconds: 3));
            Assert.Equal((long)((now + 13) * 1000), Lease.SaneStart((long)((now + 13) * 1000), now, countdownSeconds: 3));
        }

        [Fact]
        public void AStartTimeInThePastBecomesAFreshCountdown()
        {
            Assert.Equal((long)((At + 3) * 1000), Lease.SaneStart((long)((At - 1) * 1000), At, countdownSeconds: 3));
            Assert.Equal((long)((At + 3) * 1000), Lease.SaneStart(0L, At, countdownSeconds: 3));
        }

        [Fact]
        public void AStartTimeTooFarAheadBecomesAFreshCountdown()
        {
            Assert.Equal((long)((At + 3) * 1000), Lease.SaneStart((long)((At + 13.5) * 1000), At, countdownSeconds: 3));
            Assert.Equal((long)((At + 3) * 1000), Lease.SaneStart(long.MaxValue, At, countdownSeconds: 3));
        }
    }
}
