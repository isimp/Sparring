using System.Collections.Generic;

namespace Sparring.Standards
{
    /// <summary>
    /// The files under src/ the unit tests leave out, as tests/Sparring.Tests/Sparring.Tests.csproj
    /// excludes them, each with why it needs the game. The self-test reaches these in game.
    /// </summary>
    internal static class GameOnlyFiles
    {
        public static readonly Dictionary<string, string> List = new Dictionary<string, string>
        {
            ["src/Plugin.cs"] = "the plugin class, which BepInEx starts with the game and which binds the settings and hands the rules to ServerSync; tests/Sparring.Tests/PluginShim.cs stands in for it",
            ["src/ServerSync/**"] = "ServerSync, vendored unmodified, which keeps the rules in step with a server through Harmony patches on the game's network code",
        };
    }
}
