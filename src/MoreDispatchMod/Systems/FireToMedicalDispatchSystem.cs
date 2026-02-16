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
    [UpdateAfter(typeof(HealthProblemSystem))]
    public partial class FireToMedicalDispatchSystem : GameSystemBase
    {
        private const int kUpdateInterval = 64;

        private EntityQuery m_HealthProblemQuery;
        private EntityArchetype m_FireRescueRequestArchetype;
        private uint m_SimulationFrame;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_HealthProblemQuery = GetEntityQuery(
                ComponentType.ReadOnly<HealthProblem>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<FireDispatchedToMedical>());

            m_FireRescueRequestArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<ServiceRequest>(),
                ComponentType.ReadWrite<FireRescueRequest>(),
                ComponentType.ReadWrite<RequestGroup>());

            RequireForUpdate(m_HealthProblemQuery);
        }

        protected override void OnUpdate()
        {
            m_SimulationFrame = SimulationUtils.GetUpdateFrame(m_SimulationFrame, kUpdateInterval, 1);
            uint frameIndex = EntityManager.GetComponentData<SimulationFrame>(SystemHandle).m_Frame;
            if (frameIndex % kUpdateInterval != m_SimulationFrame)
            {
                return;
            }

            if (!Mod.Settings.DispatchFireToMedicalCalls)
            {
                return;
            }

            var entities = m_HealthProblemQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                HealthProblem problem = EntityManager.GetComponentData<HealthProblem>(entity);

                // Only dispatch to calls that require transport (ambulance)
                if ((problem.m_Flags & HealthProblemFlags.RequireTransport) == 0)
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
                EntityManager.AddComponent<FireDispatchedToMedical>(entity);
            }

            entities.Dispose();
        }
    }
}
