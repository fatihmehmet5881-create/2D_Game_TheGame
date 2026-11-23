using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class TileBuildingManager : MonoBehaviour
{
    [SerializeField] private bool isDemolishMode = false;

    private const string MAP_SAVE_KEY = "SavedTownData";
    [Serializable] public class PlacedTileData { public int x, y, tileIndex; }
    [Serializable] public class TownSaveData { public List<PlacedTileData> tiles = new List<PlacedTileData>(); }

    private TownSaveData currentTownData = new TownSaveData();
    [SerializeField] private Tilemap buildTilemap;
    [SerializeField] private GameObject buildPanel;
    [SerializeField] private GameObject buildButton;
    [SerializeField] private GameObject demolishButton;
    [SerializeField] private GameObject cancelDemolishButton;
    [SerializeField] private Animator playerAnimator;

    [SerializeField] private Tile[] buildingTiles;
    [SerializeField] private Tile demolishPreviewTile;
    [SerializeField] private float demolishPreviewScale = 2f;
    [SerializeField] private bool closePanelOnSelect = false;
    [SerializeField] private AudioClip placeSound;

    [Header("Build Paramaters")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private float buildRange = 1.2f;
    [SerializeField] private float buildDuration = 5f;

    private Coroutine currentBuildCoroutine;
    private SpriteRenderer constructionGhost;

    private Tile selectedTile;
    private SpriteRenderer previewRenderer;
    private Camera mainCam;

    private InputAction toggleMenuAction;
    private InputAction clickAction;
    private InputAction cancelAction;

    private void Awake()
    {
        mainCam = Camera.main;

        GameObject previewObj = new GameObject("BuildingPreview_Ghost");
        previewRenderer = previewObj.AddComponent<SpriteRenderer>();
        previewRenderer.sortingLayerName = "ForeGround";
        previewRenderer.sortingOrder = 999;
        previewObj.SetActive(false);

        toggleMenuAction = new InputAction("Toggle", binding: "<Keyboard>/b");
        clickAction = new InputAction("Click", binding: "<Mouse>/leftButton");
        clickAction.AddBinding("<Touchscreen>/primaryTouch/press");
        cancelAction = new InputAction("Cancel", binding: "<Mouse>/rightButton");
        cancelAction.AddBinding("<Keyboard>/escape");

        GameObject constructionObj = new GameObject("Construction_Ghost");
        constructionGhost = constructionObj.AddComponent<SpriteRenderer>();
        constructionGhost.sortingLayerName = "ForeGround";
        constructionGhost.sortingOrder = 998;
        constructionObj.SetActive(false);
    }

    private void OnEnable()
    {
        toggleMenuAction.Enable();
        clickAction.Enable();
        cancelAction.Enable();
        toggleMenuAction.performed += OnToggleMenu;
        clickAction.performed += OnClickPerformed;
        cancelAction.performed += OnCancelPerformed;

        if (playerController != null) playerController.OnManualMove += CancelPendingBuild;
    }

    private void OnDisable()
    {
        toggleMenuAction.performed -= OnToggleMenu;
        clickAction.performed -= OnClickPerformed;
        cancelAction.performed -= OnCancelPerformed;
        toggleMenuAction.Disable();
        clickAction.Disable();
        cancelAction.Disable();

        if (playerController != null) playerController.OnManualMove -= CancelPendingBuild;
    }

    private void OnToggleMenu(InputAction.CallbackContext _) => ToggleBuildMenu();
    private void OnClickPerformed(InputAction.CallbackContext _) => TryPlaceBuilding();
    private void OnCancelPerformed(InputAction.CallbackContext _) => CancelAllModes();

    private void Start()
    {
        LoadTown();
        ResetToNormalUI();

        if (cancelDemolishButton != null)
        {
            Button cancelBtn = cancelDemolishButton.GetComponent<Button>();
            if (cancelBtn != null)
            {
                cancelBtn.onClick.RemoveAllListeners();
                cancelBtn.onClick.AddListener(CancelAllModes);
            }
        }
    }

    private void Update()
    {
        if (buildTilemap == null || previewRenderer == null) return;

        if (isDemolishMode)
        {
            previewRenderer.transform.localScale = Vector3.one * demolishPreviewScale;
            if (IsPointerOverUI())
            {
                previewRenderer.gameObject.SetActive(false);
                return;
            }

            Vector3Int cellPos = GetMouseCellPosition();
            bool hasBuilding = buildTilemap.HasTile(cellPos);

            if (hasBuilding && demolishPreviewTile != null)
            {
                previewRenderer.sprite = demolishPreviewTile.sprite;
                previewRenderer.transform.position = buildTilemap.GetCellCenterWorld(cellPos);
                previewRenderer.color = new Color(0.65f, 0.05f, 0.05f, 0.9f);
                previewRenderer.gameObject.SetActive(true);
            }
            else previewRenderer.gameObject.SetActive(false);
            return;
        }
        if (selectedTile != null)
        {
            previewRenderer.transform.localScale = Vector3.one;
            if (IsPointerOverUI())
            {
                previewRenderer.gameObject.SetActive(false);
                return;
            }

            previewRenderer.gameObject.SetActive(true);
            Vector3Int cellPos = GetMouseCellPosition();
            previewRenderer.transform.position = buildTilemap.GetCellCenterWorld(cellPos);

            bool isOccupied = buildTilemap.HasTile(cellPos);
            previewRenderer.color = isOccupied ? new Color(1f, 0.2f, 0.2f, 0.5f) : new Color(0.2f, 1f, 0.2f, 0.5f);
        }
    }

    public void ToggleBuildMenu()
    {
        if (isDemolishMode) return;
        if (buildPanel == null) return;

        bool willOpen = !buildPanel.activeSelf;
        buildPanel.SetActive(willOpen);

        if (!willOpen) CancelBuild();
    }

    public void SelectBuilding(int index)
    {
        isDemolishMode = false;
        if (index >= 0 && index < buildingTiles.Length)
        {
            selectedTile = buildingTiles[index];
            previewRenderer.sprite = selectedTile.sprite;
            previewRenderer.gameObject.SetActive(true);

            if (closePanelOnSelect && buildPanel != null) buildPanel.SetActive(false);
        }
    }

    public void ActiveDemolishMode()
    {
        selectedTile = null;
        isDemolishMode = true;
        if (previewRenderer != null) previewRenderer.gameObject.SetActive(false);

        if (buildPanel != null) buildPanel.SetActive(false);
        if (buildButton != null) buildButton.SetActive(false);
        if (demolishButton != null) demolishButton.SetActive(false);
        if (cancelDemolishButton != null) cancelDemolishButton.SetActive(true);
    }

    public void TryPlaceBuilding()
    {
        if (IsPointerOverUI()) return;

        if (isDemolishMode)
        {
            Vector3Int demolishPos = GetMouseCellPosition();
            if (buildTilemap.HasTile(demolishPos))
            {
                buildTilemap.SetTile(demolishPos, null);
                currentTownData.tiles.RemoveAll(t => t.x == demolishPos.x && t.y == demolishPos.y);
                SaveTown();
                if (playerController != null) playerController.PlayAttackAnimation();
            }
            return;
        }
        if (selectedTile == null)
        {
            CancelPendingBuild();
            if (playerController != null)
            {
                Vector3 worldTarget = GetMouseWorldPosition();
                playerController.MoveTo(worldTarget, 0.1f, null);
            }
            return;
        }
        if (buildTilemap == null || playerController == null) return;

        Vector3Int cellPos = GetMouseCellPosition();
        if (buildTilemap.HasTile(cellPos)) return;
        CancelPendingBuild();

        Tile tileToBuild = selectedTile;
        Vector3 targetCenter = buildTilemap.GetCellCenterWorld(cellPos);

        constructionGhost.sprite = tileToBuild.sprite;
        constructionGhost.transform.position = targetCenter;
        constructionGhost.color = new Color(1f, 0.9f, 0.2f, 0.6f);
        constructionGhost.gameObject.SetActive(true);

        playerController.MoveTo(targetCenter, buildRange, () =>
        {
            currentBuildCoroutine = StartCoroutine(BuildRoutine(cellPos, tileToBuild));
        });
    }
    private System.Collections.IEnumerator BuildRoutine(Vector3Int cellPos, Tile tileToBuild)
    {
        float timer = 0f;

        while (timer < buildDuration)
        {
            if (Mathf.FloorToInt(timer) != Mathf.FloorToInt(timer + Time.deltaTime))
            {
                if (playerController != null) playerController.PlayAttackAnimation();
            }

            timer += Time.deltaTime;
            yield return null;
        }

        if (!buildTilemap.HasTile(cellPos))
        {
            buildTilemap.SetTile(cellPos, tileToBuild);

            int index = Array.IndexOf(buildingTiles, tileToBuild);
            currentTownData.tiles.Add(new PlacedTileData { x = cellPos.x, y = cellPos.y, tileIndex = index });
            SaveTown();

            if (AudioManager.Instance != null && placeSound != null)
                AudioManager.Instance.PlaySFX(placeSound);
        }

        constructionGhost.gameObject.SetActive(false);
        currentBuildCoroutine = null;
    }
    public void CancelPendingBuild()
    {
        if (currentBuildCoroutine != null)
        {
            StopCoroutine(currentBuildCoroutine);
            currentBuildCoroutine = null;
        }
        if (constructionGhost != null)
        {
            constructionGhost.gameObject.SetActive(false);
        }
    }

    public void CancelBuild()
    {
        selectedTile = null;
        if (previewRenderer != null) previewRenderer.gameObject.SetActive(false);
    }

    public void CancelAllModes()
    {
        CancelBuild();
        CancelPendingBuild();
        if (playerController != null) playerController.StopAutoMove();

        if (isDemolishMode)
        {
            isDemolishMode = false;
            ResetToNormalUI();
        }
    }
    private Vector3 GetMouseWorldPosition()
    {
        Vector2 screenPos = Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
        Vector3 worldPos = mainCam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(mainCam.transform.position.z)));
        worldPos.z = 0f;
        return worldPos;
    }

    private void ResetToNormalUI()
    {
        if (buildPanel != null) buildPanel.SetActive(false);
        if (buildButton != null) buildButton.SetActive(true);
        if (demolishButton != null) demolishButton.SetActive(true);
        if (cancelDemolishButton != null) cancelDemolishButton.SetActive(false);
    }

    public Vector3Int GetMouseCellPosition()
    {
        Vector2 screenPos = Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
        Vector3 worldPos = mainCam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(mainCam.transform.position.z)));
        return buildTilemap.WorldToCell(worldPos);
    }

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null || Pointer.current == null) return false;

        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = Pointer.current.position.ReadValue()
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }

    private void SaveTown()
    {
        string json = JsonUtility.ToJson(currentTownData);
        PlayerPrefs.SetString(MAP_SAVE_KEY, json);
        PlayerPrefs.Save();
    }

    private void LoadTown()
    {
        if (!PlayerPrefs.HasKey(MAP_SAVE_KEY)) return;
        string json = PlayerPrefs.GetString(MAP_SAVE_KEY);
        currentTownData = JsonUtility.FromJson<TownSaveData>(json);

        foreach (PlacedTileData data in currentTownData.tiles)
        {
            Vector3Int pos = new Vector3Int(data.x, data.y, 0);
            buildTilemap.SetTile(pos, buildingTiles[data.tileIndex]);
        }
    }
}