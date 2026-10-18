using System;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// The game as the duel flow sees it (<see cref="IDuelWorld{TId}"/>): the local player and
    /// their ZDO, the server's clock, the settings, the network messages, and the screen, sounds,
    /// health, card and ring. Each member does here what the flow asks and nothing more; every
    /// decision about a duel is the flow's.
    /// </summary>
    internal sealed class GameWorld : IDuelWorld<ZDOID>
    {
        private static Player Local => Player.m_localPlayer;

        public ZDOID Me
        {
            get
            {
                var me = Local;
                return me != null ? me.GetZDOID() : ZDOID.None;
            }
        }

        public string MyName
        {
            get
            {
                var me = Local;
                return me != null ? me.GetPlayerName() : "someone";
            }
        }

        public double Now => Lease.Now;

        public DuelRules Rules => new DuelRules
        {
            InviteSeconds = Plugin.InviteSeconds,
            PairGraceSeconds = Plugin.PairGraceSeconds,
            CountdownSeconds = Plugin.CountdownSeconds,
            ArenaRadius = Plugin.ArenaRadius,
            YieldHealth = Plugin.YieldHealth,
            VictoryHealth = Plugin.VictoryHealth,
            RequireOutOfCombat = Plugin.RequireOutOfCombat,
            RequireClearGround = Plugin.RequireClearGround,
        };

        public Standing Standing
        {
            get
            {
                var me = Local;
                if (me == null) return new Standing { Dead = true };

                return new Standing
                {
                    Dead = me.IsDead(),
                    InCutscene = me.InCutscene(),
                    Teleporting = me.IsTeleporting(),
                    Attached = me.IsAttached(),
                    InBed = me.InBed(),
                    Sleeping = me.IsSleeping(),
                    PvpOn = me.IsPVPEnabled(),
                    InCombat = !me.CanSwitchPVP(),
                };
            }
        }

        public bool Dead
        {
            get
            {
                var me = Local;
                return me != null && me.IsDead();
            }
        }

        /// <summary>
        /// Whether there is anything hostile standing around the ground a duel would be fought on.
        ///
        /// <c>RequireOutOfCombat</c> only knows whether you were recently hit, so a hostile creature
        /// nearby that has not attacked yet would pass it. This uses the game's own
        /// <c>HaveEnemyInRange</c>, which decides by faction, so tames and dvergr do not count.
        /// </summary>
        public bool HostileAround(float radius)
        {
            var me = Local;
            if (me == null) return false;

            try
            {
                return BaseAI.HaveEnemyInRange(me, me.transform.position, radius);
            }
            catch (Exception ex)
            {
                // Not being able to tell is not a reason to refuse a duel.
                Plugin.Log.LogDebug($"Sparring could not check for creatures: {ex.Message}");
                return false;
            }
        }

        public Vector3 Here
        {
            get
            {
                var me = Local;
                return me != null ? me.transform.position : Vector3.zero;
            }
        }

        public float? DistanceTo(ZDOID other)
        {
            var me = Local;
            var zdo = ZDOMan.instance?.GetZDO(other);
            if (me == null || zdo == null) return null;
            return Vector3.Distance(me.transform.position, zdo.GetPosition());
        }

        public Vector3? MidpointWith(ZDOID other)
        {
            var me = Local;
            var zdo = ZDOMan.instance?.GetZDO(other);
            if (me == null || zdo == null) return null;
            return Vector3.Lerp(me.transform.position, zdo.GetPosition(), 0.5f);
        }

        public float FromCenter(Vector3 center)
        {
            var me = Local;
            if (me == null) return 0f;

            var here = me.transform.position;
            return new Vector2(here.x - center.x, here.z - center.z).magnitude;
        }

        public bool TryFind(ZDOID other, out Sighting seen)
        {
            seen = default;
            var player = Lease.FindPlayer(other);
            if (player == null) return false;

            seen = new Sighting(player.GetPlayerName(), player.IsDead(), player.IsPVPEnabled());
            return true;
        }

        public void Stamp(ZDOID opponent) => Lease.Stamp(Local, opponent);
        public void SetTerms(in Lease.Terms terms) => Lease.SetTerms(Local, terms);
        public void ClearLease() => Lease.Clear(Local);

        public ZDOID Agreed() => Lease.Corroborated(Local, out var opponent) ? opponent : ZDOID.None;

        public bool SelfPair { set => Lease.AllowSelfPair = value; }

        public void SendChallenge(ZDOID to, float radius)
        {
            var target = Lease.FindPlayer(to);
            if (target != null) Link.SendChallenge(target, radius);
        }

        public void SendReply(ZDOID to, bool accepted, in Lease.Terms terms) => Link.SendReply(to, accepted, terms);
        public void SendBusy(ZDOID to, Busy why) => Link.SendBusy(to, why);
        public void SendResult(ZDOID to, EndReason reason) => Link.SendResult(to, reason);
        public void SendTally(ZDOID to) => Link.SendTally(to, Scorecard.TookTotal, Scorecard.TookHits, Scorecard.TookBiggest);
        public void SendAnnounce(ZDOID winner, string winnerName, string loserName, EndReason reason) => Link.SendAnnounce(winner, winnerName, loserName, reason);

        public void Say(string line) => Duel.Say(line);
        public void Play(Sound.Cue cue) => Sound.Play(cue);
        public void ChallengeSound() => Sound.Challenge();

        public void Lift(float fraction) => Recovery.Restore(Local, fraction);
        public void Settle() => Recovery.Settle(Local);

        /// <summary>
        /// Knocks the player down, pushed away from their opponent. Stagger turns the character to
        /// look back along this vector, so it is the push, not the facing; it falls back to their
        /// own forward when the opponent cannot be located. The animation is driven through
        /// ZSyncAnimation, so the winner sees it too rather than watching someone simply stop.
        /// </summary>
        public void KnockDown(ZDOID awayFrom)
        {
            var me = Local;
            if (me == null) return;

            var away = me.transform.forward;
            var theirs = ZDOMan.instance?.GetZDO(awayFrom);
            if (theirs != null)
            {
                var push = me.transform.position - theirs.GetPosition();
                push.y = 0f;
                if (push.sqrMagnitude > 0.01f) away = push.normalized;
            }

            me.Stagger(away);
        }

        public void Celebrate() => Victory.Emote(Local);
        public void RingChanged() => ArenaRing.Refresh();

        public void StopRehearsal()
        {
            Preview.StopSparring();
            Preview.ClearRing();
        }

        public void CardBegins(string opponent) => Scorecard.Begin(opponent);
        public void CardOutcome(DuelOutcome outcome) => Scorecard.SetOutcome(outcome);
        public void CardEnds(bool decided) => Scorecard.End(decided);
        public void CardHeard(float took, int hits, float biggest) => Scorecard.ReceiveOpponent(took, hits, biggest);
    }
}
