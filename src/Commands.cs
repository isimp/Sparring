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
    public static class Commands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand("duel",
                "Duel the player you are looking at, without either of you dying. " +
                "/duel <metres> sets the ring size; /duel preview [metres], /duel rematch, /duel status.",
                Run, isCheat: false, isNetwork: false, onlyServer: false);
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            var word = args.Length > 1 ? args[1].ToLowerInvariant() : "";

            switch (word)
            {
                case "":
                    ChallengeWhoeverIsInView(Plugin.ArenaRadius);
                    break;

                case "accept":
                case "yes":
                    Duel.Accept();
                    break;

                case "decline":
                case "no":
                    Duel.Decline();
                    break;

                case "cancel":
                case "withdraw":
                    Duel.Withdraw();
                    break;

                case "yield":
                case "forfeit":
                case "surrender":
                    Duel.Forfeit();
                    break;

                case "rematch":
                case "again":
                    Duel.Rematch();
                    break;

                case "preview":
                {
                    var size = args.Length > 2 && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
                        ? Plugin.ClampRadius(m)
                        : Plugin.ArenaRadius;
                    Preview.ToggleRing(size);
                    break;
                }

                // Rehearsals. Two of these put health back and strip debuffs, because that is what
                // they are rehearsing — which makes them a free heal on any server that leaves
                // them open. They are shut unless someone deliberately opens them.
                case "panels":
                    if (Debugging()) Preview.ShowPanels();
                    break;

                case "testyield":
                    if (Debugging()) Preview.Yield();
                    break;

                case "testwin":
                    if (Debugging()) Preview.Win();
                    break;

                case "testmessages":
                    if (Debugging()) Preview.ShowMessages();
                    break;

                case "spar":
                    if (Debugging()) Preview.ToggleSparring();
                    break;

                case "testannounce":
                    if (Debugging()) Preview.Announce();
                    break;

                case "testsound":
                    if (Debugging()) Sound.Challenge();
                    break;

                // Listing and auditioning sounds, for choosing ChallengeSoundPrefab.
                case "sounds":
                    if (Debugging()) Sound.List(args.Length > 2 ? args[2] : "");
                    break;

                case "playsound":
                    if (Debugging()) Sound.Audition(args.Length > 2 ? args[2] : "");
                    break;

                // Prefab names live in the game's asset bundles, so there is no way to know from
                // outside a running game what exists. This is how you find a centre marker you like.
                case "prefabs":
                    if (Debugging()) Preview.ListPrefabs(args.Length > 2 ? args[2] : "");
                    break;

                case "status":
                    Status();
                    break;

                default:
                    // A number is a ring size. Anything else is a typo, and saying so beats
                    // silently starting a duel with a size the person did not mean.
                    if (float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var metres))
                    {
                        ChallengeWhoeverIsInView(Plugin.ClampRadius(metres));
                    }
                    else
                    {
                        Duel.Say("Use /duel, or /duel <metres> to set the ring size. /duel preview shows one where you stand.");
                    }
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
                Duel.Say($"You have a challenge waiting — press [{Plugin.AcceptKey}] to accept, [{Plugin.DeclineKey}] to decline.");
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
}
