using Xunit;
using Skill = Skills.SkillType;

namespace Sparring.Tests
{
    /// <summary>
    /// The rules Sparring's hooks apply inside the game's own code: a death at zero health on the
    /// player's own game becomes a yield only when the duel says so; a duel teaches fighting skills
    /// at the duel's rate, or not at all, and leaves every other skill alone; a duellist's blow
    /// spares built pieces; and a blow counts on the card only when it took health.
    /// </summary>
    public class HookRuleTests
    {
        // ---- the death check ----

        private static bool Yields(bool isPlayer = true, bool local = true, bool dead = false, float health = 0f, bool survives = true) =>
            HookRules.Yields(isPlayer, local, dead, health, () => survives);

        [Fact]
        public void TheLocalPlayerAtZeroInADuelYields()
        {
            Assert.True(Yields());
        }

        [Fact]
        public void ADeathTheDuelDoesNotCoverStands()
        {
            Assert.False(Yields(survives: false));
        }

        [Fact]
        public void OnlyTheLocalPlayersOwnDeathIsJudged()
        {
            Assert.False(Yields(local: false));
            Assert.False(Yields(isPlayer: false));
        }

        [Fact]
        public void AnyHealthLeftOrADeathAlreadyTakenIsLeftToTheGame()
        {
            Assert.False(Yields(health: 0.5f));
            Assert.False(Yields(dead: true));
        }

        [Fact]
        public void TheDuelIsAskedOnlyAboutAFallToZero()
        {
            var asked = false;
            HookRules.Yields(true, true, false, 12f, () => asked = true);
            HookRules.Yields(false, true, false, 0f, () => asked = true);
            HookRules.Yields(true, false, false, 0f, () => asked = true);
            HookRules.Yields(true, true, true, 0f, () => asked = true);
            Assert.False(asked);
        }

        // ---- what a duel teaches ----

        private static (bool Raised, float Value) Teach(Skill skill, bool local = true, bool sparring = false, bool active = true,
            bool countingDown = false, bool skillGain = true, float rate = 0.5f)
        {
            var value = 2f;
            var raised = HookRules.Teach(local, sparring, active, countingDown, skill, skillGain, rate, ref value);
            return (raised, value);
        }

        [Theory]
        [InlineData((int)Skill.Swords)]
        [InlineData((int)Skill.Knives)]
        [InlineData((int)Skill.Clubs)]
        [InlineData((int)Skill.Polearms)]
        [InlineData((int)Skill.Spears)]
        [InlineData((int)Skill.Blocking)]
        [InlineData((int)Skill.Axes)]
        [InlineData((int)Skill.Bows)]
        [InlineData((int)Skill.ElementalMagic)]
        [InlineData((int)Skill.BloodMagic)]
        [InlineData((int)Skill.Unarmed)]
        [InlineData((int)Skill.Crossbows)]
        [InlineData((int)Skill.Dodge)]
        public void ADuelTeachesWeaponsBlockingAndDodgeAtItsRate(int skill)
        {
            Assert.Equal((true, 1f), Teach((Skill)skill));
        }

        [Theory]
        [InlineData((int)Skill.None)]
        [InlineData((int)Skill.Run)]
        [InlineData((int)Skill.Jump)]
        [InlineData((int)Skill.Sneak)]
        [InlineData((int)Skill.Swim)]
        [InlineData((int)Skill.Fishing)]
        [InlineData((int)Skill.Cooking)]
        [InlineData((int)Skill.Farming)]
        [InlineData((int)Skill.Crafting)]
        [InlineData((int)Skill.Ride)]
        public void EverySkillOtherThanFightingIsLeftAsTheGameTeachesIt(int skill)
        {
            Assert.Equal((true, 2f), Teach((Skill)skill));
            Assert.Equal((true, 2f), Teach((Skill)skill, skillGain: false));
        }

        [Fact]
        public void WithSkillGainOffADuelTeachesNoFightingSkill()
        {
            Assert.False(Teach(Skill.Swords, skillGain: false).Raised);
            Assert.False(Teach(Skill.Dodge, skillGain: false).Raised);
        }

        [Fact]
        public void TheCountdownAndTheTimeOutsideADuelTeachAsUsual()
        {
            Assert.Equal((true, 2f), Teach(Skill.Swords, countingDown: true));
            Assert.Equal((true, 2f), Teach(Skill.Swords, active: false));
            Assert.Equal((true, 2f), Teach(Skill.Swords, active: false, skillGain: false));
        }

        [Fact]
        public void ARehearsalTeachesAsADuelDoes()
        {
            Assert.Equal((true, 1f), Teach(Skill.Swords, sparring: true, active: false));
        }

        [Fact]
        public void OnlyTheLocalPlayersSkillsFollowTheDuel()
        {
            Assert.Equal((true, 2f), Teach(Skill.Swords, local: false));
        }

        // ---- built pieces ----

        [Fact]
        public void ADuellistsBlowSparesBuiltPieces()
        {
            Assert.True(HookRules.SparesBuilding(protect: true, byPlayer: true, rehearsing: false, inDuel: true));
        }

        [Fact]
        public void ARehearsalsBlowSparesBuiltPieces()
        {
            Assert.True(HookRules.SparesBuilding(protect: true, byPlayer: true, rehearsing: true, inDuel: false));
        }

        [Fact]
        public void EveryOtherBlowLandsOnBuiltPiecesAsUsual()
        {
            Assert.False(HookRules.SparesBuilding(protect: true, byPlayer: true, rehearsing: false, inDuel: false));
            Assert.False(HookRules.SparesBuilding(protect: true, byPlayer: false, rehearsing: true, inDuel: true));
        }

        [Fact]
        public void WithProtectionOffEveryBlowLandsOnBuiltPieces()
        {
            Assert.False(HookRules.SparesBuilding(protect: false, byPlayer: true, rehearsing: true, inDuel: true));
        }

        // ---- a blow on the card ----

        [Fact]
        public void ABlowCountsOnTheCardOnlyWhenItTookHealth()
        {
            Assert.True(HookRules.Landed(before: 50f, after: 42f));
            Assert.False(HookRules.Landed(before: 50f, after: 50f));
            Assert.False(HookRules.Landed(before: 50f, after: 51f));
        }

        [Fact]
        public void ABlowOnSomeoneElseIsNotMeasured()
        {
            Assert.False(HookRules.Landed(before: -1f, after: -5f));
        }
    }
}
