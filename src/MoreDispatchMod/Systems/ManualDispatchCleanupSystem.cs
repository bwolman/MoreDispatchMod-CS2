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

        private EntityQuery m_PoliceTrackerQuery;
        private EntityQuery m_FireTrackerQuery;
        private EntityQuery m_EMSTrackerQuery;
        private EntityQuery m_CrimeTrackerQuery;
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
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            int policeCleaned = 0;
            int fireApplied = 0;
            int fireCleaned = 0;
            int emsCleaned = 0;
            int crimeCleaned = 0;

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

                    if (!targetGone && !resolved && EntityManager.HasComponent<AccidentSite>(targetEntity))
                        ecb.RemoveComponent<AccidentSite>(targetEntity);

                    Entity eventEntity = tag.m_EventEntity;
                    if (eventEntity != Entity.Null && EntityManager.Exists(eventEntity))
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

                    if (!targetGone && !resolved && EntityManager.HasComponent<AccidentSite>(targetEntity))
                    {
                        // Use ECB to defer AccidentSite removal — structural change on rendered building
                        ecb.RemoveComponent<AccidentSite>(targetEntity);
                    }

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
                Mod.Log.Info($"[ManualCleanup] Applied: fire={fireApplied} | " +
                    $"Cleaned: police={policeCleaned} fire={fireCleaned} ems={emsCleaned} crime={crimeCleaned}");
            }
        }
    }
}
