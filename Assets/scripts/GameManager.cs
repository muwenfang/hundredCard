using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 游戏主流程管理器（对应文本中的 gameManager）。
/// 挂在一个常驻物体上（示例场景中是 GameRoot），场景启动即自动初始化。
///
/// 主流程：
///   进入游戏 → InitializeGame() → 展示 mainMenu
///   点击开始游戏 → StartGame() → 抽 14 张进手牌区（开局不删卡）
///   点击下一回合 → NextTurn() → 再抽 2 张 → 进入删卡阶段
///   删卡阶段   → 把 2 张卡拖进 deletionPanels → 删够之后按钮恢复，才能再点下一回合
///   点击结算   → Settle()   → 读槽位、判数列、加分、弹出结算画面
///   44 回合内没有点击过结算 → 分数归零并结束
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
        BuildCardPool();

        ValidateWiring();

        // 回到主菜单时把删卡阶段一并清掉，否则按钮会停在「删除2张卡」的红字状态
        ResetDeletePhase();

        // 先清槽位再清手牌，避免槽位残留对已销毁卡牌的引用
        if (SlotManager.instance != null) SlotManager.instance.ClearAllSlots();
        ClearHand();

        if (UIManager.instance != null)
        {
            UIManager.instance.UpdateScore(score);
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
        if (UIManager.instance == null) return;

        string detail = hasSettled
            ? "本局已结算过，最终得分保留。"
            : string.Format("{0} 回合内没有点击结算，得分记 0。", turnLimit);

        UIManager.instance.ShowEndMenu(score, detail);
    }

    // ==================================================================
    // 开始游戏 / 下一回合
    // ==================================================================

    /// <summary>
    /// 点击「开始游戏」：切到 gamePanel，抽 14 张，进入第 1 回合。
    /// 开局不进入删卡阶段 —— 删卡只发生在每次「下一回合」抽卡之后。
    /// </summary>
    public void StartGame()
    {
        score = 0;
        turnCount = 1;
        hasSettled = false;

        BuildCardPool();
        ResetDeletePhase();
        ClearHand();
        if (SlotManager.instance != null) SlotManager.instance.ClearAllSlots();

        if (UIManager.instance != null)
        {
            UIManager.instance.ShowGamePanel();
            UIManager.instance.UpdateScore(score);
            UIManager.instance.UpdateRound(turnCount, turnLimit);
        }

        DrawCards(initialDrawCount);
    }

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
        if (UIManager.instance != null && UIManager.instance.IsPlayingSettleSequence)
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

    /// <summary>回合用尽，收尾。</summary>
    private void EndGame()
    {
        ResetDeletePhase();
        ClearHand();
        if (SlotManager.instance != null) SlotManager.instance.ClearAllSlots();

        if (UIManager.instance != null)
        {
            UIManager.instance.UpdateScore(score);
        }

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
    /// 结算会把已用掉的槽位清空，让玩家继续构筑下一组数列。
    ///
    /// 传入 turnCount 是为了「第 1 回合即判定成功额外 +100」这条规则。
    ///
    /// 【注意】这里**不再**直接弹结算画面，也不再直接刷分数文本：
    /// 交给 UIManager.PlaySettleSequence —— 它会按索引 0→3 依次显示各组分数，
    /// 最后才显示总分，停 2 秒后才弹出 endMenuPanel（对应需求「显示完总分后停留 2 秒」）。
    /// </summary>
    public void Settle()
    {
        if (SlotManager.instance == null)
        {
            Debug.LogWarning("[GameManager] 找不到 SlotManager，无法结算。");
            return;
        }

        // 跳分动画期间槽位里的卡已经销毁，重复点结算只会把总分冲乱 → 直接挡回
        if (UIManager.instance != null && UIManager.instance.IsPlayingSettleSequence)
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

        // 槽位里的卡已经被消费掉，销毁它们并清空槽位
        ConsumeSlottedCards();
        SlotManager.instance.ClearAllSlots();

        // 结算会销毁槽位里的卡，可能让删卡阶段变得无法完成 → 自愈检查，别把玩家卡死
        EnsureDeletePhaseSolvable();

        HandScoreResult result = SlotManager.instance.lastScoreResult;
        string detail = string.Format("本次得分 +{0}\n\n{1}", gained, SlotManager.instance.lastResultSummary);

        if (UIManager.instance != null)
        {
            UIManager.instance.PlaySettleSequence(
                result != null ? result.groupScores : null,
                result != null ? result.groupNames : null,
                score,
                detail);
        }

        Debug.Log(string.Format("[GameManager] 结算：本次 +{0}，总分 {1}。各组得分：{2}",
            gained, score, DescribeGroupScores(result)));
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
        Application.Quit();
    }
}
