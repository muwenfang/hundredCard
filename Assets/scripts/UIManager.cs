using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 界面管理器。挂在场景里的常驻物体上（示例挂在 GameRoot 上，与 GameManager 同一个物体即可）。
///
/// 职责（对应文本中的 UIManager）：
///   1. 场景变化：mainMenu / gamePanel / endMenu / ranking 四个面板的互斥切换；
///   2. 分数显示：把 GameManager.score 写到 Text 上；
///   3. 结算画面与结束画面共用 endMenuPanel（都显示「标题 + 分数 + 明细」）；
///   4. 删卡阶段的界面：显示删卡投放区，并把「下一回合」按钮变红、禁用、改文字；
///   5. 结算逐组跳分：4 个组分数文本按索引 0→3 依次出现，最后显示总分，停 2 秒再弹出 endPanel。
///
/// 【你需要做的事】
/// 只需在 Inspector 里把这些 public 字段拖满：
///   面板   → mainMenuPanel / gamePanel / endMenuPanel / rankingPanel
///   文本   → scoreText / roundText / endMenuScoreText / endMenuDetailText
///   按钮   → 下面「按钮」一组的 5 个字段
///   删卡   → deletionPanels（删卡投放区，可拖多个）+ nextTurnButton（下一回合按钮，必拖）
///            nextTurnButtonLabel 留空即可，运行时会自动找按钮子物体里的 Text
///   跳分   → groupScoreTexts（**按第 1~4 组的顺序**拖入 4 个 Text）
///            settleTotalScoreText 可留空，留空就复用 scoreText 显示总分
/// 按钮的点击事件由代码在 Awake 里自动绑定（见 BindButtons），
/// 所以【不要】再在 Button 的 OnClick 列表里手动挂一遍，否则一次点击会被触发两次。
/// 如果你确实想手动挂，把 autoBindButtons 取消勾选即可。
///
/// 【结算跳分的完整流程】
/// 点「结算」→ GameManager.Settle 算分并清空槽位 → UIManager.PlaySettleSequence：
///   隐藏全部组分数文本 → 索引 0/1/2/3 逐个显示「组名 + 该组分数」（间隔 settleStepInterval 秒）
///   → 显示总分 → 停留 settleHoldAfterTotal 秒（默认 2 秒）→ 弹出 endPanel。
/// 动画期间 IsPlayingSettleSequence 为 true，GameManager 会挡住重复结算与推进回合。
/// 每组分数由 SlotManager 按「归因 + 分摊」算出，**四组之和恒等于总分**。
///
/// 【删卡阶段会做什么】
/// 抽卡之后进入删卡阶段：deletionPanels 显示出来、「下一回合」按钮变红并禁用
/// （文字变成「删除2张卡」）。玩家把卡拖进删卡区、删够 2 张之后，
/// 按钮自动还原成可点击状态，才能继续下一回合。详见 SetNextTurnButtonDeleteState。
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

    [Header("删卡阶段")]
    [Tooltip("删卡投放区，可以拖多个（场景里左右各一块就都拖进来，任一区域都算投中）。" +
             "卡牌拖进这些区域即被删除；平时隐藏，进入删卡阶段才显示")]
    public List<GameObject> deletionPanels = new List<GameObject>();

    [Tooltip("显示删卡区时，自动关掉它自身 Graphic 的射线，避免这块半透明区域挡住下面的卡（拖不出来）。" +
             "命中判定用的是矩形包含、不依赖射线，关掉不影响投放")]
    public bool disableDeletePanelRaycast = true;

    [Tooltip("「下一回合」按钮上的文字组件。留空会在运行时自动到按钮子物体里找 Text")]
    public Text nextTurnButtonLabel;

    [Tooltip("进入删卡阶段时按钮变成的颜色（红色）")]
    public Color deletePhaseButtonColor = new Color(0.85f, 0.29f, 0.26f, 1f);

    [Tooltip("进入删卡阶段时按钮上的文字。写 {0} 会被替换成需要删除的张数，例如「删除{0}张卡」→「删除2张卡」")]
    public string deletePhaseButtonText = "删除{0}张卡";

    [Tooltip("「下一回合」按钮平时的文字。留空 = 自动记录进入删卡阶段前按钮上原本的文字")]
    public string nextTurnNormalText = string.Empty;


    [Header("结算逐组跳分")]
    [Tooltip("每一组的分数文本，按索引 0 → 3 依次显示（本工程 4 个：3 个数列组 + 1 个雀头组）。" +
             "顺序必须与 SlotManager.slotGroups 的顺序一致。平时会被自动隐藏，只在结算动画时逐个出现")]
    public List<Text> groupScoreTexts = new List<Text>();

    [Tooltip("组分数文本的格式：{0} = 组名，{1} = 该组分数。例如「{0}  +{1}」→「第 1 组  +24」")]
    public string groupScoreFormat = "{0}  +{1}";

    [Tooltip("总分文本。留空则复用上面的 scoreText（显示「分数：X」）")]
    public Text settleTotalScoreText;

    [Tooltip("总分文本的格式：{0} = 总分。仅当 settleTotalScoreText 拖了物体时才会用到")]
    public string settleTotalFormat = "总分：{0}";

    [Tooltip("相邻两组出现之间的间隔（秒）")]
    public float settleStepInterval = 0.6f;

    [Tooltip("显示总分之后停留多久再弹出 endPanel（秒）")]
    public float settleHoldAfterTotal = 2f;


    [Header("开关")]
    [Tooltip("勾选后由代码自动绑定上面按钮的点击事件；" +
             "若你想自己在 Inspector 里挂 OnClick，请取消勾选，避免一次点击触发两次")]
    public bool autoBindButtons = true;

    /// <summary>「下一回合」按钮进入删卡阶段前的配色快照，退出阶段时照它还原。</summary>
    private ColorBlock normalNextTurnColors;

    /// <summary>是否已经存过配色快照（只存一次，多次进出删卡阶段也还原得回去）。</summary>
    private bool nextTurnColorsCached;

    /// <summary>「下一回合」按钮原本的文字，退出删卡阶段时还原。</summary>
    private string cachedNormalLabel;

    /// <summary>正在播放的结算跳分协程（重入时先停掉旧的）。</summary>
    private Coroutine settleRoutine;

    /// <summary>
    /// 是否正在播放结算跳分动画。
    /// GameManager 用它挡住动画期间重复点「结算 / 下一回合」——
    /// 这段时间槽位里的卡已经销毁，再结算只会把总分冲乱。
    /// </summary>
    public bool IsPlayingSettleSequence { get; private set; }

    /// <summary>动画开始前「结算 / 下一回合」按钮的可点击状态，播完照原样还原。</summary>
    private bool settleButtonWasInteractable;
    private bool nextTurnButtonWasInteractable;
    private bool buttonStatesCached;

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

    /// <summary>初始化界面：只显示 mainMenu，并确保删卡区与逐组分数字文本都是隐藏的。</summary>
    public void InitializeUi()
    {
        SetDeletePanelVisible(false);
        HideGroupScoreTexts();
        HideDedicatedTotalText();
        ShowMainMenu();
    }

    /// <summary>显示主菜单。</summary>
    public void ShowMainMenu()
    {
        SetPanelsActive(showMainMenu: true);
    }

    /// <summary>
    /// 显示游戏面板。
    /// 顺手把逐组分数字文本收起来 —— 它们只在结算跳分时逐个出现，平时不该留在屏幕上。
    /// </summary>
    public void ShowGamePanel()
    {
        SetPanelsActive(showGame: true);
        HideGroupScoreTexts();
        HideDedicatedTotalText();
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

    // ------------------------------------------------------------------
    // 结算逐组跳分：每组分数依次显示 → 最后显示总分 → 停留 → 弹出 endPanel
    // ------------------------------------------------------------------

    /// <summary>
    /// 播放结算跳分。流程（对应需求）：
    ///   1. 先隐藏全部组分数文本；
    ///   2. 按索引 0 → N-1 **依次显示**「组名 + 该组分数」，每组之间间隔 settleStepInterval 秒；
    ///   3. 各组显示完后，**最后再显示总分**（settleTotalScoreText，未拖则复用 scoreText）；
    ///   4. 停留 settleHoldAfterTotal 秒（默认 2 秒）后弹出 endMenuPanel。
    ///
    /// 用 WaitForSecondsRealtime 而不是 WaitForSeconds：
    /// 以后若把 timeScale 设为 0 做暂停，这段动画也不会被卡住。
    ///
    /// 动画期间 IsPlayingSettleSequence 为 true，GameManager 会据此挡住重复结算 / 推进回合。
    /// </summary>
    /// <param name="groupScores">每组的分数，索引与 SlotManager.slotGroups 一致。</param>
    /// <param name="groupNames">每组的显示名，可为 null。</param>
    /// <param name="total">总分（= 各组分数之和）。</param>
    /// <param name="detail">endPanel 上的明细文本。</param>
    public void PlaySettleSequence(List<int> groupScores, List<string> groupNames, int total, string detail)
    {
        if (!isActiveAndEnabled)
        {
            // 组件或物体没启用时起不了协程 —— 直接出结算画面，绝不让玩家看不到结果
            ShowSettleTotalText(total);
            ShowEndMenu(total, detail);
            return;
        }

        if (settleRoutine != null) StopCoroutine(settleRoutine);
        settleRoutine = StartCoroutine(SettleSequenceRoutine(groupScores, groupNames, total, detail));
    }

    /// <summary>跳分动画的协程本体。</summary>
    private IEnumerator SettleSequenceRoutine(List<int> groupScores, List<string> groupNames, int total, string detail)
    {
        IsPlayingSettleSequence = true;
        LockFlowButtons(true);

        // 先全部藏起来，这样才谈得上「依次显示」
        HideGroupScoreTexts();
        HideDedicatedTotalText();

        // 文本数量与分数数量取小：拖漏了或拖多了都不会越界
        int steps = 0;
        if (groupScoreTexts != null && groupScores != null)
        {
            steps = Mathf.Min(groupScoreTexts.Count, groupScores.Count);
        }

        for (int i = 0; i < steps; i++)
        {
            Text label = groupScoreTexts[i];
            if (label != null)
            {
                string name = (groupNames != null && i < groupNames.Count && !string.IsNullOrEmpty(groupNames[i]))
                    ? groupNames[i]
                    : ("第 " + (i + 1) + " 组");

                label.text = FormatGroupScore(groupScoreFormat, name, groupScores[i]);
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            }

            if (settleStepInterval > 0f) yield return new WaitForSecondsRealtime(settleStepInterval);
        }

        // 各组都显示完了，最后才显示总分
        ShowSettleTotalText(total);

        if (settleHoldAfterTotal > 0f) yield return new WaitForSecondsRealtime(settleHoldAfterTotal);

        LockFlowButtons(false);
        IsPlayingSettleSequence = false;
        settleRoutine = null;

        ShowEndMenu(total, detail);
    }

    /// <summary>
    /// 隐藏全部组分数文本（开局、返回游戏面板、跳分动画开始时调用）。
    /// 用 SetActive 而不是把字号调 0：Hierarchy 里能直观看到「现在是收起来的」。
    /// </summary>
    public void HideGroupScoreTexts()
    {
        if (groupScoreTexts == null) return;

        for (int i = 0; i < groupScoreTexts.Count; i++)
        {
            Text label = groupScoreTexts[i];
            if (label == null) continue;

            label.text = string.Empty;
            if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 隐藏「专用总分文本」。
    /// 若没拖专用文本（总分复用常驻的 scoreText），就什么都不做 —— scoreText 本来就要一直显示。
    /// </summary>
    private void HideDedicatedTotalText()
    {
        if (settleTotalScoreText == null || settleTotalScoreText == scoreText) return;

        settleTotalScoreText.text = string.Empty;
        if (settleTotalScoreText.gameObject.activeSelf) settleTotalScoreText.gameObject.SetActive(false);
    }

    /// <summary>
    /// 显示总分：拖了 settleTotalScoreText 就写它（按 settleTotalFormat），
    /// 否则复用 scoreText 的「分数：X」格式，与游戏面板上原本的显示保持一致。
    /// </summary>
    private void ShowSettleTotalText(int total)
    {
        Text target = settleTotalScoreText != null ? settleTotalScoreText : scoreText;
        if (target == null) return;

        if (target == scoreText)
        {
            UpdateScore(total);
        }
        else
        {
            target.text = FormatTotalScore(settleTotalFormat, total);
        }

        if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);
    }

    /// <summary>
    /// 组装一组的分数文本。用 Replace 而不是 string.Format：
    /// 玩家在格式串里多写了花括号也不会抛 FormatException（与 FormatDeleteLabel 同一套做法）。
    /// 做成 public 静态方法，方便脱离运行时直接验证文案。
    /// </summary>
    public static string FormatGroupScore(string template, string groupName, int points)
    {
        string text = string.IsNullOrEmpty(template) ? "{0}  +{1}" : template;
        return text.Replace("{0}", groupName ?? string.Empty).Replace("{1}", points.ToString());
    }

    /// <summary>组装总分文本（同样是 Replace，格式串写错也不会抛异常）。</summary>
    public static string FormatTotalScore(string template, int total)
    {
        string text = string.IsNullOrEmpty(template) ? "总分：{0}" : template;
        return text.Replace("{0}", total.ToString());
    }

    /// <summary>
    /// 动画期间锁住「结算 / 下一回合」按钮，避免玩家在这两三秒里重复结算或改动盘面。
    /// 只改 interactable，播完**按动画前的原值**还原，所以不会破坏删卡阶段的禁用状态。
    /// </summary>
    private void LockFlowButtons(bool locked)
    {
        if (locked)
        {
            settleButtonWasInteractable = settleButton != null && settleButton.interactable;
            nextTurnButtonWasInteractable = nextTurnButton != null && nextTurnButton.interactable;
            buttonStatesCached = true;

            if (settleButton != null) settleButton.interactable = false;
            if (nextTurnButton != null) nextTurnButton.interactable = false;
            return;
        }

        if (!buttonStatesCached) return;
        buttonStatesCached = false;

        if (settleButton != null) settleButton.interactable = settleButtonWasInteractable;
        if (nextTurnButton != null) nextTurnButton.interactable = nextTurnButtonWasInteractable;
    }

    /// <summary>
    /// 物体被关掉时 Unity 会强制中止协程，这里把状态复位 ——
    /// 否则 IsPlayingSettleSequence 会永久停在 true，把结算与推进回合全部挡死。
    /// </summary>
    private void OnDisable()
    {
        if (!IsPlayingSettleSequence) return;

        IsPlayingSettleSequence = false;
        settleRoutine = null;
        LockFlowButtons(false);
    }

    /// <summary>游戏结束（对外保留的旧入口，统一走结算/结束画面）。</summary>
    public void GameOver()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.GameOver();
        }
    }

    // ------------------------------------------------------------------
    // 删卡阶段（抽卡后强制删卡）
    // ------------------------------------------------------------------

    /// <summary>
    /// 显示 / 隐藏删卡投放区（支持配置多个区域，一起显示或一起隐藏）。
    /// 未进入删卡阶段时它们必须是隐藏的 —— 隐藏后 IsOverDeletePanel 也会返回 false，
    /// 所以玩家在非删卡阶段把卡拖到这个位置不会被误删。
    /// </summary>
    public void SetDeletePanelVisible(bool visible)
    {
        if (deletionPanels == null) return;

        for (int i = 0; i < deletionPanels.Count; i++)
        {
            GameObject panel = deletionPanels[i];
            if (panel == null) continue;

            if (panel.activeSelf != visible) panel.SetActive(visible);

            if (visible && disableDeletePanelRaycast) DisableRaycast(panel);
        }
    }

    /// <summary>
    /// 关掉删卡区自身所有 Graphic 的射线。
    /// 【为什么需要】删卡区是一块半透明的 Image、且铺得比较大，开着射线会挡住它下面的卡：
    /// 玩家想抓起被盖住的那张卡时，射线打到的是删卡区而不是卡牌，卡就「拖不动」了。
    /// 投放判定走的是矩形包含（见 IsOverDeletePanel），完全不依赖射线，所以关掉没有任何副作用。
    /// </summary>
    private static void DisableRaycast(GameObject panel)
    {
        Graphic[] graphics = panel.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null) graphics[i].raycastTarget = false;
        }
    }

    /// <summary>
    /// 屏幕点是否落在任意一个删卡投放区内。
    /// 和槽位判定一样用「矩形包含」，所以删卡区不需要挂 Image，也不需要射线。
    /// </summary>
    public bool IsOverDeletePanel(Vector2 screenPoint, Camera eventCamera)
    {
        if (deletionPanels == null) return false;

        for (int i = 0; i < deletionPanels.Count; i++)
        {
            GameObject panel = deletionPanels[i];
            if (panel == null) continue;
            if (!panel.activeInHierarchy) continue;      // 隐藏状态下不接受投放

            RectTransform rect = panel.transform as RectTransform;
            if (rect == null) continue;

            if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, eventCamera))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 切换「下一回合」按钮的删卡状态。
    ///   deleting = true  → 变红 + 文字改成「删除2张卡」+ 不可点击（interactable = false）
    ///   deleting = false → 还原成进入删卡阶段前的配色、文字与可点击状态
    /// 进入删卡阶段时会把按钮原本的配色与文字存一份快照，退出时照快照还原，
    /// 所以不会把你在 Inspector 里调好的样式改坏。
    /// </summary>
    public void SetNextTurnButtonDeleteState(bool deleting, int deleteCount)
    {
        if (nextTurnButton == null) return;

        EnsureNextTurnLabel();

        if (!deleting)
        {
            RestoreNextTurnNormalState();
            return;
        }

        CacheNextTurnNormalState();

        // 把四种状态的显示色全改成红色：按下按钮期间它一直是 disabled 状态，
        // 所以必须同时改 disabledColor，红色才真的会出现。
        ColorBlock red = normalNextTurnColors;
        red.normalColor = deletePhaseButtonColor;
        red.highlightedColor = deletePhaseButtonColor;
        red.pressedColor = deletePhaseButtonColor;
        red.selectedColor = deletePhaseButtonColor;
        red.disabledColor = deletePhaseButtonColor;
        red.colorMultiplier = 1f;
        nextTurnButton.colors = red;

        if (nextTurnButtonLabel != null) nextTurnButtonLabel.text = FormatDeleteLabel(deletePhaseButtonText, deleteCount);

        // 置为不可点击：既拦住玩家的点击，也让按钮切到 Disabled 状态、立刻用上上面的红色
        nextTurnButton.interactable = false;
    }

    /// <summary>
    /// 把「删除{0}张卡」这类模板里的 {0} 换成张数；没写占位符就原样使用。
    /// 做成 public 静态方法，方便脱离运行时直接验证文案。
    /// 用 Replace 而不是 string.Format：玩家若在文字里写了花括号也不会抛 FormatException。
    /// </summary>
    public static string FormatDeleteLabel(string template, int deleteCount)
    {
        if (string.IsNullOrEmpty(template)) return "删除" + deleteCount + "张卡";

        return template.Contains("{0}") ? template.Replace("{0}", deleteCount.ToString()) : template;
    }

    /// <summary>「下一回合」按钮上的文字组件没拖就自动到子物体里找。</summary>
    private void EnsureNextTurnLabel()
    {
        if (nextTurnButtonLabel == null && nextTurnButton != null)
        {
            nextTurnButtonLabel = nextTurnButton.GetComponentInChildren<Text>(true);
        }
    }

    /// <summary>记录进入删卡阶段前的按钮配色与文字（只记一次，之后进出多少次都用这一份）。</summary>
    private void CacheNextTurnNormalState()
    {
        if (nextTurnButton == null) return;

        if (!nextTurnColorsCached)
        {
            normalNextTurnColors = nextTurnButton.colors;
            nextTurnColorsCached = true;
        }

        if (cachedNormalLabel == null)
        {
            EnsureNextTurnLabel();
            cachedNormalLabel = !string.IsNullOrEmpty(nextTurnNormalText)
                ? nextTurnNormalText
                : (nextTurnButtonLabel != null ? nextTurnButtonLabel.text : string.Empty);
        }
    }

    /// <summary>把「下一回合」按钮还原成进入删卡阶段前的样子。</summary>
    private void RestoreNextTurnNormalState()
    {
        if (nextTurnButton == null) return;
        if (!nextTurnColorsCached) return;   // 从没进过删卡阶段，没有需要还原的东西

        nextTurnButton.colors = normalNextTurnColors;
        if (nextTurnButtonLabel != null && cachedNormalLabel != null)
        {
            nextTurnButtonLabel.text = cachedNormalLabel;
        }
        nextTurnButton.interactable = true;
    }

}
