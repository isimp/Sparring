using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Sparring.Standards
{
    /// <summary>
    /// Every section of the settings is either synchronised or local. The rules, <c>1 - Duel</c> and
    /// <c>2 - Rules</c>, have to be the same for both fighters or two games would disagree about
    /// what happened, so each is bound through <c>Plugin.Rule</c>, which hands it to ServerSync and
    /// lets a server running Sparring decide it. The lock that makes the server's word stick is
    /// bound plainly and handed over as the locking entry, which ServerSync synchronises too. The
    /// preferences, <c>3 - Controls</c> and <c>4 - Display</c>, only have to be right on the machine
    /// they are set on, so each is bound plainly and stays local.
    /// </summary>
    public class SettingStandardTests
    {
        private static readonly HashSet<string> Synced = new HashSet<string>(StringComparer.Ordinal) { "1 - Duel", "2 - Rules" };
        private static readonly HashSet<string> Local = new HashSet<string>(StringComparer.Ordinal) { "3 - Controls", "4 - Display" };

        [Fact]
        public void TheRulesAreSynchronisedAndThePreferencesStayLocal()
        {
            var found = new List<string>();
            var binds = Binds().ToList();
            Assert.True(binds.Count > 20, "Sparring's settings were not found in its source");

            foreach (var (call, how, section, name) in binds)
            {
                var where = $"{SparringSource.Where(call)} {section} / {name}";
                if (!Synced.Contains(section) && !Local.Contains(section)) found.Add($"{where} is in a section that is neither synchronised nor local");
                else if (Synced.Contains(section) && how != "Rule" && name != "LockConfiguration") found.Add($"{where} is a rule bound without Rule");
                else if (Local.Contains(section) && how != "Bind") found.Add($"{where} is a preference handed to the server");
            }
            Violations.None("bind a setting against its section's kind", found);
        }

        [Fact]
        public void TheLockIsTheServersLockingEntry()
        {
            var plugin = PluginMethods().ToList();
            var locking = plugin.SelectMany(m => m.DescendantNodes().OfType<InvocationExpressionSyntax>())
                .Where(c => c.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.Text == "AddLockingConfigEntry")
                .ToList();
            Assert.Single(locking);
            Assert.Equal("_lockConfig", locking[0].ArgumentList.Arguments[0].Expression.ToString());

            var bound = Binds().Single(b => b.Name == "LockConfiguration");
            Assert.Equal("2 - Rules", bound.Section);
            Assert.Equal("_lockConfig", (bound.Call.Parent as AssignmentExpressionSyntax)?.Left.ToString());
        }

        /// <summary>Every setting the plugin binds: through Rule, or plainly through Config.Bind, with its section and name as written.</summary>
        private static IEnumerable<(InvocationExpressionSyntax Call, string How, string Section, string Name)> Binds()
        {
            foreach (var method in PluginMethods())
            {
                if (method.Identifier.Text == "Rule") continue;
                foreach (var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var how = call.Expression is IdentifierNameSyntax id && id.Identifier.Text == "Rule" ? "Rule"
                        : call.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.Text == "Bind" && access.Expression.ToString() == "Config" ? "Bind"
                        : null;
                    if (how == null) continue;

                    var args = call.ArgumentList.Arguments;
                    if (args.Count < 2 || !(args[0].Expression is LiteralExpressionSyntax section) || !(args[1].Expression is LiteralExpressionSyntax name)) continue;
                    yield return (call, how, section.Token.ValueText, name.Token.ValueText);
                }
            }
        }

        private static IEnumerable<MethodDeclarationSyntax> PluginMethods() =>
            SparringSource.Compilation.SyntaxTrees
                .Where(t => t.FilePath.EndsWith("Plugin.cs", StringComparison.Ordinal) && !SparringSource.Vendored(t))
                .SelectMany(t => t.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>());
    }
}
