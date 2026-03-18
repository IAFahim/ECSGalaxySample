using Unity.Core;
using Unity.Entities;
using UnityEngine;

namespace Galaxy
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(SimulationTimeScaleSystem))]
    public partial struct SimulationRateSystem : ISystem
    {
        private bool _hadFirstTimeInit;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationRate>();
            SimulationSystemGroup simulationSystemGroup = state.World.GetExistingSystemManaged<SimulationSystemGroup>();

            GameObject configGO = Resources.Load<GameObject>("Config");
            if (configGO != null)
            {
                ConfigAuthoring settings = configGO.GetComponent<ConfigAuthoring>();
                if (settings != null && settings.UseFixedSimulationDeltaTime)
                {
                    simulationSystemGroup.RateManager = new RateUtils.FixedRateCatchUpManager(settings.FixedDeltaTime);
                }
                else
                {
                    simulationSystemGroup.RateManager = null;
                }   
            }

            simulationSystemGroup.World.MaximumDeltaTime = float.MaxValue;
        }

        public void OnUpdate(ref SystemState state)
        {
            ref SimulationRate simRate = ref SystemAPI.GetSingletonRW<SimulationRate>().ValueRW;
            SimulationSystemGroup simulationSystemGroup = state.World.GetExistingSystemManaged<SimulationSystemGroup>();
            
            if (!_hadFirstTimeInit)
            {
                const string _fixedRateArg = "-fixedRate:";

                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg.Contains(_fixedRateArg))
                    {
                        string rate = arg.Substring(_fixedRateArg.Length);
                        if (int.TryParse(rate, out int rateInt))
                        {
                            if (rateInt > 0)
                            {
                                simRate.UseFixedRate = true;
                                simRate.FixedTimeStep = 1f / (float)rateInt;
                            }
                            else
                            {
                                simRate.UseFixedRate = false;
                            }
                            break;
                        }
                    }
                }

                _hadFirstTimeInit = true;
            }

            if (simRate.Update)
            {
                
                if (simRate.UseFixedRate)
                {
                    simulationSystemGroup.RateManager = new RateUtils.FixedRateCatchUpManager(simRate.FixedTimeStep);
                }
                else
                {
                    simulationSystemGroup.RateManager = null;
                }
                simRate.Update = false;
            }
        }
    }
    
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(GameInitializeSystem))]
    public partial struct SimulationTimeScaleSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationRate>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ref SimulationRate simRate = ref SystemAPI.GetSingletonRW<SimulationRate>().ValueRW;
            
            state.World.SetTime(new TimeData(
                SystemAPI.Time.ElapsedTime,
                SystemAPI.Time.DeltaTime * simRate.TimeScale));
        }
    }
}