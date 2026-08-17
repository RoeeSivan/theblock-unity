using UnityEngine;

namespace TheBlock.Game
{
    /// <summary>
    /// How much of U35b's vehicle damage is switched on. Three states rather than a toggle, because
    /// the two halves fail differently: the dents and the smoke change how a crash LOOKS, and the
    /// explosion changes what a crash COSTS - a car you cannot drive home.
    /// </summary>
    public enum VehicleDamageMode
    {
        /// <summary>Nothing at all. No mesh is cloned, no emitter is built, no part comes off.</summary>
        Off = 0,

        /// <summary>Dents, smoke, fire and shed parts - but a car never dies and never explodes.</summary>
        Visual = 1,

        /// <summary>All of it: health reaches zero, the car burns down and goes up.</summary>
        Full = 2,
    }

    /// <summary>
    /// Campaign unlock progress that survives a quit - the port of <c>game/progress.ts</c>.
    ///
    /// <see cref="Campaign"/> only tracks a cursor for the current session; this is the one number
    /// that outlives it. It is MONOTONIC on purpose: completing a mission raises it, and nothing
    /// ever lowers it, so a Mission Select jump (U26) cannot cost a returning player a mission they
    /// already earned.
    ///
    /// Static, because it is asked for before any scene object is guaranteed to exist and it holds
    /// no state of its own - <c>PlayerPrefs</c> IS the state. That is Unity's <c>localStorage</c>:
    /// same keys as the web build, same write-on-every-change, and the same refusal to let a storage
    /// failure break play. <c>PlayerPrefs</c> never throws where <c>localStorage</c> can, so the
    /// web's try/catch has nothing to guard here - the tolerance is in the DEFAULTS instead.
    /// </summary>
    public static class Progress
    {
        private const string UnlockedKey = "theblock.unlocked";
        private const string CharacterKey = "theblock.character";
        private const string RadarKey = "theblock.radar";
        private const string GpsRouteKey = "theblock.gpsroute";
        private const string DayNightKey = "theblock.daynight";
        private const string SoundKey = "theblock.sound";
        private const string RagdollKey = "theblock.ragdolls";
        private const string VehicleDamageKey = "theblock.vehicledamage";

        /// <summary>
        /// Furthest mission index unlocked. 0 = only the first, which is also what a fresh profile
        /// reads, so "never played" and "played the first mission" are the same state - exactly as
        /// in the web build.
        /// </summary>
        public static int UnlockedIndex => Mathf.Max(0, PlayerPrefs.GetInt(UnlockedKey, 0));

        /// <summary>Records the furthest mission reached. Never lowers the stored value.</summary>
        public static void RecordReached(int index)
        {
            if (index <= UnlockedIndex) return;
            PlayerPrefs.SetInt(UnlockedKey, index);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Which body the player wears. Deliberately NOT touched by <see cref="Reset"/>: New Game
        /// restarts the campaign, not who you are. Empty means "never picked" and the caller
        /// resolves it to the roster default - the roster is U29's, so today it is always empty.
        /// </summary>
        public static string CharacterId
        {
            get => PlayerPrefs.GetString(CharacterKey, string.Empty);
            set
            {
                PlayerPrefs.SetString(CharacterKey, value ?? string.Empty);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Settings → Display → Radar: is the corner minimap on? Default true, which is the state
        /// the user restored on 2026-08-16 after the same day's removal.
        ///
        /// A PREFERENCE, not progress, so like <see cref="CharacterId"/> it survives
        /// <see cref="Reset"/> - a New Game is a new campaign, not a new profile. It is also not the
        /// dance's <c>GameMap.Suppressed</c>: that is a temporary override a scene takes and gives
        /// back, and writing this from there would hand the radar back ON to someone who turned it
        /// off.
        /// </summary>
        public static bool RadarOn
        {
            get => PlayerPrefs.GetInt(RadarKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(RadarKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Settings → Display → GPS Route (U35c): does the map draw a road route to the objective?
        /// <b>Default TRUE</b>, and it is the one U35 addition that may ship on without arguing with
        /// the Tier 8 off-switch rule - that rule exists to protect a VISUAL judgement of the world,
        /// and this draws on the HUD only. Nothing in the scene, the lighting or the framing moves.
        ///
        /// A PREFERENCE, like <see cref="RadarOn"/>, so it survives <see cref="Reset"/>.
        ///
        /// <b>Pull-only, deliberately.</b> Unlike the radar there is no boot-time push and no live
        /// object to write to: <c>GpsRoute.Tick</c> reads this at the top of every tick and returns
        /// early when it is false. One reader means the preference and the drawing cannot drift, and
        /// turning it off costs an A* search rather than merely hiding its result.
        /// </summary>
        public static bool GpsRouteOn
        {
            get => PlayerPrefs.GetInt(GpsRouteKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(GpsRouteKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Settings → Display → Time of Day: does the sun move? <b>Default FALSE, and that default is
        /// load-bearing.</b> A moving sun is an addition to this port, not a port of anything - the
        /// web build's sky is one constant colour - so off means the world looks exactly as it did
        /// for every play-test from U11 to U27, and it costs exactly what it did too: with this false
        /// the grading pass is not scheduled at all.
        ///
        /// A PREFERENCE, like <see cref="RadarOn"/>, so it survives <see cref="Reset"/>.
        /// </summary>
        public static bool DayNightOn
        {
            get => PlayerPrefs.GetInt(DayNightKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(DayNightKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Settings → Audio → Sound: is anything audible at all? Default true. Read and written
        /// through <c>Mute</c>, which is the only thing allowed to touch
        /// <c>AudioListener.volume</c> - this property is the storage, not the mechanism.
        ///
        /// A PREFERENCE, like <see cref="RadarOn"/>, so it survives <see cref="Reset"/>: a New Game
        /// must not start shouting at someone who muted the game.
        /// </summary>
        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt(SoundKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(SoundKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Settings → Gameplay → Ragdolls: does a body that is hit get simulated? <b>Default TRUE, and
        /// it is the only U35 addition that ships switched on.</b>
        ///
        /// The off-switch rule exists so that an addition cannot re-open a visual judgement made in
        /// U11-U27 days before a recording, and this one cannot: it replaces a REACTION, not a look.
        /// Nothing about the world, the lighting or the framing changes, only what a person does in
        /// the second after a bumper reaches them - and U18's clip is still sitting there, one toggle
        /// away, if the physics ever reads worse than the animation did.
        ///
        /// A PREFERENCE, like <see cref="RadarOn"/>, so it survives <see cref="Reset"/>.
        /// </summary>
        public static bool RagdollsOn
        {
            get => PlayerPrefs.GetInt(RagdollKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(RagdollKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Settings → Gameplay → Vehicle Damage (U35b). <b>Default Off</b>, which is the Tier 8 rule
        /// this whole showcase tier ships under: the off state IS today's game, so U30b's baseline is
        /// taken with every U35 addition switched off and each one is then measured alone.
        ///
        /// The opposite call to <see cref="RagdollsOn"/>, and the difference is worth stating. A
        /// ragdoll replaces a reaction nobody has approved a screenshot of; a dented, smoking car
        /// changes what the world LOOKS like in every shot taken since U8, and it can leave the player
        /// stranded beside a wreck. That earns the conservative default.
        ///
        /// A PREFERENCE, like <see cref="RadarOn"/>, so it survives <see cref="Reset"/>. Stored as an
        /// int and read back through a range check rather than a cast, because a corrupt or
        /// hand-edited value must land on Off - the safe state - and never on a mode that does not
        /// exist.
        /// </summary>
        public static VehicleDamageMode VehicleDamage
        {
            get
            {
                int stored = PlayerPrefs.GetInt(VehicleDamageKey, (int)VehicleDamageMode.Off);
                return stored == (int)VehicleDamageMode.Visual ? VehicleDamageMode.Visual
                    : stored == (int)VehicleDamageMode.Full ? VehicleDamageMode.Full
                    : VehicleDamageMode.Off;
            }
            set
            {
                PlayerPrefs.SetInt(VehicleDamageKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Is any of U35b on? The one read every damage call site starts with.</summary>
        public static bool VehicleDamageOn => VehicleDamage != VehicleDamageMode.Off;

        /// <summary>Are cars allowed to die and explode, or only to look hurt?</summary>
        public static bool VehicleDamageLethal => VehicleDamage == VehicleDamageMode.Full;

        /// <summary>New Game: back to only the first mission unlocked.</summary>
        public static void Reset()
        {
            PlayerPrefs.SetInt(UnlockedKey, 0);
            PlayerPrefs.Save();
        }
    }
}
