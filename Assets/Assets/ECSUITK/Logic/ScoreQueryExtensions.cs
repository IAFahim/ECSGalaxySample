using Unity.Entities;
using ECSUITK.Data;

namespace ECSUITK.Logic
{
    public static class ScoreQueryExtensions
    {
        public static EntityQuery CreateScoreQuery(this EntityManager entityManager)
        {
            return entityManager.CreateEntityQuery(typeof(Score));
        }

        public static bool TryGetScore(this EntityQuery query, out Score score)
        {
            if (!query.IsEmpty)
            {
                score = query.GetSingleton<Score>();
                return true;
            }

            score = default;
            return false;
        }
    }
}
