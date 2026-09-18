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
    public static class LookTarget
    {
        public static Player InView(out string why)
        {
            why = null;

            var me = Player.m_localPlayer;
            if (me == null) { why = "You are not in the world."; return null; }

            var eye = me.m_eye != null ? me.m_eye.position : me.transform.position + Vector3.up * 1.5f;
            var forward = me.m_eye != null ? me.m_eye.forward : me.transform.forward;

            Player best = null;
            var bestAngle = Plugin.LookAngle;

            foreach (var other in Player.GetAllPlayers())
            {
                if (other == null || other == me || other.IsDead()) continue;

                var toward = other.transform.position + Vector3.up * 1f - eye;
                if (toward.magnitude > Plugin.LookRange) continue;

                var angle = Vector3.Angle(forward, toward);
                if (angle > bestAngle) continue;

                bestAngle = angle;
                best = other;
            }

            if (best == null)
            {
                why = Messages.NobodyInView;
            }
            return best;
        }
    }
}
