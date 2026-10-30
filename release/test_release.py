"""Exercise the actual packaged PowerShell installer on disposable game files."""
import argparse
import hashlib
import json
import platform
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game-dir', type=Path, required=True)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--pwsh', default='pwsh')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    shell_version = subprocess.check_output([args.pwsh,'-NoProfile','-NonInteractive','-Command','$PSVersionTable.PSVersion.ToString()'],text=True).strip()
    results = []
    with tempfile.TemporaryDirectory(prefix='pwaat-package-test-') as directory:
        fixture = Path(directory)
        with zipfile.ZipFile(args.archive) as archive:
            archive.extractall(fixture)
        package = fixture/'furigana-mod'
        manifest = json.loads((package/'release-manifest.json').read_text())
        assert not (package/'backups').exists(), 'Game backup must not be shipped'
        assert not list(package.rglob('Assembly-CSharp.dll')), 'Game assembly must not be shipped'
        assert not list(package.rglob('readings.tsv')), 'Dialogue corpus must not be shipped'
        results.append('package excludes original assemblies and raw dialogue')
        managed = fixture/'PWAAT_Data/Managed'
        shutil.copytree(args.game_dir/'PWAAT_Data/Managed', managed)
        runtime = managed/'PwaatFurigana.dll'
        runtime.unlink(missing_ok=True)
        target = managed/'Assembly-CSharp.dll'
        original = (args.game_dir/'furigana-mod/backups/Assembly-CSharp.dll').read_bytes()
        target.write_bytes(original)
        for asset in manifest['sources']:
            dest = fixture/asset['path']
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(args.game_dir/asset['path'], dest)
        readings = fixture/'PWAAT_Data/StreamingAssets/Furigana/readings.tsv'
        backup = package/'backups/Assembly-CSharp.dll'

        def run(mode, succeeds=True):
            process = subprocess.run([args.pwsh,'-NoProfile','-NonInteractive','-File',str(package/'Patch.ps1'),
                                      '-GameDirectory',str(fixture),'-Mode',mode],text=True,capture_output=True)
            print(process.stdout.strip())
            if (process.returncode == 0) != succeeds:
                raise AssertionError(f'{mode} returned {process.returncode}: {process.stdout}\n{process.stderr}')
            return process

        run('Install')
        assert sha(target) == manifest['patched_sha256']
        assert sha(runtime) == manifest['runtime_sha256']
        assert sha(readings) == manifest['readings_sha256']
        assert backup.read_bytes() == original
        results.append('fresh install reconstructs exact verified assembly and 77,504 reading entries')
        run('Install')
        assert backup.read_bytes() == original
        results.append('repeat install is idempotent and preserves the original backup')
        run('Uninstall')
        assert target.read_bytes() == original and not runtime.exists() and not readings.exists()
        assert backup.read_bytes() == original
        results.append('uninstall restores exact original and retains backup')
        run('Uninstall')
        results.append('repeat uninstall succeeds')

        bad = original[:-1] + bytes([original[-1]^1])
        target.write_bytes(bad)
        run('Install', False)
        run('Uninstall', False)
        assert target.read_bytes() == bad and not runtime.exists()
        target.write_bytes(original)
        results.append('unsupported/modified assemblies rejected for install and uninstall')

        asset = fixture/manifest['sources'][0]['path']
        saved = asset.read_bytes()
        asset.write_bytes(saved[:-1]+bytes([saved[-1]^1]))
        run('Install', False)
        assert target.read_bytes() == original and not runtime.exists()
        asset.write_bytes(saved)
        results.append('modified scenario asset rejected before changing the game')

        payload = package/'payload/PwaatFurigana.dll'
        saved = payload.read_bytes()
        payload.write_bytes(saved[:-1]+bytes([saved[-1]^1]))
        run('Install', False)
        assert target.read_bytes() == original and not runtime.exists()
        payload.write_bytes(saved)
        results.append('corrupt release payload rejected before changing the game')

        backup.write_bytes(bad)
        run('Install', False)
        assert target.read_bytes() == original and backup.read_bytes() == bad
        backup.write_bytes(original)
        results.append('unexpected existing backup is preserved and rejected')
        run('Install')
        assert sha(target) == manifest['patched_sha256'] and sha(readings) == manifest['readings_sha256']
        results.append('reinstall after uninstall succeeds')
    report = dict(result='pass', archive_sha256=sha(args.archive), cases=results,
                  environment=f'PowerShell {shell_version} on {platform.system()}; actual Windows PowerShell 5.1 and in-game interaction remain untested')
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))


if __name__ == '__main__':
    main()
