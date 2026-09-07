using UnityEngine;

/// <summary>
/// 스프라이트 시트 프레임을 한 번 재생하고 자동 소멸하는 일회성 이펙트(로컬 렌더 전용).
/// </summary>
public class SpriteEffect : MonoBehaviour
{
    public Sprite[] frames;
    public float fps = 20f;

    private SpriteRenderer _sr;
    private float _t;

    public static void Spawn(Sprite[] frames, Vector3 pos, float fps, float scale,
        bool flipX, int sortingLayerId, int sortingOrder, Color tint)
    {
        if (frames == null || frames.Length == 0) return;
        var go = new GameObject("FX");
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.flipX = flipX;
        sr.color = tint;
        sr.sortingLayerID = sortingLayerId;
        sr.sortingOrder = sortingOrder;
        sr.sprite = frames[0];
        var fx = go.AddComponent<SpriteEffect>();
        fx.frames = frames;
        fx.fps = fps;
        fx._sr = sr;
    }

    private void Awake() { if (_sr == null) _sr = GetComponent<SpriteRenderer>(); }

    private void Update()
    {
        if (frames == null || frames.Length == 0) { Destroy(gameObject); return; }
        _t += Time.deltaTime * fps;
        int i = (int)_t;
        if (i >= frames.Length) { Destroy(gameObject); return; }
        _sr.sprite = frames[i];
    }
}
