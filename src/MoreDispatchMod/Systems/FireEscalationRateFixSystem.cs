using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MoreDispatchMod.Systems
{
    public partial class FireEscalationRateFixSystem : GameSystemBase
    {
        private const float VANILLA_ESCALATION_RATE = 1f / 60f;
        private const float ZERO_THRESHOLD = 0.001f;

        private EntityQuery m_FirePrefabQuery;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 64;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_FirePrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<FireData>(),
                ComponentType.ReadOnly<EventData>());
            RequireForUpdate(m_FirePrefabQuery);
        }

        protected override void OnUpdate()
        {
            var entities = m_FirePrefabQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                FireData fireData = EntityManager.GetComponentData<FireData>(entities[i]);
                if (fireData.m_EscalationRate < ZERO_THRESHOLD)
                {
                    fireData.m_EscalationRate = VANILLA_ESCALATION_RATE;
                    EntityManager.SetComponentData(entities[i], fireData);
                    Mod.Log.Info($"[FireFix] Restored m_EscalationRate to {VANILLA_ESCALATION_RATE:F4} on prefab {entities[i].Index}");
                }
            }
            entities.Dispose();
        }
    }
}
