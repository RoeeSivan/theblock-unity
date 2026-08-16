using UnityEngine;
using UnityEngine.Audio;
using TheBlock.Core;

namespace TheBlock.Audio
{
    /// <summary>
    /// The city's soundscape — the port of <c>src/audio/ambient.ts</c>. Two looped beds, a street
    /// murmur and an ocean bed, crossfaded by where the player is standing relative to the shore, so
    /// downtown bleeds into the beach as you walk west. Over them, a sparse layer of spot sounds
    /// (a honk, a dog, gulls) fires on a jittered timer so a 30-second loop never sounds like one.
    ///
    /// <b>The one thing that moved: the ducking.</b> The web multiplies <c>ambientAudio.duck</c>
    /// into both bed gains AND every one-shot's gain at every call site, because it has no bus to
    /// put it on. Here the duck is the Ambient mixer group's volume in a snapshot, so driving,
    /// stepping inside and starting the dance each move ONE number and the whole bed follows —
    /// including a one-shot already in the air, which the web's version cannot catch. The duck
    /// factor is still passed in here for one purpose only: skipping the spot-sound ROLL while
    /// the bus is silent, so a honk is not spent on a room nobody can hear it in.
    ///
    /// <b>Beachness runs in the WEB's frame, on purpose.</b> <c>config.sea.shoreX</c> is −430 in a
    /// right-handed world, and the crossfade is an inequality against it — the kind of expression
    /// that is silently mirrored by a missed sign and then "almost" works. Rather than re-derive the
    /// formula for a left-handed X, the player's position is converted BACK
    /// (<see cref="Convert.Pos(Vector3)"/> is its own inverse) and the web's arithmetic is used
    /// verbatim. One conversion, at the boundary, and the function stays diffable against
    /// <c>ambient.ts</c>.
    /// </summary>
    public class Ambient : MonoBehaviour
    {
        [SerializeField] private AudioLibrary library;
        [SerializeField] private AudioMixerGroup output;

        [Range(0f, 2f)]
        [Tooltip("Trim on top of the config's per-bed volumes.")]
        [SerializeField] private float trim = 1f;

        private TheBlockConfig.AmbientAudioSpec _spec;
        private TheBlockConfig.SeaSpec _sea;

        private AudioSource _street;
        private AudioSource _beach;
        private AudioSource _shot;

        private float _nextShot;
        private bool _started;
        private Coroutine _swell;

        /// <summary>Beachness at the last update, 0 inland → 1 on the sand. The measurement reads it.</summary>
        public float Beachness01 { get; private set; }

        /// <summary>Live bed volumes, for the same reason.</summary>
        public float StreetVolume => _street != null ? _street.volume : 0f;

        public float BeachVolume => _beach != null ? _beach.volume : 0f;

        /// <summary>Editor-side wiring, used by <c>The Block → Build Audio</c>.</summary>
        public void Configure(AudioLibrary clips, AudioMixerGroup group)
        {
            library = clips;
            output = group;
        }

        private void Awake()
        {
            var config = TheBlockConfig.Load()?.Config;
            _spec = config?.AmbientAudio;
            _sea = config?.Sea;
            _nextShot = RandomGap();
        }

        private void Start() => StartBeds();

        /// <summary>Starts both beds at silence. Idempotent — the update ramps them up from there.</summary>
        public void StartBeds()
        {
            if (_started || _spec?.Beds == null || library == null) return;
            _started = true;

            _street = MakeBed("Ambient Street", _spec.Beds.Street?.File);
            _beach = MakeBed("Ambient Beach", _spec.Beds.Beach?.File);

            var host = new GameObject("Ambient Spot");
            host.transform.SetParent(transform, false);
            _shot = host.AddComponent<AudioSource>();
            _shot.playOnAwake = false;
            _shot.spatialBlend = 0f;
            _shot.outputAudioMixerGroup = output;
        }

        private AudioSource MakeBed(string name, string url)
        {
            var clip = library.Resolve(url);
            if (clip == null)
            {
                // Non-critical, exactly as the web has it: a missing bed is half a soundscape, not a
                // broken game. It is said once, here, rather than every frame.
                Debug.LogWarning($"Ambient: no clip for '{url}' — that bed stays silent.");
                return null;
            }

            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = output;
            source.Play();
            return source;
        }

        /// <summary>
        /// Per frame: crossfade the beds by the player's position and roll the spot-sound timer.
        /// <paramref name="duck"/> is the linear gain the Ambient bus is currently ducked to — it
        /// gates the roll only; the mixer applies the level.
        /// </summary>
        public void Tick(float dt, Vector3 playerPosition, float duck)
        {
            if (_spec == null) return;

            float b = Beachness(playerPosition);
            Beachness01 = b;

            // The config's time-constant, applied as an exponential approach — the same curve Web
            // Audio's setTargetAtTime draws, so the crossfade is as slow as it was tuned to be.
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, _spec.Smooth));

            if (_street != null && _spec.Beds?.Street != null)
                _street.volume = Mathf.Lerp(_street.volume, _spec.Beds.Street.Volume * (1f - b) * trim, k);
            if (_beach != null && _spec.Beds?.Beach != null)
                _beach.volume = Mathf.Lerp(_beach.volume, _spec.Beds.Beach.Volume * b * trim, k);

            if (_spec.OneShots == null || _shot == null) return;

            _nextShot -= dt;
            if (_nextShot > 0f) return;
            _nextShot = RandomGap();
            if (duck <= 0f) return;

            var zone = b >= 0.5f ? _spec.OneShots.Beach : _spec.OneShots.Street;
            if (zone?.Urls == null || zone.Urls.Count == 0) return;
            if (Random.value >= zone.Chance) return;

            var clip = library != null ? library.Resolve(zone.Urls[Random.Range(0, zone.Urls.Count)]) : null;
            if (clip == null) return;

            // The web swells each spot sound in and out with its own envelope because a hard onset
            // felt startling — most of all on the gulls, which get a 0.45 s attack. Unity has no
            // per-source ramp, so the envelope is a coroutine over the source's own volume.
            if (_swell != null) StopCoroutine(_swell);
            _shot.clip = clip;
            _shot.volume = 0f;
            _shot.Play();
            _swell = StartCoroutine(Swell(zone.AttackSec, zone.ReleaseSec, zone.Gain, clip.length));
        }

        private System.Collections.IEnumerator Swell(float attack, float release, float gain, float length)
        {
            // Cap the fades so attack + release can never overrun a short clip — the web's own guard.
            float atk = Mathf.Min(attack, length * 0.45f);
            float rel = Mathf.Min(release, length * 0.45f);
            float t = 0f;

            while (t < length && _shot != null && _shot.isPlaying)
            {
                t += Time.deltaTime;
                float envelope =
                    t < atk ? t / Mathf.Max(0.0001f, atk) :
                    t > length - rel ? Mathf.Max(0f, (length - t) / Mathf.Max(0.0001f, rel)) :
                    1f;
                _shot.volume = gain * envelope * trim;
                yield return null;
            }

            if (_shot != null) _shot.volume = 0f;
            _swell = null;
        }

        /// <summary>
        /// 0 inland → 1 on the sand. Ramps over <c>blend.widthX</c> metres east of the shore, and
        /// only inside the Z band around the beach centre; pure street bed everywhere else.
        ///
        /// Argument is a UNITY position and is converted back to the web frame on the first line.
        /// </summary>
        public float Beachness(Vector3 unityPosition)
        {
            if (_spec?.Blend == null || _sea == null) return 0f;

            var web = Convert.Pos(unityPosition);
            float widthX = _spec.Blend.WidthX;
            float zHalf = _spec.Blend.ZHalf;

            float bx = Mathf.Clamp01((_sea.ShoreX + widthX - web.x) / Mathf.Max(0.0001f, widthX));
            // Soft Z gate: full inside the band, feathered to 0 over the last 20% of zHalf.
            float feather = Mathf.Max(0.0001f, zHalf * 0.2f);
            float bz = Mathf.Clamp01((zHalf - Mathf.Abs(web.z - _sea.CenterZ)) / feather);
            return bx * bz;
        }

        /// <summary>Fades both beds out. Nothing calls it yet; U26's title screen will.</summary>
        public void StopBeds()
        {
            if (_street != null) _street.volume = 0f;
            if (_beach != null) _beach.volume = 0f;
        }

        private float RandomGap()
        {
            var shots = _spec?.OneShots;
            if (shots == null) return 8f;
            return Random.Range(shots.MinGapSec, shots.MaxGapSec);
        }
    }
}
