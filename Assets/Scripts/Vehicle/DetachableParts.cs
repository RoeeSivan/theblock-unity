using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// U35b layer ③: the pieces of a car that a hard enough crash knocks off - the car's half of it.
    ///
    /// <b>No Blender work, and the GLBs are the reason.</b> The three lot cars went through the web
    /// build's <c>merge-car-meshes.py</c>, which welded the shell into one mesh but left the door, the
    /// mirror and a window as their own nodes: <c>Door_R</c> and <c>Mirror_R</c> on the Audi,
    /// <c>Door_R</c> and <c>mirror</c> on the Avenger, <c>door_dside_f</c> on the Tesla. The police
    /// CrownVic keeps a separate <c>Roof light bar_0</c>. Those are real parts, already split, already
    /// origined, already in LFS - so this ships against them.
    ///
    /// <b>The Mustang sheds nothing, and that is a fact about its .glb, not a bug.</b> Its eighteen
    /// nodes are one per MATERIAL, each spanning the whole car; there is no bumper node to detach.
    /// Splitting it is a Blender job with a real risk attached (<c>CarBuilder</c> rebinds the paint by
    /// the material name <c>CarPrimaryColor</c>, and a re-export that renames anything breaks the car
    /// in every screenshot), so it was weighed and declined for this unit. <c>CarBuilder</c> logs
    /// "parts none" for the Mustang rather than staying quiet about it.
    ///
    /// <b>One part per impact, the nearest.</b> A crash that sheds a door and both mirrors at once
    /// reads as a car disassembling itself, not as damage.
    /// </summary>
    [DisallowMultipleComponent]
    public class DetachableParts : MonoBehaviour
    {
        [Tooltip("Nodes that may come off. Wired by CarBuilder from a per-model name table.")]
        [SerializeField] private Transform[] parts = System.Array.Empty<Transform>();

        [Tooltip("Metres from the contact point within which a part is considered struck.")]
        [SerializeField] private float reach = 1.7f;

        [Tooltip("Kilograms given to a shed part. A door is heavy enough not to skitter like a can.")]
        [SerializeField] private float partMass = 22f;

        [Tooltip("Seconds a shed part lies in the road before it parks itself back on the car, " +
                 "switched off. The U35b row's number.")]
        [SerializeField] private float partLife = 20f;

        [Tooltip("Extra metres per second away from the car, on top of the car's own velocity. " +
                 "Without it a part shed at a standstill just drops.")]
        [SerializeField] private float kick = 2.6f;

        [Tooltip("The door script, if this car has one. A shed door must stop being animated.")]
        [SerializeField] private CarDoor door;

        private readonly List<DetachedPart> _shed = new();
        private Rigidbody _body;

        private void Awake() => TryGetComponent(out _body);

        /// <summary>Editor-side wiring, used by <c>CarBuilder</c>.</summary>
        public void Configure(Transform[] detachable, CarDoor carDoor)
        {
            parts = detachable ?? System.Array.Empty<Transform>();
            door = carDoor;
        }

        /// <summary>How many parts this car could ever shed. Read by the build log.</summary>
        public int PartCount => parts != null ? parts.Length : 0;

        /// <summary>Is anything currently off this car?</summary>
        public bool AnyShed => _shed.Count > 0;

        /// <summary>
        /// Knock the nearest part off, if the impact landed near one.
        ///
        /// Returns whether anything came away, so the caller can decide the crash was worth a sound.
        /// </summary>
        public bool Shed(Vector3 worldPoint, Vector3 worldNormal, float severity)
        {
            if (parts == null || parts.Length == 0) return false;

            Transform best = null;
            float bestDistance = reach * reach;

            foreach (var part in parts)
            {
                // Null is the ordinary state for an already-shed part: it has been reparented out of
                // the car, and this array is checked by reference rather than by name.
                if (part == null || !part.gameObject.activeSelf) continue;
                if (part.GetComponent<DetachedPart>() != null) continue;   // already off

                // The part's RENDERER centre, not its transform: a Sketchfab node's origin is
                // wherever the artist left it, which for a door is often the car's own centre.
                var anchor = part.TryGetComponent<Renderer>(out var renderer)
                    ? renderer.bounds.center
                    : part.position;

                float distance = (anchor - worldPoint).sqrMagnitude;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = part;
            }

            if (best == null) return false;
            return Detach(best, worldNormal, severity);
        }

        private bool Detach(Transform part, Vector3 worldNormal, float severity)
        {
            var home = part.parent;
            var localPosition = part.localPosition;
            var localRotation = part.localRotation;
            var localScale = part.localScale;

            // The door node IS CarDoor's joint on all three lot cars, so a shed door would otherwise
            // leave the open/close animation writing a rotation onto a transform lying in the road.
            if (door != null && door.IsJoint(part)) door.enabled = false;

            part.SetParent(null, true);   // keeps the world pose - the part does not jump as it comes off

            // The box is taken from the MESH's own local bounds, never from the renderer's world
            // AABB: a world box carries the car's yaw, and un-rotating it through the part's
            // transform gives a box that is too big in two axes and wrong in all three.
            var box = part.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null
                ? filter.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one * 0.4f);

            var collider = part.gameObject.AddComponent<BoxCollider>();
            collider.center = box.center;
            collider.size = box.size;

            var body = part.gameObject.AddComponent<Rigidbody>();
            body.mass = partMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // The car's own velocity plus a push along the crash. Inheriting the velocity is what
            // makes a part shed at 60 km/h tumble down the road behind you instead of appearing to
            // be dropped by a car that has already gone.
            //
            // NEGATED, unlike the dent - and the two really do differ. Unity's contact normal points
            // from the other collider INTO this car, which is the direction bodywork caves; a piece
            // that comes off goes the other way, out and away from the thing that hit it.
            var carVelocity = _body != null ? _body.linearVelocity : Vector3.zero;
            body.linearVelocity = carVelocity + -worldNormal.normalized * (kick * (0.5f + severity)) + Vector3.up * 1.2f;
            body.angularVelocity = Random.onUnitSphere * (3f + 4f * severity);

            var shed = part.gameObject.AddComponent<DetachedPart>();
            shed.Configure(this, home, localPosition, localRotation, localScale, partLife);
            _shed.Add(shed);
            DamageBudget.TakePart(shed);
            return true;
        }

        /// <summary>A part has parked itself back on the car and switched off.</summary>
        internal void NoteParked(DetachedPart part) => _shed.Remove(part);

        /// <summary>
        /// Repair: everything that came off goes back on, visible, and the door works again.
        ///
        /// Iterated over a copy, because <see cref="DetachedPart.Reattach"/> takes itself out of this
        /// list on the way through.
        /// </summary>
        public void Repair()
        {
            if (_shed.Count > 0)
            {
                var pending = _shed.ToArray();
                _shed.Clear();
                foreach (var part in pending) if (part != null) part.Reattach();
            }

            // Parts that expired on their own clock are parked and switched off, not restored - they
            // are children of this car again, so a sweep finds them without a list to trust.
            if (parts != null)
                foreach (var part in parts)
                    if (part != null && !part.gameObject.activeSelf) part.gameObject.SetActive(true);

            if (door != null) door.enabled = true;
        }
    }
}
