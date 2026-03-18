using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using ECSUITK.Data;
using ECSUITK.Logic;

namespace ECSUITK.Engine
{
    [RequireComponent(typeof(UIDocument))]
    public class ScoreUITKPresenter : MonoBehaviour
    {
        private UIDocument _document;
        private Label _scoreLabel;
        private EntityManager _entityManager;
        private EntityQuery _scoreQuery;
        private int _lastScoreValue = int.MinValue;

        public void Initialize(EntityManager entityManager)
        {
            _document = GetComponent<UIDocument>();
            _scoreLabel = _document.GetScoreLabel();
            _entityManager = entityManager;
            _scoreQuery = _entityManager.CreateScoreQuery();
        }

        private void Start()
        {
            TryInitializeFromDefaultWorld();
        }

        private void LateUpdate()
        {
            if (IsEntityManagerInvalid())
            {
                return;
            }

            UpdateScoreIfChanged();
        }

        private void TryInitializeFromDefaultWorld()
        {
            if (IsEntityManagerInvalid() && HasDefaultWorld())
            {
                Initialize(World.DefaultGameObjectInjectionWorld.EntityManager);
            }
        }

        private bool HasDefaultWorld()
        {
            return World.DefaultGameObjectInjectionWorld != null;
        }

        private bool IsEntityManagerInvalid()
        {
            return _entityManager == default;
        }

        private void UpdateScoreIfChanged()
        {
            if (_scoreQuery.TryGetScore(out Score score))
            {
                ApplyScoreIfChanged(score);
            }
        }

        private void ApplyScoreIfChanged(Score score)
        {
            if (score.HasChanged(_lastScoreValue))
            {
                _scoreLabel.ApplyScore(score);
                _lastScoreValue = score.Value;
            }
        }
    }
}
