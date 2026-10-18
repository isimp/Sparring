using System;
using Xunit;

namespace Sparring.Tests
{
    /// <summary>
    /// When the game will not take one of Sparring's hooks, Sparring switches off as a whole rather
    /// than run a duel with part of its protection missing. While it is off, /duel answers every
    /// word with why, naming what the game changed, and only the self-test still answers, so it
    /// can tell what is missing.
    /// </summary>
    public sealed class ShutoffTests : IDisposable
    {
        public ShutoffTests() => Shutoff.Reset();

        public void Dispose() => Shutoff.Reset();

        [Fact]
        public void SparringIsOnUntilAHookFails()
        {
            Assert.False(Shutoff.Engaged);
            Assert.Null(Shutoff.Answer(() => false));
        }

        [Fact]
        public void OnceOffEveryDuelWordIsAnsweredWithWhatTheGameChanged()
        {
            Shutoff.Engage("Character.CheckDeath");

            Assert.True(Shutoff.Engaged);
            var why = Shutoff.Answer(() => false);
            Assert.Contains("Sparring is off", why, StringComparison.Ordinal);
            Assert.Contains("Character.CheckDeath", why, StringComparison.Ordinal);
        }

        [Fact]
        public void OnceOffTheSelfTestStillAnswersItsOwnWords()
        {
            Shutoff.Engage("Character.CheckDeath");

            var asked = false;
            Assert.Null(Shutoff.Answer(() => asked = true));
            Assert.True(asked);
        }

        [Fact]
        public void WhileOnTheSelfTestIsNotAskedOnSparringsBehalf()
        {
            var asked = false;
            Shutoff.Answer(() => asked = true);
            Assert.False(asked);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void AFailureNamingNothingStillSwitchesSparringOffAndSaysSo(string what)
        {
            Shutoff.Engage(what);

            Assert.True(Shutoff.Engaged);
            var why = Shutoff.Message;
            Assert.Contains("Sparring is off", why, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", why, StringComparison.Ordinal);
        }

        [Fact]
        public void TheMessagesPreviewShowsTheOffLineToo()
        {
            Assert.Contains(Messages.All("Eyvind"), line => line.Contains("Sparring is off", StringComparison.Ordinal));
        }
    }
}
