using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// The wanted meter. It knows nothing about cops, crimes or cars — only a number that goes up
    /// when told and down on its own.
    ///
    /// <b>A meter, not a counter, and that is the design change of this unit.</b> The web build
    /// bumps <c>wantedLevel</c> by a whole star per crime with a 3 s cooldown, so every offence is
    /// worth the same and the smallest one is worth a police car. Here heat is continuous and
    /// measured in stars: a scrape adds a few hundredths, a solid hit adds half, running someone over
    /// adds one. The star you see is <c>floor(value)</c>, and the fraction is drawn — which makes the
    /// difference visible rather than merely fairer. You can watch a wall scrape move nothing.
    ///
    /// <b>Decay always runs.</b> That deletes a whole mechanism the web needed: there, heat cannot
    /// bleed until a cop has reached you (<c>engaged</c>), which needs a 30 s <c>inboundGrace</c>
    /// safety valve for when none ever arrives, which existed because cops regularly could not path
    /// to you at all. With an unconditional floor rate, "three stars and nothing on screen, forever"
    /// is not a state this can be in.
    /// </summary>
    public class Heat : MonoBehaviour
    {
        [SerializeField] private PoliceTuning tuning = new();

        /// <summary>
        /// Serialized run state, hidden. A mid-Play recompile reloads the domain, clears every
        /// ordinary private field and does not re-run Awake — losing your stars mid-chase.
        /// </summary>
        [SerializeField, HideInInspector] private float value;

        [SerializeField, HideInInspector] private int stars;
        [SerializeField, HideInInspector] private float sinceContact;
        [SerializeField, HideInInspector] private float sinceCrime;

        public PoliceTuning Tuning => tuning;

        /// <summary>Heat in stars. 1.00 means one star lit.</summary>
        public float Value => value;

        /// <summary>Lit stars, with hysteresis so one cannot flicker at the boundary.</summary>
        public int Stars => stars;

        /// <summary>How far into the next star. This is what the partial star draws.</summary>
        public float Fraction => Mathf.Clamp01(value - stars);

        /// <summary>Seconds since a cop last had you in sight and in range.</summary>
        public float SinceContact => sinceContact;

        /// <summary>Current bleed rate, stars per second. The HUD tints the partial star by it.</summary>
        public float DecayPerSecond { get; private set; }

        /// <summary>Raised when the lit star count changes, either way.</summary>
        public event System.Action<int> StarsChanged;

        /// <summary>
        /// Frozen: no gain, no decay, and the pursuit stands down. Set while the player is indoors.
        ///
        /// <b>A deliberate deviation:</b> the web CLEARS heat in an interior, which makes the
        /// pizzeria a free escape from any pursuit. U13's decision log says the room is a real place
        /// a kilometre away precisely so the street keeps simulating while you are in it, and
        /// wiping heat contradicts the reason it was built that way. One line to revert if it plays
        /// badly.
        /// </summary>
        public bool Frozen { get; set; }

        /// <summary>Suppresses CRASH heat only. U20 sets it inside a mission; nothing sets it yet.</summary>
        public bool SuppressCrash { get; set; }

        /// <summary>Adds heat and freezes decay briefly, so a star does not bleed as it lights.</summary>
        public void Add(float amount)
        {
            if (Frozen || amount <= 0f) return;

            value = Mathf.Clamp(value + amount, 0f, tuning.MaxStars);
            sinceCrime = 0f;
            Restar();
        }

        /// <summary>Wipes it. The bust, and the mission rising edge U20 will own.</summary>
        public void Clear()
        {
            value = 0f;
            sinceCrime = 999f;
            Restar();
        }

        /// <summary>Called by <see cref="PoliceSystem"/> on any step a cop can see the player.</summary>
        public void ReportContact() => sinceContact = 0f;

        private void Update()
        {
            float dt = Time.deltaTime;
            sinceContact += dt;
            sinceCrime += dt;

            if (Frozen || value <= 0f)
            {
                DecayPerSecond = 0f;
                return;
            }

            if (sinceCrime < tuning.CrimeFreeze)
            {
                DecayPerSecond = 0f;
                return;
            }

            // One curve: a floor that always runs, plus a much faster rate that ramps in once you
            // have been out of contact long enough for it to read as having lost them.
            float hidden = Mathf.Clamp01((sinceContact - tuning.ContactGrace) / Mathf.Max(0.01f, tuning.HiddenRamp));
            DecayPerSecond = tuning.DecayAlways + tuning.DecayHidden * hidden;

            value = Mathf.Max(0f, value - DecayPerSecond * dt);
            Restar();
        }

        /// <summary>
        /// Recomputes the lit star count.
        ///
        /// The hysteresis band is on the way DOWN only: a star lights the instant the meter crosses
        /// it, because that is the feedback for the crime you just committed, but it does not go out
        /// until the meter is clearly below — otherwise a value hovering at 1.000 flickers a star and
        /// a police car with it.
        /// </summary>
        private void Restar()
        {
            int next = Mathf.FloorToInt(value);
            if (next < stars && value > stars - 0.10f) return;
            if (next == stars) return;

            stars = Mathf.Clamp(next, 0, tuning.MaxStars);
            StarsChanged?.Invoke(stars);
        }
    }
}
