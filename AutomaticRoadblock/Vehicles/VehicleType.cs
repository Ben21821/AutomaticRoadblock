using System.Collections.Generic;
using AutomaticRoadblocks.Localization;

namespace AutomaticRoadblocks.Vehicles
{
    public class VehicleType
    {
        public static readonly VehicleType Locale = new(LocalizationKey.VehicleTypeLocale);
        public static readonly VehicleType State = new(LocalizationKey.VehicleTypeState);
        public static readonly VehicleType Fbi = new(LocalizationKey.VehicleTypeFbi);
        public static readonly VehicleType Swat = new(LocalizationKey.VehicleTypeSwat);
        public static readonly VehicleType Transporter = new(LocalizationKey.VehicleTypeTransporter);
        public static readonly VehicleType None = new(LocalizationKey.VehicleTypeNone);

        public static readonly IEnumerable<VehicleType> Values = new[]
        {
            Locale,
            State,
            Fbi,
            Swat,
            Transporter,
            None
        };

        private VehicleType(LocalizationKey localizationKey)
        {
            LocalizationKey = localizationKey;
        }
        
        /// <summary>
        /// The localization key for this type.
        /// </summary>
        public LocalizationKey LocalizationKey { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return LocalizationKey.DefaultText;
        }
    }
}