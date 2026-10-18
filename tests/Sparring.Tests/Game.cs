using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sparring.Tests
{
    /// <summary>
    /// One player's game on a <see cref="Server"/>: where they stand, how they are, their health,
    /// and Sparring's duel flow running against all of it. Whatever the game would show or do is
    /// written down in order in <see cref="Events"/>, with the lines on screen, the sounds, the
    /// card and the announcements also kept on their own.
    /// </summary>
    internal sealed class Game : IDuelWorld<int>
    {
        private readonly Server _server;

        public Game(Server server, int id, string name)
        {
            _server = server;
            Id = id;
            Name = name;
            Flow = new DuelFlow<int>(this);
        }

        public int Id { get; }
        public string Name { get; }
        public DuelFlow<int> Flow { get; }

        /// <summary>Whether the game is running; a crashed or closed one ticks no more and gets no messages.</summary>
        public bool Running { get; private set; } = true;

        /// <summary>Whether there is a character in the world, which a death takes away until the respawn.</summary>
        public bool InWorld { get; set; } = true;

        public float X, Z;
        public Standing Standing;
        public bool Hostile;
        public float Health = 100f, MaxHealth = 100f;
        public bool SelfPair;
        /// <summary>The settings' defaults, as the plugin binds them.</summary>
        public DuelRules Rules = new DuelRules
        {
            InviteSeconds = 30.0,
            PairGraceSeconds = 10.0,
            CountdownSeconds = 3.0,
            ArenaRadius = 20f,
            YieldHealth = 0.3f,
            VictoryHealth = 0.5f,
            RequireOutOfCombat = true,
            RequireClearGround = true,
        };

        public readonly List<string> Events = new List<string>();
        public readonly List<string> Said = new List<string>();
        public readonly List<Sound.Cue> Heard = new List<Sound.Cue>();
        public readonly List<(string Winner, string Loser, EndReason Reason)> Announced = new List<(string, string, EndReason)>();

        /// <summary>What this game has taken from its opponent, as the damage hook counts it.</summary>
        public float Took;
        public int TookHits;
        public float TookBiggest;

        /// <summary>The card as the flow left it: who against, how it went, whether somebody won, and the opponent's half.</summary>
        public string CardAgainst;
        public DuelOutcome CardOutcome;
        public bool? CardDecided;
        public float? CardGave;

        public int ChallengeSounds;
        public int Celebrations;
        public int KnockDowns;

        // ---- driving it ----

        public void Challenge(Game other, float radius = 20f) => Flow.Challenge(other.Id, radius);

        /// <summary>A killing blow lands, by a player or nobody, as the death check and the death hook meet it.</summary>
        public void Fall(Game by = null, HitData.HitType type = HitData.HitType.PlayerHit, int creature = 0)
        {
            var from = by != null ? by.Id : creature;
            var blow = new Blow<int>(named: from != 0, from, type);
            Health = 0f;
            if (Flow.ShouldSurvive(local: true, blow))
            {
                Flow.YieldTo();
                return;
            }

            Standing.Dead = true;
            Events.Add("died");
            Flow.DiedOutside();
        }

        /// <summary>A blow that does not kill, counted on this side's card as the damage hook counts one.</summary>
        public void Take(float amount)
        {
            Took += amount;
            TookHits++;
            if (amount > TookBiggest) TookBiggest = amount;
            Health = Math.Max(1f, Health - amount);
        }

        /// <summary>The game stops on the spot: no more ticks, no more messages, and nothing tidied away.</summary>
        public void Crash()
        {
            Running = false;
            Events.Add("crashed");
        }

        // ---- the world the flow sees ----

        public int Me => InWorld ? Id : 0;
        public string MyName => Name;
        public double Now => _server.Now;
        DuelRules IDuelWorld<int>.Rules => Rules;
        Standing IDuelWorld<int>.Standing => Standing;
        public bool Dead => Standing.Dead;
        public bool HostileAround(float radius) => Hostile;
        public Vector3 Here => Server.At(X, Z);

        public float? DistanceTo(int other)
        {
            if (!_server.Games.TryGetValue(other, out var them) || !them.InWorld) return null;
            return Flat(them.X - X, them.Z - Z);
        }

        public Vector3? MidpointWith(int other)
        {
            if (!_server.Games.TryGetValue(other, out var them) || !them.InWorld) return null;
            return Server.At((X + them.X) / 2f, (Z + them.Z) / 2f);
        }

        public float FromCenter(Vector3 center) => Flat(X - center.x, Z - center.z);

        public bool TryFind(int other, out Sighting seen)
        {
            seen = default;
            if (!_server.Games.TryGetValue(other, out var them) || !them.InWorld) return false;
            seen = new Sighting(them.Name, them.Standing.Dead, them.Standing.PvpOn);
            return true;
        }

        public void Stamp(int opponent)
        {
            _server.Leases[Id] = new Claim<int>(opponent, Lease.UntilFor(Now));
            Events.Add("stamp");
        }

        public void SetTerms(in Lease.Terms terms) => _server.Terms[Id] = terms;

        public void ClearLease()
        {
            _server.Leases[Id] = default;
            var terms = _server.Terms.TryGetValue(Id, out var held) ? held : default;
            terms.StartAtMs = 0L;
            terms.Radius = 0f;
            _server.Terms[Id] = terms;
            Events.Add("lease cleared");
        }

        public int Agreed()
        {
            Lease.Agreed(Id, _server.Find(Id) ?? default, _server.Find, Now, SelfPair, out var opponent);
            return opponent;
        }

        bool IDuelWorld<int>.SelfPair { set => SelfPair = value; }

        public void SendChallenge(int to, float radius)
        {
            Events.Add("send challenge");
            _server.Post(to, () => _server.Games[to].Flow.ReceiveChallenge(Id, Name, radius));
        }

        public void SendReply(int to, bool accepted, in Lease.Terms terms)
        {
            Events.Add(accepted ? "send accept" : "send no");
            var sent = terms;
            _server.Post(to, () => _server.Games[to].Flow.ReceiveReply(Id, accepted, sent));
        }

        public void SendBusy(int to, Busy why)
        {
            Events.Add("send busy " + why);
            _server.Post(to, () => _server.Games[to].Flow.ReceiveBusy(Id, why));
        }

        public void SendResult(int to, EndReason reason)
        {
            Events.Add("send result " + reason);
            _server.Post(to, () => _server.Games[to].Flow.ReceiveResult(Id, reason));
        }

        public void SendTally(int to)
        {
            Events.Add("send tally");
            float took = Took, biggest = TookBiggest;
            var hits = TookHits;
            _server.Post(to, () => _server.Games[to].Flow.ReceiveTally(Id, took, hits, biggest));
        }

        public void SendAnnounce(int winner, string winnerName, string loserName, EndReason reason)
        {
            Events.Add("send announce " + reason);
            foreach (var game in _server.Games.Values)
            {
                var each = game;
                _server.Post(each.Id, () => each.Announced.Add((winnerName, loserName, reason)));
            }
        }

        public void Say(string line)
        {
            Said.Add(line);
            Events.Add("say " + line);
        }

        public void Play(Sound.Cue cue)
        {
            Heard.Add(cue);
            Events.Add("sound " + cue);
        }

        public void ChallengeSound()
        {
            ChallengeSounds++;
            Events.Add("challenge sound");
        }

        public void Lift(float fraction)
        {
            if (Recovery.Lifted(Health, MaxHealth, fraction, out var lifted)) Health = lifted;
            Events.Add("lift");
        }

        public void Settle() => Events.Add("settle");

        public void KnockDown(int awayFrom)
        {
            KnockDowns++;
            Events.Add("knocked down");
        }

        public void Celebrate()
        {
            Celebrations++;
            Events.Add("celebrate");
        }

        public void RingChanged() => Events.Add("ring");
        public void StopRehearsal() => Events.Add("rehearsal stopped");

        public void CardBegins(string opponent)
        {
            CardAgainst = opponent;
            CardOutcome = DuelOutcome.Undecided;
            CardDecided = null;
            CardGave = null;
            Took = 0f;
            TookHits = 0;
            TookBiggest = 0f;
        }

        void IDuelWorld<int>.CardOutcome(DuelOutcome outcome) => CardOutcome = outcome;

        public void CardEnds(bool decided)
        {
            CardDecided = decided;
            Events.Add("card ends");
        }

        public void CardHeard(float took, int hits, float biggest) => CardGave = took;

        private static float Flat(float dx, float dz) => (float)Math.Sqrt(dx * dx + dz * dz);
    }
}
