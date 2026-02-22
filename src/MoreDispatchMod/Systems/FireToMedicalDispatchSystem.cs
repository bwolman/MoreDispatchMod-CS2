using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Simulation;
using Game.Tools;

using MoreDispatchMod.Components;

using Unity.Collections;
using Unity.Entities;

namespace MoreDispatchMod.Systems
{
    public partial class FireToMedicalDispatchSystem : GameSystemBase
    {
        // Citizens with HealthProblem in a building — candidates for dispatch
        private EntityQuery m_CitizenQuery;

        // Tracker entities — for dedup and cleanup
        private EntityQuery m_TrackerQuery;

        private EndFrameBarrier m_EndFrameBarrier;
        private SimulationSystem m_SimulationSystem;
        private int m_LogCounter;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_CitizenQuery = GetEntityQuery(
                ComponentType.ReadOnly<HealthProblem>(),
                ComponentType.ReadOnly<CurrentBuilding>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_TrackerQuery = GetEntityQuery(
                ComponentType.ReadOnly<FireMedicalTracker>());
        }

        protected override void OnUpdate()
        {
            bool shouldLog = false;
            m_LogCounter++;
            if (m_LogCounter >= 64)
            {
                m_LogCounter = 0;
                shouldLog = true;
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();

            int cleaned = 0;

            // === CLEANUP (always runs, even if toggle is off) ===

            var trackerEntities = m_TrackerQuery.ToEntityArray(Allocator.Temp);

            // Count active tracker references per building so we only remove
            // RescueTarget when the last citizen referencing it is cleaned up.
            var buildingRefCounts = new NativeHashMap<Entity, int>(trackerEntities.Length, Allocator.Temp);
            var trackersToClean = new NativeList<Entity>(trackerEntities.Length, Allocator.Temp);
            var trackedCitizens = new NativeHashSet<Entity>(trackerEntities.Length, Allocator.Temp);

            for (int i = 0; i < trackerEntities.Length; i++)
            {
                Entity tracker = trackerEntities[i];
                FireMedicalTracker tag = EntityManager.GetComponentData<FireMedicalTracker>(tracker);
                Entity citizen = tag.m_CitizenEntity;

                bool needsCleanup = false;

                if (citizen == Entity.Null || !EntityManager.Exists(citizen))
                {
                    needsCleanup = true;
                }
                else if (!EntityManager.HasComponent<HealthProblem>(citizen))
                {
                    needsCleanup = true;
                }
                else
                {
                    HealthProblem problem = EntityManager.GetComponentData<HealthProblem>(citizen);
                    if ((problem.m_Flags & HealthProblemFlags.RequireTransport) == 0)
                    {
                        needsCleanup = true;
                    }
                }

                if (needsCleanup)
                {
                    trackersToClean.Add(tracker);
                }
                else
                {
                    trackedCitizens.Add(citizen);
                    if (tag.m_BuildingEntity != Entity.Null)
                    {
                        if (buildingRefCounts.TryGetValue(tag.m_BuildingEntity, out int count))
                            buildingRefCounts[tag.m_BuildingEntity] = count + 1;
                        else
                            buildingRefCounts[tag.m_BuildingEntity] = 1;
                    }
                }
            }

            // Clean up resolved trackers
            for (int i = 0; i < trackersToClean.Length; i++)
            {
                Entity tracker = trackersToClean[i];
                FireMedicalTracker tag = EntityManager.GetComponentData<FireMedicalTracker>(tracker);
                Entity buildingEntity = tag.m_BuildingEntity;

                if (buildingEntity != Entity.Null
                    && EntityManager.Exists(buildingEntity)
                    && !EntityManager.HasComponent<Deleted>(buildingEntity))
                {
                    bool otherRefsExist = buildingRefCounts.TryGetValue(buildingEntity, out int remaining) && remaining > 0;
                    // Use ECB to defer RescueTarget removal — structural change on rendered building
                    if (!otherRefsExist && EntityManager.HasComponent<RescueTarget>(buildingEntity))
                    {
                        ecb.RemoveComponent<RescueTarget>(buildingEntity);
                    }
                }

                EntityManager.DestroyEntity(tracker);
                cleaned++;
            }

            trackersToClean.Dispose();
            buildingRefCounts.Dispose();
            trackerEntities.Dispose();

            // === DISPATCH (only if toggle is on) ===

            int dispatched = 0;
            int skippedNoTransport = 0;
            int alreadyTagged = 0;

            if (Mod.Settings.DispatchFireToMedicalCalls)
            {
                // Track buildings we've already scheduled AddComponent<RescueTarget> for this frame
                // to avoid duplicate ECB commands for the same building.
                var buildingsScheduled = new NativeHashSet<Entity>(16, Allocator.Temp);

                var citizenEntities = m_CitizenQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < citizenEntities.Length; i++)
                {
                    Entity citizen = citizenEntities[i];
                    HealthProblem problem = EntityManager.GetComponentData<HealthProblem>(citizen);

                    if ((problem.m_Flags & HealthProblemFlags.RequireTransport) == 0)
                    {
                        skippedNoTransport++;
                        continue;
                    }

                    if (trackedCitizens.Contains(citizen))
                    {
                        alreadyTagged++;
                        continue;
                    }

                    CurrentBuilding currentBuilding = EntityManager.GetComponentData<CurrentBuilding>(citizen);
                    Entity buildingEntity = currentBuilding.m_CurrentBuilding;

                    if (buildingEntity == Entity.Null || !EntityManager.Exists(buildingEntity))
                        continue;

                    // Add RescueTarget to the building via ECB (deferred, safe for rendered entities).
                    // Guard against duplicate ECB commands for the same building this frame.
                    if (!EntityManager.HasComponent<RescueTarget>(buildingEntity)
                        && !buildingsScheduled.Contains(buildingEntity))
                    {
                        ecb.AddComponent(buildingEntity, new RescueTarget(Entity.Null));
                        buildingsScheduled.Add(buildingEntity);
                    }

                    // Create fire rescue request targeting the building
                    Entity request = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(request, new ServiceRequest());
                    EntityManager.AddComponentData(request, new FireRescueRequest(
                        buildingEntity, 1f, FireRescueRequestType.Disaster));
                    EntityManager.AddComponentData(request, new RequestGroup(4u));

                    // Create tracker entity (non-rendered) instead of tagging the citizen
                    Entity trackerEntity = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(trackerEntity, new FireMedicalTracker
                    {
                        m_CitizenEntity = citizen,
                        m_BuildingEntity = buildingEntity,
                        m_CreationFrame = m_SimulationSystem.frameIndex
                    });
                    trackedCitizens.Add(citizen);
                    dispatched++;
                }
                citizenEntities.Dispose();
                buildingsScheduled.Dispose();
            }

            trackedCitizens.Dispose();

            if (shouldLog)
            {
                Mod.Log.Info($"[FireMedical] Dispatched={dispatched} SkippedNoTransport={skippedNoTransport} Cleaned={cleaned} AlreadyTagged={alreadyTagged}");
            }
        }
    }
}
