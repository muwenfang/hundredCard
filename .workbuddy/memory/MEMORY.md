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
2. **UI 与数据必须一起改**。所有「换父物体」只能走两个方法：
   - `CardSlot.Place(card)` — 放入槽位（写双向引用 + 改父物体 + 铺满）
   - `GameManager.ReturnCardToHand(card)` — 退回手牌（清双向引用 + 改父物体 + 还原手牌布局）
   不要在别处单独改 `transform.parent`，也不要只清 `CardSlot.currentCard` 或只清 `CardUI.currentSlot`。
3. **`UIManager.autoBindButtons` 当前是关闭的**（场景里存的是 0）。
   用户选择自己在 Inspector 里挂 Button 的 OnClick。不要擅自打开或再建议自动绑定。
4. **用户会直接改这些脚本**。每次动手前先重新读文件，不要凭记忆覆盖；
   `UIManager` 已被用户改过（删掉了 `endMenuTitleText`，`ShowEndMenu` 变成
   `(int finalScore, string detail)` 两个参数）。
5. `SlotManager.slotGroups` 用 **container 自动收集**：把每组槽位的父物体拖到 `container` 上，
   运行时 `GetComponentsInChildren<CardSlot>(true)` 收集（`true` 不能省，面板初始是隐藏的）。
   4 组默认：等差 / 等比 / 质数 / 特殊，分值 4/4/4/2，共 3×4+2 = 14 个槽位。

## 验证手段
- 本机 Unity 无法批处理编译（编辑器锁工程 + UPM 服务起不来）。
- 可用 **dotnet + Unity 程序集离线类型检查**，配方见 `.workbuddy/memory/2026-09-15.md`。
