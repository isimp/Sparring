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
    /// Where Sparring changes the game, every patch is declared in <c>Patches</c> and all of them go
    /// in together, with one <c>PatchAll(typeof(Patches))</c>. A duel's protection is several patches
    /// working as one, and half of them would be worse than none: blows let through between two
    /// players with no yield to catch the last one would make a duel a real fight. Harmony looks up
    /// every target of the class before it applies any, so a target a game update moved fails the
    /// patching with none applied, and Sparring then switches off as a whole and says why
    /// (<see cref="ShutoffStandardTests"/>). <c>Character.RPC_Damage</c> is never patched: the victim's game decides
    /// a hit as it always has, so a friendly area effect such as the Staff of Protection's shield
    /// reaches other players exactly as it does without the mod.
    /// </summary>
    public class PatchStandardTests
    {
        [Fact]
        public void EveryPatchIsInPatchesAndAppliedWithTheRest()
        {
            var found = new List<string>();
            var patches = 0;
            foreach (var (attribute, model) in SparringSource.In<AttributeSyntax>(new[] { SparringSource.Compilation }))
            {
                var type = model.GetTypeInfo(attribute).Type?.ToDisplayString() ?? "";
                if (!type.StartsWith("HarmonyLib.Harmony", StringComparison.Ordinal)) continue;
                patches++;
                if (SparringSource.TopType(attribute) != "Patches") found.Add($"{SparringSource.Where(attribute)} [{attribute.Name}] outside Patches");
            }

            var applied = 0;
            foreach (var (call, model) in SparringSource.In<InvocationExpressionSyntax>(new[] { SparringSource.Compilation }))
            {
                if (!(model.GetSymbolInfo(call).Symbol is IMethodSymbol method) || method.ContainingType?.Name != "Harmony") continue;
                if (method.Name == "Patch" || method.Name == "CreateClassProcessor") found.Add($"{SparringSource.Where(call)} Harmony.{method.Name} by hand");
                if (method.Name != "PatchAll") continue;

                var args = call.ArgumentList.Arguments;
                if (args.Count == 1 && args[0].Expression is TypeOfExpressionSyntax typeOf && typeOf.Type.ToString() == "Patches") applied++;
                else found.Add($"{SparringSource.Where(call)} PatchAll of something other than Patches");
            }

            Assert.True(patches > 5, "Sparring's patches were not found in its source");
            Assert.Equal(1, applied);
            Violations.None("patch the game apart from the rest", found);
        }

        [Fact]
        public void TheVictimsGameDecidesAHitAsItAlwaysHas()
        {
            var found = new List<string>();
            foreach (var (attribute, model) in SparringSource.In<AttributeSyntax>(new[] { SparringSource.Compilation }))
            {
                if (model.GetTypeInfo(attribute).Type?.ToDisplayString() != "HarmonyLib.HarmonyPatch") continue;
                var named = attribute.ArgumentList?.Arguments.Select(a => model.GetConstantValue(a.Expression).Value as string).Where(n => n != null) ?? Enumerable.Empty<string>();
                if (named.Contains("RPC_Damage", StringComparer.Ordinal)) found.Add($"{SparringSource.Where(attribute)} [{attribute}]");
            }
            Violations.None("patch Character.RPC_Damage", found);
        }
    }

    /// <summary>
    /// Sparring writes no file of its own: a duel lives in the two fighters' ZDOs and in memory,
    /// and the settings are BepInEx's to save. The self-test writes one file, its report, from
    /// <c>SelfTest.cs</c>.
    /// </summary>
    public class FileWriteStandardTests
    {
        private static readonly HashSet<string> Writes = new HashSet<string>
        {
            "WriteAllText", "WriteAllLines", "WriteAllBytes", "AppendAllText", "AppendAllLines", "AppendText", "CreateText",
            "Create", "Copy", "Move", "Replace", "Delete", "Open", "OpenWrite", "CreateDirectory",
        };

        [Fact]
        public void SparringWritesNoFileAndTheSelfTestOnlyItsReport()
        {
            var found = new List<string>();
            foreach (var (call, model) in SparringSource.All<InvocationExpressionSyntax>())
            {
                if (!(model.GetSymbolInfo(call).Symbol is IMethodSymbol method)) continue;
                var owner = method.ContainingType?.ToDisplayString();
                if ((owner != "System.IO.File" && owner != "System.IO.Directory") || !Writes.Contains(method.Name)) continue;
                if (SparringSource.Relative(call.SyntaxTree) == "selftest/SelfTest.cs" && method.Name == "WriteAllText") continue;
                found.Add($"{SparringSource.Where(call)} {owner}.{method.Name} in {SparringSource.TopType(call)}.{SparringSource.Member(call)}");
            }
            foreach (var (made, model) in SparringSource.All<BaseObjectCreationExpressionSyntax>())
            {
                var type = model.GetTypeInfo(made).Type?.ToDisplayString();
                if (type == "System.IO.StreamWriter" || type == "System.IO.FileStream")
                    found.Add($"{SparringSource.Where(made)} new {type} in {SparringSource.TopType(made)}.{SparringSource.Member(made)}");
            }
            Violations.None("write a file", found);
        }
    }
}
