using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// UI 효과음. 씬에 배치할 필요 없이 스스로 부트스트랩된다.
//  - 화면의 모든 Button/Toggle/Dropdown/Slider 에 UiSfxHook 을 자동으로 붙인다 (코드로 생성되는 UI 포함, 주기적으로 검사).
//      마우스 올림 → Hover, 클릭 → Click (취소/닫기/로비 등 뒤로 가는 버튼은 Cancel, 탭 버튼은 Tab), 슬라이더 드래그 → SliderTick
//  - 팝업/오류는 해당 UI 코드에서 UiSfx.PopupOpen() / PopupClose() / Error() 를 직접 호출한다.
//  - 소리는 Resources/SFX/UI/<이름>, 재생은 SfxPlayer 경유 (설정창 '효과음' 볼륨 적용).
//  - 특정 버튼만 소리를 빼려면 그 오브젝트에 UiSfxMute 컴포넌트를 붙인다.
public class UiSfx : MonoBehaviour
{
    private const float ScanInterval = 0.25f;

    private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
    private static float _lastHoverTime;
    private float _nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("UiSfx");
        DontDestroyOnLoad(go);
        go.AddComponent<UiSfx>();
    }

    private void Awake() => SceneManager.sceneLoaded += (s, m) => _nextScan = 0f;

    private void Update()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + ScanInterval;
        // TitleScreen 처럼 HideAndDontSave 로 만든 UI 는 FindObjectsByType 에 안 잡히므로 FindObjectsOfTypeAll 로 찾고,
        // 씬에 올라가지 않은 프리팹 에셋은 제외한다.
        foreach (var sel in Resources.FindObjectsOfTypeAll<Selectable>())
        {
            if (!sel.gameObject.scene.IsValid()) continue;
            if (sel.GetComponent<UiSfxHook>() == null && sel.GetComponent<UiSfxMute>() == null)
                sel.gameObject.AddComponent<UiSfxHook>();
        }
    }

    // ---------- 재생 API ----------
    public static void Hover()
    {
        if (Time.unscaledTime - _lastHoverTime < 0.05f) return; // 버튼 사이를 빠르게 지날 때 소리가 겹치지 않게
        _lastHoverTime = Time.unscaledTime;
        Play("Hover");
    }
    public static void Click() => Play("Click");
    public static void Cancel() => Play("Cancel");
    public static void Error() => Play("Error");
    public static void PopupOpen() => Play("PopupOpen");
    public static void PopupClose() => Play("PopupClose");
    public static void Tab() => Play("Tab");
    public static void SliderTick() => Play("SliderTick");

    private static void Play(string name)
    {
        if (!Clips.TryGetValue(name, out var clip))
        {
            clip = Resources.Load<AudioClip>("SFX/UI/" + name);
            Clips[name] = clip;
        }
        SfxPlayer.Play(clip);
    }
}
