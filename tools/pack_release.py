#!/usr/bin/env python3
"""Package only the audited standalone plugin and manifest, with the official SDK.

This creates a local release ZIP. It never installs, launches, uploads or sends
anything. Source, native references, media archives and private state are excluded.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess
import zipfile


def sha(data): return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mod', type=Path, required=True)
    parser.add_argument('--sdk', type=Path, required=True)
    parser.add_argument('--audit', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    mod = args.mod.resolve(); out = args.out.resolve()
    expected = {'manifest.json', 'plugins/YxpDefeatFinishers.dll'}
    files = {str(p.relative_to(mod)).replace(os.sep, '/') for p in mod.rglob('*') if p.is_file()}
    if files != expected or any(p.is_symlink() for p in mod.rglob('*')):
        raise ValueError('MOD input must contain exactly manifest.json and the single plugin DLL, with no links.')
    manifest = json.loads((mod / 'manifest.json').read_text())
    if manifest['entry'] != 'plugins/YxpDefeatFinishers.dll' or manifest['entryType'] != 'YxpDefeatFinishers.FinisherMod':
        raise ValueError('Unexpected plugin entry')
    dll = (mod / manifest['entry']).read_bytes()
    audit = json.loads(args.audit.read_text())
    if audit.get('pass') is not True or audit.get('errors') or audit['sha256'] != sha(dll):
        raise ValueError('The completed DLL does not match its passing final runtime audit.')
    out.mkdir(parents=True, exist_ok=True)
    with (out / 'pack.log').open('w') as log:
        subprocess.run(['dotnet', str(args.sdk.resolve() / 'tools/yx-patch.dll'), 'pack', str(mod), '--out', str(out)],
                       env=dict(os.environ, DOTNET_ROLL_FORWARD='Major'), check=True,
                       stdout=log, stderr=subprocess.STDOUT)
    archive = out / (manifest['id'] + '-' + manifest['version'] + '.zip')
    entries = {manifest['id'] + '/' + name: (mod / name).read_bytes() for name in expected}
    with zipfile.ZipFile(archive) as package:
        if set(package.namelist()) != set(entries) or len(package.namelist()) != len(entries):
            raise ValueError('Unexpected release ZIP contents')
        for name, data in entries.items():
            if package.read(name) != data: raise ValueError('Packaged bytes differ: ' + name)
    receipt = dict(archive=archive.name, sha256=sha(archive.read_bytes()), bytes=archive.stat().st_size,
                   plugin_sha256=sha(dll), entries={name:sha(data) for name,data in sorted(entries.items())})
    (out / 'package-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps(receipt, indent=2))


if __name__ == '__main__': main()
