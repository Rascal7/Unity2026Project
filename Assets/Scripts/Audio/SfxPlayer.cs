using System.Collections.Generic;
using UnityEngine;

// 효과음(2D) 재생 창구. 설정창 '효과음' 볼륨이 적용된다.
// 새 효과음은 AudioSource 를 직접 만들지 말고 SfxPlayer.Play(clip) 로 재생해야 효과음 볼륨 설정을 따른다.
public static class SfxPlayer
{
    public const string VolumePrefKey = "SfxVolume";
    public const float DefaultVolume = 0.8f;

    private static AudioSource _source;
    private static float _volume = -1f;
    private static readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();
    private static readonly Dictionary<string, float> _lastPlayTime = new Dictionary<string, float>();

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
        _source.PlayOneShot(clip, Volume * Mathf.Clamp(volumeScale, 0f, 1.5f));
    }

    // Resources/SFX/<name> 을 찾아 재생. 파일이 없으면 프로시저럴 레트로 효과음으로 폴백 재생한다.
    public static bool Play(string resourceName, float volumeScale = 1f)
    {
        if (string.IsNullOrEmpty(resourceName)) return false;

        // 동일 효과음이 한 프레임에 중복 폭주하는 현상 방지
        float now = Time.unscaledTime;
        if (_lastPlayTime.TryGetValue(resourceName, out float last) && now - last < 0.035f)
            return false;
        _lastPlayTime[resourceName] = now;

        if (!_clipCache.TryGetValue(resourceName, out AudioClip clip) || clip == null)
        {
            clip = Resources.Load<AudioClip>("SFX/" + resourceName);
            if (clip == null)
            {
                clip = SynthesizeFallbackClip(resourceName);
            }
            _clipCache[resourceName] = clip;
        }

        if (clip == null) return false;
        Play(clip, volumeScale);
        return true;
    }

    // ---------- 인게임 액션 & 환경 SFX 단축 호출 API ----------
    public static void Jump() => Play("Jump", 0.65f);
    public static void Land() => Play("Land", 0.55f);
    public static void Dash() => Play("FoxDash", 0.80f);
    public static void Sniff() => Play("FoxSniff", 0.85f);
    public static void Swap() => Play("FoxSwap", 0.85f);
    public static void Shadow() => Play("FoxShadow", 0.80f);
    public static void CarrotLaunch() => Play("RabbitCarrot", 0.75f);
    public static void CarrotStick() => Play("CarrotStick", 0.85f);
    public static void SuperJump() => Play("RabbitJump", 0.85f);
    public static void Stomp() => Play("RabbitStomp", 0.95f);
    public static void PowerUp() => Play("RabbitPowerUp", 0.90f);
    public static void Death() => Play("Death", 0.85f);
    public static void Respawn() => Play("Respawn", 0.75f);
    public static void Coin() => Play("Coin", 0.85f);
    public static void Lever(bool isOn) => Play(isOn ? "LeverOn" : "LeverOff", 0.80f);
    public static void CheckpointHalf() => Play("CheckpointHalf", 0.75f);
    public static void CheckpointFull() => Play("CheckpointFull", 0.90f);
    public static void BlockBreak() => Play("BlockBreak", 0.90f);
    public static void StageClear() => Play("Jingle_StageClear", 0.95f);
    public static void AllClear() => Play("Jingle_AllClear", 1.0f);

    /// <summary>
    /// Resources/SFX 에 외부 오디오 에셋이 아직 없는 항목을 위해
    /// 픽셀아트 플랫포머 감성에 맞는 깔끔한 신디사이즈 AudioClip을 런타임 생성한다.
    /// </summary>
    private static AudioClip SynthesizeFallbackClip(string name)
    {
        const int sr = 44100;
        switch (name)
        {
            case "Jump":
                // 경쾌한 상승 주파수 스윕 (240Hz -> 580Hz, 0.14s)
                return CreateSweepClip(name, sr, 0.14f, 240f, 580f, 0.45f, WaveType.SquareSoft);

            case "Land":
                // 부드러운 착지 톡 소리 (130Hz -> 55Hz + 약한 노이즈, 0.08s)
                return CreateSweepClip(name, sr, 0.08f, 130f, 55f, 0.40f, WaveType.Triangle, 0.25f);

            case "FoxDash":
                // 바람을 가르는 대시 후시 + 어택 (480Hz -> 220Hz + 화이트노이즈 필터, 0.18s)
                return CreateSweepClip(name, sr, 0.18f, 520f, 200f, 0.48f, WaveType.Triangle, 0.55f);

            case "FoxSniff":
                // 코로 킁킁! 2연속 짧은 호흡음 + 탐지 반짝임 차임 (0.34s)
                return CreateSniffClip(name, sr);

            case "FoxSwap":
                // 공간 이동 포털 워프음 (300Hz -> 920Hz -> 520Hz 모듈레이션, 0.28s)
                return CreateSwapClip(name, sr);

            case "FoxShadow":
                // 신비로운 그림자 분신 소환음 (392Hz -> 293Hz -> 587Hz 화음, 0.26s)
                return CreateArpeggioClip(name, sr, new float[] { 392f, 493.88f, 587.33f }, 0.085f, 0.45f, WaveType.Sine);

            case "RabbitCarrot":
                // 통통 튀는 당근 발사음 (320Hz -> 680Hz, 0.12s)
                return CreateSweepClip(name, sr, 0.12f, 320f, 680f, 0.48f, WaveType.Triangle, 0.15f);

            case "CarrotStick":
                // 당근이 벽에 촥 박혀 발판이 되는 단단한 타격음 (180Hz -> 90Hz + 360Hz, 0.13s)
                return CreateSweepClip(name, sr, 0.13f, 210f, 85f, 0.55f, WaveType.SquareSoft, 0.35f);

            case "RabbitJump":
                // 슈퍼 점프 스프링 도약음 (220Hz -> 880Hz, 0.22s)
                return CreateSweepClip(name, sr, 0.22f, 220f, 880f, 0.52f, WaveType.SquareSoft);

            case "RabbitStomp":
                // 공중 급강하 착지 쿵! 헤비 임팩트 (140Hz -> 38Hz + 강한 노이즈 버스트, 0.26s)
                return CreateSweepClip(name, sr, 0.26f, 150f, 36f, 0.65f, WaveType.SquareSoft, 0.60f);

            case "RabbitPowerUp":
                // 파워업 황금 버프 상승 팡파르 아르페지오 (C5 - E5 - G5 - C6)
                return CreateArpeggioClip(name, sr, new float[] { 523.25f, 659.25f, 783.99f, 1046.50f }, 0.065f, 0.52f, WaveType.SquareSoft);

            case "Death":
                // 함정/낙사 사망 시 하강 멜로디 (440Hz -> 392Hz -> 330Hz -> 220Hz)
                return CreateArpeggioClip(name, sr, new float[] { 440f, 369.99f, 311.13f, 207.65f }, 0.075f, 0.50f, WaveType.SquareSoft);

            case "Respawn":
                // 리스폰 부활 차임 (392Hz -> 523.25Hz -> 659.25Hz)
                return CreateArpeggioClip(name, sr, new float[] { 392f, 523.25f, 659.25f }, 0.065f, 0.42f, WaveType.Triangle);

            case "Coin":
                // 구슬 조각 획득 맑은 2음 벨 (B5 987.77Hz -> E6 1318.51Hz)
                return CreateCoinClip(name, sr);

            case "LeverOn":
                // 레버 ON 철컥 + 기믹 활성화 (280Hz -> 560Hz)
                return CreateArpeggioClip(name, sr, new float[] { 260f, 520f }, 0.065f, 0.50f, WaveType.SquareSoft);

            case "LeverOff":
                // 레버 OFF 철컥 (520Hz -> 260Hz)
                return CreateArpeggioClip(name, sr, new float[] { 520f, 260f }, 0.065f, 0.45f, WaveType.SquareSoft);

            case "CheckpointHalf":
                // 체크포인트 1/2 첫 도착 알림음 (G5 784Hz)
                return CreateArpeggioClip(name, sr, new float[] { 587.33f, 783.99f }, 0.07f, 0.45f, WaveType.Triangle);

            case "CheckpointFull":
                // 체크포인트 2/2 전원 도착 완료 화음 (C5 - E5 - G5 - B5 - C6)
                return CreateArpeggioClip(name, sr, new float[] { 523.25f, 659.25f, 783.99f, 1046.50f }, 0.075f, 0.55f, WaveType.Triangle);

            case "BlockBreak":
                // 금 간 바닥 파괴음
                return CreateSweepClip(name, sr, 0.22f, 190f, 48f, 0.60f, WaveType.SquareSoft, 0.70f);

            default:
                return null;
        }
    }

    private enum WaveType { Sine, Triangle, SquareSoft }

    private static float SampleWave(WaveType type, float phase)
    {
        float p = phase - Mathf.Floor(phase);
        switch (type)
        {
            case WaveType.Triangle:
                return 1f - 4f * Mathf.Abs(Mathf.Round(p - 0.25f) - (p - 0.25f));
            case WaveType.SquareSoft:
                return Mathf.Clamp(Mathf.Sin(p * Mathf.PI * 2f) * 2.2f, -0.85f, 0.85f);
            default:
                return Mathf.Sin(p * Mathf.PI * 2f);
        }
    }

    private static AudioClip CreateSweepClip(string name, int sr, float duration, float fStart, float fEnd, float gain, WaveType wave, float noiseMix = 0f)
    {
        int samples = Mathf.Max(64, Mathf.RoundToInt(sr * duration));
        float[] data = new float[samples];
        float phase = 0f;
        uint rng = 1234567u;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float freq = Mathf.Lerp(fStart, fEnd, t * t);
            phase += freq / sr;

            float env = (t < 0.08f) ? (t / 0.08f) : Mathf.Pow(1f - t, 1.35f);
            float tone = SampleWave(wave, phase);
            if (noiseMix > 0f)
            {
                rng = rng * 1664525u + 1013904223u;
                float nz = ((rng & 0xFFFF) / 32768f) - 1f;
                tone = Mathf.Lerp(tone, nz, noiseMix);
            }
            data[i] = tone * env * gain;
        }

        var clip = AudioClip.Create("SFX_" + name, samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateArpeggioClip(string name, int sr, float[] notes, float noteDuration, float gain, WaveType wave)
    {
        int noteSamples = Mathf.Max(64, Mathf.RoundToInt(sr * noteDuration));
        int totalSamples = noteSamples * notes.Length;
        float[] data = new float[totalSamples];
        float phase = 0f;

        for (int n = 0; n < notes.Length; n++)
        {
            float freq = notes[n];
            for (int i = 0; i < noteSamples; i++)
            {
                float t = (float)i / noteSamples;
                phase += freq / sr;
                float env = (t < 0.1f) ? (t / 0.1f) : Mathf.Pow(1f - t, 0.85f);
                data[n * noteSamples + i] = SampleWave(wave, phase) * env * gain;
            }
        }

        var clip = AudioClip.Create("SFX_" + name, totalSamples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateCoinClip(string name, int sr)
    {
        float dur = 0.26f;
        int samples = Mathf.RoundToInt(sr * dur);
        float[] data = new float[samples];
        float phase = 0f;
        int split = Mathf.RoundToInt(samples * 0.28f);

        for (int i = 0; i < samples; i++)
        {
            float freq = (i < split) ? 987.77f : 1318.51f; // B5 -> E6
            phase += freq / sr;
            float t = (float)i / samples;
            float env = (t < 0.04f) ? (t / 0.04f) : Mathf.Pow(1f - t, 1.5f);
            data[i] = (Mathf.Sin(phase * Mathf.PI * 2f) * 0.75f + SampleWave(WaveType.Triangle, phase * 2f) * 0.25f) * env * 0.52f;
        }

        var clip = AudioClip.Create("SFX_" + name, samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateSniffClip(string name, int sr)
    {
        float dur = 0.32f;
        int samples = Mathf.RoundToInt(sr * dur);
        float[] data = new float[samples];
        float phase = 0f;
        uint rng = 9876543u;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            rng = rng * 1664525u + 1013904223u;
            float nz = ((rng & 0xFFFF) / 32768f) - 1f;

            if (t < 0.22f || (t >= 0.28f && t < 0.50f))
            {
                // 짧은 코 호흡("킁-킁!")
                float localT = (t < 0.22f) ? (t / 0.22f) : ((t - 0.28f) / 0.22f);
                float env = Mathf.Sin(localT * Mathf.PI);
                phase += Mathf.Lerp(400f, 750f, localT) / sr;
                data[i] = (nz * 0.55f + Mathf.Sin(phase * Mathf.PI * 2f) * 0.45f) * env * 0.45f;
            }
            else if (t >= 0.54f)
            {
                // 탐지 반짝임 차임
                float localT = (t - 0.54f) / 0.46f;
                float env = Mathf.Pow(1f - localT, 1.4f);
                phase += Mathf.Lerp(880f, 1174.66f, localT) / sr;
                data[i] = Mathf.Sin(phase * Mathf.PI * 2f) * env * 0.42f;
            }
        }

        var clip = AudioClip.Create("SFX_" + name, samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateSwapClip(string name, int sr)
    {
        float dur = 0.26f;
        int samples = Mathf.RoundToInt(sr * dur);
        float[] data = new float[samples];
        float phase1 = 0f, phase2 = 0f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float f1 = Mathf.Lerp(280f, 860f, t);
            float f2 = Mathf.Lerp(860f, 280f, t);
            phase1 += f1 / sr;
            phase2 += f2 / sr;
            float env = Mathf.Sin(t * Mathf.PI);
            data[i] = (SampleWave(WaveType.Triangle, phase1) * 0.5f + SampleWave(WaveType.Sine, phase2) * 0.5f) * env * 0.52f;
        }

        var clip = AudioClip.Create("SFX_" + name, samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }
}

