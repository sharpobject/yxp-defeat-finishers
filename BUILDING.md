# 构建说明 / Building

公开源码包含加载器入口、回调式播放流程、动作和时间表、素材编码器、构建检查及打包脚本。它不包含可直接重建游戏资源的完整输入。

## 所需环境

- Python 3.10+ 与 UnityPy、.NET SDK 9（编译器产生 C# 7.3 / 游戏框架兼容的插件）。
- [官方 SDK](https://github.com/Airexplosion/yixianpai-mod-sdk)，当前编译／checker 工具使用提交 `636a3d71cf91f67e164e221c0ee0a236ec1b9ae1`。运行验证另使用官方 Windows 加载器 v1.0.1 安装包中的 SDK DLL，不能用源码提交代替实际运行 DLL 的摘要。SDK 工具需要 .NET 8 或以上；构建脚本允许使用较新的运行时。
- 独立的 YXP 开发工作区，提供当前游戏原始引用、离线战斗计算器生成器及经检查的十一份素材。使用自己的合法游戏副本。上述输入不随本仓库分发。

目前生成器仍依赖工作区内的已有原生提取流程，不能仅凭这个公开源码仓库完成首次提取。`--workspace` 必须指向具备以下布局的开发工作区：

```text
runbooks/yxp/INDEX.md
reports/yxp/retail-language-systematic-20261031/source/
    target_config.py
    targets.json
    inputs/retail-<build>/
        provenance.json
        windows-native.json
        authored-inputs.json
        authored/catalog.json
        DarkSun.HotUpdate.dll
        managed/*.dll
reports/yxp/defeat-finishers-20261125/
    calculator/{prepare.py,Builder.cs,Builder.csproj}
    generated/runtime/*.cs
    assets/YxpFinishers/*
reports/yxp/retail-language-gamewide-20261031/refresh-review/card-descriptions/OwnedAccumulatedText.cs
```

计算器生成器还使用该工作区相邻的 `yisim/tools/game_oracle_builder` 作者工具。`generated/runtime` 是本机游戏生成的输入，不是可提交到公开仓库的源码。素材摘要在 `source/emit_media.py` 中声明；构建时必须逐一匹配。

```sh
python3 source/build.py --workspace "$YXP_WORKSPACE" --sdk "$YXP_SDK" \
  --bundles "$YXP_FINISHER_BUNDLES"
```

默认输入由工作区 `targets.json` 的 `retail` 项选择，构建会通过 `target_config.py` 校验原始输入及 HotUpdate 摘要。先用工作区维护的提取流程准备当前游戏，再运行此命令。v0.1.2 候选版本针对 Steam build `26939223`。最终 DLL 已通过 Windows 原生 API 静态检查，当前版本的实际运行验证见下文。

`YXP_SDK` 指向官方 SDK 仓库根目录，需含 `sdk/net40` 和 `tools/yx-patch.dll`。`YXP_FINISHER_BUNDLES` 指向与输入 catalog 匹配的完整原生 AssetBundle 目录；构建只读该目录，检查当前配置的全部九种外观与 Spine 字节摘要。仅有 HotUpdate/config 的精简提取目录不够。可用 `--game-input` 指定具有上述完整文件布局的另一个已提取游戏输入目录；不同游戏版本需要重新生成计算器并验证布局和动作绑定。

构建输出是 `mod/manifest.json` 和 `mod/plugins/YxpDefeatFinishers.dll`。音频和图片按原始编码嵌入插件，通过直接字节数组读取，不依赖运行时查找资源路径。构建只在私有目录中公开少量原生字段供编译器读取，最终插件仍直接访问原字段；该引用副本不会进入 MOD。

构建在复制最终插件前执行直接方法／字段引用检查、运行时反射禁用检查、Windows 原生 API 检查及官方 SDK checker。`build-proof/build.json` 记录选中的游戏输入、Steam build、原始输入 provenance、Windows 输入清单和两个 SDK DLL 的摘要。Windows 检查不执行游戏路径：它读取最终 DLL 的调用、字段和类型引用，与实际 Windows 播放器的声明及原生方法表比较，包括泛型实例。编译用的 Mac Mono 引用或 SDK checker 通过不能代替这项检查。

### Windows 原生检查输入

默认使用所选游戏输入目录内的 `windows-native.json`；可用 `--windows-native "$YXP_WINDOWS_NATIVE_MANIFEST"` 指定另一份清单。清单缺失、Steam build 不匹配或 HotUpdate 摘要与所选游戏不同都会阻止构建，不会退回上一版本的 Windows 输入。

清单记录以下字段。路径使用开发机上的绝对路径，全部输入保持私有，不随源码或 MOD 发布：

| 字段 | 内容 |
| --- | --- |
| `steam_build_id` | 与所选游戏 provenance 相同的 Steam build |
| `game_assembly` | 实际 Windows `GameAssembly.dll` 的 `path` 与 `sha256` |
| `global_metadata` | 同一 Windows 发行版 `global-metadata.dat` 的 `path` 与 `sha256` |
| `hot_update` | 同版原始 `DarkSun.HotUpdate.dll` 的 `path` 与 `sha256` |
| `dummy_dll_directory` | 从上述原生文件提取的完整声明 DLL 目录 |
| `dummy_dll_sha256` | 该目录每个 DLL 文件名及其 SHA-256 |
| `script` | 同次提取的完整 `script.json` 的 `path` 与 `sha256` |
| `provenance` | Steam depot/manifest、HotUpdate 提取和原生元数据提取的来源收据 |

先从合法取得的当前 Windows 游戏副本提取声明与原生方法表；保存工具版本、实际命令、源文件摘要及输出摘要。准备清单时核对官方 depot 的文件摘要，确保 `GameAssembly.dll`、元数据、HotUpdate 和提取结果来自同一版本。检查器重新核对清单中所有原生输入文件的字节，要求声明目录没有未绑定的 DLL，并记录实际解析到的 HotUpdate 与 SDK DLL 摘要。更新游戏后必须准备与新输入匹配的清单，不能只修改旧清单里的版本号。

`build-proof/runtime-audit.json` 中的 `windows_native.pass` 必须为 `true`，缺失声明、原生方法或泛型实例均为构建错误。这项静态检查负责 API 覆盖；画面、声音、演出时机与游戏行为仍需在相应客户端中验证。

## v0.1.2 的实现与验证

当前 DLL SHA-256 为 `30b06f86a170224ec83a0e971081b307f1ca950608dc45666a2f4a3aa8a02912`，大小 5,804,544 字节。正常构建的 Windows 静态检查已通过，错误数为零，覆盖 182 个普通原生方法、101 个具体泛型方法和 5 个经证明可共享引用类型代码的方法。类型／字段检查覆盖 124 个声明、34 个具体泛型类型、4 个共享引用类型和 1 个由原生签名证明的类型。接口／抽象方法的虚调用检查其保留的分派声明，不声称静态证明每个运行时实现或游戏行为。

相同检查器正确拒绝旧版缺失的 `UniTaskCompletionSource<int>` 类型及方法和 Spine 设置方法；过期输入摘要和错误重载的对照输入也被拒绝。验证不需要执行对应游戏路径。构建同时完成九种外观绑定检查、直接引用检查、运行时反射禁用检查和官方 SDK checker。

Recap 的已结算输入可能携带战前命元。计算器先校验胜者、回合数、血量差和随机数消耗，再调用原生命元扣除逻辑取得最终命元。播放保留 `CardActionBase.Execute` 的原生结果任务，只在其非泛型 `ExecuteEffect` 前等待演出；无需构造 Windows 没有 AOT 代码的额外泛型 completion source。屠馗的 156 次冗余 IK 写入被删除，四次必要膝部变化使用已保留的原生 `IkConstraintTimeline`；20 个姿势的骨骼和世界网格前后完全一致。

同一 DLL 已在实际 Windows IL2CPP 客户端（macOS 上的 Whisky Wine）通过屠馗回放、林小月回放和原始内置 Recap 战斗／上下文输入。演出时钟分别达到完整的 6.8、11.1 和 11.1 秒；原生 HP／命元结果、结束保持、重播清理和状态栏恢复全部通过。三次测试进程正常退出并保留正在运行的游戏。测试使用官方 Windows 加载器 v1.0.1 附带的 SDK DLL，插件及 SDK 字节保持不变，Recap 输入没有转换成普通战绩回放。

两套回放均取得 Windows 画面；Recap 运行记录功能状态。本次没有新增声音录制或听音验证。静态 API 检查、实际原生执行和画面结果分别记录，不用其中一项替代另一项。

## 打包

原生测试完成后，使用明确的构建收据打包：

```sh
python3 tools/pack_release.py --mod mod --sdk "$YXP_SDK" \
  --audit build-proof/runtime-audit.json --out dist
```

脚本只接受 manifest 和单一插件 DLL，先核对已审计 DLL 的摘要并要求 `windows_native.pass == true`，再调用官方 `yx-patch pack`（不跳过检查），最后确认 ZIP 恰好只有这两个文件。不会包含游戏引用、生成源码、私人回放、测试日志或素材源档。

The public repository contains authored implementation and preparation tools. Building still requires the documented private native-input/generator workspace and exact media inputs; this is not a self-contained game-source distribution. The build does not install or launch the game. Packaging creates a local ZIP and performs no upload.
