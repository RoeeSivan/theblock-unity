using UnityEngine;

namespace TheBlock.Police
{
    /// <summary>
    /// The wanted meter: a whole number of stars, one per crime, one police car per star.
    ///
    /// <b>This was a continuous meter and it is a counter again, deliberately.</b> The meter version
    /// argued that a scrape should be worth less than a body, and it was right about that — but it
    /// bled unconditionally, and U19 then put every cruiser in a station bay a hundred to nine
    /// hundred metres away. Those two are incompatible and the arithmetic says so: a run-over gave
    /// 1.05, the star went out at 0.90, and at 0.030/s (0.250/s once out of contact) it survived
    /// about six seconds against a drive of fifteen to sixty. <b>The cop could never arrive.</b> It
    /// read exactly as "the police do not come", which is what the play-test reported.
    ///
    /// So the shape here is <c>police.ts</c>'s again, including the piece the meter had thrown away:
    ///
    /// <b><c>engaged</c> is the whole fix.</b> Heat cannot bleed at all until a cop has first reached
    /// <see cref="PoliceTuning.SightRadius"/>. A response has a travel time now, and a travel time is
    /// only a mechanic if the clock does not run during it. <see cref="PoliceTuning.InboundGrace"/>
    /// bounds the latch so an unreachable player still cools off rather than wearing stars forever.
    ///
    /// A pursuit therefore has exactly three ends, and all three are in this file: break contact and
    /// outlast <c>ShedStep</c>, outlast <c>GiveUpAt</c> since your last crime whether or not they can
    /// see you, or get busted.
    /// </summary>
    public class Heat : MonoBehaviour
    {
        [SerializeField] private PoliceTuning tuning = new();

        /// <summary>
        /// Serialized run state, hidden. A mid-Play recompile reloads the domain, clears every
        /// ordinary private field and does not re-run Awake — losing your stars mid-chase.
        /// </summary>
        [SerializeField, HideInInspector] private int stars;

        [SerializeField, HideInInspector] private bool engaged;
        [SerializeField, HideInInspector] private float inbound;
        [SerializeField, HideInInspector] private float noContact;
        [SerializeField, HideInInspector] private float coolTimer;
        [SerializeField, HideInInspector] private float sinceCrime;

        /// <summary>Set by <see cref="PoliceSystem"/> each step, consumed by <see cref="Update"/>.</summary>
        private bool _contactThisFrame;

        private bool _inSightThisFrame;

        public PoliceTuning Tuning => tuning;

        /// <summary>Lit stars, which is also the number of cars that will come.</summary>
        public int Stars => stars;

        /// <summary>Seconds since a cop last had you in sight and in range.</summary>
        public float SinceContact => noContact;

        /// <summary>Has any cop reached sight range yet? False while they are still driving in.</summary>
        public bool Engaged => engaged;

        /// <summary>
        /// Seconds of ENGAGED pursuit since the last crime — it does not advance while the cars are
        /// still driving in. The hard give-up cap counts this.
        /// </summary>
        public float SinceCrime => sinceCrime;

        /// <summary>
        /// The evade arc, 0..1 — how far through losing them you are.
        ///
        /// Two phases in one number, which is the web's own readout: before <c>BreakContact</c> it
        /// fills as you stay out of sight, and after it, it fills as the top star drains. The HUD
        /// draws it as the partial star, so the thing that used to show "heat bleeding" now shows
        /// "you are getting away", which is the only information a discrete counter has to give.
        /// </summary>
        public float Fraction
        {
            get
            {
                if (stars <= 0) return 0f;

                return Cooling
                    ? Mathf.Clamp01(coolTimer / Mathf.Max(0.01f, Step(tuning.ShedStep, stars)))
                    : Mathf.Clamp01(noContact / Mathf.Max(0.01f, tuning.BreakContact));
            }
        }

        /// <summary>Out of contact long enough that the top star is actively draining.</summary>
        public bool Cooling => stars > 0 && engaged && noContact >= tuning.BreakContact;

        /// <summary>Raised when the lit star count changes, either way.</summary>
        public event System.Action<int> StarsChanged;

        /// <summary>
        /// Frozen: no gain, no shedding, and the pursuit stands down. Set while the player is indoors.
        ///
        /// <b>A deliberate deviation:</b> the web CLEARS heat in an interior, which makes the
        /// pizzeria a free escape from any pursuit. U13's decision log says the room is a real place
        /// a kilometre away precisely so the street keeps simulating while you are in it, and
        /// wiping heat contradicts the reason it was built that way. One line to revert if it plays
        /// badly.
        /// </summary>
        public bool Frozen { get; set; }

        /// <summary>Suppresses CRASH crimes only. U20 sets it inside a mission; nothing sets it yet.</summary>
        public bool SuppressCrash { get; set; }

        /// <summary>
        /// One crime, one star, one more police car. The only way heat ever goes up.
        ///
        /// It re-arms the give-up cap even at maximum stars, so staying on a rampage keeps the heat
        /// alive without being able to escalate past <see cref="PoliceTuning.MaxStars"/>.
        /// </summary>
        public void Bump()
        {
            if (Frozen) return;

            sinceCrime = 0f;
            noContact = 0f;
            coolTimer = 0f;

            if (stars >= tuning.MaxStars) return;

            stars++;
            StarsChanged?.Invoke(stars);
        }

        /// <summary>Wipes it. The bust, and the mission rising edge U20 will own.</summary>
        public void Clear()
        {
            bool changed = stars != 0;
            stars = 0;
            ResetEvade();
            if (changed) StarsChanged?.Invoke(stars);
        }

        /// <summary>
        /// Called by <see cref="PoliceSystem"/> on any step a cop is within sight RANGE, line of
        /// sight or not. This is what latches <c>engaged</c> — arriving is the event, not seeing.
        /// </summary>
        public void ReportInSight() => _inSightThisFrame = true;

        /// <summary>Called on any step a cop is in range AND has a clear line to the player.</summary>
        public void ReportContact() => _contactThisFrame = true;

        private void Update()
        {
            bool contact = _contactThisFrame;
            bool inSight = _inSightThisFrame;
            _contactThisFrame = _inSightThisFrame = false;

            if (Frozen) return;

            if (stars <= 0)
            {
                ResetEvade();
                return;
            }

            float dt = Time.deltaTime;
            if (inSight) engaged = true;

            // The give-up clock only runs while a cop has actually got to you, and that is the same
            // lesson as the latch, found the same way. Measured: a cruiser left the station 127 m
            // away, drove a real route at 95% on-road — and the 45 s cap expired with it still 112 m
            // out, so the pursuit ended before the pursuit started. "The cops eventually stop even if
            // you never lose them" presupposes they reached you; the inbound phase is bounded by
            // InboundGrace instead, which is the whole reason that number exists.
            if (engaged) sinceCrime += dt;

            // Bound the latch. If nobody has arrived after InboundGrace the pursuit is structurally
            // stuck — an unreachable spot, or the orphaned downtown avenue — and the player should
            // be able to cool off rather than wear stars with no cop on screen. A cop arriving later
            // still resets noContact below, so this only ever costs a star on a genuinely broken one.
            if (!engaged)
            {
                inbound += dt;
                if (inbound >= tuning.InboundGrace) engaged = true;
            }

            if (contact || !engaged)
            {
                noContact = 0f;
                coolTimer = 0f;
            }
            else
            {
                noContact += dt;
                if (noContact >= tuning.BreakContact)
                {
                    coolTimer += dt;
                    if (coolTimer >= Step(tuning.ShedStep, stars)) ShedStar();
                }
            }

            // The hard cap, deliberately OUTSIDE the branch above: it has to run whether or not they
            // still have eyes on you. Without it a cop welded to your bumper means a pursuit that
            // never ends at all.
            if (stars > 0 && sinceCrime >= Step(tuning.GiveUpAt, stars)) ShedStar();
        }

        /// <summary>
        /// Drops exactly one star — the only place heat goes down on its own, and therefore the only
        /// place the zero crossing is handled.
        /// </summary>
        private void ShedStar()
        {
            stars = Mathf.Max(0, stars - 1);
            coolTimer = 0f;
            sinceCrime = 0f;

            if (stars == 0) ResetEvade();
            StarsChanged?.Invoke(stars);
        }

        private void ResetEvade()
        {
            noContact = 0f;
            coolTimer = 0f;
            sinceCrime = 0f;
            inbound = 0f;
            engaged = false;
        }

        /// <summary>
        /// Reads a per-star table by the star being shed, clamped — so a table someone shortens in
        /// the inspector falls back to its last entry instead of throwing.
        /// </summary>
        private static float Step(float[] table, int star)
        {
            if (table == null || table.Length == 0) return 5f;
            return table[Mathf.Clamp(star, 1, table.Length) - 1];
        }
    }
}
