using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Galaxy
{
    [BurstCompile]
    [UpdateInGroup(typeof(BuildSpatialDatabaseGroup), OrderFirst = true)]
    public unsafe partial struct ClearSpatialDatabaseSystem : ISystem
    {
        private EntityQuery _spatialDatabasesQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameIsSimulating>();
            state.RequireForUpdate<Config>();
            _spatialDatabasesQuery = SystemAPI.QueryBuilder().WithAll<SpatialDatabase, SpatialDatabaseCell, SpatialDatabaseElement>().Build();
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        public void OnUpdate(ref SystemState state)
        {
            if (_spatialDatabasesQuery.CalculateEntityCount() > 0)
            {
                BufferLookup<SpatialDatabaseCell> cellsBufferLookup = SystemAPI.GetBufferLookup<SpatialDatabaseCell>(false);
                BufferLookup<SpatialDatabaseElement> elementsBufferLookup = SystemAPI.GetBufferLookup<SpatialDatabaseElement>(false);            
                NativeArray<Entity> spatialDatabaseEntities = _spatialDatabasesQuery.ToEntityArray(Allocator.Temp);

                JobHandle initialDep = state.Dependency;

                for (int i = 0; i < spatialDatabaseEntities.Length; i++)
                {
                    ClearSpatialDatabaseJob clearJob = new ClearSpatialDatabaseJob
                    {
                        Entity = spatialDatabaseEntities[i],
                        CellsBufferLookup = cellsBufferLookup,
                        ElementsBufferLookup = elementsBufferLookup,
                    };
                    state.Dependency = JobHandle.CombineDependencies(state.Dependency, clearJob.Schedule(initialDep));
                }

                spatialDatabaseEntities.Dispose();
            }
        }
        
        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        public struct ClearSpatialDatabaseJob : IJob
        {
            public Entity Entity;
            public BufferLookup<SpatialDatabaseCell> CellsBufferLookup;
            public BufferLookup<SpatialDatabaseElement> ElementsBufferLookup;
            
            public void Execute()
            {
                if (CellsBufferLookup.TryGetBuffer(Entity, out DynamicBuffer<SpatialDatabaseCell> cellsBuffer) &&
                    ElementsBufferLookup.TryGetBuffer(Entity, out DynamicBuffer<SpatialDatabaseElement> elementsBuffer))
                {
                    SpatialDatabase.ClearAndResize(ref cellsBuffer, ref elementsBuffer);
                }
            }
        }
    }
}
