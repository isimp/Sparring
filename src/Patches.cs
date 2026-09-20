using System;
using HarmonyLib;

namespace Sparring
{
    /// <summary>
    /// The places the game has to be told about a duel. Each one asks <see cref="Lease"/> for
    /// the answer rather than keeping its own, so none of them can act on a duel the network no
    /// longer agrees is running.
    /// </summary>
    [HarmonyPatch]
    public static class Patches
    {
        private static AccessTools.FieldRef<Character, HitData> _lastHit;

        /// <summary>
        /// Turns the killing blow into a yield.
        ///
        /// <c>CheckDeath</c> is the single gate every death passes through: it sets
        /// <c>m_isDead</c> and calls <c>OnDeath</c>, and everything that costs you something — the
        /// tombstone, the skill loss, the corpse run — lives past that call. Stopping here means
        /// none of it happens, rather than happening and being undone.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), "CheckDeath")]
        public static bool CheckDeathPrefix(Character __instance)
        {
            try
            {
                if (!(__instance is Player player)) return true;
                if (player != Player.m_localPlayer) return true;
                if (player.IsDead() || player.GetHealth() > 0f) return true;
                if (!Duel.ShouldSurvive(player)) return true;

                Duel.YieldTo(player);
                return false;
            }
            catch (Exception ex)
            {
                // Never swallow a death by accident. If we cannot tell, the game's own rules apply.
                Plugin.Log.LogError($"Sparring failed deciding a death, letting it stand: {ex}");
                return true;
            }
        }

        /// <summary>
        /// Notes the health we had before a blow is worked out, so the finalizer below can tell
        /// whether one actually landed. Only our own skin is measured; every other character in
        /// the world leaves on the first test, which matters because this runs for all of them.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        public static void ApplyDamagePrefix(Character __instance, ref float __state)
        {
            __state = -1f;

            try
            {
                if (__instance != null && __instance == Player.m_localPlayer) __state = __instance.GetHealth();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not read health before a hit: {ex.Message}");
            }
        }

        /// <summary>
        /// Where the scorecard hears about a blow.
        ///
        /// <c>ApplyDamage</c> finishes by invoking <c>Character.m_onDamaged</c> with the final
        /// amount, which is the obvious thing to listen to. It is also the very last statement of a
        /// long method, and several of the calls before it are ones other mods patch. Anything that
        /// throws in between takes the callback with it, and a tally that hears nothing records
        /// nothing and says so by showing no card at all — a silent failure with no way to tell it
        /// apart from a duel in which nobody landed a hit.
        ///
        /// A finalizer runs however the method ended, so the blow is still heard. The exception is
        /// left exactly as it was: this only listens, and a fault that belongs to somebody else
        /// stays theirs to fix.
        ///
        /// The amount counted is the game's own. By this point the hit has been scaled by the local
        /// damage rate, so the HitData carries the figure that was applied, and nothing here has to
        /// repeat the calculation. Health before and after is the proof that it landed: a hit the
        /// method refused, or one an exception cut short before the health was written, changed
        /// nothing and is counted as nothing.
        /// </summary>
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        public static void ApplyDamageFinalizer(Character __instance, HitData hit, float __state)
        {
            try
            {
                if (__state < 0f || hit == null) return;
                if (__instance == null || __instance != Player.m_localPlayer) return;
                if (__instance.GetHealth() >= __state) return;

                Scorecard.Count(hit.GetTotalDamage(), hit.GetAttacker());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not tally a hit: {ex.Message}");
            }
        }

        /// <summary>
        /// Lets duel swings through without either fighter turning vanilla PvP on.
        ///
        /// A player's attack filters its targets at <c>Attack.cs:1139</c> and skips other players
        /// outright unless PvP is on or <c>IsEnemy</c> says otherwise — so the hit is never built,
        /// and stamping the HitData alone would achieve nothing. Naming just the two duelists as
        /// enemies opens that filter for them and for nobody else: everyone keeps PvP off, so every
        /// other pairing in the world behaves exactly as it did.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
        public static void IsEnemyPostfix(Character a, Character b, ref bool __result)
        {
            if (__result) return;

            // Kept to a pair of players before anything reads a ZDO. Creature AI calls this on
            // every candidate every tick, and those all leave on the first test.
            if (!(a is Player) || !(b is Player)) return;

            try
            {
                // AreFighting, not ArePaired: during the countdown the two are paired but blows do
                // not count yet, and this is the gate that decides whether a swing is even built.
                if (Lease.AreFighting(a, b)) __result = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not check a duel pairing: {ex.Message}");
            }
        }

        /// <summary>
        /// The victim's half of the same problem. <c>RPC_Damage</c> drops player-on-player damage
        /// at <c>Character.cs:2265</c> unless the victim has PvP on or the hit is marked. The mark
        /// is a real serialized field, so setting it here — on the attacker, before the RPC goes
        /// out — survives the trip.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static void DamagePrefix(Character __instance, HitData hit)
        {
            if (hit == null || !(__instance is Player)) return;

            try
            {
                var attacker = hit.GetAttacker();
                if (!(attacker is Player)) return;

                if (Lease.AreFighting(attacker, __instance)) hit.m_ignorePVP = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not mark a duel hit: {ex.Message}");
            }
        }

        /// <summary>
        /// Refuses hits that claim to bypass PvP from anyone you have not agreed to fight.
        ///
        /// <c>HitData.m_ignorePVP</c> makes <c>RPC_Damage</c> skip the PvP check entirely, and the
        /// victim honours whatever the attacker sent. Nothing in the vanilla game ever sets that
        /// flag — the only assignment anywhere in the assembly is deserialisation — so a hit that
        /// arrives with it set did not come from an unmodified client. That is a hole in the base
        /// game, open with or without this mod.
        ///
        /// Sparring is the flag's legitimate user, which puts it in a position to shut the rest.
        /// The test is not "does the network say we are dueling" but "does *this* client believe it
        /// agreed to a duel with this person" — a forged claim on the wire cannot satisfy that,
        /// because consent was never given here. The flag is cleared rather than the hit dropped,
        /// so what remains is ordinary PvP: if you had it on, the blow lands as it always would.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        public static void RpcDamagePrefix(Character __instance, HitData hit)
        {
            try
            {
                if (hit == null || !hit.m_ignorePVP) return;
                if (!Plugin.RefusePvpBypass) return;

                // Only ever our own skin. Other characters are not ours to make rulings about.
                if (__instance != Player.m_localPlayer) return;

                var attacker = hit.GetAttacker();
                if (!(attacker is Player)) return;

                // Both halves, and they guard different attacks. Local consent cannot be forged
                // over the network — Duel.Active is a field this client sets during its own
                // handshake, not anything read back off a ZDO, and ZDOs are rewritable by any peer
                // (ZDOMan.RPC_ZDOData accepts a higher data revision from anyone, with no check
                // that the sender owns what they are overwriting). AreFighting cannot be forged by
                // a confused local state, and it is false during the countdown — a window where no
                // duel blow should land anyway.
                var consented = Duel.Active && Duel.Opponent == attacker.GetZDOID();
                if (consented && Lease.AreFighting(attacker, __instance)) return;

                hit.m_ignorePVP = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not vet a PvP-bypassing hit: {ex.Message}");
            }
        }

        /// <summary>
        /// Keeps a duel from wrecking the place it is fought in.
        ///
        /// A swung axe does not care what it lands on, so an arena with walls, or somebody's hall,
        /// pays for every miss. <c>WearNTear.Damage</c> is where a blow against a built piece
        /// begins, and like <c>Character.Damage</c> it runs on the attacker's own machine and then
        /// forwards by RPC — so refusing here means the damage is never sent, rather than sent and
        /// then argued with.
        ///
        /// Read off the lease rather than the local duel, so it holds whichever of the two fighters
        /// is swinging. A forged claim would buy someone nothing but an inability to break things.
        ///
        /// Only built pieces. Trees, rocks and terrain are not covered — a duel in a forest will
        /// still take the forest down with it.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
        public static bool WearNTearDamagePrefix(HitData hit)
        {
            try
            {
                if (!Plugin.ProtectBuildings || hit == null) return true;

                var attacker = hit.GetAttacker();
                if (!(attacker is Player)) return true;

                if (Preview.Rehearsing(attacker)) return false;
                return !Lease.Corroborated(attacker, out _);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not spare a building: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Scales — or withholds — the skill a duel teaches.
        ///
        /// Duelling trains skills without any help from this mod: a landed blow raises the weapon
        /// skill at <c>Attack.cs:1074</c>, a block raises Blocking inside <c>BlockAttack</c>, and a
        /// perfect dodge is credited at <c>Attack.cs:1151</c>, just past the target filter the
        /// IsEnemy patch opens. Since nobody dies and both sides are healed, how much a duel
        /// teaches is left to the server.
        ///
        /// Only fighting skills are touched. The skill enum numbers weapons and tools 1 to 14 and
        /// everything else from 100 up, so that range plus Dodge covers fighting, and other skills
        /// such as Run are unaffected.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), nameof(Player.RaiseSkill))]
        public static bool RaiseSkillPrefix(Player __instance, Skills.SkillType skill, ref float value)
        {
            try
            {
                if (__instance != Player.m_localPlayer) return true;

                // Not while counting down: no blow lands then, so nothing should be learned either.
                var fighting = Preview.Sparring || (Duel.Active && !Duel.CountingDown);
                if (!fighting) return true;
                if (!TaughtByFighting(skill)) return true;

                if (!Plugin.SkillGain) return false;

                value *= Plugin.SkillGainRate;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not weigh a skill gain: {ex.Message}");
                return true;
            }
        }

        private static bool TaughtByFighting(Skills.SkillType skill)
        {
            if (skill == Skills.SkillType.Dodge) return true;

            var id = (int)skill;
            return id >= (int)Skills.SkillType.Swords && id <= (int)Skills.SkillType.Crossbows;
        }

        /// <summary>
        /// Dying to something that was not your opponent ends the duel. The lease would lapse on
        /// its own a few seconds later; this just makes the message honest and immediate.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        public static void OnDeathPostfix(Player __instance)
        {
            try
            {
                if (__instance != Player.m_localPlayer || !Duel.Active) return;
                Duel.DiedOutside();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not close out a duel on death: {ex.Message}");
            }
        }

        /// <summary>
        /// Who landed the last hit on someone. <c>Character.m_lastHit</c> is protected, and it is
        /// the only record of what brought a player to zero by the time <c>CheckDeath</c> runs.
        /// </summary>
        public static Character LastAttacker(Character victim)
        {
            if (victim == null) return null;

            try
            {
                if (_lastHit == null) _lastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
                return _lastHit(victim)?.GetAttacker();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not read the last hit: {ex.Message}");
                return null;
            }
        }
    }
}
