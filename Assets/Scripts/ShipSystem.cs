using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Galaxy
{
    [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
    [UpdateAfter(typeof(BuildSpatialDatabaseGroup))]
    [UpdateAfter(typeof(TeamAISystem))]
    [UpdateAfter(typeof(ApplyTeamSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(FinishInitializeSystem))]
    public partial struct ShipSystem : ISystem
    {
        private EntityQuery _spatialDatabasesQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _spatialDatabasesQuery = SystemAPI.QueryBuilder()
                .WithAll<SpatialDatabase, SpatialDatabaseCell, SpatialDatabaseElement>().Build();
            state.RequireForUpdate<Config>();
            state.RequireForUpdate<GameIsSimulating>();
            state.RequireForUpdate<SpatialDatabaseSingleton>();
            state.RequireForUpdate(_spatialDatabasesQuery);
            state.RequireForUpdate<PlanetNavigationGrid>();
            state.RequireForUpdate<TeamManagerReference>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<VFXHitSparksSingleton>();
            state.RequireForUpdate<VFXThrustersSingleton>();
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        public void OnUpdate(ref SystemState state)
        {
            Config config = SystemAPI.GetSingleton<Config>();
            SpatialDatabaseSingleton spatialDatabaseSingleton = SystemAPI.GetSingleton<SpatialDatabaseSingleton>();
            VFXHitSparksSingleton vfxSparksSingleton = SystemAPI.GetSingletonRW<VFXHitSparksSingleton>().ValueRW;
            VFXThrustersSingleton vfxThrustersSingleton = SystemAPI.GetSingletonRW<VFXThrustersSingleton>().ValueRW;

            ShipInitializeJob shipInitializeJob = new ShipInitializeJob
            {
                TeamManagerLookup = SystemAPI.GetComponentLookup<TeamManager>(true),
                ThrustersManager = vfxThrustersSingleton.Manager,
            };
            state.Dependency = shipInitializeJob.Schedule(state.Dependency);
            
            FighterInitializeJob fighterInitializeJob = new FighterInitializeJob
            { };
            state.Dependency = fighterInitializeJob.ScheduleParallel(state.Dependency);

            ShipNavigationJob navigationJob = new ShipNavigationJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                PlanetGridEntity = SystemAPI.GetSingletonEntity<PlanetNavigationGrid>(),
                PlanetNavigationGridLookup = SystemAPI.GetComponentLookup<PlanetNavigationGrid>(true),
                PlanetNavigationCellsBufferLookup = SystemAPI.GetBufferLookup<PlanetNavigationCell>(true),
            };
            state.Dependency = navigationJob.ScheduleParallel(state.Dependency);
            
            FighterAIJob fighterAIJob = new FighterAIJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                FighterActionsLookup = SystemAPI.GetBufferLookup<FighterAction>(true),
                LocalToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                TeamManagerReferencesEntity = SystemAPI.GetSingletonEntity<TeamManagerReference>(),
                TeamManagerReferenceLookup = SystemAPI.GetBufferLookup<TeamManagerReference>(true),
                CachedSpatialDatabase = new CachedSpatialDatabaseRO
                {
                    SpatialDatabaseEntity = spatialDatabaseSingleton.TargetablesSpatialDatabase, 
                    SpatialDatabaseLookup = SystemAPI.GetComponentLookup<SpatialDatabase>(true),
                    CellsBufferLookup = SystemAPI.GetBufferLookup<SpatialDatabaseCell>(true),
                    ElementsBufferLookup = SystemAPI.GetBufferLookup<SpatialDatabaseElement>(true),
                },
            };
            state.Dependency = fighterAIJob.ScheduleParallel(state.Dependency);
            
            WorkerAIJob workerAIJob = new WorkerAIJob
            {
                WorkerActionLookup = SystemAPI.GetBufferLookup<WorkerAction>(true),
                TeamLookup = SystemAPI.GetComponentLookup<Team>(true),
                TeamManagerReferencesEntity = SystemAPI.GetSingletonEntity<TeamManagerReference>(),
                TeamManagerReferenceLookup = SystemAPI.GetBufferLookup<TeamManagerReference>(true),
            };
            state.Dependency = workerAIJob.ScheduleParallel(state.Dependency);

            TraderAIJob traderAIJob = new TraderAIJob
            {
                TraderActionLookup = SystemAPI.GetBufferLookup<TraderAction>(true),
                TeamLookup = SystemAPI.GetComponentLookup<Team>(true),
                PlanetLookup = SystemAPI.GetComponentLookup<Planet>(true),
                TeamManagerReferencesEntity = SystemAPI.GetSingletonEntity<TeamManagerReference>(),
                TeamManagerReferenceLookup = SystemAPI.GetBufferLookup<TeamManagerReference>(true),
            };
            state.Dependency = traderAIJob.ScheduleParallel(state.Dependency);

            FighterExecuteAttackJob executeAttackJob = new FighterExecuteAttackJob
            {
                LaserPrefab = config.LaserPrefab,
                ECB = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                    .CreateCommandBuffer(state.WorldUnmanaged),
                HealthLookup = SystemAPI.GetComponentLookup<Health>(false),
                TeamManagerLookup = SystemAPI.GetComponentLookup<TeamManager>(true),
                HitSparksManager = vfxSparksSingleton.Manager,
            };
            state.Dependency = executeAttackJob.Schedule(state.Dependency);

            WorkerExecutePlanetCaptureJob workerExecutePlanetCaptureJob = new WorkerExecutePlanetCaptureJob
            {
                CapturingWorkerLookup = SystemAPI.GetBufferLookup<CapturingWorker>(false),
            };
            state.Dependency = workerExecutePlanetCaptureJob.Schedule(state.Dependency);

            WorkerExecuteBuildJob workerExecuteBuildJob = new WorkerExecuteBuildJob
            {
                MoonLookup = SystemAPI.GetComponentLookup<Moon>(false),
            };
            state.Dependency = workerExecuteBuildJob.Schedule(state.Dependency);

            TraderExecuteTradeJob traderExecuteTradeJob = new TraderExecuteTradeJob
            {
                PlanetLookup = SystemAPI.GetComponentLookup<Planet>(false),
            };
            state.Dependency = traderExecuteTradeJob.Schedule( state.Dependency);
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(Initialize))]
        public partial struct ShipInitializeJob : IJobEntity
        {
            [ReadOnly]
            public ComponentLookup<TeamManager> TeamManagerLookup;
            public VFXManagerParented<VFXThrusterData> ThrustersManager;
            
            private void Execute(in Team team, ref Ship ship)
            {
                ShipData shipData = ship.ShipData.Value;

                ship.ThrusterVFXIndex = ThrustersManager.Create();
                
                if (ship.ThrusterVFXIndex >= 0 && TeamManagerLookup.TryGetComponent(team.ManagerEntity, out TeamManager teamManager))
                {
                    ThrustersManager.Datas[ship.ThrusterVFXIndex] = new VFXThrusterData
                    {
                        Color = teamManager.ThrusterColor,
                        Size = shipData.ThrusterSize,
                        Length = shipData.ThrusterLength,
                    };
                }
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(Initialize))]
        public partial struct FighterInitializeJob : IJobEntity
        {
            private void Execute(Entity entity, ref Fighter fighter)
            {
                Random random = GameUtilities.GetDeterministicRandom(entity.Index);
                FighterData fighterData = fighter.FighterData.Value;
                fighter.DetectionTimer = random.NextFloat(0f, fighterData.ShipDetectionInterval);
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        public partial struct ShipNavigationJob : IJobEntity, IJobEntityChunkBeginEnd
        {
            public float DeltaTime;
            public Entity PlanetGridEntity;
            [ReadOnly] public ComponentLookup<PlanetNavigationGrid> PlanetNavigationGridLookup;
            [ReadOnly] public BufferLookup<PlanetNavigationCell> PlanetNavigationCellsBufferLookup;

            private PlanetNavigationGrid _cachedGrid; 
            private UnsafeList<PlanetNavigationCell> _cachedCellsBuffer;

            public void Execute(ref LocalTransform transform, ref Ship ship)
            {
                ShipData shipData = ship.ShipData.Value;

                if(ship.BlockNavigation == 0 && ship.NavigationTargetEntity != Entity.Null)
                {
                    float3 vectorToTarget = ship.NavigationTargetPosition - transform.Position;

                    quaternion targetRot = quaternion.LookRotationSafe(vectorToTarget, math.up());
                    transform.Rotation = math.slerp(transform.Rotation, targetRot,
                        MathUtilities.GetSharpnessInterpolant(shipData.SteeringSharpness, DeltaTime));

                    float trueAcceleration = shipData.Acceleration * ship.AccelerationMultiplier;
                    float trueMaxSpeed = shipData.MaxSpeed * ship.MaxSpeedMultiplier;
                    float3 forward = math.mul(transform.Rotation, math.forward());
                    ship.Velocity += forward * trueAcceleration * DeltaTime;

                    ship.Velocity = MathUtilities.ClampToMaxLength(ship.Velocity, trueMaxSpeed);
                }

                if (PlanetNavigationGridUtility.GetCellDataAtPosition(in _cachedGrid, in _cachedCellsBuffer,
                        transform.Position, out PlanetNavigationCell closestPlanetData))
                {
                    float distanceSqToPlanet = math.lengthsq(closestPlanetData.Position - transform.Position);

                    if (distanceSqToPlanet < shipData.PlanetAvoidanceDistance * shipData.PlanetAvoidanceDistance)
                    {
                        float3 shipToPlanet = closestPlanetData.Position - transform.Position;

                        if (math.lengthsq(shipToPlanet) <=
                            closestPlanetData.Radius * closestPlanetData.Radius)
                        {
                            float3 shipToPlanetDir = math.normalizesafe(shipToPlanet);
                            ship.Velocity = MathUtilities.ProjectOnPlane(ship.Velocity, shipToPlanetDir) * math.length(ship.Velocity);
                            transform.Position = closestPlanetData.Position +
                                                 (-shipToPlanetDir * closestPlanetData.Radius);
                        }
                        else if (ship.IgnoreAvoidance == 0)
                        {
                            if (closestPlanetData.Entity != ship.NavigationTargetEntity ||
                                shipData.ShouldAvoidTargetPlanet)
                            {
                                float3 velocityDir = math.normalizesafe(ship.Velocity);
                                float3 normalizedShipToPlanetProjectedOnVelocityDir =
                                    -math.normalizesafe(MathUtilities.ProjectOnPlane(shipToPlanet, velocityDir));
                                float3 pointOfTangencyTarget = closestPlanetData.Position +
                                                               (normalizedShipToPlanetProjectedOnVelocityDir *
                                                                closestPlanetData.Radius *
                                                                shipData.PlanetAvoidanceRelativeOffset);

                                bool wouldCollidePlanet = MathUtilities.SegmentIntersectsSphere(
                                    transform.Position,
                                    transform.Position + (velocityDir * shipData.PlanetAvoidanceDistance),
                                    closestPlanetData.Position,
                                    closestPlanetData.Radius);
                                if (wouldCollidePlanet)
                                {
                                    float3 directionToPointOfTangencyTarget =
                                        math.normalizesafe(pointOfTangencyTarget - transform.Position);
                                    quaternion targetRotation =
                                        quaternion.LookRotationSafe(directionToPointOfTangencyTarget, math.up());

                                    transform.Rotation = math.slerp(transform.Rotation, targetRotation,
                                        MathUtilities.GetSharpnessInterpolant(shipData.SteeringSharpness, DeltaTime));
                                    ship.Velocity = directionToPointOfTangencyTarget * math.length(ship.Velocity);
                                }
                            }
                        }
                    }
                }

                transform.Position += ship.Velocity * DeltaTime;
            }

            public unsafe bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                if (!_cachedCellsBuffer.IsCreated)
                {
                    _cachedGrid = PlanetNavigationGridLookup[PlanetGridEntity];
                    DynamicBuffer<PlanetNavigationCell> planetNavigationCellsBuffer = PlanetNavigationCellsBufferLookup[PlanetGridEntity];
                    _cachedCellsBuffer = new UnsafeList<PlanetNavigationCell>(
                        (PlanetNavigationCell*)planetNavigationCellsBuffer.GetUnsafeReadOnlyPtr(), 
                        planetNavigationCellsBuffer.Length);
                }

                return true;
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask, bool chunkWasExecuted)
            {
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        public partial struct FighterAIJob : IJobEntity, IJobEntityChunkBeginEnd
        {
            public float DeltaTime;
            public CachedSpatialDatabaseRO CachedSpatialDatabase;
            [ReadOnly] public BufferLookup<FighterAction> FighterActionsLookup;
            [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            public Entity TeamManagerReferencesEntity;
            [ReadOnly] public BufferLookup<TeamManagerReference> TeamManagerReferenceLookup;

            private UnsafeList<float> _tmpFinalImportances;
            private UnsafeList<UnsafeList<FighterAction>> _cachedFighterActions;

            public void Execute(Entity entity, in LocalTransform transform, ref Ship ship, in Team team, ref Fighter fighter, EnabledRefRW<ExecuteAttack> executeAttack)
            {
                if (team.IsNonNeutral())
                {
                    FighterData fighterData = fighter.FighterData.Value;

                    ship.IgnoreAvoidance = 0;

                    if (fighter.AttackTimer > 0f)
                    {
                        fighter.AttackTimer -= DeltaTime;
                    }

                    if (fighter.TargetIsEnemyShip == 1)
                    {
                        if (LocalToWorldLookup.TryGetComponent(ship.NavigationTargetEntity, out LocalToWorld targetLtW))
                        {
                            float targetDistanceSq = math.distancesq(targetLtW.Position, transform.Position);
                            if (targetDistanceSq > fighterData.AttackRange * fighterData.AttackRange)
                            {
                                fighter.TargetIsEnemyShip = 0;
                                ship.NavigationTargetEntity = Entity.Null;
                            }
                            else
                            {
                                ship.IgnoreAvoidance = 1;

                                ship.NavigationTargetPosition = targetLtW.Position;

                                if (fighter.AttackTimer <= 0f)
                                {
                                    float3 shipToTarget = targetLtW.Position - transform.Position;
                                    float3 shipToTargetDir = math.normalizesafe(shipToTarget);
                                    float3 shipForward = math.mul(transform.Rotation, math.forward());
                    
                                    bool activeAttackTargetInSights =
                                        math.dot(shipForward, shipToTargetDir) > fighterData.DotProdThresholdForTargetInSights;
                                    if (activeAttackTargetInSights)
                                    {
                                        executeAttack.ValueRW = true;
                                    }
                                }
                            }
                        }
                        else
                        {
                            fighter.TargetIsEnemyShip = 0;
                            ship.NavigationTargetEntity = Entity.Null;
                        }
                    }
                    else
                    {
                        fighter.DetectionTimer -= DeltaTime;
                        if (fighter.DetectionTimer <= 0f && fighterData.DetectionRange > 0f)
                        {
                            ShipQueryCollector collector =
                                new ShipQueryCollector(entity, transform.Position, team.Index);
                            SpatialDatabase.QueryAABBCellProximityOrder(in CachedSpatialDatabase._SpatialDatabase,
                                in CachedSpatialDatabase._SpatialDatabaseCells,
                                in CachedSpatialDatabase._SpatialDatabaseElements, transform.Position,
                                fighterData.DetectionRange, ref collector);

                            ship.NavigationTargetEntity = collector.ClosestEnemy.Entity;
                            fighter.TargetIsEnemyShip = 1;

                            fighter.DetectionTimer += fighterData.ShipDetectionInterval;
                        }

                        if (fighter.TargetIsEnemyShip == 0)
                        {
                            UnsafeList<FighterAction> fighterActionsList = _cachedFighterActions[team.Index];
                            if (fighterActionsList.IsCreated)
                            {
                                ShipData shipData = ship.ShipData.Value;

                                _tmpFinalImportances.Clear();
                                float importancesTotal = 0f;
                                float currentActionImportance = -1f;

                                for (int i = 0; i < fighterActionsList.Length; i++)
                                {
                                    FighterAction fighterActions = fighterActionsList[i];

                                    float proximityImportance = GameUtilities.CalculateProximityImportance(
                                        transform.Position, fighterActions.Position,
                                        shipData.MaxDistanceSqForPlanetProximityImportanceScaling,
                                        shipData.PlanetProximityImportanceRemap);
                                    float finalImportance = fighterActions.Importance * proximityImportance;

                                    _tmpFinalImportances.Add(finalImportance);
                                    importancesTotal += finalImportance;

                                    if (currentActionImportance < 0f && fighterActions.Entity == ship.NavigationTargetEntity)
                                    {
                                        currentActionImportance = fighterActions.Importance;
                                    }
                                }

                                if (currentActionImportance < 0f)
                                {
                                    ship.NavigationTargetEntity = Entity.Null;
                                }

                                Random persistentRandom = GameUtilities.GetDeterministicRandom(entity.Index);

                                int weightedRandomIndex = GameUtilities.GetWeightedRandomIndex(importancesTotal,
                                    in _tmpFinalImportances, ref persistentRandom);
                                if (weightedRandomIndex >= 0)
                                {
                                    FighterAction fighterAction = fighterActionsList[weightedRandomIndex];

                                    if (_tmpFinalImportances[weightedRandomIndex] > currentActionImportance * 2f)
                                    {
                                        ship.NavigationTargetEntity = fighterAction.Entity;
                                        ship.NavigationTargetPosition = fighterAction.Position;
                                        ship.NavigationTargetRadius = fighterAction.Radius;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            public unsafe bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                CachedSpatialDatabase.CacheData();
                if (!_tmpFinalImportances.IsCreated)
                {
                    _tmpFinalImportances = new UnsafeList<float>(256, Allocator.Temp);
                }

                if (!_cachedFighterActions.IsCreated)
                {
                    DynamicBuffer<TeamManagerReference> teamManagerReferences =
                        TeamManagerReferenceLookup[TeamManagerReferencesEntity];
                    
                    _cachedFighterActions = new UnsafeList<UnsafeList<FighterAction>>(teamManagerReferences.Length, Allocator.Temp);
                    _cachedFighterActions.Resize(teamManagerReferences.Length);

                    for (int teamIndex = 0; teamIndex < teamManagerReferences.Length; teamIndex++)
                    {
                        if(FighterActionsLookup.TryGetBuffer(teamManagerReferences[teamIndex].Entity, 
                               out DynamicBuffer<FighterAction> fighterActionsBuffer))
                        {
                            _cachedFighterActions[teamIndex] = new UnsafeList<FighterAction>(
                                (FighterAction*)fighterActionsBuffer.GetUnsafeReadOnlyPtr(), fighterActionsBuffer.Length);

                            if (fighterActionsBuffer.Length > _tmpFinalImportances.Capacity)
                            {
                                _tmpFinalImportances.SetCapacity(fighterActionsBuffer.Length);
                            }
                        }
                    }
                }

                return true;
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask, bool chunkWasExecuted)
            {
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(Team))]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        public partial struct WorkerAIJob : IJobEntity, IJobEntityChunkBeginEnd
        {
            [ReadOnly] public BufferLookup<WorkerAction> WorkerActionLookup;
            [ReadOnly] public ComponentLookup<Team> TeamLookup;
            public Entity TeamManagerReferencesEntity;
            [ReadOnly] public BufferLookup<TeamManagerReference> TeamManagerReferenceLookup;

            private UnsafeList<float> _tmpFinalImportances;
            private UnsafeList<UnsafeList<WorkerAction>> _cachedWorkerActions;

            public void Execute(Entity entity, ref Worker worker, in LocalTransform transform, ref Ship ship,
                EnabledRefRW<ExecutePlanetCapture> executePlanetCapture, EnabledRefRW<ExecuteBuild> executeBuild)
            {
                ship.BlockNavigation = 0;
                
                Team team = TeamLookup[entity];
                if (team.IsNonNeutral())
                {
                    WorkerData workerData = worker.WorkerData.Value;

                    if (ship.NavigationTargetEntity != Entity.Null)
                    {
                        if (worker.DesiredBuildingPrefab != Entity.Null)
                        {
                            float requiredRange = workerData.BuildRange + ship.NavigationTargetRadius;
                            if (math.distancesq(transform.Position, ship.NavigationTargetPosition) < requiredRange * requiredRange)
                            {
                                executeBuild.ValueRW = true;
                            }
                        }
                        else
                        {
                            float requiredRange = workerData.CaptureRange + ship.NavigationTargetRadius;
                            if (math.distancesq(transform.Position, ship.NavigationTargetPosition) <
                                requiredRange * requiredRange)
                            {
                                if (TeamLookup[ship.NavigationTargetEntity].Index != team.Index)
                                {
                                    executePlanetCapture.ValueRW = true;
                                }
                            }
                        }
                    }

                    UnsafeList<WorkerAction> workerActionsBuffer = _cachedWorkerActions[team.Index];
                    if (workerActionsBuffer.IsCreated)
                    {
                        ShipData shipData = ship.ShipData.Value;
                        Random persistentRandom = GameUtilities.GetDeterministicRandom(entity.Index);

                        _tmpFinalImportances.Clear();
                        float importancesTotal = 0f;
                        float currentActionImportance = -1f;
                        
                        for (int i = 0; i < workerActionsBuffer.Length; i++)
                        {
                            WorkerAction workerAction = workerActionsBuffer[i];

                            float proximityImportance = GameUtilities.CalculateProximityImportance(transform.Position,
                                workerAction.Position, shipData.MaxDistanceSqForPlanetProximityImportanceScaling,
                                shipData.PlanetProximityImportanceRemap);
                            float finalImportance = workerAction.Importance * proximityImportance;

                            _tmpFinalImportances.Add(finalImportance);
                            importancesTotal += finalImportance;

                            if (currentActionImportance < 0f && workerAction.Entity == ship.NavigationTargetEntity)
                            {
                                currentActionImportance = workerAction.Importance;
                            }
                        }

                        if (currentActionImportance < 0f)
                        {
                            worker.DesiredBuildingPrefab = Entity.Null;
                            ship.NavigationTargetEntity = Entity.Null;
                        }

                        int weightedRandomIndex = GameUtilities.GetWeightedRandomIndex(importancesTotal,
                            in _tmpFinalImportances, ref persistentRandom);
                        if (weightedRandomIndex >= 0)
                        {
                            WorkerAction workerAction = workerActionsBuffer[weightedRandomIndex];

                            if (_tmpFinalImportances[weightedRandomIndex] > currentActionImportance * 2f)
                            {
                                ship.NavigationTargetEntity = workerAction.Entity;
                                ship.NavigationTargetPosition = workerAction.Position;
                                ship.NavigationTargetRadius = workerAction.PlanetRadius;

                                if (workerAction.Type == 1)
                                {
                                    worker.DesiredBuildingPrefab = workerAction.BuildingPrefab;
                                }
                            }
                        }
                    }
                }
            }

            public unsafe bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                if (!_tmpFinalImportances.IsCreated)
                {
                    _tmpFinalImportances = new UnsafeList<float>(64, Allocator.Temp);
                }

                if (!_cachedWorkerActions.IsCreated)
                {
                    DynamicBuffer<TeamManagerReference> teamManagerReferences =
                        TeamManagerReferenceLookup[TeamManagerReferencesEntity];
                    
                    _cachedWorkerActions = new UnsafeList<UnsafeList<WorkerAction>>(teamManagerReferences.Length, Allocator.Temp);
                    _cachedWorkerActions.Resize(teamManagerReferences.Length);

                    for (int teamIndex = 0; teamIndex < teamManagerReferences.Length; teamIndex++)
                    {
                        if(WorkerActionLookup.TryGetBuffer(teamManagerReferences[teamIndex].Entity, 
                               out DynamicBuffer<WorkerAction> workerActionsBuffer))
                        {
                            _cachedWorkerActions[teamIndex] = new UnsafeList<WorkerAction>(
                                (WorkerAction*)workerActionsBuffer.GetUnsafeReadOnlyPtr(), workerActionsBuffer.Length);

                            if (workerActionsBuffer.Length > _tmpFinalImportances.Capacity)
                            {
                                _tmpFinalImportances.SetCapacity(workerActionsBuffer.Length);
                            }
                        }
                    }
                }
                
                return true;
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask, bool chunkWasExecuted)
            {
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(ExecutePlanetCapture))]
        public partial struct WorkerExecutePlanetCaptureJob : IJobEntity
        {
            public BufferLookup<CapturingWorker> CapturingWorkerLookup;

            public void Execute(ref Worker worker, in Team team, ref Ship ship,
                EnabledRefRW<ExecutePlanetCapture> executePlanetCapture)
            {
                executePlanetCapture.ValueRW = false;

                WorkerData workerData = worker.WorkerData.Value;

                ship.BlockNavigation = 1;
                ship.Velocity = 0;
                
                if (CapturingWorkerLookup.TryGetBuffer(ship.NavigationTargetEntity,
                        out DynamicBuffer<CapturingWorker> capturingWorkers))
                {
                    CapturingWorker currentWorker = capturingWorkers[team.Index];
                    currentWorker.CaptureSpeed += workerData.CaptureSpeed;
                    capturingWorkers[team.Index] = currentWorker;
                }
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(ExecuteBuild))]
        public partial struct WorkerExecuteBuildJob : IJobEntity
        {
            public ComponentLookup<Moon> MoonLookup;

            public void Execute(ref Worker worker, ref Ship ship, EnabledRefRW<ExecuteBuild> executeBuild)
            {
                executeBuild.ValueRW = false;

                WorkerData workerData = worker.WorkerData.Value;

                ship.BlockNavigation = 1;
                ship.Velocity = 0;
                
                if (MoonLookup.TryGetComponent(ship.NavigationTargetEntity, out Moon moon))
                {
                    if (moon.BuiltPrefab == Entity.Null)
                    {
                        moon.BuiltPrefab = worker.DesiredBuildingPrefab;
                    }

                    moon.CummulativeBuildSpeed += workerData.BuildSpeed;
                    MoonLookup[ship.NavigationTargetEntity] = moon;
                }
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(Team))]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        public partial struct TraderAIJob : IJobEntity, IJobEntityChunkBeginEnd
        {
            [ReadOnly] 
            public BufferLookup<TraderAction> TraderActionLookup;
            [ReadOnly] 
            public ComponentLookup<Team> TeamLookup;
            [ReadOnly] 
            public ComponentLookup<Planet> PlanetLookup;
            public Entity TeamManagerReferencesEntity;
            [ReadOnly] public BufferLookup<TeamManagerReference> TeamManagerReferenceLookup;

            private UnsafeList<float3> _tmpFinalImportancesVector;
            private UnsafeList<float> _tmpFinalImportances;
            private UnsafeList<UnsafeList<TraderAction>> _cachedTraderActions;

            public void Execute(Entity entity, in LocalTransform transform, ref Ship ship, ref Trader trader, EnabledRefRW<ExecuteTrade> executeTrade)
            {
                Team team = TeamLookup[entity];
                if (team.IsNonNeutral())
                {
                    TraderData traderData = trader.TraderData.Value;

                    if (ship.NavigationTargetEntity == Entity.Null)
                    {
                        ship.Velocity = float3.zero;
                        ship.BlockNavigation = 1;

                        FindNewTradeRoute(entity, team, ref trader, ref ship);
                    }
                    else
                    {
                        ship.BlockNavigation = 0;
                        Team activePlanetTeam = TeamLookup[ship.NavigationTargetEntity];

                        if (activePlanetTeam.Index != team.Index)
                        {
                            if (ship.NavigationTargetEntity == trader.ReceivingPlanetEntity)
                            {
                                SetNearestPlanetAsActiveAndReceiving(team, ref trader, ref ship, in transform);
                            }
                            else
                            {
                                ResetTrade(ref ship, ref trader);
                            }
                        }
                        else if (PlanetLookup.TryGetComponent(ship.NavigationTargetEntity, out Planet planet))
                        {
                            float requiredRange = traderData.ResourceExchangeRange + ship.NavigationTargetRadius;
                            if (math.distancesq(transform.Position, ship.NavigationTargetPosition) <=
                                requiredRange * requiredRange)
                            {
                                executeTrade.ValueRW = true;
                            }
                        }
                    }
                }
            }

            private void FindNewTradeRoute(Entity entity, Team team, ref Trader trader, ref Ship ship)
            {
                Random persistentRandom = GameUtilities.GetDeterministicRandom(entity.Index, trader.FindTradeRouteAttempts);

                UnsafeList<TraderAction> traderActionsBuffer = _cachedTraderActions[team.Index];
                if (traderActionsBuffer.IsCreated)
                {
                    ship.NavigationTargetEntity = Entity.Null;

                    float3 receivingPlanetStorageRatioPercentile = float3.zero;

                    {
                        _tmpFinalImportancesVector.Clear();
                        float3 importancesTotalVector = 0f;

                        for (int i = 0; i < traderActionsBuffer.Length; i++)
                        {
                            TraderAction traderAction = traderActionsBuffer[i];

                            float3 resourceNeedImportance =
                                math.saturate(new float3(1f) - traderAction.ResourceStorageRatioPercentile);
                            float3 finalImportance = traderAction.ImportanceBias * resourceNeedImportance;

                            _tmpFinalImportancesVector.Add(finalImportance);
                            importancesTotalVector += finalImportance;
                        }

                        int weightedRandomIndex = GameUtilities.GetWeightedRandomIndex(importancesTotalVector,
                            in _tmpFinalImportancesVector, ref persistentRandom, out int subIndex);
                        if (weightedRandomIndex >= 0)
                        {
                            TraderAction traderAction = traderActionsBuffer[weightedRandomIndex];

                            trader.ReceivingPlanetEntity = traderAction.Entity;
                            trader.ReceivingPlanetPosition = traderAction.Position;
                            trader.ReceivingPlanetRadius = traderAction.Radius;

                            receivingPlanetStorageRatioPercentile = traderAction.ResourceStorageRatioPercentile;
                            switch (subIndex)
                            {
                                case 0:
                                    trader.ChosenResourceMask = new float3(1f, 0f, 0f);
                                    break;
                                case 1:
                                    trader.ChosenResourceMask = new float3(0f, 1f, 0f);
                                    break;
                                case 2:
                                    trader.ChosenResourceMask = new float3(0f, 0f, 1f);
                                    break;
                            }
                        }
                    }

                    {
                        _tmpFinalImportances.Clear();
                        float importancesTotal = 0f;

                        float receiverStorageRatio =
                            math.csum(trader.ChosenResourceMask * receivingPlanetStorageRatioPercentile);

                        for (int i = 0; i < traderActionsBuffer.Length; i++)
                        {
                            TraderAction traderAction = traderActionsBuffer[i];

                            float finalImportance = 0f;
                            float giverStorageRatio = math.csum(trader.ChosenResourceMask *
                                                           traderAction.ResourceStorageRatioPercentile);
                            if (giverStorageRatio > receiverStorageRatio)
                            {
                                float resourceGivingImportance = giverStorageRatio - receiverStorageRatio;
                                finalImportance = traderAction.ImportanceBias * resourceGivingImportance;
                            }

                            _tmpFinalImportances.Add(finalImportance);
                            importancesTotal += finalImportance;
                        }

                        int weightedRandomIndex = GameUtilities.GetWeightedRandomIndex(importancesTotal,
                            in _tmpFinalImportances, ref persistentRandom);
                        if (weightedRandomIndex >= 0)
                        {
                            TraderAction traderAction = traderActionsBuffer[weightedRandomIndex];
                            ship.NavigationTargetEntity = traderAction.Entity;
                            ship.NavigationTargetPosition = traderAction.Position;
                            ship.NavigationTargetRadius = traderAction.Radius;
                        }
                        else
                        {
                            trader.FindTradeRouteAttempts++;
                        }
                    }
                }
            }

            private void SetNearestPlanetAsActiveAndReceiving(Team team, ref Trader trader, ref Ship ship, in LocalTransform transform)
            {
                if (TraderActionLookup.TryGetBuffer(team.ManagerEntity,
                        out DynamicBuffer<TraderAction> traderActionsBuffer))
                {
                    Entity closestPlanetEntity = Entity.Null;
                    float3 closestPlanetPosition = default;
                    float closestPlanetRadius = default;
                    float closestPlanetDistanceSq = float.MaxValue;
                    for (int i = 0; i < traderActionsBuffer.Length; i++)
                    {
                        TraderAction traderAction = traderActionsBuffer[i];
                        float distSq = math.distancesq(traderAction.Position, transform.Position);
                        if (distSq < closestPlanetDistanceSq)
                        {
                            closestPlanetEntity = traderAction.Entity;
                            closestPlanetPosition = traderAction.Position;
                            closestPlanetRadius = traderAction.Radius;

                            closestPlanetDistanceSq = distSq;
                        }
                    }

                    if (closestPlanetEntity != Entity.Null)
                    {
                        ship.NavigationTargetEntity = closestPlanetEntity;
                        ship.NavigationTargetPosition = closestPlanetPosition;
                        ship.NavigationTargetRadius = closestPlanetRadius;

                        trader.ReceivingPlanetEntity = closestPlanetEntity;
                        trader.ReceivingPlanetPosition = closestPlanetPosition;
                        trader.ReceivingPlanetRadius = closestPlanetRadius;
                    }
                    else
                    {
                        ResetTrade(ref ship, ref trader);
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ResetTrade(ref Ship ship, ref Trader trader)
            {
                ship.NavigationTargetEntity = Entity.Null;
                ship.NavigationTargetPosition = default;
                ship.NavigationTargetRadius = default;
                    
                trader.ReceivingPlanetEntity = Entity.Null;
                trader.ReceivingPlanetPosition = default;
                trader.ReceivingPlanetRadius = default;
                trader.FindTradeRouteAttempts = 0;
            }
            
            public unsafe bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                if (!_tmpFinalImportancesVector.IsCreated)
                {
                    _tmpFinalImportancesVector = new UnsafeList<float3>(64, Allocator.Temp);
                }
                if (!_tmpFinalImportances.IsCreated)
                {
                    _tmpFinalImportances = new UnsafeList<float>(64, Allocator.Temp);
                }

                if (!_cachedTraderActions.IsCreated)
                {
                    DynamicBuffer<TeamManagerReference> teamManagerReferences =
                        TeamManagerReferenceLookup[TeamManagerReferencesEntity];
                    
                    _cachedTraderActions = new UnsafeList<UnsafeList<TraderAction>>(teamManagerReferences.Length, Allocator.Temp);
                    _cachedTraderActions.Resize(teamManagerReferences.Length);

                    for (int teamIndex = 0; teamIndex < teamManagerReferences.Length; teamIndex++)
                    {
                        if(TraderActionLookup.TryGetBuffer(teamManagerReferences[teamIndex].Entity, 
                               out DynamicBuffer<TraderAction> traderActionsBuffer))
                        {
                            _cachedTraderActions[teamIndex] = new UnsafeList<TraderAction>(
                                (TraderAction*)traderActionsBuffer.GetUnsafeReadOnlyPtr(), traderActionsBuffer.Length);

                            if (traderActionsBuffer.Length > _tmpFinalImportances.Capacity)
                            {
                                _tmpFinalImportances.SetCapacity(traderActionsBuffer.Length);
                            }
                            if (traderActionsBuffer.Length > _tmpFinalImportancesVector.Capacity)
                            {
                                _tmpFinalImportancesVector.SetCapacity(traderActionsBuffer.Length);
                            }
                        }
                    }
                }
                
                return true;
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask, bool chunkWasExecuted)
            {
            }
        }


        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(Team))]
        [WithAll(typeof(ExecuteTrade))]
        public partial struct TraderExecuteTradeJob : IJobEntity
        {
            public ComponentLookup<Planet> PlanetLookup;

            public void Execute(ref Ship ship, ref Trader trader, EnabledRefRW<ExecuteTrade> executeTrade)
            {
                executeTrade.ValueRW = false;

                if (PlanetLookup.TryGetComponent(ship.NavigationTargetEntity, out Planet planet))
                {
                    TraderData traderData = trader.TraderData.Value;

                    if (ship.NavigationTargetEntity == trader.ReceivingPlanetEntity)
                    {
                        ship.Velocity = float3.zero;
                        ship.BlockNavigation = 1;

                        planet.ResourceCurrentStorage = math.clamp(
                            planet.ResourceCurrentStorage + trader.CarriedResources, float3.zero,
                            planet.ResourceMaxStorage);
                        trader.CarriedResources = float3.zero;

                        PlanetLookup[ship.NavigationTargetEntity] = planet;
                        ResetTrade(ref ship, ref trader);
                    }
                    else
                    {
                        ship.Velocity = float3.zero;
                        ship.BlockNavigation = 1;

                        float3 takenResources =
                            trader.ChosenResourceMask * traderData.ResourceCarryCapacity;
                        takenResources = math.min(takenResources, planet.ResourceCurrentStorage);
                        planet.ResourceCurrentStorage = math.clamp(
                            planet.ResourceCurrentStorage - takenResources, float3.zero,
                            planet.ResourceMaxStorage);
                        trader.CarriedResources += takenResources;
                        PlanetLookup[ship.NavigationTargetEntity] = planet;

                        ship.NavigationTargetEntity = trader.ReceivingPlanetEntity;
                        ship.NavigationTargetPosition = trader.ReceivingPlanetPosition;
                        ship.NavigationTargetRadius = trader.ReceivingPlanetRadius;
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ResetTrade(ref Ship ship, ref Trader trader)
            {
                ship.NavigationTargetEntity = Entity.Null;
                ship.NavigationTargetPosition = default;
                ship.NavigationTargetRadius = default;
                    
                trader.ReceivingPlanetEntity = Entity.Null;
                trader.ReceivingPlanetPosition = default;
                trader.ReceivingPlanetRadius = default;
                trader.FindTradeRouteAttempts = 0;
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(ExecuteAttack))]
        public partial struct FighterExecuteAttackJob : IJobEntity
        {
            public Entity LaserPrefab;
            public EntityCommandBuffer ECB;
            public ComponentLookup<Health> HealthLookup;
            [ReadOnly] public ComponentLookup<TeamManager> TeamManagerLookup;

            public VFXManager<VFXHitSparksRequest> HitSparksManager;

            public void Execute(in Ship ship, ref Fighter fighter, in Team team, in LocalTransform transform,
                EnabledRefRW<ExecuteAttack> executeAttack)
            {
                executeAttack.ValueRW = false;

                FighterData fighterData = fighter.FighterData.Value;

                if (ship.NavigationTargetEntity != Entity.Null &&
                    HealthLookup.TryGetComponent(ship.NavigationTargetEntity, out Health enemyHealth))
                {
                    GameUtilities.ApplyDamage(ref enemyHealth, fighterData.AttackDamage * fighter.DamageMultiplier);
                    HealthLookup[ship.NavigationTargetEntity] = enemyHealth;
                }

                float3 shipToTarget = ship.NavigationTargetPosition - transform.Position;
                float3 shipToTargetDir = math.normalizesafe(shipToTarget);

                if (TeamManagerLookup.TryGetComponent(team.ManagerEntity, out TeamManager teamManager))
                {
                    GameUtilities.SpawnLaser(ECB, LaserPrefab, teamManager.LaserColor, transform.Position, shipToTargetDir,
                        math.length(shipToTarget));

                    HitSparksManager.AddRequest(new VFXHitSparksRequest
                    {
                        Position = ship.NavigationTargetPosition,
                        Color = teamManager.LaserSparksColor,
                    });
                }

                fighter.AttackTimer = fighterData.AttackDelay;
            }
        }
    }
}
