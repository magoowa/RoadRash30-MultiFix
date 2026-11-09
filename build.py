# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
"""Build the complete HD installer without distributing or requiring game files."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess

ROOT = Path(__file__).resolve().parent


def compiler_environment(explicit):
    if explicit:
        path = explicit.resolve()
    else:
        vswhere = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'Microsoft Visual Studio/Installer/vswhere.exe'
        installation = subprocess.check_output([str(vswhere), '-latest', '-products', '*', '-requires',
                                               'Microsoft.VisualStudio.Component.VC.Tools.x86.x64',
                                               '-property', 'installationPath'], encoding='utf-8').strip()
        path = Path(installation) / 'VC/Auxiliary/Build/vcvarsall.bat'
    if not path.is_file():
        raise FileNotFoundError('Visual Studio C++ desktop development tools and the Windows SDK are required. Use --vcvarsall to point to vcvarsall.bat')
    return path


def mod_version():
    import re
    return re.search(r'RRHD_VERSION\s+"([^"]+)"', (ROOT / 'src/hd_version.hpp').read_text()).group(1)


def build(vcvarsall, tests):
    work = ROOT / 'build'
    work.mkdir(exist_ok=True)
    version = mod_version()
    (work / 'Version.cs').write_text('internal static class ModInfo { public const string Version = "%s"; public const string Name = "RoadRash30-MultiFix"; }\n' % version, encoding='utf-8')
    (ROOT / 'dist').mkdir(exist_ok=True)
    commands = ['@echo off', 'chcp 65001 >nul', f'call "{compiler_environment(vcvarsall)}" x86 >nul', 'if errorlevel 1 exit /b 1',
                'cd /d "%~dp0.."',
                'rc /nologo /fo build\\hd_menu.res src\\hd_menu.rc', 'if errorlevel 1 exit /b 1',
                # Shaders are compiled here, not at run time (works under Wine/Proton, where d3dcompiler_47 may not cope).
                'cl /nologo /std:c++17 /O2 /MT /EHsc /utf-8 tools\\compile_shaders.cpp /Fo:build\\compile_shaders.obj '
                '/Fe:build\\compile_shaders.exe /link d3dcompiler.lib', 'if errorlevel 1 exit /b 1',
                'build\\compile_shaders.exe build\\hd_shaders.inc', 'if errorlevel 1 exit /b 1',
                'cl /nologo /std:c++17 /O2 /MT /EHsc /W4 /utf-8 /I build /LD src\\hd_renderer.cpp '
                '/Fo:build\\hd_renderer.obj /Fe:build\\RoadRashMultiFix.dll /link /Brepro '
                '/DEF:src\\hd_renderer.def build\\hd_menu.res user32.lib gdi32.lib advapi32.lib ole32.lib', 'if errorlevel 1 exit /b 1',
                'cl /nologo /std:c++17 /O2 /MT /EHsc /W4 /utf-8 /LD src\\ddraw_proxy.cpp '
                '/Fo:build\\ddraw_proxy.obj /Fe:build\\ddraw.dll /link /Brepro '
                '/DEF:src\\ddraw_proxy.def user32.lib', 'if errorlevel 1 exit /b 1']
    if tests:
        for name in ['hd_test', 'hd_loader_test', 'hd_perf_test', 'hd_animation_test', 'hd_gpu_test']:
            commands.extend([f'cl /nologo /std:c++17 /O2 /MT /EHsc /W4 /utf-8 /I build tests\\{name}.cpp '
                             f'/Fo:build\\{name}.obj /Fe:build\\{name}.exe /link user32.lib gdi32.lib advapi32.lib ole32.lib',
                             'if errorlevel 1 exit /b 1'])
    commands.append('exit /b 0')
    batch = work / 'compile.cmd'
    batch.write_text('\n'.join(commands) + '\n', encoding='utf-8', newline='\r\n')
    subprocess.run(['cmd.exe', '/d', '/c', str(batch)], check=True)
    csc = Path(os.environ.get('WINDIR', r'C:\Windows')) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
    exe = ROOT / ('dist/RoadRash_MultiFix_Setup_v%s.exe' % version)
    for old in exe.parent.glob('RoadRash_MultiFix_Setup*.exe'):
        old.unlink()
    subprocess.run([str(csc), '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/win32icon:' + str(ROOT / 'app.ico'),
                    '/out:' + str(exe), '/win32manifest:' + str(ROOT / 'app.manifest'),
                    '/resource:' + str(work / 'RoadRashMultiFix.dll') + ',RoadRashMultiFix.dll',
                    '/resource:' + str(work / 'ddraw.dll') + ',ddraw.dll',
                    '/resource:' + str(ROOT / 'app.ico') + ',app.ico',
                    '/resource:' + str(ROOT / 'docs/logo.png') + ',logo.png',
                    '/resource:' + str(ROOT / 'RoadRashHD.ini') + ',RoadRashHD.ini',
                    '/resource:' + str(ROOT / 'LICENSE') + ',LICENSE', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.IO.Compression.dll',
                    str(ROOT / 'PatchWindow.cs'),
                    str(ROOT / 'Installer.cs'), str(work / 'Version.cs')], check=True)
    manifest = {'renderer_version': 5, 'installer_version': version, 'exe_modified': False, 'license': 'PolyForm-Noncommercial-1.0.0',
                'game_files_embedded': False,
                'installer_bytes': exe.stat().st_size,
                'installer_sha256': hashlib.sha256(exe.read_bytes()).hexdigest(),
                'renderer_sha256': hashlib.sha256((work / 'RoadRashMultiFix.dll').read_bytes()).hexdigest(),
                'ddraw_proxy_sha256': hashlib.sha256((work / 'ddraw.dll').read_bytes()).hexdigest()}
    (work / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--vcvarsall', type=Path, help='optional explicit path to vcvarsall.bat')
    parser.add_argument('--tests', action='store_true', help='also compile offscreen renderer tests')
    args = parser.parse_args()
    build(args.vcvarsall, args.tests)
