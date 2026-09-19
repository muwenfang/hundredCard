# Hundred Cards — 项目长期约定

Unity **2022.3.62f3c1**，`D:\unity\Hundred Cards`，场景 `Assets/Scenes/SampleScene.unity`。UI 是 uGUI + **Text (Legacy)**（唯一例外：排行榜下拉框是 **TMP_Dropdown**）。固定 **1920×1080**，Canvas 恒定像素 → UI 数值即真实像素。

## 铁律
1. **不要用代码/编辑器工具生成 UI**：层级用户自己搭，代码只暴露 `public` 字段让用户拖。
2. **用户会直接改脚本**：动手前先读文件，别凭记忆覆盖。
3. `UIManager.autoBindButtons` 关闭，别打开。
4. UI 与数据一起改：只走 `CardSlot.Place` / `ReturnCardToHand` / `DeleteCard`。
5. 改 `isDeletePhase` 必调 `RefreshDeletePhaseUi()`；分数文本显隐只切 `Text.enabled`，**绝不 `SetActive(false)`**。
6. ⚠️ **Unity 开着时别改 `SampleScene.unity`**（会被内存版本覆盖）：要改就做进**代码默认值**或让用户在 Inspector 改。

## 规则与结算
- 槽位 4 组（3×4+1×2）：`slotGroups[].container` + `GetComponentsInChildren<CardSlot>(true)`（`true` 不能省）；组与类型解绑（`candidateTypes` + `MinimumValueCount`：等差 ≥3、雀头 =2）。
- 算分唯一实现 `SlotManager.EvaluateHand`（纯静态）：1 底分 + 20 条加分（`ScoreRules`）；合规 = 槽满 + 每条数列命中 + 雀头相同，否则 0；公差/公比**分层不叠加**、公比用分数约分。`ruleLines` **恒 20 行**且只显示**命中**项；`ScoreRuleCatalog.All` 唯一清单；`ScoreItem` 只有 3 参构造。
- 删卡拖满 2 张到 `deletionPanels`（`RectangleContainsScreenPoint`，不靠射线），`EnsureDeletePhaseSolvable()` 防卡死；结算销毁槽位牌推迟到 `FinishSettlement()`（回调恰好一次）。`Canvas/endPanel/detail` 是 ScrollRect，`FitEndMenuDetail()` 强制 Overflow、**撑高不缩字**。

## 学号 / 记录 / 排行榜
- `StudentIdValidator.TryParse`：全角转半角、纯数字、0~300；校验失败**先 `ShowStudentIdPanel()` 再 `ShowStudentIdHint()`**。
- `ScoreRecordStore` 静态零连线，落盘 `persistentDataPath/score_records.csv`（按学号聚合，平均分现算）。记录时机在纯静态 `GameManager.EvaluateRecordDecision`，五个调用点要齐（`EndGame`/`GameOver`/`InitializeGame`（**在 `ResetStudentId()` 前**）/`ExitGame`/`OnApplicationQuit`）。坑：点「结算」结束的局**不走 `EndGame()`**。
- 列顺序固定 学号→平均分→最高得分→游玩局数；标题/表头「填了才输出」，场景留空（表头在 `MainMenu/ranking/tag`）。
  - **列对齐**：`\t` 在 uGUI Text 里零宽，「制表符对齐」得自己算 —— 每列补到**本列最宽值**，用 `<color=#00000000>` 的**透明 0 / .** 补齐（与真实字形同宽，像素级精确，与字体无关）。单位宽：数字 2 / 小数点·空格 1 / CJK 3.6。开关 `rankingAlignColumns`(true) 会**强制 `supportRichText`**；表头**不参与**列宽；间距 `rankingColumnSpaces`（场景 12）。
- **排序下拉框**（`Canvas/MainMenu/Dropdown`，**TMP_Dropdown**，已 `using TMPro`）：TMP_Dropdown 的弹出列表 = 实例化 Template 后**只把第一个 Toggle 当行模板**复制 N（=Options 条数）份；行文字取自 **Options**，**不是** item 上手写的 Text；还必须有 `Item Text` 才画得出字。对策：`SetupRankingDropdown()`（幂等，Awake + InitializeUi 各一次）重写 Options / 自动补 `Item Text` / 多余 item 运行时 `SetActive(false)`；同步用 `SetValueWithoutNotify`；`ToMode(i)` 把索引当枚举值 → **选项顺序不能错位**。

## 长文本面板（教程 / 写给老师）
- 共用 `ScrollTextPanel.cs`（**普通类**）：正文放 `Resources/tutorial.txt` / `forTeacher.txt`，**别塞 C# 字符串**；`FindContentText()` **只在 `ScrollRect.content` 子树里找** Text；close 要 `SetAsLastSibling()`（同父下越靠后越上层，射线取第一个命中者）。
- 「改了 Font Size 没反应」真凶是 **`Vertical Overflow = Truncate`**（静默裁尾）+ 高度写死 → 强制 Overflow + 按 `preferredHeight` 重算；**先 SetActive → 再写正文 → 再等一帧拟合**。整理逻辑在 `TutorialTextFormatter.Prepare`，45 号字是恒等变换，**资源末尾不留换行**。

## 场景与验证
- 本机 Unity **无法批处理编译** → 用**离线回归**（细节见 `unity-offline-verify` 技能）：`%TEMP%/hc_check` 编 net8.0 exe 直跑 `Assets/scripts/*.cs`；**跑完必须 `grep FAIL`**；`--dump-ranking` 打印真实排版。
