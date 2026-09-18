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
///   5. 结算逐组跳分：4 个组分数文本按索引 0→3 依次出现，最后显示总分，停 2 秒再弹出 endPanel；
///      **动画期间盘面保持原样**：槽位上的牌还留在原处（什么时候销毁由 GameManager 决定，
///      它要等动画播完才收到本类的回调），玩家才能对照着牌看清是哪几个数字凑出了这一组；
///   6. 学号输入：读取「输入学号」界面的输入内容并校验（0~300），非法时把原因写到提示文本上；
///   7. 排行榜：把每名学生的记录（学号 / 平均分 / 最高得分 / 游玩局数）拼进 rankingPanel 的文本。
///      GameManager.InitializeGame 会调用 RefreshRanking()，每次开局 / 回主菜单都重新读盘刷新一次。
///   8. 教程面板：把 Resources/tutorial.txt 的长正文写进 ScrollView 里的 Text (Legacy)，
///      并强制关掉 Best Fit、把纵向溢出改成 Overflow、按字号重算内容高度 ——
///      详见 ScrollTextPanel.ApplyContent / FitLayout 的注释（「改了字号没反应」的三个坑都在那）；
///   9. 「写给老师」面板：与教程完全同构（Resources/forTeacher.txt），共用上面同一套实现；
///  10. 【关闭按钮防遮挡】保证两个长文本面板的 close 按钮真的点得到。
///      详见 ScrollTextPanel.EnsureCloseClickable —— 层级顺序不对时会变成
///      「close 明明接的是 SetActive(false)，却怎么点都关不掉」。
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
    [Tooltip("删卡投放区，可以拖多个")]
    public List<GameObject> deletionPanels = new List<GameObject>();

    [Tooltip("显示删卡区时，自动关掉它自身 Graphic 的射线，避免这块半透明区域挡住下面的卡（拖不出来）")]
    public bool disableDeletePanelRaycast = true;

    [Tooltip("「下一回合」按钮上的文字组件")]
    public Text nextTurnButtonLabel;

    [Tooltip("进入删卡阶段时按钮变成的颜色（红色）")]
    public Color deletePhaseButtonColor = new Color(0.85f, 0.29f, 0.26f, 1f);

    [Tooltip("进入删卡阶段时按钮上的文字。")]
    public string deletePhaseButtonText = "删除{0}张卡";

    [Tooltip("「下一回合」按钮平时的文字。")]
    public string nextTurnNormalText = string.Empty;


    [Header("结算逐组跳分")]
    [Tooltip("每一组的分数文本，按索引 0 → 3 依次显示")]
    public List<Text> groupScoreTexts = new List<Text>();

    [Tooltip("组分数文本的格式：{0} = 组名，{1} = 该组分数。")]
    public string groupScoreFormat = "{0}  +{1}";

    [Tooltip("总分文本。")]
    public Text settleTotalScoreText;

    [Tooltip("总分文本的格式：{0} = 总分。")]
    public string settleTotalFormat = "总分：{0}";

    [Tooltip("相邻两组出现之间的间隔（秒）")]
    public float settleStepInterval = 0.6f;

    [Tooltip("显示总分之后停留多久再弹出 endPanel（秒）")]
    public float settleHoldAfterTotal = 2f;


    [Header("学号输入（startGame 之前的环节）")]
    [Tooltip("输入学号的界面")]
    public GameObject studentIdPanel;

    [Tooltip("显示学号面板时是否把主菜单/游戏面板全部收起来。" )]
    public bool hideOtherPanelsWhenInputId = false;

    [Tooltip("学号输入框（uGUI 的 InputField）")]
    public InputField studentIdInput;

    [Tooltip("学号提示 / 报错文本。输入非法时会把原因写在这上面；成功时清空")]
    public Text studentIdHintText;

    [Tooltip("重新打开学号界面时，是否把上一个学号预填进输入框")]
    public bool prefillLastStudentId = true;

    [Header("排行榜")]
    [Tooltip("rankingPanel 上的文本。学号→平均分→最高得分→游玩局数")]
    public Text rankingText;

    [Tooltip("默认排序方式。0=学号 1=平均分 2=最高得分 3=游玩局数（默认按学号排列）")]
    public RankingSortMode rankingSortMode = RankingSortMode.ByStudentId;

    [Tooltip("最多显示多少条记录。0 = 全部显示")]
    public int rankingMaxRows = 0;

    [Tooltip("标题格式：{0} = 记录总数，{1} = 排序方式，{2} = 本次显示条数。**留空则不输出标题行**")]
    public string rankingTitleFormat = string.Empty;

    [Tooltip("表头行。指标顺序固定为 学号 / 平均分 / 最高得分 / 游玩局数")]
    public string rankingHeaderLine = string.Empty;

    [Tooltip("每行的格式：{0} = 学号，{1} = 平均分，{2} = 最高得分，{3} = 游玩局数。" )]
    public string rankingLineFormat = RankingTextBuilder.DefaultLineFormat;

    [Tooltip("平均分的数字格式")]
    public string rankingAverageFormat = RankingTextBuilder.DefaultAverageFormat;

    [Tooltip("一条记录都没有时显示的文案")]
    public string rankingEmptyText = RankingTextBuilder.DefaultEmptyText;


    [Header("教程面板（长文本 + ScrollView）")]
    [Tooltip("教程面板的根物体（Canvas/tutorial）")]
    public GameObject tutorialPanel;

    [Tooltip("教程正文的 Text 组件。**必须拖 uGUI 的 Text (Legacy)**，" +
             "别拖 Text (TMP)——两者类型不同，TMP 拖不进这个字段")]
    public Text tutorialText;

    [Tooltip("教程的 ScrollRect。留空会从 tutorialText 往上自动找")]
    public ScrollRect tutorialScrollRect;

    [Tooltip("教程正文文件。留空则从 Resources 里按 tutorialResourcePath 加载")]
    public TextAsset tutorialContent;

    [Tooltip("Resources 下的教程文件名（不带扩展名）")]
    public string tutorialResourcePath = "tutorial";

    [Tooltip("教程正文字号。**改这里就够了**：运行时会覆盖 Text 组件上的 Font Size")]
    public float tutorialFontSize = 45f;

    [Tooltip("原文的基准字号。文档里标题写的是绝对像素值（大标题 65 / 小标题 55），" +
             "按「tutorialFontSize ÷ 这个值」等比缩放标题，改字号时标题才不会头重脚轻")]
    public float tutorialSourceFontSize = TutorialTextFormatter.DefaultSourceFontSize;

    [Tooltip("行距倍数")]
    public float tutorialLineSpacing = 1f;

    [Tooltip("是否保留正文里的富文本标记（<size=65> 这类）。关掉则标题与正文同字号")]
    public bool tutorialRichText = true;

    [Tooltip("自适应高度时给 Content 上下各留的余量（像素）")]
    public float tutorialContentPadding = 12f;


    [Header("「写给老师」面板（长文本 + ScrollView，结构与教程完全一致）")]
    [Tooltip("面板的根物体（Canvas/forTeacher）")]
    public GameObject forTeacherPanel;

    [Tooltip("正文的 Text 组件。**必须拖 Text (Legacy)**，别拖 Text (TMP)。" +
             "留空会自动在 ScrollRect 的 Content 里找一个")]
    public Text forTeacherText;

    [Tooltip("面板的 ScrollRect。留空会自动从 Text / 面板往上找")]
    public ScrollRect forTeacherScrollRect;

    [Tooltip("正文文件。留空则从 Resources 里按 forTeacherResourcePath 加载")]
    public TextAsset forTeacherContent;

    [Tooltip("Resources 下的正文文件名（不带扩展名）")]
    public string forTeacherResourcePath = "forTeacher";

    [Tooltip("正文字号。改这里就够了，标题会等比跟着缩")]
    public float forTeacherFontSize = 45f;

    [Tooltip("原文的基准字号，用于等比缩放正文里的 <size=NN> 标题标签")]
    public float forTeacherSourceFontSize = TutorialTextFormatter.DefaultSourceFontSize;

    [Tooltip("行距倍数")]
    public float forTeacherLineSpacing = 1f;

    [Tooltip("是否保留正文里的富文本标记")]
    public bool forTeacherRichText = true;

    [Tooltip("自适应高度时给 Content 上下各留的余量（像素）")]
    public float forTeacherContentPadding = 12f;


    [Header("长文本面板的关闭按钮（防遮挡）")]
    [Tooltip("关闭按钮在面板下的名字。场景里 tutorial / forTeacher 两个面板都叫 close")]
    public string panelCloseButtonName = "close";

    [Tooltip("勾选后：若关闭按钮被同级的 Viewport / 正文盖住（点击被它们吃掉），" +
             "启动时自动把它提到同级最后一位。**建议保持勾选** —— " +
             "否则层级顺序一旦不对，就会变成「close 明明调了 SetActive(false) 却关不掉」")]
    public bool autoRaiseCloseButtons = true;


    /// <summary>「下一回合」按钮进入删卡阶段前的配色快照，退出阶段时照它还原。</summary>
    private ColorBlock normalNextTurnColors;

    /// <summary>是否已经存过配色快照（只存一次，多次进出删卡阶段也还原得回去）。</summary>
    private bool nextTurnColorsCached;

    /// <summary>「下一回合」按钮原本的文字，退出删卡阶段时还原。</summary>
    private string cachedNormalLabel;

    /// <summary>上一次确认过的学号，-1 表示还没有过（用于重新打开学号界面时预填）。</summary>
    private int lastStudentId = -1;

    /// <summary>正在播放的结算跳分协程（重入时先停掉旧的）。</summary>
    private Coroutine settleRoutine;

    /// <summary>
    /// 教程 / 「写给老师」两个长文本面板的操作对象。
    ///
    /// 两个面板的结构、坑、行为完全一样（ScrollRect + Viewport + Content + Text (Legacy)，
    /// 正文几千字要换行、要按字号重算高度、关闭按钮还可能被 Viewport 盖住），
    /// 所以共用 ScrollTextPanel 这一套实现，这里只负责在每次操作前把 Inspector 上的字段同步进去
    /// （见 EnsureViews）。协程句柄与 Resources 缓存也由它自己持有。
    /// </summary>
    private ScrollTextPanel tutorialView;
    private ScrollTextPanel forTeacherView;

    /// <summary>
    /// 结算跳分播完后要执行的回调。
    /// GameManager 用它来「动画结束后再销毁槽位里的牌」—— 这样逐组显示分数时盘面上的牌还在，
    /// 玩家能看出每一组是哪几个数字。null = 没有待办。
    /// </summary>
    private System.Action settleFinishedCallback;

    /// <summary>回调是否已经执行过（保证「恰好一次」：正常播完、中途组件被禁用，都只算一次）。</summary>
    private bool settleFinishedInvoked;

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

        // 关闭按钮防遮挡：越早做越好。面板此刻还是隐藏的也无所谓 —— SetAsLastSibling 不需要布局，
        // 先修好，免得玩家第一次打开教程就发现 × 点不动。
        EnsurePanelCloseButtonsClickable();
    }

    // ------------------------------------------------------------------
    // 长文本面板：把 Inspector 字段同步给 ScrollTextPanel
    // ------------------------------------------------------------------

    /// <summary>
    /// 取出（必要时创建）两个长文本面板的操作对象，并把 Inspector 上的当前值同步进去。
    ///
    /// 【为什么每次操作都同步】你在 Inspector 里改字号、换正文文件之后不必重启，
    /// 下次打开面板就按新值渲染；也避免出现「两处各存一份配置、以谁为准说不清」。
    ///
    /// 引用类的字段只做「非空覆盖」：Inspector 填了就以它为准，没填则沿用自动找到的那个
    /// （否则自动找到的 Text / 缓存的 TextAsset 会被每次同步的空值冲掉，退化为反复重新查找与加载）。
    /// </summary>
    private void EnsureViews()
    {
        if (tutorialView == null) tutorialView = new ScrollTextPanel();
        tutorialView.host = this;
        tutorialView.label = "教程";
        tutorialView.panel = tutorialPanel;
        if (tutorialText != null) tutorialView.text = tutorialText;
        if (tutorialScrollRect != null) tutorialView.scrollRect = tutorialScrollRect;
        if (tutorialContent != null) tutorialView.content = tutorialContent;
        tutorialView.resourcePath = tutorialResourcePath;
        tutorialView.fontSize = tutorialFontSize;
        tutorialView.sourceFontSize = tutorialSourceFontSize;
        tutorialView.lineSpacing = tutorialLineSpacing;
        tutorialView.contentPadding = tutorialContentPadding;
        tutorialView.richText = tutorialRichText;
        tutorialView.closeButtonName = panelCloseButtonName;
        tutorialView.autoRaiseCloseButton = autoRaiseCloseButtons;

        // 自动找到的引用写回 Inspector 字段，方便运行时选中 UIManager 直接看到是哪个物体
        if (tutorialText == null && tutorialView.text != null) tutorialText = tutorialView.text;
        if (tutorialContent == null && tutorialView.content != null) tutorialContent = tutorialView.content;

        if (forTeacherView == null) forTeacherView = new ScrollTextPanel();
        forTeacherView.host = this;
        forTeacherView.label = "写给老师";
        forTeacherView.panel = forTeacherPanel;
        if (forTeacherText != null) forTeacherView.text = forTeacherText;
        if (forTeacherScrollRect != null) forTeacherView.scrollRect = forTeacherScrollRect;
        if (forTeacherContent != null) forTeacherView.content = forTeacherContent;
        forTeacherView.resourcePath = forTeacherResourcePath;
        forTeacherView.fontSize = forTeacherFontSize;
        forTeacherView.sourceFontSize = forTeacherSourceFontSize;
        forTeacherView.lineSpacing = forTeacherLineSpacing;
        forTeacherView.contentPadding = forTeacherContentPadding;
        forTeacherView.richText = forTeacherRichText;
        forTeacherView.closeButtonName = panelCloseButtonName;
        forTeacherView.autoRaiseCloseButton = autoRaiseCloseButtons;

        if (forTeacherText == null && forTeacherView.text != null) forTeacherText = forTeacherView.text;
        if (forTeacherContent == null && forTeacherView.content != null) forTeacherContent = forTeacherView.content;
    }

    /// <summary>
    /// 对两个长文本面板各做一次「关闭按钮点得到吗」的检查与纠正。
    /// 启动时调一次；每次打开面板时 ScrollTextPanel.Show 里还会再调一次（幂等，无副作用）。
    /// </summary>
    public void EnsurePanelCloseButtonsClickable()
    {
        EnsureViews();
        tutorialView.EnsureCloseClickable();
        forTeacherView.EnsureCloseClickable();
    }

    // ------------------------------------------------------------------
    // 场景变化
    // ------------------------------------------------------------------

    /// <summary>
    /// 初始化界面：只显示 mainMenu，并确保删卡区、逐组分数字文本、学号输入界面都是隐藏的。
    /// 玩家点「开始游戏」时会先被挡住并弹出学号界面（见 GameManager.StartGame）。
    /// </summary>
    public void InitializeUi()
    {
        SetDeletePanelVisible(false);
        HideGroupScoreTexts();
        HideDedicatedTotalText();
        SetStudentIdPanelVisible(false);
        ClearStudentIdHint();

        // 教程 / 写给老师：先把正文写好（面板此刻是隐藏的也无所谓，写 Text 不要求物体激活），再收起来。
        // 高度拟合放在这里做没有意义（面板未激活时布局不重算），所以只写内容，
        // 等玩家真正打开面板时由 ScrollTextPanel.Show 里等一帧再拟合。
        ApplyTutorialContent();
        ApplyForTeacherContent();
        HideTutorial();
        HideForTeacher();

        // 两个长文本面板的关闭按钮防遮挡（Awake 里已经做过一次，这里再来一次：
        // 防的是「开局前有人在编辑器 / 别的脚本里动过层级」）
        EnsurePanelCloseButtonsClickable();

        // 把排行榜文本按当前记录刷新一遍：同一台机器上换人玩时不会看到上一次的旧内容
        RefreshRankingText(rankingSortMode);

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

    /// <summary>
    /// rankingPanel 是否挂在 mainMenuPanel 底下。
    /// 【为什么需要判断】排行榜若是主菜单的子物体，就不能用「四面板互斥」的写法打开它 ——
    /// 那会把 mainMenuPanel 关掉，而 rankingPanel 作为子物体会被一起关掉，界面上什么都看不到。
    /// </summary>
    private bool RankingNestedInMainMenu()
    {
        if (rankingPanel == null || mainMenuPanel == null) return false;

        return rankingPanel.transform.IsChildOf(mainMenuPanel.transform);
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

    /// <summary>面板互斥切换。只传需要打开的那一个。</summary>
    private void SetPanelsActive(
        bool showMainMenu = false,
        bool showGame = false,
        bool showEndMenu = false)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(showMainMenu);
        if (gamePanel != null) gamePanel.SetActive(showGame);
        if (endMenuPanel != null) endMenuPanel.SetActive(showEndMenu);
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
    /// 【动画期间不动盘面】本方法只负责分数文本与面板，不会去隐藏/销毁任何卡牌，
    /// 槽位里的牌保持原样留在槽位上，玩家可以边看分数边对照牌面。
    /// </summary>
    /// <param name="groupScores">每组的分数，索引与 SlotManager.slotGroups 一致。</param>
    /// <param name="groupNames">每组的显示名，可为 null。</param>
    /// <param name="total">总分（= 各组分数之和）。</param>
    /// <param name="detail">endPanel 上的明细文本。</param>
    /// <param name="onFinished">
    /// 动画播完（endPanel 弹出前）执行的回调，可省略。
    /// GameManager 传的是「销毁槽位里的牌 + 清空槽位」，所以这些牌会一直留到动画结束。
    /// 无论走不走协程（组件被禁用时退回同步分支），回调都会且只会被执行一次。
    /// </param>
    public void PlaySettleSequence(List<int> groupScores, List<string> groupNames, int total, string detail,
        System.Action onFinished = null)
    {
        settleFinishedCallback = onFinished;
        settleFinishedInvoked = false;

        if (!isActiveAndEnabled)
        {
            // 组件或物体没启用时起不了协程 —— 直接出结算画面，绝不让玩家看不到结果
            ShowSettleTotalText(total);
            ShowEndMenu(total, detail);
            InvokeSettleFinished();
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
                SetLabelVisible(label, true);
            }

            if (settleStepInterval > 0f) yield return new WaitForSecondsRealtime(settleStepInterval);
        }

        // 各组都显示完了，最后才显示总分
        ShowSettleTotalText(total);

        if (settleHoldAfterTotal > 0f) yield return new WaitForSecondsRealtime(settleHoldAfterTotal);

        LockFlowButtons(false);
        IsPlayingSettleSequence = false;
        settleRoutine = null;

        // 先让 GameManager 收尾（销毁槽位里的牌、清空槽位），再弹出 endPanel：
        // 牌要一直留到这一刻，前面逐组显示分数时盘面上才有牌可对照
        InvokeSettleFinished();

        ShowEndMenu(total, detail);
    }

    /// <summary>
    /// 执行「动画播完」的回调，并保证**恰好执行一次**。
    /// 三条路径汇到这里：协程正常播完、组件被禁用导致协程被强制中止、组件本就不可用走了同步分支。
    /// 【为什么必须保证一次】回调里做的是「销毁槽位里的牌」——
    /// 漏做会让牌一直留在槽位上、下次结算被重复算分；做两次则会在对象已销毁后再动它。
    /// </summary>
    private void InvokeSettleFinished()
    {
        if (settleFinishedInvoked) return;
        settleFinishedInvoked = true;

        System.Action callback = settleFinishedCallback;
        settleFinishedCallback = null;

        if (callback != null) callback();
    }

    /// <summary>
    /// 显示 / 收起一个分数文本。
    ///
    /// 【为什么只切 Text 组件、绝不 SetActive(false) 物体】
    /// 这些分数文本很可能被摆成某一组的「组标题」，甚至直接挂在槽位组的容器上。
    /// 一旦用 SetActive(false) 把物体关掉，该组下面的槽位、以及已经放进去的牌会跟着一起消失——
    /// 表现出来就是「结算的时候牌被隐藏了」。只切组件开关就没有这个副作用：
    /// 文字不显示，物体和它的子物体全都原样不动。
    /// </summary>
    public static void SetLabelVisible(Text label, bool visible)
    {
        if (label == null) return;

        if (visible)
        {
            // 物体本身可能被你在场景里放成了未激活，这里补一次激活
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            if (!label.enabled) label.enabled = true;
            return;
        }

        label.text = string.Empty;
        if (label.enabled) label.enabled = false;
    }

    /// <summary>
    /// 隐藏全部组分数文本（开局、返回游戏面板、跳分动画开始时调用）。
    /// 只关 Text 组件、不清内容以外的任何东西，物体会员（槽位、牌）一律不受影响。
    /// </summary>
    public void HideGroupScoreTexts()
    {
        if (groupScoreTexts == null) return;

        for (int i = 0; i < groupScoreTexts.Count; i++)
        {
            SetLabelVisible(groupScoreTexts[i], false);
        }
    }

    /// <summary>
    /// 隐藏「专用总分文本」。
    /// 若没拖专用文本（总分复用常驻的 scoreText），就什么都不做 —— scoreText 本来就要一直显示。
    /// </summary>
    private void HideDedicatedTotalText()
    {
        if (settleTotalScoreText == null || settleTotalScoreText == scoreText) return;

        SetLabelVisible(settleTotalScoreText, false);
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
            if (!target.enabled) target.enabled = true;
        }
        else
        {
            target.text = FormatTotalScore(settleTotalFormat, total);
            SetLabelVisible(target, true);
        }
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
    /// 同时把「动画播完」的回调补执行一次：那里面是「销毁槽位里的牌」，
    /// 漏做的话这些牌会一直挂在槽位上，下次结算被重复算分。
    /// </summary>
    private void OnDisable()
    {
        // 本物体被禁用时 Unity 会把协程一起中止，但句柄还留在 ScrollTextPanel 手里，
        // 不清理的话下次 StopCoroutine 会报「Coroutine couldn't be stopped」
        if (tutorialView != null) tutorialView.CancelFit();
        if (forTeacherView != null) forTeacherView.CancelFit();

        if (!IsPlayingSettleSequence) return;

        IsPlayingSettleSequence = false;
        settleRoutine = null;
        LockFlowButtons(false);

        InvokeSettleFinished();
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
    // 长文本面板（教程 / 写给老师）
    //
    // 实现全部在 ScrollTextPanel 里（两个面板共用一套）：
    //   ApplyContent  写正文 + 强制打开「字号能生效」的开关
    //   FitLayout     按字号重算 Text 与 Content 的高度，让滚动范围跟着字号走
    //   Show / Hide   显示隐藏（Show 内部顺序：先修关闭按钮层级 → 激活 → 写正文 → 等一帧拟合）
    //   EnsureCloseClickable  关闭按钮防遮挡
    // 本类只负责「把 Inspector 字段同步进去」以及对外提供入口。
    // ------------------------------------------------------------------

    /// <summary>教程面板当前是否可见。</summary>
    public bool IsTutorialVisible
    {
        get { EnsureViews(); return tutorialView.IsVisible; }
    }

    /// <summary>
    /// 打开教程面板。
    /// 建议：把主菜单 / 游戏面板上的「教程」按钮的 OnClick 接到这里，
    /// 而不是直接在 Inspector 里对面板调 SetActive(true) —— 那样不会重新写正文、也不会拟合高度。
    /// </summary>
    public void ShowTutorial()
    {
        EnsureViews();
        tutorialView.Show();
    }

    /// <summary>
    /// 关闭教程面板。
    /// 建议：把面板里 close 按钮的 OnClick 接到这里，而不是直接对面板调 SetActive(false) ——
    /// 那样虽然也能关掉，但拟合协程不会被停、`IsTutorialVisible` 之外的内部状态也不受控。
    /// （两种接法代码都兼容，只是推荐走这里。）
    /// </summary>
    public void HideTutorial()
    {
        EnsureViews();
        tutorialView.Hide();
    }

    /// <summary>
    /// 把教程正文写进 Text，并强制打开「字号真的能生效」的那几个开关。
    ///
    /// 【「改了 Font Size 却没反应 / 文字被吃掉」的三个原因】
    ///   1. <b>Best Fit（resizeTextForBestFit）</b>：一旦勾上，Unity 就**完全忽略** Font Size，
    ///      把文字自动缩放到 Min~Max 之间 —— 在 Inspector 里怎么改都没用。代码强制关掉。
    ///   2. <b>Vertical Overflow = Truncate</b>：文本比 RectTransform 高时它**静默裁掉尾巴**，
    ///      既不报错、也拿不到真实内容高度。长正文放进 ScrollView 必须改成 Overflow，
    ///      否则滚动范围会按「被裁过的高度」算，字号一变就整段看不全。
    ///   3. <b>高度是写死的</b>：Content 的高度不随字号变，滚动范围一直按旧高度算。
    ///      高度由 FitTutorialLayout() 负责。
    ///
    /// 正文里的 &lt;size=65&gt; 标题标签会按「目标字号 ÷ 原文基准字号」等比缩放，
    /// 这样把字号从 45 调到 30 时标题也会一起变小，不会头重脚轻。
    ///
    /// 实现见 ScrollTextPanel.ApplyContent（「写给老师」面板走的是同一个方法）。
    /// </summary>
    public void ApplyTutorialContent()
    {
        EnsureViews();
        tutorialView.ApplyContent();
    }

    /// <summary>
    /// 请求「把教程内容高度拟合一次」。
    /// 面板没显示时会被忽略（未激活时布局不重算，拟合出来的数字没有意义）。
    /// 协程起不来（组件被禁用）时退化为同步做一次 —— 总比什么都不做好。
    /// </summary>
    public void RequestTutorialLayoutFit()
    {
        EnsureViews();
        tutorialView.RequestFit();
    }

    /// <summary>
    /// 按当前字号把「Text 高度」和「Content 高度」算对，让 ScrollView 的滚动范围跟着字号走。
    /// 空内容高度、Content 上没有 ContentSizeFitter / LayoutGroup 时手动补尺寸；
    /// 若有布局组件则自动让位，不会和你的设置打架。实现见 ScrollTextPanel.FitLayout。
    /// </summary>
    public void FitTutorialLayout()
    {
        EnsureViews();
        tutorialView.FitLayout();
    }

    /// <summary>
    /// 这个 RectTransform 的尺寸是否由布局系统托管（ContentSizeFitter 或 LayoutGroup）。
    /// 是的话就不要再手动 SetSizeWithCurrentAnchors —— 双方每帧互相覆盖，尺寸会抖。
    /// </summary>
    public static bool IsSizeDrivenByLayout(RectTransform rect)
    {
        return ScrollTextPanel.IsSizeDrivenByLayout(rect);
    }

    /// <summary>取教程的 ScrollRect：优先用拖进来的；没拖就从 Text / 面板往上找。</summary>
    public ScrollRect ResolveTutorialScrollRect()
    {
        EnsureViews();
        return tutorialView.ResolveScrollRect();
    }

    /// <summary>取教程正文：优先用拖进来的 TextAsset，否则从 Resources 按名字加载（加载一次后缓存）。</summary>
    public string ResolveTutorialRawText()
    {
        EnsureViews();
        return tutorialView.ResolveRawText();
    }

    /// <summary>
    /// 运行时调整教程字号（例如做「放大 / 缩小」两个按钮）。
    /// 只改 tutorialFontSize 一个字段，正文与高度拟合都走同一条路，不会出现「字号变了高度没变」。
    /// </summary>
    public void SetTutorialFontSize(float size)
    {
        tutorialFontSize = TutorialTextFormatter.ClampFontSize(size);
        EnsureViews();
        tutorialView.fontSize = tutorialFontSize;      // 立刻生效，不等下一次 EnsureViews
        tutorialView.ApplyContent();
        if (tutorialView.IsVisible) tutorialView.RequestFit();
    }

    // ------------------------------------------------------------------
    // 「写给老师」面板（与教程同构，共用 ScrollTextPanel 的实现）
    // ------------------------------------------------------------------

    /// <summary>「写给老师」面板当前是否可见。</summary>
    public bool IsForTeacherVisible
    {
        get { EnsureViews(); return forTeacherView.IsVisible; }
    }

    /// <summary>
    /// 打开「写给老师」面板。
    /// 建议：主菜单上的「写给老师」按钮 OnClick 接到这里（而不是直接 SetActive(true)），
    /// 这样才会重新写正文并拟合高度。
    /// </summary>
    public void ShowForTeacher()
    {
        EnsureViews();
        forTeacherView.Show();
    }

    /// <summary>
    /// 关闭「写给老师」面板。
    /// 建议：面板里 close 按钮的 OnClick 接到这里（而不是直接 SetActive(false)）。
    /// </summary>
    public void HideForTeacher()
    {
        EnsureViews();
        forTeacherView.Hide();
    }

    /// <summary>把 Resources/forTeacher.txt 的正文写进 forTeacherText（实现同教程）。</summary>
    public void ApplyForTeacherContent()
    {
        EnsureViews();
        forTeacherView.ApplyContent();
    }

    /// <summary>请求「把写给老师面板的内容高度拟合一次」。</summary>
    public void RequestForTeacherLayoutFit()
    {
        EnsureViews();
        forTeacherView.RequestFit();
    }

    /// <summary>按当前字号把写给老师面板的 Text / Content 高度算对。</summary>
    public void FitForTeacherLayout()
    {
        EnsureViews();
        forTeacherView.FitLayout();
    }

    /// <summary>运行时调整写给老师面板的字号。</summary>
    public void SetForTeacherFontSize(float size)
    {
        forTeacherFontSize = TutorialTextFormatter.ClampFontSize(size);
        EnsureViews();
        forTeacherView.fontSize = forTeacherFontSize;
        forTeacherView.ApplyContent();
        if (forTeacherView.IsVisible) forTeacherView.RequestFit();
    }

    // ------------------------------------------------------------------
    // 学号输入（startGame 之前的环节）
    // ------------------------------------------------------------------

    /// <summary>
    /// 从输入框读玩家输入的学号并校验。
    /// 数据源优先 studentIdInput（InputField），没拖则退回 studentIdText（自定义数字键盘方案）。
    /// 校验本身交给纯静态的 StudentIdValidator，这里只负责「从哪里取字」。
    /// </summary>
    /// <returns>true = 输入合法，id 已填好；false = error 是需要显示给玩家的中文提示。</returns>
    public bool TryReadStudentId(int min, int max, out int id, out string error)
    {
        id = -1;
        error = null;

        string raw = GetStudentIdRaw();
        if (raw == null)
        {
            error = "没有找到学号输入框，请在 UIManager 上把 studentIdInput（或 studentIdText）拖入。";
            return false;
        }

        return StudentIdValidator.TryParse(raw, min, max, out id, out error);
    }

    /// <summary>取当前输入原文（InputField 优先，其次 Text）。两个都没拖时返回 null。</summary>
    public string GetStudentIdRaw()
    {
        if (studentIdInput != null) return studentIdInput.text;
        return null;
    }

    /// <summary>写回输入内容（InputField 优先，其次 Text），并顺手清掉上一次的报错。</summary>
    private void SetStudentIdRaw(string text)
    {
        if (studentIdInput != null) studentIdInput.text = text;
        else return;

        ClearStudentIdHint();
    }

    /// <summary>「确认」按钮的入口：交给 GameManager 校验学号并开始游戏（校验失败会走 ShowStudentIdHint）。</summary>
    public void SubmitStudentId()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[UIManager] 场景里没有 GameManager，无法提交学号。");
            return;
        }

        GameManager.Instance.ConfirmStudentIdAndStart();
    }

    /// <summary>
    /// 学号界面上「返回」的入口：收起学号界面、清掉提示、回到主菜单。
    /// 不动已经确认过的学号（已经确认过的话仍然保留，不会把玩家已输的号抹掉）。
    /// </summary>
    public void CancelStudentId()
    {
        ClearStudentIdHint();
        SetStudentIdPanelVisible(false);
        ShowMainMenu();
    }

    /// <summary>显示学号界面（并清掉上一次的报错）。</summary>
    public void ShowStudentIdPanel()
    {
        // 勾了「当作独立面板」就先把其它面板全收起来，避免两层界面叠在一起
        if (hideOtherPanelsWhenInputId) SetPanelsActive();

        SetStudentIdPanelVisible(true);
        PrefillStudentIdInput();
        ClearStudentIdHint();

        if (studentIdInput != null) studentIdInput.ActivateInputField();
    }

    /// <summary>显示 / 隐藏学号界面。面板没拖就什么都不做（输入框可以直接摆在主菜单里）。</summary>
    public void SetStudentIdPanelVisible(bool visible)
    {
        if (studentIdPanel == null) return;
        if (studentIdPanel.activeSelf != visible) studentIdPanel.SetActive(visible);
    }

    /// <summary>把报错原因写到提示文本上（文本没拖时只打日志，不会静默失败）。</summary>
    public void ShowStudentIdHint(string message)
    {
        if (studentIdHintText != null) studentIdHintText.text = message ?? string.Empty;

        Debug.LogWarning("[UIManager] 学号校验未通过：" + message);
    }

    /// <summary>清空提示文本。</summary>
    public void ClearStudentIdHint()
    {
        if (studentIdHintText != null) studentIdHintText.text = string.Empty;
    }

    /// <summary>
    /// 记住上一次输入的学号，下次打开界面时预填。
    /// 只在**校验通过**时调用，所以预填进输入框的一定是个合法学号。
    /// </summary>
    public void RememberStudentId(int id)
    {
        if (id < 0) return;

        lastStudentId = id;
        if (prefillLastStudentId) PrefillStudentIdInput();
    }

    /// <summary>把上次的学号写进输入框（仅在输入为空时填，避免覆盖玩家已经打了一半的内容）。</summary>
    private void PrefillStudentIdInput()
    {
        if (!prefillLastStudentId || lastStudentId < 0) return;

        string current = GetStudentIdRaw();
        if (current == null) return;
        if (!string.IsNullOrEmpty(StudentIdValidator.Normalize(current))) return;

        SetStudentIdRaw(lastStudentId.ToString());
    }

    // ------------------------------------------------------------------
    // 排行榜：四个指标各一个排行函数（默认按学号），结果写进 rankingText
    // ------------------------------------------------------------------

    /// <summary>排行函数 1：按学号排列（默认）。</summary>
    public void ShowRankingByStudentId() { ShowRanking(RankingSortMode.ByStudentId); }

    /// <summary>排行函数 2：按平均分排列。</summary>
    public void ShowRankingByAverage() { ShowRanking(RankingSortMode.ByAverage); }

    /// <summary>排行函数 3：按最高得分排列。</summary>
    public void ShowRankingByHighScore() { ShowRanking(RankingSortMode.ByHighScore); }

    /// <summary>排行函数 4：按游玩局数排列。</summary>
    public void ShowRankingByPlayCount() { ShowRanking(RankingSortMode.ByPlayCount); }

    /// <summary>按指定排序方式显示排行榜。四个按钮可以分别接上面四个方法。</summary>
    public void ShowRanking(RankingSortMode mode)
    {
        rankingSortMode = mode;
        RefreshRankingText(mode);

        if (rankingPanel == null) return;

        if (RankingNestedInMainMenu())
        {
            // 排行榜嵌在主菜单里：只收掉会挡视线的另外两个面板，绝不能关 mainMenuPanel
            if (gamePanel != null && gamePanel.activeSelf) gamePanel.SetActive(false);
            if (endMenuPanel != null && endMenuPanel.activeSelf) endMenuPanel.SetActive(false);
            if (!mainMenuPanel.activeSelf) mainMenuPanel.SetActive(true);
            if (!rankingPanel.activeSelf) rankingPanel.SetActive(true);
            return;
        }

    }

    /// <summary>按 0~3 的整数选择排序方式（便于给下拉框、或一个按钮循环切换用）。</summary>
    public void ShowRankingByModeIndex(int modeIndex)
    {
        ShowRanking(RankingSort.ToMode(modeIndex));
    }

    /// <summary>
    /// 刷新排行榜：**重新读一遍记录文件** + 按当前排序方式重写 rankingText。
    ///
    /// 由 GameManager.InitializeGame 调用（开局、从结算画面回主菜单时都会走一次），
    /// 所以主菜单里看到的排行榜永远是磁盘上最新的一份记录，而不是上一次运行留下的旧文本。
    /// 读盘失败时 ScoreRecordStore 会保留内存里已有的记录，不会把榜刷成空的。
    /// </summary>
    public void RefreshRanking()
    {
        ScoreRecordStore.ReloadFromDisk();
        RefreshRankingText(rankingSortMode);
    }

    /// <summary>只刷新 rankingText 的内容，不动面板显隐（切换排序方式、记录变动后刷新时用）。</summary>
    public void RefreshRankingText(RankingSortMode mode)
    {
        if (rankingText == null) return;

        List<StudentRecord> ranked = RankingSort.Rank(ScoreRecordStore.Records, mode);

        rankingText.text = RankingTextBuilder.Build(
            ranked,
            mode,
            rankingMaxRows,
            rankingTitleFormat,
            rankingHeaderLine,
            rankingLineFormat,
            rankingAverageFormat,
            rankingEmptyText);
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
