using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

namespace Galaxy
{

    [Serializable]
    public struct FleetAssessment
    {
        public int Count;
        public float Value;
    }

    public struct EmpireStatistics
    {
        public int OwnedPlanetsCount;
        public int TotalPlanetsCount;

        public int MaxAlliedFightersCountForPlanet;
        public int MaxEnemyFightersCountForPlanet;
        public float FarthestDistance;
        public float3 MaxResourceStorageRatio;
        public float3 MaxResourceGenerationRate;

        public FleetAssessment FightersAssessment;
        public FleetAssessment WorkersAssessment;
        public FleetAssessment TradersAssessment;
        public int TotalNonFighterShipsCount;
        public int TotalShipsCount;
    }

    public struct PlanetStatistics
    {
        public float ThreatLevel;
        public float SafetyLevel;
        public float DistanceFromOwnedPlanetsScore;
        public float3 ResourceStorageRatioPercentile;
        public float ResourceGenerationScore;
    }

    [BurstCompile]
    [UpdateAfter(typeof(BeginSimulationMainThreadGroup))]
    [UpdateBefore(typeof(BuildSpatialDatabaseGroup))]
    [UpdateAfter(typeof(LaserSystem))]
    public partial struct TeamAISystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameIsSimulating>();
            state.RequireForUpdate<TeamManagerReference>();
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        public void OnUpdate(ref SystemState state)
        {
            {
                bool anyTeamDefeated = false;
                foreach (var teamManagerAI in SystemAPI.Query<RefRO<TeamManagerAI>>())
                {
                    if (teamManagerAI.ValueRO.IsDefeated)
                    {
                        anyTeamDefeated = true;
                        break;
                    }
                }

                if (anyTeamDefeated)
                {
                    EntityQuery unitsQuery = SystemAPI.QueryBuilder().WithAll<Team, Health>().WithAny<Ship, Building>().Build();
                    NativeArray<Entity> unitEntities = unitsQuery.ToEntityArray(state.WorldUpdateAllocator);
                    NativeArray<Team> unitTeams = unitsQuery.ToComponentDataArray<Team>(state.WorldUpdateAllocator);

                    TeamDefeatedJob teamDefeatedJob = new TeamDefeatedJob
                    {
                        ECB = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                            .CreateCommandBuffer(state.WorldUnmanaged),

                        UnitEntities = unitEntities,
                        UnitTeams = unitTeams,

                        HealthLookup = SystemAPI.GetComponentLookup<Health>(false),
                    };
                    state.Dependency = teamDefeatedJob.Schedule(state.Dependency);

                    unitEntities.Dispose(state.Dependency);
                    unitTeams.Dispose(state.Dependency);
                }
            }

            DynamicBuffer<TeamManagerReference> teamManagerReferences =
                SystemAPI.GetSingletonBuffer<TeamManagerReference>();
            EntityQuery teamQuery = SystemAPI.QueryBuilder().WithAll<TeamManager>().Build();
            EntityQuery planetsQuery = SystemAPI.QueryBuilder().WithAll<Planet, LocalTransform, Team>().Build();
            int teamsCount = teamManagerReferences.Length;
            int aliveTeamsCount = teamQuery.CalculateEntityCount();
            NativeArray<FleetAssessment> fightersFleet = new NativeArray<FleetAssessment>(teamsCount, Allocator.TempJob);
            NativeArray<FleetAssessment> workersFleet = new NativeArray<FleetAssessment>(teamsCount, Allocator.TempJob);
            NativeArray<FleetAssessment> tradersFleet = new NativeArray<FleetAssessment>(teamsCount, Allocator.TempJob);

            {
                JobHandle initialFleetCompositionsDep = state.Dependency;

                FighterFleetAssessmentJob fighterFleetJob = new FighterFleetAssessmentJob
                {
                    FleetCompositions = fightersFleet,
                };
                state.Dependency = JobHandle.CombineDependencies(state.Dependency, fighterFleetJob.Schedule(initialFleetCompositionsDep));

                WorkerFleetAssessmentJob workerFleetJob = new WorkerFleetAssessmentJob
                {
                    FleetCompositions = workersFleet,
                };
                state.Dependency = JobHandle.CombineDependencies(state.Dependency, workerFleetJob.Schedule(initialFleetCompositionsDep));

                TraderFleetAssessmentJob traderFleetJob = new TraderFleetAssessmentJob
                {
                    FleetCompositions = tradersFleet,
                };
                state.Dependency = JobHandle.CombineDependencies(state.Dependency, traderFleetJob.Schedule(initialFleetCompositionsDep));
            }

            {
                NativeArray<Entity> teamEntities = teamQuery.ToEntityArray(state.WorldUpdateAllocator);
                NativeArray<Entity> planetEntities = planetsQuery.ToEntityArray(state.WorldUpdateAllocator);
                NativeArray<Planet> planets = planetsQuery.ToComponentDataArray<Planet>(state.WorldUpdateAllocator);
                NativeArray<Team> planetTeams = planetsQuery.ToComponentDataArray<Team>(state.WorldUpdateAllocator);
                NativeArray<LocalTransform> planetTransforms = planetsQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);

                TeamAIJob teamAIJob = new TeamAIJob
                {
                    ShipsCollectionEntity = SystemAPI.GetSingletonEntity<ShipCollection>(),
                    BuildingsCollectionEntity = SystemAPI.GetSingletonEntity<BuildingCollection>(),

                    TeamManagerAILookup = SystemAPI.GetComponentLookup<TeamManagerAI>(false),
                    FighterActionsLookup = SystemAPI.GetBufferLookup<FighterAction>(false),
                    WorkerActionsLookup = SystemAPI.GetBufferLookup<WorkerAction>(false),
                    TraderActionsLookup = SystemAPI.GetBufferLookup<TraderAction>(false),
                    FactoryActionsLookup = SystemAPI.GetBufferLookup<FactoryAction>(false),
                    PlanetIntelLookup = SystemAPI.GetBufferLookup<PlanetIntel>(false),

                    MoonReferenceLookup = SystemAPI.GetBufferLookup<MoonReference>(true),
                    ShipCollectionBufferLookup = SystemAPI.GetBufferLookup<ShipCollection>(true),
                    BuildingCollectionBufferLookup = SystemAPI.GetBufferLookup<BuildingCollection>(true),
                    BuildingReferenceLookup = SystemAPI.GetComponentLookup<BuildingReference>(true),
                    PlanetLookup = SystemAPI.GetComponentLookup<Planet>(true),
                    TeamLookup = SystemAPI.GetComponentLookup<Team>(true),
                    ActorTypeLookup = SystemAPI.GetComponentLookup<ActorType>(true),
                    LocalTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                    PlanetNetworkLookup = SystemAPI.GetBufferLookup<PlanetNetwork>(true),
                    PlanetShipsAssessmentLookup = SystemAPI.GetBufferLookup<PlanetShipsAssessment>(true),

                    TeamEntities = teamEntities,
                    PlanetEntities = planetEntities,
                    Planets = planets,
                    PlanetTeams = planetTeams,
                    PlanetTransforms = planetTransforms,
                    FighterFleetAssessments = fightersFleet,
                    WorkerFleetAssessments = workersFleet,
                    TraderFleetAssessments = tradersFleet,
                };
                state.Dependency = teamAIJob.Schedule(aliveTeamsCount, 1, state.Dependency);

                teamEntities.Dispose(state.Dependency);
                planetEntities.Dispose(state.Dependency);
                planets.Dispose(state.Dependency);
                planetTeams.Dispose(state.Dependency);
                planetTransforms.Dispose(state.Dependency);
            }

            fightersFleet.Dispose(state.Dependency);
            workersFleet.Dispose(state.Dependency);
            tradersFleet.Dispose(state.Dependency);
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(Fighter))]
        public partial struct FighterFleetAssessmentJob : IJobEntity
        {
            public NativeArray<FleetAssessment> FleetCompositions;

            public void Execute(in Ship ship, in Team team)
            {
                FleetAssessment assessment = FleetCompositions[team.Index];
                assessment.Count++;
                assessment.Value += ship.ShipData.Value.Value;
                FleetCompositions[team.Index] = assessment;
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(Worker))]
        public partial struct WorkerFleetAssessmentJob : IJobEntity
        {
            public NativeArray<FleetAssessment> FleetCompositions;

            public void Execute(in Ship ship, in Team team)
            {
                FleetAssessment assessment = FleetCompositions[team.Index];
                assessment.Count++;
                assessment.Value += ship.ShipData.Value.Value;
                FleetCompositions[team.Index] = assessment;
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        [WithAll(typeof(Trader))]
        public partial struct TraderFleetAssessmentJob : IJobEntity
        {
            public NativeArray<FleetAssessment> FleetCompositions;

            public void Execute(in Ship ship, in Team team)
            {
                FleetAssessment assessment = FleetCompositions[team.Index];
                assessment.Count++;
                assessment.Value += ship.ShipData.Value.Value;
                FleetCompositions[team.Index] = assessment;
            }
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        public struct TeamAIJob : IJobParallelFor
        {
            public Entity ShipsCollectionEntity;
            public Entity BuildingsCollectionEntity;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<TeamManagerAI> TeamManagerAILookup;

            [NativeDisableParallelForRestriction]
            public BufferLookup<FighterAction> FighterActionsLookup;

            [NativeDisableParallelForRestriction]
            public BufferLookup<WorkerAction> WorkerActionsLookup;

            [NativeDisableParallelForRestriction]
            public BufferLookup<TraderAction> TraderActionsLookup;

            [NativeDisableParallelForRestriction]
            public BufferLookup<FactoryAction> FactoryActionsLookup;

            [NativeDisableParallelForRestriction]
            public BufferLookup<PlanetIntel> PlanetIntelLookup;

            [ReadOnly] public BufferLookup<MoonReference> MoonReferenceLookup;
            [ReadOnly] public BufferLookup<ShipCollection> ShipCollectionBufferLookup;
            [ReadOnly] public BufferLookup<BuildingCollection> BuildingCollectionBufferLookup;
            [ReadOnly] public ComponentLookup<BuildingReference> BuildingReferenceLookup;
            [ReadOnly] public ComponentLookup<Planet> PlanetLookup;
            [ReadOnly] public ComponentLookup<Team> TeamLookup;
            [ReadOnly] public ComponentLookup<ActorType> ActorTypeLookup;
            [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;

            [ReadOnly] public BufferLookup<PlanetNetwork> PlanetNetworkLookup;
            [ReadOnly] public BufferLookup<PlanetShipsAssessment> PlanetShipsAssessmentLookup;

            [ReadOnly] public NativeArray<Entity> TeamEntities;
            [ReadOnly] public NativeArray<Entity> PlanetEntities;
            [ReadOnly] public NativeArray<Planet> Planets;
            [ReadOnly] public NativeArray<Team> PlanetTeams;
            [ReadOnly] public NativeArray<LocalTransform> PlanetTransforms;
            [ReadOnly] public NativeArray<FleetAssessment> FighterFleetAssessments;
            [ReadOnly] public NativeArray<FleetAssessment> WorkerFleetAssessments;
            [ReadOnly] public NativeArray<FleetAssessment> TraderFleetAssessments;

            public void Execute(int indexOfTeamInTeamReferences)
            {
                Entity teamEntity = TeamEntities[indexOfTeamInTeamReferences];

                int teamIndex = TeamLookup[teamEntity].Index;
                TeamManagerAI teamManagerAI = TeamManagerAILookup[teamEntity];
                DynamicBuffer<FighterAction> fighterActions = FighterActionsLookup[teamEntity];
                DynamicBuffer<WorkerAction> workerActions = WorkerActionsLookup[teamEntity];
                DynamicBuffer<TraderAction> traderActions = TraderActionsLookup[teamEntity];
                DynamicBuffer<FactoryAction> factoryActions = FactoryActionsLookup[teamEntity];
                DynamicBuffer<PlanetIntel> planetIntels = PlanetIntelLookup[teamEntity];
                NativeList<Entity> teamPlanetEntities = new NativeList<Entity>(32, Allocator.Temp);

                if (teamManagerAI.Random.state == 0)
                {
                    teamManagerAI.Random = GameUtilities.GetDeterministicRandom(teamEntity.Index);
                }

                fighterActions.Clear();
                workerActions.Clear();
                traderActions.Clear();
                factoryActions.Clear();
                planetIntels.Clear();

                ComputeEmpireStatistics(teamIndex, teamPlanetEntities, ref teamManagerAI, ref planetIntels);

                if (teamManagerAI.EmpireStatistics.OwnedPlanetsCount <= 0)
                {
                    teamManagerAI.IsDefeated = true;
                }
                else
                {
                    DynamicBuffer<ShipCollection> shipsCollection = ShipCollectionBufferLookup[ShipsCollectionEntity];
                    DynamicBuffer<BuildingCollection> buildingsCollection =
                        BuildingCollectionBufferLookup[BuildingsCollectionEntity];
                    UnsafeList<float> tmpImportances = new UnsafeList<float>(32, Allocator.Temp);

                    AIProcessor fighterAIProcessor = new AIProcessor(128, Allocator.Temp);
                    AIProcessor workerAIProcessor = new AIProcessor(128, Allocator.Temp);
                    AIProcessor traderAIProcessor = new AIProcessor(128, Allocator.Temp);

                    HandleFactoryActions(ref teamManagerAI, in shipsCollection, ref factoryActions);

                    for (int i = 0; i < planetIntels.Length; i++)
                    {
                        PlanetIntel planetIntel = planetIntels[i];

                        CalculatePlanetStatistics(
                            in planetIntel,
                            in teamManagerAI,
                            out PlanetStatistics planetStatistics);

                        if (planetIntel.IsOwned == 1)
                        {
                            HandleFighterDefendAction(
                                ref fighterAIProcessor,
                                ref fighterActions,
                                in planetIntel,
                                in planetStatistics,
                                in teamManagerAI);

                            HandleWorkerBuildAction(
                                ref workerAIProcessor,
                                ref workerActions,
                                ref buildingsCollection,
                                in planetIntel,
                                in planetStatistics,
                                ref teamManagerAI,
                                ref tmpImportances);

                            HandleTraderTradeAction(
                                ref traderAIProcessor,
                                ref traderActions,
                                in planetIntel,
                                in planetStatistics,
                                in teamManagerAI);
                        }
                        else
                        {
                            HandleFighterAttackAction(
                                ref fighterAIProcessor,
                                ref fighterActions,
                                in planetIntel,
                                in planetStatistics,
                                in teamManagerAI);

                            HandleWorkerCaptureAction(
                                ref workerAIProcessor,
                                ref workerActions,
                                in planetIntel,
                                in planetStatistics,
                                in teamManagerAI);
                        }
                    }

                    fighterAIProcessor.ComputeFinalImportances();
                    workerAIProcessor.ComputeFinalImportances();
                    traderAIProcessor.ComputeFinalImportances();

                    {
                        for (int i = 0; i < fighterActions.Length; i++)
                        {
                            FighterAction c = fighterActions[i];
                            c.Importance = fighterAIProcessor.GetActionImportance(i);
                            fighterActions[i] = c;
                        }

                        for (int i = 0; i < workerActions.Length; i++)
                        {
                            WorkerAction c = workerActions[i];
                            c.Importance = workerAIProcessor.GetActionImportance(i);
                            workerActions[i] = c;
                        }

                        for (int i = 0; i < traderActions.Length; i++)
                        {
                            TraderAction c = traderActions[i];
                            c.ImportanceBias = traderAIProcessor.GetActionImportance(i);
                            traderActions[i] = c;
                        }
                    }
                }

                TeamManagerAILookup[teamEntity] = teamManagerAI;
            }

            private PlanetIntel GetPlanetIntel(
                Entity entity,
                int teamIndex,
                int planetTeamIndex,
                float3 position,
                float radius,
                float distance,
                in Planet planet)
            {
                if (PlanetShipsAssessmentLookup.TryGetBuffer(entity,
                        out DynamicBuffer<PlanetShipsAssessment> shipsAssessmentBuffer) &&
                    MoonReferenceLookup.TryGetBuffer(entity, out DynamicBuffer<MoonReference> moonReferencesBuffer))
                {
                    int alliedShips = 0;
                    int alliedFighters = 0;
                    int alliedWorkers = 0;
                    int alliedTraders = 0;

                    int enemyShips = 0;
                    int enemyFighters = 0;
                    int enemyWorkers = 0;
                    int enemyTraders = 0;

                    for (int i = 0; i < shipsAssessmentBuffer.Length; i++)
                    {
                        if (i == teamIndex)
                        {
                            alliedShips += shipsAssessmentBuffer[i].TotalCount;
                            alliedFighters += shipsAssessmentBuffer[i].FighterCount;
                            alliedWorkers += shipsAssessmentBuffer[i].WorkerCount;
                            alliedTraders += shipsAssessmentBuffer[i].TraderCount;
                        }
                        else
                        {
                            enemyShips += shipsAssessmentBuffer[i].TotalCount;
                            enemyFighters += shipsAssessmentBuffer[i].FighterCount;
                            enemyWorkers += shipsAssessmentBuffer[i].WorkerCount;
                            enemyTraders += shipsAssessmentBuffer[i].TraderCount;
                        }
                    }

                    Entity freeMoonEntity = Entity.Null;
                    int totalMoons = 0;
                    int freeMoons = 0;
                    int factoriesCount = 0;
                    int turretsCount = 0;
                    int researchesCount = 0;

                    for (int i = 0; i < moonReferencesBuffer.Length; i++)
                    {
                        Entity moonEntity = moonReferencesBuffer[i].Entity;
                        if (BuildingReferenceLookup.TryGetComponent(moonEntity, out BuildingReference buildingReference))
                        {
                            totalMoons++;

                            if (ActorTypeLookup.TryGetComponent(buildingReference.BuildingEntity, out ActorType actorType))
                            {
                                switch (actorType.Type)
                                {
                                    case ActorType.FactoryType:
                                        factoriesCount++;
                                        break;
                                    case ActorType.TurretType:
                                        turretsCount++;
                                        break;
                                    case ActorType.ResearchType:
                                        researchesCount++;
                                        break;
                                }
                            }
                            else
                            {
                                if (freeMoonEntity == Entity.Null)
                                {
                                    freeMoonEntity = moonEntity;
                                }

                                freeMoons++;
                            }
                        }
                    }

                    return new PlanetIntel
                    {
                        Entity = entity,
                        Position = position,
                        PlanetRadius = radius,
                        Distance = distance,
                        IsOwned = (planetTeamIndex == teamIndex) ? (byte)1 : (byte)0,

                        ResourceGenerationRate = planet.ResourceGenerationRate,
                        CurrentResourceStorage = planet.ResourceCurrentStorage,
                        MaxResourceStorage = planet.ResourceMaxStorage,
                        AlliedFighters = alliedFighters,
                        EnemyFighters = enemyFighters,
                        FreeMoonEntity = freeMoonEntity,
                        TotalMoonsCount = totalMoons,
                        FreeMoonsCount = freeMoons,

                        FactoriesCount = factoriesCount,
                        TurretsCount = turretsCount,
                        ResearchesCount = researchesCount,
                    };
                }

                return default;
            }

            private void ComputeEmpireStatistics(
                int teamIndex,
                NativeList<Entity> teamPlanetEntities,
                ref TeamManagerAI teamManagerAI,
                ref DynamicBuffer<PlanetIntel> planetIntels)
            {
                teamManagerAI.EmpireStatistics = default;

                teamManagerAI.EmpireStatistics.FightersAssessment = FighterFleetAssessments[teamIndex];
                teamManagerAI.EmpireStatistics.WorkersAssessment = WorkerFleetAssessments[teamIndex];
                teamManagerAI.EmpireStatistics.TradersAssessment = TraderFleetAssessments[teamIndex];

                teamManagerAI.EmpireStatistics.TotalNonFighterShipsCount =
                    teamManagerAI.EmpireStatistics.WorkersAssessment.Count +
                    teamManagerAI.EmpireStatistics.TradersAssessment.Count;
                teamManagerAI.EmpireStatistics.TotalShipsCount =
                    teamManagerAI.EmpireStatistics.TotalNonFighterShipsCount +
                    teamManagerAI.EmpireStatistics.FightersAssessment.Count;

                for (int p = 0; p < PlanetEntities.Length; p++)
                {
                    if (PlanetTeams[p].Index == teamIndex)
                    {
                        LocalTransform planetLocalTransform = PlanetTransforms[p];
                        Entity planetEntity = PlanetEntities[p];
                        Planet planet = Planets[p];

                        PlanetIntel intel = GetPlanetIntel(planetEntity, teamIndex, teamIndex,
                            planetLocalTransform.Position, planetLocalTransform.Scale * 0.5f, 0f, in planet);
                        planetIntels.Add(intel);

                        teamPlanetEntities.Add(planetEntity);
                    }
                }

                teamManagerAI.EmpireStatistics.OwnedPlanetsCount = teamPlanetEntities.Length;

                for (int e = 0; e < teamPlanetEntities.Length; e++)
                {
                    DynamicBuffer<PlanetNetwork> planetNetworkBuffer = PlanetNetworkLookup[teamPlanetEntities[e]];

                    for (int i = 0; i < planetNetworkBuffer.Length; i++)
                    {
                        PlanetNetwork connectedPlanet = planetNetworkBuffer[i];
                        Entity planetEntity = connectedPlanet.Entity;

                        bool isAlreadyAdded = false;
                        for (int j = 0; j < planetIntels.Length; j++)
                        {
                            if (planetIntels[j].Entity == connectedPlanet.Entity)
                            {
                                isAlreadyAdded = true;
                                break;
                            }
                        }

                        if (!isAlreadyAdded)
                        {
                            float3 planetPosition = connectedPlanet.Position;
                            float planetDistance = connectedPlanet.Distance;
                            Planet planet = PlanetLookup[planetEntity];
                            Team planetTeam = TeamLookup[planetEntity];

                            PlanetIntel intel = GetPlanetIntel(planetEntity, teamIndex, planetTeam.Index, planetPosition,
                                connectedPlanet.Radius, planetDistance,
                                in planet);
                            planetIntels.Add(intel);
                        }
                    }
                }

                teamManagerAI.EmpireStatistics.TotalPlanetsCount = planetIntels.Length;

                for (int i = 0; i < planetIntels.Length; i++)
                {
                    PlanetIntel planetIntel = planetIntels[i];
                    teamManagerAI.EmpireStatistics.MaxAlliedFightersCountForPlanet = math.max(teamManagerAI.EmpireStatistics.MaxAlliedFightersCountForPlanet, planetIntel.AlliedFighters);
                    teamManagerAI.EmpireStatistics.MaxEnemyFightersCountForPlanet = math.max(teamManagerAI.EmpireStatistics.MaxEnemyFightersCountForPlanet, planetIntel.EnemyFighters);
                    teamManagerAI.EmpireStatistics.FarthestDistance = math.max(teamManagerAI.EmpireStatistics.FarthestDistance, planetIntel.Distance);
                    teamManagerAI.EmpireStatistics.MaxResourceStorageRatio = math.max(teamManagerAI.EmpireStatistics.MaxResourceStorageRatio,
                        planetIntel.CurrentResourceStorage / planetIntel.MaxResourceStorage);
                    teamManagerAI.EmpireStatistics.MaxResourceGenerationRate = math.max(teamManagerAI.EmpireStatistics.MaxResourceGenerationRate, planetIntel.ResourceGenerationRate);
                }
            }

            private void HandleFactoryActions(
                ref TeamManagerAI teamManagerAI,
                in DynamicBuffer<ShipCollection> shipsCollection,
                ref DynamicBuffer<FactoryAction> factoryActions)
            {
                float totalFighterProbabilities = 0f;
                float totalWorkerProbabilities = 0f;
                float totalTraderProbabilities = 0f;
                for (int i = 0; i < shipsCollection.Length; i++)
                {
                    ShipCollection shipInfo = shipsCollection[i];
                    ActorType shipActorType = ActorTypeLookup[shipInfo.PrefabEntity];
                    float shipBuildProbability = shipInfo.ShipData.Value.BuildProbabilityForShipType;

                    switch (shipActorType.Type)
                    {
                        case ActorType.FighterType:
                            totalFighterProbabilities += shipBuildProbability;
                            break;
                        case ActorType.WorkerType:
                            totalWorkerProbabilities += shipBuildProbability;
                            break;
                        case ActorType.TraderType:
                            totalTraderProbabilities += shipBuildProbability;
                            break;
                    }
                }

                for (int i = 0; i < shipsCollection.Length; i++)
                {
                    ShipCollection shipInfo = shipsCollection[i];
                    ActorType shipActorType = ActorTypeLookup[shipInfo.PrefabEntity];
                    ref ShipData shipData = ref shipInfo.ShipData.Value;
                    float shipBuildProbability = shipData.BuildProbabilityForShipType;

                    teamManagerAI.FighterBias = teamManagerAI.MaxShipProductionBias;
                    if (teamManagerAI.EmpireStatistics.FightersAssessment.Value > 0f)
                    {
                        teamManagerAI.FighterBias =
                            (teamManagerAI.DesiredFightersPerOtherShip * (float)teamManagerAI.EmpireStatistics.TotalNonFighterShipsCount) /
                            (float)teamManagerAI.EmpireStatistics.FightersAssessment.Count;
                    }

                    teamManagerAI.WorkerBias = teamManagerAI.MaxShipProductionBias;
                    if (teamManagerAI.EmpireStatistics.WorkersAssessment.Value > 0f)
                    {
                        teamManagerAI.WorkerBias = (teamManagerAI.DesiredWorkerValuePerPlanet * teamManagerAI.EmpireStatistics.TotalPlanetsCount) /
                                                   teamManagerAI.EmpireStatistics.WorkersAssessment.Value;
                    }

                    teamManagerAI.TraderBias = teamManagerAI.MaxShipProductionBias;
                    if (teamManagerAI.EmpireStatistics.TradersAssessment.Value > 0f)
                    {
                        int planetCount = teamManagerAI.EmpireStatistics.OwnedPlanetsCount;
                        if (planetCount >= 2)
                        {
                            teamManagerAI.TraderBias = (teamManagerAI.DesiredTraderValuePerOwnedPlanet * planetCount) /
                                                       teamManagerAI.EmpireStatistics.TradersAssessment.Value;
                        }
                        else
                        {
                            teamManagerAI.TraderBias = 0;
                        }
                    }

                    float finalProbability = 0f;
                    switch (shipActorType.Type)
                    {
                        case ActorType.FighterType:
                            {
                                float probabilityInType = shipBuildProbability / totalFighterProbabilities;
                                finalProbability = teamManagerAI.FighterBias * probabilityInType;
                                break;
                            }
                        case ActorType.WorkerType:
                            {
                                float probabilityInType = shipBuildProbability / totalWorkerProbabilities;
                                finalProbability = teamManagerAI.WorkerBias * probabilityInType;
                                break;
                            }
                        case ActorType.TraderType:
                            {
                                float probabilityInType = shipBuildProbability / totalTraderProbabilities;
                                finalProbability = teamManagerAI.TraderBias * probabilityInType;
                                break;
                            }
                    }

                    factoryActions.Add(new FactoryAction
                    {
                        PrefabEntity = shipInfo.PrefabEntity,
                        Importance = finalProbability,
                        ResourceCost = shipData.ResourcesCost,
                        BuildTime = shipData.BuildTime,
                    });
                }
            }

            private Entity PickRandomBuildingToBuild(
                Entity planetEntity,
                ref UnsafeList<float> tmpImportances,
                in DynamicBuffer<BuildingCollection> buildingsCollectionBuffer,
                ref Random random)
            {
                bool planetHasAFactory = false;
                if (MoonReferenceLookup.TryGetBuffer(planetEntity, out DynamicBuffer<MoonReference> moonReferences))
                {
                    for (int i = 0; i < moonReferences.Length; i++)
                    {
                        Entity planetMoonEntity = moonReferences[i].Entity;
                        if (BuildingReferenceLookup.TryGetComponent(planetMoonEntity,
                                out BuildingReference planetMoonBuildingReference))
                        {
                            if (ActorTypeLookup.TryGetComponent(planetMoonBuildingReference.BuildingEntity,
                                    out ActorType actorType))
                            {
                                if (actorType.Type == ActorType.FactoryType)
                                {
                                    planetHasAFactory = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                Entity buildingPrefab = default;
                {
                    tmpImportances.Clear();
                    float cummulativeImportances = 0f;

                    for (int i = 0; i < buildingsCollectionBuffer.Length; i++)
                    {
                        BuildingCollection buildingInfo = buildingsCollectionBuffer[i];
                        ref BuildingData buildingData = ref buildingInfo.BuildingData.Value;
                        cummulativeImportances += buildingData.BuildProbability;
                        tmpImportances.Add(buildingData.BuildProbability);
                    }

                    int randomIndex =
                        GameUtilities.GetWeightedRandomIndex(cummulativeImportances, in tmpImportances, ref random);
                    if (randomIndex >= 0)
                    {
                        buildingPrefab = buildingsCollectionBuffer[randomIndex].PrefabEntity;
                    }
                }

                if (!planetHasAFactory)
                {
                    for (int i = 0; i < buildingsCollectionBuffer.Length; i++)
                    {
                        Entity tmpPrefab = buildingsCollectionBuffer[i].PrefabEntity;
                        if (ActorTypeLookup.TryGetComponent(tmpPrefab, out ActorType actorType))
                        {
                            if (actorType.Type == ActorType.FactoryType)
                            {
                                buildingPrefab = tmpPrefab;
                                break;
                            }
                        }
                    }
                }

                return buildingPrefab;
            }

            private void CalculatePlanetStatistics(
                in PlanetIntel planetIntel,
                in TeamManagerAI teamManagerAI,
                out PlanetStatistics planetStatistics)
            {
                planetStatistics = default;

                if (teamManagerAI.EmpireStatistics.MaxEnemyFightersCountForPlanet > 0)
                {
                    planetStatistics.ThreatLevel = math.saturate(planetIntel.EnemyFighters / (float)teamManagerAI.EmpireStatistics.MaxEnemyFightersCountForPlanet);
                }

                if (teamManagerAI.EmpireStatistics.MaxAlliedFightersCountForPlanet > 0)
                {
                    planetStatistics.SafetyLevel = math.saturate(planetIntel.AlliedFighters / (float)teamManagerAI.EmpireStatistics.MaxAlliedFightersCountForPlanet);
                }

                if (teamManagerAI.EmpireStatistics.FarthestDistance > 0f)
                {
                    planetStatistics.DistanceFromOwnedPlanetsScore =
                        math.saturate(1f - math.saturate(planetIntel.Distance / teamManagerAI.EmpireStatistics.FarthestDistance));
                }

                if (math.lengthsq(teamManagerAI.EmpireStatistics.MaxResourceStorageRatio) > 0f)
                {
                    planetStatistics.ResourceStorageRatioPercentile =
                        math.saturate((planetIntel.CurrentResourceStorage / planetIntel.MaxResourceStorage) /
                                      teamManagerAI.EmpireStatistics.MaxResourceStorageRatio);
                }

                float maxResourcesGenerationScore = math.csum(teamManagerAI.EmpireStatistics.MaxResourceGenerationRate);
                if (maxResourcesGenerationScore > 0f)
                {
                    planetStatistics.ResourceGenerationScore =
                        math.saturate(1f - math.saturate(math.csum(planetIntel.ResourceGenerationRate) / maxResourcesGenerationScore));
                }
            }

            private void HandleFighterDefendAction(
                ref AIProcessor fighterAIProcessor,
                ref DynamicBuffer<FighterAction> fighterActions,
                in PlanetIntel planetIntel,
                in PlanetStatistics planetStatistics,
                in TeamManagerAI teamManagerAI)
            {
                AIAction fighterAIAction = AIAction.New();

                fighterAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.ThreatLevel,
                    teamManagerAI.FighterDefendThreatLevelConsiderationClamp));
                fighterAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.ResourceGenerationScore,
                    teamManagerAI.FighterDefendResourceScoreConsiderationClamp));
                fighterAIAction.ApplyConsideration(teamManagerAI.FighterDefendPlanetConsideration);

                if (fighterAIAction.HasConsiderationsAndImportance())
                {
                    GameUtilities.AddAction(ref fighterActions, ref fighterAIProcessor, new FighterAction
                    {
                        Entity = planetIntel.Entity,
                        Position = planetIntel.Position,
                        Radius = planetIntel.PlanetRadius,
                    }, fighterAIAction);
                }
            }

            private void HandleWorkerBuildAction(
                ref AIProcessor workerAIProcessor,
                ref DynamicBuffer<WorkerAction> workerActions,
                ref DynamicBuffer<BuildingCollection> buildingsCollection,
                in PlanetIntel planetIntel,
                in PlanetStatistics planetStatistics,
                ref TeamManagerAI teamManagerAI,
                ref UnsafeList<float> tmpImportances)
            {
                AIAction workerAIAction = AIAction.New();

                if (planetIntel.FreeMoonsCount > 0 && planetIntel.FreeMoonEntity != Entity.Null)
                {
                    Entity buildingPrefab = PickRandomBuildingToBuild(planetIntel.Entity,
                        ref tmpImportances, in buildingsCollection,
                        ref teamManagerAI.Random);

                    if (buildingPrefab != Entity.Null)
                    {
                        LocalTransform moonTransform = LocalTransformLookup[planetIntel.FreeMoonEntity];
                        float3 freeMoonPosition = moonTransform.Position;
                        float freeMoonRadius = moonTransform.Scale * 0.5f;
                        float buildingOccupancyBias =
                            math.saturate((float)planetIntel.FreeMoonsCount / (float)planetIntel.TotalMoonsCount);

                        workerAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.SafetyLevel,
                            teamManagerAI.WorkerBuildSafetyLevelConsiderationClamp));
                        workerAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.ResourceGenerationScore,
                            teamManagerAI.WorkerBuildResourceScoreConsiderationClamp));
                        workerAIAction.ApplyConsideration(buildingOccupancyBias);
                        workerAIAction.ApplyConsideration(teamManagerAI.WorkerBuildConsideration);

                        if (workerAIAction.HasConsiderationsAndImportance())
                        {
                            GameUtilities.AddAction(ref workerActions, ref workerAIProcessor, new WorkerAction
                            {
                                Type = (byte)1,
                                Entity = planetIntel.FreeMoonEntity,
                                Position = freeMoonPosition,
                                PlanetRadius = freeMoonRadius,
                                BuildingPrefab = buildingPrefab,
                            }, workerAIAction);
                        }
                    }
                }
            }

            private void HandleTraderTradeAction(
                ref AIProcessor traderAIProcessor,
                ref DynamicBuffer<TraderAction> traderActions,
                in PlanetIntel planetIntel,
                in PlanetStatistics planetStatistics,
                in TeamManagerAI teamManagerAI)
            {
                AIAction traderAIAction = AIAction.New();

                traderAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.SafetyLevel,
                    teamManagerAI.TraderSafetyLevelConsiderationClamp));

                if (traderAIAction.HasConsiderationsAndImportance())
                {
                    GameUtilities.AddAction(ref traderActions, ref traderAIProcessor, new TraderAction
                    {
                        Entity = planetIntel.Entity,
                        Position = planetIntel.Position,
                        ResourceStorageRatioPercentile = planetStatistics.ResourceStorageRatioPercentile,
                        Radius = planetIntel.PlanetRadius,
                    }, traderAIAction);
                }
            }

            private void HandleFighterAttackAction(
                ref AIProcessor fighterAIProcessor,
                ref DynamicBuffer<FighterAction> fighterActions,
                in PlanetIntel planetIntel,
                in PlanetStatistics planetStatistics,
                in TeamManagerAI teamManagerAI)
            {
                AIAction fighterAIAction = AIAction.New();

                fighterAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.ThreatLevel,
                    teamManagerAI.FighterAttackThreatLevelConsiderationClamp));
                fighterAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.ResourceGenerationScore,
                    teamManagerAI.FighterAttackResourceScoreConsiderationClamp));
                fighterAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.DistanceFromOwnedPlanetsScore,
                    teamManagerAI.FighterAttackDistanceFromOwnedPlanetsConsiderationClamp));
                fighterAIAction.ApplyConsideration(teamManagerAI.FighterAttackPlanetConsideration);

                if (fighterAIAction.HasConsiderationsAndImportance())
                {
                    GameUtilities.AddAction(ref fighterActions, ref fighterAIProcessor, new FighterAction
                    {
                        Entity = planetIntel.Entity,
                        Position = planetIntel.Position,
                        Radius = planetIntel.PlanetRadius,
                    }, fighterAIAction);
                }
            }

            private void HandleWorkerCaptureAction(
                ref AIProcessor workerAIProcessor,
                ref DynamicBuffer<WorkerAction> workerActions,
                in PlanetIntel planetIntel,
                in PlanetStatistics planetStatistics,
                in TeamManagerAI teamManagerAI)
            {
                AIAction workerAIAction = AIAction.New();

                workerAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.SafetyLevel,
                    teamManagerAI.WorkerCaptureSafetyLevelConsiderationClamp));
                workerAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.ResourceGenerationScore,
                    teamManagerAI.WorkerCaptureResourceScoreConsiderationClamp));
                workerAIAction.ApplyConsideration(MathUtilities.Clamp(planetStatistics.DistanceFromOwnedPlanetsScore,
                    teamManagerAI.WorkerCaptureDistanceFromOwnedPlanetsConsiderationClamp));
                workerAIAction.ApplyConsideration(teamManagerAI.WorkerCapturePlanetConsideration);

                if (workerAIAction.HasConsiderationsAndImportance())
                {
                    GameUtilities.AddAction(ref workerActions, ref workerAIProcessor, new WorkerAction
                    {
                        Type = (byte)0,
                        Entity = planetIntel.Entity,
                        Position = planetIntel.Position,
                        PlanetRadius = planetIntel.PlanetRadius,
                    }, workerAIAction);
                }
            }
        }

        public partial struct TeamDefeatedJob : IJobEntity
        {
            public EntityCommandBuffer ECB;
            [ReadOnly]
            public NativeArray<Entity> UnitEntities;
            [ReadOnly]
            public NativeArray<Team> UnitTeams;

            public ComponentLookup<Health> HealthLookup;

            void Execute(Entity entity, in Team team, in TeamManagerAI teamManagerAI)
            {
                if (teamManagerAI.IsDefeated)
                {
                    ECB.DestroyEntity(entity);

                    for (int i = 0; i < UnitTeams.Length; i++)
                    {
                        if (UnitTeams[i].Index == team.Index)
                        {
                            Entity unitEntity = UnitEntities[i];
                            if (HealthLookup.TryGetComponent(unitEntity, out Health health))
                            {
                                health.CurrentHealth = -1000;
                                HealthLookup[unitEntity] = health;
                            }
                        }
                    }
                }
            }
        }
    }
}
