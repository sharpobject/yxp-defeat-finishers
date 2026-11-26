#!/usr/bin/env python3
"""Build the two-finishers loader plugin. Never install or launch the game.

Only the completed, audited plugin and explicit manifest reach mod/. Native
game references, generated calculator/source/media and logs remain private.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess

from emit_media import emit
from prepare_visuals import prepare as prepare_visuals
from prepare_calculator import prepare as prepare_calculator

HERE = Path(__file__).resolve().parent
REPORT = HERE.parent
YXP = REPORT.parent
OLD = YXP / 'defeat-finishers-20261125'
RETAIL = YXP / 'retail-language-systematic-20261031/source'


def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def run(command, log):
    with log.open('w') as stream:
        result = subprocess.run([str(x) for x in command], cwd=REPORT, env=dict(os.environ, DOTNET_ROLL_FORWARD='Major'),
                                stdout=stream, stderr=subprocess.STDOUT)
    if result.returncode:
        print(log.read_text()[-16000:])
        raise RuntimeError('Build operation failed; details: ' + str(log))


def main():
    global YXP, OLD, RETAIL
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', type=Path, default=Path(os.environ.get('YXP_WORKSPACE', str(HERE.parents[3]))))
    parser.add_argument('--game-input', type=Path)
    parser.add_argument('--bundles', type=Path, default=Path(os.environ['YXP_FINISHER_BUNDLES']) if 'YXP_FINISHER_BUNDLES' in os.environ else None,
                        help='Complete native bundle directory for the prepared catalog; read only')
    parser.add_argument('--sdk', type=Path, default=REPORT / 'sdk')
    parser.add_argument('--windows-native', type=Path,
                        help='Verified Windows metadata manifest; defaults to windows-native.json in the selected game input')
    args = parser.parse_args()
    args.sdk = args.sdk.resolve()
    workspace = args.workspace.resolve()
    if not (workspace / 'runbooks/yxp/INDEX.md').is_file():
        raise ValueError('Provide --workspace or YXP_WORKSPACE for the documented native-input/generator workspace.')
    YXP = workspace / 'reports/yxp'; OLD = YXP / 'defeat-finishers-20261125'
    RETAIL = YXP / 'retail-language-systematic-20261031/source'
    if args.game_input is None:
        spec = importlib.util.spec_from_file_location('finisher_native_target', RETAIL / 'target_config.py')
        target_config = importlib.util.module_from_spec(spec); spec.loader.exec_module(target_config)
        target = target_config.verify_inputs('retail')
        target_config.retained_hot_input(target)
        game = (RETAIL / target['input_root']).resolve()
    else:
        game = args.game_input.resolve()
    provenance = json.loads((game / 'provenance.json').read_text())
    managed = game / 'managed'; hot = game / 'DarkSun.HotUpdate.dll'
    windows_manifest = (args.windows_native or game / 'windows-native.json').resolve()
    windows = json.loads(windows_manifest.read_text())
    if str(windows['steam_build_id']) != str(provenance['steam_build_id']):
        raise ValueError('Windows native inputs belong to a different Steam build')
    if windows['hot_update']['sha256'] != digest(hot):
        raise ValueError('Windows HotUpdate differs from the selected combat and playback declarations')
    build = REPORT / '.build'; generated = REPORT / 'generated'; proof = REPORT / 'build-proof'
    for directory in (build, generated, proof, build / 'refs', build / 'candidate/plugins'):
        directory.mkdir(parents=True, exist_ok=True)
    authored_inputs = json.loads((game / 'authored-inputs.json').read_text())
    catalog_row = authored_inputs['catalog']; catalog = game / catalog_row['staged_path']
    if digest(catalog) != catalog_row['sha256'] or catalog.stat().st_size != catalog_row['size']:
        raise ValueError('Prepared native catalog differs from authored-inputs.json')
    bundles = (args.bundles or game / 'bundle/StandaloneOSX').resolve()
    for row in authored_inputs['files']:
        if row['role'] == 'config':
            actual = bundles / Path(row['staged_path']).name
            if digest(actual) != row['sha256']:
                raise ValueError('Native configuration bundle differs from prepared declarations')
    validator = HERE / 'finisher_assets.py'
    if not validator.is_file(): validator = RETAIL / 'finisher_assets.py'
    spec = importlib.util.spec_from_file_location('finisher_rig_validator', validator)
    rig_module = importlib.util.module_from_spec(spec); spec.loader.exec_module(rig_module)
    rigs = rig_module.validate_finisher_rigs(catalog, bundles)
    rig_receipt = dict(catalog_sha256=digest(catalog), validator_sha256=digest(validator), rigs=rigs,
                       bundle_sha256={row['bundle']:digest(row['bundle']) for row in rigs})
    (proof / 'authored-rigs.json').write_text(json.dumps(rig_receipt, indent=2) + '\n')
    # Reuse the current source-bound native calculator generator. It checks all
    # input/output hashes before admitting its cached detached calculation.
    spec = importlib.util.spec_from_file_location('native_calculator_prepare', OLD / 'calculator/prepare.py')
    native = importlib.util.module_from_spec(spec); spec.loader.exec_module(native)
    native.prepare(hot, managed)
    calc = prepare_calculator(OLD / 'generated/runtime', generated / 'calculator')
    media = emit(OLD / 'assets/YxpFinishers', generated / 'FinisherMedia.g.cs')
    def authored(name):
        local = HERE / name
        return local if local.is_file() else RETAIL / 'patcher' / name
    visual = prepare_visuals(authored('DefeatFinisherVisualsPayload.cs'), generated / 'DefeatFinisherVisuals.cs')
    (proof / 'preparation.json').write_text(json.dumps(dict(calculator=calc, media=media, visual=visual), indent=2) + '\n')
    tool_project = HERE / 'build-tools/BuildTools.csproj'
    run(['dotnet', 'build', tool_project, '-v:q', '--nologo', '-p:GameManaged=' + str(managed)], proof / 'tools-build.log')
    tool = HERE / 'build-tools/bin/Debug/net9.0/BuildTools.dll'
    public_hot = build / 'refs/DarkSun.HotUpdate.dll'
    run(['dotnet', tool, 'reference', hot, public_hot, proof / 'reference.json'], proof / 'reference.log')
    run(['dotnet', 'run', '--project', REPORT / 'delay-audit/Audit.csproj', '-p:GameManaged=' + str(managed),
         '-p:YxSdkDir=' + str(args.sdk.resolve() / 'sdk/net40'), '--', hot,
         REPORT / 'delay-audit/census.json', 'generate-only'], proof / 'delay-generation.log')
    sources = [HERE / name for name in ('FinisherMod.cs', 'DefeatFinisherPayload.cs', 'Playback.cs', 'FinisherMediaAudio.cs')]
    calculator_runtime = HERE / 'CalculatorRuntime.cs'
    if not calculator_runtime.is_file(): calculator_runtime = OLD / 'calculator/CalculatorRuntime.cs'
    sources += [generated / 'DefeatFinisherVisuals.cs', generated / 'FinisherMedia.g.cs', generated / 'FinisherDelays.cs', calculator_runtime]
    sources += sorted((generated / 'calculator').glob('*.cs'))
    cues = HERE / 'DefeatFinisherCues.cs'
    if not cues.is_file(): cues = OLD / 'generated/presentation/DefeatFinisherCues.cs'
    sources += [authored('DefeatFinisherPosesPayload.cs'), authored('DefeatFinisherTuPosesPayload.cs'), cues]
    refs = [managed / (name + '.dll') for name in (
        'mscorlib', 'System', 'System.Core', 'netstandard', 'DarkSun.Utility', 'DarkSun.UI', 'wProtobuf',
        'UniTask', 'LucidSight.Runtime.ColyseusSDK', 'UnityEngine', 'UnityEngine.CoreModule', 'UnityEngine.AudioModule',
        'UnityEngine.ImageConversionModule', 'UnityEngine.InputLegacyModule', 'UnityEngine.UIModule',
        'UnityEngine.UI', 'Unity.TextMeshPro', 'Unity.Addressables', 'Unity.ResourceManager',
        'spine-csharp', 'spine-unity', 'Cinemachine', 'DOTween')]
    refs += [public_hot, args.sdk / 'sdk/net40/Yx.ModSdk.Core.dll', args.sdk / 'sdk/net40/Yx.ModSdk.dll']
    for path in sources + refs:
        if not path.is_file(): raise FileNotFoundError(path)
    candidate = build / 'candidate/plugins/YxpDefeatFinishers.dll'
    shutil.copyfile(HERE / 'manifest.json', build / 'candidate/manifest.json')
    csc_args = ['-nologo', '-nostdlib+', '-target:library', '-langversion:7.3', '-optimize+', '-deterministic+',
                '-debug-', '-nowarn:0169,0219,0649,0162', '-out:' + str(candidate)]
    csc_args += ['-r:' + str(path) for path in refs] + [str(path) for path in sources]
    response = build / 'compile.rsp'; response.write_text('\n'.join('"' + value + '"' for value in csc_args) + '\n')
    sdk_list = subprocess.check_output(['dotnet', '--list-sdks'], text=True).strip().splitlines()
    version, directory = sdk_list[-1].split(' ', 1)
    csc = Path(directory.strip('[]')) / version / 'Roslyn/bincore/csc.dll'
    run(['dotnet', csc, '@' + str(response)], proof / 'compile.log')
    # The original reference directory is intentionally used here: direct
    # private-field reads must resolve against the untouched native assembly.
    run(['dotnet', tool, 'audit', candidate, proof / 'runtime-audit.json', game, managed,
         args.sdk / 'sdk/net40', '--windows-native', windows_manifest], proof / 'runtime-audit.log')
    run(['dotnet', args.sdk / 'tools/yx-patch.dll', 'check', '--json', candidate], proof / 'sdk-check.json')
    checked = json.loads((proof / 'sdk-check.json').read_text())
    findings = [item for entry in checked['files'] for item in entry['findings']]
    if any(item['level'] == 'error' for item in findings): raise RuntimeError('SDK reported errors')
    plugin = REPORT / 'mod/plugins/YxpDefeatFinishers.dll'; plugin.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(candidate, plugin)
    shutil.copyfile(HERE / 'manifest.json', REPORT / 'mod/manifest.json')
    receipt = dict(scope='Offline standalone plugin build; no game installed or launched', dll=str(plugin),
                   sha256=digest(plugin), bytes=plugin.stat().st_size, native_hot_sha256=digest(hot),
                   native_game_input=str(game), steam_build_id=provenance['steam_build_id'],
                   native_provenance_sha256=digest(game / 'provenance.json'),
                   windows_native_manifest_sha256=digest(windows_manifest),
                   sdk_sha256={name:digest(args.sdk / 'sdk/net40' / name) for name in ('Yx.ModSdk.Core.dll', 'Yx.ModSdk.dll')},
                   authored_rigs_receipt_sha256=digest(proof / 'authored-rigs.json'),
                   source_sha256={str(path):digest(path) for path in sources},
                   sdk_errors=0, sdk_warnings=sum(item['level']=='warning' for item in findings))
    (proof / 'build.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps({k:v for k,v in receipt.items() if k != 'source_sha256'}, indent=2))


if __name__ == '__main__': main()
