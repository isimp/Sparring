namespace Sparring
{
    /// <summary>
    /// Every line the mod says to you in the middle of the screen, in one place.
    ///
    /// These are the moments the panel cannot cover: refusals, and endings — things that happen
    /// when there is no duel on screen to put them on. Gathering them here is what lets
    /// <c>/duel testmessages</c> show you all of them without engineering each situation, and it
    /// means the preview cannot drift from what the game actually says, because it is reading the
    /// same strings the code does rather than a copy of them.
    /// </summary>
    public static class Messages
    {
        // Refusals when starting
        public const string AlreadyDueling = "You are already dueling {0}.";
        public const string NotYourself = "You cannot duel yourself.";
        public const string AlreadyChallenged = "You already have a challenge out to {0}.";
        public const string InCombat = "Too soon after fighting. Wait until you are out of combat.";
        public const string PvpOn = "Turn PvP off before dueling — Sparring handles the hits itself.";
        public const string NobodyInView = "Look at the player you want to duel, then use /duel again.";
        public const string CreaturesAbout = "Something hostile is nearby. Clear the ground first.";
        public const string YouAreBusy = "You cannot start a duel right now.";
        public const string Unreachable = "You cannot reach {0} any more.";
        public const string WaitBeforeAgain = "Wait a moment before challenging {0} again.";
        public const string TooFarAway = "{0} is too far away.";

        // The other player is not free
        public const string TheyBusy = "{0} cannot duel right now.";
        public const string TheyInCombat = "{0} is in combat.";
        public const string TheyPvpOn = "{0} has PvP on.";
        public const string TheyCreaturesAbout = "{0} has something hostile nearby.";
        public const string TheyDueling = "{0} is already in a duel.";
        public const string TheyAnswering = "{0} is answering another challenge.";

        // Refusals when answering
        public const string NoChallenge = "Nobody has challenged you.";
        public const string NoneOut = "You have no challenge out.";
        public const string NotDueling = "You are not dueling.";

        // Invitations ending without a duel
        public const string Unanswered = "Your challenge to {0} went unanswered.";
        public const string YouWalkedOff = "You walked away from your challenge to {0}.";
        public const string TooFarNow = "You are too far from {0} now. The challenge is off.";
        public const string Expired = "{0}'s challenge has expired.";
        public const string YouDeclined = "You decline {0}'s challenge.";
        public const string YouWithdrew = "You withdraw your challenge to {0}.";
        public const string TheyDeclined = "{0} declines your challenge.";
        public const string TheyWithdrew = "{0} withdrew the challenge.";

        // Duels ending
        public const string OutOfRing = "Out of the ring — the duel is off.";
        public const string LostTrack = "Lost track of {0}. The duel is off.";

        // Rematch
        public const string NeverDueled = "You have not dueled anyone yet.";
        public const string NotNearby = "{0} is not nearby.";

        // Version
        public const string VersionGap = "Someone challenged you with a different version of Sparring. One of you needs to update.";

        /// <summary>
        /// Every line above, in the order you would meet them, for the preview to walk through.
        /// A line taking a name gets one so it reads as it really would.
        /// </summary>
        public static string[] All(string name)
        {
            return new[]
            {
                string.Format(AlreadyDueling, name),
                NotYourself,
                string.Format(AlreadyChallenged, name),
                InCombat,
                PvpOn,
                NobodyInView,
                CreaturesAbout,
                YouAreBusy,
                string.Format(Unreachable, name),
                string.Format(WaitBeforeAgain, name),
                string.Format(TooFarAway, name),
                string.Format(TheyBusy, name),
                string.Format(TheyInCombat, name),
                string.Format(TheyPvpOn, name),
                string.Format(TheyCreaturesAbout, name),
                string.Format(TheyDueling, name),
                string.Format(TheyAnswering, name),
                NoChallenge,
                NoneOut,
                NotDueling,
                string.Format(Unanswered, name),
                string.Format(YouWalkedOff, name),
                string.Format(TooFarNow, name),
                string.Format(Expired, name),
                string.Format(YouDeclined, name),
                string.Format(YouWithdrew, name),
                string.Format(TheyDeclined, name),
                string.Format(TheyWithdrew, name),
                OutOfRing,
                string.Format(LostTrack, name),
                NeverDueled,
                string.Format(NotNearby, name),
                VersionGap,
            };
        }
    }
}
