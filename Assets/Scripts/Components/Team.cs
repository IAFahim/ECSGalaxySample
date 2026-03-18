using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Galaxy
{
    public struct TeamManager : IComponentData
    {
        public FixedString128Bytes Name;
        public float4 Color;
        public float4 LaserColor;
        public float3 ThrusterColor;
        public float3 LaserSparksColor;
    }

    public struct TeamManagerAI : IComponentData
    {
        public float FighterAttackPlanetConsideration;
        public float2 FighterAttackThreatLevelConsiderationClamp;
        public float2 FighterAttackResourceScoreConsiderationClamp;
        public float2 FighterAttackDistanceFromOwnedPlanetsConsiderationClamp;
        
        public float FighterDefendPlanetConsideration;
        public float2 FighterDefendThreatLevelConsiderationClamp;
        public float2 FighterDefendResourceScoreConsiderationClamp;

        public float WorkerCapturePlanetConsideration;
        public float2 WorkerCaptureSafetyLevelConsiderationClamp;
        public float2 WorkerCaptureResourceScoreConsiderationClamp;
        public float2 WorkerCaptureDistanceFromOwnedPlanetsConsiderationClamp;
        
        public float WorkerBuildConsideration;
        public float2 WorkerBuildSafetyLevelConsiderationClamp;
        public float2 WorkerBuildResourceScoreConsiderationClamp;

        public float2 TraderSafetyLevelConsiderationClamp;

        public float MaxShipProductionBias;
        public float DesiredFightersPerOtherShip;
        public float DesiredWorkerValuePerPlanet;
        public float DesiredTraderValuePerOwnedPlanet;

        public float FighterBias;
        public float WorkerBias;
        public float TraderBias;

        public EmpireStatistics EmpireStatistics;
        
        public Random Random;
        public bool IsDefeated;
    }

    public struct Team : IComponentData 
    {
        public int Index;
        public Entity ManagerEntity;

        public const int NeutralTeam = -1;
    }

    public static class TeamExtensions
    {
        public static bool IsNonNeutral(in this Team team)
        {
            return team.Index >= 0;
        }
    }

    public struct ApplyTeam : IComponentData, IEnableableComponent
    { }

    [InternalBufferCapacity(0)]
    public struct PlanetIntel : IBufferElementData
    {
        public Entity Entity;
        public float3 Position;
        public float PlanetRadius;
        public float Distance;
        public byte IsOwned;
        
        public float3 ResourceGenerationRate;
        public float3 CurrentResourceStorage;
        public float3 MaxResourceStorage;

        public int AlliedFighters;

        public int EnemyFighters;

        public Entity FreeMoonEntity;
        public int TotalMoonsCount;
        public int FreeMoonsCount;

        public int FactoriesCount;
        public int TurretsCount;
        public int ResearchesCount;

        public int BuildingsCount()
        {
            return FactoriesCount + TurretsCount + ResearchesCount;
        }
    }

    [InternalBufferCapacity(0)]
    public struct FighterAction : IBufferElementData
    {
        public Entity Entity;
        public float3 Position;
        public float Radius;
        public float Importance;
    }

    [InternalBufferCapacity(0)]
    public struct WorkerAction : IBufferElementData
    {
        public byte Type;
        public Entity BuildingPrefab;
        public Entity Entity;
        public float3 Position;
        public float Importance;
        public float PlanetRadius;
    }

    [InternalBufferCapacity(0)]
    public struct TraderAction : IBufferElementData
    {
        public Entity Entity;
        public float3 Position;
        public float3 ResourceStorageRatioPercentile;
        public float Radius;
        public float ImportanceBias;
    }

    [InternalBufferCapacity(0)]
    public struct FactoryAction : IBufferElementData
    {
        public Entity PrefabEntity;
        public float Importance;
        public float3 ResourceCost;
        public float BuildTime;
    }

    public struct TeamManagerReference : IBufferElementData
    {
        public Entity Entity;
    }
}
