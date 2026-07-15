using System;
using Game.Creature;
using Game.KayKit;
using UnityEngine;

public class TokenMeshSelection : MonoBehaviour
{
    public TokenMeshes[] TokenOptions = new TokenMeshes[1];
    public BaseMeshes[] BaseOptions = new BaseMeshes[1];

    [SerializeField] private CreatureVisualCatalog visualCatalog;
    [SerializeField] private Transform visualRoot;

    private GameObject tokenObject;
    private GameObject baseObject;
    private GameObject animatedVisualInstance;
    private CreatureComponent creatureComponent;
    private MeshRenderer tokenMeshRenderer;
    private MeshFilter tokenMeshFilter;
    private MeshRenderer baseMeshRenderer;
    private MeshFilter baseMeshFilter;

    protected string TokenMeshToFind;

    public CreatureVisualCatalog VisualCatalog => visualCatalog;
    public GameObject ActiveVisualInstance => animatedVisualInstance;
    public bool UsingAnimatedVisual => animatedVisualInstance != null;

    protected virtual void Start()
    {
        UpdateTokenMesh();
    }

    protected virtual void Update()
    {
    }

    protected void UpdateTokenMesh()
    {
        if (this == null || gameObject == null)
            return;

        creatureComponent = GetComponentInParent<CreatureComponent>();
        TokenMeshToFind = creatureComponent != null ? creatureComponent.name : "Wizard";
        ApplySelection(TokenMeshToFind, false);
    }

    protected void UpdateTokenMesh(string meshName)
    {
        if (this == null || gameObject == null)
            return;

        creatureComponent = GetComponentInParent<CreatureComponent>();
        TokenMeshToFind = meshName;
        ApplySelection(TokenMeshToFind, true);
    }

    public void RefreshVisual()
    {
        UpdateTokenMesh();
    }

    public void ConfigureAnimatedCatalog(CreatureVisualCatalog catalog, Transform targetVisualRoot)
    {
        visualCatalog = catalog;
        visualRoot = targetVisualRoot;
    }

    private void ApplySelection(string key, bool warnOnMissingLegacy)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        CacheLegacyObjects();
        ClearAnimatedVisual();

        if (visualCatalog != null && visualCatalog.TryResolve(key, out CreatureVisualCatalogEntry entry))
        {
            Transform parent = ResolveVisualRoot();
            animatedVisualInstance = Instantiate(entry.VisualPrefab, parent, false);
            animatedVisualInstance.name = entry.VisualPrefab.name;
            animatedVisualInstance.transform.localPosition = Vector3.zero;
            animatedVisualInstance.transform.localRotation = Quaternion.identity;
            animatedVisualInstance.transform.localScale = entry.VisualPrefab.transform.localScale;
            CreaturePresentation.SetLayerRecursively(
                animatedVisualInstance,
                creatureComponent != null ? creatureComponent.gameObject.layer : gameObject.layer);
            SetLegacyTokenVisible(false);
            BindPresentation(animatedVisualInstance);
            RefreshPortrait();
            return;
        }

        BindPresentation(null);
        SetLegacyTokenVisible(true);
        ApplyLegacyMesh(key, warnOnMissingLegacy);
        RefreshPortrait();
    }

    private void CacheLegacyObjects()
    {
        if (transform.childCount < 2)
            return;

        tokenObject = transform.GetChild(0).gameObject;
        baseObject = transform.GetChild(1).gameObject;
        tokenMeshFilter = tokenObject.GetComponent<MeshFilter>();
        tokenMeshRenderer = tokenObject.GetComponent<MeshRenderer>();
        baseMeshFilter = baseObject.GetComponent<MeshFilter>();
        baseMeshRenderer = baseObject.GetComponent<MeshRenderer>();
    }

    private void ApplyLegacyMesh(string key, bool warnOnMissing)
    {
        if (tokenMeshFilter == null)
        {
            Debug.LogWarning("No MeshFilter component found!", this);
            return;
        }

        bool meshFound = false;
        if (TokenOptions != null)
        {
            foreach (TokenMeshes entry in TokenOptions)
            {
                if (entry == null || entry.Name != key)
                    continue;
                tokenMeshFilter.sharedMesh = entry.mesh;
                if (entry.mesh == null)
                    Debug.LogError($"Mesh for {entry.Name} is null!", this);
                meshFound = true;
                break;
            }
        }

        if (baseMeshFilter != null && BaseOptions != null && BaseOptions.Length > 0 &&
            BaseOptions[0] != null)
            baseMeshFilter.sharedMesh = BaseOptions[0].mesh;

        if (!meshFound)
        {
            if (warnOnMissing)
                Debug.LogError($"No mesh found with name: {key}", this);
            tokenMeshFilter.sharedMesh = null;
        }
    }

    private Transform ResolveVisualRoot()
    {
        if (visualRoot != null)
            return visualRoot;

        Transform owner = creatureComponent != null ? creatureComponent.transform : transform;
        Transform existing = owner.Find("VisualRoot");
        if (existing != null)
        {
            visualRoot = existing;
            return visualRoot;
        }

        GameObject created = new("VisualRoot");
        visualRoot = created.transform;
        visualRoot.SetParent(owner, false);
        return visualRoot;
    }

    private void ClearAnimatedVisual()
    {
        if (animatedVisualInstance == null)
            return;

        animatedVisualInstance.SetActive(false);
        if (Application.isPlaying)
            Destroy(animatedVisualInstance);
        else
            DestroyImmediate(animatedVisualInstance);
        animatedVisualInstance = null;
    }

    private void BindPresentation(GameObject visualInstance)
    {
        if (creatureComponent == null)
            return;

        CreaturePresentation presentation = creatureComponent.GetComponent<CreaturePresentation>();
        if (presentation == null)
            return;
        presentation.Bind(
            visualInstance != null ? visualInstance.GetComponent<CreatureAnimationController>() : null,
            visualInstance != null ? visualInstance.GetComponent<CreatureEquipmentVisuals>() : null);
    }

    private void SetLegacyTokenVisible(bool visible)
    {
        if (tokenMeshRenderer != null)
            tokenMeshRenderer.enabled = visible;
        if (baseMeshRenderer != null)
            baseMeshRenderer.enabled = visible;
    }

    private void RefreshPortrait()
    {
        if (!Application.isPlaying || creatureComponent == null)
            return;
        creatureComponent.GetComponent<Portrait>()?.RefreshSnapshot();
    }
}

[Serializable]
public class TokenMeshes
{
    public Mesh mesh;
    public string Name = "default";
}

[Serializable]
public class BaseMeshes
{
    public Mesh mesh;
    public int level;
}
