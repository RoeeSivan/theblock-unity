using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// The keyboard, as a <see cref="CarInput"/>.
    ///
    /// This is the half of the old <c>CarController.FixedUpdate</c> that was never physics: W/S,
    /// A/D or the arrows, and space. Lifting it out is what lets U19's cop drive the identical car
    /// model without either driver knowing about the other - and it keeps the keys in one file, so
    /// the day the game grows a gamepad there is one place to add it.
    ///
    /// Read every FIXED step, deliberately, not in Update: <c>isPressed</c> is a level, not an edge,
    /// so sampling it on the physics tick loses nothing (memory: <c>synthetic-play-test-decays</c> -
    /// it is <c>wasPressedThisFrame</c> that must never be sampled off the player loop).
    /// </summary>
    public static class PlayerCarInput
    {
        public static CarInput Read()
        {
            // A frozen game hands back neutral rather than the last thing the driver was holding.
            // `Time.timeScale = 0` already stops FixedUpdate outright, so in practice nothing calls
            // this while paused - but a WheelCollider LATCHES the last torque it was given (memory:
            // wheelcollider-latches-last-torque), so "in practice" is the wrong standard for the one
            // function that decides what the wheels were last told.
            if (Core.Pause.Frozen) return CarInput.None;

            var keyboard = Keyboard.current;
            if (keyboard == null) return CarInput.None;

            return new CarInput(
                Held(keyboard.wKey, keyboard.upArrowKey) - Held(keyboard.sKey, keyboard.downArrowKey),
                Held(keyboard.dKey, keyboard.rightArrowKey) - Held(keyboard.aKey, keyboard.leftArrowKey),
                keyboard.spaceKey.isPressed);
        }

        private static float Held(KeyControl primary, KeyControl alternate) =>
            primary.isPressed || alternate.isPressed ? 1f : 0f;
    }
}
