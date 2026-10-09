using UnityEngine;

// 효과음(2D) 재생 창구. 설정창 '효과음' 볼륨이 적용된다.
// 새 효과음은 AudioSource 를 직접 만들지 말고 SfxPlayer.Play(clip) 로 재생해야 효과음 볼륨 설정을 따른다.
public static class SfxPlayer
{
    public const string VolumePrefKey = "SfxVolume";
    public const float DefaultVolume = 0.8f;

    private static AudioSource _source;
    private static float _volume = -1f;

    public static float Volume
    {
        get
        {
            if (_volume < 0f) _volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePrefKey, DefaultVolume));
            return _volume;
        }
        set
        {
            _volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumePrefKey, _volume);
        }
    }

    public static void Play(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null) return;
        if (_source == null)
        {
            var go = new GameObject("SfxPlayer");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }
        _source.PlayOneShot(clip, Volume * volumeScale);
    }

    // Resources/SFX/<name> 을 찾아 재생. 파일이 없으면 조용히 무시하고 false.
    public static bool Play(string resourceName, float volumeScale = 1f)
    {
        var clip = Resources.Load<AudioClip>("SFX/" + resourceName);
        if (clip == null) return false;
        Play(clip, volumeScale);
        return true;
    }
}
