// using NUnit.Framework;
// using Unity.Entities;
// using ECSUITK.Data;
// using ECSUITK.Engine;
//
// namespace ECSUITK.Tests
// {
//     public class ScoreIncrementSystemTests
//     {
//         private World _world;
//         private EntityManager _entityManager;
//
//         [SetUp]
//         public void Setup()
//         {
//             _world = new World("TestWorld");
//             _entityManager = _world.EntityManager;
//         }
//
//         [TearDown]
//         public void Teardown()
//         {
//             _world.Dispose();
//         }[Test]
//         public void IncrementSystem_AddsToScore_AndConsumesRequest()
//         {
//             var scoreEntity = _entityManager.CreateEntity();
//             _entityManager.AddComponentData(scoreEntity, new Score { Value = 10 });
//
//             var requestEntity = _entityManager.CreateEntity();
//             _entityManager.AddComponentData(requestEntity, new ScoreIncrementRequest { Amount = 5 });
//
//             var systemHandle = _world.GetOrCreateSystem<ScoreIncrementSystem>();
//             _world.Unmanaged.UpdateSystem(systemHandle);
//
//             var score = _entityManager.GetComponentData<Score>(scoreEntity);
//             Assert.AreEqual(15, score.Value);
//
//             var requestExists = _entityManager.Exists(requestEntity);
//             Assert.IsFalse(requestExists);
//         }
//     }
// }
