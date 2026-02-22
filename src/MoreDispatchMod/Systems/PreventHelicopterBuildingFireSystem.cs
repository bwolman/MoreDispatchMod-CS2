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

            int cancelled = 0;

            var requestEntities = m_DispatchedFireRequestQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < requestEntities.Length; i++)
            {
                Entity requestEntity = requestEntities[i];

                Dispatched dispatched = EntityManager.GetComponentData<Dispatched>(requestEntity);
                Entity vehicleEntity = dispatched.m_Handler;

                if (vehicleEntity == Entity.Null || !EntityManager.Exists(vehicleEntity))
                    continue;

                // Check if handler is a fire helicopter (has both FireEngine and Aircraft)
                if (!EntityManager.HasComponent<FireEngine>(vehicleEntity) || !EntityManager.HasComponent<Aircraft>(vehicleEntity))
                    continue;

                // Get the fire target
                FireRescueRequest request = EntityManager.GetComponentData<FireRescueRequest>(requestEntity);
                Entity targetEntity = request.m_Target;

                if (targetEntity == Entity.Null || !EntityManager.Exists(targetEntity))
                    continue;

                // Allow helicopters for non-building fires (forest/wildfire)
                if (!EntityManager.HasComponent<Building>(targetEntity))
                    continue;

                // Cancel helicopter dispatch to building fire
                Entity handleEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(handleEntity, new HandleRequest(
                    requestEntity, Entity.Null, true));

                cancelled++;

                if (shouldLog || cancelled <= 3)
                {
                    Mod.Log.Info($"[HeliBlock] Cancelled helicopter {vehicleEntity.Index} dispatch to building fire {targetEntity.Index}, request={requestEntity.Index}");
                }
            }
            requestEntities.Dispose();

            if (shouldLog && cancelled > 0)
            {
                Mod.Log.Info($"[HeliBlock] Cancelled={cancelled}");
            }
        }
    }
}
