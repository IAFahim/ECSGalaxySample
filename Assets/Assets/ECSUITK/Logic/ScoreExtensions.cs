using ECSUITK.Data;

namespace ECSUITK.Logic
{
    public static class ScoreExtensions
    {
        public static bool HasChanged(this Score score, int previousValue)
        {
            return score.Value != previousValue;
        }
    }
}
