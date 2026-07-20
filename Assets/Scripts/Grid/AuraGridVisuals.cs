using System.Collections.Generic;
using Game.Creature;
using Game.Creature.Rules;
using UnityEngine;

namespace GridPrivate
{
    [RequireComponent(typeof(GridAPIPrivate))]
    public class AuraGridVisuals : MonoBehaviour
    {
        [SerializeField]
        private GameObject AuraPrefab;

        [SerializeField]
        private float AuraOffset = 0.015f;

        [SerializeField]
        private GameObject AuraParticlePrefab;

        [SerializeField]
        private float AuraParticleVerticalOffset = 0.08f;

        private GameObjectPool AuraPool;
        private GameObjectPool AuraParticlePool;
        private GridAPIPrivate GridApi;
        private readonly List<Vector3Int> currentCells = new();
        private readonly List<float> currentParticleRadii = new();

        public IReadOnlyList<Vector3Int> CurrentCells => currentCells;
        public IReadOnlyList<float> CurrentParticleRadii => currentParticleRadii;

        private void Awake()
        {
            GridApi = GetComponent<GridAPIPrivate>();
            if (AuraPrefab != null)
                AuraPool = new GameObjectPool(AuraPrefab);
            if (AuraParticlePrefab != null)
                AuraParticlePool = new GameObjectPool(AuraParticlePrefab);
        }

        private void OnEnable()
        {
            OnCombatStart.AddListener(Refresh);
            OnNextTurn.AddListener(RefreshForTurn);
            OnStepEnd.AddListener(RefreshForStep);
            OnDeath.AddListener(RefreshForDeath);
        }

        private void OnDisable()
        {
            OnCombatStart.RemoveListener(Refresh);
            OnNextTurn.RemoveListener(RefreshForTurn);
            OnStepEnd.RemoveListener(RefreshForStep);
            OnDeath.RemoveListener(RefreshForDeath);
            Clear();
        }

        public void Refresh()
        {
            Clear();
            if (GridApi == null)
                return;

            List<ActionController> combatants = new(
                Object.FindObjectsByType<ActionController>(FindObjectsSortMode.None)
            );
            currentCells.AddRange(
                CreatureAuraResolver.GetAuraCells(combatants, GridApi.GetTiles())
            );

            RefreshTileAuras();
            RefreshParticleAuras(combatants);
        }

        private void RefreshTileAuras()
        {
            if (AuraPool == null)
                return;

            foreach (Vector3Int cell in currentCells)
            {
                GameObject go = AuraPool.GetObject();
                go.transform.position = new Vector3(cell.x, cell.y + AuraOffset, cell.z);
            }
        }

        private void RefreshParticleAuras(IEnumerable<ActionController> combatants)
        {
            foreach (
                CreatureAuraInstance auraInstance in CreatureAuraResolver.GetVisualAuras(combatants)
            )
            {
                if (auraInstance.SourceObject == null || auraInstance.Aura == null)
                    continue;

                float radius = Mathf.Max(0.5f, auraInstance.Aura.radiusFeet / 5f);
                currentParticleRadii.Add(radius);

                if (AuraParticlePool == null)
                    continue;

                GameObject go = AuraParticlePool.GetObject();
                go.transform.position =
                    auraInstance.SourceObject.transform.position
                    + Vector3.up * AuraParticleVerticalOffset;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                ConfigureParticleRadius(go, radius);
            }
        }

        private static void ConfigureParticleRadius(GameObject root, float radius)
        {
            ParticleSystem[] particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem particleSystem in particleSystems)
            {
                ParticleSystem.ShapeModule shape = particleSystem.shape;
                if (shape.enabled)
                    shape.radius = radius;

                particleSystem.Clear(true);
                particleSystem.Play(true);
            }
        }

        private void Clear()
        {
            currentCells.Clear();
            currentParticleRadii.Clear();
            AuraPool?.Clear();
            AuraParticlePool?.Clear();
        }

        private void RefreshForTurn(GameObject _)
        {
            Refresh();
        }

        private void RefreshForStep(Vector3 _)
        {
            Refresh();
        }

        private void RefreshForDeath(GameObject _)
        {
            Refresh();
        }
    }
}
