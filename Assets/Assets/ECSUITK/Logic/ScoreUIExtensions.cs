using UnityEngine.UIElements;
using ECSUITK.Data;

namespace ECSUITK.Logic
{
    public static class ScoreUIExtensions
    {
        public static Label GetScoreLabel(this UIDocument document)
        {
            return document.rootVisualElement.Q<Label>(UIConstants.ScoreLabelName);
        }

        public static void ApplyScore(this Label label, Score score)
        {
            label.text = score.Value.ToString();
        }
    }
}
