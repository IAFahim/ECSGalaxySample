#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-Assets/ScoreFeature}"
RUNTIME_ASM="${2:-ScoreFeature}"
TESTS_ASM="${3:-ScoreFeature.Tests}"

mkdir -p \
  "$ROOT/UI/Uxml" \
  "$ROOT/UI/Uss" \
  "$ROOT/Scripts/Data" \
  "$ROOT/Scripts/Logic" \
  "$ROOT/Scripts/Engine" \
  "$ROOT/Tests"

USS_PROJECT_PATH="project://database/${ROOT#/}/UI/Uss/ScoreScreen.uss"

cat > "$ROOT/UI/Uxml/ScoreScreen.uxml" <<EOF
<!-- File: UI/Uxml/ScoreScreen.uxml -->
<ui:UXML xmlns:ui="UnityEngine.UIElements" editor-extension-mode="False">
    <Style src="$USS_PROJECT_PATH" />
    <ui:VisualElement name="score-container" class="score-container">
        <ui:Label text="SCORE: 0" name="score-label" class="score-label" />
    </ui:VisualElement>
</ui:UXML>
EOF

cat > "$ROOT/UI/Uss/ScoreScreen.uss" <<'EOF'
/* File: UI/Uss/ScoreScreen.uss */
.score-container {
    position: absolute;
    top: 10px;
    right: 10px;
    background-color: rgba(0, 0, 0, 0.5);
    padding: 10px;
}

.score-label {
    color: rgb(255, 255, 255);
    font-size: 24px;
    -unity-font-style: bold;
}
EOF

cat > "$ROOT/Scripts/Data/ScoreConstants.cs" <<'EOF'
// File: Scripts/Data/ScoreConstants.cs
public static class ScoreConstants
{
    public const int PointsPerTick = 10;
    public const string ScoreLabelName = "score-label";
    public const string ScorePrefix = "SCORE: ";
}
EOF

cat > "$ROOT/Scripts/Data/ScoreData.cs" <<'EOF'
// File: Scripts/Data/ScoreData.cs
using Unity.Entities;

public struct ScoreData : IComponentData
{
    public int Value;
    public bool IsDirty;
}
EOF

cat > "$ROOT/Scripts/Logic/ScoreLogic.cs" <<'EOF'
// File: Scripts/Logic/ScoreLogic.cs
public static class ScoreLogic
{
    public static int AddPoints(this int currentScore, int points)
    {
        return currentScore + points;
    }

    public static bool RequiresUIUpdate(this bool isDirty)
    {
        return isDirty;
    }

    public static string FormatScore(this int currentScore, string prefix)
    {
        return prefix + currentScore;
    }
}
EOF

cat > "$ROOT/Scripts/Engine/ScoreUpdateSystem.cs" <<'EOF'
// File: Scripts/Engine/ScoreUpdateSystem.cs
using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public partial struct ScoreUpdateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScoreData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var job = new ScoreUpdateJob();
        state.Dependency = job.Schedule(state.Dependency);
    }

    [BurstCompile]
    public partial struct ScoreUpdateJob : IJobEntity
    {
        public void Execute(ref ScoreData score)
        {
            score.Value = score.Value.AddPoints(ScoreConstants.PointsPerTick);
            score.IsDirty = true;
        }
    }
}
EOF

cat > "$ROOT/Scripts/Engine/ScoreUIPresenter.cs" <<'EOF'
// File: Scripts/Engine/ScoreUIPresenter.cs
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class ScoreUIPresenter : MonoBehaviour
{
    private UIDocument _document;
    private Label _scoreLabel;
    private EntityManager _entityManager;
    private EntityQuery _scoreQuery;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _scoreLabel = _document.rootVisualElement.Q<Label>(ScoreConstants.ScoreLabelName);
    }

    private void Start()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            enabled = false;
            return;
        }

        _entityManager = world.EntityManager;
        _scoreQuery = _entityManager.CreateEntityQuery(typeof(ScoreData));
    }

    private void LateUpdate()
    {
        if (!_entityManager.IsCreated || _scoreLabel == null || _scoreQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        Entity entity = _scoreQuery.GetSingletonEntity();
        ScoreData scoreData = _entityManager.GetComponentData<ScoreData>(entity);

        if (!scoreData.IsDirty.RequiresUIUpdate())
        {
            return;
        }

        _scoreLabel.text = scoreData.Value.FormatScore(ScoreConstants.ScorePrefix);
        scoreData.IsDirty = false;
        _entityManager.SetComponentData(entity, scoreData);
    }
}
EOF

cat > "$ROOT/Scripts/Engine/ScoreBootstrapAuthoring.cs" <<'EOF'
// File: Scripts/Engine/ScoreBootstrapAuthoring.cs
using Unity.Entities;
using UnityEngine;

public sealed class ScoreBootstrapAuthoring : MonoBehaviour
{
    [Min(0)]
    public int InitialValue;

    public sealed class Baker : Baker<ScoreBootstrapAuthoring>
    {
        public override void Bake(ScoreBootstrapAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new ScoreData
            {
                Value = authoring.InitialValue,
                IsDirty = true,
            });
        }
    }
}
EOF

cat > "$ROOT/Tests/ScoreLogicTests.cs" <<'EOF'
// File: Tests/ScoreLogicTests.cs
using NUnit.Framework;

public sealed class ScoreLogicTests
{
    [Test]
    public void AddPoints_IncreasesValueCorrectly()
    {
        int currentScore = 100;
        int pointsToAdd = 50;

        int result = currentScore.AddPoints(pointsToAdd);

        Assert.AreEqual(150, result);
    }

    [Test]
    public void RequiresUIUpdate_ReturnsTrue_WhenDirty()
    {
        bool isDirty = true;

        bool result = isDirty.RequiresUIUpdate();

        Assert.IsTrue(result);
    }

    [Test]
    public void RequiresUIUpdate_ReturnsFalse_WhenNotDirty()
    {
        bool isDirty = false;

        bool result = isDirty.RequiresUIUpdate();

        Assert.IsFalse(result);
    }

    [Test]
    public void FormatScore_ReturnsCorrectlyFormattedString()
    {
        int score = 42;
        string prefix = ScoreConstants.ScorePrefix;

        string result = score.FormatScore(prefix);

        Assert.AreEqual("SCORE: 42", result);
    }
}
EOF

cat > "$ROOT/Scripts/$RUNTIME_ASM.asmdef" <<EOF
{
  "name": "$RUNTIME_ASM",
  "rootNamespace": "",
  "references": [
    "Unity.Entities",
    "Unity.Burst"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
EOF

cat > "$ROOT/Tests/$TESTS_ASM.asmdef" <<EOF
{
  "name": "$TESTS_ASM",
  "rootNamespace": "",
  "references": [
    "$RUNTIME_ASM",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false,
  "optionalUnityReferences": [
    "TestAssemblies"
  ]
}
EOF

printf 'Created full feature at: %s\n' "$ROOT"