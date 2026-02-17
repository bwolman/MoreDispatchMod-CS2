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
    public partial class ManualDispatchCleanupSystem : GameSystemBase
    {
        private const uint TIMEOUT_FRAMES = 3600; // ~60 seconds at 60 fps

        private EntityQuery m_PoliceTaggedQuery;
        private EntityQuery m_FireTaggedQuery;
        private SimulationSystem m_SimulationSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_PoliceTaggedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualPoliceDispatched>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_FireTaggedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualFireDispatched>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        protected override void OnUpdate()
        {
            uint currentFrame = m_SimulationSystem.frameIndex;
            int policeCleaned = 0;
            int fireCleaned = 0;

            // --- Police cleanup ---
            var policeEntities = m_PoliceTaggedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < policeEntities.Length; i++)
            {
                Entity entity = policeEntities[i];
                ManualPoliceDispatched tag = EntityManager.GetComponentData<ManualPoliceDispatched>(entity);

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool secured = false;

                if (EntityManager.HasComponent<AccidentSite>(entity))
                {
                    AccidentSite site = EntityManager.GetComponentData<AccidentSite>(entity);
                    secured = (site.m_Flags & AccidentSiteFlags.Secured) != 0;
                }

                if (timedOut || secured)
                {
                    // Remove AccidentSite only if we added it
                    if (tag.m_AddedAccidentSite && EntityManager.HasComponent<AccidentSite>(entity))
                    {
                        EntityManager.RemoveComponent<AccidentSite>(entity);
                    }

                    EntityManager.RemoveComponent<ManualPoliceDispatched>(entity);
                    policeCleaned++;
                }
            }
            policeEntities.Dispose();

            // --- Fire cleanup ---
            var fireEntities = m_FireTaggedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < fireEntities.Length; i++)
            {
                Entity entity = fireEntities[i];
                ManualFireDispatched tag = EntityManager.GetComponentData<ManualFireDispatched>(entity);

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;

                if (timedOut)
                {
                    if (EntityManager.HasComponent<RescueTarget>(entity))
                    {
                        EntityManager.RemoveComponent<RescueTarget>(entity);
                    }

                    EntityManager.RemoveComponent<ManualFireDispatched>(entity);
                    fireCleaned++;
                }
            }
            fireEntities.Dispose();
        }
    }
}
