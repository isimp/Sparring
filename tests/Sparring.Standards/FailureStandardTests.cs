using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Sparring.Standards
{
    /// <summary>
    /// A failure in Sparring never takes the game or another mod down with it, never swallows a
    /// death it could not judge, and is never lost either. A catch that takes every failure
    /// (<c>catch</c>, <c>catch (Exception)</c>) tells it through one of <c>Plugin.Log</c>'s
    /// levels, or passes it on. Where a failure only means a value cannot be read, a key the game
    /// cannot name yet, the catch begins with a comment saying so, and that is the whole of what
    /// it means. And a patch that can stop the game's own method lets it run when it fails, so a
    /// fault of Sparring's leaves the game's rules standing: a death stays a death.
    /// </summary>
    public class FailureStandardTests
    {
        [Fact]
        public void ACatchOfEveryFailureTellsItOrSaysWhatItMeans()
        {
            var found = new List<string>();
            foreach (var (clause, model) in SparringSource.All<CatchClauseSyntax>())
            {
                if (!CatchesEverything(clause, model)) continue;
                if (Tells(clause.Block, model) || PassesOn(clause.Block) || Explains(clause)) continue;
                found.Add($"{SparringSource.Where(clause)} in {SparringSource.TopType(clause)}.{SparringSource.Member(clause)}");
            }
            Violations.None("catch every failure without telling it or saying what it means", found);
        }

        [Fact]
        public void APatchThatFailsLeavesTheGameToItsOwnRules()
        {
            // A prefix returning false skips the game's method; one that fails must not.
            var found = new List<string>();
            var prefixes = 0;
            foreach (var (method, model) in SparringSource.In<MethodDeclarationSyntax>(new[] { SparringSource.Compilation }))
            {
                if (SparringSource.TopType(method) != "Patches" || model.GetDeclaredSymbol(method)?.ReturnType.SpecialType != SpecialType.System_Boolean) continue;
                if (!method.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() == "HarmonyPrefix")) continue;
                prefixes++;

                var catches = method.DescendantNodes().OfType<CatchClauseSyntax>().ToList();
                if (catches.Count == 0) found.Add($"{SparringSource.Where(method)} {method.Identifier.Text} catches nothing");
                foreach (var clause in catches)
                {
                    var returns = clause.Block.DescendantNodes().OfType<ReturnStatementSyntax>().ToList();
                    if (returns.Count == 0 || returns.Any(r => !r.Expression.IsKind(SyntaxKind.TrueLiteralExpression)))
                        found.Add($"{SparringSource.Where(clause)} {method.Identifier.Text} does not let the game run");
                }
            }

            Assert.True(prefixes >= 3, "the patches that can stop the game were not found");
            Violations.None("stop the game's own method when they fail", found);
        }

        private static bool CatchesEverything(CatchClauseSyntax clause, SemanticModel model) =>
            clause.Declaration == null || model.GetTypeInfo(clause.Declaration.Type).Type?.ToDisplayString() == "System.Exception";

        /// <summary>Whether the block reports through the plugin's log.</summary>
        private static bool Tells(BlockSyntax block, SemanticModel model) =>
            block.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                model.GetSymbolInfo(call).Symbol is IMethodSymbol method && method.ContainingType?.ToDisplayString() == "BepInEx.Logging.ManualLogSource");

        private static bool PassesOn(BlockSyntax block) =>
            block.DescendantNodes().OfType<ThrowStatementSyntax>().Any() || block.DescendantNodes().OfType<ThrowExpressionSyntax>().Any();

        /// <summary>Whether the catch's block begins with a comment, the first thing inside its braces or on its own line.</summary>
        private static bool Explains(CatchClauseSyntax clause)
        {
            var inside = clause.Block.OpenBraceToken.TrailingTrivia.Concat(
                clause.Block.Statements.Count > 0 ? clause.Block.Statements[0].GetLeadingTrivia() : clause.Block.CloseBraceToken.LeadingTrivia);
            return inside.Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia));
        }
    }
}
