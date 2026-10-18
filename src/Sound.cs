using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// The noises a duel makes: the challenge, and the moments of the duel itself, so none of them
    /// is something you only notice if you happened to be looking at the middle of the screen.
    ///
    /// Plays the game's own sounds rather than shipping audio files. The challenge sound is yours
    /// to choose by prefab name, because sound names live in the asset bundles and cannot be read
    /// from any assembly. Failing a name, it falls back to the chime the game plays when a skill
    /// goes up, which is the one effect on Player proven to exist and to be audible, since
    /// <c>OnSkillLevelup</c> does nothing else with it.
    ///
    /// Local in every sense: it is played on the machine that received the challenge, from a
    /// prefab spawned with its network view disabled and destroyed on a timer, so nothing is heard
    /// by anyone else and nothing is left behind if the prefab does not clean itself up.
    /// </summary>
    internal static class Sound
    {
        private const float Lifetime = 5f;

        private static GameObject _chime;
        private static bool _tried;

        /// <summary>The moments in a duel that have a sound of their own.</summary>
        public enum Cue
        {
            CountdownTick,
            FightBegins,
            Accepted,
            KnockedDown,
            CameToNothing,
        }

        /// <summary>
        /// The game's own sounds for each moment. Built in rather than configurable, since the
        /// choice only means anything once heard; a name that does not resolve leaves its moment
        /// silent rather than substituting something else.
        /// </summary>
        private static readonly Dictionary<Cue, string> CueNames = new Dictionary<Cue, string>
        {
            { Cue.CountdownTick, "sfx_gui_select" },
            { Cue.FightBegins, "sfx_fader_bell" },
            { Cue.Accepted, "sfx_gui_repairitem_forge" },
            { Cue.KnockedDown, "sfx_gameltroll_throw_attack_impact" },
            { Cue.CameToNothing, "sfx_gui_craftitem_workbench_end" },
        };

        /// <summary>Each moment and the sound built in for it.</summary>
        internal static IEnumerable<KeyValuePair<Cue, string>> Cues => CueNames;

        private static readonly Dictionary<Cue, GameObject> _cues = new Dictionary<Cue, GameObject>();
        private static bool _cuesReady;
        private static bool _muted;

        /// <summary>
        /// Looks up every cue once, as soon as there is a world and a player to hear them.
        ///
        /// Most of these sounds are not registered with the scene, and finding them means building
        /// the effect index, which takes a noticeable moment. Doing it here, on arrival, keeps that
        /// out of the duel itself: a cue is only ever played from what was found now.
        /// </summary>
        public static void Prepare()
        {
            if (_cuesReady || !Plugin.DuelSounds) return;
            if (ZNetScene.instance == null || Player.m_localPlayer == null) return;

            _cuesReady = true;
            foreach (var pair in CueNames)
            {
                var prefab = Find(pair.Value);
                _cues[pair.Key] = prefab;

                if (prefab == null) Plugin.Log.LogInfo($"Sparring could not find \"{pair.Value}\"; that moment of a duel will be silent.");
            }
        }

        /// <summary>Plays one moment's sound, at your own ears. Silent if it was never found.</summary>
        public static void Play(Cue cue)
        {
            if (_muted || !_cuesReady || !Plugin.DuelSounds) return;

            var me = Player.m_localPlayer;
            if (me == null) return;

            if (_cues.TryGetValue(cue, out var prefab) && prefab != null) Play(prefab, me);
        }

        /// <summary>Stops every cue for good. Called on the way out, so ending a duel during shutdown makes no sound.</summary>
        public static void Mute()
        {
            _muted = true;
        }

        /// <summary>Sounds the challenge, at your own ears rather than anywhere in the world.</summary>
        public static void Challenge()
        {
            var me = Player.m_localPlayer;
            if (me == null || !Plugin.ChallengeSoundOn) return;

            try
            {
                var prefab = Chime();
                if (prefab != null) { Play(prefab, me); return; }

                // Nothing named resolved, so use the one we know is there.
                var anchor = me.m_eye != null ? me.m_eye : me.transform;
                me.m_skillLevelupEffects.Create(anchor.position, anchor.rotation, null, 1f, -1, me.GetZDOID());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring could not sound the challenge: {ex.Message}");
            }
        }

        private static GameObject Chime()
        {
            var named = Plugin.ChallengeSound;
            if (string.IsNullOrEmpty(named)) return null;

            if (_tried) return _chime;

            if (ZNetScene.instance == null) return null;
            _tried = true;

            _chime = Find(named);
            if (_chime == null)
            {
                Plugin.Log.LogWarning($"Sparring could not find a sound called \"{named}\"; using the skill-up chime.");
            }
            return _chime;
        }

        private static void Play(GameObject prefab, Player at)
        {
            var was = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            try
            {
                var go = UnityEngine.Object.Instantiate(prefab, at.transform.position, Quaternion.identity);

                // On a timer regardless of what the prefab does for itself. Most sound effects tidy
                // up when they finish; one that does not would otherwise sit in the scene forever,
                // once per challenge.
                UnityEngine.Object.Destroy(go, Lifetime);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Sparring could not play that sound: {ex.Message}");
            }
            finally
            {
                ZNetView.m_forceDisableInit = was;
            }
        }

        /// <summary>
        /// A sound by name, wherever the game keeps it.
        ///
        /// <c>ZNetScene.GetPrefab</c> only knows the prefabs registered with the scene, and most
        /// sounds are not among them: they are referenced straight from the effect lists on the
        /// interface, status effects, items, pieces and creatures that play them. A name the scene
        /// does not know is looked for among those as well.
        /// </summary>
        internal static GameObject Find(string name)
        {
            var scene = ZNetScene.instance;
            if (scene == null || string.IsNullOrEmpty(name)) return null;

            var prefab = scene.GetPrefab(name);
            if (prefab != null) return prefab;

            if (_effects.TryGetValue(name, out var met)) return met;
            if (WalkedAll) return null;

            Walk(Wanted(scene, name));
            return _effects.TryGetValue(name, out var found) ? found : null;
        }

        /// <summary>Where a sound was found: registered with the scene, in an effect list, or nowhere (null).</summary>
        internal static string Source(string name)
        {
            var scene = ZNetScene.instance;
            if (scene == null || string.IsNullOrEmpty(name)) return null;

            if (scene.GetPrefab(name) != null) return "registered with the scene";
            return Find(name) != null ? "found in an effect list" : null;
        }

        private static readonly Dictionary<string, GameObject> _effects = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Type, FieldInfo[]> _effectFields = new Dictionary<Type, FieldInfo[]>();

        /// <summary>Whether a walk has been through the whole game, so a name it did not meet is nowhere to be found.</summary>
        internal static bool WalkedAll { get; private set; }

        /// <summary>How many effect prefabs the walks have met.</summary>
        internal static int Indexed => _effects.Count;

        /// <summary>How long the walks took between them.</summary>
        internal static long IndexMilliseconds { get; private set; }

        /// <summary>
        /// Everything one walk should look for: the name asked about and every sound Sparring will
        /// ask about later, so a single walk serves them all. Names the scene knows, or a walk has
        /// already met, are left out.
        /// </summary>
        private static HashSet<string> Wanted(ZNetScene scene, string asked)
        {
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { asked };
            if (Plugin.DuelSounds)
            {
                foreach (var name in CueNames.Values) wanted.Add(name);
            }
            if (Plugin.ChallengeSoundOn && !string.IsNullOrEmpty(Plugin.ChallengeSound)) wanted.Add(Plugin.ChallengeSound);

            wanted.RemoveWhere(name => _effects.ContainsKey(name) || scene.GetPrefab(name) != null);
            return wanted;
        }

        /// <summary>
        /// Walks the game's effect lists until every wanted name has been met, or until there is
        /// nothing left to walk. Stopping early is the point: a full walk takes around a tenth of a
        /// second, and what Sparring needs is usually met long before the end. A name that exists
        /// nowhere still costs one full walk, after which a miss is answered at once.
        ///
        /// Sounds that belong only to locations, such as the boss altars and waystones, stay out of
        /// reach unless something walked here also uses them: locations are loaded from their
        /// bundles on demand, and walking them would force those loads.
        /// </summary>
        private static void Walk(HashSet<string> wanted)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var complete = true;

            try
            {
                foreach (var owner in Owners())
                {
                    Gather(owner, wanted);
                    if (wanted.Count == 0)
                    {
                        complete = false;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // What was met before the fault is kept. Walking again would only meet the same
                // fault, so a miss after this is answered without one.
                Plugin.Log.LogWarning($"Sparring could not finish walking the game's effects: {ex.Message}");
            }

            WalkedAll = complete;
            IndexMilliseconds += watch.ElapsedMilliseconds;
            Plugin.Log.LogInfo(complete
                ? $"Sparring walked all of the game's effects: {Indexed} prefabs in {watch.ElapsedMilliseconds} ms."
                : $"Sparring found the sounds it needs among {Indexed} effect prefabs in {watch.ElapsedMilliseconds} ms.");
        }

        /// <summary>
        /// Every object whose effect lists can point at a sound. The interface comes first because
        /// most of the duel's sounds are its own and it is small, then the status effects, then
        /// every registered prefab with the shared data and attacks of the items among them.
        /// </summary>
        private static IEnumerable<object> Owners()
        {
            // The interface is part of the scene rather than a prefab, so its sounds are only
            // reachable through the live objects. Walking from each one's root covers the whole
            // of it, including the pieces that carry a sound of their own.
            var roots = new HashSet<Transform>();
            AddRoot(InventoryGui.instance, roots);
            AddRoot(Hud.instance, roots);
            AddRoot(StoreGui.instance, roots);
            AddRoot(Minimap.instance, roots);
            AddRoot(MessageHud.instance, roots);
            AddRoot(Menu.instance, roots);
            AddRoot(Chat.instance, roots);

            foreach (var root in roots)
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component != null) yield return component;
                }
            }

            var db = ObjectDB.instance;
            if (db != null)
            {
                foreach (var effect in db.m_StatusEffects)
                {
                    if (effect != null) yield return effect;
                }
            }

            var scene = ZNetScene.instance;
            if (scene == null) yield break;

            foreach (var name in scene.GetPrefabNames())
            {
                var prefab = scene.GetPrefab(name);
                if (prefab == null) continue;

                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    yield return component;

                    var shared = (component as ItemDrop)?.m_itemData?.m_shared;
                    if (shared == null) continue;

                    yield return shared;
                    if (shared.m_attack != null) yield return shared.m_attack;
                    if (shared.m_secondaryAttack != null) yield return shared.m_secondaryAttack;
                }
            }
        }

        private static void AddRoot(Component part, HashSet<Transform> roots)
        {
            if (part != null) roots.Add(part.transform.root);
        }

        /// <summary>Keeps whatever the effect lists on one object point at, crossing off each wanted name met.</summary>
        private static void Gather(object owner, HashSet<string> wanted)
        {
            foreach (var field in EffectFields(owner.GetType()))
            {
                if (!(field.GetValue(owner) is EffectList list) || list.m_effectPrefabs == null) continue;

                foreach (var data in list.m_effectPrefabs)
                {
                    var prefab = data?.m_prefab;
                    if (prefab == null || _effects.ContainsKey(prefab.name)) continue;

                    _effects[prefab.name] = prefab;
                    wanted.Remove(prefab.name);
                }
            }
        }

        /// <summary>The EffectList fields on a type and its bases, remembered per type.</summary>
        private static FieldInfo[] EffectFields(Type type)
        {
            if (_effectFields.TryGetValue(type, out var known)) return known;

            var found = new List<FieldInfo>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(flags))
                {
                    if (field.FieldType == typeof(EffectList)) found.Add(field);
                }
            }

            known = found.ToArray();
            _effectFields[type] = known;
            return known;
        }

        /// <summary>Drops the remembered prefab, so a name typed into the config takes without a restart.</summary>
        public static void Forget()
        {
            _chime = null;
            _tried = false;
        }
    }
}
