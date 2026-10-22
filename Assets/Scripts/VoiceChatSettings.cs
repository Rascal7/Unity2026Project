using System;
using UnityEngine;

/// <summary>
/// 인게임 음성 채팅(Photon Voice) 설정 저장/조회 전역 헬퍼.
/// - 상시 음성(Always / Open Mic) vs 푸시투톡(Push-to-Talk) 모드 지원
/// - 푸시투톡 키 매핑 (기본값: KeyCode.F)
/// - 음성 채팅 볼륨 및 마이크 음소거 설정
/// - PlayerPrefs에 저장되어 재접속 시에도 유지
/// </summary>
public static class VoiceChatSettings
{
    public enum TransmitMode
    {
        Always = 0,     // 상시 음성 송출 (기본값, 음성 감지 VAD)
        PushToTalk = 1  // 푸시투톡 (키를 누르고 있을 때만 송출)
    }

    public static event Action OnSettingsChanged;

    private const string PrefMode = "Voice_TransmitMode";
    private const string PrefKey = "Voice_PushToTalkKey";
    private const string PrefMute = "Voice_IsMuted";
    private const string PrefVolume = "Voice_Volume";

    public static TransmitMode Mode
    {
        get => (TransmitMode)PlayerPrefs.GetInt(PrefMode, (int)TransmitMode.Always);
        set
        {
            PlayerPrefs.SetInt(PrefMode, (int)value);
            PlayerPrefs.Save();
            OnSettingsChanged?.Invoke();
        }
    }

    public static KeyCode PushToTalkKey
    {
        get => (KeyCode)PlayerPrefs.GetInt(PrefKey, (int)KeyCode.F);
        set
        {
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
            OnSettingsChanged?.Invoke();
        }
    }

    public static bool IsMuted
    {
        get => PlayerPrefs.GetInt(PrefMute, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(PrefMute, value ? 1 : 0);
            PlayerPrefs.Save();
            OnSettingsChanged?.Invoke();
        }
    }

    public static float VoiceVolume
    {
        get => PlayerPrefs.GetFloat(PrefVolume, 1.0f);
        set
        {
            PlayerPrefs.SetFloat(PrefVolume, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            OnSettingsChanged?.Invoke();
        }
    }

    /// <summary>
    /// 현재 음성 송출이 허용된 상태인지 여부 (푸시투톡 키 입력 및 음소거 반영).
    /// Photon Voice Recorder의 TransmitEnabled 제어에 사용됩니다.
    /// </summary>
    public static bool IsTransmitAllowed()
    {
        if (IsMuted) return false;
        if (Mode == TransmitMode.Always) return true;
        return Input.GetKey(PushToTalkKey);
    }

    public static void ResetToDefaults()
    {
        Mode = TransmitMode.Always;
        PushToTalkKey = KeyCode.F;
        IsMuted = false;
        VoiceVolume = 1.0f;
        OnSettingsChanged?.Invoke();
    }
}
