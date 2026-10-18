using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Sparring.Standards
{
    /// <summary>
    /// Sparring opens nothing outside the game and starts no program. A browser or folder the game
    /// opened would also keep Steam showing Valheim as running after it quits, as Steam counts
    /// every process the game starts as part of it.
    /// </summary>
    public class ShellStandardTests
    {
        [Fact]
        public void NothingOpensOutsideTheGame()
        {
            var found = new List<string>();
            foreach (var (call, model) in SparringSource.All<InvocationExpressionSyntax>())
            {
                if (!(model.GetSymbolInfo(call).Symbol is IMethodSymbol method)) continue;
                var owner = method.ContainingType?.ToDisplayString();
                if ((owner == "UnityEngine.Application" && method.Name == "OpenURL") || (owner == "System.Diagnostics.Process" && method.Name == "Start"))
                    found.Add($"{SparringSource.Where(call)} {method.Name} in {SparringSource.TopType(call)}.{SparringSource.Member(call)}");
            }
            foreach (var (made, model) in SparringSource.All<BaseObjectCreationExpressionSyntax>())
            {
                if (model.GetTypeInfo(made).Type?.ToDisplayString() != "System.Diagnostics.ProcessStartInfo") continue;
                found.Add($"{SparringSource.Where(made)} ProcessStartInfo in {SparringSource.TopType(made)}.{SparringSource.Member(made)}");
            }
            Violations.None("open something outside the game or start a program", found);
        }
    }

    /// <summary>
    /// Two clocks, each for its own kind of time. What both fighters must agree on (when a lease
    /// runs out, when the countdown ends, when an invitation lapses) keeps the server's time, which
    /// every client is sent, and Sparring reads it in one place, <c>Lease.Now</c>, so no part of a
    /// duel can be timed against a clock the other side does not share. What only this machine
    /// shows (the card, the winner's effect, a rehearsal) keeps real time, which runs on while the
    /// game is paused; nothing of Sparring's keeps the game's own frame time, which stops then.
    /// </summary>
    public class ClockStandardTests
    {
        [Fact]
        public void TimersKeepRealTime()
        {
            var found = new List<string>();
            foreach (var (access, model) in SparringSource.All<MemberAccessExpressionSyntax>())
            {
                if (!(model.GetSymbolInfo(access).Symbol is IPropertySymbol property) || property.ContainingType?.ToDisplayString() != "UnityEngine.Time") continue;
                var name = property.Name;
                if (name != "time" && name != "deltaTime" && name != "timeAsDouble" && name != "fixedTime" && name != "smoothDeltaTime") continue;
                found.Add($"{SparringSource.Where(access)} Time.{name} in {SparringSource.TopType(access)}.{SparringSource.Member(access)}");
            }
            Violations.None("keep the game's time, which stops while it is paused", found);
        }

        [Fact]
        public void TheServersClockIsReadInOnePlace()
        {
            var found = new List<string>();
            var reads = 0;
            foreach (var (call, model) in SparringSource.All<InvocationExpressionSyntax>())
            {
                if (!(model.GetSymbolInfo(call).Symbol is IMethodSymbol method) || method.ContainingType?.ToDisplayString() != "ZNet") continue;
                if (!method.Name.StartsWith("GetTime", StringComparison.Ordinal)) continue;
                reads++;
                if (SparringSource.TopType(call) != "Lease" || SparringSource.Member(call) != "Now")
                    found.Add($"{SparringSource.Where(call)} ZNet.{method.Name} in {SparringSource.TopType(call)}.{SparringSource.Member(call)}");
            }

            Assert.True(reads > 0, "the server's clock was not found in Sparring's source");
            Violations.None("read the server's clock outside Lease.Now", found);
        }
    }
}
