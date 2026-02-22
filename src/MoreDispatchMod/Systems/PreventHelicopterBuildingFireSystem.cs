using Game;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;

using Unity.Collections;
using Unity.Entities;

namespace MoreDispatchMod.Systems
{
    public partial class PreventHelicopterBuildingFireSystem : GameSystemBase
    {
        private EntityQuery m_DispatchedFireRequestQuery;
        private int m_LogCounter;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_DispatchedFireRequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<FireRescueRequest>(),
                ComponentType.ReadOnly<Dispatched>(),
                ComponentType.ReadOnly<ServiceRequest>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            Mod.Log.Info("[PreventHelicopterBuildingFire] OnCreate complete");
        }

        protected override void OnUpdate()
        {
            if (!Mod.Settings.PreventHelicopterBuildingFires)
                return;

            if (m_DispatchedFireRequestQuery.IsEmptyIgnoreFilter)
                return;

            bool shouldLog = false;
            m_LogCounter++;
            if (m_LogCounter >= 64)
            {
                m_LogCounter = 0;
                shouldLog = true;
            }

            var allRequests = m_DispatchedFireRequestQuery.ToEntityArray(Allocator.Temp);

            // Pass 1: collect fire targets that have a ground engine (FireEngine, not Aircraft)
            // already dispatched. A ground engine dispatch means the vanilla pathfinder found
            // a road-accessible route — the fire can be fought from the road network.
            var engineTargets = new NativeHashSet<Entity>(allRequests.Length, Allocator.Temp);
            for (int i = 0; i < allRequests.Length; i++)
            {
                Dispatched dispatched = EntityManager.GetComponentData<Dispatched>(allRequests[i]);
                Entity vehicle = dispatched.m_Handler;

                if (vehicle == Entity.Null || !EntityManager.Exists(vehicle))
                    continue;
                if (!EntityManager.HasComponent<FireEngine>(vehicle))
                    continue;
                if (EntityManager.HasComponent<Aircraft>(vehicle))
                    continue; // this is a helicopter — skip

                FireRescueRequest req = EntityManager.GetComponentData<FireRescueRequest>(allRequests[i]);
                if (req.m_Target != Entity.Null && EntityManager.Exists(req.m_Target))
                    engineTargets.Add(req.m_Target);
            }

            // Pass 2: cancel helicopter dispatches that are unnecessary.
            // Cancel if:
            //   (a) target is a building — ground engines handle building fires
            //   (b) target is a forest fire AND a ground engine is already dispatched to it
            //       — the fire is road-accessible, no helicopter needed
            // Allow if:
            //   target is a forest fire AND no ground engine is dispatched to it
            //       — remote/inaccessible fire, helicopter is the right tool
            int cancelled = 0;
            for (int i = 0; i < allRequests.Length; i++)
            {
                Entity requestEntity = allRequests[i];

                Dispatched dispatched = EntityManager.GetComponentData<Dispatched>(requestEntity);
                Entity vehicleEntity = dispatched.m_Handler;

                if (vehicleEntity == Entity.Null || !EntityManager.Exists(vehicleEntity))
                    continue;
                if (!EntityManager.HasComponent<FireEngine>(vehicleEntity) || !EntityManager.HasComponent<Aircraft>(vehicleEntity))
                    continue; // not a helicopter

                FireRescueRequest request = EntityManager.GetComponentData<FireRescueRequest>(requestEntity);
                Entity targetEntity = request.m_Target;

                if (targetEntity == Entity.Null || !EntityManager.Exists(targetEntity))
                    continue;

                bool isBuilding = EntityManager.HasComponent<Building>(targetEntity);
                bool engineAlsoDispatched = engineTargets.Contains(targetEntity);

                if (!isBuilding && !engineAlsoDispatched)
                    continue; // remote forest fire — let helicopter respond

                // Cancel: building fire or road-accessible forest fire
                Entity handleEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(handleEntity, new HandleRequest(
                    requestEntity, Entity.Null, true));

                cancelled++;

                if (shouldLog || cancelled <= 3)
                {
                    string reason = isBuilding ? "building" : "engine-accessible forest fire";
                    Mod.Log.Info($"[HeliBlock] Cancelled helicopter {vehicleEntity.Index} → {targetEntity.Index} ({reason}), request={requestEntity.Index}");
                }
            }

            allRequests.Dispose();
            engineTargets.Dispose();

            if (shouldLog && cancelled > 0)
            {
                Mod.Log.Info($"[HeliBlock] Cancelled={cancelled}");
            }
        }
    }
}
