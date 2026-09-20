using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Main Stage2 의 초록 덤불 타일(ground_wall 에 섞여 있던 것)을
/// "여우 Sniff 로만 드러나는 나무 판자 발판"으로 변환하는 에디터 도구.
///
/// 하는 일
///  1. ground_wall 에서 초록 덤불(tileset 2_29) 칸을 찾아 가로로 붙은 덩어리로 묶는다.
///  2. 덩어리 하나당 타일맵 오브젝트 하나를 Grid 밑에 만들고
///     Tilemap + TilemapRenderer + TilemapCollider2D + NetworkObject + RevealableTilemap 을 붙인다.
///  3. 그 자리에 나무 판자 타일(좌/중/우 끝처리)을 깔고, ground_wall 에서는 원래 칸을 지운다.
///
/// 발판마다 오브젝트를 따로 두는 이유는 DoSniff 가 오브젝트 위치 기준 거리로 판정하기 때문이다.
/// (하나에 몰아넣으면 스킬 한 번에 맵 전체 발판이 드러난다.)
/// </summary>
public static class HiddenPlatformSetup
{
    private const string GroundMapName = "ground_wall";
    private const string GreenTileName = "tileset 2_29";     // 현재 깔려 있는 초록 덤불
    private const string TileDir = "Assets/pixel Adventure/Terrain/";
    private const string TileLeft = "Terrain (16x16)_46";    // 나무 판자 좌
    private const string TileMid = "Terrain (16x16)_47";     // 나무 판자 중
    private const string TileRight = "Terrain (16x16)_48";   // 나무 판자 우
    private const string ObjectPrefix = "HiddenPlatform_";
    private const int SortingOrder = 4;                      // 지형 위, 플레이어(100) 아래

    [MenuItem("Tools/숨김 발판/1. 미리보기 (변경 없음)", priority = 1)]
    public static void Preview() => Run(dryRun: true);

    [MenuItem("Tools/숨김 발판/2. 변환 실행", priority = 2)]
    public static void Convert() => Run(dryRun: false);

    /// <summary>
    /// 배치 모드 진입점. 에디터를 닫은 상태에서 커맨드라인으로 돌릴 때 쓴다.
    ///   Unity.exe -batchmode -quit -projectPath &lt;경로&gt; -executeMethod HiddenPlatformSetup.ConvertBatchMode -logFile -
    /// 씬을 직접 열고 변환한 뒤 저장한다. (에디터가 켜져 있으면 프로젝트가 잠겨 실행되지 않는다)
    /// </summary>
    public static void ConvertBatchMode()
    {
        const string scenePath = "Assets/Scenes/Main Stage2.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        Run(dryRun: false);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[숨김발판] 배치 변환 후 저장 완료: {scenePath}");
    }

    private static void Run(bool dryRun)
    {
        var ground = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None)
                           .FirstOrDefault(t => t.name == GroundMapName);
        if (ground == null)
        {
            Debug.LogError($"[숨김발판] '{GroundMapName}' 타일맵을 못 찾았습니다. Main Stage2 씬을 연 상태에서 실행하세요.");
            return;
        }

        var existing = Object.FindObjectsByType<RevealableTilemap>(FindObjectsSortMode.None);
        if (existing.Length > 0 && !dryRun)
        {
            Debug.LogError($"[숨김발판] 이미 RevealableTilemap 오브젝트가 {existing.Length}개 있습니다. " +
                           "다시 변환하려면 먼저 그 오브젝트들을 지우세요 (되돌리기는 Ctrl+Z).");
            return;
        }

        var cells = FindGreenCells(ground);
        if (cells.Count == 0)
        {
            Debug.LogWarning($"[숨김발판] '{GreenTileName}' 타일을 {GroundMapName} 에서 찾지 못했습니다. 이미 변환했나요?");
            return;
        }

        var groups = Cluster(cells);
        groups.Sort((a, b) => a.Min(c => c.x).CompareTo(b.Min(c => c.x)));

        Debug.Log($"[숨김발판] 초록 덤불 {cells.Count}칸 / 발판 {groups.Count}개 발견{(dryRun ? " (미리보기)" : "")}");
        foreach (var g in groups)
        {
            Debug.Log($"    x={g.Min(c => c.x)}~{g.Max(c => c.x)} y={g.Min(c => c.y)} ({g.Count}칸)");
        }
        if (dryRun) return;

        var left = LoadTile(TileLeft);
        var mid = LoadTile(TileMid);
        var right = LoadTile(TileRight);
        if (left == null || mid == null || right == null) return;

        var groundRenderer = ground.GetComponent<TilemapRenderer>();
        // 타일맵은 RecordObject 로는 되돌리기가 잘 안 잡혀서 전체 스냅샷을 남긴다.
        Undo.RegisterCompleteObjectUndo(ground, "숨김 발판 변환");

        int made = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            var group = new HashSet<Vector3Int>(groups[i]);

            var go = new GameObject($"{ObjectPrefix}{i + 1}");
            Undo.RegisterCreatedObjectUndo(go, "숨김 발판 생성");
            go.transform.SetParent(ground.transform.parent, false);
            // 셀 좌표가 어긋나지 않도록 ground_wall 의 로컬 트랜스폼을 그대로 따라간다.
            go.transform.localPosition = ground.transform.localPosition;
            go.transform.localRotation = ground.transform.localRotation;
            go.transform.localScale = ground.transform.localScale;
            go.layer = ground.gameObject.layer;   // Ground 레이어여야 밟을 수 있다

            var tm = go.AddComponent<Tilemap>();
            tm.tileAnchor = ground.tileAnchor;
            tm.orientation = ground.orientation;

            var tr = go.AddComponent<TilemapRenderer>();
            if (groundRenderer != null) tr.sortingLayerID = groundRenderer.sortingLayerID;
            tr.sortingOrder = SortingOrder;

            go.AddComponent<TilemapCollider2D>();
            go.AddComponent<NetworkObject>();     // Revealable 이 요구
            go.AddComponent<RevealableTilemap>();

            foreach (var c in groups[i])
            {
                bool hasLeft = group.Contains(new Vector3Int(c.x - 1, c.y, c.z));
                bool hasRight = group.Contains(new Vector3Int(c.x + 1, c.y, c.z));
                TileBase t = (!hasLeft && hasRight) ? left
                           : (hasLeft && !hasRight) ? right
                           : mid;                       // 1칸짜리 / 가운데는 mid
                tm.SetTile(c, t);
                ground.SetTile(c, null);                // 원래 지형에서는 제거
            }
            tm.CompressBounds();
            made++;
        }

        EditorSceneManager.MarkSceneDirty(ground.gameObject.scene);
        Debug.Log($"[숨김발판] 완료: 발판 {made}개 생성, ground_wall 에서 {cells.Count}칸 제거. " +
                  "씬을 저장하면 Fusion 이 NetworkObject 를 베이크합니다.");
    }

    private static List<Vector3Int> FindGreenCells(Tilemap ground)
    {
        var found = new List<Vector3Int>();
        var b = ground.cellBounds;
        foreach (var pos in b.allPositionsWithin)
        {
            var t = ground.GetTile(pos);
            if (t != null && t.name == GreenTileName) found.Add(pos);
        }
        return found;
    }

    /// <summary>상하좌우로 붙은 칸끼리 묶는다.</summary>
    private static List<List<Vector3Int>> Cluster(List<Vector3Int> cells)
    {
        var set = new HashSet<Vector3Int>(cells);
        var seen = new HashSet<Vector3Int>();
        var result = new List<List<Vector3Int>>();
        foreach (var start in cells)
        {
            if (!seen.Add(start)) continue;
            var comp = new List<Vector3Int>();
            var stack = new Stack<Vector3Int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                comp.Add(c);
                foreach (var n in Neighbours(c))
                {
                    if (set.Contains(n) && seen.Add(n)) stack.Push(n);
                }
            }
            result.Add(comp);
        }
        return result;
    }

    private static IEnumerable<Vector3Int> Neighbours(Vector3Int c)
    {
        yield return new Vector3Int(c.x + 1, c.y, c.z);
        yield return new Vector3Int(c.x - 1, c.y, c.z);
        yield return new Vector3Int(c.x, c.y + 1, c.z);
        yield return new Vector3Int(c.x, c.y - 1, c.z);
    }

    private static TileBase LoadTile(string assetName)
    {
        var t = AssetDatabase.LoadAssetAtPath<TileBase>($"{TileDir}{assetName}.asset");
        if (t == null) Debug.LogError($"[숨김발판] 타일 에셋을 못 찾았습니다: {TileDir}{assetName}.asset");
        return t;
    }
}
