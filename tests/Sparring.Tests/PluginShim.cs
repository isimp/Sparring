using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// Stands in for the plugin class, which needs the game to exist, so the code that reports
    /// through it or reads its settings compiles into the tests. The settings answer with the
    /// defaults the plugin falls back on before it has bound them. The rules the tests check take
    /// their settings as arguments, so none of these values decides an outcome there.
    /// </summary>
    internal static class Plugin
    {
        public const string Guid = "isimp.Sparring";

        internal static readonly ManualLogSource Log = new ManualLogSource("Sparring");

        internal static bool RequireOutOfCombat => true;
        internal static bool RequireClearGround => true;
        internal static bool DebugCommands => false;
        internal static float ArenaRadius => 20f;
        internal static double InviteSeconds => 30f;
        internal static double PairGraceSeconds => 10f;
        internal static double CountdownSeconds => 3f;
        internal static float YieldHealth => 0.3f;
        internal static float VictoryHealth => 0.5f;
        internal static bool ProtectBuildings => true;
        internal static bool SkillGain => true;
        internal static float SkillGainRate => 1f;
        internal static float LookRange => 10f;
        internal static float LookAngle => 25f;
        internal static bool ClearEverything => false;
        internal static HashSet<string> AlsoClear { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static KeyCode AcceptKey => KeyCode.Y;
        internal static KeyCode DeclineKey => KeyCode.N;
        internal static KeyCode YieldKey => KeyCode.Backspace;
        internal static float YieldHoldSeconds => 0.7f;

        internal static bool ShowArenaRing => true;
        internal static float RingWidth => 0.15f;
        internal static Color RingColor => default(Color);
        internal static float HudY => 0.08f;
        internal static int HudFontSize => 13;
        internal static string CentreMarker => "CharredBanner2";
        internal static string RingMarker => "dvergrtown_wood_stake";
        internal static RingStyle RingStyle => RingStyle.Stakes;
        internal static bool ChallengeSoundOn => true;
        internal static string ChallengeSound => "sfx_silvermace_hit";
        internal static bool DuelSounds => true;
        internal static bool WinnerEffect => true;
        internal static string WinnerEmote => "cheer";

        internal static bool RulesFromServer => false;
    }
}
