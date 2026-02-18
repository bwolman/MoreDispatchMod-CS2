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

        private EntityQuery m_PoliceTrackerQuery;
        private EntityQuery m_FireTrackerQuery;
        private EntityQuery m_EMSTrackerQuery;
        private EntityQuery m_CrimeTrackerQuery;
        private SimulationSystem m_SimulationSystem;
        private int m_LogCounter;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            // Tracker entity queries — these match our non-rendered tracker entities
            m_PoliceTrackerQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualPoliceDispatched>());

            m_FireTrackerQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualFireDispatched>());

            m_EMSTrackerQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualEMSDispatched>());

            m_CrimeTrackerQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualCrimeDispatched>());
        }

        protected override void OnUpdate()
        {
            // Early-out when no tracker entities exist
            if (m_PoliceTrackerQuery.IsEmptyIgnoreFilter
                && m_FireTrackerQuery.IsEmptyIgnoreFilter
                && m_EMSTrackerQuery.IsEmptyIgnoreFilter
                && m_CrimeTrackerQuery.IsEmptyIgnoreFilter)
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
            int emsCleaned = 0;
            int crimeCleaned = 0;

            // --- Police cleanup ---
            // Tracker entities reference: target building, police car, request entity.
            // Clean up when car is gone, returning, or timeout.
            var policeTrackers = m_PoliceTrackerQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < policeTrackers.Length; i++)
            {
                Entity tracker = policeTrackers[i];
                ManualPoliceDispatched tag = EntityManager.GetComponentData<ManualPoliceDispatched>(tracker);
                Entity carEntity = tag.m_PoliceCarEntity;

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool carGone = carEntity == Entity.Null || !EntityManager.Exists(carEntity)
                               || !EntityManager.HasComponent<PoliceCar>(carEntity);

                if (shouldLog && !carGone)
                {
                    PoliceCar pcLog = EntityManager.GetComponentData<PoliceCar>(carEntity);
                    Car carLog = EntityManager.GetComponentData<Car>(carEntity);
                    uint age = currentFrame - tag.m_CreationFrame;
                    Mod.Log.Info($"[ManualCleanup] DIAG police tracker={tracker.Index} car={carEntity.Index} " +
                        $"target={tag.m_TargetEntity.Index} state=0x{(uint)pcLog.m_State:X} " +
                        $"carFlags=0x{(uint)carLog.m_Flags:X} age={age}");
                }

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
                    Mod.Log.Info($"[ManualCleanup] Police cleanup: tracker={tracker.Index} car={carEntity.Index} " +
                        $"reason={reason} age={currentFrame - tag.m_CreationFrame}");

                    // Destroy our request entity
                    Entity requestEntity = tag.m_RequestEntity;
                    if (requestEntity != Entity.Null && EntityManager.Exists(requestEntity))
                    {
                        EntityManager.DestroyEntity(requestEntity);
                    }

                    // Destroy the tracker entity itself
                    EntityManager.DestroyEntity(tracker);
                    policeCleaned++;
                }
            }
            policeTrackers.Dispose();

            // --- Fire cleanup ---
            // Tracker entities reference: target building.
            // Clean up when RescueTarget is gone (vanilla resolved) or timeout.
            var fireTrackers = m_FireTrackerQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < fireTrackers.Length; i++)
            {
                Entity tracker = fireTrackers[i];
                ManualFireDispatched tag = EntityManager.GetComponentData<ManualFireDispatched>(tracker);
                Entity targetEntity = tag.m_TargetEntity;

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool targetGone = targetEntity == Entity.Null || !EntityManager.Exists(targetEntity);
                bool alreadyResolved = !targetGone && !EntityManager.HasComponent<RescueTarget>(targetEntity);

                if (timedOut || targetGone || alreadyResolved)
                {
                    // If timed out, remove RescueTarget from the building
                    if (!targetGone && !alreadyResolved && EntityManager.HasComponent<RescueTarget>(targetEntity))
                    {
                        EntityManager.RemoveComponent<RescueTarget>(targetEntity);
                    }

                    EntityManager.DestroyEntity(tracker);
                    fireCleaned++;
                }
            }
            fireTrackers.Dispose();

            // --- EMS cleanup ---
            // Tracker entities reference: citizen entity.
            // Clean up when citizen no longer needs transport or timeout.
            var emsTrackers = m_EMSTrackerQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < emsTrackers.Length; i++)
            {
                Entity tracker = emsTrackers[i];
                ManualEMSDispatched tag = EntityManager.GetComponentData<ManualEMSDispatched>(tracker);
                Entity citizen = tag.m_CitizenEntity;

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool citizenGone = citizen == Entity.Null || !EntityManager.Exists(citizen);

                bool resolved = citizenGone;
                if (!citizenGone)
                {
                    if (!EntityManager.HasComponent<HealthProblem>(citizen))
                    {
                        resolved = true;
                    }
                    else
                    {
                        HealthProblem hp = EntityManager.GetComponentData<HealthProblem>(citizen);
                        resolved = (hp.m_Flags & HealthProblemFlags.RequireTransport) == 0;
                    }
                }

                if (timedOut || resolved)
                {
                    EntityManager.DestroyEntity(tracker);
                    emsCleaned++;
                }
            }
            emsTrackers.Dispose();

            // --- Crime cleanup ---
            // Tracker entities reference: target building, event entity.
            // AccidentSiteSystem manages AccidentSite lifecycle. Clean up when AccidentSite is gone or timeout.
            var crimeTrackers = m_CrimeTrackerQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < crimeTrackers.Length; i++)
            {
                Entity tracker = crimeTrackers[i];
                ManualCrimeDispatched tag = EntityManager.GetComponentData<ManualCrimeDispatched>(tracker);
                Entity targetEntity = tag.m_TargetEntity;

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool targetGone = targetEntity == Entity.Null || !EntityManager.Exists(targetEntity);
                bool resolved = !targetGone && !EntityManager.HasComponent<AccidentSite>(targetEntity);

                if (shouldLog && !targetGone && !resolved)
                {
                    AccidentSite accSite = EntityManager.GetComponentData<AccidentSite>(targetEntity);
                    uint age = currentFrame - tag.m_CreationFrame;
                    Mod.Log.Info($"[ManualCleanup] DIAG crime tracker={tracker.Index} target={targetEntity.Index} " +
                        $"flags=0x{(uint)accSite.m_Flags:X} age={age}");
                }

                if (timedOut || targetGone || resolved)
                {
                    string reason = timedOut ? "timeout" : targetGone ? "targetGone" : "resolved";
                    Mod.Log.Info($"[ManualCleanup] Crime cleanup: tracker={tracker.Index} target={targetEntity.Index} " +
                        $"reason={reason} age={currentFrame - tag.m_CreationFrame}");

                    // If timed out, remove AccidentSite ourselves
                    if (!targetGone && !resolved && EntityManager.HasComponent<AccidentSite>(targetEntity))
                    {
                        EntityManager.RemoveComponent<AccidentSite>(targetEntity);
                    }

                    // Destroy our event entity
                    Entity eventEntity = tag.m_EventEntity;
                    if (eventEntity != Entity.Null && EntityManager.Exists(eventEntity))
                    {
                        EntityManager.DestroyEntity(eventEntity);
                    }

                    EntityManager.DestroyEntity(tracker);
                    crimeCleaned++;
                }
            }
            crimeTrackers.Dispose();

            if (shouldLog)
            {
                Mod.Log.Info($"[ManualCleanup] PoliceCleaned={policeCleaned} FireCleaned={fireCleaned} " +
                    $"EMSCleaned={emsCleaned} CrimeCleaned={crimeCleaned}");
            }
        }
    }
}
