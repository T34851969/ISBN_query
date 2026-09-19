# -*- mode: python ; coding: utf-8 -*-
"""PyInstaller 打包配置。

用法：python -m PyInstaller --noconfirm --clean isbn_query.spec
产物：dist/ISBNQuery.exe（单文件，浏览器访问本地端口）。
"""

a = Analysis(
    ['main.py'],
    pathex=[],
    binaries=[],
    datas=[],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='ISBNQuery',
    debug=False,
    strip=False,
    upx=False,
    console=True,
)
