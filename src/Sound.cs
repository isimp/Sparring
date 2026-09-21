using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// The noise a challenge makes, so an invitation is not something you only notice if you
    /// happened to be looking at the middle of the screen.
    ///
    /// Plays one of the game's own sounds rather than shipping an audio file. Which one is yours to
    /// choose by prefab name, because sound names live in the asset bundles and cannot be read from
    /// any assembly — <c>/duel sounds</c> lists what your install has. Failing a name, it falls
    /// back to the chime the game plays when a skill goes up, which is the one effect on Player
    /// proven to exist and to be audible, since <c>OnSkillLevelup</c> does nothing else with it.
    ///
    /// Local in every sense: it is played on the machine that received the challenge, from a
    /// prefab spawned with its network view disabled and destroyed on a timer, so nothing is heard
    /// by anyone else and nothing is left behind if the prefab does not clean itself up.
    /// </summary>
    public static class Sound
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
                Plugin.Log.LogWarning($"Sparring could not find a sound called \"{named}\"; using the skill-up chime. /duel sounds lists what is there.");
            }
            return _chime;
        }

        /// <summary>
        /// Plays one sound by name, so a choice can be made by listening instead of guessing.
        ///
        /// It also says whether the prefab has any audio on it at all, which is the thing a name
        /// cannot tell you: plenty of prefabs sound like sounds and are not, and silence then means
        /// either "wrong name", "no audio" or "played, and it is quiet" with no way to tell which.
        /// </summary>
        public static void Audition(string name)
        {
            var me = Player.m_localPlayer;
            if (me == null) { Duel.Say("Not in a world yet."); return; }

            if (string.IsNullOrEmpty(name)) { Duel.Say("Add a sound name: /duel playsound sfx_haldor_greet"); return; }

            var prefab = Find(name);
            if (prefab == null) { Duel.Say($"No prefab called \"{name}\"."); return; }

            var audible = prefab.GetComponentInChildren<AudioSource>(true) != null;
            Play(prefab, me);

            Duel.Say(audible
                ? $"Playing {name}. Set ChallengeSoundPrefab to keep it."
                : $"{name} has no audio on it — nothing to hear.");
        }

        /// <summary>
        /// Lists prefabs matching some text that actually carry audio.
        ///
        /// The plain prefab listing is names only, and most of what matches "sfx" is not something
        /// you would want or can even hear. Filtering on an AudioSource turns a few hundred guesses
        /// into a handful worth auditioning.
        /// </summary>
        public static void List(string filter)
        {
            var scene = ZNetScene.instance;
            if (scene == null) { Duel.Say("Not in a world yet."); return; }
            if (string.IsNullOrEmpty(filter)) { Duel.Say("Add a search term: /duel sounds horn"); return; }

            // Registered prefabs and everything the effect lists point at, once each, in order.
            var names = new SortedSet<string>(scene.GetPrefabNames(), StringComparer.OrdinalIgnoreCase);
            var effects = Effects();
            if (effects != null) names.UnionWith(effects.Keys);

            var shown = 0;
            foreach (var name in names)
            {
                if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var prefab = Find(name);
                if (prefab == null || prefab.GetComponentInChildren<AudioSource>(true) == null) continue;

                Plugin.Log.LogInfo($"sound: {name}");
                Console.instance?.AddString(name);
                if (++shown >= 30) break;
            }

            Duel.Say(shown == 0
                ? $"Nothing audible matching \"{filter}\"."
                : $"{shown} audible matching \"{filter}\" — in the console (F5). /duel playsound <name> to hear one.");
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
        /// items, pieces, creatures and status effects that play them. A name the scene does not
        /// know is looked up among those as well.
        /// </summary>
        private static GameObject Find(string name)
        {
            var scene = ZNetScene.instance;
            if (scene == null || string.IsNullOrEmpty(name)) return null;

            var prefab = scene.GetPrefab(name);
            if (prefab != null) return prefab;

            var effects = Effects();
            return effects != null && effects.TryGetValue(name, out var found) ? found : null;
        }

        private static Dictionary<string, GameObject> _effects;
        private static readonly Dictionary<Type, FieldInfo[]> _effectFields = new Dictionary<Type, FieldInfo[]>();

        /// <summary>
        /// Every prefab an effect list in the game points at, by name.
        ///
        /// Built once, the first time a name misses the scene, by walking every registered prefab's
        /// components, the shared data and attacks of every item, every status effect, and the
        /// interface. It is a walk over the whole game, so it is only ever started by a name the
        /// scene did not know, and how long it took is written to the log.
        ///
        /// Sounds that belong only to locations, such as the boss altars and waystones, stay out of
        /// reach: locations are loaded from their bundles on demand, and walking them would force
        /// those loads.
        /// </summary>
        private static Dictionary<string, GameObject> Effects()
        {
            if (_effects != null) return _effects;

            var scene = ZNetScene.instance;
            if (scene == null) return null;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var index = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var name in scene.GetPrefabNames())
                {
                    var prefab = scene.GetPrefab(name);
                    if (prefab == null) continue;

                    foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) continue;
                        Gather(component, index);

                        var shared = (component as ItemDrop)?.m_itemData?.m_shared;
                        if (shared == null) continue;

                        Gather(shared, index);
                        if (shared.m_attack != null) Gather(shared.m_attack, index);
                        if (shared.m_secondaryAttack != null) Gather(shared.m_secondaryAttack, index);
                    }
                }

                var db = ObjectDB.instance;
                if (db != null)
                {
                    foreach (var effect in db.m_StatusEffects)
                    {
                        if (effect != null) Gather(effect, index);
                    }
                }

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
                        if (component != null) Gather(component, index);
                    }
                }
            }
            catch (Exception ex)
            {
                // Whatever was gathered before the fault is still worth having.
                Plugin.Log.LogWarning($"Sparring could not finish indexing the game's effects: {ex.Message}");
            }

            _effects = index;
            Plugin.Log.LogInfo($"Sparring indexed {index.Count} effect prefabs in {watch.ElapsedMilliseconds} ms.");
            return _effects;
        }

        private static void AddRoot(Component part, HashSet<Transform> roots)
        {
            if (part != null) roots.Add(part.transform.root);
        }

        /// <summary>Adds whatever the effect lists on one object point at.</summary>
        private static void Gather(object owner, Dictionary<string, GameObject> index)
        {
            foreach (var field in EffectFields(owner.GetType()))
            {
                if (!(field.GetValue(owner) is EffectList list) || list.m_effectPrefabs == null) continue;

                foreach (var data in list.m_effectPrefabs)
                {
                    var prefab = data?.m_prefab;
                    if (prefab != null && !index.ContainsKey(prefab.name)) index[prefab.name] = prefab;
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
