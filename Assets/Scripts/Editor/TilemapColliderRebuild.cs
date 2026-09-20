using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 타일맵 콜라이더를 강제로 다시 만든다.
///
/// 타일맵은 콜라이더 도형을 씬에 캐싱한다. 그래서 타일 에셋의 m_Transform 이나
/// 스프라이트의 physicsShape 을 바꿔도, 이미 놓인 칸의 콜라이더는 옛날 도형 그대로다.
/// Tilemap.RefreshAllTiles() 로는 안 풀리고 TilemapCollider2D 를 껐다 켜야 다시 만들어진다.
/// 그 상태로 씬을 저장하면 런타임까지 유지된다.
/// </summary>
public static class TilemapColliderRebuild
{
    [MenuItem("Tools/타일맵 콜라이더 다시 만들기")]
    public static void Rebuild()
    {
        int n = 0;
        foreach (var tc in Object.FindObjectsByType<TilemapCollider2D>(FindObjectsSortMode.None))
        {
            Undo.RegisterCompleteObjectUndo(tc, "타일맵 콜라이더 재생성");
            tc.enabled = false;
            tc.enabled = true;
            var cc = tc.GetComponent<CompositeCollider2D>();
            if (cc != null) cc.GenerateGeometry();
            n++;
        }
        Debug.Log($"[콜라이더] {n}개 타일맵 콜라이더를 다시 만들었습니다. 씬을 저장해야 유지됩니다.");
    }

    /// <summary>배치 모드: 주요 씬을 열어 콜라이더를 다시 만들고 저장한다.</summary>
    public static void RebuildBatchMode()
    {
        string[] scenes =
        {
            "Assets/Scenes/Main Stage1.unity",
            "Assets/Scenes/Main Stage2.unity",
            "Assets/Scenes/Main Stage3.unity",
            "Assets/Scenes/TutorialStage.unity",
        };
        foreach (var path in scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Rebuild();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[콜라이더] 저장: {path}");
        }

        // 확인: Stage1/2 의 발사대 칸에 레이캐스트
        Verify("Assets/Scenes/Main Stage1.unity", new Vector3Int(18, -2, 0));
        Verify("Assets/Scenes/Main Stage2.unity", new Vector3Int(71, -31, 0));
    }

    private static void Verify(string path, Vector3Int cell)
    {
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var ground = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None)
                           .FirstOrDefault(t => t.name.ToLower().Contains("ground"));
        if (ground == null) { Debug.LogError($"[콜라이더] ground 타일맵 없음: {path}"); return; }

        var cc = ground.GetComponent<CompositeCollider2D>();
        if (cc != null) cc.GenerateGeometry();
        Physics2D.SyncTransforms();

        Vector3 center = ground.GetCellCenterWorld(cell);
        float cellBottom = center.y - 0.5f;
        var hit = Physics2D.Raycast(new Vector2(center.x, center.y + 3f), Vector2.down, 6f);
        string tile = ground.GetTile(cell)?.name ?? "(없음)";
        if (hit.collider != null)
            Debug.Log($"[콜라이더] 확인 {System.IO.Path.GetFileName(path)} cell{cell} tile={tile} " +
                      $"→ 접지면 {(hit.point.y - cellBottom) * 16f:F2} px (목표 11.00)");
        else
            Debug.Log($"[콜라이더] 확인 {System.IO.Path.GetFileName(path)} cell{cell} → 안 닿음");
    }
}
