using System;
using System.Collections.Generic;
using System.Linq;
using AutomaticRoadblocks.AbstractionLayer;
using JetBrains.Annotations;
using Rage;
using Rage.Native;

namespace AutomaticRoadblocks.Utils.Road
{
    public static class RoadUtils
    {
        private static readonly ILogger Logger = IoC.Instance.GetInstance<ILogger>();

        #region Methods

        /// <summary>
        /// Get the closest road near the given position.
        /// </summary>
        /// <param name="position">Set the position to use as reference.</param>
        /// <param name="nodeType">Set the road type.</param>
        /// <returns>Returns the position of the closest road.</returns>
        public static Road FindClosestRoad(Vector3 position, EVehicleNodeType nodeType)
        {
            Road.NodeInfo closestRoad = null;
            var closestRoadDistance = 99999f;

            foreach (var road in FindNearbyVehicleNodes(position, nodeType))
            {
                var roadDistanceToPosition = Vector3.Distance2D(road.Position, position);

                if (roadDistanceToPosition > closestRoadDistance)
                    continue;

                closestRoad = road;
                closestRoadDistance = roadDistanceToPosition;
            }

            return DiscoverRoadForVehicleNode(closestRoad);
        }

        /// <summary>
        /// Find a road by traversing the current road position towards the heading.
        /// </summary>
        /// <param name="position">The position to start from.</param>
        /// <param name="heading">The heading to traverse the road from.</param>
        /// <param name="distance">The distance to traverse.</param>
        /// <param name="roadType">The road types to follow.</param>
        /// <param name="blacklistedFlags">The flags of a node which should be ignored if present.</param>
        /// <returns>Returns the found round traversed from the start position.</returns>
        public static Road FindRoadTraversing(Vector3 position, float heading, float distance, EVehicleNodeType roadType, ENodeFlag blacklistedFlags)
        {
            FindVehicleNodesWhileTraversing(position, heading, distance, roadType, blacklistedFlags, out var lastFoundNode);
            var startedAt = DateTime.Now.Ticks;
            var road = DiscoverRoadForVehicleNode(lastFoundNode);
            var calculationTime = (DateTime.Now.Ticks - startedAt) / TimeSpan.TicksPerMillisecond;
            Logger.Debug($"Converted the vehicle node into a road in {calculationTime} millis");
            return road;
        }

        /// <summary>
        /// Find roads while traversing the given distance from the current location.
        /// </summary>
        /// <param name="position">The position to start from.</param>
        /// <param name="heading">The heading to traverse the road from.</param>
        /// <param name="distance">The distance to traverse.</param>
        /// <param name="roadType">The road types to follow.</param>
        /// <param name="blacklistedFlags">The flags of a node which should be ignored if present.</param>
        /// <returns>Returns the roads found while traversing the distance.</returns>
        public static ICollection<Road> FindRoadsTraversing(Vector3 position, float heading, float distance, EVehicleNodeType roadType,
            ENodeFlag blacklistedFlags)
        {
            var nodeInfos = FindVehicleNodesWhileTraversing(position, heading, distance, roadType, blacklistedFlags, out _);

            var startedAt = DateTime.Now.Ticks;
            var roads = nodeInfos
                .Select(DiscoverRoadForVehicleNode)
                .ToList();
            var calculationTime = (DateTime.Now.Ticks - startedAt) / TimeSpan.TicksPerMillisecond;
            Logger.Debug($"Converted a total of {nodeInfos.Count} nodes to roads in {calculationTime} millis");
            return roads;
        }

        /// <summary>
        /// Get nearby roads near the given position.
        /// </summary>
        /// <param name="position">Set the position to use as reference.</param>
        /// <param name="nodeType">The allowed node types to return.</param>
        /// <param name="radius">The radius to search for nearby roads.</param>
        /// <returns>Returns the position of the closest road.</returns>
        public static IEnumerable<Road> FindNearbyRoads(Vector3 position, EVehicleNodeType nodeType, float radius)
        {
            Assert.NotNull(position, "position cannot be null");
            Assert.NotNull(nodeType, "nodeType cannot be null");
            var nodes = FindNearbyVehicleNodes(position, nodeType, radius);

            return nodes
                .Select(DiscoverRoadForVehicleNode)
                .ToList();
        }

        /// <summary>
        /// Create a new speed zone.
        /// </summary>
        /// <param name="position">The position of the zone.</param>
        /// <param name="radius">The radius of the zone.</param>
        /// <param name="speed">The max speed within the zone.</param>
        /// <returns>Returns the created zone ID.</returns>
        public static int CreateSpeedZone(Vector3 position, float radius, float speed)
        {
            Assert.NotNull(position, "position cannot be null");
            //VEHICLE::_ADD_SPEED_ZONE_FOR_COORD
            return NativeFunction.CallByHash<int>(0x2CE544C68FB812A0, position.X, position.Y, position.Z, radius, speed, false);
        }

        /// <summary>
        /// Remove a speed zone.
        /// </summary>
        /// <param name="zoneId">The zone id to remove.</param>
        public static void RemoveSpeedZone(int zoneId)
        {
            Assert.NotNull(zoneId, "zoneId cannot be null");
            NativeFunction.CallByHash<int>(0x2CE544C68FB812A0, zoneId);
        }

        /// <summary>
        /// Verify if the given point is on a road.
        /// </summary>
        /// <param name="position">The point position to check.</param>
        /// <returns>Returns true if the position is on a road, else false.</returns>
        public static bool IsPointOnRoad(Vector3 position)
        {
            return NativeFunction.Natives.IS_POINT_ON_ROAD<bool>(position.X, position.Y, position.Z);
        }

        /// <summary>
        /// Verify if the given position is a dirt/offroad location based on the slow road flag.
        /// </summary>
        /// <param name="position">The position to check.</param>
        /// <returns>Returns true when the position is a dirt/offroad, else false.</returns>
        public static bool IsSlowRoad(Vector3 position)
        {
            var nodeId = GetClosestNodeId(position);
            return IsSlowRoad(nodeId);
        }

        #endregion

        #region Functions

        [CanBeNull]
        private static Road.NodeInfo FindVehicleNodeWithHeading(Vector3 position, float heading, EVehicleNodeType nodeType,
            ENodeFlag blacklistedFlags = ENodeFlag.None)
        {
            Logger.Trace($"Searching for vehicle nodes at {position} matching heading {heading}");
            var nodes = FindNearbyVehicleNodes(position, nodeType).ToList();
            var closestNodeDistance = 9999f;
            var closestNode = (Road.NodeInfo)null;
            var ignoreHeading = false;

            // filter out any nodes which match one or more blacklisted conditions
            var filteredNodes = nodes
                .Where(x => (x.Flags & blacklistedFlags) == 0)
                .Where(x => Math.Abs(x.Heading - heading) <= 45f)
                .ToList();

            if (filteredNodes.Count == 0)
            {
                ignoreHeading = true;
                filteredNodes = nodes.Where(x => (x.Flags & blacklistedFlags) == 0).ToList();
            }

            foreach (var node in filteredNodes)
            {
                var distance = position.DistanceTo(node.Position);

                if (distance > closestNodeDistance)
                    continue;

                closestNodeDistance = distance;
                closestNode = node;
            }

            if (closestNode != null && ignoreHeading)
            {
                closestNode = new Road.NodeInfo(closestNode.Position, heading, closestNode.NumberOfLanes1, closestNode.NumberOfLanes2, closestNode.AtJunction)
                {
                    Density = closestNode.Density,
                    Flags = closestNode.Flags
                };
            }

            Logger.Debug(closestNode != null
                ? $"Using node {closestNode} as closest matching"
                : $"No matching node found for position: {position} with heading {heading}");

            return closestNode;
        }

        private static List<Road.NodeInfo> FindVehicleNodesWhileTraversing(Vector3 position, float heading, float distance, EVehicleNodeType nodeType,
            ENodeFlag blacklistedFlags, out Road.NodeInfo lastFoundNodeInfo)
        {
            var startedAt = DateTime.Now.Ticks;
            var nextNodeCalculationStartedAt = DateTime.Now.Ticks;
            var distanceTraversed = 0f;
            var distanceToMove = 5f;
            var findNodeAttempt = 0;
            var nodeInfos = new List<Road.NodeInfo>();

            lastFoundNodeInfo = new Road.NodeInfo(position, heading, -1, -1, -1f);

            while (distanceTraversed < distance)
            {
                if (findNodeAttempt == 10)
                {
                    Logger.Warn($"Failed to traverse road, unable to find next node after {lastFoundNodeInfo} (tried {findNodeAttempt} times)");
                    break;
                }

                var findNodeAt = lastFoundNodeInfo.Position + MathHelper.ConvertHeadingToDirection(lastFoundNodeInfo.Heading) * distanceToMove;
                var nodeTowardsHeading = FindVehicleNodeWithHeading(findNodeAt, lastFoundNodeInfo.Heading, nodeType, blacklistedFlags);

                if (nodeTowardsHeading == null
                    || nodeTowardsHeading.Position.Equals(lastFoundNodeInfo.Position)
                    || nodeInfos.Any(x => x.Equals(nodeTowardsHeading)))
                {
                    distanceToMove *= 1.5f;
                    findNodeAttempt++;
                }
                else
                {
                    var additionalDistanceTraversed = lastFoundNodeInfo.Position.DistanceTo(nodeTowardsHeading.Position);
                    var nextNodeCalculationDuration = (DateTime.Now.Ticks - nextNodeCalculationStartedAt) / TimeSpan.TicksPerMillisecond;
                    nodeInfos.Add(nodeTowardsHeading);
                    distanceToMove = 5f;
                    findNodeAttempt = 0;
                    distanceTraversed += additionalDistanceTraversed;
                    lastFoundNodeInfo = nodeTowardsHeading;
                    Logger.Trace(
                        $"Traversed an additional {additionalDistanceTraversed} distance in {nextNodeCalculationDuration} millis, new total traversed distance = {distanceTraversed}");
                    nextNodeCalculationStartedAt = DateTime.Now.Ticks;
                }
            }

            var calculationTime = (DateTime.Now.Ticks - startedAt) / TimeSpan.TicksPerMillisecond;
            Logger.Debug(
                $"Traversed a total of {position.DistanceTo(lastFoundNodeInfo.Position)} distance with expectation {distance} within {calculationTime} millis\n" +
                $"origin: {position}, destination: {lastFoundNodeInfo.Position}");
            return nodeInfos;
        }

        private static Road DiscoverRoadForVehicleNode(Road.NodeInfo nodeInfo)
        {
            var nodeHeading = nodeInfo.Heading;
            var rightSideHeading = nodeHeading - 90f;
            var leftSideHeading = nodeHeading + 90f;

            if (!LastPointOnRoadUsingRaytracing(nodeInfo.Position, rightSideHeading, out var roadRightSide))
            {
                LastPointOnRoadUsingNative(nodeInfo.Position, rightSideHeading, out roadRightSide);
            }

            if (!LastPointOnRoadUsingRaytracing(nodeInfo.Position, leftSideHeading, out var roadLeftSide))
            {
                LastPointOnRoadUsingNative(nodeInfo.Position, leftSideHeading, out roadLeftSide);
            }

            return new Road
            {
                RightSide = roadRightSide,
                LeftSide = roadLeftSide,
                NumberOfLanes1 = nodeInfo.NumberOfLanes1,
                NumberOfLanes2 = nodeInfo.NumberOfLanes2,
                JunctionIndicator = (int)nodeInfo.AtJunction,
                Lanes = DiscoverLanes(roadRightSide, roadLeftSide, nodeInfo.Position, nodeHeading, nodeInfo.NumberOfLanes1,
                    nodeInfo.NumberOfLanes2),
                Node = nodeInfo,
            };
        }

        private static List<Road.Lane> DiscoverLanes(Vector3 roadRightSide, Vector3 roadLeftSide, Vector3 roadMiddle, float rightSideHeading,
            int numberOfLanes1, int numberOfLanes2)
        {
            var lanes = new List<Road.Lane>();

            // verify if there is currently only one lane
            // then calculate the lane from right to left
            if (numberOfLanes1 == 0 || numberOfLanes2 == 0)
            {
                var numberOfLanes = numberOfLanes1 == 0 ? numberOfLanes2 : numberOfLanes1;
                lanes.AddRange(CreateLanes(roadRightSide, roadLeftSide, rightSideHeading, numberOfLanes, false));
            }
            else
            {
                lanes.AddRange(CreateLanes(roadRightSide, roadMiddle, rightSideHeading, numberOfLanes1, false));
                lanes.AddRange(CreateLanes(roadLeftSide, roadMiddle, MathHelper.NormalizeHeading(rightSideHeading - 180), numberOfLanes2, true));
            }

            return lanes;
        }

        private static IEnumerable<Road.Lane> CreateLanes(Vector3 rightSidePosition, Vector3 leftSidePosition, float laneHeading, int numberOfLanes,
            bool isOpposite)
        {
            var lastRightPosition = rightSidePosition;
            var laneWidth = rightSidePosition.DistanceTo(leftSidePosition) / numberOfLanes;
            var moveDirection = MathHelper.ConvertHeadingToDirection(laneHeading + 90f);
            var lanes = new List<Road.Lane>();

            for (var index = 1; index <= numberOfLanes; index++)
            {
                var laneLeftPosition = lastRightPosition + moveDirection * laneWidth;
                var nodePosition = lastRightPosition + moveDirection * (laneWidth / 2);

                lanes.Add(new Road.Lane
                {
                    Heading = laneHeading,
                    RightSide = lastRightPosition,
                    LeftSide = laneLeftPosition,
                    NodePosition = nodePosition,
                    Width = laneWidth,
                    IsOppositeHeadingOfRoadNodeHeading = isOpposite
                });
                lastRightPosition = laneLeftPosition;
            }

            return lanes;
        }

        private static IEnumerable<Road.NodeInfo> FindNearbyVehicleNodes(Vector3 position, EVehicleNodeType nodeType)
        {
            NativeFunction.Natives.GET_CLOSEST_ROAD(position.X, position.Y, position.Z, 1f, 1, out Vector3 roadPosition1, out Vector3 roadPosition2,
                out int numberOfLanes1, out int numberOfLanes2, out float junctionIndication, (int)ERoadType.All);

            return new List<Road.NodeInfo>
            {
                FindVehicleNode(roadPosition1, nodeType, numberOfLanes1, numberOfLanes2, junctionIndication),
                FindVehicleNode(roadPosition2, nodeType, numberOfLanes2, numberOfLanes1, junctionIndication)
            };
        }

        private static IEnumerable<Road.NodeInfo> FindNearbyVehicleNodes(Vector3 position, EVehicleNodeType nodeType, float radius, int rotationInterval = 20)
        {
            const int radInterval = 2;
            var nodes = new List<Road.NodeInfo>();
            var rad = 1;

            while (rad <= radius)
            {
                for (var rot = 0; rot < 360; rot += rotationInterval)
                {
                    var x = (float)(position.X + rad * Math.Sin(rot));
                    var y = (float)(position.Y + rad * Math.Cos(rot));

                    var node = FindVehicleNode(new Vector3(x, y, position.Z + 5f), nodeType);

                    if (nodes.Contains(node))
                        continue;

                    Logger.Trace($"Discovered new vehicle node, {node}");
                    nodes.Add(node);
                }

                if (radius % radInterval != 0 && rad == (int)radius - (int)radius % radInterval)
                {
                    rad += (int)radius % radInterval;
                }
                else
                {
                    rad += radInterval;
                }
            }

            return nodes;
        }

        private static Road.NodeInfo FindVehicleNode(Vector3 position, EVehicleNodeType type)
        {
            Assert.NotNull(position, "position cannot be null");
            Assert.NotNull(type, "type cannot be null");
            NativeFunction.Natives.GET_CLOSEST_ROAD(position.X, position.Y, position.Z, 1f, 1, out Vector3 roadPosition1, out Vector3 roadPosition2,
                out int numberOfLanes1, out int numberOfLanes2, out float junctionIndication, (int)ERoadType.All);

            return position.DistanceTo2D(roadPosition1) < position.DistanceTo2D(roadPosition2)
                ? FindVehicleNode(roadPosition1, type, numberOfLanes1, numberOfLanes2, junctionIndication)
                : FindVehicleNode(roadPosition1, type, numberOfLanes2, numberOfLanes1, junctionIndication);
        }

        private static Road.NodeInfo FindVehicleNode(Vector3 position, EVehicleNodeType type, int numberOfLanes1, int numberOfLanes2, float junctionIndication)
        {
            Assert.NotNull(position, "position cannot be null");
            Assert.NotNull(type, "type cannot be null");

            NativeFunction.Natives.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING(position.X, position.Y, position.Z, out Vector3 nodePosition, out float nodeHeading,
                (int)type, 3, 0);
            NativeFunction.Natives.GET_VEHICLE_NODE_PROPERTIES<bool>(nodePosition.X, nodePosition.Y, nodePosition.Z, out int density, out int flags);

            return new Road.NodeInfo(nodePosition, MathHelper.NormalizeHeading(nodeHeading), numberOfLanes1, numberOfLanes2, junctionIndication)
            {
                Density = density,
                Flags = (ENodeFlag)flags
            };
        }

        private static bool LastPointOnRoadUsingNative(Vector3 position, float heading, out Vector3 lastPointPosition)
        {
            Vector3 lastPositionNative;
            bool result;

            unsafe
            {
                // PATHFIND::GET_ROAD_BOUNDARY_USING_HEADING
                result = NativeFunction.CallByHash<bool>(0xA0F8A7517A273C05, position.X, position.Y, position.Z, heading, &lastPositionNative);
                lastPointPosition = lastPositionNative;
            }

            return result;
        }

        private static bool LastPointOnRoadUsingRaytracing(Vector3 position, float heading, out Vector3 lastPointOnRoad)
        {
            const float stopAtMinimumInterval = 0.2f;
            const float maxLaneWidth = 25f;
            var intervalCheck = 2f;
            var direction = MathHelper.ConvertHeadingToDirection(heading);
            var roadMaterial = 0U;
            var positionToCheck = position;
            lastPointOnRoad = position;

            Logger.Trace($"Calculating last point on road for position: {position}, heading: {heading}");
            while (intervalCheck > stopAtMinimumInterval)
            {
                var rayTracingDistance = position.DistanceTo(lastPointOnRoad);

                // verify if we're not exceeding the maximum
                // as this indicates that the raytracing isn't working as it supposed to
                if (rayTracingDistance > maxLaneWidth)
                {
                    Logger.Warn(
                        $"Failed to calculate the last point on road using raytracing for position: {position}, heading: {heading} (tracing distance: {rayTracingDistance})");
                    return false;
                }

                var positionOnTheGround = GameUtils.GetOnTheGroundPosition(positionToCheck);
                uint materialHash;

                unsafe
                {
                    bool hit;
                    Vector3 worldHitPosition;
                    Vector3 surfacePosition;
                    uint hitEntity;

                    // SHAPETEST::START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE
                    var handle = NativeFunction.CallByHash<int>(0x377906D8A31E5586, positionOnTheGround.X, positionOnTheGround.Y, positionOnTheGround.Z + 1f,
                        positionOnTheGround.X, positionOnTheGround.Y, positionOnTheGround.Z - 1f,
                        (int)ETraceFlags.IntersectWorld, Game.LocalPlayer.Character, 7);

                    // WORLDPROBE::GET_SHAPE_TEST_RESULT_INCLUDING_MATERIAL
                    NativeFunction.CallByHash<int>(0x65287525D951F6BE, handle, &hit, &worldHitPosition, &surfacePosition, &materialHash, &hitEntity);
                }

                if (roadMaterial == 0U)
                    roadMaterial = materialHash;

                // verify if positionOnTheGround is still on the road
                if (roadMaterial == materialHash)
                {
                    // store the last known position
                    lastPointOnRoad = positionOnTheGround;
                }
                else
                {
                    // reduce the check distance as we're over the road
                    intervalCheck /= 2;
                }

                positionToCheck = lastPointOnRoad + direction * intervalCheck;
            }

            return true;
        }

        private static int GetClosestNodeId(Vector3 position)
        {
            return NativeFunction.CallByName<int>("GET_NTH_CLOSEST_VEHICLE_NODE_ID", position.X, position.Y, position.Z, 1, 1, 1077936128, 0f);
        }

        /// <summary>
        /// Verify if the given node is is offroad.
        /// </summary>
        /// <param name="nodeId">The node id to check.</param>
        /// <returns>Returns true when the node is an alley, dirt road or carpark.</returns>
        private static bool IsSlowRoad(int nodeId)
        {
            // PATHFIND::_GET_IS_SLOW_ROAD_FLAG
            return NativeFunction.CallByHash<bool>(0x4F5070AA58F69279, nodeId);
        }

        #endregion
    }
}