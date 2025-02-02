using System;
using System.Collections.Generic;
using System.Linq;
using AutomaticRoadblocks.Barriers;
using AutomaticRoadblocks.Instances;
using AutomaticRoadblocks.Roadblock;
using AutomaticRoadblocks.Roadblock.Slot;
using AutomaticRoadblocks.Utils.Road;
using Rage;

namespace AutomaticRoadblocks.Pursuit.Level
{
    internal class PursuitRoadblockLevel1 : AbstractPursuitRoadblock
    {
        public PursuitRoadblockLevel1(Road road, Vehicle vehicle, bool limitSpeed, bool addLights)
            : base(road, BarrierType.SmallCone, vehicle, limitSpeed, addLights)
        {
        }

        #region Properties

        /// <inheritdoc />
        public override RoadblockLevel Level => RoadblockLevel.Level1;

        #endregion

        #region Functions

        /// <inheritdoc />
        protected override void InitializeScenery()
        {
            var position = Position + MathHelper.ConvertHeadingToDirection(MathHelper.NormalizeHeading(Road.Node.Heading - 180)) * 2f;

            Instances.Add(new InstanceSlot(EntityType.Scenery, position, 0f,
                (conePosition, _) => BarrierFactory.Create(BarrierType.BigCone, conePosition)));
        }

        /// <inheritdoc />
        protected override void InitializeLights()
        {
            // no-op
        }

        /// <inheritdoc />
        protected override IReadOnlyList<Road.Lane> LanesToBlock()
        {
            var lanesToBlock = base.LanesToBlock();

            // only block the lanes in the same direction as the pursuit
            return lanesToBlock
                .Where(x => Math.Abs(x.Heading - TargetHeading) < LaneHeadingTolerance)
                .ToList();
        }

        /// <inheritdoc />
        protected override IRoadblockSlot CreateSlot(Road.Lane lane, float heading, Vehicle targetVehicle, bool shouldAddLights)
        {
            return new PursuitRoadblockSlotLevel1(lane, MainBarrierType, heading, targetVehicle, shouldAddLights);
        }

        #endregion
    }
}