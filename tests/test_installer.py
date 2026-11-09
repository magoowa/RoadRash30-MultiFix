# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
"""Test file installation and the loaded DLLs without starting the game."""
from pathlib import Path
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def run(original_dir):
    build = (ROOT / 'build').resolve()
    folder = Path(tempfile.mkdtemp(prefix='installer-test-', dir=build)).resolve()
    if folder.parent != build:
        raise RuntimeError('Test directory must be inside build')
    unrelated_cwd = folder / 'unrelated working directory'; unrelated_cwd.mkdir()
    installer = next((ROOT / 'dist').glob('RoadRash_MultiFix_Setup_v*.exe'))
    renderer = (build / 'RoadRashMultiFix.dll').read_bytes()
    proxy = (build / 'ddraw.dll').read_bytes()
    original_exe = (original_dir / 'RoadRash95.exe').read_bytes()
    license_text = (ROOT / 'LICENSE').read_bytes()
    assert license_text in installer.read_bytes(), 'license not embedded in installer'
    checks = []

    def fixture(name, with_exe=True):
        game = folder / name; (game / 'TEXT').mkdir(parents=True)
        shutil.copyfile(installer, game / 'HD_Resolution_Patch.exe')
        if with_exe:
            (game / 'RoadRash95.exe').write_bytes(original_exe)
            shutil.copyfile(original_dir / 'TEXT/ENU.DLL', game / 'TEXT/ENU.DLL')
        (game / '1.RRS').write_bytes(b'save fixture - never modify')
        (game / 'original-art.rri').write_bytes(b'art fixture - never modify')
        return game

    def kept(ini):
        # The user's own values survive; keys missing from an older ini are added with defaults.
        text = ini.read_text(encoding='utf-8', errors='replace')
        return 'RenderHeight=900' in text and '; preserve my settings' in text and 'TextureFilter=' in text

    def execute(game, *args, success=True):
        result = subprocess.run([str(game / 'HD_Resolution_Patch.exe'), '/quiet', '/path:' + str(game), *args], cwd=unrelated_cwd, timeout=30)
        log = (game / 'RoadRash_MultiFix_Setup.log').read_text(encoding='utf-8-sig')
        assert (result.returncode == 0) == success, (result.returncode, log)
        assert 'PolyForm Noncommercial License 1.0.0' in log
        if (game / 'RoadRash95.exe').exists():
            assert (game / 'RoadRash95.exe').read_bytes() == original_exe, 'RoadRash95.exe was modified'
        assert (game / '1.RRS').read_bytes() == b'save fixture - never modify'
        assert (game / 'original-art.rri').read_bytes() == b'art fixture - never modify'
        assert not list(game.rglob('.rrhd-*.tmp'))
        return log

    try:
        compiler = Path(os.environ.get('WINDIR', r'C:\Windows')) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
        window_test = build / 'window_test.exe'
        subprocess.run([str(compiler), '/nologo', '/utf8output', '/out:' + str(window_test),
                        '/win32manifest:' + str(ROOT / 'app.manifest'),
                        '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.IO.Compression.dll',
                        str(ROOT / 'tests/test_window.cs')], check=True)
        missing = fixture('missing game', False)
        assert 'game folder' in execute(missing, success=False)
        assert not (missing / 'ddraw.dll').exists()
        checks.append('missing game executable rejected')

        game = fixture('gog path with spaces')
        subprocess.run([str(window_test), str(game / 'HD_Resolution_Patch.exe'), str(build / 'window-gog.png')],
                       cwd=unrelated_cwd, check=True, timeout=30, env=dict(os.environ, RRMF_NO_UPDATE_CHECK='1'))
        checks.append('GUI shows folder, options and Install/Restore, and opening it writes nothing')
        assert 'MultiFix installed' in execute(game)
        assert (game / 'ddraw.dll').read_bytes() == proxy and (game / 'RoadRashMultiFix.dll').read_bytes() == renderer
        assert (game / 'ResolutionPatch_Backup/state.txt').exists()
        assert not (game / 'ResolutionPatch_Backup/ddraw.dll').exists()
        subprocess.run([str(build / 'hd_test.exe'), str(game / 'RoadRash95.exe')], check=True, timeout=60)
        subprocess.run([str(build / 'hd_loader_test.exe'), str(game / 'RoadRash95.exe'), str(game / 'ddraw.dll')],
                       check=True, timeout=60)
        subprocess.run([str(build / 'hd_animation_test.exe'), str(game / 'RoadRash95.exe'),
                        str(game / 'RoadRashMultiFix.dll')], check=True, timeout=60)
        custom = b'[Graphics]\r\nEnabled=1\r\nRenderHeight=900\r\n; preserve my settings\r\n'
        (game / 'RoadRashHD.ini').write_bytes(custom)
        assert 'already installed' in execute(game)
        assert kept(game / 'RoadRashHD.ini')
        assert 'removed' in execute(game, '/restore')
        assert not (game / 'ddraw.dll').exists() and not (game / 'RoadRashMultiFix.dll').exists()
        assert kept(game / 'RoadRashHD.ini')
        assert 'already in its pre-install state' in execute(game, '/restore')
        assert 'MultiFix installed' in execute(game)
        assert kept(game / 'RoadRashHD.ini')
        checks.append('install leaves RoadRash95.exe untouched; real DLLs load; repeat, restore and config preserved')

        foreign = fixture('foreign ddraw wrapper')
        wrapper = b'someone else\'s ddraw wrapper'
        (foreign / 'ddraw.dll').write_bytes(wrapper)
        execute(foreign)
        assert (foreign / 'ddraw.dll').read_bytes() == proxy
        assert (foreign / 'ResolutionPatch_Backup/ddraw.dll').read_bytes() == wrapper
        execute(foreign, '/restore')
        assert (foreign / 'ddraw.dll').read_bytes() == wrapper
        assert not (foreign / 'RoadRashMultiFix.dll').exists()
        checks.append('existing ddraw.dll backed up and restored exactly')

        replaced = fixture('replaced after install')
        execute(replaced)
        (replaced / 'ddraw.dll').write_bytes(b'user installed another wrapper later')
        assert 'replaced by a different file' in execute(replaced, '/restore', success=False)
        assert (replaced / 'ddraw.dll').read_bytes() == b'user installed another wrapper later'
        checks.append('file replaced after install is never overwritten')

        conflict = fixture('different existing backup')
        old_backup = conflict / 'ResolutionPatch_Backup/ddraw.dll'
        old_backup.parent.mkdir(); old_backup.write_bytes(b'keep existing backup')
        (conflict / 'ddraw.dll').write_bytes(b'current wrapper')
        assert 'backup' in execute(conflict, success=False)
        assert old_backup.read_bytes() == b'keep existing backup'
        assert not (conflict / 'RoadRashMultiFix.dll').exists()
        checks.append('conflicting backup retained')

        locked = fixture('ddraw locked by another process')
        (locked / 'ddraw.dll').write_bytes(b'locked wrapper')
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                                      wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        kernel.CreateFileW.restype = wintypes.HANDLE
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        handle = kernel.CreateFileW(str(locked / 'ddraw.dll'), 0x80000000, 1, None, 3, 0, None)
        assert handle != ctypes.c_void_p(-1).value, ctypes.get_last_error()
        try:
            assert 'did not complete' in execute(locked, success=False)
        finally:
            kernel.CloseHandle(handle)
        assert (locked / 'ddraw.dll').read_bytes() == b'locked wrapper'
        assert not (locked / 'RoadRashMultiFix.dll').exists(), 'newly committed renderer not rolled back'
        assert not (locked / 'RoadRashHD.ini').exists()
        checks.append('locked ddraw.dll replacement rolls back the newly installed renderer')

        report = {'passed': checks, 'license': 'PolyForm-Noncommercial-1.0.0',
                  'game_started': False, 'real_install_modified': False, 'installer_sha256': digest(installer.read_bytes())}
        (build / 'test-results.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(report))
    finally:
        if folder.resolve().parent != build:
            raise RuntimeError('Refusing to clean a directory outside build')
        shutil.rmtree(folder)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--original-dir', type=Path, required=True,
                        help='GOG/Steam game folder (only RoadRash95.exe and TEXT/ENU.DLL are copied)')
    args = parser.parse_args()
    run(args.original_dir.resolve())
