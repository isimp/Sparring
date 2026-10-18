using Xunit;

namespace Sparring.Tests
{
    /// <summary>
    /// The settings' rules that hold whatever is typed: a ring is five to sixty metres whatever size
    /// is asked for or arrives from the other side, no size means the default ring, and the extra
    /// effects to clear are names separated by commas, matched whatever their case.
    /// </summary>
    public class SettingTests
    {
        [Theory]
        [InlineData(2f, 5f)]
        [InlineData(5f, 5f)]
        [InlineData(30f, 30f)]
        [InlineData(60f, 60f)]
        [InlineData(100f, 60f)]
        [InlineData(float.PositiveInfinity, 60f)]
        public void ARingIsHeldToFiveToSixtyMetres(float asked, float ring)
        {
            Assert.Equal(ring, Arena.Clamp(asked, fallback: 25f));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-3f)]
        [InlineData(float.NaN)]
        [InlineData(float.NegativeInfinity)]
        public void NoRingSizeMeansTheDefaultRing(float asked)
        {
            Assert.Equal(25f, Arena.Clamp(asked, fallback: 25f));
        }

        [Fact]
        public void TheExtraEffectsToClearAreNamesSeparatedByCommas()
        {
            var names = Recovery.NameSet(" SE_Foo , ,Bar,");

            Assert.Equal(2, names.Count);
            Assert.Contains("SE_Foo", names);
            Assert.Contains("Bar", names);
        }

        [Fact]
        public void AnExtraEffectMatchesByEitherNameWhateverItsCase()
        {
            var names = Recovery.NameSet("se_foo,$se_bar_name");

            Assert.True(Recovery.Listed(names, "SE_Foo", "$se_other"));
            Assert.True(Recovery.Listed(names, "SE_Other", "$SE_Bar_Name"));
            Assert.False(Recovery.Listed(names, "SE_Other", "$se_other"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" , ,")]
        public void NoExtraEffectsNamedClearsNothingExtra(string typed)
        {
            var names = Recovery.NameSet(typed);

            Assert.Empty(names);
            Assert.False(Recovery.Listed(names, "SE_Foo", "$se_foo"));
            Assert.False(Recovery.Listed(names, "", ""));
        }
    }
}
