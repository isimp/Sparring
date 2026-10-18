using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Sparring.Standards
{
    /// <summary>
    /// A release packages Sparring.dll alone, so a player installing Sparring gets nothing of the
    /// self-test. The self-test is a plugin of its own, <c>Sparring.SelfTest.dll</c>, which only a
    /// development build installs; it brings its words, its runner and its report, and reaches
    /// Sparring's insides through <c>InternalsVisibleTo</c>. Sparring holds only the plug point,
    /// <c>SelfTestHost</c>, where the self-test registers as it loads and /duel hands it a word
    /// Sparring does not know. Installing it is what turns the self-test on, so Sparring itself
    /// names no self-test word, setting or file.
    /// </summary>
    public class SelfTestContainmentTests
    {
        [Fact]
        public void SparringAloneHoldsNothingOfTheSelfTestButWhereItPlugsIn()
        {
            var found = Declared(SparringSource.Compilation)
                .Where(t => t.Name.IndexOf("SelfTest", StringComparison.Ordinal) >= 0 && t.Name != "SelfTestHost")
                .Select(t => $"{t.Where} {t.Name}");
            Violations.None("hold a part of the self-test in Sparring", found);
        }

        [Fact]
        public void SparringAloneNamesNoSelfTestSettingCommandOrFile()
        {
            var found = Literals(SparringSource.Compilation)
                .Where(l => l.Text.IndexOf("selftest", StringComparison.OrdinalIgnoreCase) >= 0 || l.Text.IndexOf("self-test", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(l => $"{l.Where} \"{l.Text}\"");
            Violations.None("name a self-test setting, command or file in Sparring", found);
        }

        [Fact]
        public void TheSelfTestPluginBringsItsCommand()
        {
            var types = Declared(SparringSource.SelfTest).Select(t => t.Name).ToList();
            Assert.Contains("SelfTestPlugin", types);
            Assert.Contains(Literals(SparringSource.SelfTest), l => l.Text.Equals("selftest", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void AReleaseTakesSparringDllAlone()
        {
            // The plugin's own project leaves the self-test's folder out, and the release packs
            // the one DLL the plugin's build makes.
            var root = SparringSource.Root();
            var project = File.ReadAllText(Path.Combine(root, "Sparring.csproj"));
            Assert.Contains("<Compile Remove=\"selftest/**\" />", project, StringComparison.Ordinal);

            var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
            Assert.Contains("cp bin/Release/Sparring.dll dist/package/", release, StringComparison.Ordinal);
            Assert.DoesNotContain("SelfTest", release, StringComparison.Ordinal);
        }

        /// <summary>Every type a compilation declares, nested ones too, with where.</summary>
        private static IEnumerable<(string Name, string Where)> Declared(CSharpCompilation compilation)
        {
            foreach (var tree in compilation.SyntaxTrees.Where(t => !string.IsNullOrEmpty(t.FilePath)))
            {
                foreach (var type in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()) yield return (type.Identifier.Text, SparringSource.Where(type));
            }
        }

        /// <summary>Every string a compilation writes, plain or as the text of an interpolated one, with where.</summary>
        private static IEnumerable<(string Text, string Where)> Literals(CSharpCompilation compilation)
        {
            foreach (var tree in compilation.SyntaxTrees.Where(t => !string.IsNullOrEmpty(t.FilePath)))
            {
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) yield return (literal.Token.ValueText, SparringSource.Where(node));
                    else if (node is InterpolatedStringTextSyntax text) yield return (text.TextToken.ValueText, SparringSource.Where(node));
                }
            }
        }
    }
}
