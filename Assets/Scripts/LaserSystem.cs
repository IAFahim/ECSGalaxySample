using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Galaxy
{
    [BurstCompile]
    [UpdateAfter(typeof(BeginSimulationMainThreadGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct LaserSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameIsSimulating>();
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        public void OnUpdate(ref SystemState state)
        {
            LaserJob job = new LaserJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ECB = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
            };
            job.ScheduleParallel();
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]
        public partial struct LaserJob : IJobEntity, IJobEntityChunkBeginEnd
        {
            public float DeltaTime;
            public EntityCommandBuffer.ParallelWriter ECB;

            private int _chunkIndex;

            private void Execute(Entity entity, ref Laser laser, ref PostTransformMatrix postTransformMatrix)
            {
                laser.LifetimeCounter -= DeltaTime;

                if (laser.HasExistedOneFrame == 1)
                {
                    float lifetimeRatio = math.saturate(laser.LifetimeCounter / laser.MaxLifetime);
                    float originalScaleZ = postTransformMatrix.Value.Scale().z;
                    postTransformMatrix.Value = float4x4.Scale(lifetimeRatio, lifetimeRatio, originalScaleZ);

                    if (laser.LifetimeCounter <= 0f)
                    {
                        ECB.DestroyEntity(_chunkIndex, entity);
                    }
                }

                laser.HasExistedOneFrame = 1;
            }

            public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                _chunkIndex = unfilteredChunkIndex;
                return true;
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask,
                bool chunkWasExecuted)
            {
            }
        }
    }
}
