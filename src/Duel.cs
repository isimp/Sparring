using System;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// The local player's duel in the game: one <see cref="DuelFlow{TId}"/> run against the game
    /// itself (<see cref="GameWorld"/>), and the way the rest of Sparring reaches it. The handshake,
    /// the countdown, the renewal and every way a duel ends live in the flow, where the unit tests
    /// play them through two simulated players; what is left here needs the game: the network
    /// messages being registered, reading the last hit, and the practice tools that land real
    /// blows.
    /// </summary>
    internal static class Duel
    {
        private static readonly DuelFlow<ZDOID> _flow = new DuelFlow<ZDOID>(new GameWorld());

        public static bool Active => _flow.Active;
        public static ZDOID Opponent => _flow.Opponent;

        /// <summary>
        /// Whether this duel is the solo one. Read by the tally, which has no opponent to
        /// attribute blows to and so counts whatever lands.
        /// </summary>
        public static bool Practising => _flow.Practising;

        /// <summary>How the last duel on this machine ended, so an ending can be told apart afterwards.</summary>
        internal static EndReason? LastEnd => _flow.LastEnd;

        public static string OpponentName => _flow.OpponentName;
        public static bool HasInvite => _flow.HasInvite;
        public static bool HasOutgoing => _flow.HasOutgoing;
        public static string InviteFrom => _flow.InviteFrom;
        public static string AwaitingFrom => _flow.AwaitingFrom;
        public static Lease.Terms Terms => _flow.Terms;

        /// <summary>Seconds left on whichever invitation is on screen, for the HUD to count down.</summary>
        public static double InviteSecondsLeft => _flow.InviteSecondsLeft;

        /// <summary>True between the handshake and the first blow counting.</summary>
        public static bool CountingDown => _flow.CountingDown;

        /// <summary>The moment the count runs out, held just long enough to read.</summary>
        public static bool ShowingStart => _flow.ShowingStart;

        public static void Tick()
        {
            Link.EnsureRegistered();
            _flow.Tick();
        }

        public static void Challenge(Player target, float radius)
        {
            if (target == null) return;
            _flow.Challenge(target.GetZDOID(), radius);
        }

        public static void ReceiveChallenge(ZDOID challenger, string name, float radius) => _flow.ReceiveChallenge(challenger, name, radius);

        /// <summary>Whether this player is free to start a duel, and if not, why; see <see cref="Challenges.Readiness"/>.</summary>
        internal static Busy Readiness(float radius) => _flow.Readiness(radius);

        public static void Accept() => _flow.Accept();
        public static void Decline() => _flow.Decline();
        public static void Withdraw() => _flow.Withdraw();
        public static void ReceiveReply(ZDOID responder, bool accepted, Lease.Terms terms) => _flow.ReceiveReply(responder, accepted, terms);
        public static void ReceiveBusy(ZDOID from, Busy why) => _flow.ReceiveBusy(from, why);

        /// <summary>Giving up on purpose, with the duel still live.</summary>
        public static void Forfeit() => _flow.Forfeit();

        /// <summary>Dropped to nothing at the opponent's hands. Called from the CheckDeath patch, before the game has marked anyone dead.</summary>
        public static void YieldTo() => _flow.YieldTo();

        /// <summary>Killed by something that was not your opponent.</summary>
        public static void DiedOutside() => _flow.DiedOutside();

        public static void ReceiveTally(ZDOID from, float took, int hits, float biggest) => _flow.ReceiveTally(from, took, hits, biggest);
        public static void ReceiveResult(ZDOID from, EndReason reason) => _flow.ReceiveResult(from, reason);
        public static void EndLocal(EndReason reason, bool notify) => _flow.EndLocal(reason, notify);

        /// <summary>Re-challenge whoever you last fought, if they are still around.</summary>
        public static void Rematch() => _flow.Rematch();

        /// <summary>A duel against yourself, for testing the parts that do not need a second player; see <see cref="DuelFlow{TId}.Practice"/>.</summary>
        internal static void Practice() => _flow.Practice();

        public static void Reset() => _flow.Reset();

        /// <summary>Whether a hit carrying no attacker should be read as the opponent's; see <see cref="Survival.OpponentTick"/>.</summary>
        public static bool OpponentTick(HitData hit) => hit != null && Survival.OpponentTick(hit.HaveAttacker(), hit.m_hitType);

        /// <summary>
        /// Whether a killing blow should become a yield instead; see <see cref="Survival.ShouldSurvive{TId}"/>.
        /// The pair is read off the leases as they stand, and the blow off the game's last hit.
        /// </summary>
        public static bool ShouldSurvive(Player me) =>
            me != null && _flow.ShouldSurvive(me == Player.m_localPlayer, Survival.BlowOf(Patches.LastHit(me)));

        /// <summary>
        /// Lands a real blow on yourself, so a practice card can be filled without waiting for
        /// something to wander past and take an interest.
        ///
        /// It goes through <c>ApplyDamage</c>, which is where the game subtracts the health, so
        /// what lands is a genuine hit rather than a number written into the card. Armour and
        /// resistances are not applied — those happen in <c>Damage</c>, further up — so the amount
        /// asked for is close to the amount that lands.
        ///
        /// A lethal one is recorded as coming from the opponent, which in a practice duel is this
        /// player, so it takes the route a fatal blow takes in a real duel and ends in a yield
        /// rather than a death.
        /// </summary>
        internal static void SelfHit(float amount)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            if (!Practising) { Say("Only during a practice duel. Start one with /duel practice."); return; }

            var hit = new HitData();
            hit.m_damage.m_blunt = Mathf.Clamp(amount, 1f, 1000f);
            hit.m_point = me.GetCenterPoint();
            hit.m_dir = me.transform.forward;
            hit.SetAttacker(me);

            // The damage path is one other mods patch heavily, and a throw from any of them would
            // otherwise leave the chat window stuck on the command that caused it.
            Plugin.Log.LogInfo(FormattableString.Invariant($"Sparring practice: hit for {hit.GetTotalDamage():0.#}."));

            try
            {
                me.ApplyDamage(hit, showDamageText: true, triggerEffects: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Sparring took a hit but something in the damage path threw: {ex}");
            }
        }

        /// <summary>
        /// Sets this player alight, the way an opponent's fire arrow does.
        ///
        /// Not a hit: it hands the damage to the game's own burning effect, which is what turns it
        /// into ticks. Those ticks are built by the effect with nobody named on them, so this is
        /// the real article rather than an imitation of one — the same thing that has to be read as
        /// the opponent's doing for a duel to survive it.
        ///
        /// Take enough damage first and the burn will be what finishes you, which is the case worth
        /// watching: it should end in a yield.
        /// </summary>
        internal static void SelfBurn(float amount)
        {
            var seman = Kindling(out var me);
            if (seman == null) return;

            var burning = seman.GetStatusEffect(SEMan.s_statusEffectBurning) as SE_Burning;
            if (burning == null) burning = seman.AddStatusEffect(SEMan.s_statusEffectBurning, false, 0, 0f, -1) as SE_Burning;

            if (burning == null) { Say("The burning effect is not available here."); return; }

            if (!burning.AddFireDamage(Mathf.Clamp(amount, 1f, 1000f)))
            {
                // Too little to spread over the effect's lifetime, which the game answers by
                // dropping the effect rather than leaving a burn that does nothing.
                seman.RemoveStatusEffect(burning, quiet: true);
                Say("Not enough to catch. Try a larger number.");
                return;
            }

            Say($"Alight for {Mathf.RoundToInt(amount)} over time.");
            Plugin.Log.LogInfo(FormattableString.Invariant($"Sparring practice: alight for {amount:0.#}."));
        }

        /// <summary>The same for poison, which the game carries in its own effect.</summary>
        internal static void SelfPoison(float amount)
        {
            var seman = Kindling(out var me);
            if (seman == null) return;

            var poison = seman.GetStatusEffect(SEMan.s_statusEffectPoison) as SE_Poison;
            if (poison == null) poison = seman.AddStatusEffect(SEMan.s_statusEffectPoison, false, 0, 0f, -1) as SE_Poison;

            if (poison == null) { Say("The poison effect is not available here."); return; }

            // Only a dose larger than whatever is already running takes hold.
            poison.AddDamage(Mathf.Clamp(amount, 1f, 1000f));
            Say($"Poisoned for {Mathf.RoundToInt(amount)} over time.");
            Plugin.Log.LogInfo(FormattableString.Invariant($"Sparring practice: poisoned for {amount:0.#}."));
        }

        /// <summary>The checks both of the above share, and the effect manager they need.</summary>
        private static SEMan Kindling(out Player me)
        {
            me = Player.m_localPlayer;
            if (me == null) return null;

            if (!Practising) { Say("Only during a practice duel. Start one with /duel practice."); return null; }

            return me.GetSEMan();
        }


        internal static void Say(string text, MessageHud.MessageType type = MessageHud.MessageType.Center)
        {
            var me = Player.m_localPlayer;
            if (me != null) me.Message(type, text);
        }
    }
}
