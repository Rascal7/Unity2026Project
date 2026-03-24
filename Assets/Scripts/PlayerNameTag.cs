using Fusion;
using TMPro;
using UnityEngine;

public class PlayerNameTag : NetworkBehaviour
{
    // [Networked] 변수가 변하면 OnNameChanged를 실행하라는 뜻
    [Networked, OnChangedRender(nameof(OnNameChanged))]
    public string Nickname { get; set; }

    public TextMeshProUGUI nameText;

    public override void Spawned()
    {
        // 모든 클라이언트에서 일단 현재 데이터로 텍스트 업데이트
        OnNameChanged();

        if (Object.HasInputAuthority)
        {
            // 1. 저장된 이름을 정확한 키값("PlayerName")으로 가져옵니다.
            string myName = PlayerPrefs.GetString("PlayerName", "Unknown");
            Debug.Log($"<color=lime>[NameTag]</color> 내 이름은 {myName}입니다. 서버에 알립니다.");

            // 2. 서버에 내 이름을 전송
            RPC_SetNickname(myName);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetNickname(string name)
    {
        // 서버가 변수를 바꾸면 모든 클라이언트의 OnNameChanged가 실행됨
        Nickname = name;
    }
    // Fusion 2에서는 'static'과 'Changed<T>' 대신 이렇게 직접 참조가 가능합니다.
    public void OnNameChanged()
    {
        if (nameText != null && !string.IsNullOrEmpty(Nickname))
        {
            nameText.text = Nickname;

            nameText.ForceMeshUpdate();
        }
    }

    // 이름표가 카메라를 보게 만드는 코드 (성능을 위해 LateUpdate 권장)
    private void LateUpdate()
    {
        if (Camera.main != null && nameText != null)
        {
            nameText.transform.rotation = Camera.main.transform.rotation;
        }
    }
}