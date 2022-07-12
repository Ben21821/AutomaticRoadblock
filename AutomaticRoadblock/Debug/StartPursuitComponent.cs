using System.Linq;
using AutomaticRoadblocks.AbstractionLayer;
using AutomaticRoadblocks.Menu;
using AutomaticRoadblocks.Utils;
using AutomaticRoadblocks.Utils.Road;
using LSPD_First_Response.Mod.API;
using Rage;
using RAGENativeUI.Elements;

namespace AutomaticRoadblocks.Debug
{
    public class StartPursuitComponent : IMenuComponent<UIMenuItem>
    {
        private readonly IGame _game;

        private LHandle _currentPursuit;

        public StartPursuitComponent(IGame game)
        {
            _game = game;
        }

        /// <inheritdoc />
        public UIMenuItem MenuItem { get; } = new(AutomaticRoadblocksPlugin.StartPursuit);

        /// <inheritdoc />
        public MenuType Type => MenuType.DEBUG;

        /// <inheritdoc />
        public bool IsAutoClosed => true;

        /// <inheritdoc />
        public void OnMenuActivation(IMenu sender)
        {
            if (_currentPursuit == null)
            {
                StartPursuit();
            }
            else
            {
                EndPursuit();
            }
        }

        private void StartPursuit()
        {
            _game.NewSafeFiber(() =>
            {
                _currentPursuit = Functions.CreatePursuit();

                var road = RoadUtils.GetClosestRoad(_game.PlayerPosition + MathHelper.ConvertHeadingToDirection(_game.PlayerHeading) * 25f, RoadType.All);
                var lane = road.Lanes.First();
                var ped = new Ped(road.Position);
                var vehicle = new Vehicle(new Model("Buffalo3"), lane.Position, lane.Heading);

                ped.Inventory.GiveNewWeapon(new WeaponAsset(ModelUtils.Weapons.Pistol), -1, true);
                ped.WarpIntoVehicle(vehicle, (int)VehicleSeat.Driver);

                Functions.AddPedToPursuit(_currentPursuit, ped);
                Functions.SetPursuitIsActiveForPlayer(_currentPursuit, true);
                MenuItem.Text = AutomaticRoadblocksPlugin.EndPursuit;
            }, "StartPursuitComponent.StartPursuit");
        }

        private void EndPursuit()
        {
            Functions.ForceEndPursuit(_currentPursuit);
            MenuItem.Text = AutomaticRoadblocksPlugin.StartPursuit;
        }
    }
}