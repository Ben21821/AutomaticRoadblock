using System.Collections.Generic;
using System.Linq;
using AutomaticRoadblocks.AbstractionLayer;
using AutomaticRoadblocks.Localization;
using AutomaticRoadblocks.Pursuit.Factory;
using AutomaticRoadblocks.Settings;
using AutomaticRoadblocks.Utils;
using AutomaticRoadblocks.Utils.Road;
using Rage;

namespace AutomaticRoadblocks.Roadblock.Dispatcher
{
    public class RoadblockDispatcher : IRoadblockDispatcher
    {
        private const float MinimumVehicleSpeed = 20f;
        private const float MinimumRoadblockPlacementDistance = 175f;
        private const int AutoCleanRoadblockAfterSeconds = 60;
        private const float RoadblockCleanupDistanceFromPlayer = 75f;
        private const float MinimumDistanceBetweenRoadblocks = 10f;
        private const string AudioRequestDenied = "ROADBLOCK_REQUEST_DENIED";
        private const string AudioRequestConfirmed = "ROADBLOCK_REQUEST_CONFIRMED";
        private const string AudioRoadblockBypassed = "ROADBLOCK_BYPASSED";
        private const string AudioRoadblockHit = "ROADBLOCK_HIT";

        private readonly ILogger _logger;
        private readonly IGame _game;
        private readonly ISettingsManager _settingsManager;
        private readonly ILocalizer _localizer;

        private readonly List<IRoadblock> _roadblocks = new();
        private readonly List<Road> _foundRoads = new();

        private bool _cleanerRunning;
        private bool _userRequestedRoadblockDispatching;

        public RoadblockDispatcher(ILogger logger, IGame game, ISettingsManager settingsManager, ILocalizer localizer)
        {
            _logger = logger;
            _game = game;
            _settingsManager = settingsManager;
            _localizer = localizer;
        }

        #region Properties

        /// <inheritdoc />
        public IEnumerable<IRoadblock> Roadblocks => _roadblocks;

        #endregion

        #region Events

        /// <inheritdoc />
        public event RoadblockEvents.RoadblockStateChanged RoadblockStateChanged;

        /// <inheritdoc />
        public event RoadblockEvents.RoadblockCopKilled RoadblockCopKilled;

        /// <inheritdoc />
        public event RoadblockEvents.RoadblockCopsJoiningPursuit RoadblockCopsJoiningPursuit;

        #endregion

        #region IRoadblockDispatcher

        /// <inheritdoc />
        public bool Dispatch(RoadblockLevel level, Vehicle vehicle, bool userRequested, bool force, bool atCurrentLocation = false)
        {
            Assert.NotNull(level, "level cannot be null");
            Assert.NotNull(vehicle, "vehicle cannot be null");

            _logger.Trace(
                $"Starting roadblock dispatching with {nameof(level)}: {level}, {nameof(userRequested)}: {userRequested}, {nameof(force)}: {force}, {nameof(atCurrentLocation)}: {atCurrentLocation}");
            if (force || userRequested || IsRoadblockDispatchingAllowed(vehicle))
                return DoInternalDispatch(level, vehicle, userRequested, atCurrentLocation);

            _logger.Info($"Dispatching of a roadblock is not allowed with {nameof(level)}: {level}, {nameof(atCurrentLocation)}: {atCurrentLocation}");
            return false;
        }

        /// <inheritdoc />
        public void DispatchPreview(RoadblockLevel level, Vehicle vehicle, bool atCurrentLocation)
        {
            Assert.NotNull(level, "level cannot be null");
            Assert.NotNull(vehicle, "vehicle cannot be null");

            _game.NewSafeFiber(() =>
            {
                _logger.Debug("Dispatching new roadblock preview");
                var roads = DetermineRoadblockLocationPreview(level, vehicle, atCurrentLocation);
                var road = roads.Last();
                _logger.Trace($"Dispatching roadblock on {road}");

                _game.DisplayNotification(_localizer[LocalizationKey.RoadblockDispatchedAt, World.GetStreetName(road.Position)]);
                var roadblock = PursuitRoadblockFactory.Create(level, road, vehicle, _settingsManager.AutomaticRoadblocksSettings.SlowTraffic,
                    ShouldAddLightsToRoadblock());

                _roadblocks.Add(roadblock);
                _foundRoads.AddRange(roads);

                roadblock.CreatePreview();
                _foundRoads.ForEach(x => x.CreatePreview());
            }, "RoadblockDispatcher.DispatchPreview");
        }

        /// <inheritdoc />
        public void DismissActiveRoadblocks()
        {
            List<IRoadblock> roadblocksToRelease;

            lock (_roadblocks)
            {
                roadblocksToRelease = _roadblocks
                    .Where(x => x.State == RoadblockState.Active)
                    .ToList();

                _roadblocks.RemoveAll(x => roadblocksToRelease.Contains(x));
            }

            roadblocksToRelease.ForEach(x => x.Release());
            _logger.Debug($"Dismissed a total of {roadblocksToRelease.Count} roadblocks which were still active");
        }

        #endregion

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            _logger.Trace($"Disposing {_roadblocks.Count} roadblock(s)");
            _cleanerRunning = false;
            _roadblocks.ForEach(x => x.Dispose());
            _roadblocks.Clear();
            _foundRoads.ForEach(x => x.DeletePreview());
            _foundRoads.Clear();
            _logger.Debug("Roadblocks have been disposed");
        }

        #endregion

        #region Functions

        private bool IsRoadblockDispatchingAllowed(Vehicle vehicle)
        {
            return vehicle.Speed >= MinimumVehicleSpeed;
        }

        private bool ShouldAddLightsToRoadblock()
        {
            return _settingsManager.AutomaticRoadblocksSettings.EnableLights &&
                   GameUtils.TimePeriod is TimePeriod.Evening or TimePeriod.Night;
        }

        private bool DoInternalDispatch(RoadblockLevel level, Vehicle vehicle, bool userRequest, bool atCurrentLocation)
        {
            // start the cleaner if it's not yet running
            if (!_cleanerRunning)
                StartCleaner();

            // verify if a user requested roadblock is still being dispatched
            // because of the fact that a user requested roadblock plays blocking audio,
            // the roadblock might still not have been deployed when a new one is requested
            if (_userRequestedRoadblockDispatching)
                return DenyUserRequestForRoadblock(userRequest, "user requested roadblock is currently being dispatched");

            if (userRequest)
                AllowUserRequestForRoadblock();

            _logger.Debug($"Dispatching new roadblock with {nameof(userRequest)}: {userRequest}, {nameof(atCurrentLocation)}: {atCurrentLocation}");
            // calculate the roadblock location
            var road = DetermineRoadblockLocation(level, vehicle, atCurrentLocation);

            // verify if another roadblock is already present nearby
            // if so, deny the roadblock request
            if (IsRoadblockNearby(road))
                return DenyUserRequestForRoadblock(userRequest, $"a roadblock is already present in the vicinity for {road}");

            _game.NewSafeFiber(() =>
                {
                    var roadblock = PursuitRoadblockFactory.Create(level, road, vehicle, _settingsManager.AutomaticRoadblocksSettings.SlowTraffic,
                        ShouldAddLightsToRoadblock());
                    _logger.Info($"Dispatching new roadblock\n{roadblock}");
                    _roadblocks.Add(roadblock);

                    // subscribe to the roadblock events
                    roadblock.RoadblockStateChanged += InternalRoadblockStateChanged;
                    roadblock.RoadblockCopKilled += InternalRoadblockCopKilled;
                    roadblock.RoadblockCopsJoiningPursuit += InternalRoadblockCopsJoiningThePursuit;

                    _logger.Trace($"Distance between vehicle and roadblock before spawn {road.Position.DistanceTo(vehicle.Position)}");
                    roadblock.Spawn();
                    _logger.Trace($"Distance between vehicle and roadblock after spawn {road.Position.DistanceTo(vehicle.Position)}");
                    _game.DisplayNotification(_localizer[LocalizationKey.RoadblockDispatchedAt, World.GetStreetName(road.Position)]);
                    _logger.Info($"Roadblock has been dispatched, {roadblock}");
                    LspdfrUtils.PlayScannerAudioNonBlocking("ROADBLOCK_DEPLOYED");
                    _userRequestedRoadblockDispatching = false;
                },
                "RoadblockDispatcher.Dispatch");
            return true;
        }

        private bool IsRoadblockNearby(Road road)
        {
            bool isThereANearbyRoadblock;

            lock (_roadblocks)
            {
                isThereANearbyRoadblock = _roadblocks
                    // filter out any previews and roadblocks in error state
                    // as we don't want them to prevent a roadblock placement
                    .Where(x => !x.IsPreviewActive && x.State != RoadblockState.Error)
                    .Any(x => x.Position.DistanceTo(road.Position) <= MinimumDistanceBetweenRoadblocks);
            }

            return isThereANearbyRoadblock;
        }

        private Road DetermineRoadblockLocation(RoadblockLevel level, Vehicle vehicle, bool atCurrentLocation)
        {
            var roadblockDistance = CalculateRoadblockDistance(vehicle, atCurrentLocation);
            var roadType = DetermineAllowedRoadTypes(level);

            _logger.Trace($"Determining roadblock location with {nameof(roadblockDistance)}: {roadblockDistance}, {nameof(roadType)}: {roadType}");
            return RoadUtils.FindRoadTraversing(vehicle.Position, vehicle.Heading, roadblockDistance, roadType);
        }

        private ICollection<Road> DetermineRoadblockLocationPreview(RoadblockLevel level, Vehicle vehicle, bool atCurrentLocation)
        {
            var roadblockDistance = CalculateRoadblockDistance(vehicle, atCurrentLocation);
            var roadType = DetermineAllowedRoadTypes(level);

            _logger.Trace(
                $"Determining roadblock location for the preview with {nameof(roadblockDistance)}: {roadblockDistance}, {nameof(roadType)}: {roadType}");
            return RoadUtils.FindRoadsTraversing(vehicle.Position, vehicle.Heading, roadblockDistance, roadType);
        }

        private void InternalRoadblockStateChanged(IRoadblock roadblock, RoadblockState newState)
        {
            _logger.Debug($"Roadblock state changed to {newState}");
            _game.NewSafeFiber(() =>
            {
                switch (newState)
                {
                    case RoadblockState.Hit:
                        _game.DisplayNotification(_localizer[LocalizationKey.RoadblockHasBeenHit]);
                        LspdfrUtils.PlayScannerAudioNonBlocking(AudioRoadblockHit);
                        break;
                    case RoadblockState.Bypassed:
                        _game.DisplayNotification(_localizer[LocalizationKey.RoadblockHasBeenBypassed]);
                        LspdfrUtils.PlayScannerAudioNonBlocking(AudioRoadblockBypassed);
                        break;
                    case RoadblockState.Disposed:
                        _logger.Trace($"Removing roadblock {roadblock} from dispatcher");
                        _roadblocks.Remove(roadblock);
                        break;
                }
            }, "RoadblockDispatcher.RoadblockStateChanged");
            RoadblockStateChanged?.Invoke(roadblock, newState);
        }

        private void InternalRoadblockCopKilled(IRoadblock roadblock)
        {
            _logger.Debug($"A roadblock cop has been killed");
            RoadblockCopKilled?.Invoke(roadblock);
        }

        private void InternalRoadblockCopsJoiningThePursuit(IRoadblock roadblock, IEnumerable<Ped> cops)
        {
            RoadblockCopsJoiningPursuit?.Invoke(roadblock, cops);
        }

        private void StartCleaner()
        {
            _logger.Trace("Starting the roadblock dispatcher cleaner");
            _cleanerRunning = true;
            _game.NewSafeFiber(() =>
            {
                _logger.Info("Roadblock dispatch cleaner started");
                while (_cleanerRunning)
                {
                    _roadblocks
                        .Where(x => !x.IsPreviewActive && x.State is not RoadblockState.Active or RoadblockState.Preparing or RoadblockState.Disposing)
                        // verify if the player if far enough away for the roadblock to be cleaned
                        // if not, we auto clean roadblocks after AutoCleanRoadblockAfterSeconds
                        .Where(x => IsPlayerFarAwayFromRoadblock(x) || IsAutoRoadblockCleaningAllowed(x))
                        .ToList()
                        .ForEach(x => x.Dispose());
                    GameFiber.Wait(10 * 1000);
                }
            }, "RoadblockDispatcher.StartCleaner");
        }

        private bool IsPlayerFarAwayFromRoadblock(IRoadblock roadblock)
        {
            return _game.PlayerPosition.DistanceTo(roadblock.Position) > RoadblockCleanupDistanceFromPlayer;
        }

        private bool IsAutoRoadblockCleaningAllowed(IRoadblock roadblock)
        {
            return _game.GameTime - roadblock.LastStateChange >= AutoCleanRoadblockAfterSeconds * 1000;
        }

        private void AllowUserRequestForRoadblock()
        {
            _logger.Trace("Playing roadblock requested by user audio");
            _userRequestedRoadblockDispatching = true;
            LspdfrUtils.PlayScannerAudio(AudioRequestConfirmed, true);
        }

        private bool DenyUserRequestForRoadblock(bool userRequest, string reason)
        {
            _logger.Warn("Dispatching new roadblock is not allowed, " + reason);
            if (userRequest)
                LspdfrUtils.PlayScannerAudioNonBlocking(AudioRequestDenied);

            return false;
        }

        private static float CalculateRoadblockDistance(Vehicle vehicle, bool atCurrentLocation)
        {
            return atCurrentLocation ? 2.5f : DetermineRoadblockDistanceFor(vehicle);
        }

        private static float DetermineRoadblockDistanceFor(Vehicle vehicle)
        {
            var vehicleSpeed = vehicle.Speed;
            var distance = vehicleSpeed * 3.5f;

            if (distance < MinimumRoadblockPlacementDistance)
                distance = MinimumRoadblockPlacementDistance;

            return distance;
        }

        private static VehicleNodeType DetermineAllowedRoadTypes(RoadblockLevel level)
        {
            return level.Level <= RoadblockLevel.Level2.Level ? VehicleNodeType.AllRoadNoJunctions : VehicleNodeType.MainRoads;
        }

        #endregion
    }
}