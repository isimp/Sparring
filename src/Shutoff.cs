using System;

namespace Sparring
{
    /// <summary>
    /// Sparring switched off as a whole, because the game would not take one of its hooks.
    ///
    /// A duel's protection is every hook at once: blows let through between two players with no
    /// yield to catch the last one would make a duel a real fight. So when patching fails,
    /// <c>Plugin.Awake</c> takes out the hooks that did go in and switches Sparring off here, and
    /// everything a duel runs from checks this first: <c>Update</c> (the lease, the messages
    /// between two games, the keys), <c>OnGUI</c> (the panel) and /duel. With no lease written, no
    /// other player's game can start a duel with this one either. /duel stays registered so it can
    /// say why, and the self-test still runs its checks that only read. Nothing here needs the
    /// game, so the unit tests hold these rules as they are.
    /// </summary>
    internal static class Shutoff
    {
        /// <summary>Said for a failure that names no hook, such as one the game refused to take rather than one it no longer has.</summary>
        private const string Unnamed = "one of the game's methods";

        /// <summary>What the game would not take Sparring's hook into, or null while Sparring is on.</summary>
        internal static string What { get; private set; }

        /// <summary>Whether Sparring is off.</summary>
        internal static bool Engaged => What != null;

        /// <summary>What /duel says while Sparring is off, naming what the game changed; null while it is on.</summary>
        internal static string Message => Engaged ? Messages.Named(Messages.Off, What) : null;

        /// <summary>Switches Sparring off, for the hook the game would not take (null or empty when the failure names none).</summary>
        internal static void Engage(string what) => What = string.IsNullOrEmpty(what) ? Unnamed : what;

        /// <summary>
        /// What /duel answers while Sparring is off: nothing for a word the self-test takes, since
        /// it still runs to tell what is missing, and why Sparring is off for every other word.
        /// While Sparring is on, nothing, and the self-test is not asked here.
        /// </summary>
        internal static string Answer(Func<bool> selfTestTook)
        {
            if (!Engaged) return null;
            return selfTestTook() ? null : Message;
        }

        /// <summary>Back on, for the tests: in the game nothing switches Sparring on again until it is next loaded.</summary>
        internal static void Reset() => What = null;
    }
}
