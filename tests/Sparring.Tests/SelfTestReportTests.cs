using System.Collections.Generic;
using Xunit;
using Result = Sparring.SelfTestReport.Result;

namespace Sparring.Tests
{
    /// <summary>
    /// The self-test's report says how it went on its top line, then lists what failed, what needs a
    /// look, what passed and what is only information, each in the order checked. A run that checked
    /// nothing never reads as success.
    /// </summary>
    public class SelfTestReportTests
    {
        private static SelfTestReport.Check C(string what, Result result, string detail = "") => new SelfTestReport.Check("Area", what, result, detail);

        [Fact]
        public void ARunThatCheckedNothingDoesNotReadAsSuccess()
        {
            var lines = SelfTestReport.Lines(new List<SelfTestReport.Check>());
            Assert.Equal(new[] { "Self-test of Sparring: nothing was checked" }, lines);
        }

        [Fact]
        public void ARunWhereEverythingPassedSaysSo()
        {
            var lines = SelfTestReport.Lines(new List<SelfTestReport.Check> { C("a", Result.Pass), C("b", Result.Pass), C("c", Result.Info) });
            Assert.Equal("Self-test of Sparring: all 2 passed, 1 for information", lines[0]);
        }

        [Fact]
        public void TheTopLineCountsFailuresWarningsAndPasses()
        {
            var lines = SelfTestReport.Lines(new List<SelfTestReport.Check>
            {
                C("a", Result.Pass), C("b", Result.Warn), C("c", Result.Fail), C("d", Result.Warn), C("e", Result.Info),
            });
            Assert.Equal("Self-test of Sparring: 1 failed, 2 warnings, 1 passed, 1 for information", lines[0]);
        }

        [Fact]
        public void OneWarningIsOneWarning()
        {
            var lines = SelfTestReport.Lines(new List<SelfTestReport.Check> { C("a", Result.Warn) });
            Assert.Equal("Self-test of Sparring: 1 warning", lines[0]);
        }

        [Fact]
        public void FailuresComeFirstThenWarningsPassesAndInformationEachInTheOrderChecked()
        {
            var lines = SelfTestReport.Lines(new List<SelfTestReport.Check>
            {
                C("pass one", Result.Pass), C("info", Result.Info), C("fail one", Result.Fail),
                C("warn", Result.Warn), C("fail two", Result.Fail), C("pass two", Result.Pass),
            });
            Assert.Equal(new[]
            {
                "FAIL  Area: fail one", "FAIL  Area: fail two", "WARN  Area: warn",
                "ok    Area: pass one", "ok    Area: pass two", "info  Area: info",
            }, lines.GetRange(1, lines.Count - 1));
        }

        [Fact]
        public void ADetailFollowsWhatWasChecked()
        {
            var lines = SelfTestReport.Lines(new List<SelfTestReport.Check> { C("the bell", Result.Fail, "not found") });
            Assert.Equal("FAIL  Area: the bell - not found", lines[1]);
        }
    }
}
