using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// A door, a mirror or a light bar that has come off a car and is now its own rigid body -
    /// U35b layer ③, the piece's own half of it.
    ///
    /// <b>It never destroys itself, and that is the whole design.</b> The obvious implementation
    /// deletes the part when its clock runs out, and then a repaired car is missing a door forever:
    /// the node came out of the .glb and there is nowhere to get another one without instantiating
    /// the whole model again. Instead the part goes HOME on expiry - back to its recorded parent and
    /// local pose - and switches itself off. Repair is then one <c>SetActive(true)</c>, and a car that
    /// respawns is whole because the door never left.
    ///
    /// A <c>BoxCollider</c> off the mesh bounds rather than a convex <c>MeshCollider</c>: cooking a
    /// hull from an 11.6k-vertex door mid-crash is a hitch, and nothing about a bumper sliding down
    /// the road needs the true shape.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <c>[RequireComponent(typeof(Rigidbody))]</c>, though it always has one:
    /// Unity refuses to destroy a component another component declares a dependency on, so the
    /// attribute would make <see cref="Strip"/> log an error and leave a rigidbody on a door that is
    /// back on the car.
    /// </remarks>
    public class DetachedPart : MonoBehaviour
    {
        private DetachableParts _owner;
        private Transform _home;
        private Vector3 _localPosition;
        private Quaternion _localRotation;
        private Vector3 _localScale;
        private float _remaining;
        private Rigidbody _body;
        private BoxCollider _collider;

        /// <summary>Set up by <see cref="DetachableParts.Shed"/> immediately after it adds this.</summary>
        internal void Configure(DetachableParts owner, Transform home, Vector3 localPosition,
            Quaternion localRotation, Vector3 localScale, float seconds)
        {
            _owner = owner;
            _home = home;
            _localPosition = localPosition;
            _localRotation = localRotation;
            _localScale = localScale;
            _remaining = seconds;

            TryGetComponent(out _body);
            TryGetComponent(out _collider);
        }

        private void Update()
        {
            if (_remaining <= 0f) return;

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f) Expire();
        }

        /// <summary>
        /// Time is up, or the world has more shed parts than <see cref="DamageBudget"/> allows.
        /// Puts the part back where it belongs and hides it.
        /// </summary>
        public void Expire()
        {
            DamageBudget.GivePart(this);

            if (_home == null)
            {
                // The car it belonged to is gone - a despawned wreck, a scene change. Nothing to go
                // home to, so this is the one path that really does delete the piece.
                Destroy(gameObject);
                return;
            }

            Park();
            gameObject.SetActive(false);
            if (_owner != null) _owner.NoteParked(this);
            Strip();
        }

        /// <summary>Repair: back on the car, visible, and a part of it again.</summary>
        internal void Reattach()
        {
            DamageBudget.GivePart(this);

            if (_home == null) { Destroy(gameObject); return; }

            Park();
            gameObject.SetActive(true);
            Strip();
        }

        private void Park()
        {
            transform.SetParent(_home, false);
            transform.localPosition = _localPosition;
            transform.localRotation = _localRotation;
            transform.localScale = _localScale;
        }

        /// <summary>
        /// Take the physics off again, in the order that cannot leave a body half-alive: the
        /// collider first (a collider with no body left under it becomes a STATIC collider in the
        /// middle of a moving car, which PhysX re-cooks every time the car moves), then this
        /// component, then the body.
        /// </summary>
        private void Strip()
        {
            if (_collider != null) Destroy(_collider);
            var body = _body;
            _body = null;
            Destroy(this);
            if (body != null) Destroy(body);
        }
    }
}
