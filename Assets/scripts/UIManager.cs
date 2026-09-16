using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 界面管理器。挂在场景里的常驻物体上（示例挂在 GameRoot 上，与 GameManager 同一个物体即可）。
///
/// 职责（对应文本中的 UIManager）：
///   1. 场景变化：mainMenu / gamePanel / endMenu / ranking 四个面板的互斥切换；
///   2. 分数显示：把 GameManager.score 写到 Text 上；
///   3. 结算画面与结束画面共用 endMenuPanel（都显示「标题 + 分数 + 明细」）。
///
/// 【你需要做的事】
/// 只需在 Inspector 里把这些 public 字段拖满：
///   面板   → mainMenuPanel / gamePanel / endMenuPanel / rankingPanel
///   文本   → scoreText / roundText / endMenuTitleText / endMenuScoreText / endMenuDetailText
///   按钮   → 下面「按钮」一组的 7 个字段
/// 按钮的点击事件由代码在 Awake 里自动绑定（见 BindButtons），
/// 所以【不要】再在 Button 的 OnClick 列表里手动挂一遍，否则一次点击会被触发两次。
/// 如果你确实想手动挂，把 autoBindButtons 取消勾选即可。
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager instance;

    [Header("场景面板（对应文本中的 mainMenu / gamePanel / endMenu / ranking）")]
    [Tooltip("主菜单面板")]
    public GameObject mainMenuPanel;

    [Tooltip("游戏面板")]
    public GameObject gamePanel;

    [Tooltip("结算 / 结束画面。文本里的「结算画面」和「结束画面」共用一个面板，靠标题文本区分")]
    public GameObject endMenuPanel;

    [Tooltip("排行榜面板（原文只列了成员，未描述具体内容，这里只保留显示/隐藏入口）")]
    public GameObject rankingPanel;

    [Header("游戏面板上的文本")]
    [Tooltip("分数显示")]
    public Text scoreText;

    [Tooltip("回合显示，形如 3 / 44")]
    public Text roundText;

    [Header("结算 / 结束画面上的文本")]
    [Tooltip("本次总分")]
    public Text endMenuScoreText;

    [Tooltip("明细，显示各组判定结果")]
    public Text endMenuDetailText;

    [Header("按钮（拖入即可，点击事件由代码自动绑定）")]
    [Tooltip("主菜单 → 开始游戏")]
    public Button startGameButton;

    [Tooltip("游戏面板 → 下一回合")]
    public Button nextTurnButton;

    [Tooltip("游戏面板 → 结算")]
    public Button settleButton;

    [Tooltip("结算画面 → 继续游戏")]
    public Button continueButton;

    [Tooltip("结算 / 结束画面 → 返回主菜单")]
    public Button returnToMainMenuButton;


    [Header("开关")]
    [Tooltip("勾选后由代码自动绑定上面按钮的点击事件；" +
             "若你想自己在 Inspector 里挂 OnClick，请取消勾选，避免一次点击触发两次")]
    public bool autoBindButtons = true;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Debug.LogWarning("[UIManager] 场景中存在多个 UIManager，已销毁重复的那个。");
            Destroy(gameObject);
            return;
        }

        if (autoBindButtons) BindButtons();
    }

    // ------------------------------------------------------------------
    // 按钮自动绑定（数据连接由代码完成，避免在 Inspector 里手工挂 OnClick）
    // ------------------------------------------------------------------

    /// <summary>
    /// 把按钮的点击事件接到对应的流程方法上。
    /// AddListener 加的是「运行时监听器」，不会写回预制体/场景；先 Remove 再 Add 是为了防止重复注册。
    /// </summary>
    private void BindButtons()
    {
        Bind(startGameButton, OnClickStartGame);
        Bind(nextTurnButton, OnClickNextTurn);
        Bind(settleButton, OnClickSettle);
        Bind(continueButton, OnClickContinue);
        Bind(returnToMainMenuButton, OnClickReturnToMainMenu);
    }

    /// <summary>给一个 Button 绑定回调；按钮为空时静默跳过。</summary>
    private static void Bind(Button button, UnityAction action)
    {
        if (button == null) return;
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void OnClickStartGame() { if (GameManager.Instance != null) GameManager.Instance.StartGame(); }
    private void OnClickNextTurn() { if (GameManager.Instance != null) GameManager.Instance.NextTurn(); }
    private void OnClickSettle() { if (GameManager.Instance != null) GameManager.Instance.Settle(); }
    private void OnClickContinue() { if (GameManager.Instance != null) GameManager.Instance.ContinueAfterSettlement(); }
    private void OnClickReturnToMainMenu() { if (GameManager.Instance != null) GameManager.Instance.ReturnToMainMenu(); }

    // ------------------------------------------------------------------
    // 场景变化
    // ------------------------------------------------------------------

    /// <summary>初始化界面：只显示 mainMenu。</summary>
    public void InitializeUi()
    {
        ShowMainMenu();
    }

    /// <summary>显示主菜单。</summary>
    public void ShowMainMenu()
    {
        SetPanelsActive(showMainMenu: true);
    }

    /// <summary>显示游戏面板。</summary>
    public void ShowGamePanel()
    {
        SetPanelsActive(showGame: true);
    }

    /// <summary>显示排行榜面板。</summary>
    public void ShowRanking()
    {
        SetPanelsActive(showRanking: true);
    }

    /// <summary>
    /// 显示结算 / 结束画面。
    /// </summary>
    /// <param name="title">标题，例如「结算」「回合结束」「游戏结束」</param>
    /// <param name="finalScore">要显示的分数</param>
    /// <param name="detail">明细文本，例如每组的判定结果</param>
    public void ShowEndMenu(int finalScore, string detail)
    {
        SetPanelsActive(showEndMenu: true);
        if (endMenuScoreText != null) endMenuScoreText.text = "得分：" + finalScore;
        if (endMenuDetailText != null) endMenuDetailText.text = detail ?? string.Empty;
    }

    /// <summary>四个面板互斥切换。只传需要打开的那一个。</summary>
    private void SetPanelsActive(
        bool showMainMenu = false,
        bool showGame = false,
        bool showEndMenu = false,
        bool showRanking = false)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(showMainMenu);
        if (gamePanel != null) gamePanel.SetActive(showGame);
        if (endMenuPanel != null) endMenuPanel.SetActive(showEndMenu);
        if (rankingPanel != null) rankingPanel.SetActive(showRanking);
    }

    // ------------------------------------------------------------------
    // 分数 / 回合显示
    // ------------------------------------------------------------------

    /// <summary>刷新分数显示。</summary>
    public void UpdateScore(int newScore)
    {
        if (scoreText != null) scoreText.text = "分数：" + newScore;
    }

    /// <summary>刷新回合显示。</summary>
    public void UpdateRound(int currentTurn, int limit)
    {
        if (roundText != null) roundText.text = "回合：" + currentTurn + " / " + limit;
    }

    /// <summary>游戏结束（对外保留的旧入口，统一走结算/结束画面）。</summary>
    public void GameOver()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.GameOver();
        }
    }

}
