# Hundred Cards — 项目长期约定

## 工程
- Unity **2022.3.62f3c1**，工程路径 `D:\unity\Hundred Cards`，场景 `Assets/Scenes/SampleScene.unity`。
- UI 用 **uGUI + 传统 Text（Text (Legacy)）**，不是 TextMeshPro。引用 `UnityEngine.UI.dll`。
- 牌面资产：`Assets/Resources/allCards/01.asset ~ 100.asset`（NumberCardData，1~100 各 2 张）。
- 卡牌预制体：`Assets/prefabs/CardUI.prefab`（根节点 Image + CardUI + DragHandler + Text 子物体）。

## 工作方式约定（用户明确要求）
1. **不要用代码/编辑器工具生成 UI**。场景层级由用户在 Unity 里自己搭；
   代码只暴露 `public` 字段并在代码里做好数据连接，用户负责把物体拖进 Inspector。
   （曾写过一个 `Assets/Editor/HundredCardsSceneSetup.cs` 一键生成面板+槽位，用户要求删除。）
2. **UI 与数据必须一起改**。所有「换父物体 / 销毁卡牌」只能走这三个方法：
   - `CardSlot.Place(card)` — 放入槽位（写双向引用 + 改父物体 + 铺满）
   - `GameManager.ReturnCardToHand(card)` — 退回手牌（清双向引用 + 改父物体 + 还原手牌布局）
   - `GameManager.DeleteCard(card)` — 删卡（清双向引用 + 移出手牌列表 + 销毁物体）
   不要在别处单独改 `transform.parent` / `Destroy`，也不要只清 `CardSlot.currentCard` 或只清 `CardUI.currentSlot`。
3. **`UIManager.autoBindButtons` 当前是关闭的**（场景里存的是 0）。
   用户选择自己在 Inspector 里挂 Button 的 OnClick。不要擅自打开或再建议自动绑定。
4. **用户会直接改这些脚本**。每次动手前先重新读文件，不要凭记忆覆盖；
   `UIManager` 已被用户改过（删掉了 `endMenuTitleText`，`ShowEndMenu` 变成
   `(int finalScore, string detail)` 两个参数）。
5. `SlotManager.slotGroups` 用 **container 自动收集**：把每组槽位的父物体拖到 `container` 上，
   运行时 `GetComponentsInChildren<CardSlot>(true)` 收集（`true` 不能省，面板初始是隐藏的）。
   4 组部署：3 组各 4 槽 + 1 组 2 槽，共 14 个；分值 4/4/4/2（`pointsPerMatch`）。
   场景里 4 个 container 与 14 个槽位均已拖好。
6. **槽位组号与数列类型解绑（2026-09-17 起）**。每组持有一份 `candidateTypes` 列表，
   结算时对列表逐项判定，任意一项命中即本组合规。
   4 槽组默认 = 等差 / 等比 / 斐波那契（正好判三次）；2 槽组默认 = **雀头**（两张数字相同）。
   - 为什么 2 槽组不判等差等比：2 张牌必然满足等差与等比，会白送分。
     为此加了 `MinimumValueCount`（等差/等比/斐波那契需 ≥3 张，雀头需 =2 张），张数不足的类型直接跳过。
   - 旧的 `SlotGroup.sequenceType` 字段已删除（场景 YAML 里还残留 `sequenceType: N`，Unity 会自动忽略）。
     `candidateTypes` 为空时由 `EnsureCandidateTypes()` 按槽位数自动填默认值，无需手工再配。
   - `SlotGroup` 右键菜单「按槽位数填充默认判定类型」可把默认值写进场景。
7. **加分规则（2026-09-17 第四轮起，已整体替换）**。唯一实现在 `SlotManager.EvaluateHand`（纯静态）。
   - **单组不再加分**（`pointsPerMatch` / `awardedPoints` 已删除）。得分 = 1 分底分 + 20 条条件加分。
   - **合规闸门**：所有槽位放满 + 每条数列都命中 + 雀头两张相同，才叫合规；不合规整手 0 分。
   - 规则清单（数值集中在 `ScoreRules`）：时机 +100；同奇偶 +10；全质数 +100 / 全合数 +1；
     全等差 +5 / 全等比 +50 / 全斐波那契 +15 / 恰好各一个 +30；
     三等差拼 12 项 +30 / 两等差拼 8 项 +10 / 两斐波那契拼 8 项 +100 / 两条相等数列 +36 / 数字同属两数列 +6×个数；
     两等差同公差 +10 / 三等差同公差 +20 / 两等比同公比 +50 / 三等比同公比 +100；
     雀头质数 +1 / 雀头 1 或 100 +10。
   - 「三条公差均相同」与「两条相同」是**分层取值，不叠加**。
   - 拼合判定 = 多值并集排序后判该数列；重复数字自动让拼合失败。
   - 公比用**分数约分**比较（`TryGetCommonRatio`），不用浮点。
   - 实测：4 项时等差/等比/斐波那契两两互斥；3 项时 `a,2a,3a` 可同时等差 + 斐波那契。
   - `CalculateScore` 需要 `turnCount` 参数（为「第 1 回合 +100」）。
7. **删卡阶段（2026-09-17 起）**。`NextTurn()` = 抽 2 张 → `BeginDeletePhase()`，
   玩家必须把 **2 张**卡拖进 `UIManager.deletionPanels` 才能进入下一回合。
   - 「有且仅为 2 张」靠「删满 2 张即 `EndDeletePhase()` 并隐藏投放区」实现，想多删也没有区域可投。
   - 投放判定用 `RectTransformUtility.RectangleContainsScreenPoint`，**不依赖射线**；
     `UIManager.disableDeletePanelRaycast` 会把投放区自身 Graphic 的射线关掉，避免这块半透明区域挡住下面的卡。
   - 任何改动 `GameManager.isDeletePhase` 的地方都必须调 `RefreshDeletePhaseUi()`，否则界面与数据不同步。
   - `EnsureDeletePhaseSolvable()` 是防卡死自愈：删满就结束阶段；手牌不够 2 张也结束阶段
     （否则按钮永久禁用，玩家会被锁死）。
   - 按钮变红用的是「把 ColorBlock 四种状态全设红 + `interactable = false`」，退出时用快照还原。
     场景里 **`UIManager.nextTurnButton` 目前仍是空的（fileID: 0）**，不拖进去就不会变红。

## 学号 / 成绩记录 / 排行榜（2026-09-18 起）
8. **学号关卡**：`StartGame()` = 学号校验 + `StartGameInternal()`。`requireStudentId` 默认 **true**，
   但 `UIManager` 上一旦没有输入源（`studentIdInput` / `studentIdText` 都空）就**跳过校验并警告直接开局**
   —— 「忘了连线」不会把游戏卡死。校验失败时**必须先 `ShowStudentIdPanel()` 再 `ShowStudentIdHint()`**，
   因为前者内部会清提示（这个顺序错过一次，提示会被吞掉）。
   校验规则在 `StudentIdValidator.TryParse`：全角数字自动转半角、严格只收纯数字、0~300、返回中文提示。
   「确认」按钮两条连线都通：接 `UIManager.SubmitStudentId()` 或直接接 `GameManager.StartGame()`。
9. **成绩记录**：`ScoreRecordStore`（**静态类，零场景连线**）落盘
   `Application.persistentDataPath/score_records.csv`，一行 `学号,累计总分,最高得分,游玩局数`。
   **按学号聚合**（局数累加、最高分取历史最高），**平均分不落盘、显示时现算**。
   记录时机 = **一局结束**（`EndGame()` / `GameOver()`），靠 `resultRecordedThisGame` 幂等。
   排行排序在 `RankingSort`：四个函数 `RankByStudentId/ByAverage/ByHighScore/ByPlayCount`，
   默认按学号；平均分用**整数交叉相乘**比较；并列一律退化到学号升序。
   展示在 `RankingTextBuilder.Build`：**标题行与表头行「填了才输出」**，
   行内指标顺序固定 **学号 → 平均分 → 最高得分 → 游玩局数**。
10. **场景布局的坑**（已适配，别再改回去）：
   - `UIManager.rankingPanel` 指向 `Canvas/MainMenu/ranking`，即**排行面板嵌在主菜单里**。
     打开它不能用「四面板互斥」（会把 mainMenuPanel 一起关掉），代码里有 `RankingNestedInMainMenu()` 分支。
   - 表头已经单独画在 `Canvas/MainMenu/ranking/tag` 这个 Text 上，
     所以 `rankingHeaderLine` 默认留空，否则会出现两行表头。
   - 学号输入框用户已建好：`Canvas/inputID/inputField`（挂 InputField）→ `studentIdInput`，
     提示文本 = `Canvas/inputID/inputField/Text (Legacy)`。
   - 4 个组分数 Text = **`Canvas/gamePanel/slotGroups/sg1 ~ sg4`**，各自独立、没有子物体。
   - 真正的槽位组是 **4 个 prefab 实例**（`Assets/prefabs/slotGroup (1)~(4).prefab`），
     父物体 = `gamePanel/slotGroups`；`SlotManager.slotGroups[].container` 引用的 GameObject
     在场景 YAML 里是 `!u!1 &<id> stripped` 块（预制体实例内部物体不出现在场景层级里）。
   - `GameManager.handCardArea` = `gamePanel/handCardArea/Viewport/Content`（手牌是 ScrollRect）。
   - `Canvas/gamePanel/settlement/score` 同时被当作 `scoreText` 与 `settleTotalScoreText`（同一个 Text）。
   - `continueButton` 与 `returnToMainMenuButton` 连的是同一个按钮（用户自己的选择）。
11. **结算流程（2026-09-18 第二轮起）**。`Settle()` 只做「算分 + 播动画」，
   **销毁槽位里的牌与清空槽位推迟到 `FinishSettlement()`**，由 `UIManager.PlaySettleSequence`
   在跳分动画播完时回调 —— 逐组显示分数时盘面上的牌必须还在，玩家才对得上「这一组是哪几个数字」。
   - 回调保证**恰好执行一次**：协程播完 / `OnDisable` 中止协程 / 无 UIManager 的同步分支，三条路都走
     `InvokeSettleFinished()`。漏做会让牌一直留在槽位上被重复算分。
   - 动画期间 `GameManager.IsSettleSequencePlaying` 为 true，`DragHandler.IsBoardFrozen()` 会挡掉
     `OnBeginDrag` / `OnPointerClick`（盘面定格，牌不能被拖走或点回手牌）。
   - **分数文本的显隐只允许切 `Text.enabled`**（`UIManager.SetLabelVisible`），
     **绝不 SetActive(false) 物体** —— 万一某个分数 Text 被挪到槽位组容器上，
     SetActive 会把整组槽位和已放进去的牌一起关掉。
   - `GameManager.InitializeGame()` 会调 `UIManager.RefreshRanking()`（= 重新读盘 + 重建文本），
     位置在 `InitializeUi()` 之前。
   - `ScoreRecordStore.ReloadFromDisk()` 读盘失败时**保留内存里的旧记录**，不会把榜刷空。

## 长文本面板（教程 / 写给老师）（2026-09-18 第三轮起）
12. **两个长文本面板共用一套实现**：`Assets/scripts/ScrollTextPanel.cs`。
   教程（`Assets/Resources/tutorial.txt`，7839 字符 / 16 个 `<size=NN>` 标签）与
   写给老师（`Assets/Resources/forTeacher.txt`，1459 字符 / 2 个标签）的结构、坑、行为完全一样，
   所以 `UIManager` 只保留「把 Inspector 字段同步进去」的 `EnsureViews()`，
   逻辑全在 `ScrollTextPanel`：`Show/Hide/ApplyContent/FitLayout/RequestFit/SetFontSize/
   ResolveScrollRect/ResolveRawText/EnsureText/FindContentText/EnsureCloseClickable`。
   - **不要再把长正文塞进 C# 字符串常量**，放 Resources 里可直接编辑。
   - 已删除 `UIManager.tutorialFitRoutine` 与 `FitTutorialLayoutNextFrame`（协程句柄改由 ScrollTextPanel 持有，
     `OnDisable` 里对两个 view 各调一次 `CancelFit()`）。
   - `EnsureViews()` 每次操作前同步；**引用类字段只做「非空覆盖」**，
     否则自动找到的 Text 与缓存的 TextAsset 会被 Inspector 的空值冲掉。
   - `FindContentText()` **只在 `ScrollRect.content` 子树里找 Text** ——
     面板下还有关闭按钮上那个只写着「x」的 Text，全面板找会先把「x」抓来当正文。
   整理逻辑在纯静态的 `TutorialTextFormatter`（可离线回归）：
   `Prepare(raw, sourceFontSize, targetFontSize, richText)` = 归一 `\r` → 去行尾空白 →
   并回孤立 `</size>` → 折叠空行 → 按比例缩放 `<size=NN>`（或剥掉所有标签）。
   - 标题标签是**绝对像素值**（大标题 65 / 小标题 55，正文基准 45），所以**必须等比缩放**，
     否则改字号后标题头重脚轻。45 号字下 `Prepare` 是恒等变换（有断言保证首跑观感不变）。
   - **资源文件末尾不留换行**（`tutorial.txt` / `forTeacher.txt` 都是），
     这样文件本身就是 `Prepare` 的规范形式，「基准字号下恒等」这条断言才成立。
   - 正文里的 `>` 是数学比较符号（`10>8`、`等比数列>斐波那契数列`），Unity 富文本只认 `<` 开头，安全。
13. **「改了 Text 的 Font Size 没反应」的真实原因**（2026-09-18 现场确认，**不是 Best Fit**）：
   - `m_BestFit` 本来就是 0（关着），字号字段是可改的；
   - 真凶是 **`Vertical Overflow = Truncate`**：文本比 RectTransform 高时**静默裁掉尾巴**，
     既不报错也拿不到真实内容高度，字号一变大多出来的部分直接没了；
   - 加上 **Text 与 Content 的高度都是写死的**（Text 17824 / Content 18667），
     且 `Content` 上**没有 ContentSizeFitter、没有 VerticalLayoutGroup** —— 字号变了高度不跟着变，
     滚动范围一直按旧高度算，所以也滚不到被裁掉的部分。
   代码里的对策：强制 `resizeTextForBestFit=false` + `verticalOverflow=Overflow` +
   `FitLayout()` 按 `preferredHeight` 重算 Text 与 Content 高度（若 Content 挂了
   SizeFitter/LayoutGroup 则自动让位，见 `IsSizeDrivenByLayout`）。
   **顺序铁律：先 `SetActive(true)` 激活面板 → 再写正文 → 再等一帧拟合**，
   面板未激活时布局系统不重算，同帧读 `preferredHeight` 拿到的还是旧值。
14. **`Canvas/tutorial` 场景现状**（读 YAML 得到）：
   `Canvas/tutorial`（**初始未激活**）= Image + ScrollRect(仅纵向) + Button `close`
   → `Viewport`(Image + Mask) → `Content`（只有 RectTransform）→ 子物体有**两个重叠的文本**：
   - **`Text (Legacy)`** = 教程正文所在，`FontSize 45` / `RichText 1` / `VerticalOverflow 0(Truncate)` /
     字体 = 内置 Arial（动态字体，中文靠系统字体回退，能正常显示）
   - **`Text (TMP)`** = 残留占位物体，内容只有默认的 `New Text`，字体是默认 LiberationSans SDF
     （guid `8f586378b4e144a9851e7b34d9b748ee`）→ **改它的字号当然不影响教程**，
     建议删掉或取消激活（它和正文重叠，会让人误判）
   拖线：`UIManager.tutorialText` ← `Canvas/tutorial/Viewport/Content/Text (Legacy)`；
   `tutorialPanel` ← `Canvas/tutorial`；`tutorialScrollRect` 可留空（会自动往上找）。
   **字号只改 `UIManager.tutorialFontSize` 一处。**
15. **【uGUI 层级铁律】同一父物体下「越靠后 = 越上层」，
   射线也按同样顺序从高到低取第一个命中者。**
   凡是「按钮点了没反应，但它明明接好了 OnClick」，第一件事就是去数它的**同级顺序**。
   2026-09-18 的真实案例：`Canvas/tutorial` 的子物体顺序是
   `close → Viewport → Scrollbar Horizontal → Scrollbar Vertical`，
   `close` 在最底层，而 `Viewport` 的 Image 拉伸铺满整个面板且 `raycastTarget=1`
   （`Mask.showMaskGraphic=0` 只是不画出来，**照样挡射线**），
   Content 里的正文 Text 也铺得很长 ——
   于是点 × 时命中的是 Viewport/正文，事件被 ScrollRect 吃掉，
   **close 的 OnClick 根本不会触发**，看起来就是「close 接的是 SetActive(false) 却关不掉」。
   - 用真实 RectTransform 数值算过：close 矩形 x 1255.66→1340.04 / y 841.20→917.79，
     Viewport x 0→1362.70 / y 17→938.99（盖住），
     Content/Text x 63.71→1298.99 / y −16955.15→869.37（也盖住），
     两个滚动条与 close 不重叠（所以它们不是元凶）。
   - **「只把 Viewport 的 raycastTarget 关掉」不够**（正文 Text 还在挡）。
     唯一正解：**把 close 提到同级最后一位**（`SetAsLastSibling()`）。
   - 代码已内置护栏 `ScrollTextPanel.EnsureCloseClickable()`，在 `UIManager.Awake()` 与
     `InitializeUi()` 各跑一次，并在每次 `Show()` 时再跑一次（幂等）：
     先算出「排在 close 之后且几何覆盖它」的 Graphic 名单并告警，再 `SetAsLastSibling()`。
     `autoRaiseCloseButtons=false` 时只告警不自动改。
   - 判断遮挡用 **`activeSelf` 而不是 `activeInHierarchy`** ——
     面板初始是隐藏的，用后者会永远算出「没人挡」。
   - 通用排查工具：`ScrollPanelLayout`（纯静态、可离线回归）——
     `RectInParent`（锚点→父坐标系矩形换算）、`Overlaps`、`WouldBlock`、`FindBlockers`、`DescribeBlockers`。
16. **`Canvas/forTeacher` 场景现状**：子物体顺序是
   `Viewport → Scrollbar Horizontal → Scrollbar Vertical → close`（**close 在最后，所以它能关** ——
   同一个 bug 只暴露了一半）。`Viewport/Content` 下**只有一个 `Image`、没有 Text**，
   所以要把 `forTeacher.txt` 显示出来，需要在 `Content` 下建一个 `Text (Legacy)`
   并拖到 `UIManager.forTeacherText`（留空也行，代码会自动在 `ScrollRect.content` 子树里找）。
   它的 `close` 在**左上角**（x 从 −21 开始，探出面板左边缘 21px），和 tutorial 的右上角不一致；
   ScrollRect 的横向滚动是开着的（`h=1`），正文用 Wrap 其实不需要。
   `forTeacher` 在 Canvas 里排在 `tutorial` **前面**（`background → MainMenu → gamePanel →
   endPanel → inputID → forTeacher → tutorial`），所以 tutorial 永远盖在 forTeacher 上。

## 读场景 YAML 的经验（重要）
- Unity 会把**非 ASCII 字符转义成 `\uXXXX`**，所以 `grep "学号" Assets/Scenes/*.unity` 一定搜不到。
  要搜中文，得搜转义形式（如 `5B66`），或者先写脚本反转义再匹配。
  （2026-09-18 因此误判「场景里还没有 inputID 物体」，实际早就搭好了。）
- 组件类型靠 `m_Script` 的 guid 认：Image=`fe87c0e1...`、Text=`5f7201a1...`、
  Button=`4e29b1a8...`、InputField=`d199490a83bb2b844b9695cbf13b01ef`、
  ScrollRect=`1aa08ab6...`、Scrollbar=`2a4db7a1...`、
  **TextMeshProUGUI=`f4688fdb...`**、**Mask=`31a19414...`**。
- 认「哪些物体在场景里 vs 哪些在预制体里」：预制体实例的**内部**物体在场景 YAML 里是
  `!u!1 &<id> stripped` 占位块，只有 `m_Modifications` 会出现在场景里。
- **Text 的字号字段藏在 `m_FontData:` 块里面**（`m_Font` / `m_FontSize` / `m_BestFit` /
  `m_MinSize` / `m_MaxSize` / `m_Alignment` / `m_RichText` / `m_HorizontalOverflow` /
  `m_VerticalOverflow` / `m_LineSpacing`），不在顶层。`m_VerticalOverflow: 0` = **Truncate**。

## docx → Unity 长文本的抽取配方（2026-09-18 起）
- docx 就是个 zip，直接读 `word/document.xml` 即可，不需要 python-docx：
  按 `<w:p>` 切段 → 段内拼所有 `<w:t>` 的内容。注意 `<w:pPr>` 也会匹配到 `<w:p` 前缀，
  正则要用 `<w:p[ >]` 卡边界。
- 读出来的文本用 **universal newline 模式**打开会顺手把 Word 的段内换行 `\r` 变成 `\n`，
  表现为「`</size>` 掉到单独一行」—— 这是预期行为，靠 `MergeOrphanSizeCloseTags` 并回去即可。

## 验证手段

- 本机 Unity 无法批处理编译（编辑器锁工程 + UPM 服务起不来）。
- 校验配方（**已升级，优先用第二条**）：
  1. 离线类型检查：dotnet 工程引用 Unity 程序集编译 `Assets/scripts/*.cs`，配方见 `.workbuddy/memory/2026-09-15.md`。
  2. **离线逻辑回归（可执行真实代码）**：把 `Assets/scripts/*.cs` 与一个带 `Main` 的测试文件
     一起编成 net8.0 控制台 exe 直接运行。MonoBehaviour / ScriptableObject 的**纯静态方法**
     可以在没有 Unity 运行时的情况下正常调用（能加载 UnityEngine.CoreModule，实测可行），
     因此数列判定这类纯逻辑可以写用例回归，不必靠阅读代码判断对错。
     dotnet 必须补 `APPDATA` / `PROGRAMFILES` / `HOMEDRIVE` / `HOMEPATH` 环境变量，否则 NuGet 还原失败。
     工程在 `%TEMP%/hc_check`，`hc_check.csproj` 用通配符 `<Compile Include="...\Assets\scripts\*.cs" />`
     + `Program.cs`，所以**新加脚本不用改工程文件**。
     当前累计 **308 项用例**（截至 2026-09-18 第四轮）。
- 纯静态类（`SlotManager` / `ScoreRecords` / `TutorialTextFormatter` / `ScrollPanelLayout`）都能离线跑用例；
  MonoBehaviour 的生命周期（协程、SetActive、布局重建、射线命中）**离线测不了，必须进 Unity 实测**。
  → 遇到「点不到 / 看不见」这类问题，**先用脚本按真实 RectTransform 数值复算几何**再下结论
  （配方见 `TEMP/hc_check/verify_close_block.py`），比在编辑器里试快得多，也能直接变成回归用例。
