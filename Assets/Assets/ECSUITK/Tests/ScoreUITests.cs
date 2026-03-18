// using NUnit.Framework;
// using Unity.Entities;
// using UnityEngine.UIElements;
// using ECSUITK.Data;
// using ECSUITK.Logic;
//
// namespace ECSUITK.Tests
// {
//     public class ScoreUITests
//     {
//         [Test]
//         public void ApplyScore_UpdatesLabelText()
//         {
//             var label = new Label();
//             var score = new Score { Value = 42 };
//
//             label.ApplyScore(score);
//
//             Assert.AreEqual("42", label.text);
//         }
//
//         [Test]
//         public void HasChanged_ReturnsTrue_WhenValuesDiffer()
//         {
//             var score = new Score { Value = 10 };
//             
//             var result = score.HasChanged(5);
//
//             Assert.IsTrue(result);
//         }
//
//         [Test]
//         public void HasChanged_ReturnsFalse_WhenValuesAreIdentical()
//         {
//             var score = new Score { Value = 10 };
//             
//             var result = score.HasChanged(10);
//
//             Assert.IsFalse(result);
//         }
//
//         [Test]
//         public void TryGetScore_ReturnsFalse_WhenNoScoreEntity()
//         {
//             using var world = new World("TestWorld");
//             var query = world.EntityManager.CreateScoreQuery();
//
//             var result = query.TryGetScore(out Score score);
//
//             Assert.IsFalse(result);
//         }
//
//         [Test]
//         public void TryGetScore_ReturnsTrue_WhenScoreEntityExists()
//         {
//             using var world = new World("TestWorld");
//             var entity = world.EntityManager.CreateEntity();
//             world.EntityManager.AddComponentData(entity, new Score { Value = 99 });
//             var query = world.EntityManager.CreateScoreQuery();
//
//             var result = query.TryGetScore(out Score score);
//
//             Assert.IsTrue(result);
//             Assert.AreEqual(99, score.Value);
//         }
//     }
// }
