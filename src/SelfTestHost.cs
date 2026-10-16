namespace Sparring
{
    /// <summary>
    /// Where the in-game self-test plugs in. The self-test is a plugin of its own
    /// (<c>Sparring.SelfTest.dll</c>), deployed with a development build and never packaged with a
    /// release, so its runner and the /duel words it answers all live there. As it loads it
    /// registers here, and a word /duel does not know is offered to it. Without it nothing of the
    /// self-test exists, and those words are typos like any other.
    /// </summary>
    internal static class SelfTestHost
    {
        /// <summary>What the self-test offers Sparring.</summary>
        internal interface IRunner
        {
            /// <summary>Whether the word after /duel is one of the self-test's, acting on it if so.</summary>
            bool Answer(string word, Terminal.ConsoleEventArgs args);
        }

        private static IRunner _runner;

        /// <summary>The self-test registers itself as it loads.</summary>
        internal static void Register(IRunner runner) => _runner = runner;

        internal static bool Answer(string word, Terminal.ConsoleEventArgs args) =>
            _runner != null && _runner.Answer(word, args);
    }
}
