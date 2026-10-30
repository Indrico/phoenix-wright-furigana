"""Build a Windows patch ZIP without distributing game assemblies or dialogue."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

REPO = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game-dir', type=Path, required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output-dir', type=Path, default=REPO/'dist')
    parser.add_argument('--cecil-path', type=Path, default=Path.home()/'.nuget/packages/mono.cecil/0.11.6/lib/net40/Mono.Cecil.dll')
    args = parser.parse_args()
    if not args.version or any(c not in '0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ.-' for c in args.version):
        parser.error('Invalid version')
    game = args.game_dir.resolve()
    evidence = game/'furigana-mod/evidence'
    patch = json.loads((evidence/'patch.json').read_text())
    readings = json.loads((evidence/'readings-build.json').read_text())
    original = game/'furigana-mod/backups/Assembly-CSharp.dll'
    updated = game/'PWAAT_Data/Managed/Assembly-CSharp.dll'
    runtime = game/'PWAAT_Data/Managed/PwaatFurigana.dll'
    data = game/'PWAAT_Data/StreamingAssets/Furigana/readings.tsv'
    lexicon = game/'PWAAT_Data/StreamingAssets/Furigana/lexicon.tsv'
    for file, expected in [(original,patch['original_sha256']), (updated,patch['patched_sha256']), (runtime,patch['runtime_sha256']), (data,readings['readings_sha256']), (lexicon,readings['lexicon_sha256'])]:
        if digest(file) != expected:
            raise ValueError(f'Input hash differs from verified build: {file}')
    package = args.output_dir/'staging'/'furigana-mod'
    if package.exists():
        shutil.rmtree(package)
    payload = package/'payload'
    payload.mkdir(parents=True)
    shutil.copyfile(args.cecil_path, payload/'Mono.Cecil.dll')
    shutil.copyfile(runtime, payload/'PwaatFurigana.dll')
    shutil.copyfile(lexicon, payload/'lexicon.tsv')
    hashes = []
    for row in data.read_text(encoding='utf-8').splitlines():
        text, spans = row.split('\t')
        hashes.append(hashlib.sha256(text.encode()).hexdigest()+'\t'+spans+'\n')
    (payload/'readings-hashes.tsv.gz').write_bytes(gzip.compress(''.join(sorted(hashes)).encode(), mtime=0))
    manifest = dict(version=args.version, original_sha256=patch['original_sha256'], patched_sha256=patch['patched_sha256'],
                    runtime_sha256=patch['runtime_sha256'], readings_sha256=readings['readings_sha256'], lexicon_sha256=readings['lexicon_sha256'], sources=readings['sources'],
                    previous_runtime_sha256=['d77f282bec348beafb9d8a9a5286817a93c4d05979d4ccb919d042eb637e7080'],
                    previous_readings_sha256=['9717d9e307acaa4049c3ea1ee426210647b90ecd43195d788a019b6c50271f59'],
                    payloads=[dict(path='payload/'+p.name,sha256=digest(p)) for p in sorted(payload.iterdir())])
    for name in ['Patch.ps1','GamePatch.cs','CecilPatch.cs','Install.cmd','Uninstall.cmd']:
        src = REPO/'release'/name
        if src.suffix == '.cmd':
            (package/name).write_bytes(src.read_bytes().replace(b'\r\n',b'\n').replace(b'\n',b'\r\n'))
        else:
            shutil.copyfile(src, package/name)
    (package/'release-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    shutil.copytree(REPO/'furigana-mod/licenses', package/'licenses')
    shutil.copyfile(REPO/'README.md', package/'README.md')
    archive = args.output_dir/f'phoenix-wright-furigana-{args.version}-windows.zip'
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for p in sorted(package.rglob('*')):
            if p.is_file():
                entry = zipfile.ZipInfo(str(p.relative_to(package.parent)), date_time=(2026,1,1,0,0,0))
                entry.create_system = 3
                entry.external_attr = 0o100644 << 16
                z.writestr(entry,p.read_bytes(),compress_type=zipfile.ZIP_DEFLATED,compresslevel=9)
    (args.output_dir/'SHA256SUMS.txt').write_text(digest(archive)+'  '+archive.name+'\n')
    print(json.dumps(dict(archive=str(archive),bytes=archive.stat().st_size,sha256=digest(archive),reading_entries=len(hashes)),indent=2))


if __name__ == '__main__':
    main()
