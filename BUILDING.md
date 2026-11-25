# 构建说明 / Building

公开源码包含加载器入口、回调式播放流程、动作和时间表、素材编码器、构建检查及打包脚本。它不包含可直接重建游戏资源的完整输入。

## 所需环境

- Python 3.10+ 与 UnityPy、.NET SDK 9（编译器产生 C# 7.3 / 游戏框架兼容的插件）。
- [官方 SDK](https://github.com/Airexplosion/yixianpai-mod-sdk)，本版本检查的提交为 `636a3d71cf91f67e164e221c0ee0a236ec1b9ae1`。SDK 工具需要 .NET 8 或以上；构建脚本允许使用较新的运行时。
- 独立的 YXP 开发工作区，提供当前游戏原始引用、离线战斗计算器生成器及经检查的十一份素材。使用自己的合法游戏副本。上述输入不随本仓库分发。

目前生成器仍依赖工作区内的已有原生提取流程，不能仅凭这个公开源码仓库完成首次提取。`--workspace` 必须指向具备以下布局的开发工作区：

```text
runbooks/yxp/INDEX.md
reports/yxp/retail-language-systematic-20261031/source/inputs/retail-26776454/
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

`YXP_SDK` 指向官方 SDK 仓库根目录，需含 `sdk/net40` 和 `tools/yx-patch.dll`。`YXP_FINISHER_BUNDLES` 指向与输入 catalog 匹配的完整原生 AssetBundle 目录；构建只读该目录，检查当前配置的全部九种外观与 Spine 字节摘要。仅有 HotUpdate/config 的精简提取目录不够。可用 `--game-input` 指定具有相同文件布局的另一个游戏输入目录；不同游戏版本需要重新验证布局和动作绑定。

构建输出是 `mod/manifest.json` 和 `mod/plugins/YxpDefeatFinishers.dll`。音频和图片按原始编码嵌入插件，通过直接字节数组读取，不依赖运行时查找资源路径。构建只在私有目录中公开少量原生字段供编译器读取，最终插件仍直接访问原字段；该引用副本不会进入 MOD。

构建在复制最终插件前执行所有直接方法／字段引用检查、运行时反射禁用检查，以及官方 SDK checker。保留的 AOT 泛型警告必须结合目标客户端的实际绑定和原生运行结果审查；SDK checker 通过不代表已在 Windows IL2CPP 上运行通过。

## 打包

原生测试完成后，使用明确的构建收据打包：

```sh
python3 tools/pack_release.py --mod mod --sdk "$YXP_SDK" \
  --audit build-proof/runtime-audit.json --out dist
```

脚本只接受 manifest 和单一插件 DLL，先核对已审计 DLL 的摘要，再调用官方 `yx-patch pack`（不跳过检查），最后确认 ZIP 恰好只有这两个文件。不会包含游戏引用、生成源码、私人回放、测试日志或素材源档。

The public repository contains authored implementation and preparation tools. Building still requires the documented private native-input/generator workspace and exact media inputs; this is not a self-contained game-source distribution. The build does not install or launch the game. Packaging creates a local ZIP and performs no upload.
