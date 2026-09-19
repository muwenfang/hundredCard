using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 游戏主流程管理器（对应文本中的 gameManager）。
/// 挂在一个常驻物体上（示例场景中是 GameRoot），场景启动即自动初始化。
///
/// 主流程：
///   进入游戏 → InitializeGame() → 刷新排行榜 + 展示 mainMenu
///   点击开始游戏 → StartGame() → **先校验学号（0~300）** → 抽 14 张进手牌区（开局不删卡）
///   点击下一回合 → NextTurn() → 再抽 2 张 → 进入删卡阶段
///   删卡阶段   → 把 2 张卡拖进 deletionPanels → 删够之后按钮恢复，才能再点下一回合
///   点击结算   → Settle()   → 读槽位、判数列、加分、逐组跳分（牌留在盘面上展示）、
///                            动画播完才销毁槽位里的牌并弹出结算画面
///   44 回合内没有点击过结算 → 分数归零并结束
///   一局结束   → EndGame() / GameOver() → 把本局成绩写进 ScoreRecordStore（同一局只写一次）
///
/// 【学号环节】StartGame 之前必须先有一个合法学号：
///   1. 点「开始游戏」→ StartGame 发现还没确认学号 → 尝试直接从输入框取值校验；
///   2. 校验通过就直接开局（所以你把「确认」按钮接到 StartGame 也能跑通）；
///   3. 校验不通过就在提示文本上报错并弹出学号界面，等玩家改完再点「确认」。
///   如果 UIManager 上一个输入源都没拖，会自动跳过校验并给出警告 ——
///   即「忘了连线」不会把游戏卡死。
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("数据源")]
    [Tooltip("全部牌面定义。初始化时用 Resources.LoadAll 从 Assets/Resources/allCards 读取 100 个资产")]
    public List<NumberCardData> allCards = new List<NumberCardData>();

    [Tooltip("抽卡池。每个牌面初始放入 number（默认 2）份；抽走一张就从池里移除一项")]
    public List<NumberCardData> tempCardsPool = new List<NumberCardData>();

    [Header("场景引用")]
    [Tooltip("卡牌预制体，需挂有 CardUI + DragHandler")]
    public GameObject cardPrefab;

    [Tooltip("手牌区。把场景里手牌区的物体拖进来即可，抽到的卡都会挂到它下面")]
    public GameObject handCardArea;

    [Header("规则参数")]
    [Tooltip("开局抽卡数量")]
    public int initialDrawCount = 14;

    [Tooltip("每回合抽卡数量")]
    public int drawPerTurn = 2;

    [Tooltip("回合上限：44 回合内没有点击结算则得分归零")]
    public int turnLimit = 44;

    [Tooltip("每次点击「下一回合」抽卡后，必须删掉的卡牌数量（有且仅能删这么多）")]
    public int deleteCountPerTurn = 2;

    [Header("运行时状态（只读）")]
    [Tooltip("当前累计得分")]
    public int score;

    [Tooltip("当前回合数，开局抽 14 张后记为第 1 回合")]
    public int turnCount;

    [Tooltip("本局是否点击过结算")]
    public bool hasSettled;

    [Tooltip("是否正处于「必须先删掉 N 张卡」的阶段。此阶段「下一回合」按钮被禁用")]
    public bool isDeletePhase;

    [Tooltip("本次删卡阶段已经删掉的张数")]
    public int deletedCount;

    [Tooltip("本局玩家的学号。-1 表示还没输入过")]
    public int studentId = -1;

    [Tooltip("本局学号是否已经确认过（校验通过才会置 true）")]
    public bool studentIdConfirmed;

    [Tooltip("最近一次学号校验失败的原因；校验通过后清空（只读，便于排查）")]
    public string lastStudentIdError;

    [Header("学号与成绩记录（参数）")]
    [Tooltip("是否强制「先输学号再开始游戏」：开启后 StartGame 会先校验学号。" +
             "若 UIManager 上连一个输入源都没拖，会自动跳过校验并给出警告 —— 忘记连线不会把游戏卡死")]
    public bool requireStudentId = true;

    [Tooltip("学号允许的最小值（含）")]
    public int studentIdMin = 0;

    [Tooltip("学号允许的最大值（含）")]
    public int studentIdMax = 300;

    [Tooltip("是否每次回到主菜单都重新要求输学号。" +
             "勾上 = 换一名学生就重输一次；不勾 = 同一次运行内沿用上一次确认过的学号")]
    public bool askStudentIdEveryGame = true;

    [Tooltip("一局结束时是否自动写一条记录（学号 / 平均分 / 最高得分 / 游玩局数）。" +
             "按学号聚合：游玩局数累加、最高得分取历史最高、平均分 = 累计总分 ÷ 局数")]
    public bool recordResultOnGameEnd = true;

    /// <summary>本局是否已经写过记录（防止 EndGame 与 GameOver 被先后调用时把同一局记两次）。</summary>
    private bool resultRecordedThisGame;

    /// <summary>
    /// 当前是否有「一局」正在进行：StartGame 之后置 true，结束一局 / 回到主菜单后置 false。
    ///
    /// 【为什么需要这个开关】记录只能在「一局结束」时写一次，而 InitializeGame() 在游戏刚启动
    /// （Start() 里）也会被调用一次 —— 那一次并没有任何一局可记，必须靠它挡掉，
    /// 否则会用初始的 0 分凭空造出一条记录。
    /// </summary>
    private bool gameStarted;

    /// <summary>
    /// 本局是否已经被 EndGame() / GameOver() 正式判定结束（典型是 44 回合用尽）。
    ///
    /// 【为什么要单独一个标记】记录不仅要在「结束的那一瞬间」写，还要在玩家
    /// 「从结束画面回主菜单 / 直接关窗口」时兜底补写（结算成功的那一局就靠这条路径）。
    /// 而兜底补写必须能区分「打完的一局」和「玩到一半放弃的一局」——
    /// 只看 hasSettled 判断不了「回合用尽但一次都没结算」的情形，所以在这里单独记一个。
    /// </summary>
    private bool roundConcluded;

    /// <summary>手牌区里所有卡牌实例。</summary>
    private readonly List<CardUI> handCards = new List<CardUI>();

    /// <summary>手牌区的 Transform（handCardArea 留空时返回 null）。</summary>
    public Transform HandAreaTransform => handCardArea != null ? handCardArea.transform : null;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("[GameManager] 场景中存在多个 GameManager，已销毁重复的那个。");
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        InitializeGame();

        LoadAllCards();
    }

    // ==================================================================
    // 初始化 / 结束
    // ==================================================================

    /// <summary>
    /// 初始化：加载全部卡牌、重建抽卡池、分数清零、展示 mainMenu。
    /// 注意：原文的 initialize() 伪代码里同时写了「抽卡 14 张」，
    /// 但流程段又写明是「点击开始游戏后 startGame 再抽 14 张」，二者冲突。
    /// 这里取流程段的说法——初始化只准备数据与界面，抽卡交给 StartGame()。
    /// </summary>
    public void InitializeGame()
    {
        // 【一局结束的兜底 —— 位置绝对不能后移，也不能挪到 ResetStudentId 之后】
        //
        // 点「结算」成功结束的那一局**不会**走 EndGame()：结算动画播完直接弹 endMenu，
        // 玩家点「返回主菜单」就走到这里。如果只在 EndGame() / GameOver() 里写记录，
        // 所有「结算成功」的局都会被漏掉，排行榜上就只剩「回合用尽」那一类失败局
        // —— 表现就是「失败的局有记录、成功的局没有」。
        //
        // 必须放在 ResetStudentId() 之前：它会把 studentId 与 studentIdConfirmed 一起清掉，
        // 清完再记就变成「没有合法学号」，记不上了。
        RecordCurrentGameResult();

        // 回到主菜单 = 当前没有进行中的对局（防止在启动时凭空记录，见 gameStarted 的注释）
        gameStarted = false;

        BuildCardPool();

        ValidateWiring();

        // 默认每次都重新要求输学号：回到主菜单后换一名学生，不会误用上一个人的学号
        if (askStudentIdEveryGame) ResetStudentId();

        // 回到主菜单时把删卡阶段一并清掉，否则按钮会停在「删除2张卡」的红字状态
        ResetDeletePhase();

        // 先清槽位再清手牌，避免槽位残留对已销毁卡牌的引用
        if (SlotManager.instance != null) SlotManager.instance.ClearAllSlots();
        ClearHand();

        if (UIManager.instance != null)
        {
            // 排行榜：重新读一遍记录文件再刷新文本。
            // 放在 InitializeUi 之前 —— 主菜单一露出来，榜上的内容就已经是最新的了。
            UIManager.instance.RefreshRanking();

            UIManager.instance.UpdateRound(turnCount, turnLimit);
            UIManager.instance.InitializeUi();  // 展示 mainMenu
        }
    }

    /// <summary>
    /// 自检：把「需要在 Unity 里拖入、但忘了拖」的引用一次性报到 Console。
    /// 只做提示，不阻断流程，方便按提示逐个补齐。
    /// </summary>
    private void ValidateWiring()
    {
        List<string> missing = new List<string>();

        if (cardPrefab == null) missing.Add("GameManager.cardPrefab（卡牌预制体）");
        if (handCardArea == null) missing.Add("GameManager.handCardArea（手牌区）");
        if (SlotManager.instance == null) missing.Add("场景里没有 SlotManager 组件");
        else if (SlotManager.instance.TotalSlotCount != 14)
        {
            Debug.LogWarning(string.Format(
                "[GameManager] 当前扫描到 {0} 个槽位，按需求应该是 3*4+2 = 14 个，请检查各组的 container 是否都拖好了。",
                SlotManager.instance.TotalSlotCount));
        }
        if (UIManager.instance == null) missing.Add("场景里没有 UIManager 组件");
        else
        {
            if (UIManager.instance.deletionPanels == null || UIManager.instance.deletionPanels.Count == 0)
                missing.Add("UIManager.deletionPanels（删卡投放区）");
            if (UIManager.instance.nextTurnButton == null)
                missing.Add("UIManager.nextTurnButton（下一回合按钮，删卡阶段要改它的文字与颜色）");
            if (UIManager.instance.groupScoreTexts == null || UIManager.instance.groupScoreTexts.Count == 0)
                missing.Add("UIManager.groupScoreTexts（每组分数文本，结算时按索引 0→3 依次显示）");

            if (UIManager.instance.studentIdInput == null)
                missing.Add("UIManager.studentIdInput（学号输入框）");
            else if (UIManager.instance.studentIdHintText == null)
                Debug.LogWarning("[GameManager] UIManager.studentIdHintText 没拖：学号输入非法时玩家在界面上看不到提示，" +
                                 "只能在 Console 里看到原因。建议拖一个 Text 上去。");

            if (UIManager.instance.rankingText == null)
                missing.Add("UIManager.rankingText（排行榜文本，展示 学号/平均分/最高得分/游玩局数）");

            // 教程：面板拖了却没拖正文文本，点开就是一片空白（内容写不进去），属于必检项
            if (UIManager.instance.tutorialPanel != null && UIManager.instance.tutorialText == null)
                missing.Add("UIManager.tutorialText（教程正文文本；教程面板已拖入但正文没拖，点开会是空白）");
            // 正文来源两个都空则连内容都取不到
            if (UIManager.instance.tutorialText != null
                && UIManager.instance.tutorialContent == null
                && string.IsNullOrEmpty(UIManager.instance.tutorialResourcePath))
                missing.Add("UIManager.tutorialContent 或 tutorialResourcePath（至少要有一个教程正文来源）");

            // 写给老师：与教程共用同一套实现，检查项也一样
            if (UIManager.instance.forTeacherPanel != null && UIManager.instance.forTeacherText == null)
                Debug.LogWarning(
                    "[GameManager] UIManager.forTeacherPanel 拖了，但 forTeacherText 还没拖、" +
                    "且面板里也没找到可用的 Text (Legacy)：点开「写给老师」会是一片空白。\n" +
                    "  请在 Canvas/forTeacher/Viewport/Content 下建一个 Text (Legacy)（Content 现在只有一个 Image），" +
                    "再拖到 UIManager.forTeacherText 上。");
            if (UIManager.instance.forTeacherText != null
                && UIManager.instance.forTeacherContent == null
                && string.IsNullOrEmpty(UIManager.instance.forTeacherResourcePath))
                missing.Add("UIManager.forTeacherContent 或 forTeacherResourcePath（至少要有一个「写给老师」正文来源）");

            // 总分文本可以留空（会自动复用 scoreText），但两个都空就真的没地方显示总分了
            if (UIManager.instance.settleTotalScoreText == null && UIManager.instance.scoreText == null)
                missing.Add("UIManager.settleTotalScoreText 或 scoreText（至少要有一个来显示总分）");

            // 组分数文本数量与槽位组数量不一致只是提示，不阻断流程
            if (SlotManager.instance != null
                && UIManager.instance.groupScoreTexts != null
                && UIManager.instance.groupScoreTexts.Count > 0
                && UIManager.instance.groupScoreTexts.Count != SlotManager.instance.slotGroups.Count)
            {
                Debug.LogWarning(string.Format(
                    "[GameManager] 组分数文本有 {0} 个，槽位分组有 {1} 组 —— " +
                    "结算跳分只显示「两者取小」的那么多组，请核对 UIManager.groupScoreTexts 的顺序与数量。",
                    UIManager.instance.groupScoreTexts.Count, SlotManager.instance.slotGroups.Count));
            }
        }

        if (missing.Count > 0)
        {
            Debug.LogWarning("[GameManager] 以下引用还未在 Inspector 里拖入：\n  - " + string.Join("\n  - ", missing));
        }
    }

    /// <summary>从 Resources/allCards 读取全部牌面并自检 isPrime 是否正确。</summary>
    private void LoadAllCards()
    {
        allCards.Clear();

        NumberCardData[] loaded = Resources.LoadAll<NumberCardData>("allCards");
        allCards.AddRange(loaded);

        // Resources.LoadAll 的返回顺序不保证稳定，按 value 升序排一下，方便调试与复现
        allCards.Sort((a, b) => a.value.CompareTo(b.value));

        if (allCards.Count == 0)
        {
            Debug.LogError("[GameManager] 没有从 Resources/allCards 读到任何 NumberCardData，请检查资产是否存在。");
            return;
        }

        int inconsistent = 0;
        for (int i = 0; i < allCards.Count; i++)
        {
            if (!allCards[i].IsPrimeFlagConsistent) inconsistent++;
        }
        if (inconsistent > 0)
        {
            Debug.LogWarning(string.Format(
                "[GameManager] 有 {0} 个卡牌资产的 isPrime 与 value 不一致（只做提示，不会修改资产）。" +
                "运行时的质数判定始终按 value 实时计算，不受影响。", inconsistent));
        }

        Debug.Log(string.Format("[GameManager] 已加载 {0} 个牌面定义。", allCards.Count));
    }

    /// <summary>重建抽卡池：每个牌面放入 number（默认 2）份，共 200 张。</summary>
    private void BuildCardPool()
    {
        tempCardsPool.Clear();

        for (int i = 0; i < allCards.Count; i++)
        {
            NumberCardData card = allCards[i];
            if (card == null || !card.canDraw) continue;

            int copies = Mathf.Max(1, card.number);
            for (int c = 0; c < copies; c++)
            {
                tempCardsPool.Add(card);
            }
        }
    }

    /// <summary>结束游戏：按最终分数展示结算/结束画面。</summary>
    public void GameOver()
    {
        // 一局到此为止：先标记「本局已结束」，再把成绩写进记录，最后走界面流程
        roundConcluded = true;
        RecordCurrentGameResult();

        if (UIManager.instance == null) return;

        string detail = hasSettled
            ? "本局已结算过，最终得分保留。"
            : string.Format("{0} 回合内没有点击结算，得分记 0。", turnLimit);

        UIManager.instance.ShowEndMenu(score, detail);
    }

    // ==================================================================
    // 学号校验（startGame 之前的环节）
    // ==================================================================

    /// <summary>
    /// 点击「开始游戏」：**先过学号这一关**，再切到 gamePanel、抽 14 张、进入第 1 回合。
    /// 开局不进入删卡阶段 —— 删卡只发生在每次「下一回合」抽卡之后。
    ///
    /// 学号关卡的三种走向：
    ///   1. requireStudentId 关掉 / 已经确认过学号 → 直接开局；
    ///   2. 还没确认 → 当场读输入框校验一次，通过就开局（所以「确认」按钮直接接到 StartGame 也能跑通）；
    ///   3. 校验不通过 → 把原因写到提示文本并亮出学号界面，本次不开始游戏。
    ///
    /// 例外：UIManager 上一个输入源都没拖时，会打警告后跳过校验直接开局 ——
    /// 忘记连线只会少一层校验，不至于把游戏彻底卡死。
    /// </summary>
    public void StartGame()
    {
        if (requireStudentId && !studentIdConfirmed)
        {
            if (!HasStudentIdInputSource())
            {
                Debug.LogWarning("[GameManager] requireStudentId 已开启，但 UIManager 上没有拖入学号输入框" +
                                 "（studentIdInput / studentIdText），本次跳过学号校验直接开局。");
            }
            else if (!ConfirmStudentId())
            {
                // 顺序很重要：ShowStudentIdPanel 会清掉上一次的提示，
                // 所以必须「先亮界面、再把这次的出错原因写上去」，反了玩家就什么都看不到。
                if (UIManager.instance != null)
                {
                    UIManager.instance.ShowStudentIdPanel();
                    UIManager.instance.ShowStudentIdHint(lastStudentIdError);
                }

                Debug.LogWarning("[GameManager] 学号校验未通过，本次不开始游戏。");
                return;
            }
        }

        StartGameInternal();
    }

    /// <summary>
    /// 「确认」按钮的入口：校验学号，通过则立刻开局。
    /// （UIManager.SubmitStudentId 调的就是它；你也可以把按钮直接挂到 StartGame，两条路等价）
    /// </summary>
    /// <returns>true = 学号合法且本局已经开始。</returns>
    public bool ConfirmStudentIdAndStart()
    {
        if (!ConfirmStudentId()) return false;

        StartGameInternal();
        return true;
    }

    /// <summary>
    /// 只做「读输入 → 校验 → 记下学号」，不开局。
    /// 校验失败时会把中文原因写到 UIManager.studentIdHintText 上，
    /// 同时也存进 <see cref="lastStudentIdError"/>，方便调用方先亮界面再补写提示。
    /// </summary>
    /// <returns>true = 学号合法且已记录。</returns>
    public bool ConfirmStudentId()
    {
        if (UIManager.instance == null)
        {
            Debug.LogWarning("[GameManager] 场景里没有 UIManager，无法读取学号输入。");
            return false;
        }

        int id;
        string error;
        if (!UIManager.instance.TryReadStudentId(studentIdMin, studentIdMax, out id, out error))
        {
            lastStudentIdError = error;
            UIManager.instance.ShowStudentIdHint(error);
            return false;
        }

        lastStudentIdError = null;
        ApplyStudentId(id);
        return true;
    }

    /// <summary>记下学号：写入字段、收起学号界面、清掉提示、把输入框预填成这个值。</summary>
    private void ApplyStudentId(int id)
    {
        studentId = id;
        studentIdConfirmed = true;

        if (UIManager.instance != null)
        {
            UIManager.instance.ClearStudentIdHint();
            UIManager.instance.SetStudentIdPanelVisible(false);
            UIManager.instance.RememberStudentId(id);
        }

        Debug.Log("[GameManager] 学号已确认：" + id + "。");
    }

    /// <summary>清掉当前的学号（回到主菜单、或要换一名学生时调用）。</summary>
    public void ResetStudentId()
    {
        studentId = -1;
        studentIdConfirmed = false;
        lastStudentIdError = null;
    }

    /// <summary>UIManager 上是否至少拖了一个学号输入源。</summary>
    private static bool HasStudentIdInputSource()
    {
        return UIManager.instance != null
            && (UIManager.instance.studentIdInput != null);
    }

    /// <summary>
    /// 真正开始一局的流程（不含学号校验）。
    /// </summary>
    private void StartGameInternal()
    {
        score = 0;
        turnCount = 1;
        hasSettled = false;
        resultRecordedThisGame = false;
        gameStarted = true;              // 从这里开始「有一局在进行」，结束一局时才会写记录
        roundConcluded = false;          // 本局尚未结束，中途退出不算成绩

        BuildCardPool();
        ResetDeletePhase();
        ClearHand();
        if (SlotManager.instance != null) SlotManager.instance.ClearAllSlots();

        if (UIManager.instance != null)
        {
            UIManager.instance.ShowGamePanel();
            UIManager.instance.UpdateRound(turnCount, turnLimit);
        }

        DrawCards(initialDrawCount);
    }

    // ==================================================================
    // 成绩记录
    // ==================================================================

    /// <summary>
    /// 「本局要不要写进记录」的纯判定结果。
    /// 判定本身不碰任何 Unity 运行时状态，可以离线回归（见 HC_Check 里的用例）。
    /// </summary>
    public enum RecordDecision
    {
        /// <summary>写：本局有效，可以落盘。</summary>
        Record,

        /// <summary>不写，但属于正常情况（程序刚启动、同一个结束点被重复触发），不必报警告。</summary>
        SkipBenign,

        /// <summary>不写，且值得在 Console 里提醒（记录开关关着、中途放弃、没有合法学号）。</summary>
        SkipNotable
    }

    /// <summary>
    /// 判断本局该不该写进记录。这是「记录功能会不会漏记」的唯一裁决点 ——
    /// 所有调用点（EndGame / GameOver / InitializeGame / ExitGame / OnApplicationQuit）
    /// 都只负责在**正确的时机**调过来，要不要写由这里说了算。
    ///
    /// 判定顺序即优先级：开关 → 有没有一局 → 是不是已经记过 → 这一局算不算打完 → 学号是否合法。
    /// </summary>
    /// <param name="recordEnabled">recordResultOnGameEnd。</param>
    /// <param name="gameStarted">是否有一局在进行。</param>
    /// <param name="alreadyRecorded">本局是否已经记过（幂等保护）。</param>
    /// <param name="hasSettled">本局是否点过结算。</param>
    /// <param name="roundConcluded">本局是否已被 EndGame / GameOver 正式判定结束。</param>
    /// <param name="idConfirmed">是否已确认合法学号。</param>
    /// <param name="reason">不写时的中文原因；要写时为 null。</param>
    public static RecordDecision EvaluateRecordDecision(
        bool recordEnabled, bool gameStarted, bool alreadyRecorded,
        bool hasSettled, bool roundConcluded, bool idConfirmed,
        out string reason)
    {
        if (!recordEnabled)
        {
            reason = "记录开关关着（recordResultOnGameEnd = false）";
            return RecordDecision.SkipNotable;
        }

        if (!gameStarted)
        {
            reason = "当前没有进行中的对局";
            return RecordDecision.SkipBenign;
        }

        if (alreadyRecorded)
        {
            reason = "本局成绩已经记录过";
            return RecordDecision.SkipBenign;
        }

        // 这一局必须「真的打完了」才有成绩可记：
        //   hasSettled     = 玩家点过结算（哪怕结算下来是 0 分，那也是有效的一局）
        //   roundConcluded = 本局已由 EndGame / GameOver 正式结束（典型是 44 回合用尽）
        // 两者都不成立的，就是「玩到一半直接关窗口」这类中途放弃：
        // 这种局从头到尾没有结算过，分数必然是 0，写进去只会把游玩局数 +1、把平均分拉低。
        if (!hasSettled && !roundConcluded)
        {
            reason = "本局既没有结算、也没有打满回合，属于中途放弃";
            return RecordDecision.SkipNotable;
        }

        if (!idConfirmed)
        {
            reason = "还没有确认合法的学号";
            return RecordDecision.SkipNotable;
        }

        reason = null;
        return RecordDecision.Record;
    }

    /// <summary>
    /// 把本局成绩写进记录（需求 2）。**同一局只会写一次**，
    /// 所以 EndGame / GameOver / InitializeGame / ExitGame 被先后触发也不会把一局算成两局。
    ///
    /// 【调用点必须是全集】目前共 4 处：EndGame（回合用尽）、GameOver（外部按钮）、
    /// InitializeGame（玩家从结算 / 结束画面回主菜单）、ExitGame 与 OnApplicationQuit（直接关窗口）。
    /// 少一处就会出现「某种结束方式没有成绩」的漏记 ——
    /// 之前只有 EndGame / GameOver，于是「点结算成功结束」的局全都漏掉了。
    ///
    /// 要不要写由 <see cref="EvaluateRecordDecision"/> 裁决，规则见那个方法。
    ///
    /// 记录按学号聚合（一条记录 = 学号 / 平均分 / 最高得分 / 游玩局数）：
    ///   游玩局数 +1、累计总分 += 本局得分、最高得分取历史最高，
    ///   平均分不落盘，由「累计总分 ÷ 游玩局数」在显示排行榜时现算。
    /// </summary>
    /// <returns>true = 本次确实写入了一条记录。</returns>
    public bool RecordCurrentGameResult()
    {
        string reason;
        RecordDecision decision = EvaluateRecordDecision(
            recordResultOnGameEnd,
            gameStarted,
            resultRecordedThisGame,
            hasSettled,
            roundConcluded,
            studentIdConfirmed && studentId >= studentIdMin && studentId <= studentIdMax,
            out reason);

        if (decision != RecordDecision.Record)
        {
            string message = "[GameManager] 本局不写记录：" + reason + "。";
            if (decision == RecordDecision.SkipBenign) Debug.Log(message);
            else Debug.LogWarning(message);
            return false;
        }

        resultRecordedThisGame = true;
        StudentRecord record = ScoreRecordStore.AddResult(studentId, score);

        // 顺手刷新排行榜文本（面板没显示也不影响，只是把内容准备好）
        if (UIManager.instance != null) UIManager.instance.RefreshRankingText(UIManager.instance.rankingSortMode);

        Debug.Log(string.Format(
            "[GameManager] 学号 {0} 本局 {1} 分已记录 → 共 {2} 局，平均 {3:0.00}，最高 {4}。",
            record.studentId, score, record.playCount, record.Average, record.highScore));

        return true;
    }

    // ==================================================================
    // 下一回合 / 结束
    // ==================================================================

    /// <summary>
    /// 点击「下一回合」：继续抽 drawPerTurn（2）张，然后【强制进入删卡阶段】。
    ///
    /// 流程：抽 2 张 → 必须往删卡区拖掉 2 张 → 按钮恢复可点击 → 才能再点下一回合。
    /// 删卡没完成时本方法会被直接挡回（此时按钮本身也是禁用状态，这里是第二层保护）。
    ///
    /// 回合数已达上限时不能再推进——此时直接判定结束；
    /// 如果整局都没点击过结算，分数归零（对应「44 回合内没有点击结算算作得分为 0」）。
    /// </summary>
    public void NextTurn()
    {
        // 结算跳分动画期间不允许推进回合（这段窗口里的盘面正在「定格展示」）
        if (IsSettleSequencePlaying)
        {
            Debug.LogWarning("[GameManager] 结算动画播放中，暂不能进入下一回合。");
            return;
        }

        // 删卡阶段没完成 → 不允许推进回合
        if (isDeletePhase)
        {
            Debug.LogWarning(string.Format(
                "[GameManager] 还差 {0} 张卡没删完，删完才能进入下一回合。",
                Mathf.Max(0, deleteCountPerTurn - deletedCount)));
            return;
        }

        if (turnCount >= turnLimit)
        {
            if (!hasSettled) score = 0;
            EndGame();
            return;
        }

        turnCount++;
        DrawCards(drawPerTurn);

        if (UIManager.instance != null)
        {
            UIManager.instance.UpdateRound(turnCount, turnLimit);
        }

        // 抽完卡立刻进入删卡阶段（对应「nextTurn 里增加删除卡牌函数」）
        if (HandCardCount >= deleteCountPerTurn)
        {
            BeginDeletePhase();
        }
        else
        {
            // 手牌不够 2 张（牌库抽空等极端情况）就不进删卡阶段，否则玩家会被卡死
            Debug.LogWarning(string.Format(
                "[GameManager] 手牌只剩 {0} 张，不足 {1} 张，跳过本次删卡阶段。",
                HandCardCount, deleteCountPerTurn));
        }
    }

    /// <summary>回合用尽，收尾：把本局成绩写进记录，然后弹出结束画面。</summary>
    private void EndGame()
    {
        ResetDeletePhase();
        ClearHand();
        if (SlotManager.instance != null) SlotManager.instance.ClearAllSlots();

        if (UIManager.instance != null)
        {
            UIManager.instance.UpdateScore(score);
        }

        // 一局到此为止：先标记「本局已结束」（这样即使整局都没结算过，44 回合用尽也算一局 0 分），
        // 再写记录（同一局只会写一次，见 RecordCurrentGameResult）
        roundConcluded = true;
        RecordCurrentGameResult();

        string detail = hasSettled
            ? string.Format("{0} 回合已用尽，共结算过若干次。", turnLimit)
            : string.Format("{0} 回合内没有点击结算，得分记 0。", turnLimit);

        if (UIManager.instance != null)
        {
            UIManager.instance.ShowEndMenu(score, detail);
        }
    }

    // ==================================================================
    // 抽卡
    // ==================================================================

    /// <summary>
    /// 抽卡：随机抽 n 张。
    /// 每抽一张 → 从 tempCardsPool 移除一项（等价于「该牌面剩余数量 --」）；
    /// 池子里找不到该牌面时即为「数量为 0，不可抽取」。
    /// 抽到后绑定 UI 数据并挂进手牌区 handCardArea。
    /// </summary>
    private void DrawCards(int n)
    {
        if (n <= 0) return;

        if (cardPrefab == null)
        {
            Debug.LogError("[GameManager] cardPrefab 未赋值，无法抽卡。请把 CardUI.prefab 拖到这个字段上。");
            return;
        }
        Transform handArea = HandAreaTransform;
        if (handArea == null)
        {
            Debug.LogError("[GameManager] handCardArea 未赋值，无法放置手牌。请把手牌区物体拖到这个字段上。");
            return;
        }

        int drawn = 0;

        for (int i = 0; i < n; i++)
        {
            // 检测剩余卡牌：池子空了就抽不出来了
            if (tempCardsPool.Count == 0)
            {
                Debug.LogWarning("[GameManager] 抽卡池已空，本次抽卡提前结束。");
                break;
            }

            int index = Random.Range(0, tempCardsPool.Count);
            NumberCardData data = tempCardsPool[index];
            tempCardsPool.RemoveAt(index);      // 抽到后数量 --

            // 实例化卡牌并绑定 UI
            GameObject go = Instantiate(cardPrefab, handArea, false);
            go.name = "Card_" + (data != null ? data.value.ToString() : "Null");

            CardUI card = go.GetComponent<CardUI>();
            if (card == null) card = go.AddComponent<CardUI>();
            card.Bind(data);                    // 读取 value 并写到 Text 上

            handCards.Add(card);
            drawn++;
        }

        Debug.Log(string.Format("[GameManager] 本回合抽到 {0} 张，剩余牌库 {1} 张。", drawn, tempCardsPool.Count));
    }

    /// <summary>
    /// 把一张卡退回手牌区。【统一入口】
    ///
    /// 拖拽落空、拖到手牌区、拖到槽位与手牌区以外的任何地方、点击槽位里的卡、
    /// 槽位被别的卡顶替 —— 全部走这一个方法，避免两处各改一半导致数据与 UI 不同步。
    ///
    /// 内部顺序：先校验 → 清数据（解除与原槽位的双向引用）→ 搬 UI（父物体 + 还原手牌布局）→ 补进手牌列表。
    /// </summary>
    /// <returns>true = 卡已经在手牌区了；false = 前提不满足，什么都没改（数据与 UI 都保持原状）。</returns>
    public bool ReturnCardToHand(CardUI card)
    {
        if (card == null) return false;

        // 先把前提校验完再动手：只要有一项不满足就整体不动，
        // 不能先清掉数据再发现搬不了 UI。
        Transform home = HandAreaTransform;
        RectTransform rect = card.transform as RectTransform;
        if (home == null || rect == null)
        {
            Debug.LogError("[GameManager] 无法退回手牌区：handCardArea 未赋值，或卡牌根节点没有 RectTransform。" +
                           "为避免数据与界面不一致，本次不做任何改动。请检查 Inspector 里的引用。");
            return false;
        }

        // 数据侧：解除与原槽位的双向引用（Clear 会同时清 slot.currentCard 与 card.currentSlot）
        if (card.currentSlot != null) card.currentSlot.Clear();

        // UI 侧：搬父物体 + 还原手牌布局（不还原锚点的话，从槽位回来的卡会保持「铺满」的尺寸）
        rect.SetParent(home, false);
        card.RestoreHandLayout(rect);
        rect.SetAsLastSibling();

        // 手牌区若用 LayoutGroup 排布，改完父物体后主动通知它重建一次
        RectTransform homeRect = home as RectTransform;
        if (homeRect != null && home.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
        {
            UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(homeRect);
        }

        // 从槽位退回的卡要保证它仍在手牌列表里
        if (!handCards.Contains(card)) handCards.Add(card);
        return true;
    }

    /// <summary>
    /// 屏幕点是否落在手牌区内。
    /// 用于「拖到哪」的分支判断与日志：落在手牌区内和落在界面空白处，处理相同（都是退回手牌区），
    /// 但分开判断便于调试，也方便以后想给「落在手牌区」加特殊表现。
    /// </summary>
    public bool IsOverHandArea(Vector2 screenPoint, Camera eventCamera)
    {
        RectTransform handRect = handCardArea != null ? handCardArea.transform as RectTransform : null;
        if (handRect == null) return false;

        return RectTransformUtility.RectangleContainsScreenPoint(handRect, screenPoint, eventCamera);
    }

    /// <summary>销毁手牌区里的所有卡牌。</summary>
    public void ClearHand()
    {
        for (int i = 0; i < handCards.Count; i++)
        {
            if (handCards[i] != null) Destroy(handCards[i].gameObject);
        }
        handCards.Clear();
    }

    // ==================================================================
    // 删卡阶段
    // ==================================================================

    /// <summary>
    /// 进入删卡阶段：显示删卡投放区，把「下一回合」按钮变红、禁用、文字改成「删除2张卡」。
    /// 玩家必须拖够 deleteCountPerTurn 张卡进删卡区，阶段才会结束。
    /// </summary>
    public void BeginDeletePhase()
    {
        isDeletePhase = true;
        deletedCount = 0;
        RefreshDeletePhaseUi();

        Debug.Log(string.Format("[GameManager] 进入删卡阶段：请拖 {0} 张卡到删卡区。", deleteCountPerTurn));
    }

    /// <summary>结束删卡阶段：隐藏删卡投放区，把「下一回合」按钮还原成可点击状态。</summary>
    public void EndDeletePhase()
    {
        isDeletePhase = false;
        deletedCount = 0;
        RefreshDeletePhaseUi();

        Debug.Log("[GameManager] 删卡完成，可以继续下一回合。");
    }

    /// <summary>重置删卡阶段（开局、返回主菜单、结束游戏时调用）。</summary>
    private void ResetDeletePhase()
    {
        isDeletePhase = false;
        deletedCount = 0;
        RefreshDeletePhaseUi();
    }

    /// <summary>
    /// 把删卡阶段的状态刷到界面上：删卡区显示 / 隐藏 + 「下一回合」按钮的红字与禁用。
    /// 所有改动 isDeletePhase 的地方都必须调它，否则会出现「数据在删卡阶段、界面却是正常状态」的不一致。
    /// </summary>
    private void RefreshDeletePhaseUi()
    {
        if (UIManager.instance == null) return;

        UIManager.instance.SetDeletePanelVisible(isDeletePhase);
        UIManager.instance.SetNextTurnButtonDeleteState(isDeletePhase, deleteCountPerTurn);
    }

    /// <summary>
    /// 删除一张卡 —— 把卡拖进删卡区时由 DragHandler 调用。
    ///
    /// 只在删卡阶段生效；删够 deleteCountPerTurn 张后自动结束阶段（删卡区随之隐藏，
    /// 所以「有且仅为 2 张」是天然成立的：想多删也没有可投放的区域了）。
    /// 删除 = 从本局彻底移除，不回牌库、不进手牌。
    /// </summary>
    /// <returns>true = 已经删掉了；false = 不在删卡阶段或参数无效，什么都没做。</returns>
    public bool DeleteCard(CardUI card)
    {
        if (!isDeletePhase)
        {
            Debug.LogWarning("[GameManager] 当前不在删卡阶段，忽略本次删除。");
            return false;
        }
        if (card == null) return false;

        if (deletedCount >= deleteCountPerTurn)
        {
            Debug.LogWarning("[GameManager] 本次删卡数量已达上限，忽略本次删除。");
            return false;
        }

        // 数据与 UI 一起处理：先解除槽位双向引用，再从手牌列表移除，最后销毁物体
        if (card.currentSlot != null) card.currentSlot.Clear();
        handCards.Remove(card);

        int value = card.Value;
        Destroy(card.gameObject);   // 物体在本帧末尾才真正销毁，但引用已经清干净

        deletedCount++;
        Debug.Log(string.Format("[GameManager] 已删掉卡牌 {0}（{1}/{2}）。",
            value, deletedCount, deleteCountPerTurn));

        // 删够就结束阶段；删不满（例如卡被结算销毁了）就自愈，别把玩家卡死
        EnsureDeletePhaseSolvable();

        // 还在删卡阶段 → 刷新一次界面；删满了的话 EndDeletePhase 已经刷过了
        if (isDeletePhase) RefreshDeletePhaseUi();

        return true;
    }

    /// <summary>
    /// 删卡阶段的自愈检查。
    ///   已删满   → 结束阶段；
    ///   手牌不够 → 也结束阶段（此时已经不可能删够，不结束的话「下一回合」按钮会永久禁用，
    ///              玩家会被彻底卡住。宁可跳过删卡也不能卡死）。
    /// </summary>
    private void EnsureDeletePhaseSolvable()
    {
        if (!isDeletePhase) return;

        int remaining = deleteCountPerTurn - deletedCount;
        if (remaining <= 0)
        {
            EndDeletePhase();
            return;
        }

        if (handCards.Count < remaining)
        {
            Debug.LogWarning(string.Format(
                "[GameManager] 手牌只剩 {0} 张，已经删不满 {1} 张，自动结束删卡阶段（避免卡死）。",
                handCards.Count, remaining));
            EndDeletePhase();
        }
    }

    /// <summary>
    /// 屏幕点是否落在删卡投放区内。
    /// 只在删卡阶段成立，且删卡区必须可见 —— 隐藏状态下拖到那个位置不会被误删。
    /// </summary>
    public bool IsOverDeleteArea(Vector2 screenPoint, Camera eventCamera)
    {
        if (!isDeletePhase) return false;
        if (UIManager.instance == null) return false;

        return UIManager.instance.IsOverDeletePanel(screenPoint, eventCamera);
    }

    // ==================================================================
    // 结算
    // ==================================================================

    /// <summary>
    /// 点击「结算」：读卡 → 逐组判定（等差 / 等比 / 斐波那契 / 雀头）→ 按 SlotManager.EvaluateHand
    /// 的规则统一算分 → 累加到总分 → 播放「逐组跳分」动画 → 弹出结算画面。
    ///
    /// 传入 turnCount 是为了「第 1 回合即判定成功额外 +100」这条规则。
    ///
    /// 【注意 1】这里**不**直接弹结算画面、**不**直接刷分数文本：
    /// 交给 UIManager.PlaySettleSequence —— 它会按索引 0→3 依次显示各组分数，
    /// 最后才显示总分，停 2 秒后才弹出 endMenuPanel（对应需求「显示完总分后停留 2 秒」）。
    ///
    /// 【注意 2】结算时槽位里的牌**不是立刻销毁**：它们会原样留在槽位上，
    /// 等跳分动画播完（UIManager 回调 <see cref="FinishSettlement"/>）才销毁并清空槽位。
    /// 这样逐组显示分数时盘面上的牌还在，玩家能对照着看出每一组是哪几个数字凑出来的。
    /// 动画期间这些牌不接受拖动与点击，见 <see cref="IsSettleSequencePlaying"/>。
    /// </summary>
    public void Settle()
    {
        if (SlotManager.instance == null)
        {
            Debug.LogWarning("[GameManager] 找不到 SlotManager，无法结算。");
            return;
        }

        // 跳分动画期间槽位里的卡还没有结算收尾，重复点结算只会把总分冲乱 → 直接挡回
        if (IsSettleSequencePlaying)
        {
            Debug.LogWarning("[GameManager] 结算动画播放中，忽略本次点击。");
            return;
        }

        // 回合已经用尽才点结算 → 视为超时，不得分
        if (turnCount > turnLimit)
        {
            score = 0;
            EndGame();
            return;
        }

        int gained = SlotManager.instance.CalculateScore(turnCount);   // 读卡 + 判数列 + 算分
        score += gained;                                              // 加分（累加）
        hasSettled = true;

        HandScoreResult result = SlotManager.instance.lastScoreResult;
        string detail = string.Format("本次得分 +{0}\n\n{1}", gained, SlotManager.instance.lastResultSummary);

        // 销毁槽位里的牌 + 清空槽位，推迟到动画播完再做（见 FinishSettlement）
        if (UIManager.instance != null)
        {
            UIManager.instance.PlaySettleSequence(
                result != null ? result.groupScores : null,
                result != null ? result.groupNames : null,
                score,
                detail,
                FinishSettlement);
        }
        else
        {
            // 没有 UIManager 就没有动画，直接收尾，别让牌一直留在槽位上
            FinishSettlement();
        }

        Debug.Log(string.Format("[GameManager] 结算：本次 +{0}，总分 {1}。各组得分：{2}",
            gained, score, DescribeGroupScores(result)));
    }

    /// <summary>
    /// 结算收尾：销毁槽位里的牌 → 清空槽位 → 自愈检查删卡阶段。
    /// 由 UIManager 在逐组跳分动画播完时回调（正常路径），
    /// 或由本类在没有 UIManager 时直接调用。
    ///
    /// 【为什么要延后到动画结束】动画要一屏一屏地显示每一组的得分，
    /// 牌在这期间必须留在槽位上，玩家才对得上「这一组是哪几个数字」。
    /// </summary>
    private void FinishSettlement()
    {
        ConsumeSlottedCards();
        if (SlotManager.instance != null) SlotManager.instance.ClearAllSlots();

        // 结算会销毁槽位里的卡，可能让删卡阶段变得无法完成 → 自愈检查，别把玩家卡死
        EnsureDeletePhaseSolvable();

        Debug.Log("[GameManager] 结算收尾：槽位里的卡已销毁，槽位已清空。");
    }

    /// <summary>
    /// 是否正在播放结算跳分动画。
    /// 动画期间盘面是「定格展示」状态：槽位里的牌还没销毁、也不该被拖走或点回手牌，
    /// 所以它同时也是「暂停交互」的开关（DragHandler 会读它）。
    /// </summary>
    public bool IsSettleSequencePlaying
    {
        get { return UIManager.instance != null && UIManager.instance.IsPlayingSettleSequence; }
    }

    /// <summary>把各组得分拼成一行日志，方便对照「各组之和 = 总分」。</summary>
    private static string DescribeGroupScores(HandScoreResult result)
    {
        if (result == null || result.groupScores == null || result.groupScores.Count == 0) return "（无）";

        string text = string.Empty;
        for (int i = 0; i < result.groupScores.Count; i++)
        {
            if (i > 0) text += "，";
            text += string.Format("{0} +{1}", result.GroupNameOf(i), result.groupScores[i]);
        }
        return text;
    }

    /// <summary>销毁所有放在槽位里的卡牌物体，并从手牌统计中移除。</summary>
    private void ConsumeSlottedCards()
    {
        if (SlotManager.instance == null) return;

        for (int g = 0; g < SlotManager.instance.slotGroups.Count; g++)
        {
            SlotGroup group = SlotManager.instance.slotGroups[g];
            if (group == null || group.slots == null) continue;

            for (int s = 0; s < group.slots.Count; s++)
            {
                CardSlot slot = group.slots[s];
                if (slot == null || slot.currentCard == null) continue;

                CardUI card = slot.currentCard;
                handCards.Remove(card);
                slot.Clear();               // 先清双向引用（Clear 会同时把 card.currentSlot 置空）
                Destroy(card.gameObject);   // 再销毁，避免清引用时碰到已经销毁的对象
            }
        }
    }

    /// <summary>结算画面上的「继续游戏」：关掉结算画面，回到 gamePanel。</summary>
    public void ContinueAfterSettlement()
    {
        if (UIManager.instance != null)
        {
            UIManager.instance.ShowGamePanel();
        }

        // 结算画面是从 gamePanel 上盖过去的，回来时把删卡阶段的界面重新对齐一次，
        // 避免出现「还在删卡阶段，删卡区和红按钮却没显示」的不一致
        RefreshDeletePhaseUi();
    }

    /// <summary>结算/结束画面上的「返回主菜单」。</summary>
    public void ReturnToMainMenu()
    {
        InitializeGame();
    }

    // ==================================================================
    // 查询工具
    // ==================================================================

    /// <summary>某个牌面在牌库中的剩余张数（0 表示已抽完、不可再抽）。</summary>
    public int GetRemainingCount(int value)
    {
        int count = 0;
        for (int i = 0; i < tempCardsPool.Count; i++)
        {
            if (tempCardsPool[i] != null && tempCardsPool[i].value == value) count++;
        }
        return count;
    }

    /// <summary>手牌数量。</summary>
    public int HandCardCount => handCards.Count;

    //退出游戏
    public void ExitGame()
    {
        // 直接关窗口也是一局结束：先把成绩落盘再退出，
        // 否则「结算完不点返回主菜单、直接退出」这一局就白打了。
        // 要不要写由 RecordCurrentGameResult 内部裁决：中途放弃的局会被挡掉，不会污染记录。
        RecordCurrentGameResult();

        Application.Quit();
    }

    /// <summary>
    /// 正常关闭程序（点窗口右上角的 ×，或 Application.Quit）时的最后一道保险。
    /// 和 ExitGame 走同一个幂等入口，重复调用不会把一局记成两局；
    /// 玩到一半直接关窗口的局同样会被 EvaluateRecordDecision 挡掉。
    /// </summary>
    private void OnApplicationQuit()
    {
        RecordCurrentGameResult();
    }
}
