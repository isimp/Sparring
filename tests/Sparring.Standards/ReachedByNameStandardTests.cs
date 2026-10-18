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
    /// What Sparring reaches in the game by its name, out of the compiler's sight, the self-test
    /// looks for in the running game, so an update that renames one is told rather than found as
    /// a duel that quietly stopped protecting anyone. Every field handed to
    /// <c>AccessTools.FieldRefAccess</c> in Sparring's source is checked by the self-test's
    /// <c>Member(checks, typeof(Type), "name", ...)</c>, and so is every name handed to the other
    /// lookups of <c>AccessTools</c> and to plain reflection on a game type.
    /// </summary>
    public class ReachedByNameStandardTests
    {
        private static readonly HashSet<string> Lookups = new HashSet<string>
        {
            "Method", "DeclaredMethod", "Field", "DeclaredField", "Property", "DeclaredProperty", "PropertyGetter", "PropertySetter", "FieldRefAccess",
        };

        private static readonly HashSet<string> TypeLookups = new HashSet<string> { "GetMethod", "GetField", "GetProperty" };

        [Fact]
        public void EveryMemberReachedByNameIsLookedForInGame()
        {
            var looked = LookedFor();
            var sparring = SparringSource.Compilation.Assembly;

            var found = new List<string>();
            var asked = 0;
            foreach (var (call, model) in SparringSource.In<InvocationExpressionSyntax>(new[] { SparringSource.Compilation }))
            {
                if (!(model.GetSymbolInfo(call).Symbol is IMethodSymbol method)) continue;
                var owner = method.ContainingType?.ToDisplayString();
                var args = call.ArgumentList.Arguments;

                ITypeSymbol type = null;
                string name = null;
                if (owner == "HarmonyLib.AccessTools" && Lookups.Contains(method.Name))
                {
                    // FieldRefAccess<Character, HitData>("m_lastHit") names its type as a type argument;
                    // the others take typeof(Type) and the name.
                    if (method.TypeArguments.Length == 2 && args.Count == 1)
                    {
                        type = method.TypeArguments[0];
                        name = model.GetConstantValue(args[0].Expression).Value as string;
                    }
                    else if (args.Count >= 2 && args[0].Expression is TypeOfExpressionSyntax typeOf)
                    {
                        type = model.GetTypeInfo(typeOf.Type).Type;
                        name = model.GetConstantValue(args[1].Expression).Value as string;
                    }
                }
                else if (owner == "System.Type" && TypeLookups.Contains(method.Name)
                         && call.Expression is MemberAccessExpressionSyntax access && access.Expression is TypeOfExpressionSyntax reflected && args.Count >= 1)
                {
                    type = model.GetTypeInfo(reflected.Type).Type;
                    name = model.GetConstantValue(args[0].Expression).Value as string;
                }

                if (type == null || name == null || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, sparring)) continue;
                asked++;
                var expected = type.ToDisplayString() + "." + name;
                if (!looked.Contains(expected)) found.Add($"{SparringSource.Where(call)} {expected}");
            }

            Assert.True(asked >= 3, "the names Sparring reaches were not found in its source");
            Violations.None("reach a game member by name that the self-test does not look for", found);
        }

        /// <summary>Every "Type.member" the self-test looks for with Member(checks, typeof(Type), "member", ...).</summary>
        private static HashSet<string> LookedFor()
        {
            var looked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (call, model) in SparringSource.In<InvocationExpressionSyntax>(new[] { SparringSource.SelfTest }))
            {
                if (!(call.Expression is IdentifierNameSyntax id) || id.Identifier.Text != "Member") continue;
                var args = call.ArgumentList.Arguments;
                if (args.Count < 3 || !(args[1].Expression is TypeOfExpressionSyntax typeOf)) continue;
                if (model.GetConstantValue(args[2].Expression).Value is string name)
                    looked.Add(model.GetTypeInfo(typeOf.Type).Type?.ToDisplayString() + "." + name);
            }
            return looked;
        }
    }
}
