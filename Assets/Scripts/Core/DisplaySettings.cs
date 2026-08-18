using UnityEngine;

namespace TheBlock.Core
{
    /// <summary>
    /// Caps the frame rate. One setting, applied once, before anything renders.
    ///
    /// <b>What it fixes.</b> U30b round 1 read the Player's own state and found <c>vSyncCount: 0</c>
    /// on both quality levels with nothing anywhere assigning <c>Application.targetFrameRate</c> - so
    /// the game rendered as fast as the machine could go. On the cheap frames that is real work
    /// thrown away: a 17 ms frame redrawn at 300 fps buys no visible smoothness and costs battery,
    /// fan noise and the thermal headroom the expensive frames need.
    ///
    /// <b>Why a soft cap and not vSync.</b> vSync would be the tidier answer on a machine that
    /// always beats the refresh rate. This one does not: round 1 measured a mean of 17 ms at rest and
    /// 44-77 ms in a chase. A swap-interval cap has a cliff - miss the 16.7 ms window by a hair and
    /// the frame waits for the NEXT one, so 17 ms becomes 33 - which would make the game worse on
    /// precisely the frames that are currently fine. <c>targetFrameRate</c> only ever sleeps when
    /// the frame came in early, so it has no cliff. <c>vSyncCount</c> is left at 0 deliberately;
    /// setting it would override this and reintroduce the cliff.
    ///
    /// <b>60, not 120.</b> The measured frames do not reach 120 anywhere in the game, so a higher cap
    /// would be a cap in name only.
    ///
    /// This is a global, and once per session is exactly right for it - which is what
    /// <see cref="RuntimeInitializeOnLoadMethod"/> gives (it does not fire again on a scene reload).
    /// Nothing here needs to run again on Quit to Title, because nothing else writes these two.
    /// </summary>
    public static class DisplaySettings
    {
        /// <summary>The cap. Not serialized anywhere - there is no Settings row for it by choice.</summary>
        public const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;
        }
    }
}
