using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一个「长文本 + ScrollView」面板的全部数据与操作（普通 C# 类，不是 MonoBehaviour）。
///
/// 【为什么单独抽出来】
/// 教程面板（Canvas/tutorial）和「写给老师」面板（Canvas/forTeacher）的结构、坑、行为完全一样：
/// 都是 ScrollRect + Viewport + Content + 一个 Text (Legacy)，正文都是几千字、要自动换行、
/// 要按字号自动算高度。与其把同一套逻辑写两遍（写第二遍时一定会漏掉某个坑），
/// 不如把「一个长文本面板」抽象成一个类，UIManager 只负责把 Inspector 里的引用喂进来。
///
/// 【它负责四件事】
///   1. 显示 / 隐藏；
///   2. 把正文写进 Text，并强制打开「字号真的能生效」的那几个开关（见 <see cref="ApplyContent"/>）；
///   3. 按当前字号把 Text 与 Content 的高度算对，让滚动范围跟着字号走（见 <see cref="FitLayout"/>）；
///   4. 【防遮挡】保证关闭按钮能被点到（见 <see cref="EnsureCloseClickable"/>）。
///
/// 【它不负责】创建任何 UI 物体。面板里的物体一律由你在 Unity 里搭好、把引用拖进 UIManager。
/// </summary>
public class ScrollTextPanel
{
    // ==================================================================
    // 由 UIManager 在每次操作前同步进来的字段
    // ==================================================================

    /// <summary>用来起协程的 MonoBehaviour（通常是 UIManager 自己）。</summary>
    public MonoBehaviour host;

    /// <summary>日志里用的面板名，例如「教程」。</summary>
    public string label = "长文本面板";

    /// <summary>面板根物体。</summary>
    public GameObject panel;

    /// <summary>正文的 Text 组件。**必须是 uGUI 的 Text (Legacy)** —— Text (TMP) 类型不同、拖不进这个字段。</summary>
    public Text text;

    /// <summary>面板的 ScrollRect。留空会自动从 text / panel 往上找。</summary>
    public ScrollRect scrollRect;

    /// <summary>正文文件。留空则从 Resources 里按 <see cref="resourcePath"/> 加载。</summary>
    public TextAsset content;

    /// <summary>Resources 下的文件名（不带扩展名）。</summary>
    public string resourcePath = string.Empty;

    /// <summary>正文字号 —— 改这一个值就够了，标题会等比跟着缩。</summary>
    public float fontSize = 45f;

    /// <summary>原文的基准字号，用于等比缩放正文里的 &lt;size=NN&gt; 标题标签。</summary>
    public float sourceFontSize = TutorialTextFormatter.DefaultSourceFontSize;

    /// <summary>行距倍数。</summary>
    public float lineSpacing = 1f;

    /// <summary>拟合高度时给 Content 上下各留的余量（像素）。</summary>
    public float contentPadding = 12f;

    /// <summary>是否保留正文里的富文本标签（&lt;size=65&gt; 这类）。关掉则标题与正文同字号。</summary>
    public bool richText = true;

    // ---------- 关闭按钮的防遮挡 ----------

    /// <summary>关闭按钮在面板下的名字（与场景里一致：close）。</summary>
    public string closeButtonName = "close";

    /// <summary>发现关闭按钮被盖住时，是否自动把它提到同级最后一位。</summary>
    public bool autoRaiseCloseButton = true;

    // ==================================================================
    // 内部状态
    // ==================================================================

    /// <summary>「自动纠正过关闭按钮顺序」这条日志是否已经打过（同一个面板只吵一次）。</summary>
    private bool closeGuardLogged;

    /// <summary>上一次载入正文的统计（字符数 / 行数 / 标题标签数），供外部自检。</summary>
    public int LastCharCount { get; private set; }
    public int LastLineCount { get; private set; }
    public int LastSizeTagCount { get; private set; }

    /// <summary>正在「等一帧再拟合」的那次协程。</summary>
    private Coroutine fitRoutine;

    /// <summary>面板当前是否可见。</summary>
    public bool IsVisible { get { return panel != null && panel.activeSelf; } }

    // ==================================================================
    // 显示 / 隐藏
    // ==================================================================

    /// <summary>
    /// 打开面板。
    ///
    /// 【顺序很重要，别调】
    ///   ① 先纠正关闭按钮的层级（面板还没激活时也能改 hierarchy，修好了再露出来，不会闪一下）；
    ///   ② 再激活面板；
    ///   ③ 再写正文；
    ///   ④ 最后请求「等一帧再拟合高度」—— 面板刚激活的当帧 Canvas 还没重算布局，
    ///      同帧读 preferredHeight 拿到的是旧值，滚动条长度就会不对。
    /// </summary>
    public void Show()
    {
        EnsureCloseClickable();

        if (panel != null && !panel.activeSelf) panel.SetActive(true);

        ApplyContent();
        RequestFit();
    }

    /// <summary>关闭面板。</summary>
    public void Hide()
    {
        CancelFit();

        if (panel != null && panel.activeSelf) panel.SetActive(false);
    }

    // ==================================================================
    // 正文
    // ==================================================================

    /// <summary>
    /// 把正文写进 Text，并强制打开「字号真的能生效」的那几个开关。
    ///
    /// 【「我改了 Font Size 却没反应 / 文字被静默裁掉」的三个原因，都堵在这里】
    ///   1. <b>Best Fit（resizeTextForBestFit）</b>：一旦勾上，Unity 就**完全忽略** Font Size，
    ///      把文字自动缩放到 Min~Max 之间 —— 在 Inspector 里怎么改都没用。这里强制关掉。
    ///   2. <b>Vertical Overflow</b>：默认是 Truncate。文本比 RectTransform 高时它**静默裁掉尾巴**，
    ///      既不报错也拿不到真实内容高度。长正文放进 ScrollView 必须用 Overflow，
    ///      否则滚动条长度会按「被裁过的高度」算，字号一变就整段看不全。
    ///   3. <b>高度是写死的</b>：Content 上的高度不会随字号变，滚动范围一直按旧高度算。
    ///      这一步只负责字号与组件开关，高度交给 <see cref="FitLayout"/>。
    ///
    /// 另外把正文里的 &lt;size=65&gt; 标题标签按「目标字号 ÷ 原文基准字号」等比缩放，
    /// 这样把字号从 45 调到 30 时标题也会一起变小，不会头重脚轻。
    /// </summary>
    /// <returns>true = 正文已经成功写进去。</returns>
    public bool ApplyContent()
    {
        if (!EnsureText()) return false;

        string raw = ResolveRawText();
        if (string.IsNullOrEmpty(raw))
        {
            Debug.LogWarning(string.Format(
                "[{0}] 没有找到正文：content 没拖，Resources/{1}.txt 也读不到。",
                label, resourcePath));
            return false;
        }

        string prepared = TutorialTextFormatter.Prepare(raw, sourceFontSize, fontSize, richText);

        text.text = prepared;

        // ① 关掉 Best Fit —— 否则 Font Size 形同虚设
        text.resizeTextForBestFit = false;
        // ② 字号与行距
        text.fontSize = Mathf.RoundToInt(TutorialTextFormatter.ClampFontSize(fontSize));
        text.lineSpacing = Mathf.Approximately(lineSpacing, 0f) ? 1f : lineSpacing;
        // ③ 富文本（正文里的 <size=65> 标题标签靠它生效）
        text.supportRichText = richText;
        // ④ 长文本必须「横向换行 + 纵向溢出」，不能 Truncate
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        LastCharCount = prepared.Length;
        LastLineCount = TutorialTextFormatter.CountVisualLines(prepared);
        LastSizeTagCount = TutorialTextFormatter.CountSizeTags(prepared);

        Debug.Log(string.Format(
            "[{0}] 正文已载入：{1} 字符 / {2} 行 / {3} 个标题标签，字号 {4}，富文本 {5}，Best Fit 已强制关闭。",
            label, LastCharCount, LastLineCount, LastSizeTagCount, text.fontSize, richText ? "开" : "关"));

        return true;
    }

    /// <summary>
    /// 取正文：优先用拖进来的 TextAsset，否则从 Resources 按名字加载（加载一次后缓存）。
    /// </summary>
    public string ResolveRawText()
    {
        if (content != null) return content.text;
        if (string.IsNullOrEmpty(resourcePath)) return null;

        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null) return null;

        content = asset;      // 缓存，省掉后续的 Resources.Load
        return asset.text;
    }

    // ==================================================================
    // 高度拟合：让 ScrollView 的滚动范围跟着字号走
    // ==================================================================

    /// <summary>
    /// 请求「把内容高度拟合一次」。
    /// 面板没显示时直接跳过 —— 面板未激活时布局系统不重算，拟合出来的数字没有意义。
    /// 协程起不来（组件被禁用）时退化为同步做一次，总比什么都不做好。
    /// </summary>
    public void RequestFit()
    {
        if (text == null) return;
        if (!IsVisible) return;

        if (host == null || !host.isActiveAndEnabled)
        {
            FitLayout();
            return;
        }

        if (fitRoutine != null) host.StopCoroutine(fitRoutine);
        fitRoutine = host.StartCoroutine(FitNextFrame());
    }

    /// <summary>停掉还没跑的那次拟合（关面板、组件被禁用时调用）。</summary>
    public void CancelFit()
    {
        if (fitRoutine != null && host != null && host.isActiveAndEnabled) host.StopCoroutine(fitRoutine);
        fitRoutine = null;
    }

    /// <summary>等一帧再拟合：面板刚 SetActive(true) 的当帧，Canvas 还没重算布局。</summary>
    private IEnumerator FitNextFrame()
    {
        yield return null;
        FitLayout();
        fitRoutine = null;
    }

    /// <summary>
    /// 按当前字号把「Text 高度」和「Content 高度」算对，让 ScrollView 的滚动范围跟着字号走。
    ///
    /// 场景里的 <c>Viewport/Content</c> **只有 RectTransform**，
    /// 既没有 ContentSizeFitter 也没有 VerticalLayoutGroup，高度是写死的 ——
    /// 所以字号一变大，多出来的正文既显示不全也滚不到底。这里手动补上这一步。
    ///
    /// 如果以后你给 Content 挂了 ContentSizeFitter / VerticalLayoutGroup，
    /// 本方法会自动让位（只重建布局、不再手改尺寸），不会和你的设置打架。
    /// </summary>
    public void FitLayout()
    {
        if (text == null) return;

        RectTransform textRect = text.rectTransform;
        if (textRect == null) return;

        // 宽度为 0（锚点拉伸但 sizeDelta 没配好）时先向 Viewport 借宽度，
        // 否则 preferredHeight 会按「每行一个字」算出个天文数字
        ScrollRect scroll = ResolveScrollRect();
        if (textRect.rect.width <= 1f && scroll != null && scroll.viewport != null)
        {
            float viewportWidth = scroll.viewport.rect.width;
            if (viewportWidth > 1f)
            {
                textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, viewportWidth);
            }
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(textRect);

        float preferred = Mathf.Max(text.preferredHeight, 1f);
        textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferred);

        RectTransform contentRect = textRect.parent as RectTransform;
        if (contentRect != null && contentRect != textRect && !IsSizeDrivenByLayout(contentRect))
        {
            contentRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, preferred + Mathf.Max(0f, contentPadding) * 2f);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect != null ? contentRect : textRect);
        Canvas.ForceUpdateCanvases();

        if (scroll != null) scroll.verticalNormalizedPosition = 1f;    // 打开永远是顶部

        Debug.Log(string.Format("[{0}] 布局已拟合：文本高 {1:0.0}px（字号 {2}）。",
            label, preferred, text.fontSize));
    }

    /// <summary>
    /// 这个 RectTransform 的尺寸是否由布局系统托管（ContentSizeFitter 或 LayoutGroup）。
    /// 是的话就不要再手动 SetSizeWithCurrentAnchors —— 双方每帧互相覆盖，尺寸会抖。
    /// </summary>
    public static bool IsSizeDrivenByLayout(RectTransform rect)
    {
        if (rect == null) return false;
        if (rect.GetComponent<ContentSizeFitter>() != null) return true;
        if (rect.GetComponent<LayoutGroup>() != null) return true;
        return false;
    }

    /// <summary>运行时调整字号（例如做「放大 / 缩小」两个按钮）。</summary>
    public void SetFontSize(float size)
    {
        fontSize = TutorialTextFormatter.ClampFontSize(size);
        ApplyContent();
        if (IsVisible) RequestFit();
    }

    // ==================================================================
    // 引用解析
    // ==================================================================

    /// <summary>取 ScrollRect：优先用拖进来的；没拖就从 text / panel 往上找。</summary>
    public ScrollRect ResolveScrollRect()
    {
        if (scrollRect == null)
        {
            if (text != null) scrollRect = text.GetComponentInParent<ScrollRect>();
            if (scrollRect == null && panel != null) scrollRect = panel.GetComponentInChildren<ScrollRect>(true);
        }
        return scrollRect;
    }

    /// <summary>
    /// 确保 text 有值。没拖的话尝试自动找 —— 但**只在 ScrollRect.content 的子树里找**。
    ///
    /// 【为什么限定在 content 里找】面板下还有关闭按钮，它身上挂着一个只写着「x」的 Text。
    /// 如果在整个面板里找第一个 Text，会先把那个「x」抓过来当正文 —— 正文就被写没了。
    /// </summary>
    /// <returns>true = text 可用。</returns>
    public bool EnsureText()
    {
        if (text != null) return true;

        text = FindContentText();
        if (text != null)
        {
            Debug.Log(string.Format("[{0}] text 没拖，已自动从 ScrollRect 的 Content 里找到：{1}",
                label, PathOf(text.transform)));
            return true;
        }

        Debug.LogError(string.Format(
            "[{0}] 找不到正文 Text。请在面板的 Viewport/Content 下建一个 Text (Legacy)，" +
            "再拖到 UIManager 对应字段上（注意别拖成 Text (TMP)，类型不同）。当前面板：{1}",
            label, panel != null ? PathOf(panel.transform) : "<未拖 panel>"));
        return false;
    }

    /// <summary>
    /// 找正文用的 Text：优先 ScrollRect.content 子树；没有 ScrollRect 时才退回整个面板。
    /// </summary>
    public Text FindContentText()
    {
        if (panel == null) return null;

        ScrollRect scroll = ResolveScrollRect();
        if (scroll != null)
        {
            if (scroll.content == null) return null;
            return scroll.content.GetComponentInChildren<Text>(true);
        }

        return panel.GetComponentInChildren<Text>(true);
    }

    /// <summary>面板下是否已经有一个能当正文用的 Text（自检用，不产生任何副作用）。</summary>
    public bool HasUsableText()
    {
        return text != null || FindContentText() != null;
    }

    // ==================================================================
    // 【防遮挡】保证关闭按钮能被点到
    // ==================================================================

    /// <summary>
    /// 找关闭按钮：先按层级路径找（closeButtonName 里带 '/'），再按名字在整棵子树里找。
    /// </summary>
    public Transform FindCloseTransform()
    {
        if (panel == null || string.IsNullOrEmpty(closeButtonName)) return null;

        Transform root = panel.transform;
        if (closeButtonName.IndexOf('/') >= 0)
        {
            Transform direct = root.Find(closeButtonName);
            if (direct != null) return direct;
        }
        return FindDescendantByName(root, closeButtonName);
    }

    /// <summary>在子树里按名字找第一个物体（深度优先，先浅后深）。</summary>
    public static Transform FindDescendantByName(Transform root, string name)
    {
        if (root == null) return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == name) return child;
        }
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDescendantByName(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// 【真凶在这里】保证关闭按钮能被点到。
    ///
    /// uGUI 的点击是「按 Graphic 的 depth 从高往低取第一个命中者」，而 depth 由**同级顺序**决定：
    /// 同一个父物体下，**越靠后的子物体越上层**，射线也越先命中它。
    ///
    /// 本项目的场景里，<c>Canvas/tutorial</c> 的子物体顺序是：
    /// <code>
    ///   close  →  Viewport  →  Scrollbar Horizontal  →  Scrollbar Vertical
    /// </code>
    /// 也就是 close 排在**最底层**；而 Viewport 的 Image 是拉伸铺满整个面板的、
    /// raycastTarget = 1（Mask 的 showMaskGraphic = 0 只是不画出来，照样挡射线），
    /// Viewport 底下的 Content/Text 也一样 raycastTarget = 1 且铺得很长。
    /// 于是点在右上角的 × 上时，射线命中的是 Viewport / 正文，
    /// 事件被 ScrollRect 吃掉，**close 的 OnClick 根本不会触发**。
    /// 现象就是：「close 明明接的是 SetActive(false)，却怎么点都关不掉」。
    /// （<c>Canvas/forTeacher</c> 里 close 恰好排在最后，所以那个面板是好的 —— 同一个 bug 只暴露了一半。）
    ///
    /// 【修法】把 close 提到同级最后一位（SetAsLastSibling），一行搞定，且与你在 Hierarchy 里的顺序解耦。
    /// 建议你同时把场景里的 close 也手动拖到同级最后一位，让编辑器里的层级和运行时一致。
    /// </summary>
    /// <returns>true = 关闭按钮存在且（在当前设置下）没有被遮挡。</returns>
    public bool EnsureCloseClickable()
    {
        Transform close = FindCloseTransform();
        if (close == null)
        {
            // 面板没拖就不吵：ValidateWiring 已经会提示「这个面板还没连线」
            if (panel != null)
            {
                Debug.LogWarning(string.Format(
                    "[{0}] 面板（{1}）下没有名为 \"{2}\" 的子物体，跳过关闭按钮防遮挡检查。",
                    label, PathOf(panel.transform), closeButtonName));
            }
            return false;
        }

        Transform parent = close.parent;
        if (parent == null) return false;

        List<PanelSibling> blockers = CollectBlockersAbove(close);
        bool needsRaise = close.GetSiblingIndex() != parent.childCount - 1;

        if (autoRaiseCloseButton)
        {
            if (needsRaise)
            {
                if (blockers.Count > 0 && !closeGuardLogged)
                {
                    Debug.LogWarning(string.Format(
                        "[{0}] 关闭按钮 \"{1}\" 原本被这些上层物体盖住，点击会被它们吃掉：{2}。" +
                        "已自动把它提到同级最后一位（不想自动处理就把 autoRaiseCloseButton 取消勾选）。",
                        label, close.name, ScrollPanelLayout.DescribeBlockers(blockers)));
                    closeGuardLogged = true;
                }
                close.SetAsLastSibling();
            }
            return true;
        }

        if (blockers.Count > 0)
        {
            Debug.LogError(string.Format(
                "[{0}] 关闭按钮 \"{1}\" 被盖住，点击无效：{2}。请把它在 Hierarchy 里拖到同级最后一位。",
                label, close.name, ScrollPanelLayout.DescribeBlockers(blockers)));
            return false;
        }

        return true;
    }

    /// <summary>
    /// 收集「排在 close 之后（= 渲染在它上层）且几何上覆盖了它」的 Graphic。
    /// 会把每个上层的整棵子树都算进去 —— 因为子物体的 depth 比 close 更高，同样能吃掉点击。
    /// </summary>
    private List<PanelSibling> CollectBlockersAbove(Transform close)
    {
        List<PanelSibling> collected = new List<PanelSibling>();
        Transform parent = close.parent;
        if (parent == null) return collected;

        Rect closeArea = ScrollPanelLayout.RectInParent(close as RectTransform);

        int index = close.GetSiblingIndex();
        for (int i = index + 1; i < parent.childCount; i++)
        {
            Transform sibling = parent.GetChild(i);
            if (!sibling.gameObject.activeSelf) continue;      // 自己关掉的物体不参与射线

            Rect area = ScrollPanelLayout.RectInParent(sibling as RectTransform);
            CollectGraphics(sibling, area, collected);
        }

        return ScrollPanelLayout.FindBlockers(closeArea, collected);
    }

    /// <summary>
    /// 把一个子树里所有有 raycaster 意义的 Graphic 收集成「以 close 的父物体为共同坐标系」的矩形。
    /// 逐层把子物体的矩形平移到共同坐标系里，这样不同深度的物体也能直接两两比较。
    /// </summary>
    private static void CollectGraphics(Transform node, Rect areaInCommon, List<PanelSibling> outList)
    {
        if (node == null || !node.gameObject.activeSelf) return;

        Graphic graphic = node.GetComponent<Graphic>();
        if (graphic != null)
        {
            outList.Add(new PanelSibling(
                node.name, graphic.raycastTarget, node.gameObject.activeSelf, areaInCommon));
        }

        for (int i = 0; i < node.childCount; i++)
        {
            Transform child = node.GetChild(i);
            if (!child.gameObject.activeSelf) continue;

            Rect childLocal = ScrollPanelLayout.RectInParent(child as RectTransform);
            Rect childInCommon = new Rect(
                areaInCommon.x + childLocal.x,
                areaInCommon.y + childLocal.y,
                childLocal.width,
                childLocal.height);

            CollectGraphics(child, childInCommon, outList);
        }
    }

    /// <summary>拼一个「A/B/C」形式的完整路径，只用于日志。</summary>
    public static string PathOf(Transform t)
    {
        if (t == null) return "<null>";

        StringBuilder sb = new StringBuilder(t.name);
        Transform cur = t.parent;
        while (cur != null)
        {
            sb.Insert(0, cur.name + "/");
            cur = cur.parent;
        }
        return sb.ToString();
    }
}

/// <summary>
/// 一个「可能挡住别人点击」的 UI 元素快照（纯数据，不含任何 Unity 引用）。
/// 把采集与判断拆开，判断那部分就是纯函数，可以离线喂用例回归。
/// </summary>
public struct PanelSibling
{
    /// <summary>物体名。</summary>
    public string name;

    /// <summary>它的 Graphic 是否接收射线（raycastTarget）。</summary>
    public bool raycastTarget;

    /// <summary>它自己是否被激活（看 activeSelf，不看 activeInHierarchy —— 面板整体隐藏时也要能预测）。</summary>
    public bool active;

    /// <summary>相对共同父物体（关闭按钮的父物体）的矩形，单位像素。</summary>
    public Rect rect;

    public PanelSibling(string name, bool raycastTarget, bool active, Rect rect)
    {
        this.name = name;
        this.raycastTarget = raycastTarget;
        this.active = active;
        this.rect = rect;
    }
}

/// <summary>
/// uGUI 布局的纯计算部分：矩形换算 + 遮挡判断。
/// 全部是静态纯函数、不碰任何 Unity 组件，所以能脱离运行时直接喂用例（见离线回归）。
/// </summary>
public static class ScrollPanelLayout
{
    /// <summary>
    /// 把一个 RectTransform 的矩形换算到「父物体左下角为原点」的坐标系。
    ///
    /// 公式来自 RectTransform 的定义：
    ///   尺寸     = (anchorMax - anchorMin) × 父尺寸 + sizeDelta
    ///   锚点参考点 = (anchorMin + (anchorMax - anchorMin) × pivot) × 父尺寸
    ///   轴心位置  = 锚点参考点 + anchoredPosition
    ///   左下角    = 轴心位置 − pivot × 尺寸
    ///
    /// 【适用前提】父链上没有旋转、缩放不为 1 的情况（UI 面板基本都是这样）。
    /// </summary>
    public static Rect RectInParent(
        Vector2 parentSize,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 sizeDelta,
        Vector2 anchoredPosition,
        Vector2 pivot)
    {
        Vector2 size = new Vector2(
            (anchorMax.x - anchorMin.x) * parentSize.x + sizeDelta.x,
            (anchorMax.y - anchorMin.y) * parentSize.y + sizeDelta.y);

        Vector2 anchorRef = new Vector2(
            (anchorMin.x + (anchorMax.x - anchorMin.x) * pivot.x) * parentSize.x,
            (anchorMin.y + (anchorMax.y - anchorMin.y) * pivot.y) * parentSize.y);

        Vector2 pivotPos = anchorRef + anchoredPosition;
        Vector2 min = new Vector2(pivotPos.x - pivot.x * size.x, pivotPos.y - pivot.y * size.y);

        return new Rect(min, size);
    }

    /// <summary>读取一个 RectTransform 在它父物体坐标系里的矩形。父物体不是 RectTransform 时按自身尺寸兜底。</summary>
    public static Rect RectInParent(RectTransform rect)
    {
        if (rect == null) return new Rect(0f, 0f, 0f, 0f);

        RectTransform parent = rect.parent as RectTransform;
        Vector2 parentSize = parent != null ? parent.rect.size : rect.rect.size;

        return RectInParent(parentSize, rect.anchorMin, rect.anchorMax,
            rect.sizeDelta, rect.anchoredPosition, rect.pivot);
    }

    /// <summary>两个矩形是否有重叠面积（只接触边不算重叠）。</summary>
    public static bool Overlaps(Rect a, Rect b)
    {
        return a.xMin < b.xMax && b.xMin < a.xMax && a.yMin < b.yMax && b.yMin < a.yMax;
    }

    /// <summary>这个元素会不会挡住目标区域的点击。</summary>
    public static bool WouldBlock(Rect target, PanelSibling sibling)
    {
        if (!sibling.active) return false;
        if (!sibling.raycastTarget) return false;
        return Overlaps(target, sibling.rect);
    }

    /// <summary>
    /// 从「目标区域上层的所有元素」里挑出真正会挡点击的那些。
    /// 调用方只需保证传进来的都是排在目标**之后**（渲染在上层）的物体。
    /// </summary>
    public static List<PanelSibling> FindBlockers(Rect target, List<PanelSibling> elementsAbove)
    {
        List<PanelSibling> result = new List<PanelSibling>();
        if (elementsAbove == null) return result;

        for (int i = 0; i < elementsAbove.Count; i++)
        {
            if (WouldBlock(target, elementsAbove[i])) result.Add(elementsAbove[i]);
        }
        return result;
    }

    /// <summary>把挡路的元素拼成一句人能看懂的日志（最多列 6 个，避免刷屏）。</summary>
    public static string DescribeBlockers(List<PanelSibling> blockers)
    {
        if (blockers == null || blockers.Count == 0) return "（无）";

        const int maxShown = 6;
        StringBuilder sb = new StringBuilder();

        int shown = Mathf.Min(blockers.Count, maxShown);
        for (int i = 0; i < shown; i++)
        {
            if (i > 0) sb.Append('、');
            sb.Append('"').Append(blockers[i].name).Append('"');
        }

        if (blockers.Count > shown) sb.Append(" 等 ").Append(blockers.Count).Append(" 个");
        return sb.ToString();
    }
}
