using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace Sparring
{
    [BepInPlugin(Guid, "Sparring", "0.2.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "isimp.Sparring";

        public static ManualLogSource Log;

        private static ConfigEntry<bool> _requireOutOfCombat;
        private static ConfigEntry<bool> _requireClearGround;
        private static ConfigEntry<bool> _refusePvpBypass;
        private static ConfigEntry<bool> _debugCommands;
        private static ConfigEntry<float> _arenaRadius;
        private static ConfigEntry<float> _inviteSeconds;
        private static ConfigEntry<float> _pairGrace;
        private static ConfigEntry<float> _countdown;
        private static ConfigEntry<float> _yieldHealth;
        private static ConfigEntry<float> _victoryHealth;
        private static ConfigEntry<bool> _protectBuildings;
        private static ConfigEntry<bool> _skillGain;
        private static ConfigEntry<float> _skillGainRate;
        private static ConfigEntry<float> _lookRange;
        private static ConfigEntry<float> _lookAngle;
        private static ConfigEntry<bool> _clearEverything;
        private static ConfigEntry<string> _alsoClear;

        private static ConfigEntry<KeyCode> _acceptKey;
        private static ConfigEntry<KeyCode> _declineKey;
        private static ConfigEntry<KeyCode> _yieldKey;
        private static ConfigEntry<float> _yieldHold;

        private static ConfigEntry<bool> _showRing;
        private static ConfigEntry<float> _ringWidth;
        private static ConfigEntry<string> _ringColor;
        private static ConfigEntry<float> _hudY;
        private static ConfigEntry<int> _hudFontSize;
        private static ConfigEntry<string> _centreMarker;
        private static ConfigEntry<string> _ringMarker;
        private static ConfigEntry<RingStyle> _ringStyle;
        private static ConfigEntry<bool> _challengeSoundOn;
        private static ConfigEntry<string> _challengeSound;
        private static ConfigEntry<bool> _duelSounds;
        private static ConfigEntry<bool> _winnerEffect;
        private static ConfigEntry<string> _winnerEmote;

        private static readonly HashSet<string> _alsoClearSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Color _ring = new Color(0.85f, 0.72f, 0.35f, 0.85f);

        public static bool RequireOutOfCombat => _requireOutOfCombat?.Value ?? true;
        public static bool RequireClearGround => _requireClearGround?.Value ?? true;
        public static bool RefusePvpBypass => _refusePvpBypass?.Value ?? true;
        public static bool DebugCommands => _debugCommands?.Value ?? false;
        public static float ArenaRadius => _arenaRadius?.Value ?? 20f;
        public static double InviteSeconds => _inviteSeconds?.Value ?? 30f;
        public static double PairGraceSeconds => _pairGrace?.Value ?? 10f;
        public static double CountdownSeconds => _countdown?.Value ?? 3f;
        public static float YieldHealth => _yieldHealth?.Value ?? 0.3f;
        public static float VictoryHealth => _victoryHealth?.Value ?? 0.5f;
        public static bool ProtectBuildings => _protectBuildings?.Value ?? true;
        public static bool SkillGain => _skillGain?.Value ?? true;
        public static float SkillGainRate => _skillGainRate?.Value ?? 1f;
        public static float LookRange => _lookRange?.Value ?? 10f;
        public static float LookAngle => _lookAngle?.Value ?? 25f;
        public static bool ClearEverything => _clearEverything?.Value ?? false;
        public static HashSet<string> AlsoClear => _alsoClearSet;

        public static KeyCode AcceptKey => _acceptKey?.Value ?? KeyCode.Y;
        public static KeyCode DeclineKey => _declineKey?.Value ?? KeyCode.N;
        public static KeyCode YieldKey => _yieldKey?.Value ?? KeyCode.Backspace;
        public static float YieldHoldSeconds => _yieldHold?.Value ?? 0.7f;

        public static bool ShowArenaRing => _showRing?.Value ?? true;
        public static float RingWidth => _ringWidth?.Value ?? 0.15f;
        public static Color RingColor => _ring;
        public static float HudY => _hudY?.Value ?? 0.08f;
        public static int HudFontSize => _hudFontSize?.Value ?? 13;
        public static string CentreMarker => (_centreMarker?.Value ?? "CharredBanner2").Trim();
        public static string RingMarker => (_ringMarker?.Value ?? "dvergrtown_wood_stake").Trim();
        public static RingStyle RingStyle => _ringStyle?.Value ?? RingStyle.Stakes;
        public static bool ChallengeSoundOn => _challengeSoundOn?.Value ?? true;
        public static string ChallengeSound => (_challengeSound?.Value ?? "sfx_silvermace_hit").Trim();
        public static bool DuelSounds => _duelSounds?.Value ?? true;
        public static bool WinnerEffect => _winnerEffect?.Value ?? true;
        public static string WinnerEmote => (_winnerEmote?.Value ?? "cheer").Trim().ToLowerInvariant();

        /// <summary>
        /// The hard bounds on a ring, in metres. Constants rather than settings: a radius crosses
        /// the network and both fighters clamp it, so configurable bounds could clamp the same
        /// number to two different rings. The proposed size stays configurable, since it only has
        /// to be right on the machine proposing it.
        /// </summary>
        public const float RadiusFloor = 5f;
        public const float RadiusCeiling = 60f;

        /// <summary>
        /// Holds a proposed ring size to what the mod allows. Applied to the number typed locally
        /// and to the one that arrives from the other side, because a radius off the network is
        /// untrusted input: a huge one would protect the fighters across the map, and zero would
        /// put both permanently outside the ring.
        /// </summary>
        public static float ClampRadius(float radius)
        {
            if (radius <= 0f || float.IsNaN(radius)) return ArenaRadius;
            return Mathf.Clamp(radius, RadiusFloor, RadiusCeiling);
        }

        /// <summary>
        /// How far apart the fighters may get before the duel is called off, derived from the ring
        /// rather than set on its own.
        ///
        /// Two people at opposite edges of the ring are two radii apart, so any fixed figure below
        /// that ends duels that never left the arena. Derived, it cannot disagree with the ring it
        /// guards.
        /// </summary>
        public static float OpponentRangeFor(float radius)
        {
            return radius * 2f + 15f;
        }

        private Harmony _harmony;
        private ConfigSync _sync;
        private static ConfigEntry<bool> _lockConfig;

        /// <summary>
        /// Whether the rules in force came from a server rather than from this machine, so
        /// /duel status can say why a local change had no effect.
        /// </summary>
        public static bool RulesFromServer { get; private set; }

        /// <summary>
        /// Binds a setting that is part of the *rules* and hands it to ServerSync, so an admin's
        /// value governs everyone on their server.
        ///
        /// The split is between rules and preferences. A rule has to be the same for both fighters,
        /// or two clients would disagree about what happened. A preference only has to be right on
        /// the machine it is set on (keys, text size, whether to show the ring), so it stays local.
        /// </summary>
        private ConfigEntry<T> Rule<T>(string group, string name, T value, ConfigDescription description)
        {
            var entry = Config.Bind(group, name, value, description);
            _sync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }

        private ConfigEntry<T> Rule<T>(string group, string name, T value, string description)
        {
            return Rule(group, name, value, new ConfigDescription(description));
        }

        private void Awake()
        {
            Log = Logger;

            // The version comes from the plugin metadata, so there is no fourth place for it to
            // drift from.
            //
            // MinimumRequiredVersion is the version the current message protocol arrived in, not
            // the current version of the mod. ServerSync requires each side to be at least the
            // other's minimum, so a minimum equal to CurrentVersion would demand an exact match and
            // lock out a player one patch ahead as firmly as one behind. Pinned to the protocol,
            // only releases that change how clients talk refuse older clients.
            //
            // Raise this only when Link.Protocol is raised, to the version that carries the change.
            //
            // ModRequired stays false: a server running Sparring still admits players without it.
            _sync = new ConfigSync(Guid)
            {
                DisplayName = "Sparring",
                CurrentVersion = Info.Metadata.Version.ToString(),
                MinimumRequiredVersion = Link.ProtocolSince,
                ModRequired = false,
            };
            _sync.SourceOfTruthChanged += mine => RulesFromServer = !mine;

            // Local, not a rule: a server sets the rules of a duel, but does not get to switch off a
            // player's protection against being hit by someone they never agreed to fight.
            _refusePvpBypass = Config.Bind("5 - Safety", "RefusePvpBypass", true,
                "Refuse hits that claim to bypass PvP from players you have not agreed to duel. The vanilla game " +
                "never sets that flag, so such a hit comes from a modified client. The hit is not dropped; it falls " +
                "back to ordinary PvP rules. Turn this off only if another mod uses the flag legitimately.");

            // Bound plainly rather than through Rule, but synchronised all the same:
            // AddLockingConfigEntry calls AddConfigEntry itself, since a lock each client could
            // switch off would not be a lock.
            _lockConfig = Config.Bind("2 - Rules", "LockConfiguration", true,
                "Whether the server's duel rules override local settings. Admins are exempt. Keys and display " +
                "settings always stay local.");
            _sync.AddLockingConfigEntry(_lockConfig);

            _debugCommands = Rule("2 - Rules", "DebugCommands", false,
                "Enables the rehearsal and tuning commands: /duel practice, hit, burn, poison, spar, panels, " +
                "testwin, testyield, testmessages, testannounce, testsound, sounds, playsound and prefabs. " +
                "Off by default because several of them restore health. /duel preview is always available.");

            _requireClearGround = Rule("2 - Rules", "RequireClearGround", true,
                "A duel can only be started with nothing hostile around the ring. Tames and dvergr do not count.");
            _requireOutOfCombat = Rule("2 - Rules", "RequireOutOfCombat", true,
                "A duel can only be started out of combat, using the same ten second rule the game applies to " +
                "switching PvP.");

            _arenaRadius = Rule("1 - Duel", "ArenaRadius", 20f,
                new ConfigDescription("Default ring size in metres, used when /duel is given no number. Leaving the " +
                    "ring ends the duel.",
                    new AcceptableValueRange<float>(RadiusFloor, RadiusCeiling)));
            _countdown = Rule("1 - Duel", "CountdownSeconds", 3f,
                new ConfigDescription("Seconds between accepting and the fight starting. No duel damage is possible " +
                    "before it ends.",
                    new AcceptableValueRange<float>(0f, 10f)));
            _inviteSeconds = Rule("1 - Duel", "InviteSeconds", 30f,
                new ConfigDescription("How long a challenge waits for an answer, in seconds.",
                    new AcceptableValueRange<float>(10f, 120f)));
            _pairGrace = Rule("1 - Duel", "PairGraceSeconds", 10f,
                new ConfigDescription("How long to wait after accepting for both sides' state to line up over the " +
                    "network before the duel is called off, in seconds.",
                    new AcceptableValueRange<float>(2f, 30f)));
            _yieldHealth = Rule("1 - Duel", "YieldHealth", 0.3f,
                new ConfigDescription("Fraction of maximum health the loser is brought up to when the duel ends. " +
                    "Never lowers health.",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            _victoryHealth = Rule("1 - Duel", "VictoryHealth", 0.5f,
                new ConfigDescription("Fraction of maximum health the winner is brought up to when the duel ends. " +
                    "Never lowers health. Keep this at or above YieldHealth.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            _acceptKey = Config.Bind("3 - Controls", "Accept", KeyCode.Y,
                "Accepts the challenge on screen. A plain key rather than a combination, because BepInEx " +
                "combinations do not fire while another key is held.");
            _declineKey = Config.Bind("3 - Controls", "Decline", KeyCode.N,
                "Declines the challenge on screen.");
            _yieldKey = Config.Bind("3 - Controls", "Yield", KeyCode.Backspace,
                "Hold to give up a duel in progress. /duel yield does the same from chat.");
            _yieldHold = Config.Bind("3 - Controls", "YieldHoldSeconds", 0.7f,
                new ConfigDescription("How long the yield key must be held, in seconds. Letting go early cancels it.",
                    new AcceptableValueRange<float>(0.2f, 2f)));

            _showRing = Config.Bind("4 - Display", "ShowArenaRing", true,
                "Show the ring and its centre marker. Each client builds its own copy, and nothing is spawned into " +
                "the world.");
            _ringWidth = Config.Bind("4 - Display", "RingWidth", 0.15f,
                new ConfigDescription("Thickness of the drawn ring, in metres.",
                    new AcceptableValueRange<float>(0.05f, 0.5f)));
            _ringColor = Config.Bind("4 - Display", "RingColour", "D9B859B0",
                "Ring colour as RRGGBBAA hex.");
            _hudY = Config.Bind("4 - Display", "HudHeight", 0.08f,
                new ConfigDescription("Where the duel panel sits down the screen: 0 is the top, 1 the bottom.",
                    new AcceptableValueRange<float>(0f, 0.9f)));
            _hudFontSize = Config.Bind("4 - Display", "HudFontSize", 13,
                new ConfigDescription("Text size in the duel panel.", new AcceptableValueRange<int>(10, 24)));
            _ringStyle = Config.Bind("4 - Display", "RingStyle", RingStyle.Stakes,
                "How the edge of the arena is shown. Stakes places posts around it. Line draws a circle, and is " +
                "used automatically when no post prefab can be found.");
            _ringMarker = Config.Bind("4 - Display", "RingMarker", "dvergrtown_wood_stake",
                "Prefab placed around the edge when RingStyle is Stakes. A name that does not resolve falls back to " +
                "a few known ones. With DebugCommands on, /duel prefabs <text> lists what is available.");
            _centreMarker = Config.Bind("4 - Display", "CentreMarker", "CharredBanner2",
                "Prefab placed in the middle of the ring, as scenery only. A name that does not resolve falls back " +
                "to a few known ones, and the log says which was used. With DebugCommands on, /duel prefabs <text> " +
                "lists what is available.");

            _challengeSoundOn = Config.Bind("4 - Display", "ChallengeSound", true,
                "Play a sound when you are challenged. Only you hear it.");
            _challengeSound = Config.Bind("4 - Display", "ChallengeSoundPrefab", "sfx_silvermace_hit",
                "Sound prefab played on a challenge. A name that does not resolve falls back to the skill-up chime. " +
                "With DebugCommands on, /duel sounds <text> lists audible prefabs and /duel playsound <name> plays one.");
            _duelSounds = Config.Bind("4 - Display", "DuelSounds", true,
                "Play sounds through a duel: one when your challenge is accepted, a tick each second of the countdown, " +
                "a bell when the fight begins, one when you are knocked down, and one when a challenge is turned down " +
                "or withdrawn or a duel is called off. Only you hear them.");

            _winnerEffect = Config.Bind("4 - Display", "WinnerEffect", true,
                "Show the skill-up effect over the winner, three times. Everyone nearby running Sparring sees it.");
            _winnerEmote = Config.Bind("4 - Display", "WinnerEmote", "cheer",
                "Emote the winner plays: wave, challenge, cheer, thumbsup, bow, flex, point, laugh, roar, dance, " +
                "toast, shrug, headbang, kneel, relax, vibe, loveyou, comehere, blowkiss, nonono, cower, cry, " +
                "despair, sit or rest. Leave empty for none.");

            _clearEverything = Rule("1 - Duel", "ClearEverything", false,
                "Remove every status effect when a duel ends, not only the harmful ones. This includes rested, food " +
                "and potion effects.");
            _alsoClear = Rule("1 - Duel", "AlsoClear", "",
                "Extra status effects to remove when a duel ends, by name, comma separated. The game's own burning, " +
                "poison, frost and harpoon effects are always removed; use this for effects added by other mods.");

            _protectBuildings = Rule("2 - Rules", "ProtectBuildings", true,
                "Duellists' hits do not damage built pieces. Trees, rocks and terrain are not covered.");
            _skillGain = Rule("1 - Duel", "SkillGain", true,
                "Whether duelling raises weapon, Blocking and Dodge skills as ordinary fighting does.");
            _skillGainRate = Rule("1 - Duel", "SkillGainRate", 1f,
                new ConfigDescription("Multiplier on skill gained while duelling, compared to ordinary fighting. " +
                    "Applies to weapon skills, Blocking and Dodge only.",
                    new AcceptableValueRange<float>(0f, 5f)));

            _lookRange = Config.Bind("3 - Controls", "LookRange", 10f,
                new ConfigDescription("How far away /duel looks for the player you mean, in metres.",
                    new AcceptableValueRange<float>(3f, 25f)));
            _lookAngle = Config.Bind("3 - Controls", "LookAngle", 25f,
                new ConfigDescription("How far off the middle of your view a player can be and still count as the one " +
                    "you are looking at, in degrees.",
                    new AcceptableValueRange<float>(5f, 45f)));

            RebuildAlsoClear();
            RebuildRingColor();
            _alsoClear.SettingChanged += (s, e) => RebuildAlsoClear();
            _ringColor.SettingChanged += (s, e) => RebuildRingColor();
            _showRing.SettingChanged += (s, e) => { if (!ShowArenaRing) { ArenaRing.HideAll(); ArenaRing.HidePreview(); } };
            _ringMarker.SettingChanged += (s, e) => ArenaRing.ForgetPrefabs();
            _centreMarker.SettingChanged += (s, e) => ArenaRing.ForgetPrefabs();
            _challengeSound.SettingChanged += (s, e) => Sound.Forget();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Patches));

            Commands.Register();

            Log.LogInfo("Sparring loaded.");
        }

        private void OnDestroy()
        {
            // Best effort. If none of this runs, the lease lapses and every protection ends by itself.
            try
            {
                Sound.Mute();
                ArenaRing.HideAll();
                ArenaRing.HidePreview();
                Victory.Stop();
                Scorecard.Stop();
                if (Duel.Active) Duel.EndLocal(EndReason.LeaseLapsed, notify: true);
            }
            catch (Exception ex)
            {
                Log?.LogDebug($"Sparring unload: {ex.Message}");
            }

            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            try
            {
                Sound.Prepare();
                Duel.Tick();
                Keys.Tick();
                Preview.Tick();
                ArenaRing.Sync();
                Victory.Tick();
            }
            catch (Exception ex)
            {
                Log.LogError($"Sparring tick failed: {ex}");
            }
        }

        private void OnGUI()
        {
            DuelHud.Warm();
            DuelHud.Draw();
        }

        private static void RebuildAlsoClear()
        {
            _alsoClearSet.Clear();
            var raw = _alsoClear?.Value;
            if (string.IsNullOrEmpty(raw)) return;

            foreach (var part in raw.Split(','))
            {
                var name = part.Trim();
                if (name.Length > 0) _alsoClearSet.Add(name);
            }
        }

        /// <summary>Parses the ring colour once rather than per frame, and keeps the old one if it will not parse.</summary>
        private static void RebuildRingColor()
        {
            var raw = _ringColor?.Value?.Trim().TrimStart('#');
            if (string.IsNullOrEmpty(raw)) return;

            if (ColorUtility.TryParseHtmlString("#" + raw, out var parsed)) _ring = parsed;
            else Log?.LogWarning($"Sparring could not read the ring colour \"{raw}\"; keeping the previous one.");
        }
    }
}
