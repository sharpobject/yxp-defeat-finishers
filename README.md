# 鸭神与压路机

弈仙牌双角色淘汰演出 MOD，使用 [Airexplosion 的 MOD SDK](https://github.com/Airexplosion/yixianpai-mod-sdk)。只包含屠馗和林小月的两套演出。

v0.1.1 增加未结算练习对局的支持，并更新两位角色的动作。独立加载器版本已完成 Mac 原生回放验证。Windows IL2CPP 运行尚未验证。

## 触发条件

- **屠馗 · 瞬狱杀**：在击杀并淘汰对手的同一回合，实际打出过「百杀破境掌」或「逍遥无影拳」。最后一击可以来自该回合的另一张牌、追击或回合结束效果。演出参考《Street Fighter III: 3rd Strike》的 Raging Demon，由屠馗完成动作，背后和背景显示红色书法「鸭」。
- **林小月 · 时停压路机**：同一回合中，先前已完整打出的一张牌必须实际攻击至少两次，并在这一次出牌中清除对手至少一层「护体」；之后「万法归灵剑」的直接攻击击杀并淘汰对手。猫影追加攻击计入攻击次数。仅击败本轮、对手仍有命元时不会触发。

两套演出替换符合条件的整个回合动画，保留原战场、左右站位、原生伤害计算和伤害飘字。林小月先前的攻击对应飞刀命中，万法归灵剑对应压路机落地。结束姿势保持到离开、跳过或重新播放该场战斗；林小月脚下的压路机也会保留。

练习对局也必须满足上述出牌与淘汰条件。对于尚无胜负结果的练习输入，MOD 使用原生战斗和命元扣除规则计算是否淘汰；对手仍有命元或属于心魔时不会触发。普通战绩回放仍须与记录的胜者、回合数、血量差及随机数消耗一致。

支持当前已适配的屠馗两套服装及其换色、林小月三套服装及其换色，共九种外观。音量跟随游戏音效设置。新版本新增的外观需要重新检查骨骼与动作映射。

## v0.1.1 动作更新

- 屠馗的冲刺、抓取与背身结束动作重新编排；抓取手臂与手腕使用匹配的原生贴图，结束姿势保留完整的脚踝和靴子形状。
- 林小月在落地击打后先起身，再挺胸后仰、向后拉开双肘；双脚保持踩住压路机，长袖下垂，结束姿势持续呼吸起伏。
- 两位角色的服装与表情均使用游戏自带贴图。林小月保留原生侧脸表情，没有另绘喊叫面部。

## 安装

1. 安装与当前游戏版本相容的 Airexplosion MOD 加载器／管理器。
2. 从本项目的 Releases 下载 MOD ZIP，在管理器中导入并启用。
3. 完成正在进行的对局后，完整退出并重新启动游戏，使插件生效。

卸载时在管理器中禁用或移除本 MOD，然后重新启动游戏。不要将本包与另一个已经包含这两套演出的整合 MOD 同时启用。

本仓库不提供游戏、加载器安装程序或游戏引用 DLL。加载器的 Windows IL2CPP 兼容性仍需在对应平台验证；Mac 原生测试不能替代 Windows 测试。

当前检查所用游戏输入：Steam build `26776454`，客户端／服务器比较版本 `001.0008.0007`，资源版本 `1.8.0`；SDK 提交 `636a3d71cf91f67e164e221c0ee0a236ec1b9ae1`。其他版本需要重新验证，manifest 暂不声明未经确认的版本范围。

## 验证与已知差异

- 原始客户端热更程序集、官方 SDK 与独立插件 DLL 已在 Mac 原生客户端中完成屠馗和林小月的完整回放检查，包括原场景、实际伤害、结束姿势和重播清理。同一 DLL 还通过了林小月的合成练习输入检查：从已保存战斗构造未结算练习对局，执行原生战斗；这项检查不包含练习场 MOD 的完整界面流程。
- 上述测试使用的插件 DLL SHA-256 为 `c9143dcf7d160ba95d55abc04f7440a6484f6cfda26bd83611ef5888f9e2238b`。
- 构建检查当前全部九种服装／换色的骨骼资源；整合版与独立版离线计算器各通过 70/70 项检查，包括练习输入、非淘汰情形及输入数据不变性。插件通过最终直接引用／无运行时反射检查及官方 SDK checker。
- 林小月的致命伤害仍发生在压路机阶段，保留原生出牌和被动效果顺序。
- 以上原生运行证据来自 Mac Mono/ILRuntime，尚未完成 Windows IL2CPP 测试。

## 开发与素材

构建所需的专有游戏输入与素材不在源码仓库中，详见 [构建说明](BUILDING.md)。素材出处及制作说明见 [致谢](CREDITS.md)。公开仓库不包含私人回放、账号数据或测试日志。

## English

Two elimination cinematics for Airexplosion's Yi Xian Pai mod loader: Tu Kui performs a Third Strike–inspired Raging Demon; Lin Xiaoyue performs a time-stop, knives and steamroller sequence.

Tu Kui must actually play Realm-Killing Palms or Carefree Shadowless Fist during the eliminating turn. Lin Xiaoyue requires an earlier completed card activation in the same turn to make at least two attacks **and** clear Guard Up, followed by Dharma Spirit Sword's direct killing attack and match elimination. Both retain the actual stage, equipped supported costume/recolor and native damage text.

Version 0.1.1 also accepts unfinished practice battles, using native combat and Destiny-loss rules to determine elimination. Inner demons and opponents with remaining Destiny do not qualify. Completed replay records retain their winner, turn-count, HP-difference and random-consumption checks.

Tu Kui has revised charge, grip and rear poses with matching native arm/hand views and intact boot shapes. Lin Xiaoyue rises into a backward chest arch with elbows drawn behind her, feet planted on the roller, hanging sleeves and a breathing hold. Both characters use only the game's own costume and facial textures; Lin retains her original restrained profile expression.

Import the release ZIP through the mod manager, enable it, then restart after finishing any active game. The exact DLL identified above passed native Mac replays for both cinematics and a synthetic practice battle for Lin using the real SDK. Both calculator variants passed 70/70 checks. Windows IL2CPP remains untested. Lin's lethal hit stays within the roller phase with native card/passive ordering preserved.
