using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sparring
{
    /// <summary>How the edge of the arena is shown.</summary>
    public enum RingStyle
    {
        /// <summary>Posts placed around the edge.</summary>
        Stakes,

        /// <summary>A drawn circle. Always available, since it needs no prefab to exist.</summary>
        Line,
    }

    /// <summary>
    /// The ring on the ground and the marker at its middle, for every duel in sight.
    ///
    /// Nothing here is part of the world. The centre and radius travel on the duellists' ZDOs as
    /// part of the lease, so each client reads the same two numbers and builds its own copy: no
    /// ZNetView, no ZDO, nothing spawned. Networked pieces would be world state the lease cannot
    /// expire, and a crash mid-duel would leave them behind. These go with the duel, or at worst
    /// with the process. Players without Sparring see no ring.
    /// </summary>
    public static class ArenaRing
    {
        private const int Segments = 72;
        private const float MarkerHeight = 6f;
        private const float StakeSpacing = 4f;
        private const float StakeSink = 0.3f;

        /// <summary>How far the drawn line sits above the ground, to keep it out of the surface.</summary>
        private const float LineLift = 0.12f;

        private const float SyncInterval = 0.4f;
        private const float SeeingDistance = 250f;

        private static GameObject _root;
        private static GameObject _preview;
        private static Material _material;
        private static bool _materialTried;

        private static readonly Dictionary<ZDOID, GameObject> _rings = new Dictionary<ZDOID, GameObject>();
        private static readonly List<ZDOID> _live = new List<ZDOID>();
        private static readonly List<ZDOID> _gone = new List<ZDOID>();
        private static float _next;

        /// <summary>
        /// Draws a ring around every duel in sight, not only your own. Needs no network traffic of
        /// its own: the centre and radius are already on both fighters' ZDOs.
        /// </summary>
        public static void Sync()
        {
            if (Time.time < _next) return;
            _next = Time.time + SyncInterval;

            if (!Plugin.ShowArenaRing || ZNet.instance == null || ZDOMan.instance == null)
            {
                HideAll();
                return;
            }

            var me = Player.m_localPlayer;
            if (me == null) { HideAll(); return; }

            _live.Clear();

            foreach (var player in Player.GetAllPlayers())
            {
                if (player == null) continue;
                if (!Lease.Corroborated(player, out var foe)) continue;

                // One ring per pair, not one per fighter: keyed on whichever of the two sorts
                // first, so both sides of the same duel resolve to the same entry.
                var mine = player.GetZDOID();
                var key = mine.CompareTo(foe) <= 0 ? mine : foe;
                if (_live.Contains(key)) continue;

                var terms = Lease.ReadTerms(player);
                if (terms.Radius <= 0f) continue;
                if (Vector3.Distance(me.transform.position, terms.Center) > SeeingDistance) continue;

                _live.Add(key);
                if (_rings.ContainsKey(key)) continue;

                var ring = Build(terms, "SparringArena");
                if (ring != null) _rings[key] = ring;
            }

            Prune();
        }

        /// <summary>Takes down rings whose duel has ended, or which we have walked away from.</summary>
        private static void Prune()
        {
            if (_rings.Count == 0) return;

            _gone.Clear();
            foreach (var entry in _rings)
            {
                if (!_live.Contains(entry.Key)) _gone.Add(entry.Key);
            }

            foreach (var key in _gone)
            {
                if (_rings[key] != null) UnityEngine.Object.Destroy(_rings[key]);
                _rings.Remove(key);
            }
            _gone.Clear();
        }

        /// <summary>Asks for the next scan to happen now rather than on the usual beat.</summary>
        public static void Refresh() => _next = 0f;

        public static void HideAll()
        {
            foreach (var ring in _rings.Values)
            {
                if (ring != null) UnityEngine.Object.Destroy(ring);
            }
            _rings.Clear();
            _live.Clear();
        }

        /// <summary>A ring with no duel behind it, for walking a spot before you fight on it.</summary>
        public static void ShowPreview(Lease.Terms terms)
        {
            HidePreview();
            if (!Plugin.ShowArenaRing || terms.Radius <= 0f) return;

            _preview = Build(terms, "SparringPreview");
        }

        public static void HidePreview()
        {
            if (_preview == null) return;

            UnityEngine.Object.Destroy(_preview);
            _preview = null;
        }

        private static GameObject Build(Lease.Terms terms, string name)
        {
            try
            {
                var material = Paint();
                if (material == null) return null;

                _root = new GameObject(name);
                _root.transform.position = terms.Center;

                BuildRing(material, terms);
                BuildMarker(material, terms);

                var built = _root;
                _root = null;
                return built;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Sparring could not draw the arena ring: {ex.Message}");
                if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; }
                return null;
            }
        }

        /// <summary>
        /// Drops the remembered prefabs so the next ring looks them up again. The probe result is
        /// cached to keep it off the hot path, which would otherwise mean a name typed into the
        /// config did nothing until the game was restarted.
        /// </summary>
        public static void ForgetPrefabs()
        {
            _post = null; _postTried = false;
            _stake = null; _stakeTried = false;
        }

        /// <summary>
        /// The ring itself, laid on the ground rather than floating at the height the duel happened
        /// to be struck at. Sampled once — the arena does not move, so there is nothing to keep
        /// re-sampling every frame.
        ///
        /// The heights follow the *terrain* and are then smoothed. Boulders and buildings are not
        /// ground: draping over whatever solid thing a ray happens to hit sends a segment up the
        /// side of a rock and leaves a spike standing over it.
        /// </summary>
        private static void BuildRing(Material material, Lease.Terms terms)
        {
            if (Plugin.RingStyle == RingStyle.Stakes && BuildStakes(terms)) return;

            var points = Circle(terms, Segments, smooth: true);
            var line = NewLine(material, "Ring", Plugin.RingWidth);
            line.loop = true;
            line.positionCount = points.Length;

            for (var i = 0; i < points.Length; i++)
            {
                points[i].y += LineLift;
                line.SetPosition(i, points[i]);
            }
        }

        /// <summary>
        /// A ring of stakes instead of a drawn circle. Spacing is fixed and the count follows from
        /// the circumference, so small and large rings look equally dense. Returns false if there
        /// is no prefab to place, and the caller draws the line instead.
        /// </summary>
        private static bool BuildStakes(Lease.Terms terms)
        {
            var prefab = Stake();
            if (prefab == null) return false;

            var count = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * terms.Radius / StakeSpacing), 8, 48);

            // Unsmoothed, and sunk a little. Smoothing averages each point against its neighbours,
            // which is what a drawn circle wants — it should not kink over a crag — but it is
            // exactly wrong for objects: a stake has to meet the ground it is actually standing on,
            // and an averaged height leaves the ones on low ground hanging in the air. The sink
            // buries the base so it reads as driven in rather than resting on the surface.
            var points = Circle(terms, count, smooth: false);
            for (var i = 0; i < points.Length; i++) points[i].y -= StakeSink;

            var wasDisabled = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            try
            {
                for (var i = 0; i < points.Length; i++)
                {
                    // A little yaw on each so a ring of identical posts does not read as a fence.
                    var turn = Quaternion.Euler(0f, i * 37.5f % 360f, 0f);
                    var stake = UnityEngine.Object.Instantiate(prefab, points[i], turn, _root.transform);
                    Strip(stake);
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Sparring could not plant the ring stakes: {ex.Message}");
                return false;
            }
            finally
            {
                ZNetView.m_forceDisableInit = wasDisabled;
            }
        }

        /// <summary>
        /// Points evenly around the ring, sitting on the terrain. Shared by both styles, but not
        /// smoothed for both: see <see cref="BuildStakes"/> for why objects want the raw height and
        /// a drawn line does not.
        /// </summary>
        private static Vector3[] Circle(Lease.Terms terms, int count, bool smooth)
        {
            var points = new Vector3[count];
            var heights = new float[count];

            for (var i = 0; i < count; i++)
            {
                var angle = i / (float)count * Mathf.PI * 2f;
                points[i] = terms.Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * terms.Radius;
                heights[i] = GroundAt(points[i]);
            }

            if (smooth)
            {
                // Smoothed, but never below the ground it was measured from.
                //
                // Averaging cuts both ways: it lifts a point sitting in a dip, which is what stops
                // a drawn circle kinking, and it drags down a point sitting on a rise, which buries
                // that stretch of line inside the hill. Taking the higher of the two keeps the
                // bridging and loses the burying — the line floats a little over hollows, which is
                // what a taut line does anyway, and still rests on every high point it crosses.
                var raw = (float[])heights.Clone();
                Smooth(heights);
                for (var i = 0; i < count; i++) heights[i] = Mathf.Max(heights[i], raw[i]);
            }

            for (var i = 0; i < count; i++) points[i].y = heights[i];
            return points;
        }

        /// <summary>
        /// A configured name first, then the ones known to work, so a setting is a preference
        /// rather than a cliff. A name that stops resolving — a game update moving a prefab, a
        /// typo — falls through to something that does instead of leaving a bare ring.
        /// </summary>
        private static string[] Candidates(string named, params string[] known)
        {
            if (string.IsNullOrEmpty(named)) return known;

            var all = new string[known.Length + 1];
            all[0] = named;
            Array.Copy(known, 0, all, 1, known.Length);
            return all;
        }

        /// <summary>The prefab to plant around the edge. Named in config, or probed like the centre one.</summary>
        private static GameObject Stake()
        {
            if (_stake != null || _stakeTried) return _stake;
            _stakeTried = true;

            var scene = ZNetScene.instance;
            if (scene == null) { _stakeTried = false; return null; }

            var candidates = Candidates(Plugin.RingMarker,
                "dvergrtown_wood_stake", "wood_stake", "wood_pole", "wood_pole2");

            foreach (var name in candidates)
            {
                var found = scene.GetPrefab(name);
                if (found == null) continue;
                if (!Scenery(found, name)) continue;

                Plugin.Log.LogInfo($"Sparring is marking the ring with \"{name}\".");
                _stake = found;
                return _stake;
            }

            Plugin.Log.LogWarning("Sparring found no prefab to stake the ring with, so it is drawing a line instead. /duel prefabs finds one for RingMarker.");
            return null;
        }

        private static GameObject _stake;
        private static bool _stakeTried;

        /// <summary>
        /// Rounds off the ring's profile. Terrain alone still has cliffs and crags in it, and a
        /// single sample landing on one puts a kink in an otherwise smooth circle. Two weighted
        /// passes around the loop flatten a lone outlier while leaving a genuine slope alone — the
        /// ring should climb a hillside, it just should not jump.
        /// </summary>
        private static void Smooth(float[] heights)
        {
            var working = new float[heights.Length];

            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < heights.Length; i++)
                {
                    var before = heights[(i - 1 + heights.Length) % heights.Length];
                    var after = heights[(i + 1) % heights.Length];
                    working[i] = (before + heights[i] * 2f + after) * 0.25f;
                }
                Array.Copy(working, heights, heights.Length);
            }
        }

        /// <summary>
        /// The marker in the middle of the ring: one of the game's own objects, or a vertical line
        /// when none resolves.
        ///
        /// <c>ZNetView.m_forceDisableInit</c> makes the network view destroy itself in Awake, which
        /// is how the game spawns its own build ghost. What is left is a model with no ZDO, no
        /// owner and nothing on the wire. It cannot be interacted with, exists only on this client,
        /// and goes with the duel or the process.
        /// </summary>
        private static void BuildMarker(Material material, Lease.Terms terms)
        {
            var foot = terms.Center;
            foot.y = GroundAt(foot);

            // One switch for the whole look: Line means the plain drawn shapes, centre included.
            var prefab = Plugin.RingStyle == RingStyle.Stakes ? Post() : null;
            if (prefab != null)
            {
                var wasDisabled = ZNetView.m_forceDisableInit;
                ZNetView.m_forceDisableInit = true;
                try
                {
                    var post = UnityEngine.Object.Instantiate(prefab, foot, Quaternion.identity, _root.transform);
                    Strip(post);
                    return;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Sparring could not raise the centre marker: {ex.Message}");
                }
                finally
                {
                    ZNetView.m_forceDisableInit = wasDisabled;
                }
            }

            // No prefab resolved, so fall back to a drawn line.
            var line = NewLine(material, "Centre", Plugin.RingWidth * 1.5f);
            line.positionCount = 2;
            line.SetPosition(0, foot);
            line.SetPosition(1, foot + Vector3.up * MarkerHeight);
        }

        /// <summary>
        /// Whether a prefab is usable as scenery. Marker names are free text, and naming a creature
        /// would instantiate its Character and behaviours on an object the world does not know
        /// about. So a marker must have something to render and no Character or AI. That
        /// still admits banners, stakes, poles, torches and rocks, and a refusal is logged by name.
        /// </summary>
        private static bool Scenery(GameObject prefab, string name)
        {
            if (prefab.GetComponentInChildren<Renderer>(true) == null)
            {
                Plugin.Log.LogWarning($"Sparring will not use \"{name}\" as a marker: it has nothing to draw.");
                return false;
            }

            if (prefab.GetComponentInChildren<Character>(true) != null
                || prefab.GetComponentInChildren<BaseAI>(true) != null)
            {
                Plugin.Log.LogWarning($"Sparring will not use \"{name}\" as a marker: it is a creature, not scenery.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Takes the working parts off a copy that is only meant to be looked at. A piece carries
        /// colliders you could walk into, wear that could be knocked down, and hover text offering
        /// interactions that would do nothing — all of it attached to an object the rest of the
        /// world does not know exists.
        /// </summary>
        private static void Strip(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                // Anything that would tick, break, or offer itself to the player.
                if (b is WearNTear || b is Piece || b is Hoverable || b is Interactable || b is Destructible)
                {
                    UnityEngine.Object.Destroy(b as UnityEngine.Object);
                }
            }
            foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) r.isKinematic = true;
        }

        /// <summary>
        /// The prefab to stand in the middle. A named one from the config wins; otherwise the usual
        /// suspects are tried in order and the first that resolves is kept.
        ///
        /// Probed rather than hard-coded because prefab names live in the game's asset bundles, not
        /// in any assembly we can read — there is no way to know from outside the running game
        /// which of these exist. <c>/duel prefabs &lt;text&gt;</c> lists what is actually there.
        /// </summary>
        private static GameObject Post()
        {
            if (_post != null || _postTried) return _post;
            _postTried = true;

            var scene = ZNetScene.instance;
            if (scene == null) { _postTried = false; return null; }

            var candidates = Candidates(Plugin.CentreMarker,
                "CharredBanner2", "CharredBanner1", "piece_groundtorch_wood", "wood_pole2", "wood_pole");

            foreach (var name in candidates)
            {
                var found = scene.GetPrefab(name);
                if (found == null) continue;
                if (!Scenery(found, name)) continue;

                Plugin.Log.LogInfo($"Sparring is marking the arena centre with \"{name}\".");
                _post = found;
                return _post;
            }

            Plugin.Log.LogWarning("Sparring found no centre-marker prefab, not even its fallbacks; drawing a plain beam. /duel prefabs finds one for CentreMarker.");
            return null;
        }

        private static GameObject _post;
        private static bool _postTried;

        private static LineRenderer NewLine(Material material, string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, worldPositionStays: true);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.material = material;
            line.startColor = Plugin.RingColor;
            line.endColor = Plugin.RingColor;
            line.startWidth = width;
            line.endWidth = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Tile;
            line.numCapVertices = 2;
            return line;
        }

        /// <summary>
        /// Terrain height, deliberately. <c>GetGroundHeight</c> casts against the terrain mask and
        /// so passes straight through rocks, trees and buildings; <c>GetSolidHeight</c> stops at
        /// the first of them, which is the right answer for standing on something and the wrong one
        /// for drawing a line on the ground. Solid is kept only as a fallback for the rare spot the
        /// terrain ray misses entirely.
        /// </summary>
        private static float GroundAt(Vector3 point)
        {
            var zones = ZoneSystem.instance;
            if (zones != null)
            {
                if (zones.GetGroundHeight(point, out var ground)) return ground;
                if (zones.GetSolidHeight(point, out var solid)) return solid;
            }
            return point.y;
        }

        /// <summary>
        /// A material to draw with. Which shaders survive into a shipped Unity build depends on
        /// what the build referenced, so rather than assume one, try the usual candidates in order
        /// and remember what worked. If none of them resolve we draw nothing, markers included. The
        /// duel still ends at the edge of the arena — a missing ring is a cosmetic loss, not a
        /// broken duel, so this fails soft rather than throwing.
        /// </summary>
        private static Material Paint()
        {
            if (_materialTried) return _material;
            _materialTried = true;

            string[] candidates =
            {
                "Particles/Standard Unlit",
                "Legacy Shaders/Particles/Alpha Blended",
                "Sprites/Default",
                "Unlit/Color",
                "Hidden/Internal-Colored",
            };

            foreach (var name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;

                _material = new Material(shader);
                Plugin.Log.LogInfo($"Sparring is drawing the arena ring with \"{name}\".");
                return _material;
            }

            Plugin.Log.LogWarning("Sparring found no usable shader for the arena ring, so it will not be drawn. The duel still ends when a fighter leaves the arena.");
            return null;
        }
    }
}
