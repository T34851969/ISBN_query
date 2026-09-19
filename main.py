"""ISBN 查重系统入口（NiceGUI 本地 Web 应用）。"""

import os
import sys


def _setup_frozen_path() -> None:
    """PyInstaller 打包（frozen）后，让 NiceGUI 能定位到真实脚本路径。"""
    if not getattr(sys, 'frozen', False):
        return
    meipass = getattr(sys, '_MEIPASS', None)
    if meipass:
        candidate = os.path.join(meipass, 'main.py')
        if os.path.exists(candidate):
            sys.argv[0] = candidate
    else:
        try:
            import inspect
            import tempfile
            src = inspect.getsource(sys.modules['__main__'])
            fd, path = tempfile.mkstemp(suffix='.py', text=True)
            with os.fdopen(fd, 'w', encoding='utf-8') as f:
                f.write(src)
            sys.argv[0] = path
        except Exception:
            pass


_setup_frozen_path()

from nicegui import ui  # noqa: E402  需在 frozen 路径处理之后导入

from app.database import ISBN_Database  # noqa: E402
from app.logger import Global_logger  # noqa: E402
from app.ui import IsbnQueryUI  # noqa: E402


def main() -> None:
    Global_logger.append('应用启动')
    database = ISBN_Database()
    IsbnQueryUI(database)
    ui.run(reload=False, title='ISBN查重系统')


if __name__ == '__main__':
    main()
