using Fusion;
using TMPro;
using UnityEngine;

public class PlayerNameTag : NetworkBehaviour
{
    // [Networked]  ϸ OnNameChanged ϶ 
    [Networked, OnChangedRender(nameof(OnNameChanged))]
    public string Nickname { get; set; }

    public TextMeshProUGUI nameText;

    public override void Spawned()
    {
        //  Ŭ̾Ʈ ϴ  ͷ ؽƮ Ʈ
        OnNameChanged();

        if (Object.HasInputAuthority)
        {
            // 1.  ̸ Ȯ Ű("PlayerName") ɴϴ.
            string myName = PlayerPrefs.GetString("PlayerName", "Unknown");
            Debug.Log($"<color=lime>[NameTag]</color>  ̸ {myName}Դϴ.  ˸ϴ.");

            // 2.   ̸ 
            RPC_SetNickname(myName);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetNickname(string name)
    {
        //   ٲٸ  Ŭ̾Ʈ OnNameChanged 
        Nickname = name;
    }
    // Fusion 2 'static' 'Changed<T>'  ̷   մϴ.
    public void OnNameChanged()
    {
        if (nameText != null && !string.IsNullOrEmpty(Nickname))
        {
            nameText.text = Nickname;

            nameText.ForceMeshUpdate();
        }
    }

    // ̸ǥ ī޶   ڵ (  LateUpdate )
    private void LateUpdate()
    {
        if (Camera.main != null && nameText != null)
        {
            nameText.transform.rotation = Camera.main.transform.rotation;
        }
    }

    public string GetResolvedName()
    {
        if (!string.IsNullOrEmpty(Nickname)) return Nickname;
        if (nameText != null && !string.IsNullOrEmpty(nameText.text)) return nameText.text;
        return null;
    }
}