using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sparring.Standards
{
    /// <summary>
    /// Sparring offers no API: no other mod builds on it, and two fighters' games talk through
    /// routed messages and their own ZDOs, never through each other's types. The self-test reaches
    /// Sparring's insides as its project grants it. So every type is internal but the plugin
    /// classes BepInEx loads, and the rules for what a library shows the world do not apply.
    /// </summary>
    public class ApiStandardTests
    {
        [Xunit.Fact]
        public void NothingButThePluginsIsPublic()
        {
            var found = new List<string>();
            foreach (var (declaration, model) in SparringSource.All<BaseTypeDeclarationSyntax>())
            {
                if (!(model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type) || type.ContainingType != null) continue;
                if (type.DeclaredAccessibility != Accessibility.Public) continue;
                var plugin = false;
                for (var b = type.BaseType; b != null; b = b.BaseType) plugin |= b.ToDisplayString() == "BepInEx.BaseUnityPlugin";
                if (!plugin) found.Add($"{SparringSource.Where(declaration)} {type.Name}");
            }
            foreach (var (declaration, model) in SparringSource.All<DelegateDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type && type.ContainingType == null && type.DeclaredAccessibility == Accessibility.Public)
                    found.Add($"{SparringSource.Where(declaration)} {type.Name}");
            }
            Violations.None("declare a public type that is no plugin", found);
        }
    }
}
