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
    /// A duel's protection is every hook at once, so a hook the game will not take switches
    /// Sparring off as a whole (<c>Shutoff</c>). <c>Plugin.Awake</c> catches the failed patching,
    /// takes out the hooks that went in and switches off, and registers /duel either way, so /duel
    /// can say why. Every part of a duel runs from <c>Update</c> (the lease, the messages between
    /// two games, the keys), <c>OnGUI</c> (the panel) or /duel, and each of them checks the switch
    /// before anything else. The self-test still runs its checks that only read, and fights no
    /// duel while Sparring is off: with no hook to catch it, a fall to zero would be a real death.
    /// </summary>
    public class ShutoffStandardTests
    {
        [Fact]
        public void WhileSparringIsOffNoPartOfADuelRuns()
        {
            var found = new List<string>();
            foreach (var name in new[] { "Update", "OnGUI" })
            {
                var (method, model) = Method(SparringSource.Compilation, "Plugin", name);
                var first = method.Body?.Statements.FirstOrDefault();
                if (!(first is IfStatementSyntax check) || !Mentions(check.Condition, model) || !(check.Statement is ReturnStatementSyntax))
                    found.Add($"{SparringSource.Where(method)} Plugin.{name} does not begin with a return while Sparring is off");
            }

            var (run, runModel) = Method(SparringSource.Compilation, "Commands", "Run");
            var gate = run.Body.Statements.OfType<IfStatementSyntax>().FirstOrDefault(s => Mentions(s.Condition, runModel));
            var choice = run.Body.Statements.OfType<SwitchStatementSyntax>().FirstOrDefault();
            if (gate == null || choice == null || gate.SpanStart > choice.SpanStart || !gate.Statement.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any())
                found.Add($"{SparringSource.Where(run)} Commands.Run reaches its words before it checks whether Sparring is off");

            Violations.None("run a part of a duel while Sparring is off", found);
        }

        [Fact]
        public void AHookTheGameWillNotTakeSwitchesSparringOffAsAWhole()
        {
            var (awake, model) = Method(SparringSource.Compilation, "Plugin", "Awake");
            var calls = awake.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

            var patchAll = calls.Single(c => Called(c, model) == "PatchAll");
            var attempt = patchAll.Ancestors().OfType<TryStatementSyntax>().FirstOrDefault();
            Assert.True(attempt != null, "PatchAll is not inside a try in Plugin.Awake");

            var handled = new List<string>();
            foreach (var clause in attempt.Catches) Reached(clause.Block, model, handled, new HashSet<string>(StringComparer.Ordinal));
            Assert.Contains("UnpatchSelf", handled);
            Assert.Contains("Shutoff.Engage", handled);

            var register = calls.Single(c => Called(c, model) == "Commands.Register");
            Assert.False(register.Ancestors().Contains(attempt), "/duel is registered only when the hooks went in");
            Assert.True(register.SpanStart > attempt.Span.End, "/duel is registered before the hooks are tried");
        }

        [Fact]
        public void TheSelfTestFightsNoDuelWhileSparringIsOff()
        {
            var (answer, model) = Method(SparringSource.SelfTest, "SelfTest", "Answer");
            var first = answer.Body?.Statements.FirstOrDefault();
            Assert.True(first is IfStatementSyntax check && Mentions(check.Condition, model),
                "the self-test answers its duel words before it checks whether Sparring is off");

            var (run, runModel) = Method(SparringSource.SelfTest, "SelfTest", "Run");
            var fights = run.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c => Called(c, runModel) == "SelfTest.Lifecycle").ToList();
            Assert.NotEmpty(fights);
            foreach (var fight in fights)
            {
                Assert.True(fight.Ancestors().OfType<IfStatementSyntax>().Any(s => Mentions(s.Condition, runModel)),
                    $"{SparringSource.Where(fight)} the self-test fights its duels whether or not Sparring is off");
            }
        }

        /// <summary>A method of a type in a compilation, by name, with the semantic model of its file.</summary>
        private static (MethodDeclarationSyntax Method, SemanticModel Model) Method(CSharpCompilation compilation, string type, string name)
        {
            var found = SparringSource.In<MethodDeclarationSyntax>(new[] { compilation })
                .Where(m => m.Node.Identifier.Text == name && SparringSource.TopType(m.Node) == type)
                .ToList();
            Assert.True(found.Count == 1, $"{type}.{name} was not found once in the source");
            return found[0];
        }

        /// <summary>Every call a block makes, and those of the plugin's own methods it calls, as <see cref="Called"/> names them.</summary>
        private static void Reached(SyntaxNode block, SemanticModel model, List<string> calls, HashSet<string> seen)
        {
            foreach (var call in block.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = Called(call, model);
                if (name == null) continue;
                calls.Add(name);
                if (!name.StartsWith("Plugin.", StringComparison.Ordinal) || !seen.Add(name)) continue;

                var declared = (model.GetSymbolInfo(call).Symbol as IMethodSymbol)?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                if (declared != null) Reached(declared, SparringSource.Compilation.GetSemanticModel(declared.SyntaxTree), calls, seen);
            }
        }

        /// <summary>Whether an expression reads whether Sparring is off.</summary>
        private static bool Mentions(ExpressionSyntax condition, SemanticModel model) =>
            condition.DescendantNodesAndSelf().Any(n => model.GetSymbolInfo(n).Symbol is IPropertySymbol p
                && p.Name == "Engaged" && p.ContainingType?.ToDisplayString() == "Sparring.Shutoff");

        /// <summary>A call's method, as "Type.Method" for Sparring's own and the bare name otherwise.</summary>
        private static string Called(InvocationExpressionSyntax call, SemanticModel model)
        {
            if (!(model.GetSymbolInfo(call).Symbol is IMethodSymbol method)) return null;
            var owner = method.ContainingType?.ToDisplayString() ?? "";
            return owner.StartsWith("Sparring.", StringComparison.Ordinal) ? method.ContainingType.Name + "." + method.Name : method.Name;
        }
    }
}
