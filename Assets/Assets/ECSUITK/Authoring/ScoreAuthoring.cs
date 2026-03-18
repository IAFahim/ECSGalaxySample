using ECSUITK.Data;
using Unity.Entities;
using UnityEngine;

namespace Assets.ECSUITK.Authoring
{
    public class ScoreAuthoring : MonoBehaviour
    {
        public int score;
        public int incrementRequest = 1;

        private class ScoreAuthoringBaker : Baker<ScoreAuthoring>
        {
            public override void Bake(ScoreAuthoring authoring)
            {
                var entity = GetEntity(authoring.gameObject, TransformUsageFlags.None);
                AddComponent(entity, new Score()
                {
                    Value = authoring.score
                });
                AddComponent(entity, new ScoreIncrementRequest()
                {
                    Amount = authoring.incrementRequest
                });
            }
        }
    }
}