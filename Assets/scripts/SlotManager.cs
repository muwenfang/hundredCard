using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>槽位组可以判定的数列 / 牌型种类。</summary>
public enum SequenceType
{
    /// <summary>等差：升序后相邻差值全部相等（公差 &gt; 0）。</summary>
    Arithmetic,

    /// <summary>等比：升序后每一项的平方等于前后两项之积（支持 4,6,9 这类分数公比）。</summary>
    Geometric,

    /// <summary>斐波那契：升序后每一项等于前两项之和。</summary>
    Fibonacci,

    /// <summary>质数：组内每个数字都是质数。默认不参与任何组，需要时在 Inspector 里加进候选列表。</summary>
    Prime,

    /// <summary>2 槽组的旧特殊判定（1 / 100 / 质数）。已被「雀头」取代，保留备用。</summary>
    Special,

    /// <summary>雀头：恰好 2 张、且两张数字相同。这是 2 槽组的默认判定。</summary>
    Pair
}

[System.Serializable]
public class SlotGroup
{
    [Tooltip("组名，仅用于结算画面与日志显示")]
    public string groupName = "Group";

    [Tooltip("本组的槽位容器。填了它，运行时自动收集它下面所有 CardSlot，无需手工拖槽位")]
    public GameObject container;

    [Tooltip("本组合法的牌型（可多选）。结算时逐项判定，任意一项命中即本组合规。" +
             "留空 = 按槽位数自动填充：2 槽组为「雀头」，其余为「等差/等比/斐波那契」")]
    public List<SequenceType> candidateTypes = new List<SequenceType>();

    [Tooltip("本组包含的槽位（可手工拖入）。container 非空时，这份列表会被自动收集结果覆盖")]
    public List<CardSlot> slots = new List<CardSlot>();

    /// <summary>本组实际的槽位数量。</summary>
    public int SlotCount => slots != null ? slots.Count : 0;

    /// <summary>本组是否是「雀头组」（2 槽组）。雀头不参与数列类的加分规则。</summary>
    public bool IsPairGroup => SlotCount > 0 && SlotCount <= ScoreRules.PairSize;

    /// <summary>
    /// 解析出本组的槽位列表。
    /// container 有值 → 自动收集它下面所有 CardSlot（含未激活的，因为面板初始可能是关掉的）；
    /// container 为空   → 保留手工拖入的 slots。
    /// </summary>
    public void BuildSlots()
    {
        if (container != null)
        {
            // includeInactive: true —— gamePanel 初始是隐藏的，不带上这个参数会一个都收集不到
            CardSlot[] found = container.GetComponentsInChildren<CardSlot>(true);

            slots = new List<CardSlot>(found.Length);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null) slots.Add(found[i]);
            }
        }

        if (slots == null) slots = new List<CardSlot>();
    }

    /// <summary>
    /// 保证候选类型列表非空。
    /// 列表为空时按槽位数填默认值——这样即使场景里存的是旧数据（本次改动前存的字段），
    /// 也不需要你再手工配一遍。
    /// </summary>
    /// <param name="force">true = 忽略现有内容，强制重填默认值（供 Inspector 右键菜单使用）。</param>
    public void EnsureCandidateTypes(bool force = false)
    {
        if (candidateTypes == null)
        {
            candidateTypes = new List<SequenceType>();
        }

        if (force || candidateTypes.Count == 0)
        {
            candidateTypes = DefaultCandidateTypes(SlotCount);
        }
    }

    /// <summary>
    /// 按槽位数给出默认的候选类型：
    ///   2 槽组 → 只有「雀头」（两张数字相同的牌）；
    ///   其余   → 等差 / 等比 / 斐波那契（正好三种，即每组判三次）。
    /// </summary>
    public static List<SequenceType> DefaultCandidateTypes(int slotCount)
    {
        List<SequenceType> types = new List<SequenceType>();

        if (slotCount > 0 && slotCount <= ScoreRules.PairSize)
        {
            types.Add(SequenceType.Pair);
        }
        else
        {
            types.Add(SequenceType.Arithmetic);
            types.Add(SequenceType.Geometric);
            types.Add(SequenceType.Fibonacci);
        }
        return types;
    }
}

/// <summary>
/// 一组槽位的判定结果。用于结算明细文字，也方便以后接 UI 或做统计。
/// 刻意不做成 [System.Serializable]：这是运行时产物，不需要被 Unity 序列化。
/// </summary>
public class GroupJudgeResult
{
    /// <summary>组名。</summary>
    public string groupName;

    /// <summary>本组读到的数字（只含已放入卡牌的槽位，顺序 = 槽位顺序）。</summary>
    public List<int> values = new List<int>();

    /// <summary>本组实际槽位数。</summary>
    public int slotCount;

    /// <summary>整组是否放满。没放满的组不参与判定。</summary>
    public bool isFull;

    /// <summary>实际执行了判定的类型。</summary>
    public List<SequenceType> evaluatedTypes = new List<SequenceType>();

    /// <summary>因张数不足被跳过的类型（例如 2 张牌判等差没有意义）。</summary>
    public List<SequenceType> skippedTypes = new List<SequenceType>();

    /// <summary>所有命中的类型（可能不止一种）。</summary>
    public List<SequenceType> matchedTypes = new List<SequenceType>();

    /// <summary>本组是否合规（放满 + 至少命中一种类型）。</summary>
    public bool matched;
}

/// <summary>
/// 全部加分规则的分值。集中放在这里，方便与需求逐条对照、逐条调整。
/// 所有数值都是「额外获得」的分值，最终得分 = 各条命中项之和。
/// </summary>
public static class ScoreRules
{
    // —— 部署形状 ——
    /// <summary>雀头（2 槽组）的槽位数。</summary>
    public const int PairSize = 2;
    /// <summary>数列的条数（3 个 4 槽组）。</summary>
    public const int SequenceCount = 3;

    // —— 底分与时机 ——
    /// <summary>手牌能合规放置的底分。</summary>
    public const int BaseLegalPlacement = 1;
    /// <summary>第 1 回合就判定成功的额外分。</summary>
    public const int FirstTurnSuccess = 100;

    // —— 所有数的种类 ——
    /// <summary>所有数都是奇数、或都是偶数。</summary>
    public const int AllSameParity = 10;
    /// <summary>所有数都是质数。</summary>
    public const int AllPrime = 100;
    /// <summary>所有数都是合数。</summary>
    public const int AllComposite = 1;

    // —— 所有数列的种类 ——
    /// <summary>三条数列全部是等差。</summary>
    public const int AllSequencesArithmetic = 5;
    /// <summary>三条数列全部是等比。</summary>
    public const int AllSequencesGeometric = 50;
    /// <summary>三条数列全部是斐波那契。</summary>
    public const int AllSequencesFibonacci = 15;
    /// <summary>三条数列恰好分别为等差 / 等比 / 斐波那契各一个。</summary>
    public const int OneEachOfThreeKinds = 30;

    // —— 数列的长度与重叠 ——
    /// <summary>三个等差数列恰好拼成一条 12 项等差数列。</summary>
    public const int ThreeArithmeticInto12Terms = 30;
    /// <summary>存在两个等差数列恰好拼成一条 8 项等差数列。</summary>
    public const int TwoArithmeticInto8Terms = 10;
    /// <summary>存在两个斐波那契数列恰好拼成一条 8 项斐波那契数列。</summary>
    public const int TwoFibonacciInto8Terms = 100;
    /// <summary>存在两条完全相等的数列。</summary>
    public const int TwoIdenticalSequences = 36;
    /// <summary>某个数字同时作为两条数列中的一项（每个数字各加一次，可叠加）。</summary>
    public const int SharedValuePerNumber = 6;

    /// <summary>「三个等差拼成一条」的目标项数。</summary>
    public const int MergedThreeArithmeticTerms = 12;
    /// <summary>「两个数列拼成一条」的目标项数（等差 / 斐波那契共用 8）。</summary>
    public const int MergedTwoSequenceTerms = 8;

    // —— 公差 / 公比 ——
    /// <summary>存在两条公差相同的等差数列。</summary>
    public const int TwoSameArithmeticDifference = 10;
    /// <summary>三条等差数列公差均相同（比上面那条更强，二者只取其一）。</summary>
    public const int ThreeSameArithmeticDifference = 20;
    /// <summary>存在两条公比相同的等比数列。</summary>
    public const int TwoSameGeometricRatio = 50;
    /// <summary>三条等比数列公比均相同（比上面那条更强，二者只取其一）。</summary>
    public const int ThreeSameGeometricRatio = 100;

    // —— 雀头 ——
    /// <summary>雀头为质数。</summary>
    public const int PairIsPrime = 1;
    /// <summary>雀头为 1 或 100。</summary>
    public const int PairIsOneOrHundred = 10;
}

/// <summary>结算时的一条「数列」= 一个非雀头槽位组。</summary>
public class SequenceLine
{
    /// <summary>组名，仅用于显示。</summary>
    public string name = string.Empty;

    /// <summary>
    /// 这条数列对应的槽位组索引（对应 SlotManager.slotGroups 的下标）。
    /// 用途：把加分项「归因」到具体的组，从而算出每组的分数。
    /// -1 = 未知（离线用例不填时即为此值），会被当成手牌级加分处理。
    /// </summary>
    public int groupIndex = -1;

    /// <summary>这条数列里的数字。</summary>
    public List<int> values = new List<int>();

    /// <summary>这条数列命中的类型（可能不止一种，例如 2,4,6 同时是等差与斐波那契）。</summary>
    public List<SequenceType> matchedTypes = new List<SequenceType>();

    /// <summary>这条数列是否命中了指定类型。</summary>
    public bool HasMatchedType(SequenceType type)
    {
        return matchedTypes != null && matchedTypes.Contains(type);
    }

    /// <summary>这条数列是否至少命中一种类型（即是否合规）。</summary>
    public bool HasAnyMatch => matchedTypes != null && matchedTypes.Count > 0;
}

/// <summary>一手牌的布局：若干条数列 + 一个雀头 + 是否所有槽位都放满。</summary>
public class HandLayout
{
    /// <summary>三条数列（4 槽组）。</summary>
    public List<SequenceLine> sequences = new List<SequenceLine>();

    /// <summary>雀头（2 槽组的两个数字）。合规时两个数字必须相同。</summary>
    public List<int> pair = new List<int>();

    /// <summary>是否所有槽位都放满了。</summary>
    public bool allSlotsFilled;

    /// <summary>
    /// 槽位组总数（= 场景里 slotGroups 的条数）。
    /// </summary>
    public int groupCount;

    /// <summary>雀头所在的组索引（-1 = 未指定，默认取最后一组）。</summary>
    public int pairGroupIndex = -1;
}

/// <summary>一条加分项。</summary>
public class ScoreItem
{
    /// <summary>加分说明（会显示在结算画面上）。</summary>
    public string label;

    /// <summary>分值。</summary>
    public int points;

    /// <summary>
    /// 这条加分项归属的组索引（可多个）。
    ///   空列表 = 手牌级加分（底分、回合、数字种类、数列种类等），显示时平摊到全部组；
    ///   非空   = 只在列出的组之间分摊（例如「两条公差相同」归到这两条数列所在的组）。
    /// 只影响「每组分数」的分解，不影响总分的计算。
    /// </summary>
    public List<int> ownerGroups = new List<int>();

    public ScoreItem(string label, int points)
    {
        this.label = label;
        this.points = points;
    }
}

/// <summary>一手牌的结算结果。</summary>
public class HandScoreResult
{
    /// <summary>这手牌是否「合规放置」。</summary>
    public bool legal;

    /// <summary>本次总得分。布局不合规时为 0。</summary>
    public int total;

    /// <summary>命中的加分项（未命中任何一项时为空列表）。</summary>
    public List<ScoreItem> items = new List<ScoreItem>();

    /// <summary>
    /// 每组的分数，长度 = 组数，**各项之和恒等于 total**（分摊时余数补给索引最小的组）。
    /// 结算画面按索引 0→N-1 依次显示这几个数字，全部显示完再显示 total。
    /// </summary>
    public List<int> groupScores = new List<int>();

    /// <summary>每组的显示名，长度与 groupScores 一致。</summary>
    public List<string> groupNames = new List<string>();

    /// <summary>取某一组的分数（索引越界或未摊分时返回 0）。</summary>
    public int GroupScoreOf(int index)
    {
        return (groupScores != null && index >= 0 && index < groupScores.Count) ? groupScores[index] : 0;
    }

    /// <summary>取某一组的显示名（越界时返回「第 N 组」）。</summary>
    public string GroupNameOf(int index)
    {
        if (groupNames != null && index >= 0 && index < groupNames.Count && !string.IsNullOrEmpty(groupNames[index]))
        {
            return groupNames[index];
        }
        return "第 " + (index + 1) + " 组";
    }
}

/// <summary>
/// 槽位管理器。
///
/// 功能（对应文本中的 SlotManager）：
///   1. 位置检测：FindSlotAt —— 供 DragHandler 判断卡牌落在哪个槽位；
///   2. 读卡：把槽位里的数字读成 List，交给数列判定；
///   3. 数列判定：对每个组逐项判定它配置的候选类型（等差 / 等比 / 斐波那契 / 质数 / 雀头）；
///   4. 结算分数：把整手牌交给 EvaluateHand，按下面的规则算总分。
///
/// 【加分规则（全部在 EvaluateHand 里实现，纯静态、可离线回归）】
/// 前置：三条数列都命中 + 雀头是两张相同的牌 + 所有槽位放满，才叫「合规放置」，
///       不合规则本次得 0 分（后续所有加分项都不再计算）。
/// </summary>
public class SlotManager : MonoBehaviour
{
    public static SlotManager instance;

    [Header("槽位分组（部署：3 × 4 + 1 × 2）")]
    [Tooltip("逐组配置容器与合法牌型。把每组槽位的父物体拖到该组的 container 上即可")]
    public List<SlotGroup> slotGroups = new List<SlotGroup>
    {
        new SlotGroup { groupName = "第 1 组" },
        new SlotGroup { groupName = "第 2 组" },
        new SlotGroup { groupName = "第 3 组" },
        new SlotGroup { groupName = "第 4 组" },
    };

    [Header("上次结算的结果（只读，供结算画面显示）")]
    [Tooltip("最近一次 CalculateScore 生成的文字明细（逐组判定 + 加分明细）")]
    public string lastResultSummary = string.Empty;

    [Tooltip("最近一次 CalculateScore 的结构化结果，逐组一项（运行时产物，不参与序列化）")]
    [System.NonSerialized] public List<GroupJudgeResult> lastGroupResults = new List<GroupJudgeResult>();

    [Tooltip("最近一次结算的完整加分结果（运行时产物，不参与序列化）")]
    [System.NonSerialized] public HandScoreResult lastScoreResult = new HandScoreResult();

    /// <summary>槽位是否已经扫描过。</summary>
    private bool slotsBuilt;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Debug.LogWarning("[SlotManager] 场景中存在多个 SlotManager，已禁用重复的那个。");
            enabled = false;
            return;
        }

        // 在 Awake 里收集：Unity 保证所有 Awake 都先于任何 Start 执行，
        // 所以 GameManager.Start() → InitializeGame() → ClearAllSlots() 时槽位一定已经就绪。
        BuildSlots();
    }

    // ------------------------------------------------------------------
    // 槽位扫描
    // ------------------------------------------------------------------

    /// <summary>
    /// 扫描所有分组，重建每组的槽位列表，并把组号 / 组内序号回写到每个 CardSlot。
    /// 在 Inspector 里给组件右键「重新扫描槽位」也可以手动触发（运行时改完层级后用得上）。
    /// </summary>
    [ContextMenu("重新扫描槽位")]
    public void BuildSlots()
    {
        int total = 0;

        for (int g = 0; g < slotGroups.Count; g++)
        {
            SlotGroup group = slotGroups[g];
            if (group == null) continue;

            group.BuildSlots();

            for (int s = 0; s < group.slots.Count; s++)
            {
                CardSlot slot = group.slots[s];
                if (slot == null) continue;

                slot.groupIndex = g;   // 回写归属，方便调试与后续扩展
                slot.slotIndex = s;
                total++;
            }

            // 候选类型列表为空时按槽位数自动填充（场景里存的旧数据不需要你手工再配一遍）
            group.EnsureCandidateTypes();

            if (group.SlotCount == 0)
            {
                Debug.LogWarning(string.Format(
                    "[SlotManager] 【{0}】一个槽位都没找到。请把该组的槽位容器拖到 container 上，" +
                    "或手工往 slots 里拖入挂了 CardSlot 的物体。", group.groupName));
            }
        }

        slotsBuilt = true;
        Debug.Log(string.Format("[SlotManager] 槽位扫描完成：{0} 组，共 {1} 个槽位。", slotGroups.Count, total));
    }

    /// <summary>
    /// 把每组的候选类型重置为默认值（2 槽组 = 雀头，其余 = 等差/等比/斐波那契），
    /// 并写入场景，方便在 Inspector 里直接看到、再按需调整。
    /// 注意：这会覆盖你手工改过的候选列表。
    /// </summary>
    [ContextMenu("按槽位数填充默认判定类型（会覆盖现有配置）")]
    public void FillDefaultCandidateTypes()
    {
        BuildSlots();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.Undo.RecordObject(this, "填充默认判定类型");
        }
#endif

        for (int g = 0; g < slotGroups.Count; g++)
        {
            SlotGroup group = slotGroups[g];
            if (group == null) continue;

            group.EnsureCandidateTypes(force: true);
            Debug.Log(string.Format("[SlotManager] 【{0}】{1} 个槽位 → 判定 {2}。",
                group.groupName, group.SlotCount, TypeNamesOf(group.candidateTypes)));
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

    /// <summary>惰性扫描：任何用到槽位的公开接口都会先确保槽位已经收集过。</summary>
    private void EnsureSlots()
    {
        if (!slotsBuilt) BuildSlots();
    }

    /// <summary>全部槽位总数（只读，供外部校验部署是否符合 3*4+2）。</summary>
    public int TotalSlotCount
    {
        get
        {
            int n = 0;
            for (int g = 0; g < slotGroups.Count; g++)
            {
                if (slotGroups[g] != null) n += slotGroups[g].SlotCount;
            }
            return n;
        }
    }

    // ------------------------------------------------------------------
    // 一、位置检测（供 DragHandler 调用）
    // ------------------------------------------------------------------

    /// <summary>
    /// 返回屏幕点下方的槽位；没有任何槽位命中时返回 null。
    /// 用矩形包含判定，不依赖 EventSystem 射线，因此槽位不挂 Image 也有效。
    /// </summary>
    public CardSlot FindSlotAt(Vector2 screenPoint, Camera eventCamera)
    {
        EnsureSlots();

        for (int g = 0; g < slotGroups.Count; g++)
        {
            SlotGroup group = slotGroups[g];
            if (group == null || group.slots == null) continue;

            for (int s = 0; s < group.slots.Count; s++)
            {
                CardSlot slot = group.slots[s];
                if (slot == null || !slot.gameObject.activeInHierarchy) continue;

                RectTransform rt = slot.Rect;
                if (rt == null) continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, eventCamera))
                {
                    return slot;
                }
            }
        }
        return null;
    }

    // ------------------------------------------------------------------
    // 二、读卡
    // ------------------------------------------------------------------

    /// <summary>读卡：返回某个组里所有已放入卡牌的数值列表（空槽位会被跳过）。</summary>
    public List<int> ReadGroupValues(SlotGroup group)
    {
        EnsureSlots();

        List<int> values = new List<int>();
        if (group == null || group.slots == null) return values;

        for (int i = 0; i < group.slots.Count; i++)
        {
            CardSlot slot = group.slots[i];
            if (slot != null && !slot.IsEmpty)
            {
                values.Add(slot.Value);
            }
        }
        return values;
    }

    /// <summary>读卡：返回全部槽位里所有已放入卡牌的数值列表。</summary>
    public List<int> ReadAllSlotValues()
    {
        EnsureSlots();

        List<int> values = new List<int>();
        for (int g = 0; g < slotGroups.Count; g++)
        {
            values.AddRange(ReadGroupValues(slotGroups[g]));
        }
        return values;
    }

    /// <summary>读卡（数据对象版本）：需要拿到 NumberCardData 时使用，方便后续扩展自定义判定。</summary>
    public List<NumberCardData> ReadGroupCards(SlotGroup group)
    {
        EnsureSlots();

        List<NumberCardData> cards = new List<NumberCardData>();
        if (group == null || group.slots == null) return cards;

        for (int i = 0; i < group.slots.Count; i++)
        {
            CardSlot slot = group.slots[i];
            if (slot != null && slot.currentCard != null && slot.currentCard.data != null)
            {
                cards.Add(slot.currentCard.data);
            }
        }
        return cards;
    }

    // ------------------------------------------------------------------
    // 三、结算分数
    // ------------------------------------------------------------------

    /// <summary>
    /// 结算分数：逐组读卡判定，再把整手牌交给 EvaluateHand 统一算分。
    ///
    /// 判定与加分的分工：
    ///   JudgeGroup       —— 回答「这一组里的牌是否合规」（命中至少一种候选类型）；
    ///   EvaluateHand     —— 回答「整手牌拿多少分」（合规底分 + 各条额外加分）。
    /// 明细写入 lastResultSummary（逐组判定 + 加分明细），结构化结果写入 lastGroupResults / lastScoreResult。
    /// </summary>
    /// <param name="turnCount">当前回合数，用于「第 1 回合判定成功 +100」。</param>
    public int CalculateScore(int turnCount)
    {
        EnsureSlots();

        lastGroupResults.Clear();

        HandLayout layout = new HandLayout();
        layout.allSlotsFilled = true;
        layout.groupCount = slotGroups.Count;   // 每组的分数就按这个数量分摊（本工程 = 4）

        for (int g = 0; g < slotGroups.Count; g++)
        {
            SlotGroup group = slotGroups[g];
            if (group == null) continue;

            GroupJudgeResult result = JudgeGroup(group);
            lastGroupResults.Add(result);

            // 只要有一组没放满，整手牌就不算「合规放置」
            if (!result.isFull) layout.allSlotsFilled = false;

            SequenceLine line = new SequenceLine();
            line.name = result.groupName;
            line.groupIndex = g;                 // 归因用：这条数列属于第 g 组
            line.values = result.values;
            line.matchedTypes = result.matchedTypes;

            // 2 槽组 = 雀头，不参与数列类的加分规则
            if (group.IsPairGroup)
            {
                layout.pair = result.values;
                layout.pairGroupIndex = g;
            }
            else
            {
                layout.sequences.Add(line);
            }
        }

        HandScoreResult score = EvaluateHand(layout, turnCount);

        // 把组名换成场景里配的名字（EvaluateHand 是纯静态的，不认识 SlotGroup）
        score.groupNames.Clear();
        for (int g = 0; g < slotGroups.Count; g++)
        {
            SlotGroup group = slotGroups[g];
            score.groupNames.Add(group != null && !string.IsNullOrEmpty(group.groupName)
                ? group.groupName
                : "第 " + (g + 1) + " 组");
        }

        lastScoreResult = score;
        lastResultSummary = ComposeSummary(lastGroupResults, score);

        return score.total;
    }

    /// <summary>清空所有槽位的占用标记（结算后调用）。</summary>
    public void ClearAllSlots()
    {
        EnsureSlots();

        for (int g = 0; g < slotGroups.Count; g++)
        {
            SlotGroup group = slotGroups[g];
            if (group == null || group.slots == null) continue;

            for (int s = 0; s < group.slots.Count; s++)
            {
                if (group.slots[s] != null) group.slots[s].Clear();
            }
        }
    }

    // ------------------------------------------------------------------
    // 四、单组判定（每组逐类型判定 N 次）
    // ------------------------------------------------------------------

    /// <summary>
    /// 判定一个组：对它的候选类型逐项调用判定，任意一项命中即本组合规。
    ///
    /// 两种「不判」的情况：
    ///   1. 整组没放满 → 直接返回，不判定；
    ///   2. 某个候选类型所需张数不够（见 MinimumValueCount）→ 跳过该类型，不计入命中。
    ///      例如给 2 槽组配了「等差」，2 张牌必然等差，会白送分，所以直接跳过。
    /// </summary>
    public GroupJudgeResult JudgeGroup(SlotGroup group)
    {
        EnsureSlots();

        GroupJudgeResult result = new GroupJudgeResult();

        if (group == null)
        {
            result.groupName = "(空组)";
            return result;
        }

        group.EnsureCandidateTypes();

        result.groupName = group.groupName;
        result.slotCount = group.SlotCount;
        result.values = ReadGroupValues(group);
        result.isFull = group.SlotCount > 0 && result.values.Count >= group.SlotCount;

        // 没放满的组不判定
        if (!result.isFull)
        {
            return result;
        }

        // 逐项判定：4 槽组默认就是三次（等差 / 等比 / 斐波那契）
        EvaluateTypes(group.candidateTypes, result.values,
            result.evaluatedTypes, result.skippedTypes, result.matchedTypes);

        // 命中即合规（本组不再单独加分，加分由 EvaluateHand 统一处理）
        result.matched = result.matchedTypes.Count > 0;
        return result;
    }

    /// <summary>
    /// 逐项判定一组候选类型——「每组判 N 次」的核心就是这一个循环。
    /// 抽成纯静态函数（不碰任何 Unity 对象），可以脱离运行时直接跑用例回归。
    /// 三种结果的区分：
    ///   evaluatedTypes = 真的判定过的类型；
    ///   skippedTypes   = 张数不够、没判的类型；
    ///   matchedTypes   = 判定通过的类型（可能不止一个）。
    /// </summary>
    public static void EvaluateTypes(
        List<SequenceType> candidateTypes,
        List<int> values,
        List<SequenceType> evaluatedTypes,
        List<SequenceType> skippedTypes,
        List<SequenceType> matchedTypes)
    {
        if (evaluatedTypes != null) evaluatedTypes.Clear();
        if (skippedTypes != null) skippedTypes.Clear();
        if (matchedTypes != null) matchedTypes.Clear();

        if (candidateTypes == null || values == null) return;

        for (int i = 0; i < candidateTypes.Count; i++)
        {
            SequenceType type = candidateTypes[i];

            // 张数不够就不判：例如 2 张牌必然满足等差与等比，判了等于白送分
            if (values.Count < MinimumValueCount(type))
            {
                if (skippedTypes != null) skippedTypes.Add(type);
                continue;
            }

            if (evaluatedTypes != null) evaluatedTypes.Add(type);

            if (IsMatch(type, values) && matchedTypes != null)
            {
                matchedTypes.Add(type);
            }
        }
    }

    /// <summary>
    /// 某个类型判定所需的最少张数。张数不足时判定没有意义，直接跳过。
    ///   等差 / 等比 / 斐波那契：3 张（2 张必然满足等差与等比，会白送分）
    ///   质数 / 特殊：1 张
    ///   雀头：2 张（且必须恰好 2 张，见 IsPair）
    /// </summary>
    public static int MinimumValueCount(SequenceType type)
    {
        switch (type)
        {
            case SequenceType.Arithmetic:
            case SequenceType.Geometric:
            case SequenceType.Fibonacci:
                return 3;

            case SequenceType.Pair:
                return ScoreRules.PairSize;

            case SequenceType.Prime:
            case SequenceType.Special:
                return 1;

            default:
                return 1;
        }
    }

    /// <summary>
    /// 把一次判定结果写成一行中文明细（结算画面用）。
    /// 本组不再单独加分，所以这里不显示分数，只显示「命中了哪种类型」。
    /// 做成 public 静态方法，方便脱离运行时直接验证文案与分支。
    /// </summary>
    public static string DescribeGroupResult(GroupJudgeResult result)
    {
        if (result == null) return string.Empty;

        string values = result.values.Count > 0 ? string.Join(", ", result.values) : "空";

        if (!result.isFull)
        {
            return string.Format("{0} [{1}] 未放满（{2}/{3}），不参与判定",
                result.groupName, values, result.values.Count, result.slotCount);
        }

        string tried = TypeNamesOf(result.evaluatedTypes);
        if (result.skippedTypes.Count > 0)
        {
            tried += "，跳过" + TypeNamesOf(result.skippedTypes) + "（张数不足）";
        }

        if (result.matched)
        {
            return string.Format("{0} [{1}] 判定→ 命中【{3}】",
                result.groupName, values, tried, TypeNamesOf(result.matchedTypes));
        }

        return string.Format("{0} [{1}] 判定→ 均未命中", result.groupName, values, tried);
    }

    /// <summary>
    /// 把「逐组判定 + 加分明细」拼成结算画面上的文字。
    /// 抽成静态函数便于离线比对文案。
    /// </summary>
    public static string ComposeSummary(List<GroupJudgeResult> groups, HandScoreResult score)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("【数列判定】");
        if (groups == null || groups.Count == 0)
        {
            sb.AppendLine("（无槽位）");
        }
        else
        {
            for (int i = 0; i < groups.Count; i++)
            {
                sb.AppendLine(DescribeGroupResult(groups[i]));
            }
        }

        sb.AppendLine();
        sb.AppendLine("【加分明细】");

        if (score == null)
        {
            sb.AppendLine("（无）");
            return sb.ToString();
        }

        if (!score.legal)
        {
            sb.AppendLine("布局不合规（有组没放满 / 没命中任何类型 / 雀头不是两张相同的牌），本次不得分。");
            sb.AppendLine("本次合计  +0");
            return sb.ToString();
        }

        if (score.items.Count == 0)
        {
            sb.AppendLine("（无额外加分项）");
        }
        else
        {
            for (int i = 0; i < score.items.Count; i++)
            {
                ScoreItem item = score.items[i];
                sb.AppendLine(string.Format("{0}  +{1}", item.label, item.points));
            }
        }

        sb.AppendLine(string.Format("本次合计  +{0}", score.total));

        // 各组得分：与结算画面上那 4 个逐组跳分的文本完全一致，便于玩家对账
        if (score.groupScores != null && score.groupScores.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【各组得分】（依次显示的就是这几项）");

            for (int i = 0; i < score.groupScores.Count; i++)
            {
                sb.AppendLine(string.Format("{0}  +{1}", score.GroupNameOf(i), score.groupScores[i]));
            }
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // 五、整手算分（全部加分规则的实现，纯静态、可离线回归）
    // ------------------------------------------------------------------

    /// <summary>
    /// 顺序：先判「是否合规放置」——不合规直接 0 分，后面所有加分项都不算。
    /// 合规之后按需求文本的顺序逐条累加：底分 → 时机 → 数字种类 → 数列种类 →
    /// 长度与重叠 → 公差公比 → 雀头。
    /// </summary>
    /// <param name="layout">手牌布局（三条数列 + 雀头 + 是否放满 + 组数 / 各组索引）。</param>
    /// <param name="turnCount">判定时的回合数。</param>
    public static HandScoreResult EvaluateHand(HandLayout layout, int turnCount)
    {
        HandScoreResult result = new HandScoreResult();
        if (layout == null) return result;

        // 组数：优先用布局里显式指定的（= 场景里 slotGroups 的条数），否则按「三条数列 + 雀头」推断
        int groupCount = layout.groupCount > 0 ? layout.groupCount : layout.sequences.Count + 1;

        result.groupScores = new List<int>(new int[groupCount]);
        result.groupNames = new List<string>(new string[groupCount]);
        for (int g = 0; g < groupCount; g++) result.groupNames[g] = "第 " + (g + 1) + " 组";

        // 雀头所在组：布局没指定时取最后一组（场景里 2 槽组就是第 4 组）
        int pairGroup = (layout.pairGroupIndex >= 0 && layout.pairGroupIndex < groupCount)
            ? layout.pairGroupIndex
            : groupCount - 1;

        // ---- 前置：合规则得底分，不合规则 0 分 ----
        result.legal = IsLegalLayout(layout);
        if (!result.legal)
        {
            result.total = 0;
            return result;      // groupScores 保持全 0，与 total 一致
        }

        List<ScoreItem> items = result.items;
        List<SequenceLine> seqs = layout.sequences;
        List<SequenceLine> arithSeqs = FilterByType(seqs, SequenceType.Arithmetic);
        List<SequenceLine> geoSeqs = FilterByType(seqs, SequenceType.Geometric);
        List<SequenceLine> fibSeqs = FilterByType(seqs, SequenceType.Fibonacci);

        // ---- 1. 底分 ----
        Add(items, "手牌合规放置底分", ScoreRules.BaseLegalPlacement);

        // ---- 2. 判定时机 ----
        if (turnCount <= 1)
        {
            Add(items, "第 1 回合即判定成功", ScoreRules.FirstTurnSuccess);
        }

        // ---- 3. 所有数的种类（数列 + 雀头里的全部数字） ----
        List<int> allNumbers = CollectAllNumbers(layout);

        if (AllSameParity(allNumbers))
        {
            Add(items, "所有数都是奇数或偶数", ScoreRules.AllSameParity);
        }
        if (AllPrimeNumbers(allNumbers))
        {
            Add(items, "所有数都是质数", ScoreRules.AllPrime);
        }
        else if (AllCompositeNumbers(allNumbers))
        {
            Add(items, "所有数都是合数", ScoreRules.AllComposite);
        }

        // ---- 4. 所有数列的种类 ----
        if (AllMatchType(seqs, SequenceType.Arithmetic))
        {
            Add(items, "所有数列都是等差数列", ScoreRules.AllSequencesArithmetic);
        }
        if (AllMatchType(seqs, SequenceType.Geometric))
        {
            Add(items, "所有数列都是等比数列", ScoreRules.AllSequencesGeometric);
        }
        if (AllMatchType(seqs, SequenceType.Fibonacci))
        {
            Add(items, "所有数列都是斐波那契数列", ScoreRules.AllSequencesFibonacci);
        }
        if (HasOneOfEachOfThreeKinds(seqs))
        {
            Add(items, "三条数列恰好分别为等差 / 等比 / 斐波那契各一个", ScoreRules.OneEachOfThreeKinds);
        }

        // ---- 5. 数列的长度与重叠 ----
        // 5.1 三个等差数列拼成一条 12 项等差数列（拼出来的项数 = 3 × 4 = 12）
        //     加分归到这三条数列所在的组（各摊 1/3）
        if (arithSeqs.Count == ScoreRules.SequenceCount && arithSeqs.Count == seqs.Count)
        {
            List<int> merged = ConcatValues(arithSeqs);
            if (merged.Count == ScoreRules.MergedThreeArithmeticTerms && IsArithmetic(merged))
            {
                Add(items, "三个等差数列拼成一条 12 项等差数列", ScoreRules.ThreeArithmeticInto12Terms,
                    GroupIndexesOf(arithSeqs));
            }
        }

        // 5.2 存在两个等差数列拼成一条 8 项等差数列（存在即可，只加一次）
        //     加分归到这两条所在的组（各摊一半）
        int mergeA, mergeB;
        if (TryFindMergePair(arithSeqs, ScoreRules.MergedTwoSequenceTerms, false, out mergeA, out mergeB))
        {
            Add(items, "两个等差数列拼成一条 8 项等差数列", ScoreRules.TwoArithmeticInto8Terms,
                arithSeqs[mergeA].groupIndex, arithSeqs[mergeB].groupIndex);
        }

        // 5.3 存在两个斐波那契数列拼成一条 8 项斐波那契数列
        if (TryFindMergePair(fibSeqs, ScoreRules.MergedTwoSequenceTerms, true, out mergeA, out mergeB))
        {
            Add(items, "两个斐波那契数列拼成一条 8 项斐波那契数列", ScoreRules.TwoFibonacciInto8Terms,
                fibSeqs[mergeA].groupIndex, fibSeqs[mergeB].groupIndex);
        }

        // 5.4 存在两条完全相等的数列（存在即可，只加一次）
        int equalA, equalB;
        if (TryFindIdenticalPair(seqs, out equalA, out equalB))
        {
            Add(items, "存在两条完全相等的数列", ScoreRules.TwoIdenticalSequences,
                seqs[equalA].groupIndex, seqs[equalB].groupIndex);
        }

        // 5.5 某个数字同时作为两条数列中的一项（每个这样的数字各 +6，可叠加）
        //     加分归到「参与了共享」的那几条数列所在的组
        int sharedCount = CountSharedValues(seqs);
        if (sharedCount > 0)
        {
            Add(items, string.Format("有 {0} 个数字同时出现在两条数列中", sharedCount),
                sharedCount * ScoreRules.SharedValuePerNumber, CollectSharedValueGroups(seqs));
        }

        // ---- 6. 公差 / 公比 ----
        // 分层取值：三条均相同只算 +20 / +100，不再叠加「两条相同」的 +10 / +50
        int diffA, diffB;
        if (arithSeqs.Count == ScoreRules.SequenceCount && AllSameCommonDifference(arithSeqs))
        {
            Add(items, "三条等差数列公差均相同", ScoreRules.ThreeSameArithmeticDifference,
                GroupIndexesOf(arithSeqs));
        }
        else if (TryFindSameDifferencePair(arithSeqs, out diffA, out diffB))
        {
            Add(items, "存在两条公差相同的等差数列", ScoreRules.TwoSameArithmeticDifference,
                arithSeqs[diffA].groupIndex, arithSeqs[diffB].groupIndex);
        }

        int ratioA, ratioB;
        if (geoSeqs.Count == ScoreRules.SequenceCount && AllSameCommonRatio(geoSeqs))
        {
            Add(items, "三条等比数列公比均相同", ScoreRules.ThreeSameGeometricRatio,
                GroupIndexesOf(geoSeqs));
        }
        else if (TryFindSameRatioPair(geoSeqs, out ratioA, out ratioB))
        {
            Add(items, "存在两条公比相同的等比数列", ScoreRules.TwoSameGeometricRatio,
                geoSeqs[ratioA].groupIndex, geoSeqs[ratioB].groupIndex);
        }

        // ---- 7. 雀头（只归到雀头所在的那一组） ----
        int pairValue = layout.pair[0];
        if (pairValue == 1 || pairValue == 100)
        {
            Add(items, "雀头为 1 或 100", ScoreRules.PairIsOneOrHundred, pairGroup);
        }
        else if (NumberCardData.IsPrimeNumber(pairValue))
        {
            Add(items, "雀头为质数", ScoreRules.PairIsPrime, pairGroup);
        }

        // ---- 合计 ----
        // 一边累加总分，一边把每条加分项按归属摊到各组。
        // DistributePoints 保证「分摊之和 = 该条分值」，所以 groupScores 之和恒等于 total。
        int total = 0;
        for (int i = 0; i < items.Count; i++)
        {
            total += items[i].points;
            DistributePoints(items[i].points, items[i].ownerGroups, groupCount, result.groupScores);
        }
        result.total = total;
        return result;
    }

    /// <summary>
    /// 把一条加分项分摊到各组（「每组分数」的唯一实现）。
    ///   groups 非空且全部有效 → 只在列出的组之间分摊（自动去重）；
    ///   groups 为空 / 存在无效索引（-1 或越界）→ 视为手牌级加分，分摊到全部组。
    /// 只要出现无效索引就整条按手牌级处理，而不是「跳过无效的那几个」——
    /// 后者会让某个组的份额被静默丢掉，分摊之和就不再等于分值了。
    /// </summary>
    /// <param name="points">该条加分项的分值（非正数直接忽略）。</param>
    /// <param name="groups">归属的组索引列表，可为 null / 空。</param>
    /// <param name="groupCount">组总数。</param>
    /// <param name="groupScores">累加目标，长度必须 ≥ groupCount。</param>
    public static void DistributePoints(int points, List<int> groups, int groupCount, List<int> groupScores)
    {
        if (groupScores == null || points <= 0 || groupCount <= 0) return;
        if (groupScores.Count < groupCount) return;

        List<int> targets = new List<int>();
        bool allValid = groups != null && groups.Count > 0;

        if (allValid)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                int g = groups[i];
                if (g < 0 || g >= groupCount) { allValid = false; break; }
                if (!targets.Contains(g)) targets.Add(g);
            }
        }

        if (!allValid)
        {
            targets.Clear();
            for (int g = 0; g < groupCount; g++) targets.Add(g);
        }
        targets.Sort();

        int each = points / targets.Count;
        int remainder = points - each * targets.Count;      // points > 0 ⇒ remainder ≥ 0

        for (int i = 0; i < targets.Count; i++)
        {
            groupScores[targets[i]] += each + (i < remainder ? 1 : 0);
        }
    }

    /// <summary>取出一批数列所属的组索引（保持顺序，未去重；-1 由 DistributePoints 过滤掉）。</summary>
    public static List<int> GroupIndexesOf(List<SequenceLine> lines)
    {
        List<int> indexes = new List<int>();
        if (lines == null) return indexes;

        for (int i = 0; i < lines.Count; i++)
        {
            indexes.Add(lines[i] != null ? lines[i].groupIndex : -1);
        }
        return indexes;
    }

    /// <summary>
    /// 「合规放置」的判定：所有槽位放满 + 每条数列都命中至少一种类型 + 雀头是两张数字相同的牌。
    /// 不合规时本次得 0 分（需求文本里所有加分项都以「能合规放置」为前提）。
    /// </summary>
    public static bool IsLegalLayout(HandLayout layout)
    {
        if (layout == null || !layout.allSlotsFilled) return false;
        if (layout.sequences == null || layout.sequences.Count == 0) return false;

        for (int i = 0; i < layout.sequences.Count; i++)
        {
            if (!layout.sequences[i].HasAnyMatch) return false;
        }

        return IsPair(layout.pair);
    }

    /// <summary>雀头判定：恰好 2 个数字，且两个数字相同。</summary>
    public static bool IsPair(List<int> values)
    {
        return values != null && values.Count == ScoreRules.PairSize && values[0] == values[1];
    }

    /// <summary>加一条「手牌级」加分项：不归属任何组，显示时平摊到全部组。</summary>
    private static void Add(List<ScoreItem> items, string label, int points)
    {
        if (items == null || points == 0) return;
        items.Add(new ScoreItem(label, points));
    }

    /// <summary>加一条归属到单个组的加分项（例如雀头类加分只算在雀头那一组）。</summary>
    private static void Add(List<ScoreItem> items, string label, int points, int groupIndex)
    {
        if (items == null || points == 0) return;

        ScoreItem item = new ScoreItem(label, points);
        item.ownerGroups.Add(groupIndex);
        items.Add(item);
    }

    /// <summary>加一条归属到两个组的加分项（例如「存在两条公差相同的数列」）。</summary>
    private static void Add(List<ScoreItem> items, string label, int points, int groupA, int groupB)
    {
        if (items == null || points == 0) return;

        ScoreItem item = new ScoreItem(label, points);
        item.ownerGroups.Add(groupA);
        item.ownerGroups.Add(groupB);
        items.Add(item);
    }

    /// <summary>加一条归属到一组组的加分项（列表为空 = 等同于手牌级）。</summary>
    private static void Add(List<ScoreItem> items, string label, int points, List<int> groupIndexes)
    {
        if (items == null || points == 0) return;

        ScoreItem item = new ScoreItem(label, points);
        if (groupIndexes != null) item.ownerGroups.AddRange(groupIndexes);
        items.Add(item);
    }

    /// <summary>收集所有已放置的数字（三条数列 + 雀头）。</summary>
    public static List<int> CollectAllNumbers(HandLayout layout)
    {
        List<int> all = new List<int>();
        if (layout == null) return all;

        if (layout.sequences != null)
        {
            for (int i = 0; i < layout.sequences.Count; i++)
            {
                SequenceLine line = layout.sequences[i];
                if (line != null && line.values != null) all.AddRange(line.values);
            }
        }

        if (layout.pair != null) all.AddRange(layout.pair);
        return all;
    }

    /// <summary>是否所有数都是奇数、或者都是偶数。</summary>
    public static bool AllSameParity(List<int> values)
    {
        if (values == null || values.Count == 0) return false;

        bool allOdd = true;
        bool allEven = true;

        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] % 2 == 0) allOdd = false;
            else allEven = false;
        }
        return allOdd || allEven;
    }

    /// <summary>是否所有数都是质数。</summary>
    public static bool AllPrimeNumbers(List<int> values)
    {
        if (values == null || values.Count == 0) return false;

        for (int i = 0; i < values.Count; i++)
        {
            if (!NumberCardData.IsPrimeNumber(values[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// 是否所有数都是合数。
    /// 注意 1 既不是质数也不是合数，所以只要出现 1 就不算「都是合数」。
    /// </summary>
    public static bool AllCompositeNumbers(List<int> values)
    {
        if (values == null || values.Count == 0) return false;

        for (int i = 0; i < values.Count; i++)
        {
            int v = values[i];
            if (v <= 1 || NumberCardData.IsPrimeNumber(v)) return false;
        }
        return true;
    }

    /// <summary>是否每条数列都命中了指定类型。</summary>
    public static bool AllMatchType(List<SequenceLine> seqs, SequenceType type)
    {
        if (seqs == null || seqs.Count == 0) return false;

        for (int i = 0; i < seqs.Count; i++)
        {
            if (seqs[i] == null || !seqs[i].HasMatchedType(type)) return false;
        }
        return true;
    }

    /// <summary>挑出命中了指定类型的数列。</summary>
    public static List<SequenceLine> FilterByType(List<SequenceLine> seqs, SequenceType type)
    {
        List<SequenceLine> picked = new List<SequenceLine>();
        if (seqs == null) return picked;

        for (int i = 0; i < seqs.Count; i++)
        {
            if (seqs[i] != null && seqs[i].HasMatchedType(type)) picked.Add(seqs[i]);
        }
        return picked;
    }

    /// <summary>
    /// 三条数列是否恰好为等差 / 等比 / 斐波那契各一个。
    ///
    /// 判定方式：枚举三种类型到三条数列的所有一一对应（3! = 6 种），
    /// 只要存在一种对应让每条数列都命中被分配到的类型，就算成立。
    /// 用「命中类型集合」而不是「唯一类型」的原因：一条数列可能同时满足多种规则
    /// （例如 2,4,6 既是等差也是斐波那契），此时应允许它扮演其中任意一个角色。
    /// </summary>
    public static bool HasOneOfEachOfThreeKinds(List<SequenceLine> seqs)
    {
        if (seqs == null || seqs.Count != ScoreRules.SequenceCount) return false;

        SequenceType[] kinds =
        {
            SequenceType.Arithmetic,
            SequenceType.Geometric,
            SequenceType.Fibonacci
        };

        for (int a = 0; a < kinds.Length; a++)
        {
            for (int b = 0; b < kinds.Length; b++)
            {
                if (b == a) continue;
                for (int c = 0; c < kinds.Length; c++)
                {
                    if (c == a || c == b) continue;

                    if (seqs[0].HasMatchedType(kinds[a]) &&
                        seqs[1].HasMatchedType(kinds[b]) &&
                        seqs[2].HasMatchedType(kinds[c]))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>把若干条数列的数字拼成一个列表（保持传入顺序，不排序）。</summary>
    public static List<int> ConcatValues(params SequenceLine[] lines)
    {
        List<int> all = new List<int>();
        if (lines == null) return all;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] != null && lines[i].values != null) all.AddRange(lines[i].values);
        }
        return all;
    }

    /// <summary>把若干条数列的数字拼成一个列表（List 入参版本）。</summary>
    public static List<int> ConcatValues(List<SequenceLine> lines)
    {
        List<int> all = new List<int>();
        if (lines == null) return all;

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i] != null && lines[i].values != null) all.AddRange(lines[i].values);
        }
        return all;
    }

    /// <summary>
    /// 两条数列是否完全相等。比较的是「排序后的数字列表」——
    /// 玩家把 2,5,8,11 摆成 11,2,8,5 也是同一条数列，不应判为不同。
    /// </summary>
    public static bool SameValuesIgnoringOrder(List<int> a, List<int> b)
    {
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        if (a.Count == 0) return false;

        List<int> sa = new List<int>(a);
        List<int> sb = new List<int>(b);
        sa.Sort();
        sb.Sort();

        for (int i = 0; i < sa.Count; i++)
        {
            if (sa[i] != sb[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// 统计「每个数字出现在多少条数列里」。
    /// 同一个数字在同一条数列里出现多次只算一次（那不算「同属两条数列」）。
    /// </summary>
    private static Dictionary<int, int> BuildValueAppearCount(List<SequenceLine> seqs)
    {
        Dictionary<int, int> appearCount = new Dictionary<int, int>();
        if (seqs == null) return appearCount;

        for (int i = 0; i < seqs.Count; i++)
        {
            SequenceLine line = seqs[i];
            if (line == null || line.values == null) continue;

            HashSet<int> distinctInThisLine = new HashSet<int>();
            for (int v = 0; v < line.values.Count; v++) distinctInThisLine.Add(line.values[v]);

            foreach (int value in distinctInThisLine)
            {
                if (appearCount.ContainsKey(value)) appearCount[value]++;
                else appearCount[value] = 1;
            }
        }
        return appearCount;
    }

    /// <summary>
    /// 统计「同时出现在两条及以上数列中」的数字个数。
    /// 计算结果用于 +6 × 个数（可叠加）。
    /// </summary>
    public static int CountSharedValues(List<SequenceLine> seqs)
    {
        if (seqs == null || seqs.Count < 2) return 0;

        Dictionary<int, int> appearCount = BuildValueAppearCount(seqs);

        int shared = 0;
        foreach (KeyValuePair<int, int> kv in appearCount)
        {
            if (kv.Value >= 2) shared++;
        }
        return shared;
    }

    /// <summary>
    /// 收集「参与了共享数字」的组索引。
    /// 口径与 CountSharedValues 完全一致：某数列里只要含有一个出现在 ≥2 条数列中的数字，
    /// 这条数列所在的组就算参与，用于把「数字同属两条数列」的加成按组分摊。
    /// </summary>
    public static List<int> CollectSharedValueGroups(List<SequenceLine> seqs)
    {
        List<int> groups = new List<int>();
        if (seqs == null || seqs.Count < 2) return groups;

        Dictionary<int, int> appearCount = BuildValueAppearCount(seqs);

        for (int i = 0; i < seqs.Count; i++)
        {
            SequenceLine line = seqs[i];
            if (line == null || line.values == null) continue;

            HashSet<int> distinct = new HashSet<int>();
            for (int v = 0; v < line.values.Count; v++) distinct.Add(line.values[v]);

            bool participates = false;
            foreach (int value in distinct)
            {
                if (appearCount[value] >= 2) { participates = true; break; }
            }

            if (participates) groups.Add(line.groupIndex);
        }
        return groups;
    }

    /// <summary>
    /// 找出一对「完全相等的数列」（排序后数字列表相同）。
    /// 找到返回 true 并输出下标（保证 indexA &lt; indexB）；找不到输出 -1。
    /// </summary>
    public static bool TryFindIdenticalPair(List<SequenceLine> seqs, out int indexA, out int indexB)
    {
        indexA = -1;
        indexB = -1;
        if (seqs == null) return false;

        for (int i = 0; i < seqs.Count; i++)
        {
            for (int j = i + 1; j < seqs.Count; j++)
            {
                if (SameValuesIgnoringOrder(seqs[i].values, seqs[j].values))
                {
                    indexA = i;
                    indexB = j;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 找出两条「拼起来正好构成目标数列」的数列：
    /// 拼接后的数字个数 = targetTerms，且整体满足 fibonacci 指定的那套规则
    /// （true = 斐波那契，false = 等差）。找到返回 true 并输出下标（indexA &lt; indexB）。
    /// </summary>
    public static bool TryFindMergePair(List<SequenceLine> lines, int targetTerms, bool fibonacci, out int indexA, out int indexB)
    {
        indexA = -1;
        indexB = -1;
        if (lines == null || lines.Count < 2) return false;

        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = i + 1; j < lines.Count; j++)
            {
                List<int> merged = ConcatValues(lines[i], lines[j]);
                if (merged.Count != targetTerms) continue;

                bool ok = fibonacci ? IsFibonacci(merged) : IsArithmetic(merged);
                if (ok)
                {
                    indexA = i;
                    indexB = j;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>等差数列的公差（升序后相邻差）。调用前请先确认它是等差数列。</summary>
    public static int CommonDifference(List<int> values)
    {
        if (values == null || values.Count < 2) return 0;

        List<int> sorted = new List<int>(values);
        sorted.Sort();
        return sorted[1] - sorted[0];
    }

    /// <summary>是否所有等差数列的公差都相同（调用方负责保证条数）。</summary>
    public static bool AllSameCommonDifference(List<SequenceLine> arithSeqs)
    {
        if (arithSeqs == null || arithSeqs.Count < 2) return false;

        int diff = CommonDifference(arithSeqs[0].values);
        for (int i = 1; i < arithSeqs.Count; i++)
        {
            if (CommonDifference(arithSeqs[i].values) != diff) return false;
        }
        return true;
    }

    /// <summary>
    /// 找出两条公差相同的等差数列。找到返回 true 并输出下标（indexA &lt; indexB）；找不到输出 -1。
    /// </summary>
    public static bool TryFindSameDifferencePair(List<SequenceLine> arithSeqs, out int indexA, out int indexB)
    {
        indexA = -1;
        indexB = -1;
        if (arithSeqs == null || arithSeqs.Count < 2) return false;

        for (int i = 0; i < arithSeqs.Count; i++)
        {
            for (int j = i + 1; j < arithSeqs.Count; j++)
            {
                if (CommonDifference(arithSeqs[i].values) == CommonDifference(arithSeqs[j].values))
                {
                    indexA = i;
                    indexB = j;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>是否存在两条公差相同的等差数列。</summary>
    public static bool HasPairWithSameCommonDifference(List<SequenceLine> arithSeqs)
    {
        int a, b;
        return TryFindSameDifferencePair(arithSeqs, out a, out b);
    }

    /// <summary>
    /// 求等比数列的公比，输出约分后的分数 numerator / denominator。
    /// 用分数而不是浮点数，避免 4,6,9（公比 3/2）这类情形出现精度误差。
    /// </summary>
    public static bool TryGetCommonRatio(List<int> values, out long numerator, out long denominator)
    {
        numerator = 0;
        denominator = 1;

        if (values == null || values.Count < 2) return false;

        List<int> sorted = new List<int>(values);
        sorted.Sort();

        if (sorted[0] <= 0 || sorted[1] <= 0) return false;

        long n = sorted[1];
        long d = sorted[0];
        long g = Gcd(n, d);

        numerator = n / g;
        denominator = d / g;
        return true;
    }

    /// <summary>最大公约数（辗转相除，只用于公比约分，输入都是正数）。</summary>
    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            long t = a % b;
            a = b;
            b = t;
        }
        return a < 0 ? -a : a;
    }

    /// <summary>是否所有等比数列的公比都相同。</summary>
    public static bool AllSameCommonRatio(List<SequenceLine> geoSeqs)
    {
        if (geoSeqs == null || geoSeqs.Count < 2) return false;

        long n0, d0;
        if (!TryGetCommonRatio(geoSeqs[0].values, out n0, out d0)) return false;

        for (int i = 1; i < geoSeqs.Count; i++)
        {
            long n, d;
            if (!TryGetCommonRatio(geoSeqs[i].values, out n, out d)) return false;
            if (n0 * d != n * d0) return false;      // 分数比较：n0/d0 == n/d ⟺ n0*d == n*d0
        }
        return true;
    }

    /// <summary>
    /// 找出两条公比相同的等比数列。找到返回 true 并输出下标（indexA &lt; indexB）；找不到输出 -1。
    /// </summary>
    public static bool TryFindSameRatioPair(List<SequenceLine> geoSeqs, out int indexA, out int indexB)
    {
        indexA = -1;
        indexB = -1;
        if (geoSeqs == null || geoSeqs.Count < 2) return false;

        for (int i = 0; i < geoSeqs.Count; i++)
        {
            long ni, di;
            if (!TryGetCommonRatio(geoSeqs[i].values, out ni, out di)) continue;

            for (int j = i + 1; j < geoSeqs.Count; j++)
            {
                long nj, dj;
                if (!TryGetCommonRatio(geoSeqs[j].values, out nj, out dj)) continue;

                if (ni * dj == nj * di)     // 分数比较：ni/di == nj/dj ⟺ ni*dj == nj*di
                {
                    indexA = i;
                    indexB = j;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>是否存在两条公比相同的等比数列。</summary>
    public static bool HasPairWithSameCommonRatio(List<SequenceLine> geoSeqs)
    {
        int a, b;
        return TryFindSameRatioPair(geoSeqs, out a, out b);
    }

    // ------------------------------------------------------------------
    // 六、数列判定（静态方法，可独立复用与单元测试）
    // ------------------------------------------------------------------

    /// <summary>按类型分发判定。</summary>
    public static bool IsMatch(SequenceType type, List<int> values)
    {
        switch (type)
        {
            case SequenceType.Arithmetic: return IsArithmetic(values);
            case SequenceType.Geometric: return IsGeometric(values);
            case SequenceType.Fibonacci: return IsFibonacci(values);
            case SequenceType.Prime: return IsPrimeSequence(values);
            case SequenceType.Special: return IsSpecial(values);
            case SequenceType.Pair: return IsPair(values);
            default: return false;
        }
    }

    /// <summary>
    /// 等差判定：至少 2 个数，升序后相邻差值全部相等。
    /// 要求公差 &gt; 0，因此 5,5,5,5 这类重复数字不算等差。例：2,5,8,11 → true。
    /// </summary>
    public static bool IsArithmetic(List<int> values)
    {
        if (values == null || values.Count < 2) return false;

        List<int> sorted = new List<int>(values);
        sorted.Sort();

        int diff = sorted[1] - sorted[0];
        if (diff <= 0) return false;

        for (int i = 2; i < sorted.Count; i++)
        {
            if (sorted[i] - sorted[i - 1] != diff) return false;
        }
        return true;
    }

    /// <summary>
    /// 等比判定：至少 2 个数，升序后满足「每一项的平方 = 前后两项之积」，即 aᵢ² = aᵢ₋₁ × aᵢ₊₁。
    /// 用整数乘法实现，天然支持 4,6,9 这类分数公比（公比 3/2），不引入浮点误差。
    /// 要求严格递增（公比 &gt; 1），因此 5,5,5,5 不算等比。例：2,4,8,16 → true。
    /// </summary>
    public static bool IsGeometric(List<int> values)
    {
        if (values == null || values.Count < 2) return false;

        List<int> sorted = new List<int>(values);
        sorted.Sort();

        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == sorted[i - 1]) return false; // 必须严格递增
        }

        for (int i = 1; i < sorted.Count - 1; i++)
        {
            long left = (long)sorted[i] * sorted[i];
            long right = (long)sorted[i - 1] * sorted[i + 1];
            if (left != right) return false;
        }
        return true;
    }

    /// <summary>
    /// 斐波那契判定：至少 3 个数，升序后每一项都等于前两项之和（aᵢ = aᵢ₋₁ + aᵢ₋₂），且首项为正。
    /// 例：2,3,5,8 → true；1,2,3,5 → true；3,5,8,13 → true；1,1,2,3 → true（对应经典数列开头的 1,1）。
    /// 不要求必须是经典数列的片段，起始两项任意（2,5,7,12 也成立）；
    /// 若只想认经典数列，把下面的 FibonacciMustBeClassic 改为 true 即可。
    /// </summary>
    public static bool IsFibonacci(List<int> values)
    {
        if (values == null || values.Count < 3) return false;

        List<int> sorted = new List<int>(values);
        sorted.Sort();

        // 牌面值域是 1~100，这里只做防御：含 0 或负数时无法构成有意义的斐波那契数列
        if (sorted[0] <= 0) return false;

        if (FibonacciMustBeClassic && !IsClassicFibonacciWindow(sorted)) return false;

        for (int i = 2; i < sorted.Count; i++)
        {
            long expected = (long)sorted[i - 1] + sorted[i - 2];
            if (sorted[i] != expected) return false;
        }
        return true;
    }

    /// <summary>
    /// 斐波那契判定是否限定为「经典数列的连续片段」（1,1,2,3,5,8,13,21,34,55,89）。
    /// false（默认）= 只要求递推关系成立，起始两项任意。想换策略改这一个值即可。
    /// 用 static readonly 而不是 const，避免编译器把条件折叠成常量后对分支报「不可达代码」警告。
    /// </summary>
    private static readonly bool FibonacciMustBeClassic = false;

    /// <summary>经典斐波那契数列（牌面最大 100，列到 89 足够）。</summary>
    private static readonly int[] ClassicFibonacci = { 1, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89 };

    /// <summary>判断已升序的序列是否等于经典斐波那契数列的某个连续片段。</summary>
    private static bool IsClassicFibonacciWindow(List<int> sorted)
    {
        if (sorted == null || sorted.Count == 0) return false;
        if (sorted.Count > ClassicFibonacci.Length) return false;

        for (int start = 0; start + sorted.Count <= ClassicFibonacci.Length; start++)
        {
            bool same = true;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] != ClassicFibonacci[start + i])
                {
                    same = false;
                    break;
                }
            }
            if (same) return true;
        }
        return false;
    }

    /// <summary>质数判定：组内每个数字都必须是质数。</summary>
    public static bool IsPrimeSequence(List<int> values)
    {
        if (values == null || values.Count == 0) return false;

        for (int i = 0; i < values.Count; i++)
        {
            if (!NumberCardData.IsPrimeNumber(values[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// 旧的特殊判定（1 / 100 / 质数），保留备用。
    /// 2 槽组现在默认判「雀头」（两张数字相同），不再用这个。
    /// </summary>
    public static bool IsSpecial(List<int> values)
    {
        if (values == null || values.Count == 0) return false;

        for (int i = 0; i < values.Count; i++)
        {
            int v = values[i];
            bool isSpecialCard = (v == 1 || v == 100 || NumberCardData.IsPrimeNumber(v));
            if (!isSpecialCard) return false;
        }
        return true;
    }

    /// <summary>类型的中文名，用于日志与结算画面。</summary>
    public static string TypeName(SequenceType type)
    {
        switch (type)
        {
            case SequenceType.Arithmetic: return "等差";
            case SequenceType.Geometric: return "等比";
            case SequenceType.Fibonacci: return "斐波那契";
            case SequenceType.Prime: return "质数";
            case SequenceType.Special: return "特殊";
            case SequenceType.Pair: return "雀头";
            default: return type.ToString();
        }
    }

    /// <summary>把类型列表拼成「等差/等比/斐波那契」这样的中文串。</summary>
    public static string TypeNamesOf(List<SequenceType> types)
    {
        if (types == null || types.Count == 0) return "（无）";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < types.Count; i++)
        {
            if (i > 0) sb.Append("/");
            sb.Append(TypeName(types[i]));
        }
        return sb.ToString();
    }
}
