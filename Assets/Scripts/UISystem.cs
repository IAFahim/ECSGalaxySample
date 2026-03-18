using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public partial struct UISystem : ISystem
{
    private bool m_SettingInitialized;
    private bool m_AutoSimulateInitialized;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<Config>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Config config = SystemAPI.GetSingleton<Config>();

        if (!m_SettingInitialized)
        {
            UIEvents.InitializeUISettings?.Invoke();
            m_SettingInitialized = true;
        }

        if (!m_AutoSimulateInitialized && config.AutoInitializeGame && SystemAPI.HasSingleton<GameIsSimulating>())
        {
            UIEvents.SimulateGame?.Invoke();
            m_AutoSimulateInitialized = true;
        }
    }
}