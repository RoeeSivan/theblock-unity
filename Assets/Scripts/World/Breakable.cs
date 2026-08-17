using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.World
{
    /// <summary>
    /// U35h - anything at the kerb that a car can knock about and the world later puts back.
    ///
    /// Two kinds share this: a <see cref="StreetProp"/> (cone, bin, bench - a real Rigidbody from
    /// the moment it spawns, asleep until touched) and a <see cref="BreakablePole"/> (a traffic
    /// light - a static collider until it is rammed, then promoted the way <see cref="LotCar"/>
    /// promotes a parked filler). What they have in common is the half that <see cref="PropSystem"/>
    /// runs: where home is, whether they have been disturbed, whether they have come to rest, and
    /// how to put them back when nobody is looking. That is the GTA answer to a city that would
    /// otherwise wear every fender-bender for the rest of the session.
    ///
    /// <b>The registry is OnEnable/OnDisable</b>, the <see cref="LotCar"/> pattern, so a domain
    /// reload or a scene reload rebuilds it without a static reset hook.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class Breakable : MonoBehaviour
    {
        /// <summary>The layer name every cone, bin and bench sits on, and a felled pole moves to.</summary>
        public const string PropsLayerName = "Props";

        private static int _propsLayer = -2;

        /// <summary>
        /// The Props layer index, or -1 if the project has no such layer. Cached on first read.
        /// <c>CrashSensor</c>, <c>TrafficCar</c> and <c>LotCar</c> early-out on it - a 3 kg cone
        /// must not be a crash, a crime, a dent, or a wrecked traffic car.
        /// </summary>
        public static int PropsLayer
        {
            get
            {
                if (_propsLayer == -2) _propsLayer = LayerMask.NameToLayer(PropsLayerName);
                return _propsLayer;
            }
        }

        /// <summary>Where it stood when it was placed. Written once, by whoever spawned it.</summary>
        public Vector3 HomePosition { get; private set; }
        public Quaternion HomeRotation { get; private set; }

        /// <summary>Has it left home since it was last put back?</summary>
        public bool Disturbed { get; protected set; }

        /// <summary><c>Time.time</c> it was disturbed. The awake cap sleeps the oldest first.</summary>
        public float DisturbedAt { get; protected set; }

        /// <summary>The body, while it has one. Null for a pole that is still standing.</summary>
        public Rigidbody Body { get; protected set; }

        /// <summary>At rest, as far as PhysX is concerned - or with no body to be otherwise.</summary>
        public bool IsSettled => Body == null || Body.IsSleeping();

        /// <summary>
        /// <c>Time.time</c> the tick first found it settled, or -1 while it is still moving.
        /// Owned by <see cref="PropSystem"/>'s tick, kept here so it needs no side table.
        /// </summary>
        public float SettledSince { get; set; } = -1f;

        private Renderer _renderer;

        /// <summary>World bounds of the first renderer, for the frustum test. Cheap: one renderer.</summary>
        public Bounds WorldBounds
        {
            get
            {
                if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
                return _renderer != null ? _renderer.bounds : new Bounds(transform.position, Vector3.one);
            }
        }

        /// <summary>Records the current transform as home. Called by the spawner right after placing.</summary>
        public void SetHome()
        {
            HomePosition = transform.position;
            HomeRotation = transform.rotation;
        }

        /// <summary>
        /// Is the home spot free of anything that is not another prop? A restore that lands a
        /// collider inside a parked car is a depenetration pop under the player; the tick retries
        /// next time instead. Implementations test a SHRUNKEN shape so the ground itself is never
        /// a hit.
        /// </summary>
        public abstract bool HomeIsClear();

        /// <summary>Back to home, upright, at rest, undisturbed. The caller has already checked <see cref="HomeIsClear"/>.</summary>
        public abstract void Restore();

        /// <summary>Marks the moment it first left home. Idempotent until <see cref="Restore"/>.</summary>
        protected void MarkDisturbed()
        {
            if (Disturbed) return;
            Disturbed = true;
            DisturbedAt = Time.time;
        }

        protected void ClearDisturbed() => Disturbed = false;

        // --- the registry -------------------------------------------------------------------------

        private static readonly List<Breakable> Live = new();

        /// <summary>Every breakable currently enabled - props and promoted poles alike.</summary>
        public static IReadOnlyList<Breakable> All => Live;

        protected virtual void OnEnable() => Live.Add(this);

        protected virtual void OnDisable() => Live.Remove(this);
    }
}
