using UnityEngine;

/// <summary>
/// 槽位标记组件，挂在每一个 slot 物体上。
///
/// 布局：一共 4 组、3*4+2 = 14 个槽位（3 个 4 槽组 + 1 个 2 槽组），
/// 每组要判定的数列类型不同，具体分组在 SlotManager 里配置。
///
/// 本组件只负责「记住自己属于哪一组、当前被哪张卡占用」，
/// 「指针是否落在自己身上」的命中判定统一由 SlotManager 负责，避免职责分散。
/// </summary>
[DisallowMultipleComponent]
public class CardSlot : MonoBehaviour
{
    [Header("归属（由 SlotManager 在启动时回写，也可在 Inspector 手工指定）")]
    [Tooltip("所属槽位组的序号，从 0 开始；-1 表示尚未分配")]
    public int groupIndex = -1;

    [Tooltip("组内序号，从 0 开始")]
    public int slotIndex;

    [Header("运行时状态")]
    [Tooltip("当前占用该槽位的卡，空为 null")]
    public CardUI currentCard;

    /// <summary>自己的 RectTransform 缓存，供指针命中判定使用。</summary>
    private RectTransform cachedRect;

    /// <summary>
    /// 自己的 RectTransform，供 SlotManager 做矩形命中判定。
    /// 用惰性获取而不是在 Awake 里赋值：槽位所在的 gamePanel 开局是隐藏的，
    /// 未激活物体的 Awake 不会执行，那样 Rect 会是 null。
    /// </summary>
    public RectTransform Rect
    {
        get
        {
            if (cachedRect == null) cachedRect = transform as RectTransform;
            return cachedRect;
        }
    }

    /// <summary>槽位是否为空。</summary>
    public bool IsEmpty => currentCard == null;

    /// <summary>槽位里卡牌的数字值；空槽位返回 0。</summary>
    public int Value => currentCard != null ? currentCard.Value : 0;

    /// <summary>
    /// 把一张卡放进槽位。数据（currentCard / card.currentSlot）与 UI（父物体、锚点）在这里成对更新。
    ///
    /// 两种情况会先腾位置：
    ///   1. 槽位已被别的卡占着 → 把旧卡退回手牌区（相当于交换）；
    ///   2. 这张卡原本在别的槽位里 → 先释放那个槽位，保证「一张卡最多属于一个槽位」。
    /// </summary>
    public void Place(CardUI card)
    {
        if (card == null) return;
        if (currentCard == card) return;

        // 先校验：连 RectTransform 都没有就没法搬 UI，直接放弃，不能出现「数据改了 UI 没改」
        RectTransform cardRect = card.transform as RectTransform;
        if (cardRect == null)
        {
            Debug.LogError("[CardSlot] 卡牌根节点上没有 RectTransform，无法放入槽位；本次不改动任何数据。");
            return;
        }

        // 槽位已满 → 把旧卡退回手牌区。
        // 这里刻意不预先清双向引用：清引用与搬 UI 必须一起成功，
        // 否则一旦退回失败，就会留下「槽位认为自己是空的、旧卡却还挂在这个槽位上」的不一致。
        if (currentCard != null)
        {
            CardUI previous = currentCard;
            bool movedOut = GameManager.Instance != null && GameManager.Instance.ReturnCardToHand(previous);

            if (!movedOut)
            {
                // 退不出去就不硬塞，整体保持原状（数据与 UI 都仍是「旧卡占着这个槽位」）
                Debug.LogWarning("[CardSlot] 旧卡未能退回手牌区，本次放入取消，槽位保持原样。");
                return;
            }
        }

        // 这张卡原先在别的槽位里 → 先释放那个槽位（Clear 会同时清掉双向引用）
        if (card.currentSlot != null && card.currentSlot != this)
        {
            card.currentSlot.Clear();
        }

        // 先搬 UI
        cardRect.SetParent(transform, false);
        cardRect.anchorMin = Vector2.zero;
        cardRect.anchorMax = Vector2.one;
        cardRect.offsetMin = Vector2.zero;
        cardRect.offsetMax = Vector2.zero;
        cardRect.localScale = Vector3.one;
        cardRect.localRotation = Quaternion.identity;
        cardRect.SetAsLastSibling();

        // 再写数据，保证槽位与卡牌同时指向对方
        currentCard = card;
        card.currentSlot = this;
    }

    /// <summary>把卡从槽位取出并返回（只解除占用，不负责改父物体）。</summary>
    public CardUI Take()
    {
        CardUI card = currentCard;
        if (card != null) card.currentSlot = null;
        currentCard = null;
        return card;
    }

    /// <summary>
    /// 只解除占用，不销毁卡牌物体、也不改父物体（谁调用谁负责搬 UI）。
    ///
    /// 【重要】必须成对清空：槽位的 currentCard 与卡牌的 currentSlot 只清一边，
    /// 就会留下「槽位空着、卡牌却还指着这个槽位」的不一致状态。
    /// </summary>
    public void Clear()
    {
        if (currentCard != null) currentCard.currentSlot = null;
        currentCard = null;
    }
}
