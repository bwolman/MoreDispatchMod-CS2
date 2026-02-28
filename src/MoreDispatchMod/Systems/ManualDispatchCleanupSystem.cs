using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Events;
using Game.Simulation;
using Game.Tools;

using MoreDispatchMod.Components;

using Unity.Collections;
using Unity.Entities;

namespace MoreDispatchMod.Systems
{
    public partial class ManualDispatchCleanupSystem : GameSystemBase
    {
        private const uint TIMEOUT_FRAMES = 1800; // ~30 seconds at 60 fps
        private const uint ACCIDENT_TIMEOUT_FRAMES = 18000; // ~5 min — exceeds 14400-frame AccidentSite cleanup window

        private EntityQuery m_PoliceTrackerQuery;
        private EntityQuery m_FireTrackerQuery;
        private EntityQuery m_EMSTrackerQuery;
        private EntityQuery m_CrimeTrackerQuery;
        private EntityQuery m_AccidentTrackerQuery;
        private EndFrameBarrier m_EndFrameBarrier;
        private SimulationSystem m_SimulationSystem;
        private int m_LogCounter;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
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

            m_AccidentTrackerQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualAccidentDispatched>());
        }

        protected override void OnUpdate()
        {
            // Early-out when no tracker entities exist
            if (m_PoliceTrackerQuery.IsEmptyIgnoreFilter
                && m_FireTrackerQuery.IsEmptyIgnoreFilter
                && m_EMSTrackerQuery.IsEmptyIgnoreFilter
                && m_CrimeTrackerQuery.IsEmptyIgnoreFilter
                && m_AccidentTrackerQuery.IsEmptyIgnoreFilter)
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
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            int policeCleaned = 0;
            int fireApplied = 0;
            int fireCleaned = 0;
            int emsCleaned = 0;
            int crimeCleaned = 0;
            int accidentCleaned = 0;

            // =====================================================================
            // APPLY PHASE — add deferred structural changes to rendered entities.
            // Direct EntityManager structural changes (Add/Remove component) crash
            // BatchUploadSystem because GPU upload jobs hold chunk references that
            // become invalid when an entity moves archetypes. We use EndFrameBarrier
            // ECB to defer these to after all frame jobs complete.
            // =====================================================================

            // Police: no apply phase needed — AccidentSite is added via AddAccidentSite command
            // entity in ManualDispatchToolSystem; vanilla AddAccidentSiteSystem handles it safely.
            var policeTrackers = m_PoliceTrackerQuery.ToEntityArray(Allocator.Temp);
            // Don't dispose — reused in cleanup below

            // --- Fire: add RescueTarget to building ---
            var fireTrackers = m_FireTrackerQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < fireTrackers.Length; i++)
            {
                Entity tracker = fireTrackers[i];
                ManualFireDispatched tag = EntityManager.GetComponentData<ManualFireDispatched>(tracker);
                Entity targetEntity = tag.m_TargetEntity;

                if (targetEntity != Entity.Null && EntityManager.Exists(targetEntity)
                    && !EntityManager.HasComponent<RescueTarget>(targetEntity))
                {
                    // Use ECB to defer RescueTarget add — structural change on rendered building
                    ecb.AddComponent(targetEntity, new RescueTarget(Entity.Null));
                    fireApplied++;
                }
            }
            // Don't dispose — reused in cleanup below

            // Crime: AccidentSite is now added via AddAccidentSite command entity
            // in ManualDispatchToolSystem — vanilla AddAccidentSiteSystem handles it safely.
            var crimeTrackers = m_CrimeTrackerQuery.ToEntityArray(Allocator.Temp);
            // Don't dispose — reused in cleanup below

            // =====================================================================
            // CLEANUP PHASE — remove tracker entities when dispatch is resolved.
            // =====================================================================

            // --- Police cleanup (mirrors Crime cleanup — AccidentSite lifecycle) ---
            for (int i = 0; i < policeTrackers.Length; i++)
            {
                Entity tracker = policeTrackers[i];
                ManualPoliceDispatched tag = EntityManager.GetComponentData<ManualPoliceDispatched>(tracker);
                Entity targetEntity = tag.m_TargetEntity;

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool targetGone = targetEntity == Entity.Null || !EntityManager.Exists(targetEntity);
                // Grace period: AccidentSite added via ECB (plays back at end of frame) — avoid
                // false "resolved" detection for first 2 frames.
                bool resolved = !targetGone
                    && !EntityManager.HasComponent<AccidentSite>(targetEntity)
                    && (currentFrame - tag.m_CreationFrame) > 2;

                if (shouldLog && !targetGone && !resolved)
                {
                    AccidentSite site = EntityManager.GetComponentData<AccidentSite>(targetEntity);
                    uint age = currentFrame - tag.m_CreationFrame;
                    Mod.Log.Info($"[ManualCleanup] DIAG police tracker={tracker.Index} target={targetEntity.Index} " +
                        $"flags=0x{(uint)site.m_Flags:X} age={age}");
                }

                if (timedOut || targetGone || resolved)
                {
                    string reason = timedOut ? "timeout" : targetGone ? "targetGone" : "resolved";
                    Mod.Log.Info($"[ManualCleanup] Police cleanup: tracker={tracker.Index} target={targetEntity.Index} " +
                        $"reason={reason} age={currentFrame - tag.m_CreationFrame}");

                    // CRITICAL: Never destroy the event entity while AccidentSite may still be on
                    // the building referencing it via m_Event. AccidentSiteSystem reads m_Event in a
                    // burst job every 64 frames — if the entity is destroyed, it crashes (BatchUploadSystem).
                    // Only destroy the event entity if the building itself is gone (orphaned entity).
                    // In all other cases, let AccidentSiteSystem own the event entity lifecycle; it will
                    // destroy the event entity when it removes AccidentSite naturally.
                    Entity eventEntity = tag.m_EventEntity;
                    if (targetGone && eventEntity != Entity.Null && EntityManager.Exists(eventEntity))
                        EntityManager.DestroyEntity(eventEntity);

                    EntityManager.DestroyEntity(tracker);
                    policeCleaned++;
                }
            }
            policeTrackers.Dispose();

            // --- Fire cleanup ---
            for (int i = 0; i < fireTrackers.Length; i++)
            {
                Entity tracker = fireTrackers[i];
                ManualFireDispatched tag = EntityManager.GetComponentData<ManualFireDispatched>(tracker);
                Entity targetEntity = tag.m_TargetEntity;

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool targetGone = targetEntity == Entity.Null || !EntityManager.Exists(targetEntity);
                // Grace period: RescueTarget is added via ECB (plays back at end of frame),
                // so HasComponent returns false for 1-2 frames after creation. Avoid false
                // "already resolved" detection until ECB has had time to play back.
                bool alreadyResolved = !targetGone
                    && !EntityManager.HasComponent<RescueTarget>(targetEntity)
                    && (currentFrame - tag.m_CreationFrame) > 1;

                if (timedOut || targetGone || alreadyResolved)
                {
                    if (!targetGone && !alreadyResolved && EntityManager.HasComponent<RescueTarget>(targetEntity))
                    {
                        // Use ECB to defer RescueTarget removal — structural change on rendered building
                        ecb.RemoveComponent<RescueTarget>(targetEntity);
                    }

                    EntityManager.DestroyEntity(tracker);
                    fireCleaned++;
                }
            }
            fireTrackers.Dispose();

            // --- EMS cleanup ---
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
            for (int i = 0; i < crimeTrackers.Length; i++)
            {
                Entity tracker = crimeTrackers[i];
                if (!EntityManager.Exists(tracker))
                    continue;

                ManualCrimeDispatched tag = EntityManager.GetComponentData<ManualCrimeDispatched>(tracker);
                Entity targetEntity = tag.m_TargetEntity;

                bool timedOut = (currentFrame - tag.m_CreationFrame) > TIMEOUT_FRAMES;
                bool targetGone = targetEntity == Entity.Null || !EntityManager.Exists(targetEntity);
                bool resolved = !targetGone && !EntityManager.HasComponent<AccidentSite>(targetEntity);

                if (timedOut || targetGone || resolved)
                {
                    string reason = timedOut ? "timeout" : targetGone ? "targetGone" : "resolved";
                    Mod.Log.Info($"[ManualCleanup] Crime cleanup: tracker={tracker.Index} target={targetEntity.Index} " +
                        $"reason={reason} age={currentFrame - tag.m_CreationFrame}");

                    // CRITICAL: Never destroy the event entity while AccidentSite may still be on
                    // the building referencing it via m_Event. AccidentSiteSystem reads m_Event in a
                    // burst job every 64 frames — if the entity is destroyed, it crashes (BatchUploadSystem).
                    // Only destroy the event entity if the building itself is gone (orphaned entity).
                    // In all other cases, let AccidentSiteSystem own the event entity lifecycle; it will
                    // destroy the event entity when it removes AccidentSite naturally.
                    Entity eventEntity = tag.m_EventEntity;
                    if (targetGone && eventEntity != Entity.Null && EntityManager.Exists(eventEntity))
                    {
                        EntityManager.DestroyEntity(eventEntity);
                    }

                    EntityManager.DestroyEntity(tracker);
                    crimeCleaned++;
                }
            }
            crimeTrackers.Dispose();

            // --- Accident cleanup ---
            var accidentTrackers = m_AccidentTrackerQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < accidentTrackers.Length; i++)
            {
                Entity tracker = accidentTrackers[i];
                ManualAccidentDispatched tag = EntityManager.GetComponentData<ManualAccidentDispatched>(tracker);
                Entity vehicle = tag.m_VehicleEntity;

                bool timedOut = (currentFrame - tag.m_CreationFrame) > ACCIDENT_TIMEOUT_FRAMES;
                bool vehicleGone = vehicle == Entity.Null || !EntityManager.Exists(vehicle);
                // Grace period: InvolvedInAccident is added by ImpactSystem next frame — avoid false resolution
                bool resolved = !vehicleGone
                    && !EntityManager.HasComponent<InvolvedInAccident>(vehicle)
                    && (currentFrame - tag.m_CreationFrame) > 2;

                if (timedOut || vehicleGone || resolved)
                {
                    string reason = timedOut ? "timeout" : vehicleGone ? "vehicleGone" : "resolved";
                    Mod.Log.Info($"[ManualCleanup] Accident cleanup: tracker={tracker.Index} " +
                        $"vehicle={vehicle.Index} reason={reason} age={currentFrame - tag.m_CreationFrame}");

                    Entity eventEntity = tag.m_EventEntity;
                    if (eventEntity != Entity.Null && EntityManager.Exists(eventEntity))
                        EntityManager.DestroyEntity(eventEntity);

                    EntityManager.DestroyEntity(tracker);
                    accidentCleaned++;
                }
            }
            accidentTrackers.Dispose();

            if (shouldLog)
            {
                Mod.Log.Info($"[ManualCleanup] Applied: fire={fireApplied} | " +
                    $"Cleaned: police={policeCleaned} fire={fireCleaned} ems={emsCleaned} crime={crimeCleaned} accident={accidentCleaned}");
            }
        }
    }
}
