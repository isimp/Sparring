using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// Makes duelists invisible to creature targeting, using the game's own opt-out rather than a
    /// patch of our own.
    ///
    /// <c>Character.m_aiSkipTarget</c> is read inside <c>BaseAI.FindEnemy</c>, which is how
    /// <c>MonsterAI</c> picks targets. Setting it means nothing can newly choose that player. It is
    /// a plain field on a live Character with nothing persisted behind it, so a player who vanishes
    /// takes it with them and a player whose duel lapses gets it back on the next pass.
    ///
    /// Two things this does not do. It does not stop a creature the duelist attacks from hitting
    /// back: <c>MonsterAI.OnDamaged</c> sets its target unconditionally, so attacking a creature
    /// makes you its target again. And it does not touch <c>BaseAI.IsEnemy</c>, which would make
    /// duelists count as friends to creature healers.
    ///
    /// This runs for every player we can see, not just the local one, because creature AI runs on
    /// whichever client owns the creature — which may be neither duelist.
    /// </summary>
    public static class Neutrality
    {
        private const float IntervalSeconds = 0.25f;

        private static readonly Dictionary<Character, bool> _held = new Dictionary<Character, bool>();
        private static readonly List<Character> _stale = new List<Character>();
        private static float _next;

        private static AccessTools.FieldRef<MonsterAI, Character> _targetCreature;

        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + IntervalSeconds;

            if (ZNet.instance == null || ZDOMan.instance == null || !Plugin.NeutralToCreatures)
            {
                ReleaseAll();
                return;
            }

            foreach (var player in Player.GetAllPlayers())
            {
                if (player == null) continue;

                // A practice duel is deliberately left unshielded: it is fought alone, so creatures
                // are the only thing that can put a blow on the card.
                var practising = Duel.Practising && player == Player.m_localPlayer;

                var shielded = !practising && (Preview.Rehearsing(player) || Lease.Corroborated(player, out _));
                var holding = _held.ContainsKey(player);

                if (shielded && !holding)
                {
                    _held[player] = player.m_aiSkipTarget;
                    player.m_aiSkipTarget = true;
                    ShedAggro(player);
                }
                else if (!shielded && holding)
                {
                    Release(player);
                }
            }

            PruneAndReleaseMissing();
        }

        /// <summary>
        /// Drops targets a creature already had. Setting the flag stops new picks but not a creature
        /// already attacking, so aggro that exists when a duel starts is cleared once, here.
        /// </summary>
        private static void ShedAggro(Character duelist)
        {
            var field = TargetCreature();
            if (field == null) return;

            foreach (var ai in BaseAI.GetAllInstances())
            {
                if (!(ai is MonsterAI monster)) continue;

                var view = monster.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;

                try
                {
                    if (field(monster) == duelist) field(monster) = null;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug($"Sparring could not clear a creature's target: {ex.Message}");
                    return;
                }
            }
        }

        private static AccessTools.FieldRef<MonsterAI, Character> TargetCreature()
        {
            if (_targetCreature != null) return _targetCreature;

            try
            {
                _targetCreature = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Sparring cannot reach MonsterAI.m_targetCreature, so creatures already chasing a duelist will keep chasing until they lose interest: {ex.Message}");
            }
            return _targetCreature;
        }

        /// <summary>Puts one player's flag back the way we found it.</summary>
        private static void Release(Character player)
        {
            if (!_held.TryGetValue(player, out var original)) return;

            if (player != null) player.m_aiSkipTarget = original;
            _held.Remove(player);
        }

        /// <summary>
        /// Forgets players who are gone. A destroyed Character needs no restoring — the flag went
        /// with the object — so these only have to leave the table.
        /// </summary>
        private static void PruneAndReleaseMissing()
        {
            if (_held.Count == 0) return;

            _stale.Clear();
            foreach (var entry in _held)
            {
                if (entry.Key == null) _stale.Add(entry.Key);
            }
            foreach (var gone in _stale) _held.Remove(gone);
            _stale.Clear();
        }

        /// <summary>Hands every flag back. Used when the feature is switched off and at unload.</summary>
        public static void ReleaseAll()
        {
            if (_held.Count == 0) return;

            _stale.Clear();
            foreach (var entry in _held) _stale.Add(entry.Key);
            foreach (var player in _stale) Release(player);
            _stale.Clear();
            _held.Clear();
        }
    }
}
