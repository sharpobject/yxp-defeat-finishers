# 鸭神与压路机

弈仙牌双角色淘汰演出 MOD，使用 [Airexplosion 的 MOD SDK](https://github.com/Airexplosion/yixianpai-mod-sdk)。屠馗完成《Street Fighter III: 3rd Strike》风格的瞬狱杀；林小月完成时停、飞刀与压路机演出。

v0.1.3 针对 Steam build `26939223`，改进屠馗滑行护架、林小月时停与飞刀动作，以及结束姿势的原生手型。

## 触发条件

- **屠馗 · 瞬狱杀**：在击杀并淘汰对手的同一回合，实际打出过「百杀破境掌」或「逍遥无影拳」。最后一击可以来自该回合的另一张牌、追击或回合结束效果。背后和背景显示红色书法「鸭」。
- **林小月 · 时停压路机**：同一回合中，先前已完整打出的一张牌实际攻击至少两次，并在这一次出牌中清除对手至少一层「护体」；之后「万法归灵剑」的直接攻击击杀并淘汰对手。猫影追加攻击计入攻击次数。

淘汰指对手不是心魔，并且因本次战败降至零或负命元。仅击败本轮但对手仍有命元时不会触发。普通回放、内置复盘和未结算练习输入都需要满足对应的出牌与淘汰条件。

两套演出替换符合条件的整个回合动画，保留原战场、左右站位、原生伤害计算和伤害飘字。林小月先前的攻击对应飞刀命中，万法归灵剑对应压路机落地。结束姿势及压路机保持到离开、跳过或重新播放该场战斗。

支持屠馗两套服装及其换色、林小月三套服装及其换色，共九种外观。人物与表情仅使用游戏自带贴图；林小月没有另绘喊叫面部。音量跟随游戏音效设置。新增外观需要另行检查骨骼与动作映射。

## v0.1.3 更新

- 屠馗滑行时使用各服装原有的闭合拳头与对应前臂，降低高举的护架，让拳头和肘部轮廓更清楚；已经修好的腿、靴子和护膝保持不变。
- 林小月时停和准备动作收紧肘部并握拳。飞刀出手后更早进入后仰姿势，并稍延长该姿势的停留，让甩臂轮廓更容易看清。
- 林小月结束姿势使用原生弯曲手指／猫爪及其配套手掌层，不新增人物或表情贴图。
- 保留 v0.1.2 的 Windows、内置 Recap 和练习输入修复，以及构建时的同版本 Windows 原生 API 检查。触发条件、伤害时间点和声音时间线不变。

## 安装

1. 安装与当前游戏版本相容的 Airexplosion MOD 加载器／管理器。
2. 从本项目的 Releases 下载 MOD ZIP，在管理器中导入并启用。
3. 完成正在进行的对局后，完整退出并重新启动游戏。

卸载时在管理器中禁用或移除本 MOD，然后重新启动游戏。不要与另一个已包含这两套演出的整合 MOD 同时启用。本仓库不提供游戏、加载器安装程序或游戏引用 DLL。

## 验证

当前 0.1.3 插件 SHA-256：`71f911d6cfb7cf0b4737b5b7527b0cac32187c3b7f50b46f3dcbfe323ab33428`（5,526,016 字节）。最终 DLL 通过当前 Windows 原生 API、直接引用及运行时反射禁用检查；Windows 静态检查错误数为零。官方 SDK checker 报告 0 个错误、932 个警告。

相同动作源码已在原生 Mac 整合客户端完成五次实际回放，覆盖林小月三种和屠馗两种身体模型。完整演出、伤害／命元、结束保持、重播清理和状态栏恢复通过，五个测试进程均正常退出。其余四种换色经过本次原生 Spine 样张与几何检查，不计为额外游戏运行。

本次没有执行新 DLL 的 Windows 播放测试，也没有新增实际声音录制。旧版 Windows 实际运行记录仍归属于 0.1.2；静态 API 检查不代替画面或声音验收。

## 开发与素材

构建所需的专有游戏输入与素材不在源码仓库中，详见 [构建说明](BUILDING.md)。素材出处及制作说明见 [致谢](CREDITS.md)。公开仓库不包含私人回放、账号数据或测试日志。

## English

Two elimination cinematics for Airexplosion's Yi Xian Pai mod loader. Tu Kui performs a Third Strike–inspired Raging Demon; Lin Xiaoyue performs a time-stop, knives and steamroller sequence. Both replace the entire qualifying turn, retain the actual stage, supported equipped costume and native damage text, and hold the ending until the battle is left, skipped or replayed.

Tu Kui must actually play Realm-Killing Palms or Carefree Shadowless Fist during the eliminating turn. Lin Xiaoyue needs an earlier completed card activation in the same turn to make at least two attacks **and** clear Guard Up, followed by Dharma Spirit Sword's direct killing attack. The defeated opponent must not be an inner demon and must lose their remaining Destiny. Extra Cat Shadow attacks count. Both characters use only the game's original costume and facial textures.

Version 0.1.3 targets Steam build `26939223`. Tu Kui's glide uses a clearer closed-fist guard with matching original forearm views; his repaired legs, boots and kneepads are retained. Lin Xiaoyue bends her arms and closes her hands during time stop, reaches a stronger backward knife follow-through earlier, holds it longer, and uses original curled hands or paws for her ending pose. Trigger rules, damage cues and audio timing remain unchanged.

The release retains the Windows, Recap and practice-input fixes and mandatory same-version Windows native API audit. The final DLL above passes the Windows native API, direct-reference and no-runtime-reflection checks. The official SDK checker reports 0 errors and 932 warnings. The shared pose sources were reviewed in five actual integrated Mac replay runs covering all five body models, with full movies, damage/Destiny, held endings and replay cleanup; all test processes exited normally. The four recolors have fresh offline native Spine review. This new DLL has not been playback-tested on Windows, and audible output was not newly captured; previous Windows evidence remains attached to v0.1.2.

Import the release ZIP through the mod manager, enable it, then restart after finishing any active game. Do not enable it alongside an integrated mod that already contains these finishers.
