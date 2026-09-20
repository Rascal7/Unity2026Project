using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 발사대(Idle_0) 타일이 실제로 어떤 콜라이더를 만드는지 Unity 에 직접 물어보는 진단 도구.
/// 스프라이트의 physics shape 이 비어 있으면 Unity 가 자동 생성하는데, 그 결과는
/// 에셋 파일만 봐서는 알 수 없어서 런타임 API 로 확인한다.
/// </summary>
public static class ColliderProbe
{
    [MenuItem("Tools/진단/발사대 콜라이더 확인")]
    public static void Probe()
    {
        var tile = AssetDatabase.LoadAssetAtPath<Tile>("Assets/Tilemap/Traps/Idle_0.asset");
        if (tile == null) { Debug.LogError("[진단] Idle_0.asset 을 못 찾음"); return; }

        var sp = tile.sprite;
        Debug.Log($"[진단] Idle_0  colliderType={tile.colliderType}  flags={tile.flags}  " +
                  $"transform.y={tile.transform.GetColumn(3).y}");
        Debug.Log($"[진단] sprite={sp.name} rect={sp.rect} pivot={sp.pivot} PPU={sp.pixelsPerUnit} " +
                  $"bounds={sp.bounds}");

        int n = sp.GetPhysicsShapeCount();
        Debug.Log($"[진단] physicsShapeCount={n}");
        for (int i = 0; i < n; i++)
        {
            var pts = new List<Vector2>();
            sp.GetPhysicsShape(i, pts);
            float minY = pts.Min(p => p.y), maxY = pts.Max(p => p.y);
            float minX = pts.Min(p => p.x), maxX = pts.Max(p => p.x);
            Debug.Log($"[진단]   shape[{i}] {pts.Count}점  x {minX:F4}~{maxX:F4}  y {minY:F4}~{maxY:F4} (유닛)");
            Debug.Log($"[진단]   shape[{i}] 픽셀환산  y {minY * sp.pixelsPerUnit:F2}~{maxY * sp.pixelsPerUnit:F2}");
        }

        // 실제 씬에서 타일맵 콜라이더가 만든 도형 확인
        EditorSceneManager.OpenScene("Assets/Scenes/Main Stage1.unity", OpenSceneMode.Single);
        var ground = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None)
                           .FirstOrDefault(t => t.name == "ground_wall");
        if (ground == null) { Debug.LogError("[진단] ground_wall 없음"); return; }

        var cell = new Vector3Int(18, -2, 0);      // 스크린샷의 발사대
        Debug.Log($"[진단] cell{cell} tile={ground.GetTile(cell)?.name}  " +
                  $"cellBoundsWorld={ground.GetCellCenterWorld(cell)}  cellSize={ground.cellSize}");

        var tc = ground.GetComponent<TilemapCollider2D>();
        var cc = ground.GetComponent<CompositeCollider2D>();
        Debug.Log($"[진단] TilemapCollider2D usedByComposite={tc.usedByComposite} " +
                  $"extrusionFactor={tc.extrusionFactor}");
        if (cc != null)
        {
            cc.GenerateGeometry();
            Vector3 center = ground.GetCellCenterWorld(cell);
            // 이 셀 주변을 지나는 경로만 추려서 y 최대값(윗면)을 본다
            float best = float.NegativeInfinity;
            for (int p = 0; p < cc.pathCount; p++)
            {
                var pts = new Vector2[cc.GetPathPointCount(p)];
                cc.GetPath(p, pts);
                foreach (var pt in pts)
                {
                    if (Mathf.Abs(pt.x - center.x) < 0.9f && Mathf.Abs(pt.y - center.y) < 1.2f)
                        best = Mathf.Max(best, pt.y);
                }
            }
            Debug.Log($"[진단] 셀중심 y={center.y:F4}, 셀 아랫변 y={center.y - 0.5f:F4}");
            Debug.Log($"[진단] 이 셀 주변 컴포지트 경로의 최대 y = {best:F4}  " +
                      $"→ 셀 아랫변 기준 {(best - (center.y - 0.5f)) * 16f:F2} px");
        }
    }

    /// <summary>배치 모드 진입점.</summary>
    public static void ProbeBatchMode() => Probe();
}
