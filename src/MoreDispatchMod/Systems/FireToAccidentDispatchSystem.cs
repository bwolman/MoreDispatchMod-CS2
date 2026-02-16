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
        private EntityQuery m_AccidentQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_AccidentQuery = GetEntityQuery(
                ComponentType.ReadWrite<AccidentSite>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<FireDispatchedToAccident>());

            RequireForUpdate(m_AccidentQuery);
        }

        protected override void OnUpdate()
        {
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
                Entity request = EntityManager.CreateEntity();
                EntityManager.AddComponentData(request, new ServiceRequest());
                EntityManager.AddComponentData(request, new FireRescueRequest(
                    entity, 1f, FireRescueRequestType.Disaster));
                EntityManager.AddComponentData(request, new RequestGroup(4u));

                // Mark entity so we don't create duplicate requests
                EntityManager.AddComponent<FireDispatchedToAccident>(entity);
            }

            entities.Dispose();
        }
    }
}
