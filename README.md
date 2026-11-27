# 鸭神与压路机

弈仙牌双角色淘汰演出 MOD，使用 [Airexplosion 的 MOD SDK](https://github.com/Airexplosion/yixianpai-mod-sdk)。屠馗完成《Street Fighter III: 3rd Strike》风格的瞬狱杀；林小月完成时停、飞刀与压路机演出。

v0.1.4 针对 Steam build `26965567`，修复屠馗火焰中残留的人形与脚部空洞，并更新屠馗和林小月的声音混音。

## 触发条件

- **屠馗 · 瞬狱杀**：在击杀并淘汰对手的同一回合，实际打出过「百杀破境掌」或「逍遥无影拳」。最后一击可以来自该回合的另一张牌、追击或回合结束效果。背后和背景显示红色书法「鸭」。
- **林小月 · 时停压路机**：同一回合中，先前已完整打出的一张牌实际攻击至少两次，并在这一次出牌中清除对手至少一层「护体」；之后「万法归灵剑」的直接攻击击杀并淘汰对手。猫影追加攻击计入攻击次数。

淘汰指对手不是心魔，并且因本次战败降至零或负命元。仅击败本轮但对手仍有命元时不会触发。普通回放、内置复盘和未结算练习输入都需要满足对应的出牌与淘汰条件。

两套演出替换符合条件的整个回合动画，保留原战场、左右站位、原生伤害计算和伤害飘字。林小月先前的攻击对应飞刀命中，万法归灵剑对应压路机落地。结束姿势及压路机保持到离开、跳过或重新播放该场战斗。

支持屠馗两套服装及其换色、林小月三套服装及其换色，共九种外观。人物与表情仅使用游戏自带贴图；林小月没有另绘喊叫面部。音量跟随游戏音效设置。新增外观需要另行检查骨骼与动作映射。

## v0.1.4 更新

- 修复屠馗火焰全部 30 帧中原豪鬼身体与脚部留下的空洞。每帧仍为原生 290 × 265 像素，仅使用原有颜色；修复区域以外的像素保持不变。
- 根据街机参考调整两套声音的相对音量，包括屠馗启动、林小月时停、飞刀与命中混音。音轨长度仍分别为 6.8 秒和 11.1 秒，保持现有演出时间表。
- 保留 v0.1.3 的角色动作、原生手型、触发条件、伤害时间点、Windows／内置 Recap／练习输入支持。人物与表情贴图没有更换。

## 安装

1. 安装与当前游戏版本相容的 Airexplosion MOD 加载器／管理器。
2. 从本项目的 Releases 下载 MOD ZIP，在管理器中导入并启用。
3. 完成正在进行的对局后，完整退出并重新启动游戏。

卸载时在管理器中禁用或移除本 MOD，然后重新启动游戏。不要与另一个已包含这两套演出的整合 MOD 同时启用。本仓库不提供游戏、加载器安装程序或游戏引用 DLL。

## 验证

修复后的 30 帧火焰已逐帧及按相邻帧顺序检查；原生尺寸、原有调色板和修复区域以外的像素逐一核对通过。两个音轨的采样格式与长度仍与播放器声明一致，其余八份素材与 v0.1.3 相同。最终 DLL 内十一项命名素材工厂的字节与已审阅 PNG/WAV 逐字节相同。

v0.1.4 插件 SHA-256：`7a98b87286fdafe82b7551e6e8c25d888d3dd8d181b7b5e3e61615fe4e69d1cd`（5,599,232 字节）。最终 DLL 已通过 Steam build `26965567` 的 Windows 原生 API、直接引用及运行时反射禁用检查；官方 SDK checker 报告 0 个错误、932 个警告。发布 ZIP SHA-256：`865a0608438613e1816f40017c1abe324dd92517d411a2b5e5cb32c44b7ae029`（4,569,221 字节）。

上述同一 v0.1.4 DLL 已在 Steam build `26965567` 的实际 Windows IL2CPP 游戏中，使用 macOS Whisky Wine 完成屠馗、林小月和内置 Recap 三项离线运行。完整演出、原生伤害／命元、结束保持、重播清理、状态栏恢复及 SDK 停用均通过，三个测试进程全部退出，保留正在运行的玩家游戏。实际取样画面覆盖屠馗热血服装和林小月睡衣服装，检查了修复火焰、压路机及结束姿势；不代表本次逐一运行了全部外观。

测试使用真实 Windows 可执行文件，但宿主是 Wine，并非 Windows 系统。测试启动器从保存的回放／Recap 数据调用原生战斗接口，不是登录账号或加载器安装界面的端到端测试。已检查 AudioSource 播放时钟及嵌入的音频字节；测试关闭了监听器输出，因此没有新增实际声音录制或听感验收。

## 开发与素材

构建所需的专有游戏输入与素材不在源码仓库中，详见 [构建说明](BUILDING.md)。素材出处及制作说明见 [致谢](CREDITS.md)。公开仓库不包含私人回放、账号数据或测试日志。

## English

Two elimination cinematics for Airexplosion's Yi Xian Pai mod loader. Tu Kui performs a Third Strike–inspired Raging Demon; Lin Xiaoyue performs a time-stop, knives and steamroller sequence. Both replace the entire qualifying turn, retain the actual stage, supported equipped costume and native damage text, and hold the ending until the battle is left, skipped or replayed.

Tu Kui must actually play Realm-Killing Palms or Carefree Shadowless Fist during the eliminating turn. Lin Xiaoyue needs an earlier completed card activation in the same turn to make at least two attacks **and** clear Guard Up, followed by Dharma Spirit Sword's direct killing attack. The defeated opponent must not be an inner demon and must lose their remaining Destiny. Extra Cat Shadow attacks count. Both characters use only the game's original costume and facial textures.

Version 0.1.4 targets Steam build `26965567`. It repairs the original fighter-shaped and foot-shaped gaps across all 30 Tu Kui flame frames, retaining the native 290 × 265 cell size, original colors and every pixel outside the repair region. It also updates both sound mixes while keeping the 6.8-second Tu Kui and 11.1-second Lin Xiaoyue timelines. The v0.1.3 poses, native hands, trigger rules, damage cues, Windows compatibility, Recap and practice support are retained. Character and facial textures are unchanged.

All 30 repaired frames and their ordered transitions have been reviewed, and the native dimensions, palette and protected pixels verified. Both audio files still match the declared PCM format and duration; the other eight media files are unchanged. All eleven named media factories in the final DLL contain the exact reviewed PNG/WAV bytes. The final v0.1.4 DLL is SHA-256 `7a98b87286fdafe82b7551e6e8c25d888d3dd8d181b7b5e3e61615fe4e69d1cd` (5,599,232 bytes). It passes the current Windows native API, direct-reference and no-runtime-reflection audits; the official SDK checker reports 0 errors and 932 warnings. The release ZIP is SHA-256 `865a0608438613e1816f40017c1abe324dd92517d411a2b5e5cb32c44b7ae029` (4,569,221 bytes).

This exact v0.1.4 DLL passed three offline runs in the actual Windows IL2CPP game, Steam build `26965567`, under Whisky Wine on macOS: Tu Kui, Lin Xiaoyue and Recap. Full movies, native damage/Destiny, held endings, replay cleanup, status-bar restoration and SDK disable passed; all test processes exited while preserving active player games. Reviewed native samples cover Tu Kui’s Hot Blood costume and Lin Xiaoyue’s hooded costume, including the repaired flame and roller ending. This is Windows-executable testing under Wine, not a Windows OS host or every-costume run. The private startup invokes native battle APIs with saved replay/Recap data, rather than testing account login or the loader installer UI. AudioSource clocks and exact embedded audio bytes were checked; the harness muted output, so no new audible recording or listening acceptance is claimed.

Import the release ZIP through the mod manager, enable it, then restart after finishing any active game. Do not enable it alongside an integrated mod that already contains these finishers.
