using Game;
using Game.Buildings;
using Game.Common;
using Game.Events;
using Game.Simulation;
using Game.Tools;

using MoreDispatchMod.Components;

using Unity.Collections;
using Unity.Entities;

namespace MoreDispatchMod.Systems
{
    public partial class FireToAccidentDispatchSystem : GameSystemBase
    {
        // All accident sites — candidates for dispatch
        private EntityQuery m_AccidentQuery;

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

            m_AccidentQuery = GetEntityQuery(
                ComponentType.ReadOnly<AccidentSite>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_TrackerQuery = GetEntityQuery(
                ComponentType.ReadOnly<FireAccidentTracker>());
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
            var trackedAccidents = new NativeHashSet<Entity>(trackerEntities.Length, Allocator.Temp);

            for (int i = 0; i < trackerEntities.Length; i++)
            {
                Entity tracker = trackerEntities[i];
                FireAccidentTracker tag = EntityManager.GetComponentData<FireAccidentTracker>(tracker);
                Entity accidentEntity = tag.m_AccidentEntity;

                bool accidentGone = accidentEntity == Entity.Null || !EntityManager.Exists(accidentEntity);
                bool shouldClean = accidentGone;

                if (!accidentGone)
                {
                    if (!EntityManager.HasComponent<AccidentSite>(accidentEntity))
                    {
                        shouldClean = true;
                    }
                    else
                    {
                        AccidentSite site = EntityManager.GetComponentData<AccidentSite>(accidentEntity);
                        bool secured = (site.m_Flags & AccidentSiteFlags.Secured) != 0;
                        bool notTraffic = (site.m_Flags & AccidentSiteFlags.TrafficAccident) == 0;
                        if (secured || notTraffic)
                            shouldClean = true;
                    }
                }

                if (shouldClean)
                {
                    // Use ECB to defer RescueTarget removal — structural change on rendered entity
                    if (!accidentGone && EntityManager.HasComponent<RescueTarget>(accidentEntity))
                    {
                        ecb.RemoveComponent<RescueTarget>(accidentEntity);
                    }
                    EntityManager.DestroyEntity(tracker);
                    cleaned++;
                }
                else
                {
                    trackedAccidents.Add(accidentEntity);
                }
            }
            trackerEntities.Dispose();

            // === DISPATCH (only if toggle is on) ===

            int dispatched = 0;
            int alreadyTagged = 0;

            if (Mod.Settings.DispatchFireToAccidents)
            {
                var accidentEntities = m_AccidentQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < accidentEntities.Length; i++)
                {
                    Entity entity = accidentEntities[i];
                    AccidentSite site = EntityManager.GetComponentData<AccidentSite>(entity);

                    if ((site.m_Flags & AccidentSiteFlags.TrafficAccident) == 0)
                        continue;

                    if ((site.m_Flags & AccidentSiteFlags.Secured) != 0)
                        continue;

                    if (trackedAccidents.Contains(entity))
                    {
                        alreadyTagged++;
                        continue;
                    }

                    // Add RescueTarget so FireRescueDispatchSystem accepts this entity.
                    // Use ECB to defer the structural change to after BatchUploadSystem GPU jobs complete.
                    if (!EntityManager.HasComponent<RescueTarget>(entity))
                    {
                        ecb.AddComponent(entity, new RescueTarget(Entity.Null));
                    }

                    // Create fire rescue request
                    Entity request = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(request, new ServiceRequest());
                    EntityManager.AddComponentData(request, new FireRescueRequest(
                        entity, 1f, FireRescueRequestType.Disaster));
                    EntityManager.AddComponentData(request, new RequestGroup(4u));

                    // Create tracker entity (non-rendered) instead of tagging the accident entity
                    Entity trackerEntity = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(trackerEntity, new FireAccidentTracker
                    {
                        m_AccidentEntity = entity,
                        m_CreationFrame = m_SimulationSystem.frameIndex
                    });
                    trackedAccidents.Add(entity);
                    dispatched++;
                }
                accidentEntities.Dispose();
            }

            trackedAccidents.Dispose();

            if (shouldLog)
            {
                Mod.Log.Info($"[FireAccident] Dispatched={dispatched} Cleaned={cleaned} AlreadyTagged={alreadyTagged}");
            }
        }
    }
}
