using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 拖拽 / 点击处理组件，挂在 Assets/prefabs/CardUI.prefab 的根节点上。
///
/// 功能（对应文本中的 dragHandler）：
///   1. 拖动卡牌：把卡抬到「根 Canvas」下并跟随指针，保证它渲染在手牌与槽位之上；
///   2. 松手时检测落点（按优先级）：
///        落在槽位上       → 交给 CardSlot.Place 放进去；
///        落在删卡投放区   → 交给 GameManager.DeleteCard 删掉这张卡（仅删卡阶段）；
///        落在手牌区内     → 退回手牌区；
///        落在其它任何地方 → 同样退回手牌区；
///   3. 点击槽位里的卡 → 退回手牌区（手牌区里的卡被点击不做任何事）。
///
/// 【数据与 UI 的一致性 —— 这是本类的核心约束】
/// 换父物体或销毁卡牌只允许通过下面三个方法发生，绝不在这里单独改 transform.parent / Destroy：
///    CardSlot.Place(card)               放入槽位：写 currentCard / currentSlot + 改父物体 + 铺满槽位
///    GameManager.ReturnCardToHand(card) 退回手牌：清掉槽位双向引用 + 改父物体 + 还原手牌布局
///    GameManager.DeleteCard(card)       删卡：清掉槽位双向引用 + 移出手牌列表 + 销毁物体
/// 三者都是「数据与 UI 一起成功或一起不动」。本类只负责判断落点，不负责搬运。
///
/// 注意 1：命中判定用的是「矩形包含」而不是 EventSystem 射线，
/// 所以槽位物体不需要挂 Image 也能被命中。
///
/// 注意 2：必须显式实现 IBeginDragHandler / IDragHandler / IEndDragHandler / IPointerClickHandler，
/// 光有同名方法是不够的 —— EventSystem 是用接口类型去查找事件处理者的。
///
/// 注意 3：结算逐组跳分播放期间盘面「定格展示」（槽位上的牌还没销毁），
/// 这段时间不接受任何拖拽与点击，见 IsBoardFrozen。
/// </summary>
[RequireComponent(typeof(CardUI))]
public class DragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    private CardUI card;
    private RectTransform rect;
    private Canvas rootCanvas;

    /// <summary>拖动开始前的父物体，用于「没有手牌区引用」时兜底归位。</summary>
    private Transform originParent;

    /// <summary>按下点与卡牌轴心的世界坐标差值，保证拖动时卡牌不会瞬间跳到指针中心。</summary>
    private Vector3 dragOffset;

    /// <summary>是否正处在一次有效拖拽中。用于拦掉「拖拽被判定成点击」的情况。</summary>
    private bool dragging;

    private void Awake()
    {
        EnsureRefs();
    }

    // ------------------------------------------------------------------
    // EventSystem 回调
    // ------------------------------------------------------------------

    public void OnBeginDrag(PointerEventData eventData)
    {
        EnsureRefs();
        if (rect == null) return;

        // 结算跳分播放中：盘面正在「定格展示」，槽位上的牌还没销毁。
        // 此时不允许拖动 —— 否则玩家能把正在结算的牌拉回手牌或换个槽位，
        // 动画显示的分数就和盘面对不上了。不设 dragging，后面的 OnDrag / OnEndDrag 自然什么都不做。
        if (IsBoardFrozen()) return;

        // 抬到根 Canvas 下需要它。找不到就整段放弃，什么都不改 ——
        // 绝不能出现「已经清了槽位数据、却没把卡搬走」的中间状态。
        if (rootCanvas == null)
        {
            Debug.LogError("[DragHandler] 找不到根 Canvas，本次拖拽取消（未改动任何数据）。");
            return;
        }

        // 卡本来就在手牌区 → 顺手把此刻的样子记为「手牌布局」，
        // 这样在手牌区里挪过位置的卡，退回时也能回到原位。
        if (card != null && card.currentSlot == null) card.CaptureHandLayout();

        originParent = transform.parent;

        // 记下「按下点」与「卡牌轴心」的世界坐标差值，后面按这个差值跟随
        Vector3 pointerWorld;
        dragOffset = Vector3.zero;
        if (TryGetPointerWorld(eventData, out pointerWorld))
        {
            dragOffset = rect.position - pointerWorld;
        }

        // 从原槽位解除占用，让该槽位立刻变回可放置状态（Clear 会同时清掉双向引用）
        if (card != null && card.currentSlot != null) card.currentSlot.Clear();

        // 抬到根 Canvas 下，保证渲染在所有其它 UI 之上。
        // worldPositionStays = true：父级缩放链相同，所以画面位置与缩放都不变
        rect.SetParent(rootCanvas.transform, true);

        // 拖动期间关掉自身射线，避免遮挡其它 UI 的点击。
        // 附带好处：松手那一帧 EventSystem 是用「射线已关」的命中结果判断要不要算点击，
        // 所以拖完松手不会被误判成一次点击。
        if (card != null) card.SetRaycastTarget(false);

        dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        EnsureRefs();

        Vector3 pointerWorld;
        if (TryGetPointerWorld(eventData, out pointerWorld))
        {
            transform.position = pointerWorld + dragOffset;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;

        EnsureRefs();
        if (card == null) return;

        card.RestoreRaycastDefaults();

        // 1) 落点在槽位上 → 放入槽位（数据与 UI 由 CardSlot.Place 成对更新）
        CardSlot target = SlotManager.instance != null
            ? SlotManager.instance.FindSlotAt(eventData.position, GetEventCamera())
            : null;

        if (target != null)
        {
            target.Place(card);

            // 后置校验：Place 可能因为「槽位里的旧卡退不出去」而放弃执行。
            // 确认卡真的进了槽位，否则往下走退回手牌 —— 绝不能让它悬在 Canvas 上。
            if (card.currentSlot == target) return;

            Debug.LogWarning("[DragHandler] 放入槽位未成功，改为退回手牌区。");
        }

        // 2) 落点在删卡投放区 → 删掉这张卡（只在删卡阶段、且删卡区可见时成立）
        if (GameManager.Instance != null
            && GameManager.Instance.IsOverDeleteArea(eventData.position, GetEventCamera())
            && GameManager.Instance.DeleteCard(card))
        {
            Debug.Log("[DragHandler] 卡已拖入删卡区并被删除。");
            return;
        }

        // 3) 落点不在槽位也不在删卡区 → 一律退回手牌区。
        //    落在手牌区内 / 落在界面空白处 / 拖出界面，三种情况处理完全相同。
        bool overHand = GameManager.Instance != null
            && GameManager.Instance.IsOverHandArea(eventData.position, GetEventCamera());

        if (GameManager.Instance != null && GameManager.Instance.ReturnCardToHand(card))
        {
            Debug.Log(string.Format("[DragHandler] 卡 {0} 松手位置：{1} → 已退回手牌区。",
                card.Value, overHand ? "手牌区内" : "非槽位且非手牌区"));
            return;
        }

        // 4) 兜底：没有 GameManager，或 handCardArea 没配 → 退回拖动前的父物体。
        //    原父物体若是槽位，必须走 CardSlot.Place 把数据一起补回来，不能只改父物体。
        Debug.LogWarning("[DragHandler] GameManager 或 handCardArea 不可用，退回拖动前的父物体。");
        FallbackToOrigin();
    }

    /// <summary>
    /// 点击卡牌：只处理「正放在槽位里」的卡 —— 点击即退回手牌区（位置无所谓，回去就行）。
    /// 手牌区里的卡被点击不做任何事。
    ///
    /// 拖拽不会误触发这里：松手那一帧 EventSystem 先用「拖动期间已关射线」的命中结果判断是否算点击，
    /// 那时卡牌挡不住射线，pointerPress（卡）与 pointerUpHandler（卡下方的东西）对不上，于是不判为点击；
    /// 而且本方法执行时 dragging 仍为 true（OnEndDrag 更晚执行），双重保险。
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (dragging) return;                                    // 拖拽过程中不算点击
        if (IsBoardFrozen()) return;                             // 结算跳分播放中：牌要留在盘面上展示

        EnsureRefs();
        if (card == null || card.currentSlot == null) return;     // 只处理槽位里的卡

        Debug.Log(string.Format("[DragHandler] 点击了槽位里的卡 {0}，退回手牌区。", card.Value));

        if (GameManager.Instance != null && GameManager.Instance.ReturnCardToHand(card)) return;

        Debug.LogWarning("[DragHandler] GameManager 或 handCardArea 不可用，无法退回手牌区。");
    }

    // ------------------------------------------------------------------
    // 内部工具
    // ------------------------------------------------------------------

    private void EnsureRefs()
    {
        if (card == null) card = GetComponent<CardUI>();
        if (rect == null) rect = transform as RectTransform;
        if (rootCanvas == null)
        {
            rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas != null) rootCanvas = rootCanvas.rootCanvas;
        }
    }

    /// <summary>
    /// 盘面是否处于「定格展示」状态（结算逐组跳分播放中）。
    /// 这期间槽位里的牌还没被销毁，必须留在原处让玩家对照分数看，所以拖拽与点击都要拦住。
    /// 没有 GameManager 时视为不冻结（宁可让玩家能操作，也不要整个界面点不动）。
    /// </summary>
    private static bool IsBoardFrozen()
    {
        return GameManager.Instance != null && GameManager.Instance.IsSettleSequencePlaying;
    }

    /// <summary>把屏幕点转到「当前父物体」平面上的世界坐标。</summary>
    private bool TryGetPointerWorld(PointerEventData eventData, out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        RectTransform parentRect = transform.parent as RectTransform;
        if (parentRect == null) return false;

        return RectTransformUtility.ScreenPointToWorldPointInRectangle(
            parentRect, eventData.position, GetEventCamera(), out worldPoint);
    }

    /// <summary>
    /// 取事件相机：Screen Space - Overlay 模式下必须传 null，
    /// Screen Space - Camera / World Space 模式下要传 Canvas 的渲染相机。
    /// </summary>
    private Camera GetEventCamera()
    {
        if (rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return rootCanvas.worldCamera;
    }

    /// <summary>
    /// 兜底归位（仅在 GameManager / handCardArea 不可用时才会走到）：
    /// 原父物体是槽位 → 走 CardSlot.Place，把数据一起补回来；
    /// 否则退回原父物体。
    /// </summary>
    private void FallbackToOrigin()
    {
        if (originParent == null || rect == null) return;

        CardSlot originSlot = originParent.GetComponent<CardSlot>();
        if (originSlot != null)
        {
            originSlot.Place(card);
            return;
        }

        if (card != null && card.currentSlot != null) card.currentSlot.Clear();

        rect.SetParent(originParent, false);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.SetAsLastSibling();

        RectTransform parentRect = originParent as RectTransform;
        if (parentRect != null && originParent.GetComponent<LayoutGroup>() != null)
        {
            LayoutRebuilder.MarkLayoutForRebuild(parentRect);
        }
    }
}
