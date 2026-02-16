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
        // Citizens with HealthProblem not yet tagged — candidates for new dispatch
        private EntityQuery m_UntaggedQuery;

        // Citizens we've already tagged — candidates for cleanup
        private EntityQuery m_TaggedQuery;

        private int m_LogCounter;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_UntaggedQuery = GetEntityQuery(
                ComponentType.ReadOnly<HealthProblem>(),
                ComponentType.ReadOnly<CurrentBuilding>(),
                ComponentType.Exclude<FireDispatchedToMedical>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_TaggedQuery = GetEntityQuery(
                ComponentType.ReadOnly<FireDispatchedToMedical>(),
                ComponentType.Exclude<Deleted>());
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

            int cleaned = 0;

            // === CLEANUP (always runs, even if toggle is off) ===

            var taggedEntities = m_TaggedQuery.ToEntityArray(Allocator.Temp);

            // First pass: count how many active tags reference each building
            // so we only remove RescueTarget when the last citizen referencing it is cleaned up
            var buildingRefCounts = new NativeHashMap<Entity, int>(taggedEntities.Length, Allocator.Temp);
            var citizensToClean = new NativeList<Entity>(taggedEntities.Length, Allocator.Temp);

            for (int i = 0; i < taggedEntities.Length; i++)
            {
                Entity citizen = taggedEntities[i];
                FireDispatchedToMedical tag = EntityManager.GetComponentData<FireDispatchedToMedical>(citizen);

                bool needsCleanup = false;

                // Citizen no longer has HealthProblem at all
                if (!EntityManager.HasComponent<HealthProblem>(citizen))
                {
                    needsCleanup = true;
                }
                else
                {
                    HealthProblem problem = EntityManager.GetComponentData<HealthProblem>(citizen);
                    // Citizen no longer requires transport (recovered or died)
                    if ((problem.m_Flags & HealthProblemFlags.RequireTransport) == 0)
                    {
                        needsCleanup = true;
                    }
                }

                if (needsCleanup)
                {
                    citizensToClean.Add(citizen);
                }
                else if (tag.m_Building != Entity.Null)
                {
                    // Still active — count this reference
                    if (buildingRefCounts.TryGetValue(tag.m_Building, out int count))
                    {
                        buildingRefCounts[tag.m_Building] = count + 1;
                    }
                    else
                    {
                        buildingRefCounts[tag.m_Building] = 1;
                    }
                }
            }

            // Second pass: clean up citizens and conditionally remove RescueTarget from buildings
            for (int i = 0; i < citizensToClean.Length; i++)
            {
                Entity citizen = citizensToClean[i];
                FireDispatchedToMedical tag = EntityManager.GetComponentData<FireDispatchedToMedical>(citizen);

                if (tag.m_Building != Entity.Null
                    && EntityManager.Exists(tag.m_Building)
                    && !EntityManager.HasComponent<Deleted>(tag.m_Building))
                {
                    // Only remove RescueTarget if no other active tag references this building
                    bool otherRefsExist = buildingRefCounts.TryGetValue(tag.m_Building, out int remaining) && remaining > 0;

                    if (!otherRefsExist && EntityManager.HasComponent<RescueTarget>(tag.m_Building))
                    {
                        EntityManager.RemoveComponent<RescueTarget>(tag.m_Building);
                    }
                }

                EntityManager.RemoveComponent<FireDispatchedToMedical>(citizen);
                cleaned++;
            }

            citizensToClean.Dispose();
            buildingRefCounts.Dispose();
            taggedEntities.Dispose();

            // === DISPATCH (only if toggle is on) ===

            int dispatched = 0;
            int skippedNoTransport = 0;
            int alreadyTagged = 0;

            if (Mod.Settings.DispatchFireToMedicalCalls)
            {
                var untaggedEntities = m_UntaggedQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < untaggedEntities.Length; i++)
                {
                    Entity citizen = untaggedEntities[i];
                    HealthProblem problem = EntityManager.GetComponentData<HealthProblem>(citizen);

                    // Only dispatch to calls that require transport (ambulance)
                    if ((problem.m_Flags & HealthProblemFlags.RequireTransport) == 0)
                    {
                        skippedNoTransport++;
                        continue;
                    }

                    // Get the building the citizen is in
                    CurrentBuilding currentBuilding = EntityManager.GetComponentData<CurrentBuilding>(citizen);
                    Entity buildingEntity = currentBuilding.m_CurrentBuilding;

                    if (buildingEntity == Entity.Null || !EntityManager.Exists(buildingEntity))
                        continue;

                    // Add RescueTarget to the BUILDING (not the citizen)
                    if (!EntityManager.HasComponent<RescueTarget>(buildingEntity))
                    {
                        EntityManager.AddComponentData(buildingEntity, new RescueTarget(Entity.Null));
                    }

                    // Create fire rescue request targeting the building
                    Entity request = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(request, new ServiceRequest());
                    EntityManager.AddComponentData(request, new FireRescueRequest(
                        buildingEntity, 1f, FireRescueRequestType.Disaster));
                    EntityManager.AddComponentData(request, new RequestGroup(4u));

                    // Tag citizen with building reference for cleanup
                    EntityManager.AddComponentData(citizen, new FireDispatchedToMedical
                    {
                        m_Building = buildingEntity
                    });
                    dispatched++;
                }
                untaggedEntities.Dispose();

                alreadyTagged = m_TaggedQuery.CalculateEntityCount();
            }

            if (shouldLog)
            {
                Mod.Log.Info($"[FireMedical] Dispatched={dispatched} SkippedNoTransport={skippedNoTransport} Cleaned={cleaned} AlreadyTagged={alreadyTagged}");
            }
        }
    }
}
