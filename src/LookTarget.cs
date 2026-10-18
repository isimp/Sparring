using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// Works out who you meant by "them" — the player nearest the middle of your view.
    ///
    /// A cone rather than a raycast on purpose: a raycast has to actually connect with a capsule,
    /// which makes challenging someone a matter of aim, and misses read as "nothing there" when the
    /// person is plainly in front of you. Picking the smallest angle off your look direction does
    /// what looking at someone means.
    /// </summary>
    internal static class LookTarget
    {
        /// <summary>Unity's degrees in a radian, as <c>Mathf.Rad2Deg</c> works it out.</summary>
        private const float Rad2Deg = 360f / ((float)Math.PI * 2f);

        public static Player InView(out string why)
        {
            why = null;

            var me = Player.m_localPlayer;
            if (me == null) { why = "You are not in the world."; return null; }

            var eye = me.m_eye != null ? me.m_eye.position : me.transform.position + Vector3.up * 1.5f;
            var forward = me.m_eye != null ? me.m_eye.forward : me.transform.forward;

            var others = new List<(Player Who, Vector3 At, bool Dead)>();
            foreach (var other in Player.GetAllPlayers())
            {
                if (other == null || other == me) continue;
                others.Add((other, other.transform.position, other.IsDead()));
            }

            var best = Pick(eye, forward, others, Plugin.LookRange, Plugin.LookAngle);
            if (best == null)
            {
                why = Messages.NobodyInView;
            }
            return best;
        }

        /// <summary>
        /// The living one of <paramref name="others"/> nearest the middle of the view from
        /// <paramref name="eye"/> along <paramref name="forward"/>, aimed a metre up their body,
        /// within <paramref name="range"/> metres and <paramref name="maxAngle"/> degrees; of two
        /// at the same angle, the later. Worked out as Unity's own <c>Vector3.Angle</c> and
        /// <c>magnitude</c> do, step for step, so it needs no game.
        /// </summary>
        internal static T Pick<T>(Vector3 eye, Vector3 forward, List<(T Who, Vector3 At, bool Dead)> others, float range, float maxAngle) where T : class
        {
            T best = null;
            var bestAngle = maxAngle;

            foreach (var (who, at, dead) in others)
            {
                if (dead) continue;

                float x = at.x - eye.x, y = at.y + 1f - eye.y, z = at.z - eye.z;
                if ((float)Math.Sqrt(x * x + y * y + z * z) > range) continue;

                var angle = Angle(forward, x, y, z);
                if (angle > bestAngle) continue;

                bestAngle = angle;
                best = who;
            }
            return best;
        }

        /// <summary>The angle in degrees between a direction and the vector (x, y, z), as <c>Vector3.Angle</c> gives it.</summary>
        private static float Angle(Vector3 from, float x, float y, float z)
        {
            var denominator = (float)Math.Sqrt((from.x * from.x + from.y * from.y + from.z * from.z) * (x * x + y * y + z * z));
            if (denominator < 1e-15f) return 0f;

            var dot = (from.x * x + from.y * y + from.z * z) / denominator;
            if (dot < -1f) dot = -1f;
            else if (dot > 1f) dot = 1f;
            return (float)Math.Acos(dot) * Rad2Deg;
        }
    }
}
