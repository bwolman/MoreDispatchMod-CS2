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
        // Untagged accident sites — candidates for new dispatch
        private EntityQuery m_UntaggedQuery;

        // Tagged accident sites — candidates for cleanup
        private EntityQuery m_TaggedQuery;

        // Orphaned tags — entity lost AccidentSite but still has our tag
        private EntityQuery m_OrphanedQuery;

        private int m_LogCounter;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_UntaggedQuery = GetEntityQuery(
                ComponentType.ReadWrite<AccidentSite>(),
                ComponentType.Exclude<FireDispatchedToAccident>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_TaggedQuery = GetEntityQuery(
                ComponentType.ReadOnly<AccidentSite>(),
                ComponentType.ReadOnly<FireDispatchedToAccident>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_OrphanedQuery = GetEntityQuery(
                ComponentType.ReadOnly<FireDispatchedToAccident>(),
                ComponentType.Exclude<AccidentSite>(),
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
            int orphansCleaned = 0;

            // === CLEANUP (always runs, even if toggle is off) ===

            // Clean up tagged accident sites that are secured or no longer traffic accidents
            var taggedEntities = m_TaggedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < taggedEntities.Length; i++)
            {
                Entity entity = taggedEntities[i];
                AccidentSite site = EntityManager.GetComponentData<AccidentSite>(entity);

                bool secured = (site.m_Flags & AccidentSiteFlags.Secured) != 0;
                bool notTraffic = (site.m_Flags & AccidentSiteFlags.TrafficAccident) == 0;

                if (secured || notTraffic)
                {
                    if (EntityManager.HasComponent<RescueTarget>(entity))
                    {
                        EntityManager.RemoveComponent<RescueTarget>(entity);
                    }
                    EntityManager.RemoveComponent<FireDispatchedToAccident>(entity);
                    cleaned++;
                }
            }
            taggedEntities.Dispose();

            // Clean up orphaned tags (entity lost AccidentSite component)
            var orphanedEntities = m_OrphanedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < orphanedEntities.Length; i++)
            {
                Entity entity = orphanedEntities[i];
                if (EntityManager.HasComponent<RescueTarget>(entity))
                {
                    EntityManager.RemoveComponent<RescueTarget>(entity);
                }
                EntityManager.RemoveComponent<FireDispatchedToAccident>(entity);
                orphansCleaned++;
            }
            orphanedEntities.Dispose();

            // === DISPATCH (only if toggle is on) ===

            int dispatched = 0;
            int alreadyTagged = 0;

            if (Mod.Settings.DispatchFireToAccidents)
            {
                var untaggedEntities = m_UntaggedQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < untaggedEntities.Length; i++)
                {
                    Entity entity = untaggedEntities[i];
                    AccidentSite site = EntityManager.GetComponentData<AccidentSite>(entity);

                    if ((site.m_Flags & AccidentSiteFlags.TrafficAccident) == 0)
                        continue;

                    if ((site.m_Flags & AccidentSiteFlags.Secured) != 0)
                        continue;

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

                    // Tag to prevent duplicate dispatch
                    EntityManager.AddComponent<FireDispatchedToAccident>(entity);
                    dispatched++;
                }
                untaggedEntities.Dispose();

                alreadyTagged = m_TaggedQuery.CalculateEntityCount();
            }

            if (shouldLog)
            {
                Mod.Log.Info($"[FireAccident] Dispatched={dispatched} Cleaned={cleaned} OrphansCleaned={orphansCleaned} AlreadyTagged={alreadyTagged}");
            }
        }
    }
}
