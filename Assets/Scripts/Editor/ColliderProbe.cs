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

        // 스프라이트의 physics shape 을 바꿔도 타일맵이 캐싱한 콜라이더 도형은 그대로다.
        // 타일을 새로고침해야 새 도형으로 다시 만들어진다.
        ground.RefreshAllTiles();

        var tc = ground.GetComponent<TilemapCollider2D>();
        var cc = ground.GetComponent<CompositeCollider2D>();
        Debug.Log($"[진단] TilemapCollider2D usedByComposite={tc.usedByComposite} " +
                  $"extrusionFactor={tc.extrusionFactor}");
        if (cc != null)
        {
            cc.GenerateGeometry();
            Physics2D.SyncTransforms();

            Vector3 center = ground.GetCellCenterWorld(cell);
            float cellBottom = center.y - 0.5f;
            Debug.Log($"[진단] ground_wall transform.position={ground.transform.position} " +
                      $"셀중심 y={center.y:F4} 셀아랫변 y={cellBottom:F4}");

            // 실제로 '위에서 떨어뜨렸을 때 어디에 닿는지'를 레이캐스트로 직접 잰다.
            foreach (float dx in new[] { -0.4f, 0f, 0.4f })
            {
                var origin = new Vector2(center.x + dx, center.y + 3f);
                var hit = Physics2D.Raycast(origin, Vector2.down, 6f);
                if (hit.collider != null)
                    Debug.Log($"[진단]   레이캐스트 x={origin.x:F2} → 닿은 y={hit.point.y:F4} " +
                              $"(셀아랫변 기준 {(hit.point.y - cellBottom) * 16f:F2} px)  collider={hit.collider.GetType().Name}");
                else
                    Debug.Log($"[진단]   레이캐스트 x={origin.x:F2} → 안 닿음");
            }

            // 비교용: 옆 칸(발사대가 없는 지면) 도 재본다
            var g2 = new Vector2(center.x + 3f, center.y + 3f);
            var h2 = Physics2D.Raycast(g2, Vector2.down, 8f);
            if (h2.collider != null)
                Debug.Log($"[진단]   (비교) 옆 지면 x={g2.x:F2} → y={h2.point.y:F4}");

            // --- 콜라이더 강제 재생성 방법들을 차례로 시험 ---
            float Surface()
            {
                Physics2D.SyncTransforms();
                var h = Physics2D.Raycast(new Vector2(center.x, center.y + 3f), Vector2.down, 6f);
                return h.collider != null ? (h.point.y - cellBottom) * 16f : float.NaN;
            }

            Debug.Log($"[진단] [A] 씬을 막 열었을 때        : {Surface():F2} px");

            tc.enabled = false; tc.enabled = true;
            cc.GenerateGeometry();
            Debug.Log($"[진단] [B] 콜라이더 강제 재생성 후  : {Surface():F2} px");

            // 이 상태를 저장한 뒤 다시 열어서, 씬 로드만으로도 올바른지 확인한다.
            EditorSceneManager.MarkSceneDirty(ground.gameObject.scene);
            EditorSceneManager.SaveScene(ground.gameObject.scene);
            EditorSceneManager.OpenScene("Assets/Scenes/Main Stage1.unity", OpenSceneMode.Single);

            var g2b = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None)
                            .FirstOrDefault(t => t.name == "ground_wall");
            var cc2 = g2b != null ? g2b.GetComponent<CompositeCollider2D>() : null;
            if (cc2 != null)
            {
                cc2.GenerateGeometry();
                Physics2D.SyncTransforms();
                var h3 = Physics2D.Raycast(new Vector2(center.x, center.y + 3f), Vector2.down, 6f);
                float v = h3.collider != null ? (h3.point.y - cellBottom) * 16f : float.NaN;
                Debug.Log($"[진단] [C] 저장 후 다시 연 직후   : {v:F2} px   <-- 이 값이 런타임에서 보게 될 값");
            }

            Debug.Log($"[진단] 목표: 보이는 막대 윗면 11.00 px");
        }
    }

    /// <summary>
    /// 플레이어 프리팹을 실제로 만들어 콜라이더와 스프라이트의 월드 경계를 직접 잰다.
    /// (프리팹 YAML 계산이 실제와 어긋나는지 확인용)
    /// </summary>
    [MenuItem("Tools/진단/플레이어 콜라이더 확인")]
    public static void ProbePlayer()
    {
        foreach (var path in new[] { "Assets/Prefebs/Player.prefab", "Assets/Prefebs/Player2.prefab" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogError($"[진단] 없음: {path}"); continue; }

            var go = Object.Instantiate(prefab);
            go.transform.position = Vector3.zero;

            var col = go.GetComponent<Collider2D>();
            var sr = go.GetComponent<SpriteRenderer>();
            Debug.Log($"[진단] === {System.IO.Path.GetFileName(path)} (scale={go.transform.localScale}) ===");
            if (col != null)
            {
                Debug.Log($"[진단]   {col.GetType().Name} bounds: min.y={col.bounds.min.y:F4} max.y={col.bounds.max.y:F4} " +
                          $"size={col.bounds.size}");
            }
            if (sr != null && sr.sprite != null)
            {
                var sp = sr.sprite;
                Debug.Log($"[진단]   sprite={sp.name} rect={sp.rect} PPU={sp.pixelsPerUnit}");
                Debug.Log($"[진단]   SpriteRenderer bounds: min.y={sr.bounds.min.y:F4} max.y={sr.bounds.max.y:F4}");
                if (col != null)
                {
                    float d = col.bounds.min.y - sr.bounds.min.y;
                    Debug.Log($"[진단]   >>> 콜라이더 밑면 - 스프라이트 밑면 = {d:F4} 유닛 = {d * 16f:F2} px " +
                              $"({(d > 0 ? "발이 콜라이더보다 아래 → 파묻힘" : "발이 콜라이더보다 위 → 떠 보임")})");
                }
            }
            var gc = go.transform.Find("GroundCheck");
            if (gc != null) Debug.Log($"[진단]   GroundCheck world y={gc.position.y:F4}");

            var pm = go.GetComponent<PlayerMovement>();
            if (pm != null)
                Debug.Log($"[진단]   점프대: launcherTile={(pm.launcherTile != null ? pm.launcherTile.name : "(미지정)")} " +
                          $"launcherJumpForce={pm.launcherJumpForce} jumpForce={pm.jumpForce}");

            Object.DestroyImmediate(go);
        }
    }

    /// <summary>배치 모드 진입점.</summary>
    public static void ProbeBatchMode()
    {
        ProbePlayer();
        Probe();
    }
}
