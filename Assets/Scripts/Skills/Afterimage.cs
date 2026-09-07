using UnityEngine;

/// <summary>
/// 잔상 한 조각: 생성 후 서서히 투명해지며 사라진다. (로컬 렌더 전용, 물리 없음)
/// </summary>
public class Afterimage : MonoBehaviour
{
    private SpriteRenderer _sr;
    private Color _color;
    private float _life;
    private float _t;

    public void Init(float life, Color color)
    {
        _sr = GetComponent<SpriteRenderer>();
        _color = color;
        _life = Mathf.Max(life, 0.01f);
        _t = _life;
    }

    private void Update()
    {
        _t -= Time.deltaTime;
        if (_sr != null)
        {
            var c = _color;
            c.a = _color.a * Mathf.Clamp01(_t / _life);
            _sr.color = c;
        }
        if (_t <= 0f) Destroy(gameObject);
    }
}
