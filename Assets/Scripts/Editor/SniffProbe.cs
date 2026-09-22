using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 여우 Sniff 가 숨김 발판을 제대로 감지하는지 확인하는 진단 도구.
/// 타일맵 오브젝트의 transform 은 Grid 원점에 있고 실제 타일은 멀리 떨어져 있어서,
/// 거리 판정을 transform 으로 하면 아무것도 안 잡힌다. 그게 고쳐졌는지 본다.
/// </summary>
public static class SniffProbe
{
    private const string Scene = "Assets/Scenes/Main Stage2.unity";

    [MenuItem("Tools/진단/Sniff 숨김 발판 감지 확인")]
    public static void Probe()
    {
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("Main Stage2.unity"))
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        float radius = DefaultSniffRadius();
        var items = Object.FindObjectsByType<RevealableTilemap>(FindObjectsSortMode.None)
                          .OrderBy(t => t.name).ToArray();
        Debug.Log($"[Sniff진단] RevealableTilemap {items.Length}개, sniffRadius={radius}");

        foreach (var rt in items)
        {
            var tm = rt.GetComponent<Tilemap>();
            if (tm == null) { Debug.LogWarning($"[Sniff진단] {rt.name}: Tilemap 없음"); continue; }

            Bounds lb = tm.localBounds;
            var world = new Bounds(tm.transform.TransformPoint(lb.center),
                                   Vector3.Scale(lb.size, tm.transform.lossyScale));
            Vector2 tileCenter = world.center;

            // 발판 바로 위(플레이어가 서 있을 만한 곳)와, 오브젝트 transform 위치에서 각각 판정
            bool nearTiles = rt.IsWithinRange(tileCenter + Vector2.up * 1.5f, radius);
            bool atTransform = rt.IsWithinRange(rt.transform.position, radius);
            bool farAway = rt.IsWithinRange(tileCenter + Vector2.right * (radius + 5f), radius);

            Debug.Log($"[Sniff진단] {rt.name,-18} transform={rt.transform.position} " +
                      $"타일영역 center={tileCenter} size=({world.size.x:F1}x{world.size.y:F1})");
            Debug.Log($"[Sniff진단]   발판 바로 위에서 감지 = {nearTiles}  (true 여야 정상)");
            Debug.Log($"[Sniff진단]   transform 위치에서 감지 = {atTransform}  (원점이면 false 가 정상)");
            Debug.Log($"[Sniff진단]   반경+5 밖에서 감지 = {farAway}  (false 여야 정상)");
        }

        // 발광 테두리는 런타임(Spawned)에 자식 타일맵으로 만들어진다.
        // 원점 기준 스케일을 타일 영역 중심 기준으로 보정하는 계산이 맞는지 미리 검산한다.
        foreach (var rt in items)
        {
            var tm = rt.GetComponent<Tilemap>();
            if (tm == null || !rt.drawGlowOutline) continue;

            Bounds lb = tm.localBounds;
            float th = rt.glowThickness;
            float sx = lb.size.x > 0.0001f ? (lb.size.x + 2f * th) / lb.size.x : 1f;
            float sy = lb.size.y > 0.0001f ? (lb.size.y + 2f * th) / lb.size.y : 1f;
            var offset = new Vector3(lb.center.x * (1f - sx), lb.center.y * (1f - sy), 0f);

            // 보정 후 테두리 타일맵의 실제 월드 경계
            Vector2 gMin = new Vector2(offset.x + lb.min.x * sx, offset.y + lb.min.y * sy);
            Vector2 gMax = new Vector2(offset.x + lb.max.x * sx, offset.y + lb.max.y * sy);
            float padL = lb.min.x - gMin.x, padR = gMax.x - lb.max.x;
            float padB = lb.min.y - gMin.y, padT = gMax.y - lb.max.y;

            bool ok = Mathf.Abs(padL - th) < 0.01f && Mathf.Abs(padR - th) < 0.01f
                   && Mathf.Abs(padB - th) < 0.01f && Mathf.Abs(padT - th) < 0.01f;
            Debug.Log($"[Sniff진단] {rt.name,-18} 테두리 scale=({sx:F3},{sy:F3}) offset=({offset.x:F2},{offset.y:F2}) " +
                      $"삐져나온 폭 L/R/B/T = {padL:F3}/{padR:F3}/{padB:F3}/{padT:F3} (목표 {th:F2}) → {(ok ? "OK" : "불일치!")}");
        }

        // 숨김 상태로 시작하는지(렌더러/콜라이더가 꺼지는지)는 런타임 Spawned 에서 처리되므로
        // 여기서는 컴포넌트 구성만 확인한다.
        foreach (var rt in items)
        {
            var tr = rt.GetComponent<TilemapRenderer>();
            var tc = rt.GetComponent<TilemapCollider2D>();
            var no = rt.GetComponent<Fusion.NetworkObject>();
            if (tr == null || tc == null || no == null)
                Debug.LogError($"[Sniff진단] {rt.name}: 구성 누락 " +
                               $"Renderer={tr != null} Collider={tc != null} NetworkObject={no != null}");
        }
        Debug.Log("[Sniff진단] 구성 확인 완료 (에러가 없으면 9개 모두 정상)");
    }

    private static float DefaultSniffRadius()
    {
        var pm = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefebs/Player.prefab")
                              ?.GetComponent<PlayerMovement>();
        return pm != null ? pm.sniffRadius : 5f;
    }

    public static void ProbeBatchMode() => Probe();
}
