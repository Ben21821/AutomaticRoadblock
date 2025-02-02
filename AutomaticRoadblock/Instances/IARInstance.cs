using System;
using System.Diagnostics.CodeAnalysis;
using Rage;

namespace AutomaticRoadblocks.Instances
{
    /// <summary>
    /// Interface wrapper for a game <see cref="Entity"/>.
    /// The wrapper provides, based on the type, additional helper functions for the <see cref="Entity"/>.
    /// </summary>
    /// <typeparam name="TType"></typeparam>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public interface IARInstance<out TType> : IDisposable where TType : Entity
    {
        /// <summary>
        /// Get the game instance of this ARInstance.
        /// </summary>
        TType GameInstance { get; }
        
        /// <summary>
        /// Verify if the instance is no longer valid.
        /// This might be the case if the game engine has removed the entity from the game world.
        /// </summary>
        bool IsInvalid { get; }

        /// <summary>
        /// Release the <see cref="Entity"/> back to the game world.
        /// This method removes persistence and other attributes on the <see cref="Entity"/> which might be memory intensive.
        /// </summary>
        void Release();
    }
}