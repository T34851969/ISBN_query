"""全局日志记录器"""

import threading


class Logger:
    def __init__(self):
        self.logs = []
        self.lock = threading.Lock()

    def append(self, text: str) -> None:
        with self.lock:
            self.logs.append(text)

    def get_all(self) -> str:
        with self.lock:
            return '\n'.join(self.logs)

    def clear(self) -> None:
        with self.lock:
            self.logs.clear()


Global_logger = Logger()
