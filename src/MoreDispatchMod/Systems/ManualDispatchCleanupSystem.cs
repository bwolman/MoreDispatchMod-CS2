using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Events;
using Game.Objects;
using Game.Pathfind;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;

using MoreDispatchMod.Components;

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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

            // Police: no apply phase needed — Emergency car flags are set via
            // SetComponentData in CreatePoliceDispatch() (non-structural, safe).
            // DO NOT add EffectsUpdated here — it's a structural change on a
            // rendered entity that crashes BatchUploadSystem.
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

            // --- Police cleanup + heartbeat ---
            // We do NOT use ServiceRequest/PoliceEmergencyRequest for dispatch. PoliceCarAISystem
            // calls ValidateSite() on the request's m_Site entity, requires AccidentSite, fails,
            // and returns the car after ~64 frames. Instead we assert Emergency flags, Target, and
            // empty ServiceDispatch each tick. Cleanup on arrival, timeout, destruction, or shift end.
            for (int i = 0; i < policeTrackers.Length; i++)
            {
                Entity tracker = policeTrackers[i];
                ManualPoliceDispatched tag = EntityManager.GetComponentData<ManualPoliceDispatched>(tracker);
                Entity carEntity = tag.m_PoliceCarEntity;
                uint age = currentFrame - tag.m_CreationFrame;

                bool timedOut = age > TIMEOUT_FRAMES;
                bool carGone = carEntity == Entity.Null || !EntityManager.Exists(carEntity)
                               || !EntityManager.HasComponent<PoliceCar>(carEntity);

                // ---- Heartbeat: re-assert dispatch every tick ----
                bool shiftEnded = false;
                if (!carGone && !timedOut)
                {
                    PoliceCar pc = EntityManager.GetComponentData<PoliceCar>(carEntity);
                    shiftEnded = (pc.m_State & PoliceCarFlags.ShiftEnded) != 0;

                    if (!shiftEnded)
                    {
                        bool needPathUpdate = false;

                        // 1. Clear Returning if vanilla AI sent the car home prematurely.
                        if ((pc.m_State & PoliceCarFlags.Returning) != 0)
                        {
                            pc.m_State &= ~PoliceCarFlags.Returning;
                            EntityManager.SetComponentData(carEntity, pc);
                            needPathUpdate = true;
                            Mod.Log.Info($"[ManualCleanup] Police heartbeat: cleared Returning car={carEntity.Index} age={age}");
                        }

                        // 2. Keep ServiceDispatch empty so the station can't re-assign the car.
                        if (EntityManager.HasBuffer<ServiceDispatch>(carEntity))
                        {
                            DynamicBuffer<ServiceDispatch> buf = EntityManager.GetBuffer<ServiceDispatch>(carEntity);
                            if (buf.Length > 0)
                            {
                                buf.Clear();
                                needPathUpdate = true;
                            }
                        }

                        // 3. Ensure Emergency flags are maintained.
                        Car car = EntityManager.GetComponentData<Car>(carEntity);
                        if ((car.m_Flags & CarFlags.Emergency) == 0 || (car.m_Flags & CarFlags.AnyLaneTarget) != 0)
                        {
                            car.m_Flags |= CarFlags.Emergency | CarFlags.StayOnRoad | CarFlags.UsePublicTransportLanes;
                            car.m_Flags &= ~CarFlags.AnyLaneTarget;
                            EntityManager.SetComponentData(carEntity, car);
                        }

                        // 4. Re-apply Target if vanilla AI redirected the car.
                        if (EntityManager.HasComponent<Target>(carEntity))
                        {
                            Target currentTarget = EntityManager.GetComponentData<Target>(carEntity);
                            if (currentTarget.m_Target != tag.m_TargetEntity)
                            {
                                EntityManager.SetComponentData(carEntity, new Target(tag.m_TargetEntity));
                                needPathUpdate = true;
                            }
                        }

                        // 5. Trigger pathfinding only when something changed, not every tick.
                        if (needPathUpdate && EntityManager.HasComponent<PathOwner>(carEntity))
                        {
                            PathOwner pathOwner = EntityManager.GetComponentData<PathOwner>(carEntity);
                            if ((pathOwner.m_State & PathFlags.Updated) == 0)
                            {
                                pathOwner.m_State |= PathFlags.Updated;
                                EntityManager.SetComponentData(carEntity, pathOwner);
                            }
                        }
                    }
                }

                // ---- DIAG log ----
                if (shouldLog && !carGone)
                {
                    PoliceCar pcLog = EntityManager.GetComponentData<PoliceCar>(carEntity);
                    Car carLog = EntityManager.GetComponentData<Car>(carEntity);
                    PathOwner pathOwnerLog = EntityManager.GetComponentData<PathOwner>(carEntity);
                    Mod.Log.Info($"[ManualCleanup] DIAG police tracker={tracker.Index} car={carEntity.Index} " +
                        $"target={tag.m_TargetEntity.Index} state=0x{(uint)pcLog.m_State:X} " +
                        $"carFlags=0x{(uint)carLog.m_Flags:X} " +
                        $"pathState=0x{(uint)pathOwnerLog.m_State:X} age={age}");
                }

                // ---- Cleanup conditions ----
                // carReturning is no longer a trigger — heartbeat clears it unless shift ended.
                bool closeEnough = false;
                if (!carGone && !shiftEnded && age > 120
                    && tag.m_TargetEntity != Entity.Null
                    && EntityManager.Exists(tag.m_TargetEntity)
                    && EntityManager.HasComponent<Game.Objects.Transform>(carEntity)
                    && EntityManager.HasComponent<Game.Objects.Transform>(tag.m_TargetEntity))
                {
                    float3 carPos = EntityManager.GetComponentData<Game.Objects.Transform>(carEntity).m_Position;
                    float3 targetPos = EntityManager.GetComponentData<Game.Objects.Transform>(tag.m_TargetEntity).m_Position;
                    closeEnough = math.distancesq(carPos, targetPos) < (100f * 100f);
                }

                if (timedOut || carGone || shiftEnded || closeEnough)
                {
                    string reason = timedOut ? "timeout" : carGone ? "carGone" : shiftEnded ? "shiftEnded" : "closeEnough";
                    Mod.Log.Info($"[ManualCleanup] Police cleanup: tracker={tracker.Index} car={carEntity.Index} " +
                        $"reason={reason} age={age}");

                    Entity requestEntity = tag.m_RequestEntity;
                    if (requestEntity != Entity.Null && EntityManager.Exists(requestEntity))
                    {
                        EntityManager.DestroyEntity(requestEntity);
                    }

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
