using Game;
using Game.Common;
using Game.Events;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;

using Unity.Collections;
using Unity.Entities;

namespace MoreDispatchMod.Systems
{
    [UpdateAfter(typeof(AccidentSiteSystem))]
    public partial class ForcePoliceDispatchSystem : GameSystemBase
    {
        private EntityQuery m_AccidentQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_AccidentQuery = GetEntityQuery(
                ComponentType.ReadWrite<AccidentSite>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            RequireForUpdate(m_AccidentQuery);
        }

        protected override void OnUpdate()
        {
            if (!Mod.Settings.AlwaysDispatchPoliceToAccidents)
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

                // Force the RequirePolice flag
                if ((site.m_Flags & AccidentSiteFlags.RequirePolice) == 0)
                {
                    site.m_Flags |= AccidentSiteFlags.RequirePolice;
                    EntityManager.SetComponentData(entity, site);
                }

                // Create police request if none exists
                if (site.m_PoliceRequest == Entity.Null)
                {
                    Entity request = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(request, new ServiceRequest());
                    EntityManager.AddComponentData(request, new PoliceEmergencyRequest(
                        entity, entity, 1f, PolicePurpose.Emergency));
                    EntityManager.AddComponentData(request, new RequestGroup(4u));

                    site.m_PoliceRequest = request;
                    EntityManager.SetComponentData(entity, site);
                }
            }

            entities.Dispose();
        }
    }
}
