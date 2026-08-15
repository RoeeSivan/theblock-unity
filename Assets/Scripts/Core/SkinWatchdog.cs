using System.Text;
using UnityEngine;

namespace TheBlock.Core
{
    /// <summary>
    /// Catches the wedge.
    ///
    /// A skinned mesh drawn across the sky is the same failure every time — one bone gets a world
    /// pose that has nothing to do with the rest of the skeleton, and every vertex weighted to it is
    /// dragged there. What makes it expensive is not the fix but the identification: it is
    /// intermittent, it is gone by the time anyone can look, and a screenshot cannot say whether the
    /// apex belongs to the car, to Joe, or to a pedestrian standing next to both. This unit's ledger
    /// entry has already spent one play-test on exactly that ambiguity.
    ///
    /// So: watch every SkinnedMeshRenderer in the scene, and the moment one's world bounds exceed
    /// anything this game legitimately skins, name it, name the bone that dragged it, and PAUSE the
    /// editor on that frame so the Game view still shows what the log is describing.
    ///
    /// Editor-only by construction — the auto-install is compiled out of a player build.
    /// </summary>
    [DisallowMultipleComponent]
    public class SkinWatchdog : MonoBehaviour
    {
        /// <summary>
        /// How far a bone may sit from the middle of its own skeleton before it is not a pose but a
        /// fault — the floor under the adaptive limit, for skeletons small enough that a proportional
        /// budget would be jumpy. A pedestrian spans about 2 m and its bones never spread past that,
        /// so three is above every honest answer for a person.
        ///
        /// <b>Measured on the BONES, not on <c>renderer.bounds</c>, and that is the whole trick.</b>
        /// A SkinnedMeshRenderer's bounds are baked at import and re-derived from the root bone's
        /// transform — they do NOT grow when a bone is thrown. Verified here by throwing
        /// <c>Hood_front_5</c> 500 m and watching the bounds report 5.65 m as if nothing had
        /// happened. A bounds-based watchdog is not a weak test, it is a test that can never fire.
        /// </summary>
        [SerializeField] private float minBoneStray = 3f;

        /// <summary>
        /// The adaptive half of the limit: a bone may stray this many times the renderer's own baked
        /// diagonal before it counts as a fault.
        ///
        /// <b>Why a fixed 15 m was the wrong number, measured.</b> That was chosen against the
        /// wedge, which reaches the horizon, and it is 2.6x the Mustang's whole length — so a spike
        /// of six to fourteen metres, long enough to draw a white ray across half the screen, sat
        /// comfortably underneath it and this watchdog stayed silent through a play-test that
        /// produced a screenshot of exactly that. The budget has to be a proportion of the thing
        /// being watched, not a constant shared by a car and a pedestrian.
        ///
        /// One is deliberately tight: the Mustang's baked bounds are 6.6 m corner to corner and its
        /// worst honest bone — <c>steer_3</c>, the steering column tip — measures 2.9 m out, so this
        /// leaves better than 3 m of headroom over the worst pose the car legitimately reaches.
        /// </summary>
        [SerializeField] private float strayPerDiagonal = 1f;

        [Tooltip("Seconds between rescans of the scene. Renderers spawn and despawn constantly.")]
        [SerializeField] private float rescanInterval = 0.5f;

        [Tooltip("Pause the editor on the offending frame, so the log and the Game view agree.")]
        [SerializeField] private bool pauseOnCatch = true;

        private SkinnedMeshRenderer[] _renderers = System.Array.Empty<SkinnedMeshRenderer>();
        private float _nextScan;
        private bool _caught;

#if UNITY_EDITOR
        /// <summary>Installs itself when Play starts. Nothing to add to a scene, nothing to forget.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("__SkinWatchdog") { hideFlags = HideFlags.DontSave };
            go.AddComponent<SkinWatchdog>();
            DontDestroyOnLoad(go);
        }
#endif

        /// <summary>
        /// LateUpdate, after every poser has had its say: <c>CarWheel</c>, <c>CarDoor</c> and the
        /// Animators all write bones before this runs, so what is measured here is what gets drawn.
        /// </summary>
        private void LateUpdate()
        {
            if (_caught) return;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + rescanInterval;
                _renderers = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude);
            }

            foreach (var smr in _renderers)
            {
                if (smr == null || !smr.enabled) continue;

                var bones = smr.bones;
                if (bones == null || bones.Length < 2) continue;

                // Against the first bone rather than the median: this runs every frame on every
                // skeleton in the scene, and the median costs three sorts. If the first bone is
                // itself the thrown one every other bone reads as far, which still trips — the
                // report then does the expensive, careful version to name the right bone.
                var anchor = bones[0] != null ? bones[0].position : Vector3.zero;
                float stray = 0f;
                foreach (var bone in bones)
                {
                    if (bone == null) continue;
                    float d = Vector3.Distance(bone.position, anchor);
                    if (float.IsNaN(d) || float.IsInfinity(d)) { stray = float.MaxValue; break; }
                    if (d > stray) stray = d;
                }

                float limit = LimitFor(smr);
                if (stray <= limit) continue;

                Report(smr, stray, limit);
                return;
            }
        }

        /// <summary>
        /// How far this particular skeleton's bones may spread.
        ///
        /// <c>bounds</c> is the right reference precisely BECAUSE it is baked and never grows (see
        /// <see cref="minBoneStray"/>): it is a fixed statement of how big this renderer is supposed
        /// to be, unaffected by the very fault being looked for. A live measurement would move with
        /// the spike and the test would chase its own tail.
        /// </summary>
        private float LimitFor(SkinnedMeshRenderer smr) =>
            Mathf.Max(minBoneStray, smr.bounds.size.magnitude * strayPerDiagonal);

        private void Report(SkinnedMeshRenderer smr, float stray, float limit)
        {
            _caught = true;

            var report = new StringBuilder();
            report.AppendLine($"SkinWatchdog: {Path(smr.transform)} has a bone {stray:0.#} m out of " +
                              $"its own skeleton — this is the wedge. Limit was {limit:0.#} m " +
                              $"(baked diagonal {smr.bounds.size.magnitude:0.#} m).");

            // The renderer names the victim; the bones name the culprit. Measure every bone against
            // the MEDIAN of the skeleton rather than against the root — a thrown root would make
            // every other bone look like the outlier, and it is the one bone that disagrees with the
            // crowd that is always the one that was written wrong.
            var bones = smr.bones;
            if (bones == null || bones.Length == 0)
            {
                report.AppendLine("  no bones on this renderer — the fault is the mesh or its root bone.");
            }
            else
            {
                var centre = Median(bones);
                report.AppendLine($"  skeleton median {centre}, {bones.Length} bones:");

                var reported = new bool[bones.Length];
                for (int pass = 0; pass < 3; pass++)
                {
                    int worst = -1;
                    float worstDistance = -1f;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bones[i] == null || reported[i]) continue;
                        float d = Vector3.Distance(bones[i].position, centre);
                        if (float.IsNaN(d)) d = float.MaxValue;
                        if (d > worstDistance && d > 3f) { worstDistance = d; worst = i; }
                    }

                    if (worst < 0) break;
                    reported[worst] = true;

                    var bone = bones[worst];
                    var q = bone.rotation;
                    float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                    report.AppendLine($"  bone[{worst}] {bone.name} is {worstDistance:0.#} m out — " +
                                      $"pos {bone.position}, quaternion norm {norm:0.####} " +
                                      $"(1 is a valid rotation), local {bone.localPosition}");
                }
            }

            report.Append("  Editor paused on this frame. Press Play again to continue.");
            Debug.LogError(report.ToString(), smr);

#if UNITY_EDITOR
            if (pauseOnCatch) UnityEditor.EditorApplication.isPaused = true;
#endif
        }

        /// <summary>Component-wise median, which no single thrown bone can move.</summary>
        private static Vector3 Median(Transform[] bones)
        {
            var xs = new System.Collections.Generic.List<float>(bones.Length);
            var ys = new System.Collections.Generic.List<float>(bones.Length);
            var zs = new System.Collections.Generic.List<float>(bones.Length);

            foreach (var bone in bones)
            {
                if (bone == null) continue;
                var p = bone.position;
                if (float.IsNaN(p.sqrMagnitude)) continue;
                xs.Add(p.x); ys.Add(p.y); zs.Add(p.z);
            }

            if (xs.Count == 0) return Vector3.zero;
            xs.Sort(); ys.Sort(); zs.Sort();
            int mid = xs.Count / 2;
            return new Vector3(xs[mid], ys[mid], zs[mid]);
        }

        private static string Path(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
