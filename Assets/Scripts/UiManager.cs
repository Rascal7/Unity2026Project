using UnityEngine;

public class UIManager : MonoBehaviour
{
    // (구형) 씬에 배치된 RankingBoard 패널. 이제 코드 생성 보드(RankingBoardUI)를 쓰므로 사용하지 않는다.
    public GameObject rankingBoard;

    void Start()
    {
        if (rankingBoard != null) rankingBoard.SetActive(false);
    }

    // 트로피(랭킹) 버튼에 연결
    public void ToggleRanking()
    {
        if (rankingBoard != null) rankingBoard.SetActive(false); // 구형 패널은 항상 꺼둔다

        var rm = GetComponent<RankingManager>();
        if (rm != null) rm.ToggleBoard();
    }

    // X 버튼에 연결
    public void CloseRanking()
    {
        var rm = GetComponent<RankingManager>();
        if (rm != null) rm.HideBoard();
        if (rankingBoard != null) rankingBoard.SetActive(false);
    }
}
