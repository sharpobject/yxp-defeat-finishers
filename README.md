# 鸭神与压路机

弈仙牌双角色淘汰演出 MOD，使用 [Airexplosion 的 MOD SDK](https://github.com/Airexplosion/yixianpai-mod-sdk)。屠馗完成《Street Fighter III: 3rd Strike》风格的瞬狱杀；林小月完成时停、飞刀与压路机演出。

v0.1.2 适配 Steam build `26939223`，修复内置复盘（Recap）的淘汰判断、Windows 出牌停顿和屠馗演出启动失败。

## 触发条件

- **屠馗 · 瞬狱杀**：在击杀并淘汰对手的同一回合，实际打出过「百杀破境掌」或「逍遥无影拳」。最后一击可以来自该回合的另一张牌、追击或回合结束效果。背后和背景显示红色书法「鸭」。
- **林小月 · 时停压路机**：同一回合中，先前已完整打出的一张牌实际攻击至少两次，并在这一次出牌中清除对手至少一层「护体」；之后「万法归灵剑」的直接攻击击杀并淘汰对手。猫影追加攻击计入攻击次数。

淘汰指对手不是心魔，并且因本次战败降至零或负命元。仅击败本轮但对手仍有命元时不会触发。普通回放、内置复盘和未结算练习输入都需要满足对应的出牌与淘汰条件。

两套演出替换符合条件的整个回合动画，保留原战场、左右站位、原生伤害计算和伤害飘字。林小月先前的攻击对应飞刀命中，万法归灵剑对应压路机落地。结束姿势及压路机保持到离开、跳过或重新播放该场战斗。

支持屠馗两套服装及其换色、林小月三套服装及其换色，共九种外观。人物与表情仅使用游戏自带贴图；林小月没有另绘喊叫面部。音量跟随游戏音效设置。新增外观需要另行检查骨骼与动作映射。

## v0.1.2 更新

- 根据当前游戏原始程序集重新生成战斗计算器。已结算 Recap 输入可能保留战前命元，MOD 校验记录的战斗结果后，使用游戏原生扣除规则判断最终命元。
- 保留游戏原本的出牌结果任务，只在效果执行前等待演出时间点，消除 Windows 缺失泛型 AOT 代码造成的第一张牌停顿。原生伤害、出牌结果与被动效果顺序保持不变。
- 屠馗膝部调整使用 Windows 已保留的原生时间线，避免调用被裁剪的 Spine 设置方法。20 个姿势的骨骼与世界网格对照完全一致；动作、表情和声音沿用 v0.1.1。
- 构建与打包必须通过同版本 Windows 原生 API 静态检查。缺失方法、字段、类型或泛型实例会在执行演出前被发现；游戏更新后必须准备匹配的检查输入。

## 安装

1. 安装与当前游戏版本相容的 Airexplosion MOD 加载器／管理器。
2. 从本项目的 Releases 下载 MOD ZIP，在管理器中导入并启用。
3. 完成正在进行的对局后，完整退出并重新启动游戏。

卸载时在管理器中禁用或移除本 MOD，然后重新启动游戏。不要与另一个已包含这两套演出的整合 MOD 同时启用。本仓库不提供游戏、加载器安装程序或游戏引用 DLL。

## 验证

当前插件 SHA-256：`30b06f86a170224ec83a0e971081b307f1ca950608dc45666a2f4a3aa8a02912`。静态检查通过，错误数为零；检查器也正确拒绝旧版两个实际缺失的原生 API、过期输入摘要和错误重载。全部九种外观绑定、最终直接引用、运行时反射禁用规则和官方 SDK checker 均已通过。

同一 DLL 使用官方 Windows 加载器 v1.0.1 附带的原始 SDK，在实际 Windows IL2CPP 客户端（macOS 上的 Whisky Wine）通过三项检查：

- 屠馗回放：完整 6.8 秒演出与原生伤害／命元结算。
- 林小月回放：完整 11.1 秒演出；飞刀阶段清除护体并造成伤害，压路机阶段完成致命伤害。
- 原始内置 Recap 战斗及上下文：完整 11.1 秒演出与正确淘汰结果，输入未转换为战绩回放。

三次运行均通过结束保持、重播清理和状态栏恢复；测试进程正常退出，并保留正在运行的游戏。两套回放均已取得 Windows 画面；本次未新增声音录制或听音验证。

## 开发与素材

构建所需的专有游戏输入与素材不在源码仓库中，详见 [构建说明](BUILDING.md)。素材出处及制作说明见 [致谢](CREDITS.md)。公开仓库不包含私人回放、账号数据或测试日志。

## English

Two elimination cinematics for Airexplosion's Yi Xian Pai mod loader. Tu Kui performs a Third Strike–inspired Raging Demon; Lin Xiaoyue performs a time-stop, knives and steamroller sequence. Both replace the entire qualifying turn, retain the actual stage, supported equipped costume and native damage text, and hold the ending until the battle is left, skipped or replayed.

Tu Kui must actually play Realm-Killing Palms or Carefree Shadowless Fist during the eliminating turn. Lin Xiaoyue needs an earlier completed card activation in the same turn to make at least two attacks **and** clear Guard Up, followed by Dharma Spirit Sword's direct killing attack. The defeated opponent must not be an inner demon and must lose their remaining Destiny. Extra Cat Shadow attacks count. Both characters use only the game's original costume and facial textures.

Version 0.1.2 targets Steam build `26939223`. It fixes built-in Recap elimination detection, keeps the native card-result task to avoid a missing Windows AOT specialization, and uses a preserved native IK timeline for Tu Kui without changing any of the 20 poses. Builds and packaging require a static API audit against the matching Windows player; the final DLL passes, and the checker rejects both known-broken versions plus stale-input and wrong-overload controls.

The exact DLL above passed Tu Kui’s 6.8-second replay, Lin’s 11.1-second replay, and an unchanged built-in Recap battle/context in the actual Windows IL2CPP player under Whisky Wine, using the SDK DLLs bundled with the official Windows loader v1.0.1. Native damage/Destiny results, held endings, replay cleanup and status-bar restoration passed; all private test processes exited normally and active play was preserved. Both replay runs captured Windows frames. Audio was not newly recorded or reviewed.

Import the release ZIP through the mod manager, enable it, then restart after finishing any active game. Do not enable it alongside an integrated mod that already contains these finishers.
