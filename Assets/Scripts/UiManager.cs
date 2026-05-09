using UnityEngine;

public class UIManager : MonoBehaviour
{
    // 유니티에서 RankingBoard 패널을 여기에 드래그할 거예요.
    public GameObject rankingBoard;

    void Start()
    {
        // 처음 화면 랭킹판 비활성화   
        rankingBoard.SetActive(false);
    }

    // 트로피 버튼에 연결할 함수
    public void ToggleRanking()
    {
        // 1. 현재 랭킹판이 켜져 있는지(activeSelf) 확인합니다.
        bool isActive = rankingBoard.activeSelf;

        if (isActive)
        {
            // 이미 켜져 있다면? -> 끈다!
            rankingBoard.SetActive(false);
        }
        else
        {
            // 꺼져 있다면? -> 켜고 데이터를 새로고침한다!
            rankingBoard.SetActive(true);
            GetComponent<RankingManager>().RequestRanking();
        }
    }

    // X 버튼에 연결할 함수
    public void CloseRanking()
    {
        rankingBoard.SetActive(false);
    }
}