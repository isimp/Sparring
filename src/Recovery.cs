using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// Putting a duelist back together when the duel ends: the lingering damage-over-time effects
    /// come off, and whoever yielded gets back on their feet with something to stand on.
    ///
    /// Valheim has no "this is a debuff" flag — <c>StatusEffect.StatusAttribute</c> only covers
    /// cold resistance, impact, sailing and taming — so there is nothing structural to filter on in
    /// general. What there is: the harmful combat effects are their own classes. Matching on those
    /// types clears exactly what a duel can inflict and leaves rested, food and potions alone,
    /// without a list of English names to go stale.
    ///
    /// The limit of that, stated plainly: a mod-added debuff built on its own class is not one of
    /// these types and will not be recognised. <c>AlsoClear</c> exists to name those.
    /// </summary>
    internal static class Recovery
    {
        private static readonly Type[] Harmful =
        {
            typeof(SE_Burning),   // fire, spirit and tar damage all run through this one
            typeof(SE_Poison),
            typeof(SE_Frost),
            typeof(SE_Harpooned),
        };

        private static readonly List<StatusEffect> _scratch = new List<StatusEffect>();

        /// <summary>Clears what the duel inflicted. Run on both sides, however the duel ended.</summary>
        public static void Settle(Player player)
        {
            if (player == null) return;

            var seman = player.GetSEMan();
            if (seman == null) return;

            try
            {
                if (Plugin.ClearEverything)
                {
                    seman.RemoveAllStatusEffects(quiet: true);
                    return;
                }

                _scratch.Clear();
                _scratch.AddRange(seman.GetStatusEffects());

                foreach (var se in _scratch)
                {
                    if (se != null && ShouldClear(se)) seman.RemoveStatusEffect(se, quiet: true);
                }
                _scratch.Clear();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Sparring could not clear status effects: {ex.Message}");
            }
        }

        /// <summary>
        /// Puts a fighter back on their feet at the end, to a floor rather than a fixed amount.
        ///
        /// Both sides get one, and the winner's is the higher of the two. With a floor for the
        /// loser alone, a winner could finish on a sliver while the loser finished at half health,
        /// and throwing a duel you were losing badly would be the rewarded play.
        /// </summary>
        public static void Restore(Player player, float fraction)
        {
            if (player == null) return;

            if (Lifted(player.GetHealth(), player.GetMaxHealth(), fraction, out var health)) player.SetHealth(health);
        }

        /// <summary>
        /// The health a fighter is lifted to: their share of full health, the share held between
        /// none and whole, and at least one point. Never downward: yielding at zero should lift you
        /// to the floor, but winning or forfeiting at full health must not cost you the difference —
        /// the same rule serves every ending, and only some of them involve a player who needs
        /// helping up. False when the fighter already has as much.
        /// </summary>
        internal static bool Lifted(float health, float maxHealth, float fraction, out float lifted)
        {
            var share = fraction < 0f ? 0f : fraction > 1f ? 1f : fraction;
            var floor = maxHealth * share;
            if (1f > floor) floor = 1f;

            lifted = floor;
            return !(health >= floor);
        }

        private static bool ShouldClear(StatusEffect se)
        {
            var type = se.GetType();
            foreach (var harmful in Harmful)
            {
                if (harmful.IsAssignableFrom(type)) return true;
            }

            return Listed(Plugin.AlsoClear, se.name, se.m_name);
        }

        /// <summary>
        /// The extra effects to clear as the AlsoClear setting names them: separated by commas,
        /// spaces around each name ignored, and matched whatever their case.
        /// </summary>
        internal static HashSet<string> NameSet(string typed)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(typed)) return names;

            foreach (var part in typed.Split(','))
            {
                var name = part.Trim();
                if (name.Length > 0) names.Add(name);
            }
            return names;
        }

        /// <summary>Whether an effect is one of the extra ones named, by its object name or the name the game shows.</summary>
        internal static bool Listed(HashSet<string> names, string objectName, string displayName) =>
            names.Count > 0 && (names.Contains(objectName) || names.Contains(displayName));
    }
}
