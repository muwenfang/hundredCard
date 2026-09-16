using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 游戏主流程管理器（对应文本中的 gameManager）。
/// 挂在一个常驻物体上（示例场景中是 GameRoot），场景启动即自动初始化。
///
/// 主流程：
///   进入游戏 → InitializeGame() → 展示 mainMenu
///   点击开始游戏 → StartGame() → 抽 14 张进手牌区
///   点击下一回合 → NextTurn() → 再抽 2 张
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

    [Header("运行时状态（只读）")]
    [Tooltip("当前累计得分")]
    public int score;

    [Tooltip("当前回合数，开局抽 14 张后记为第 1 回合")]
    public int turnCount;

    [Tooltip("本局是否点击过结算")]
    public bool hasSettled;

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
        LoadAllCards();
        BuildCardPool();

        ValidateWiring();

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

    /// <summary>点击「开始游戏」：切到 gamePanel，抽 14 张，进入第 1 回合。</summary>
    public void StartGame()
    {
        score = 0;
        turnCount = 1;
        hasSettled = false;

        BuildCardPool();
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
    /// 点击「下一回合」：继续抽 drawPerTurn（2）张。
    /// 回合数已达上限时不能再推进——此时直接判定结束；
    /// 如果整局都没点击过结算，分数归零（对应「44 回合内没有点击结算算作得分为 0」）。
    /// </summary>
    public void NextTurn()
    {
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
    }

    /// <summary>回合用尽，收尾。</summary>
    private void EndGame()
    {
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
    // 结算
    // ==================================================================

    /// <summary>
    /// 点击「结算」：读卡 → 判断等比 / 等差 / 质数 → 加分 → 刷新分数 → 播放结算画面。
    /// 结算会把已用掉的槽位清空，让玩家继续构筑下一组数列。
    /// </summary>
    public void Settle()
    {
        if (SlotManager.instance == null)
        {
            Debug.LogWarning("[GameManager] 找不到 SlotManager，无法结算。");
            return;
        }

        // 回合已经用尽才点结算 → 视为超时，不得分
        if (turnCount > turnLimit)
        {
            score = 0;
            EndGame();
            return;
        }

        int gained = SlotManager.instance.CalculateScore();   // 读卡 + 判数列 + 算分
        score += gained;                                      // 加分（累加）
        hasSettled = true;

        // 槽位里的卡已经被消费掉，销毁它们并清空槽位
        ConsumeSlottedCards();
        SlotManager.instance.ClearAllSlots();

        if (UIManager.instance != null)
        {
            UIManager.instance.UpdateScore(score);
            UIManager.instance.ShowEndMenu(
                score,
                string.Format("本次得分 +{0}\n\n{1}", gained, SlotManager.instance.lastResultSummary));
        }

        Debug.Log(string.Format("[GameManager] 结算：本次 +{0}，总分 {1}。", gained, score));
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
