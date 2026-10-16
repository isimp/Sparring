using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Result = Sparring.SelfTestReport.Result;

namespace Sparring
{
    /// <summary>
    /// The checks that fight: three duels against yourself, each ending in a different kind of
    /// fall to zero that has to come out as a yield.
    ///
    /// The falls are real. Health reaches zero and the game's own <c>CheckDeath</c> decides,
    /// because that call is the one a duel has to survive, and a test that stopped short of it
    /// would pass while it was broken. The price is that a broken yield here is a real death.
    /// </summary>
    internal sealed partial class SelfTest
    {
        private const string Area = "Duel";

        private enum Finisher
        {
            Blow,
            Burn,
            Poison,
        }

        /// <summary>Whether the last practice duel reached the end of its countdown.</summary>
        private bool _ready;

        /// <summary>Whether the player came through the last fall on their feet.</summary>
        private bool _standing;

        private IEnumerator Lifecycle(List<SelfTestReport.Check> checks)
        {
            var me = Player.m_localPlayer;
            var why = NotReady(me);
            if (why != null)
            {
                checks.Add(new SelfTestReport.Check(Area, "the duels against yourself", Result.Warn, "not run: " + why));
                yield break;
            }

            // The first duel carries the checks that only need saying once: how it begins, a
            // blow on the card, and what is left once it is over.
            foreach (var step in StartPractice(checks, me, first: true)) yield return step;
            if (!_ready) yield break;

            Blow(checks, me);

            foreach (var step in KnockDown(checks, me, Finisher.Blow)) yield return step;
            if (!_standing) yield break;

            Aftermath(checks, me);

            foreach (var step in StartPractice(checks, me, first: false)) yield return step;
            if (!_ready) yield break;

            foreach (var step in KnockDown(checks, me, Finisher.Burn)) yield return step;
            if (!_standing) yield break;

            foreach (var step in StartPractice(checks, me, first: false)) yield return step;
            if (!_ready) yield break;

            foreach (var step in KnockDown(checks, me, Finisher.Poison)) yield return step;
        }

        /// <summary>Why the duels cannot be fought here, or null when they can.</summary>
        private static string NotReady(Player me)
        {
            if (me == null) return "no character";
            if (Duel.Active || Duel.HasInvite || Duel.HasOutgoing) return "a duel or a challenge is already under way";
            if (me.IsDead() || me.InCutscene() || me.IsTeleporting()) return "you are not free to fight";
            if (me.IsAttached() || me.InBed() || me.IsSleeping()) return "you are sitting, lying or riding";

            // Creatures are not kept off a duel, and one joining in would turn a check into a
            // real death that has nothing to do with Sparring.
            if (BaseAI.HaveEnemyInRange(me, me.transform.position, 30f)) return "something hostile is within 30 m";

            return null;
        }

        /// <summary>Starts a duel against yourself and waits out its countdown.</summary>
        private IEnumerable StartPractice(List<SelfTestReport.Check> checks, Player me, bool first)
        {
            _ready = false;
            Duel.Practice();

            var began = Duel.Active && Duel.Practising;
            if (first)
            {
                Add(checks, Area, "a duel against yourself begins", began, began ? null : "no duel began");
                if (!began) yield break;

                Add(checks, Area, "its lease agrees with itself",
                    Lease.Corroborated(me, out var paired) && paired == me.GetZDOID(), null);
                Add(checks, Area, "blows wait for the countdown", Duel.CountingDown, Duel.CountingDown ? null : "the duel began without one");
            }
            else if (!began)
            {
                Add(checks, Area, "a further duel against yourself begins", false, "no duel began");
                yield break;
            }

            var started = Time.realtimeSinceStartup;
            var deadline = started + (float)Plugin.CountdownSeconds + 3f;
            while (Duel.Active && Duel.CountingDown && !Gone(me) && Time.realtimeSinceStartup < deadline) yield return null;

            var waited = Time.realtimeSinceStartup - started;
            if (!Duel.Active || Duel.CountingDown)
            {
                Add(checks, Area, "the countdown ends", false,
                    !Duel.Active ? "the duel ended during it, as " + Duel.LastEnd : "still counting after " + Number(waited) + " s");
                yield break;
            }

            if (first)
            {
                var expected = (float)Plugin.CountdownSeconds;
                Add(checks, Area, "the countdown ends on time", Mathf.Abs(waited - expected) < 1f,
                    Number(waited) + " s for a " + Number(expected) + " s countdown");
            }

            _ready = true;
        }

        /// <summary>A blow small enough to stand through, checked on the card against what the game took.</summary>
        private static void Blow(List<SelfTestReport.Check> checks, Player me)
        {
            const string what = "a blow you take is on the card at the amount the game took off";

            var amount = Mathf.Max(1f, Mathf.Min(10f, me.GetHealth() * 0.3f));
            var health = me.GetHealth();
            var hits = Scorecard.TookHits;
            var total = Scorecard.TookTotal;

            Duel.SelfHit(amount);

            var took = health - me.GetHealth();
            var counted = Scorecard.TookTotal - total;

            if (Scorecard.TookHits != hits + 1)
            {
                Add(checks, Area, what, false, "the card counted " + (Scorecard.TookHits - hits) + " hits for one blow");
                return;
            }

            Add(checks, Area, what, Mathf.Abs(counted - took) < 0.05f, "counted " + Number(counted) + ", the game took " + Number(took));
        }

        /// <summary>Takes you to zero one way and checks that the duel caught it as a yield.</summary>
        private IEnumerable KnockDown(List<SelfTestReport.Check> checks, Player me, Finisher how)
        {
            _standing = false;

            var hits = Scorecard.TookHits;
            var health = me.GetHealth();
            string what;
            float patience;

            switch (how)
            {
                case Finisher.Burn:
                    what = "a burn your opponent set that takes you to zero ends in a yield";
                    patience = 30f;
                    Duel.SelfBurn(Mathf.Min(1000f, health * 4f + 40f));
                    break;

                case Finisher.Poison:
                    what = "poison your opponent put on you that takes you to zero ends in a yield";
                    patience = 45f;
                    Duel.SelfPoison(1000f);
                    break;

                default:
                    what = "a blow from your opponent that takes you to zero ends in a yield";
                    patience = 3f;
                    Duel.SelfHit(health + 50f);
                    break;
            }

            var started = Time.realtimeSinceStartup;
            while (Duel.Active && !Gone(me) && Time.realtimeSinceStartup < started + patience) yield return null;

            if (Gone(me))
            {
                Add(checks, Area, what, false, "it ended in a real death");
                yield break;
            }

            if (Duel.Active)
            {
                Add(checks, Area, what, false, "the duel was still running " + Number(patience) + " s later");
                yield break;
            }

            _standing = true;
            var yielded = Duel.LastEnd == EndReason.Yielded;
            Add(checks, Area, what, yielded, yielded ? "after " + Number(Time.realtimeSinceStartup - started) + " s" : "the duel ended as " + Duel.LastEnd);
            if (how == Finisher.Blow || !yielded) yield break;

            // That it was the tick, and not something else, that took you down; and that the
            // ticks reached the card through the same hook a blow does.
            var tick = how == Finisher.Burn ? HitData.HitType.Burning : HitData.HitType.Poisoned;
            var name = how == Finisher.Burn ? "burn" : "poison";
            var last = Patches.LastHit(me);

            Add(checks, Area, "the finishing hit was a " + name + " tick with nobody named on it",
                last != null && last.m_hitType == tick && !last.HaveAttacker(),
                last == null ? "no last hit recorded" : last.m_hitType + (last.HaveAttacker() ? ", naming someone" : ""));
            Add(checks, Area, "the " + name + " ticks are on the card", Scorecard.TookHits > hits,
                (Scorecard.TookHits - hits) + " counted");
        }

        /// <summary>What a yield leaves behind: health at the floor, the card, and no protection.</summary>
        private static void Aftermath(List<SelfTestReport.Check> checks, Player me)
        {
            var floor = Mathf.Max(1f, me.GetMaxHealth() * Mathf.Clamp01(Plugin.YieldHealth));
            Add(checks, Area, "you are back on your feet at the yield floor", me.GetHealth() >= floor - 0.5f,
                Number(me.GetHealth()) + " of " + Number(me.GetMaxHealth()) + ", floor " + Number(floor));

            var card = Scorecard.Showing && Scorecard.Outcome == DuelOutcome.Lost && Scorecard.TookHits >= 2;
            Add(checks, Area, "the card shows the loss and both blows", card,
                (Scorecard.Showing ? "shown" : "not shown") + ", " + Scorecard.Outcome + ", " + Scorecard.TookHits + " hits for " + Number(Scorecard.TookTotal));

            Add(checks, Area, "nothing protects you once it is over", !Lease.Corroborated(me, out _), null);
            Add(checks, Area, "the practice switches are off again", !Duel.Practising && !Lease.AllowSelfPair, null);
        }

        /// <summary>Whether this character is no longer the one in play: dead, or replaced.</summary>
        private static bool Gone(Player me) => me == null || me.IsDead() || Player.m_localPlayer != me;
    }
}
