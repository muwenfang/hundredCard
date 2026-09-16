using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>槽位组要判定的数列类型。</summary>
public enum SequenceType
{
    /// <summary>等差：升序后相邻差值全部相等（公差 &gt; 0）。</summary>
    Arithmetic,

    /// <summary>等比：升序后每一项的平方等于前后两项之积（支持 4,6,9 这类分数公比）。</summary>
    Geometric,

    /// <summary>质数：组内每个数字都是质数。</summary>
    Prime,

    /// <summary>2 槽组的特殊判定。规则见 SlotManager.IsSpecial（占位实现）。</summary>
    Special
}

/// <summary>
/// 一个槽位组 = 一个槽位容器 + 该组要判定的数列类型 + 命中后的加分。
///
/// 【怎么用（重要）】
/// 你不需要在 Inspector 里一个个拖槽位。只要：
///   1. 在场景里建一个空的 UI 物体当「本组容器」（例如 Group1，可带 HorizontalLayoutGroup）；
///   2. 把本组的槽位物体都放在它下面，每个槽位物体上挂 CardSlot 组件；
///   3. 把容器拖到下面的 container 字段上。
/// 运行时 SlotManager.BuildSlots() 会自动收集容器下所有 CardSlot（含未激活的子物体），
/// 收集顺序就是它们在 Hierarchy 里的排列顺序，也就是读卡顺序。
///
/// 如果不想用容器，也可以把 CardSlot 一个个拖进 slots 列表（container 为空时才会用这个）。
/// </summary>
[System.Serializable]
public class SlotGroup
{
    [Tooltip("组名，仅用于结算画面与日志显示")]
    public string groupName = "Group";

    [Tooltip("本组的槽位容器。填了它，运行时自动收集它下面所有 CardSlot，无需手工拖槽位")]
    public GameObject container;

    [Tooltip("本组要判定的数列类型")]
    public SequenceType sequenceType = SequenceType.Arithmetic;

    [Tooltip("命中本组数列时加的分；默认 4 槽组 4 分、2 槽组 2 分")]
    public int pointsPerMatch = 4;

    [Tooltip("本组包含的槽位（可手工拖入）。container 非空时，这份列表会被自动收集结果覆盖")]
    public List<CardSlot> slots = new List<CardSlot>();

    /// <summary>本组实际的槽位数量。</summary>
    public int SlotCount => slots != null ? slots.Count : 0;

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
}

/// <summary>
/// 槽位管理器。
/// 功能（对应文本中的 SlotManager）：
///   1. 读卡：读取槽位里卡牌的值，装进 List 中，再作为参数传给数列判定；
///   2. 数列判定：等差 / 等比 / 质数（外加 2 槽组的占位判定 Special）；
///   3. 结算分数：判定为等差 / 等比 / 质数的组会加分（同一次结算中多组可同时命中并累加）。
///
/// 【槽位怎么接进来】
/// 只需要把 4 个「槽位容器」拖到 slotGroups 里各组的 container 字段上，
/// 槽位本身（挂了 CardSlot 的物体）放在容器下面就行，数量由代码自动数出来。
/// 部署：3 组各放 4 个槽位 + 1 组放 2 个槽位，合计 14 个。
/// </summary>
public class SlotManager : MonoBehaviour
{
    public static SlotManager instance;

    [Header("槽位分组（部署：3 × 4 + 1 × 2）")]
    [Tooltip("逐组配置容器、数列类型与分值。把每组槽位的父物体拖到该组的 container 上即可")]
    public List<SlotGroup> slotGroups = new List<SlotGroup>
    {
        new SlotGroup { groupName = "第 1 组", sequenceType = SequenceType.Arithmetic, pointsPerMatch = 4 },
        new SlotGroup { groupName = "第 2 组", sequenceType = SequenceType.Geometric,  pointsPerMatch = 4 },
        new SlotGroup { groupName = "第 3 组", sequenceType = SequenceType.Prime,      pointsPerMatch = 4 },
        new SlotGroup { groupName = "第 4 组", sequenceType = SequenceType.Special,    pointsPerMatch = 2 },
    };

    [Header("上次结算的结果（只读，供结算画面显示）")]
    [Tooltip("最近一次 CalculateScore 生成的文字明细")]
    public string lastResultSummary = string.Empty;

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
    /// 结算分数：逐组读卡并判定，命中则累加该组的分值。
    /// 判定要求整组放满，放不满的组不加分。明细写入 lastResultSummary。
    /// </summary>
    public int CalculateScore()
    {
        EnsureSlots();

        int total = 0;
        StringBuilder sb = new StringBuilder();

        for (int g = 0; g < slotGroups.Count; g++)
        {
            SlotGroup group = slotGroups[g];
            if (group == null) continue;

            List<int> values = ReadGroupValues(group);
            string typeName = TypeName(group.sequenceType);

            // 整组放满才判定
            if (group.SlotCount == 0 || values.Count < group.SlotCount)
            {
                sb.AppendLine(string.Format("{0}【{1}】未放满（{2}/{3}），不加分",
                    group.groupName, typeName, values.Count, group.SlotCount));
                continue;
            }

            bool matched = IsMatch(group.sequenceType, values);
            if (matched) total += group.pointsPerMatch;

            sb.AppendLine(string.Format("{0}【{1}】{2} → {3}",
                group.groupName, typeName, string.Join(", ", values),
                matched ? "命中 +" + group.pointsPerMatch : "未命中"));
        }

        lastResultSummary = sb.ToString();
        return total;
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
    // 四、数列判定（静态方法，可独立复用与单元测试）
    // ------------------------------------------------------------------

    /// <summary>按类型分发判定。</summary>
    public static bool IsMatch(SequenceType type, List<int> values)
    {
        switch (type)
        {
            case SequenceType.Arithmetic: return IsArithmetic(values);
            case SequenceType.Geometric: return IsGeometric(values);
            case SequenceType.Prime: return IsPrimeSequence(values);
            case SequenceType.Special: return IsSpecial(values);
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
    /// 2 槽组的特殊判定【占位实现】。
    /// 两张牌都必须是「特殊牌」，即 1、100 或质数。
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

    /// <summary>数列类型的中文名，用于日志与结算画面。</summary>
    public static string TypeName(SequenceType type)
    {
        switch (type)
        {
            case SequenceType.Arithmetic: return "等差";
            case SequenceType.Geometric: return "等比";
            case SequenceType.Prime: return "质数";
            case SequenceType.Special: return "特殊";
            default: return type.ToString();
        }
    }
}
