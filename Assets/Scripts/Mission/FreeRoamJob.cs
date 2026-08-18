namespace TheBlock.Missions
{
    /// <summary>
    /// The one street job running right now, or null.
    ///
    /// <b>It exists to settle who owns three HUD labels.</b> <c>MissionHud</c>'s objective, clock and
    /// counter are latched setters, not the arbitrated immediate-mode one the prompt uses, and
    /// <see cref="CampaignRunner.DriveHud"/> rewrites all three every frame from the campaign cursor.
    /// A free-roam job that wrote its own objective would be overwritten within the same frame by
    /// whichever component Unity happened to call second. So the campaign stands down while this is
    /// set, and the job draws its own - one flag, one owner, the shape <c>Heat.Frozen</c> settled
    /// (memory: <c>one-flag-one-owner-heat-frozen</c>).
    ///
    /// <b>One writer: <see cref="FalafelRun"/>.</b> A second job would need a rule for what happens
    /// when both are running, and there is no second job.
    ///
    /// Typed as a <see cref="MissionBehaviour"/> - a UnityEngine.Object - on purpose: a destroyed
    /// component read through <c>!= null</c> answers false, so a world torn down mid-round cannot
    /// leave the campaign permanently stood down behind a dangling reference.
    /// </summary>
    public static class FreeRoamJob
    {
        /// <summary>The running job, or null. Set on start, cleared on every exit.</summary>
        public static MissionBehaviour Active;

        /// <summary>
        /// Called by <see cref="Core.SessionReset"/> on a scene reload. Belt and braces - the job
        /// clears this itself on every exit and on disable - but a static with no per-frame writer
        /// to heal it is exactly the shape that file exists for.
        /// </summary>
        public static void Clear() => Active = null;
    }
}
