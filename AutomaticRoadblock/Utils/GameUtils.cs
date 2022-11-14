using System.Drawing;
using System.Windows.Forms;
using AutomaticRoadblocks.Utils.Type;
using Rage;
using Rage.Native;

namespace AutomaticRoadblocks.Utils
{
    public static class GameUtils
    {
        /// <summary>
        /// Get the current hour of the game.
        /// </summary>
        /// <returns>Returns the hour of the game.</returns>
        public static int GetCurrentHour()
        {
            return World.TimeOfDay.Hours;
        }

        /// <summary>
        /// Get the current time period of the game.
        /// </summary>
        public static ETimePeriod TimePeriod
        {
            get
            {
                var hour = GetCurrentHour();

                return hour switch
                {
                    >= 6 and < 8 => ETimePeriod.Morning,
                    >= 8 and < 12 => ETimePeriod.Forenoon,
                    >= 12 and < 18 => ETimePeriod.Afternoon,
                    >= 18 and < 20 => ETimePeriod.Evening,
                    _ => ETimePeriod.Night
                };

                // otherwise, it's night
            }
        }

        /// <summary>
        /// Get a new vector that is placed on the ground in the game world for the current position.
        /// The Z vector needs to be above the ground before a position can be determined, otherwise, the result will be <see cref="Vector3.Zero"/>.
        /// Modify the <see cref="heightOffset"/> if needed to prevent an inconclusive <see cref="Vector3.Zero"/> result.
        /// </summary>
        /// <param name="position">The game world position.</param>
        /// <param name="heightOffset">The height offset to apply to the Z vector before getting the coordinate.</param>
        /// <returns>Returns a new Vector that is placed on the ground.</returns>
        public static Vector3 GetOnTheGroundPosition(Vector3 position, float heightOffset = 5f)
        {
            var newPosition = new Vector3(position.X, position.Y, 0f);

            NativeFunction.Natives.GET_GROUND_Z_FOR_3D_COORD<bool>(position.X, position.Y, position.Z + heightOffset, out float newHeight, 0);
            newPosition.Z = newHeight;

            return newPosition;
        }

        /// <summary>
        /// Create a marker at the given position.
        /// </summary>
        /// <param name="position">The position of the marker.</param>
        /// <param name="markerType">The marker type.</param>
        /// <param name="color">The color of the marker.</param>
        /// <param name="size">The size of the marker.</param>
        /// <param name="height">The height of the marker.</param>
        /// <param name="rotate">Set if the marker should be rotating or not.</param>
        public static void CreateMarker(Vector3 position, EMarkerType markerType, Color color, float size = 1f, float height = 1f, bool rotate = true)
        {
            Assert.NotNull(position, "position cannot be null");
            Assert.NotNull(markerType, "markerType cannot be null");
            Assert.NotNull(color, "color cannot be null");
            NativeFunction.CallByName<uint>("DRAW_MARKER", (int)markerType, position.X, position.Y, position.Z, position.X, position.Y, position.Z, 0f, 0f, 0f,
                size, size, height, color.R, color.G, color.B, 100, false, rotate, "", "", false);
        }

        /// <summary>
        /// Verify if the given key is pressed.
        /// </summary>
        /// <param name="key">The primary key.</param>
        /// <param name="secondKey">The optional secondary key.</param>
        /// <returns>Returns true when the key/key-combo is pressed.</returns>
        public static bool IsKeyPressed(Keys key, Keys secondKey = Keys.None) 
        {
            Assert.NotNull(key, "key cannot be null");
            Assert.NotNull(secondKey, "secondKey cannot be null");
            var secondKeyDown = secondKey == Keys.None;

            if (!secondKeyDown && Game.IsKeyDownRightNow(secondKey))
                secondKeyDown = true;

            return Game.IsKeyDown(key) && secondKeyDown;
        }
    }
}