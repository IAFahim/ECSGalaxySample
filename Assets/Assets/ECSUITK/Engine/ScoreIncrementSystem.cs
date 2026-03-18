using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using ECSUITK.Data;
using ECSUITK.Logic;

namespace ECSUITK.Engine
{
    [BurstCompile]
    public partial struct ScoreIncrementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var scoreEntity = SystemAPI.GetSingletonEntity<Score>();
            var score = SystemAPI.GetComponentRW<Score>(scoreEntity);
            var increment = SystemAPI.GetComponent<ScoreIncrementRequest>(scoreEntity);
            score.ValueRW.Value += increment.Amount;
        }
    }
}
