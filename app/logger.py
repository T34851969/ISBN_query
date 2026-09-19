"""全局运行日志：有界缓冲 + 版本号脏标记。

不向 UI 推送回调；界面端用定时器轮询 snapshot()，
版本号变化才刷新，避免逐条全量重渲染造成的 O(n²) 卡顿。
"""

import datetime
import threading
from collections import deque
from pathlib import Path

MAX_LOG_LINES = 2000


class Logger:
    def __init__(self, maxlen: int = MAX_LOG_LINES):
        self._logs: deque[str] = deque(maxlen=maxlen)
        self._lock = threading.Lock()
        self._version = 0
        self._dropped = 0  # 因超出上限被裁剪的总行数

    def append(self, text: str) -> None:
        timestamp = datetime.datetime.now().strftime("[%Y-%m-%d %H:%M:%S]")
        with self._lock:
            if len(self._logs) == self._logs.maxlen:
                self._dropped += 1
            self._logs.append(f"{timestamp} {text}")
            self._version += 1

    def snapshot(self) -> tuple[int, int, list[str]]:
        """返回（版本号, 累计裁剪行数, 当前行列表副本），调用方据此做增量刷新。"""
        with self._lock:
            return self._version, self._dropped, list(self._logs)

    def export(self, path: str = 'log.txt') -> Path:
        """把当前全部日志写入文本文件。"""
        out = Path(path)
        with self._lock:
            lines = list(self._logs)
        out.write_text('\n'.join(lines) + ('\n' if lines else ''), encoding='utf-8')
        return out

    def clear(self) -> None:
        with self._lock:
            self._logs.clear()
            self._version += 1


Global_logger = Logger()
