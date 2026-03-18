using Unity.Entities;

namespace ECSUITK.Data
{
    public struct ScoreIncrementRequest : IComponentData
    {
        public int Amount;
    }
}
