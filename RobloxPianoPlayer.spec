# -*- mode: python ; coding: utf-8 -*-
from PyInstaller.utils.hooks import collect_all

datas = [
    ('src/resources', 'src/resources'),
    ('src/samples', 'src/samples'),
    ('src/piano/profiles', 'src/piano/profiles'),
    ('app_icon.ico', '.'),
]
binaries = []
hiddenimports = ['music21', 'mido', 'PySide6', 'keyboard']
tmp_ret = collect_all('music21')

def is_runtime_music21_path(path):
    parts = path.replace('\\', '/').lower().strip('/').split('/')
    return not any(part in ('corpus', 'test', 'tests') for part in parts)

def is_runtime_music21_module(name):
    parts = name.lower().split('.')
    return not any(
        part in ('corpus', 'test', 'tests') or part.startswith('test') or part.endswith('tests')
        for part in parts
    )

datas += [item for item in tmp_ret[0] if is_runtime_music21_path(item[0]) and is_runtime_music21_path(item[1])]
binaries += tmp_ret[1]
hiddenimports += [module for module in tmp_ret[2] if is_runtime_music21_module(module)]


a = Analysis(
    ['run.py'],
    pathex=['.'],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    # Music21's plotting, science, test, and corpus features are not used by the app.
    excludes=['matplotlib', 'scipy', 'pytest', 'IPython', 'notebook', 'music21.corpus', 'music21.test'],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='RobloxPianoPlayer',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,
    icon='app_icon.ico',
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    upx_exclude=[],
    name='RobloxPianoPlayer',
)
