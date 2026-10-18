using Xunit;

namespace Sparring.Tests
{
    /// <summary>
    /// What a player's game keeps and forgets around a duel: with no character in the world, as
    /// between a death and the respawn, every duel and challenge is forgotten but the pauses and the
    /// last opponent are kept; a practice duel pairs you with yourself only while it lasts; and the
    /// panel's timers read off the duel's own times.
    /// </summary>
    public class AfterwardsTests
    {
        private const int Ana = 1, Bo = 2;

        private readonly Server _server = new Server();
        private readonly Game _ana, _bo;

        public AfterwardsTests()
        {
            _ana = _server.Join(Ana, "Ana", x: 0f);
            _bo = _server.Join(Bo, "Bo", x: 6f);
        }

        private void Respawn()
        {
            _ana.InWorld = false;
            _server.Pass(0.25);
            _ana.InWorld = true;
        }

        [Fact]
        public void WithNoCharacterEveryDuelAndChallengeIsForgotten()
        {
            _ana.Challenge(_bo);
            _server.Deliver();
            _bo.Challenge(_ana);
            _ana.InWorld = false;

            // Ana's game alone, before anything from Bo's could clear it.
            _ana.Flow.Tick();
            _server.Deliver();

            Assert.False(_ana.Flow.Active);
            Assert.False(_ana.Flow.HasOutgoing);
            Assert.False(_ana.Flow.HasInvite);
        }

        [Fact]
        public void APauseOutlastsDeathAndTheRespawn()
        {
            _ana.Challenge(_bo);
            _server.Deliver();
            _bo.Flow.Decline();
            _server.Deliver();
            Respawn();

            _ana.Challenge(_bo);
            Assert.Contains(Messages.Named(Messages.WaitBeforeAgain, "Bo"), _ana.Said);
        }

        [Fact]
        public void ARematchAfterARespawnProposesTheSameRing()
        {
            _ana.Challenge(_bo, 30f);
            _server.Deliver();
            _bo.Flow.Accept();
            _server.Deliver();
            _server.Pass(3.25);
            _bo.Flow.Forfeit();
            _server.Deliver();
            _server.Pass(15.25);
            Respawn();

            _ana.Flow.Rematch();
            _server.Deliver();
            _bo.Flow.Accept();
            _server.Deliver();

            Assert.True(_ana.Flow.Active);
            Assert.Equal(30f, _ana.Flow.Terms.Radius);
            Assert.Equal(30f, _bo.Flow.Terms.Radius);
        }

        [Fact]
        public void ARematchNeedsSomeoneFoughtBeforeAndNearby()
        {
            _ana.Flow.Rematch();
            Assert.Contains(Messages.NeverDueled, _ana.Said);

            _ana.Challenge(_bo);
            _server.Deliver();
            _bo.Flow.Accept();
            _server.Deliver();
            _bo.Flow.Forfeit();
            _server.Deliver();
            _bo.InWorld = false;

            _ana.Flow.Rematch();
            Assert.Contains(Messages.Named(Messages.NotNearby, "Bo"), _ana.Said);
        }

        [Fact]
        public void APracticeDuelPairsYouWithYourselfOnlyWhileItLasts()
        {
            _ana.Flow.Practice();

            Assert.True(_ana.Flow.Active);
            Assert.True(_ana.Flow.Practising);
            Assert.Equal(Ana, _ana.Flow.Opponent);
            Assert.Equal(Ana, _ana.Agreed());

            _ana.Flow.Forfeit();
            Assert.False(_ana.Flow.Practising);
            Assert.False(_ana.SelfPair);
            Assert.Equal(0, _ana.Agreed());
        }

        [Fact]
        public void DyingInAPracticeDuelLeavesNoSelfPairing()
        {
            _ana.Flow.Practice();
            Respawn();

            Assert.False(_ana.Flow.Active);
            Assert.False(_ana.Flow.Practising);
            Assert.False(_ana.SelfPair);
        }

        [Fact]
        public void APracticeDuelsBlowYieldsToYourself()
        {
            _ana.Flow.Practice();
            _server.Pass(3.25);
            _ana.Fall(by: _ana);

            Assert.DoesNotContain("died", _ana.Events);
            Assert.Equal(EndReason.Yielded, _ana.Flow.LastEnd);
        }

        [Fact]
        public void TheChallengeOnScreenCountsDownItsSeconds()
        {
            _ana.Challenge(_bo);
            _server.Deliver();
            _server.Pass(10);

            Assert.Equal(20.0, _bo.Flow.InviteSecondsLeft, 3);
            Assert.Equal(20.0, _ana.Flow.InviteSecondsLeft, 3);
        }

        [Fact]
        public void TheStartIsHeldOnTheScreenForASecond()
        {
            _ana.Challenge(_bo);
            _server.Deliver();
            _bo.Flow.Accept();
            _server.Deliver();

            _server.Pass(2.75);
            Assert.True(_ana.Flow.CountingDown);
            Assert.False(_ana.Flow.ShowingStart);

            _server.Pass(0.5);
            Assert.False(_ana.Flow.CountingDown);
            Assert.True(_ana.Flow.ShowingStart);

            _server.Pass(1);
            Assert.False(_ana.Flow.ShowingStart);
        }
    }
}
