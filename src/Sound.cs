using System;
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
            _tried = true;

            var scene = ZNetScene.instance;
            if (scene == null) { _tried = false; return null; }

            _chime = scene.GetPrefab(named);
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

            var scene = ZNetScene.instance;
            var prefab = scene != null ? scene.GetPrefab(name) : null;
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

            var shown = 0;
            foreach (var name in scene.GetPrefabNames())
            {
                if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var prefab = scene.GetPrefab(name);
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

        /// <summary>Drops the remembered prefab, so a name typed into the config takes without a restart.</summary>
        public static void Forget()
        {
            _chime = null;
            _tried = false;
        }
    }
}
