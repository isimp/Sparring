using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Result = Sparring.SelfTestReport.Result;

namespace Sparring
{
    /// <summary>The checks that only read: hooks, the game's members, sounds, the ring and the hit rules.</summary>
    internal sealed partial class SelfTest
    {
        [Flags]
        private enum Kinds
        {
            None = 0,
            Prefix = 1,
            Postfix = 2,
            Finalizer = 4,
        }

        /// <summary>
        /// Whether every hook went in, read from Harmony's own record of what Sparring patched, and
        /// that the one method Sparring has to leave to the game is left alone.
        /// </summary>
        private static void Hooks(List<SelfTestReport.Check> checks)
        {
            var patched = Harmony.GetAllPatchedMethods().Where(m => Owned(m) != Kinds.None).ToList();

            Hook(checks, patched, typeof(Character), "CheckDeath", Kinds.Prefix, "a killing blow from your opponent can become a yield");
            Hook(checks, patched, typeof(Character), nameof(Character.ApplyDamage), Kinds.Prefix | Kinds.Finalizer, "the card hears every blow, even when another mod throws in the damage path");
            Hook(checks, patched, typeof(BaseAI), nameof(BaseAI.IsEnemy), Kinds.Postfix, "a duellist's swing can reach their opponent");
            Hook(checks, patched, typeof(Character), nameof(Character.Damage), Kinds.Prefix, "a duel blow gets past the PvP check");
            Hook(checks, patched, typeof(WearNTear), nameof(WearNTear.Damage), Kinds.Prefix, "buildings can be spared during a duel");
            Hook(checks, patched, typeof(Player), nameof(Player.RaiseSkill), Kinds.Prefix, "skill gain follows the duel's rules");
            Hook(checks, patched, typeof(Player), nameof(Player.OnDeath), Kinds.Postfix, "a death to anything else ends the duel at once");

            var rpc = patched.FirstOrDefault(m => m.DeclaringType == typeof(Character) && m.Name == "RPC_Damage");
            Add(checks, "Hooks", "friendly area effects such as the Staff of Protection's shield reach other players", rpc == null,
                rpc == null ? "Character.RPC_Damage is left to the game" : "Sparring patches Character.RPC_Damage");
        }

        private static void Hook(List<SelfTestReport.Check> checks, List<MethodBase> patched, Type type, string name, Kinds wanted, string what)
        {
            var method = patched.FirstOrDefault(m => m.DeclaringType == type && m.Name == name);
            var owned = method == null ? Kinds.None : Owned(method);
            var where = type.Name + "." + name;

            Add(checks, "Hooks", what, (owned & wanted) == wanted,
                owned == Kinds.None ? where + " carries no hook of Sparring's" : where + ": " + Describe(owned));
        }

        private static Kinds Owned(MethodBase method)
        {
            var info = Harmony.GetPatchInfo(method);
            if (info == null) return Kinds.None;

            var kinds = Kinds.None;
            if (info.Prefixes.Any(p => p.owner == Plugin.Guid)) kinds |= Kinds.Prefix;
            if (info.Postfixes.Any(p => p.owner == Plugin.Guid)) kinds |= Kinds.Postfix;
            if (info.Finalizers.Any(p => p.owner == Plugin.Guid)) kinds |= Kinds.Finalizer;
            return kinds;
        }

        private static string Describe(Kinds kinds)
        {
            var parts = new List<string>();
            if ((kinds & Kinds.Prefix) != 0) parts.Add("prefix");
            if ((kinds & Kinds.Postfix) != 0) parts.Add("postfix");
            if ((kinds & Kinds.Finalizer) != 0) parts.Add("finalizer");
            return string.Join(" and ", parts);
        }

        /// <summary>The fields Sparring reads by name, which a game update could rename without anything failing to load.</summary>
        private static void Members(List<SelfTestReport.Check> checks)
        {
            Member(checks, typeof(Character), "m_lastHit", typeof(HitData), "the blow that brought you down can be read back");
            Member(checks, typeof(Character), "m_nview", typeof(ZNetView), "a character's duel record can be read");
            Member(checks, typeof(Chat), "m_hideTimer", typeof(float), "an announcement brings the chat window up");
        }

        private static void Member(List<SelfTestReport.Check> checks, Type type, string name, Type expected, string what)
        {
            var field = AccessTools.Field(type, name);
            var where = type.Name + "." + name;

            if (field == null) Add(checks, "Game", what, false, where + " no longer exists");
            else Add(checks, "Game", what, field.FieldType == expected, where + " is " + field.FieldType.Name);
        }

        private static void Sounds(List<SelfTestReport.Check> checks)
        {
            if (!Plugin.DuelSounds)
            {
                checks.Add(new SelfTestReport.Check("Sounds", "the duel's own sounds", Result.Info, "DuelSounds is off"));
            }
            else
            {
                foreach (var cue in Sound.Cues)
                {
                    var source = Sound.Source(cue.Value);
                    Add(checks, "Sounds", "a sound for " + Moment(cue.Key), source != null,
                        source != null ? cue.Value + ", " + source : cue.Value + " was not found, so that moment is silent", Result.Warn);
                }
            }

            if (!Plugin.ChallengeSoundOn)
            {
                checks.Add(new SelfTestReport.Check("Sounds", "a sound for a challenge", Result.Info, "ChallengeSound is off"));
            }
            else
            {
                var named = Plugin.ChallengeSound;
                var source = Sound.Source(named);
                Add(checks, "Sounds", "a sound for a challenge", source != null,
                    source != null ? named + ", " + source : named + " was not found, so the skill-up chime plays instead", Result.Warn);
            }

            // After every lookup above, so it reports what finding them all cost.
            checks.Add(new SelfTestReport.Check("Sounds", "the walk through the game's effect lists", Result.Info,
                Sound.Indexed == 0
                    ? "not needed: every sound was registered with the scene"
                    : Sound.Indexed + " effect prefabs met in " + Sound.IndexMilliseconds + " ms, "
                      + (Sound.WalkedAll ? "through the whole game" : "stopped once every sound was found")));
        }

        private static string Moment(Sound.Cue cue)
        {
            switch (cue)
            {
                case Sound.Cue.CountdownTick: return "each second of the countdown";
                case Sound.Cue.FightBegins: return "the start of the fight";
                case Sound.Cue.Accepted: return "an accepted challenge";
                case Sound.Cue.KnockedDown: return "being knocked down";
                case Sound.Cue.CameToNothing: return "a challenge or a duel that comes to nothing";
                default: return cue.ToString();
            }
        }

        private static void Ring(List<SelfTestReport.Check> checks)
        {
            var paint = ArenaRing.Paint();
            Add(checks, "Ring", "the ring has a shader to be drawn with", paint != null,
                paint != null ? paint.shader.name : "none of the shaders it tries exist, so no ring is drawn", Result.Warn);

            Marker(checks, "the posts around the edge", ArenaRing.Stake(), Plugin.RingMarker, Plugin.RingStyle == RingStyle.Stakes, "a plain line is drawn instead");
            Marker(checks, "the marker in the middle", ArenaRing.Post(), Plugin.CentreMarker, true, "a plain beam is drawn instead");
        }

        private static void Marker(List<SelfTestReport.Check> checks, string what, GameObject found, string named, bool used, string otherwise)
        {
            if (!used) checks.Add(new SelfTestReport.Check("Ring", what, Result.Info, "RingStyle is Line"));
            else if (found == null) checks.Add(new SelfTestReport.Check("Ring", what, Result.Warn, "nothing resolved, not even the fallbacks; " + otherwise));
            else if (found.name != named) checks.Add(new SelfTestReport.Check("Ring", what, Result.Warn, "\"" + named + "\" did not resolve; using \"" + found.name + "\""));
            else checks.Add(new SelfTestReport.Check("Ring", what, Result.Pass, named));
        }

        /// <summary>
        /// How a hit with nobody named on it is judged. The duel checks below prove the two ticks
        /// through a real fall; these cover the hits that must not be read as the opponent's,
        /// which cannot be proved that way without ending in a real death.
        /// </summary>
        private static void Rules(List<SelfTestReport.Check> checks)
        {
            Judged(checks, "a burn tick with nobody named on it is the opponent's", HitData.HitType.Burning, null, true);
            Judged(checks, "a poison tick with nobody named on it is the opponent's", HitData.HitType.Poisoned, null, true);
            Judged(checks, "a fall is nobody's", HitData.HitType.Fall, null, false);
            Judged(checks, "drowning is nobody's", HitData.HitType.Drowning, null, false);
            Judged(checks, "smoke is nobody's", HitData.HitType.Smoke, null, false);
            Judged(checks, "a burn with somebody named on it is judged by who it names", HitData.HitType.Burning, Player.m_localPlayer, false);
        }

        private static void Judged(List<SelfTestReport.Check> checks, string what, HitData.HitType type, Character attacker, bool expected)
        {
            var hit = new HitData { m_hitType = type };
            if (attacker != null) hit.SetAttacker(attacker);

            var judged = Duel.OpponentTick(hit);
            Add(checks, "Rules", what, judged == expected, judged ? "read as the opponent's" : "not read as the opponent's");
        }
    }
}
