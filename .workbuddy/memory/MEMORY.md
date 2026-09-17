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

## 验证手段
- 本机 Unity 无法批处理编译（编辑器锁工程 + UPM 服务起不来）。
- 校验配方（**已升级，优先用第二条**）：
  1. 离线类型检查：dotnet 工程引用 Unity 程序集编译 `Assets/scripts/*.cs`，配方见 `.workbuddy/memory/2026-09-15.md`。
  2. **离线逻辑回归（可执行真实代码）**：把 `Assets/scripts/*.cs` 与一个带 `Main` 的测试文件
     一起编成 net8.0 控制台 exe 直接运行。MonoBehaviour / ScriptableObject 的**纯静态方法**
     可以在没有 Unity 运行时的情况下正常调用（能加载 UnityEngine.CoreModule，实测可行），
     因此数列判定这类纯逻辑可以写用例回归，不必靠阅读代码判断对错。
     dotnet 必须补 `APPDATA` / `PROGRAMFILES` / `HOMEDRIVE` / `HOMEPATH` 环境变量，否则 NuGet 还原失败。
