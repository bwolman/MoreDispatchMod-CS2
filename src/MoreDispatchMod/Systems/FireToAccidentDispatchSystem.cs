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
    [UpdateAfter(typeof(AccidentSiteSystem))]
    public partial class FireToAccidentDispatchSystem : GameSystemBase
    {
        private const int kUpdateInterval = 64;

        private EntityQuery m_AccidentQuery;
        private EntityArchetype m_FireRescueRequestArchetype;
        private uint m_SimulationFrame;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_AccidentQuery = GetEntityQuery(
                ComponentType.ReadWrite<AccidentSite>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<FireDispatchedToAccident>());

            m_FireRescueRequestArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<ServiceRequest>(),
                ComponentType.ReadWrite<FireRescueRequest>(),
                ComponentType.ReadWrite<RequestGroup>());

            RequireForUpdate(m_AccidentQuery);
        }

        protected override void OnUpdate()
        {
            m_SimulationFrame = SimulationUtils.GetUpdateFrame(m_SimulationFrame, kUpdateInterval, 1);
            uint frameIndex = EntityManager.GetComponentData<SimulationFrame>(SystemHandle).m_Frame;
            if (frameIndex % kUpdateInterval != m_SimulationFrame)
            {
                return;
            }

            if (!Mod.Settings.DispatchFireToAccidents)
            {
                return;
            }

            var entities = m_AccidentQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                AccidentSite site = EntityManager.GetComponentData<AccidentSite>(entity);

                if ((site.m_Flags & AccidentSiteFlags.TrafficAccident) == 0)
                {
                    continue;
                }

                if ((site.m_Flags & AccidentSiteFlags.Secured) != 0)
                {
                    continue;
                }

                // Add RescueTarget so FireRescueDispatchSystem accepts this entity
                if (!EntityManager.HasComponent<RescueTarget>(entity))
                {
                    EntityManager.AddComponentData(entity, new RescueTarget(Entity.Null));
                }

                // Create fire rescue request
                Entity request = EntityManager.CreateEntity(m_FireRescueRequestArchetype);
                EntityManager.SetComponentData(request, new FireRescueRequest(
                    entity, 1f, FireRescueRequestType.Disaster));
                EntityManager.SetComponentData(request, new RequestGroup(4u));

                // Mark entity so we don't create duplicate requests
                EntityManager.AddComponent<FireDispatchedToAccident>(entity);
            }

            entities.Dispose();
        }
    }
}
