using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Events;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;

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
        private EntityQuery m_EMSTaggedQuery;
        private EntityQuery m_CrimeTaggedQuery;
        private SimulationSystem m_SimulationSystem;
        private int m_LogCounter;

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

            m_EMSTaggedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualEMSDispatched>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_CrimeTaggedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualCrimeDispatched>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        protected override void OnUpdate()
        {
            // Early-out when no tagged entities exist
            if (m_PoliceTaggedQuery.IsEmptyIgnoreFilter
                && m_FireTaggedQuery.IsEmptyIgnoreFilter
                && m_EMSTaggedQuery.IsEmptyIgnoreFilter
                && m_CrimeTaggedQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            bool shouldLog = false;
            m_LogCounter++;
            if (m_LogCounter >= 64)
            {
                m_LogCounter = 0;
                shouldLog = true;
            }

            uint currentFrame = m_SimulationSystem.frameIndex;
            int policeCleaned = 0;
            int fireCleaned = 0;
            int crimeCleaned = 0;

            // --- Police cleanup ---
            // Car drives to target via normal patrol pathfinding (no AccidentTarget).
            // SelectNextDispatch processes our request, car arrives and returns.
            var policeEntities = m_PoliceTaggedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < policeEntities.Length; i++)
            {
                Entity entity = policeEntities[i];
                if (!EntityManager.Exists(entity))
                    continue;

                ManualPoliceDispatched tag = EntityManager.GetComponentData<ManualPoliceDispatched>(entity);
                Entity carEntity = tag.m_PoliceCarEntity;

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool carGone = carEntity == Entity.Null || !EntityManager.Exists(carEntity)
                               || !EntityManager.HasComponent<PoliceCar>(carEntity);

                // Diagnostic logging every 64 frames
                if (shouldLog && !carGone)
                {
                    PoliceCar pcLog = EntityManager.GetComponentData<PoliceCar>(carEntity);
                    Car carLog = EntityManager.GetComponentData<Car>(carEntity);
                    uint age = currentFrame - tag.m_CreationFrame;
                    Mod.Log.Info($"[ManualCleanup] DIAG police car={carEntity.Index} state=0x{(uint)pcLog.m_State:X} " +
                        $"carFlags=0x{(uint)carLog.m_Flags:X} age={age}");
                }

                // Wait at least 2 seconds before checking car state
                bool gracePeriodPassed = (currentFrame - tag.m_CreationFrame) > 120;
                bool carReturning = false;

                if (!carGone && gracePeriodPassed)
                {
                    PoliceCar pc = EntityManager.GetComponentData<PoliceCar>(carEntity);
                    carReturning = (pc.m_State & PoliceCarFlags.Returning) != 0;
                }

                if (timedOut || carGone || carReturning)
                {
                    string reason = timedOut ? "timeout" : carGone ? "carGone" : "returning";
                    Mod.Log.Info($"[ManualCleanup] Police cleanup: car={carEntity.Index} reason={reason} " +
                        $"age={currentFrame - tag.m_CreationFrame}");

                    // Destroy our request entity
                    Entity requestEntity = tag.m_RequestEntity;
                    if (requestEntity != Entity.Null && EntityManager.Exists(requestEntity))
                    {
                        EntityManager.DestroyEntity(requestEntity);
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
                if (!EntityManager.Exists(entity))
                    continue;

                ManualFireDispatched tag = EntityManager.GetComponentData<ManualFireDispatched>(entity);

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool alreadyResolved = !EntityManager.HasComponent<RescueTarget>(entity);

                if (timedOut || alreadyResolved)
                {
                    if (!alreadyResolved && EntityManager.HasComponent<RescueTarget>(entity))
                    {
                        EntityManager.RemoveComponent<RescueTarget>(entity);
                    }
                    EntityManager.RemoveComponent<ManualFireDispatched>(entity);
                    fireCleaned++;
                }
            }
            fireEntities.Dispose();

            // --- EMS cleanup (citizens) ---
            // HealthProblem is managed by AddHealthProblemSystem — we only track our tag.
            // The game handles the citizen health lifecycle (ambulance pickup, hospital, recovery).
            int emsCleaned = 0;
            var emsEntities = m_EMSTaggedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < emsEntities.Length; i++)
            {
                Entity citizen = emsEntities[i];
                if (!EntityManager.Exists(citizen))
                    continue;

                ManualEMSDispatched tag = EntityManager.GetComponentData<ManualEMSDispatched>(citizen);

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;

                // Resolved when citizen no longer needs transport (ambulance picked them up or recovered)
                bool resolved = !EntityManager.HasComponent<HealthProblem>(citizen);
                if (!resolved)
                {
                    HealthProblem hp = EntityManager.GetComponentData<HealthProblem>(citizen);
                    resolved = (hp.m_Flags & HealthProblemFlags.RequireTransport) == 0;
                }

                if (timedOut || resolved)
                {
                    EntityManager.RemoveComponent<ManualEMSDispatched>(citizen);
                    emsCleaned++;
                }
            }
            emsEntities.Dispose();

            // --- Crime cleanup ---
            // AccidentSiteSystem manages the AccidentSite lifecycle (crime duration, police dispatch,
            // secured state, removal). We clean up our tag and event entity when AccidentSite is gone.
            var crimeEntities = m_CrimeTaggedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < crimeEntities.Length; i++)
            {
                Entity entity = crimeEntities[i];
                if (!EntityManager.Exists(entity))
                    continue;

                ManualCrimeDispatched tag = EntityManager.GetComponentData<ManualCrimeDispatched>(entity);

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                // Vanilla AccidentSiteSystem removes AccidentSite after crime resolves
                bool resolved = !EntityManager.HasComponent<AccidentSite>(entity);

                if (shouldLog && !resolved)
                {
                    AccidentSite accSite = EntityManager.GetComponentData<AccidentSite>(entity);
                    uint age = currentFrame - tag.m_CreationFrame;
                    Mod.Log.Info($"[ManualCleanup] DIAG crime entity={entity.Index} flags=0x{(uint)accSite.m_Flags:X} age={age}");
                }

                if (timedOut || resolved)
                {
                    string reason = timedOut ? "timeout" : "resolved";
                    Mod.Log.Info($"[ManualCleanup] Crime cleanup: entity={entity.Index} reason={reason} " +
                        $"age={currentFrame - tag.m_CreationFrame}");

                    // If timed out, remove AccidentSite ourselves
                    if (!resolved && EntityManager.HasComponent<AccidentSite>(entity))
                    {
                        EntityManager.RemoveComponent<AccidentSite>(entity);
                    }

                    // Destroy our event entity
                    Entity eventEntity = tag.m_EventEntity;
                    if (eventEntity != Entity.Null && EntityManager.Exists(eventEntity))
                    {
                        EntityManager.DestroyEntity(eventEntity);
                    }

                    EntityManager.RemoveComponent<ManualCrimeDispatched>(entity);
                    crimeCleaned++;
                }
            }
            crimeEntities.Dispose();

            if (shouldLog)
            {
                Mod.Log.Info($"[ManualCleanup] PoliceCleaned={policeCleaned} FireCleaned={fireCleaned} " +
                    $"EMSCleaned={emsCleaned} CrimeCleaned={crimeCleaned}");
            }
        }
    }
}
