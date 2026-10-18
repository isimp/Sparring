using System.Globalization;

namespace Sparring
{
    /// <summary>
    /// <c>/duel</c> and its subcommands.
    ///
    /// Registered as an ordinary non-cheat console command, which is all it takes to reach it from
    /// chat: <c>Chat.InputText</c> strips the leading slash and hands the rest to
    /// <c>TryRunCommand</c>, and chat's only restriction is that cheat commands are refused. So no
    /// patch of the chat window is needed, and the command also works from the console.
    ///
    /// Accept, decline and yield are keys on the HUD. The same actions also exist as words, in
    /// case a key is taken by another mod.
    /// </summary>
    internal static class Commands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            // The command adds itself to the game's list as it is made, so nothing keeps it.
            _ = new Terminal.ConsoleCommand("duel",
                "Duel the player you are looking at, without either of you dying. " +
                "/duel <metres> sets the ring size; /duel preview [metres], /duel rematch, /duel status.",
                Run, isCheat: false, isNetwork: false, onlyServer: false);
        }

        /// <summary>
        /// What a word after /duel asks for, already in lower case. Several actions answer to more
        /// than one word. A number is a ring size, read the same way on every PC, so "12,5" is no
        /// size and gets the hint rather than a ring the player did not mean. Anything else is
        /// <see cref="Order.Other"/>: the self-test's, when it is installed, or a typo.
        /// </summary>
        internal static Order Read(string word, out float metres)
        {
            metres = 0f;
            switch (word)
            {
                case "": return Order.Challenge;
                case "accept": case "yes": return Order.Accept;
                case "decline": case "no": return Order.Decline;
                case "cancel": case "withdraw": return Order.Withdraw;
                case "yield": case "forfeit": case "surrender": return Order.Yield;
                case "rematch": case "again": return Order.Rematch;
                case "preview": return Order.Preview;
                case "status": return Order.Status;
                case "panels": return Order.Panels;
                case "testyield": return Order.TestYield;
                case "testwin": return Order.TestWin;
                case "testmessages": return Order.TestMessages;
                case "spar": return Order.Spar;
                case "testannounce": return Order.TestAnnounce;
                case "testsound": return Order.TestSound;
            }

            return float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out metres) ? Order.Ring : Order.Other;
        }

        /// <summary>The ring a preview shows: the size typed, held to the ring's bounds, or the default ring when none is.</summary>
        internal static float Size(string typed, float fallback) =>
            typed != null && float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var metres)
                ? Arena.Clamp(metres, fallback)
                : fallback;

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            var word = args.Length > 1 ? args[1].ToLowerInvariant() : "";

            // Switched off, every word says why; only the self-test still answers, to tell what is missing.
            if (Shutoff.Engaged)
            {
                var why = Shutoff.Answer(() => SelfTestHost.Answer(word, args));
                if (why != null) Duel.Say(why);
                return;
            }

            switch (Read(word, out var metres))
            {
                case Order.Challenge:
                    ChallengeWhoeverIsInView(Plugin.ArenaRadius);
                    break;

                case Order.Accept:
                    Duel.Accept();
                    break;

                case Order.Decline:
                    Duel.Decline();
                    break;

                case Order.Withdraw:
                    Duel.Withdraw();
                    break;

                case Order.Yield:
                    Duel.Forfeit();
                    break;

                case Order.Rematch:
                    Duel.Rematch();
                    break;

                case Order.Preview:
                    Preview.ToggleRing(Size(args.Length > 2 ? args[2] : null, Plugin.ArenaRadius));
                    break;

                // Rehearsals. Two of these put health back and strip debuffs, because that is what
                // they are rehearsing — which makes them a free heal on any server that leaves
                // them open. They are shut unless someone deliberately opens them.
                case Order.Panels:
                    if (Debugging()) Preview.ShowPanels();
                    break;

                case Order.TestYield:
                    if (Debugging()) Preview.Yield();
                    break;

                case Order.TestWin:
                    if (Debugging()) Preview.Win();
                    break;

                case Order.TestMessages:
                    if (Debugging()) Preview.ShowMessages();
                    break;

                case Order.Spar:
                    if (Debugging()) Preview.ToggleSparring();
                    break;

                case Order.TestAnnounce:
                    if (Debugging()) Preview.Announce();
                    break;

                case Order.TestSound:
                    if (Debugging()) Sound.Challenge();
                    break;

                case Order.Status:
                    Status();
                    break;

                case Order.Ring:
                    ChallengeWhoeverIsInView(Arena.Clamp(metres, Plugin.ArenaRadius));
                    break;

                default:
                    // The self-test's words, when it is installed beside a development build.
                    // Anything else is a typo, and saying so beats silently starting a duel with a
                    // size the person did not mean.
                    if (!SelfTestHost.Answer(word, args))
                        Duel.Say("Use /duel, or /duel <metres> to set the ring size. /duel preview shows one where you stand.");
                    break;
            }
        }

        /// <summary>
        /// Whether the rehearsal commands are open. Off unless asked for, and a rule rather than a
        /// preference: <c>testwin</c> hands out health and clears what a fight left on you, so
        /// whether it is available is the server's business, not each player's.
        /// </summary>
        private static bool Debugging()
        {
            if (Plugin.DebugCommands) return true;

            Duel.Say("Turn on DebugCommands in the config to use the rehearsal commands.");
            return false;
        }

        private static void ChallengeWhoeverIsInView(float radius)
        {
            // Someone typing /duel with a challenge already waiting almost certainly means to take
            // it, not to open a second one.
            if (Duel.HasInvite)
            {
                Duel.Say($"You have a challenge waiting — press [{KeyLabels.Of(Plugin.AcceptKey)}] to accept, [{KeyLabels.Of(Plugin.DeclineKey)}] to decline.");
                return;
            }

            var target = LookTarget.InView(out var why);
            if (target == null)
            {
                Duel.Say(why);
                return;
            }

            Duel.Challenge(target, radius);
        }

        private static void Status()
        {
            if (Duel.Active)
            {
                var ring = UnityEngine.Mathf.RoundToInt(Duel.Terms.Radius);
                Duel.Say(Duel.CountingDown
                    ? $"About to duel {Duel.OpponentName}."
                    : $"Dueling {Duel.OpponentName}, {ring}m ring.");
            }
            else if (Duel.HasInvite) Duel.Say($"{Duel.InviteFrom} is waiting on your answer.");
            else if (Duel.HasOutgoing) Duel.Say($"Your challenge to {Duel.AwaitingFrom} is out.");
            else if (Plugin.RulesFromServer) Duel.Say("Not dueling. The duel rules here are the server's.");
            else Duel.Say("Not dueling.");
        }
    }

    /// <summary>What a word after /duel asks for (<see cref="Commands.Read"/>).</summary>
    internal enum Order
    {
        Challenge,
        Accept,
        Decline,
        Withdraw,
        Yield,
        Rematch,
        Preview,
        Status,
        Panels,
        TestYield,
        TestWin,
        TestMessages,
        Spar,
        TestAnnounce,
        TestSound,

        /// <summary>A number: a challenge with that ring size.</summary>
        Ring,

        /// <summary>Any other word: the self-test's, or a typo.</summary>
        Other,
    }
}
