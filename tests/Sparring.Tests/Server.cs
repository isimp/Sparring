using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sparring.Tests
{
    /// <summary>
    /// A server and the players' games on it, for duels without the game: one clock in server
    /// seconds, every player's lease as their ZDO would hold it, and the messages between games,
    /// which arrive when <see cref="Deliver"/> or <see cref="Pass"/> lets them. Each player's game
    /// runs Sparring's own duel flow; what the game would do (play a sound, lift health, put a line
    /// on screen) is written down for the tests to read.
    /// </summary>
    internal sealed class Server
    {
        public double Now = 1000.0;

        public readonly Dictionary<int, Game> Games = new Dictionary<int, Game>();
        public readonly Dictionary<int, Claim<int>> Leases = new Dictionary<int, Claim<int>>();
        public readonly Dictionary<int, Lease.Terms> Terms = new Dictionary<int, Lease.Terms>();

        /// <summary>Messages sent and not yet arrived, in the order they were sent.</summary>
        public readonly Queue<(int To, Action Arrive)> InFlight = new Queue<(int To, Action Arrive)>();

        public Game Join(int id, string name, float x = 0f, float z = 0f)
        {
            var game = new Game(this, id, name) { X = x, Z = z };
            Games[id] = game;
            return game;
        }

        /// <summary>Hands a message to the network; it arrives on the next <see cref="Deliver"/>, if the player is still there.</summary>
        public void Post(int to, Action arrive) => InFlight.Enqueue((to, arrive));

        /// <summary>While set, messages stay in flight: the network is slow, and only leases and time move.</summary>
        public bool Holding;

        /// <summary>Lets every message in flight arrive, and those they send in turn, until nothing is left.</summary>
        public void Deliver()
        {
            if (Holding) return;

            var guard = 0;
            while (InFlight.Count > 0)
            {
                if (++guard > 1000) throw new InvalidOperationException("messages kept answering each other");
                var (to, arrive) = InFlight.Dequeue();
                if (Games.TryGetValue(to, out var game) && game.Running) arrive();
            }
        }

        /// <summary>Lets time pass, every running game ticking each step as Update does, with messages arriving between steps.</summary>
        public void Pass(double seconds, double step = 0.25)
        {
            var until = Now + seconds;
            while (Now < until - 1e-9)
            {
                Now = Math.Min(until, Now + step);
                foreach (var game in new List<Game>(Games.Values))
                {
                    if (game.Running) game.Flow.Tick();
                }
                Deliver();
            }
        }

        /// <summary>A player's lease as the server holds it; none for one never written.</summary>
        public Claim<int>? Find(int id) => Leases.TryGetValue(id, out var claim) ? claim : (Claim<int>?)null;

        /// <summary>
        /// Whether a blow from one player reaches the other, as the game's attack filter and hit
        /// check ask it of Sparring: both leases agreeing on the pair, and the attacker's countdown
        /// over.
        /// </summary>
        public bool Lands(Game attacker, Game victim)
        {
            if (!Lease.Paired(attacker.Id, victim.Id, Find, Now, attacker.SelfPair)) return false;
            return Terms.TryGetValue(attacker.Id, out var terms) && terms.BegunAt(Now);
        }

        /// <summary>A point on the ground, as a position the flow carries but never works out itself.</summary>
        public static Vector3 At(float x, float z)
        {
            var point = default(Vector3);
            point.x = x;
            point.z = z;
            return point;
        }
    }
}
