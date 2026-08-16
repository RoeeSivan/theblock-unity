using TheBlock.Missions;
using TheBlock.Npc;
using TheBlock.UI;
using TheBlock.Vehicles;
using TheBlock.World;

namespace TheBlock.Core
{
    /// <summary>
    /// Drops every static this game keeps across a scene, so the world can be rebuilt inside one
    /// Play session.
    ///
    /// <b>Why this has to exist at all:</b> six things in the project reset themselves with
    /// <c>[RuntimeInitializeOnLoadMethod]</c>, and every one of those comments is correct about the
    /// trap it guards — statics survive entering Play. What none of them can do is fire again when a
    /// SCENE is reloaded, because that attribute runs once per session. Until U26 nothing ever
    /// reloaded a scene, so the distinction never mattered. <c>Quit to Title</c> makes it matter.
    ///
    /// Two of the six are the reason this is not optional:
    ///
    ///  - <see cref="MapRegistry"/> accumulates. Every district and POI registers on Awake, so a
    ///    second run through the world draws the whole map twice, over itself.
    ///  - <see cref="SeaSurface"/> LATCHES. Its <c>_searched</c> flag goes true once; after a reload
    ///    the cached component is a destroyed object that still reads as non-null (memory:
    ///    <c>unity-null-coalescing-fake-null</c>) and nothing will ever go looking for the new one.
    ///
    /// The others accumulate dead entries their own sweeps would eventually drop, or hold meshes
    /// that <c>UnloadUnusedAssets</c> may or may not have taken. "May or may not" is the answer that
    /// makes them worth clearing here rather than reasoning about per-type.
    ///
    /// <see cref="DayNightCycle"/> (U33) is the third that is not optional: its clock is a static, so
    /// Quit to Title at 22:00 would otherwise drop a New Game into the dark.
    /// </summary>
    public static class SessionReset
    {
        /// <summary>Called by <c>BootLoader</c> before it loads the world.</summary>
        public static void Run()
        {
            MapRegistry.Clear();
            EnterableRegistry.Clear();
            CrossingRegistry.Clear();
            BuoyField.Clear();
            Beacon.ResetCaches();
            SeaSurface.ResetCaches();
            DayNightCycle.ResetClock();
        }
    }
}
