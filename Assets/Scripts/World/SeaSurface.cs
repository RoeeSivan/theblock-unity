using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// Where the water actually IS, this frame, at a given point — the CPU's copy of the swell that
    /// <c>Water.shader</c> displaces its vertices by.
    ///
    /// <b>Why this has to exist.</b> U12 gave the sea three summed swells in the vertex stage
    /// (amplitudes 0.18 + 0.12 + 0.07 = up to 0.37 m of crest) because a dead-flat plane reads as
    /// lino. Nothing on the CPU knew: <see cref="SeaGeometry"/> answers <c>sea.Level</c>, which is
    /// the water's MEAN height, and everything floating was placed against that. A buoy whose hull
    /// starts exactly at the mean is under water for half of every wave — which is precisely how it
    /// was reported ("the buoys are a bit lower"). The port's own standing rule caught this late:
    /// when a unit gains a mechanism, re-measure everything that was accepted against the old one.
    ///
    /// <b>It reads the MATERIAL, not a copy of the numbers.</b> Every wave parameter is already on
    /// <c>Water.mat</c>, written there by <c>WorldBuilder.Sea</c> from <c>config.sea.surface</c>. A
    /// second hand-kept table of amplitudes here would be a fourth place for the sea's shape to
    /// live, and the first to drift. If the shader and this file ever disagree, the shader is what
    /// the player sees — so the shader's own inputs are the source.
    ///
    /// Static and lazily bound: it is asked for by missions that may run before or after the world
    /// is built, and it holds nothing but a material reference.
    /// </summary>
    public static class SeaSurface
    {
        private static Material _water;
        private static bool _searched;

        // The shader's inputs, resolved once.
        private static Vector4 _wave0, _wave1, _wave2, _speeds;
        private static float _level, _shoreX, _wadeRun, _deepY, _fadeDepth;

        private const float TwoPi = Mathf.PI * 2f;

        /// <summary>Was a water material found? False means <see cref="Height"/> is just the level.</summary>
        public static bool Bound => Resolve() != null;

        /// <summary>The mean water height — what <c>sea.Level</c> is, with no swell on it.</summary>
        public static float Level => Resolve() == null ? 0f : _level;

        /// <summary>
        /// The drawn water height at a world XZ, right now. This is the number to float something
        /// on; <c>sea.Level</c> is the number to compare depths against.
        /// </summary>
        public static float Height(float x, float z) => Height(x, z, Time.timeSinceLevelLoad);

        /// <summary>Explicit-time overload, so a measurement can ask for a named instant.</summary>
        public static float Height(float x, float z, float time)
        {
            if (Resolve() == null) return _level;

            // The shader fades the swell out in the shallows so a wave never lifts above the sand.
            // Same fade here, or a buoy near the beach would ride a wave the water does not have.
            var depth = _level - Seabed(x);
            var fade = Mathf.Clamp01(depth / Mathf.Max(0.0001f, _fadeDepth));

            var y = Wave(_wave0, _speeds.x, x, z, time)
                    + Wave(_wave1, _speeds.y, x, z, time)
                    + Wave(_wave2, _speeds.z, x, z, time);

            return _level + y * fade;
        }

        /// <summary>The largest crest the swell can produce — every amplitude in phase.</summary>
        public static float MaxCrest =>
            Resolve() == null ? 0f : Mathf.Abs(_wave0.z) + Mathf.Abs(_wave1.z) + Mathf.Abs(_wave2.z);

        private static float Wave(Vector4 w, float speed, float x, float z, float time)
        {
            var dir = new Vector2(w.x, w.y);
            var length = Mathf.Max(0.0001f, dir.magnitude);
            dir /= length;

            var k = TwoPi / Mathf.Max(0.0001f, w.w);
            return w.z * Mathf.Sin((dir.x * x + dir.y * z) * k + time * speed * k);
        }

        /// <summary>The shader's <c>Seabed()</c>, which is itself a copy of SeaGeometry's ramp.</summary>
        private static float Seabed(float x)
        {
            if (x <= _shoreX) return 0f;
            return _deepY * Mathf.Min((x - _shoreX) / Mathf.Max(0.0001f, _wadeRun), 1f);
        }

        private static Material Resolve()
        {
            if (_searched) return _water;
            _searched = true;

            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var material = renderer.sharedMaterial;
                if (material == null || material.shader == null) continue;
                if (material.shader.name != "TheBlock/Water") continue;

                _water = material;
                _wave0 = material.GetVector("_Wave0");
                _wave1 = material.GetVector("_Wave1");
                _wave2 = material.GetVector("_Wave2");
                _speeds = material.GetVector("_WaveSpeeds");
                _level = material.GetFloat("_Level");
                _shoreX = material.GetFloat("_ShoreX");
                _wadeRun = material.GetFloat("_WadeRun");
                _deepY = material.GetFloat("_DeepY");
                _fadeDepth = material.GetFloat("_SwellFadeDepth");
                break;
            }

            return _water;
        }

        /// <summary>
        /// A domain reload keeps statics only when it does not happen; entering Play with reload
        /// disabled would otherwise hand the next session a material from the last one.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => ResetCaches();

        /// <summary>
        /// The same drop, callable. This one is the sharpest of the session-scoped caches:
        /// <c>_searched</c> latches true, so after a scene reload the component reference is a
        /// destroyed object that reads as non-null AND nothing will ever look for the new one. See
        /// <see cref="Core.SessionReset"/>.
        /// </summary>
        public static void ResetCaches()
        {
            _water = null;
            _searched = false;
        }
    }
}
