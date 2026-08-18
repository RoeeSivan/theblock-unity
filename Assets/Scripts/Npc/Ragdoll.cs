using System.Collections.Generic;
using UnityEngine;

namespace TheBlock.Npc
{
    /// <summary>
    /// A body that can stop being animated and start being physics - U35a's mechanism, shared by the
    /// crowd and by the player.
    ///
    /// <b>The argument for the unit, in one sentence:</b> the web build's run-over is a canned Mixamo
    /// clip because Rapier on the main thread has no budget for a 15-body articulated rig per victim;
    /// PhysX does, and a real ragdoll is the difference between a hit that plays and a hit that
    /// happens.
    ///
    /// <b>This component is the mechanism and holds no policy.</b> It does not know what hit the body,
    /// how long it should lie there, or whether ragdolls are switched on - <see cref="RagdollReaction"/>
    /// owns the clock, <c>Pedestrian</c> and <c>PlayerRagdoll</c> own the decision. What lives here is
    /// the three-state switch and nothing else: animated → simulated → animated again.
    ///
    /// <b>The rig is BUILT, not found.</b> <c>RagdollBuilder</c> writes the bodies, the shells and the
    /// joints into the prefab at edit time, so every one of them is inspectable, tunable by hand and
    /// identical on every instance. Building it at the moment of the hit would put eleven
    /// <c>AddComponent</c> calls and ten joint solves on the frame the player most wants smooth
    /// (rule 3c: nothing in bulk on one frame).
    ///
    /// <b>At rest the rig costs as close to nothing as PhysX allows:</b> every body is kinematic and
    /// every shell is disabled, so none of it is in the broadphase and no joint is solved. That is
    /// what makes it safe to carry on all six pedestrian prefabs while at most
    /// <see cref="RagdollBudget"/>'s cap of them are ever live.
    /// </summary>
    [DisallowMultipleComponent]
    public class Ragdoll : MonoBehaviour
    {
        /// <summary>
        /// The layer every shell is built on - the crowd's own, and not a new one.
        ///
        /// It looks like a shortcut and it is the opposite: three behaviours this rig needs are
        /// already attached to this layer and would have to be re-created for a <c>Ragdoll</c> layer,
        /// each of them somewhere else in the project.
        ///
        ///  - <c>CrashSensor</c> ignores contacts with it, so driving over a corpse is not a crash,
        ///    does not thump and does not mint a wanted star. On any other layer, running a body over
        ///    twice would be a hit-and-run against a dead man.
        ///  - <c>CrowdBuilder</c> writes the crowd's ground mask as "everything except Pedestrian", so
        ///    a body lying in the road is not something the next pedestrian can stand on.
        ///  - The capsules on it are kinematic, so the pair being ignored below costs nothing that
        ///    was ever happening.
        /// </summary>
        public const string ShellLayer = "Pedestrian";

        [Tooltip("The Animator that owns this skeleton while the body is alive. Switched OFF for the " +
                 "duration of the simulation - a posing Animator and a solving joint chain fight over " +
                 "the same transforms, and the Animator wins every frame.")]
        [SerializeField] private Animator animator;

        [Tooltip("The pelvis body. Everything hangs off it, and it is what the rest of the game reads " +
                 "as 'where the body is' once the root transform has stopped moving.")]
        [SerializeField] private Rigidbody hips;

        [Tooltip("Every simulated bone, hips first. Built by The Block → Build Ragdolls.")]
        [SerializeField] private Rigidbody[] bones = System.Array.Empty<Rigidbody>();

        [Tooltip("The collision shells, index-matched to the bones.")]
        [SerializeField] private Collider[] shells = System.Array.Empty<Collider>();

        /// <summary>
        /// A skinned bone ABOVE the hips, carried along by hand while the body is simulating. Null on
        /// most rigs, and then none of this costs anything.
        ///
        /// <b>U38, and it presents as a crash.</b> The ragdoll drives the hips and everything under
        /// them; a bone above the hips is not in the joint chain and nothing moves it, so it stays at
        /// the transform the body had when it was hit. On a Mixamo rig that is nobody - every skinned
        /// bone is the hips or below. The pack rigs put a <c>Root</c> above the hips and list it as
        /// <c>bones[0]</c> on the garment meshes, so a corpse that slides four metres leaves one bone
        /// four metres behind, and <see cref="Core.SkinWatchdog"/> - correctly - calls that the wedge
        /// and pauses the editor.
        ///
        /// <b>It is not only the watchdog, which is why this is carried rather than exempted.</b> The
        /// same bone is those renderers' <c>rootBone</c>, and Unity derives a SkinnedMeshRenderer's
        /// world bounds from its root bone: left behind, a body's bounds stay where it was standing,
        /// so it is frustum-culled and shadowed against a position it no longer occupies. Carrying the
        /// bone fixes the diagnostic and the rendering with one line.
        ///
        /// The weights on it are zero, so no vertex is dragged either way - this is about where the
        /// renderer THINKS it is, not about the silhouette.
        /// </summary>
        [SerializeField] private Transform skinRoot;

        private Transform[] _boneTransforms;

        /// <summary>
        /// <see cref="skinRoot"/>'s pose in its parent, captured before anything has moved it.
        ///
        /// Restored by <see cref="Rest"/>, and that is not optional: a Humanoid Animator writes the
        /// mapped bones, and a bone above the hips is not mapped. Without this the displacement from
        /// the first run-over is permanent for the life of that pooled body - and the crowd recycles
        /// bodies, so it would spread through the pool one corpse at a time.
        /// </summary>
        private Vector3 _skinRootLocal;
        private bool _skinRootCaptured;

        /// <summary>True while this body is being simulated - frozen counts.</summary>
        public bool Active { get; private set; }

        /// <summary>True once the budget has parked this body in its final pose.</summary>
        public bool Frozen { get; private set; }

        /// <summary>False when the prefab has never been through <c>RagdollBuilder</c>.</summary>
        public bool IsBuilt => hips != null && bones != null && bones.Length > 0;

        /// <summary>
        /// Where the body actually is - the pelvis, not the root transform it left behind.
        ///
        /// Read off the TRANSFORM rather than <c>Rigidbody.position</c>, which is PhysX's copy and is
        /// stale for exactly as long as a kinematic body has been driven by a script. The transform is
        /// written every step while a body simulates, so it is never behind.
        /// </summary>
        public Vector3 Position => hips != null ? hips.transform.position : transform.position;

        /// <summary>The bones, for a caller that has to blend a pose back (the player's stand-up).</summary>
        public IReadOnlyList<Rigidbody> Bones => bones;

        /// <summary>The skeleton's Animator, so a caller does not have to go looking for it twice.</summary>
        public Animator Animator => animator;

        /// <summary>
        /// Mean bone speed in m/s - what "has it stopped moving yet" is asked of.
        ///
        /// Mean rather than max on purpose: a single hand can twitch against a joint limit for
        /// seconds after the body itself is still, and a max would keep the corpse from ever settling.
        /// </summary>
        public float Speed
        {
            get
            {
                if (!Active || Frozen || bones == null || bones.Length == 0) return 0f;

                float total = 0f;
                int counted = 0;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null || bones[i].isKinematic) continue;
                    total += bones[i].linearVelocity.magnitude;
                    counted++;
                }

                return counted == 0 ? 0f : total / counted;
            }
        }

        /// <summary>
        /// Editor-side wiring, called by <c>RagdollBuilder</c>. Everything is serialized, so this runs
        /// once at build time and never again.
        /// </summary>
        public void Configure(
            Animator skeleton, Rigidbody pelvis, Rigidbody[] built, Collider[] builtShells,
            Transform skinnedRootAboveHips)
        {
            animator = skeleton;
            hips = pelvis;
            bones = built;
            shells = builtShells;
            skinRoot = skinnedRootAboveHips;
        }

        private void Awake()
        {
            EnsureLayerRules();

            // Captured here, before the first Rest() below and long before any run-over: this is the
            // rig as the prefab authored it.
            if (skinRoot != null)
            {
                _skinRootLocal = skinRoot.localPosition;
                _skinRootCaptured = true;
            }

            // A prefab is saved with its shells enabled if anyone ever inspected it mid-simulation,
            // and a body that boots half-ragdolled walks around dragging its own limbs. Cheap to be
            // certain: eleven assignments, once, per body that ever exists.
            Rest();
        }

        /// <summary>
        /// Stop being animated and take this velocity.
        ///
        /// <b>The whole body is given the vehicle's velocity, and only then is an impulse added.</b>
        /// Handing the momentum to the pelvis alone and letting the joints drag the rest through is
        /// what makes a ragdoll look like a sack: the limbs arrive late, the chain snaps taut, and the
        /// body folds around its own hips. Every bone starting at the same speed is a body that was
        /// travelling with the car, which is what actually happened.
        /// </summary>
        /// <param name="velocity">World velocity every bone starts with.</param>
        /// <param name="hitPoint">Where the blow landed; the nearest bone takes the extra impulse.</param>
        /// <param name="impulse">Extra impulse at that point, in newton-seconds. May be zero.</param>
        public void Activate(Vector3 velocity, Vector3 hitPoint, Vector3 impulse)
        {
            if (!IsBuilt || Active) return;

            if (animator != null) animator.enabled = false;

            // ⚠ WITHOUT THIS LINE AND THE TWO IN THE LOOP, THE WHOLE FEATURE IS BROKEN in a way that
            // reads as a physics mystery rather than a bug.
            //
            // PhysX keeps its own copy of every body's pose and `Physics.autoSyncTransforms` is off,
            // so a kinematic body whose TRANSFORM a script has moved has told PhysX nothing. Every
            // bone in this rig is exactly that: crowd bodies are instantiated at the origin and driven
            // to their seed by `Pedestrian`, and the player walks around under a CharacterController.
            // The moment `isKinematic` goes false the solver takes over from ITS pose - the one baked
            // into the prefab - and the corpse appears at world (0, 0.85, 0), a hundred metres from
            // the car that hit it. Measured exactly that, with the victim standing at
            // (−9.2, 0, −159.4) and their hips ending up at the origin.
            Physics.SyncTransforms();

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;

                // The pose is written again per bone, and AFTER the flip: a write to a kinematic
                // body's position is a move target that the next physics step consumes, so doing it
                // in the other order queues a move that going dynamic then throws away. This ordering
                // is a teleport, which is what is wanted.
                bones[i].isKinematic = false;
                bones[i].position = bones[i].transform.position;
                bones[i].rotation = bones[i].transform.rotation;

                // Smooth only while it is actually simulating. Interpolation writes PhysX's pose onto
                // the transform every frame, which is right for a tumbling body and catastrophic for
                // a resting one - the note in RagdollBuilder is the measurement.
                bones[i].interpolation = RigidbodyInterpolation.Interpolate;

                bones[i].linearVelocity = velocity;
                bones[i].angularVelocity = Vector3.zero;
                if (i < shells.Length && shells[i] != null) shells[i].enabled = true;
            }

            Active = true;
            Frozen = false;

            if (impulse.sqrMagnitude > 1e-4f)
            {
                var struck = Nearest(hitPoint);
                if (struck != null) struck.AddForceAtPosition(impulse, hitPoint, ForceMode.Impulse);
            }
        }

        /// <summary>
        /// Park the body exactly where it is, in the pose it is in.
        ///
        /// <see cref="RagdollBudget"/>'s answer to the cap, and it is deliberately not "un-ragdoll the
        /// oldest": a corpse that snaps back to a lying animation while you are looking at it is worse
        /// than one that stops moving a second early. Kinematic bodies with their shells off leave the
        /// pose on screen and the solver empty.
        /// </summary>
        public void Freeze()
        {
            if (!Active || Frozen) return;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bones[i].linearVelocity = Vector3.zero;
                bones[i].angularVelocity = Vector3.zero;
                bones[i].isKinematic = true;
                bones[i].interpolation = RigidbodyInterpolation.None;
                if (i < shells.Length && shells[i] != null) shells[i].enabled = false;
            }

            Frozen = true;
        }

        /// <summary>
        /// Back under the Animator. The pose is not restored by hand - a Humanoid Animator writes
        /// every bone's rotation and the hips' position on its first update, so one frame after this
        /// the skeleton is wherever the controller says it is.
        ///
        /// The caller is what decides where the BODY then stands; this component has no opinion.
        /// </summary>
        public void Rest()
        {
            if (bones != null)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null) continue;
                    if (!bones[i].isKinematic)
                    {
                        bones[i].linearVelocity = Vector3.zero;
                        bones[i].angularVelocity = Vector3.zero;
                    }

                    bones[i].isKinematic = true;

                    // Back to None the moment it stops simulating, or this rig quietly drags the
                    // body's whole skeleton to the prefab's pose for the rest of the session.
                    bones[i].interpolation = RigidbodyInterpolation.None;
                    if (i < shells.Length && shells[i] != null) shells[i].enabled = false;
                }
            }

            if (Active) RagdollBudget.Give(this);

            // Put the carried bone back where the prefab had it. Nothing else will: it is above the
            // hips, so the Humanoid Animator does not write it.
            if (_skinRootCaptured && skinRoot != null) skinRoot.localPosition = _skinRootLocal;

            Active = false;
            Frozen = false;
            if (animator != null) animator.enabled = true;
        }

        /// <summary>
        /// Carries <see cref="skinRoot"/> to the hips for as long as the body is simulating.
        ///
        /// <b>Move the parent, then put the child back.</b> <see cref="skinRoot"/> is an ANCESTOR of
        /// the hips, so moving it drags the whole solved pose with it; restoring the hips' world pose
        /// immediately afterwards undoes that for the hips and, because every other simulated bone
        /// hangs off the hips, for all of them. The net effect is that one unsimulated bone catches
        /// up and the ragdoll does not move a millimetre.
        ///
        /// LateUpdate, after the solver's pose has been written and - by execution order - before
        /// <see cref="Core.SkinWatchdog"/> measures it.
        /// </summary>
        private void LateUpdate()
        {
            if (!Active || skinRoot == null || hips == null) return;

            var pose = hips.transform.position;
            var spin = hips.transform.rotation;

            skinRoot.position = pose;
            hips.transform.SetPositionAndRotation(pose, spin);
        }

        /// <summary>Every simulated bone's transform, cached - the pose blend walks it every frame.</summary>
        public Transform[] BoneTransforms()
        {
            if (_boneTransforms != null && _boneTransforms.Length == bones.Length) return _boneTransforms;

            _boneTransforms = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                _boneTransforms[i] = bones[i] != null ? bones[i].transform : null;

            return _boneTransforms;
        }

        private Rigidbody Nearest(Vector3 point)
        {
            Rigidbody best = hips;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                float sqr = (bones[i].worldCenterOfMass - point).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = bones[i];
            }

            return best;
        }

        // --- the collision matrix ------------------------------------------------------------------

        private static bool _layersRuled;

        /// <summary>
        /// One pair of layers that must not collide - <see cref="ShellLayer"/> with itself - set in
        /// code rather than by a checkbox in the physics matrix.
        ///
        /// It buys two things at once, and both are ragdoll faults rather than crowd faults:
        ///
        ///  - <b>Self-collision inside one rig.</b> A thigh permanently overlapping its own pelvis is
        ///    a contact the solver cannot resolve, and the body buzzes rather than settles. Every
        ///    shipped ragdoll switches this off; the Ragdoll Wizard's own output does not, which is
        ///    why its output twitches.
        ///  - <b>One corpse against another.</b> A pile-up of eleven bodies each is a contact count
        ///    with no ceiling, for a heap nobody looks at closely.
        ///
        /// The pedestrian capsules that also live on this layer are kinematic and never collided with
        /// each other anyway, so nothing that was happening stops happening.
        ///
        /// In code because it is a property of this mechanism: the pairing is meaningless without the
        /// rig, and a matrix checkbox in Project Settings is exactly the kind of state that is
        /// silently lost and impossible to grep for.
        /// </summary>
        private static void EnsureLayerRules()
        {
            if (_layersRuled) return;
            _layersRuled = true;

            int shells = LayerMask.NameToLayer(ShellLayer);
            if (shells >= 0) Physics.IgnoreLayerCollision(shells, shells, true);
        }
    }
}
