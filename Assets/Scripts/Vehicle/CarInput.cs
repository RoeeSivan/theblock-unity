namespace TheBlock.Vehicles
{
    /// <summary>
    /// One frame of driving intent: what a driver asks the car for, with no idea who is asking.
    ///
    /// <b>Why this exists.</b> Until U19 the only driver was the keyboard, read inline inside
    /// <see cref="CarController"/>'s FixedUpdate, so "drive a car" and "read W/A/S/D" were the same
    /// code. A police pursuit needs a second driver, and the cheap version of that — give the AI its
    /// own steering and torque code — would produce a cop that corners on different physics from the
    /// player, which is the one thing a chase cannot afford. With this struct there is still exactly
    /// one <see cref="CarController.ApplySteering"/> and one <c>ApplyDrive</c>: the same rate limit,
    /// the same speed-sensitive lock, the same rear-wheel drive, the same brake-then-reverse rule.
    /// The AI and the player differ only in what they ask for.
    ///
    /// All three fields are intent, not achievement — exactly like the keys they replace.
    /// </summary>
    public readonly struct CarInput
    {
        /// <summary>−1 full brake/reverse … +1 full throttle. What W and S produced.</summary>
        public readonly float Throttle;

        /// <summary>−1 full left … +1 full right, before the speed-sensitive lock is applied.</summary>
        public readonly float Steer;

        public readonly bool Handbrake;

        public CarInput(float throttle, float steer, bool handbrake = false)
        {
            Throttle = throttle;
            Steer = steer;
            Handbrake = handbrake;
        }

        /// <summary>
        /// Nothing held. This is what an absent driver looks like, and it coasts rather than
        /// freewheeling: <c>ApplyDrive</c> reads zero throttle as the coast brake.
        /// </summary>
        public static CarInput None => default;
    }
}
