using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 单张卡的「视图层」，挂在 Assets/prefabs/CardUI.prefab 的根节点上。
///
/// 职责（对应文本中的 numberCardData 显示需求）：
///   1. 持有它代表的 NumberCardData；
///   2. 抽到卡后，读取 NumberCardData.value 并直接写到 prefab 里那个 Text 上；
///   3. 记录自己当前所在槽位（CardSlot），供 DragHandler 拖拽与 SlotManager 读卡使用。
///
/// 【为什么要单独拆出这一层】
/// NumberCardData 是 ScriptableObject 资产，它无法反向引用场景里的 UI 组件。
/// 所以必须在每张卡牌实例上挂一个 MonoBehaviour 来当「数据 ↔ 画面」的桥，
/// 否则「合卡牌 UI」这件事没有落脚点。
/// </summary>
[DisallowMultipleComponent]
public class CardUI : MonoBehaviour
{
    [Header("数据")]
    [Tooltip("这张卡对应的牌面定义，抽卡时由 GameManager 写入")]
    public NumberCardData data;

    [Header("UI 引用（留空会在 Awake 时自动从自身与子物体中查找）")]
    [Tooltip("卡面背景图，即 prefab 根节点上的 Image")]
    public Image background;

    [Tooltip("显示牌面数字的文本，即 prefab 里的 Text 子物体")]
    public Text valueText;

    [Header("运行时状态")]
    [Tooltip("当前所在槽位；放在手牌区时为 null")]
    public CardSlot currentSlot;

    [Header("手牌区布局快照（从槽位退回手牌时用来还原）")]
    [Tooltip("手牌布局的锚点下限。Awake 时按 prefab 的样子记录，拖拽开始时若卡在手牌区会再记一次")]
    public Vector2 handAnchorMin = new Vector2(0.5f, 0.5f);

    [Tooltip("手牌布局的锚点上限")]
    public Vector2 handAnchorMax = new Vector2(0.5f, 0.5f);

    [Tooltip("手牌布局的尺寸")]
    public Vector2 handSizeDelta = new Vector2(130f, 125f);

    [Tooltip("手牌布局的坐标")]
    public Vector2 handAnchoredPosition = Vector2.zero;

    /// <summary>这张卡是否正放在某个槽位里。</summary>
    public bool IsInSlot => currentSlot != null;

    /// <summary>这张卡的数字值；没有绑定数据时返回 0。</summary>
    public int Value => data != null ? data.value : 0;

    /// <summary>prefab 原本的射线开关，拖拽结束后按原样还原（而不是一律设回 true）。</summary>
    private bool defaultBackgroundRaycast = true;
    private bool defaultValueTextRaycast = true;
    private bool raycastDefaultsCached;

    private void Awake()
    {
        CacheReferences();
        CacheRaycastDefaults();

        // 抽卡是用 Instantiate(prefab, handCardArea, false) 生成的，此刻布局还是 prefab 自带的样子
        // （LayoutGroup 要到本帧末尾才重排），正好把「在手牌区里应该长什么样」记下来，
        // 之后从槽位退回时照这个还原。
        CaptureHandLayout();
    }

    /// <summary>记录 prefab 原本的射线开关。</summary>
    private void CacheRaycastDefaults()
    {
        defaultBackgroundRaycast = background != null && background.raycastTarget;
        defaultValueTextRaycast = valueText != null && valueText.raycastTarget;
        raycastDefaultsCached = true;
    }

    /// <summary>
    /// 把当前的锚点 / 尺寸 / 坐标记为「手牌区布局」。
    /// 拖拽开始时如果卡本来就在手牌区，会再记一次，这样在手牌区里挪动过的卡也能回到原位。
    /// </summary>
    [ContextMenu("记录当前布局为手牌布局")]
    public void CaptureHandLayout()
    {
        RectTransform rect = transform as RectTransform;
        if (rect == null) return;

        handAnchorMin = rect.anchorMin;
        handAnchorMax = rect.anchorMax;
        handSizeDelta = rect.sizeDelta;
        handAnchoredPosition = rect.anchoredPosition;
    }

    /// <summary>
    /// 还原手牌区布局。
    /// 【为什么必须做】卡放进槽位时会被铺满（anchorMin=(0,0)、anchorMax=(1,1)、offset 全为 0），
    /// 退回手牌区时若不还原锚点，这张卡会以「铺满整个手牌区」的尺寸出现——
    /// 这正是「数据回到手牌了、画面却没回到手牌」的典型表现。
    /// </summary>
    public void RestoreHandLayout(RectTransform rect)
    {
        if (rect == null) return;

        rect.anchorMin = handAnchorMin;
        rect.anchorMax = handAnchorMax;
        rect.sizeDelta = handSizeDelta;
        rect.anchoredPosition = handAnchoredPosition;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    /// <summary>把 prefab 里的 Image / Text 引用缓存下来，避免每次都去查组件。</summary>
    public void CacheReferences()
    {
        if (background == null) background = GetComponent<Image>();
        if (valueText == null) valueText = GetComponentInChildren<Text>(true);
    }

    /// <summary>绑定牌面数据并刷新显示。抽卡时由 GameManager.DrawCards 调用。</summary>
    public void Bind(NumberCardData cardData)
    {
        data = cardData;
        CacheReferences();
        Refresh();
    }

    /// <summary>数字卡显示：直接把 data.value 写到 Text 上。</summary>
    public void Refresh()
    {
        if (valueText == null) CacheReferences();
        if (valueText != null)
        {
            valueText.text = data != null ? data.value.ToString() : string.Empty;
        }
    }

    /// <summary>
    /// 拖拽期间临时开关自身射线检测。
    /// 拖拽中卡片会被抬到 Canvas 根节点下，关掉射线可以避免它挡住其它 UI 的点击。
    /// </summary>
    public void SetRaycastTarget(bool enable)
    {
        CacheReferences();
        if (background != null) background.raycastTarget = enable;
        if (valueText != null) valueText.raycastTarget = enable;
    }

    /// <summary>还原成 prefab 原本的射线开关（不要在拖拽结束时一律设回 true）。</summary>
    public void RestoreRaycastDefaults()
    {
        CacheReferences();
        if (!raycastDefaultsCached) CacheRaycastDefaults();

        if (background != null) background.raycastTarget = defaultBackgroundRaycast;
        if (valueText != null) valueText.raycastTarget = defaultValueTextRaycast;
    }
}
