using Rage;

namespace AutomaticRoadblocks.Pursuit
{
    public interface IPursuitManager
    {
        /// <summary>
        /// Verify if there is currently an active pursuit.
        /// </summary>
        bool IsPursuitActive { get; }

        /// <summary>
        /// Verify if the pursuit is on foot and not anymore in vehicles.
        /// </summary>
        /// <exception cref="NoPursuitActiveException">Is thrown when this property is called and <see cref="IsPursuitActive"/> is false.</exception>
        bool IsPursuitOnFoot { get; }

        /// <summary>
        /// Enable automatic dispatching of roadblocks during a pursuit.
        /// </summary>
        bool EnableAutomaticDispatching { get; set; }

        /// <summary>
        /// Enable automatic level increases during a pursuit.
        /// </summary>
        bool EnableAutomaticLevelIncreases { get; set; }

        /// <summary>
        /// Retrieve the current pursuit level.
        /// </summary>
        PursuitLevel PursuitLevel { get; set; }

        /// <summary>
        /// Invoked when the pursuit state changes.
        /// </summary>
        event PursuitEvents.PursuitStateChangedEventHandler PursuitStateChanged;

        /// <summary>
        /// Invoked when the pursuit level changes.
        /// </summary>
        event PursuitEvents.PursuitLevelChangedEventHandler PursuitLevelChanged;

        void StartListener();

        void StopListener();

        /// <summary>
        /// Retrieve a vehicle of one of the suspects within the pursuit.
        /// Verify if there is an active pursuit through <see cref="IsPursuitActive"/>,
        /// otherwise, this method will return null.
        /// </summary>
        /// <returns>Returns a vehicle in the pursuit, else null when there is no active pursuit.</returns>
        Vehicle GetSuspectVehicle();

        /// <summary>
        /// Dispatch a roadblock for the current pursuit.
        /// </summary>
        /// <param name="userRequested">Indicates if the roadblock is requested by the user.</param>
        /// <param name="force">Force the spawning of a roadblock, this will disable the verification of conditions which are applied before a roadblock can be dispatched.</param>
        /// <param name="atCurrentLocation">Indicates if the roadblock location should be calculated or the current location of the target should be used.</param>
        /// <returns>Returns true if a roadblock will be dispatched, else false.</returns>
        bool DispatchNow(bool userRequested = false, bool force = false, bool atCurrentLocation = false);

        /// <summary>
        /// Dispatch a preview of a possible roadblock.
        /// This preview doesn't require an active pursuit, but required that the player has at least a vehicle to determine the roadblock location.
        /// </summary>
        void DispatchPreview(bool currentLocation);
    }
}