using UnityEngine;

namespace TheBlock.Vehicles
{
    /// <summary>
    /// Which colour each vehicle was painted at the auto shop - U35g. <c>PlayerPrefs</c>, beside
    /// <c>Progress</c>, one int per vehicle: <c>theblock.paint.&lt;name&gt;</c> = <c>0xRRGGBB</c>.
    ///
    /// <b>Keyed per vehicle, not one global colour</b> - and "vehicle" means the config's spawn name
    /// (<c>config.vehicle.cars[i].name</c>, and <c>Motorcycle</c>), because that is the only identity
    /// that survives a reload: <c>CarSpawner</c> spawns those in config order at config positions
    /// every boot. A car promoted from the lot or hijacked off the street keeps its paint for the
    /// session (the material stays on its renderers) and is not stored - it did not exist at boot and
    /// will not exist at the next one, so there is nothing to key on. <see cref="CarPaint.PersistKey"/>
    /// is null on those and the shop writes nothing.
    ///
    /// Not a preference, so New Game clears it (<c>GameFlow.NewGame</c> calls <see cref="Reset"/>) -
    /// it clears the wallet that paid for the coat. <c>PlayerPrefs</c> cannot enumerate, so the reset
    /// walks the config's names, exactly as <c>PowerUps.Reset</c> walks its catalogue.
    /// </summary>
    public static class PaintStore
    {
        private const string KeyPrefix = "theblock.paint.";

        /// <summary>The store key for the one motorcycle. Its spawner names it this.</summary>
        public const string MotorcycleKey = "Motorcycle";

        public static bool TryGet(string vehicleKey, out int hex)
        {
            hex = -1;
            if (string.IsNullOrEmpty(vehicleKey)) return false;
            var key = KeyPrefix + vehicleKey;
            if (!PlayerPrefs.HasKey(key)) return false;
            hex = PlayerPrefs.GetInt(key, -1);
            return hex >= 0;
        }

        public static void Set(string vehicleKey, int hex)
        {
            if (string.IsNullOrEmpty(vehicleKey)) return;
            PlayerPrefs.SetInt(KeyPrefix + vehicleKey, hex);
            PlayerPrefs.Save();
        }

        public static void Clear(string vehicleKey)
        {
            if (string.IsNullOrEmpty(vehicleKey)) return;
            PlayerPrefs.DeleteKey(KeyPrefix + vehicleKey);
            PlayerPrefs.Save();
        }

        /// <summary>New Game: every config vehicle back to its built colour.</summary>
        public static void Reset()
        {
            var cars = Core.TheBlockConfig.Load()?.Config?.Vehicle?.Cars;
            if (cars != null)
                foreach (var car in cars)
                    if (car != null && !string.IsNullOrEmpty(car.Name))
                        PlayerPrefs.DeleteKey(KeyPrefix + car.Name);

            PlayerPrefs.DeleteKey(KeyPrefix + MotorcycleKey);
            PlayerPrefs.Save();
        }
    }
}
