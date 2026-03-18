using ECSUITK.Data;

namespace ECSUITK.Logic
{
    public static class ScoreIncrementExtensions
    {
        public static void ApplyIncrement(this ref Score score, ScoreIncrementRequest request)
        {
            score.Value += request.Amount;
        }
    }
}
