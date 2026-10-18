using System;

namespace Sparring
{
    /// <summary>
    /// The rules Sparring's hooks apply inside the game's own code (<see cref="Patches"/>), apart
    /// from the game so the unit tests hold them as they are. Each hook reads the game's state and
    /// asks here what to do with it.
    /// </summary>
    internal static class HookRules
    {
        /// <summary>
        /// Whether a death check becomes a yield: only for the local player, at zero health and not
        /// already dead, and only when the duel says this blow is one it covers
        /// (<paramref name="shouldSurvive"/>, which reads the leases and so is asked only then).
        /// </summary>
        internal static bool Yields(bool isPlayer, bool local, bool dead, float health, Func<bool> shouldSurvive)
        {
            if (!isPlayer || !local) return false;
            if (dead || health > 0f) return false;
            return shouldSurvive();
        }

        /// <summary>
        /// Scales — or withholds — the skill a duel teaches, and says whether the game raises it at
        /// all.
        ///
        /// Duelling trains skills without any help from this mod: a landed blow raises the weapon
        /// skill, a block raises Blocking, and a perfect dodge is credited past the target filter
        /// the IsEnemy hook opens. Since nobody dies and both sides are healed, how much a duel
        /// teaches is left to the server: while the local player is fighting a duel whose countdown
        /// is over, or rehearsing one, a fighting skill is raised at the duel's rate, or not at all
        /// with skill gain off. Every other skill, and every skill outside a duel, is left as the
        /// game teaches it.
        /// </summary>
        internal static bool Teach(bool local, bool sparring, bool active, bool countingDown, Skills.SkillType skill, bool skillGain, float rate, ref float value)
        {
            if (!local) return true;

            // Not while counting down: no blow lands then, so nothing should be learned either.
            var fighting = sparring || (active && !countingDown);
            if (!fighting) return true;
            if (!TaughtByFighting(skill)) return true;

            if (!skillGain) return false;

            value *= rate;
            return true;
        }

        /// <summary>
        /// Whether a skill is one fighting teaches. The game numbers weapons and tools 1 to 14 and
        /// everything else from 100 up, so that range plus Dodge covers fighting, and other skills
        /// such as Run are unaffected. WoodCutting sits in the range but only ever comes from
        /// chopping trees, never from a blow on a person, so a tree felled in the ring teaches as
        /// it always does. Pickaxes stays: a pickaxe is a weapon too, and a hit on the opponent
        /// with one raises it.
        /// </summary>
        private static bool TaughtByFighting(Skills.SkillType skill)
        {
            if (skill == Skills.SkillType.Dodge) return true;
            if (skill == Skills.SkillType.WoodCutting) return false;

            var id = (int)skill;
            return id >= (int)Skills.SkillType.Swords && id <= (int)Skills.SkillType.Crossbows;
        }

        /// <summary>
        /// Whether a blow spares a built piece. A swung axe does not care what it lands on, so an
        /// arena with walls, or somebody's hall, would pay for every miss. With protection on, a
        /// player's blow while in a duel the leases agree on, or while rehearsing one, leaves built
        /// pieces alone. A forged claim would buy someone nothing but an inability to break things.
        /// </summary>
        internal static bool SparesBuilding(bool protect, bool byPlayer, bool rehearsing, bool inDuel) =>
            protect && byPlayer && (rehearsing || inDuel);

        /// <summary>
        /// Whether a blow on the local player belongs on the card: health measured before it
        /// (<paramref name="before"/> is below zero for anyone else) and less after. A hit the game
        /// refused, or one an exception cut short before the health was written, changed nothing
        /// and is counted as nothing.
        /// </summary>
        internal static bool Landed(float before, float after) => !(before < 0f) && !(after >= before);
    }
}
