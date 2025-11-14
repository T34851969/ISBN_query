"""全局日志记录器"""

import threading
import datetime


class Logger:
    def __init__(self):
        self.logs = []
        self.lock = threading.Lock()
        self.callbacks = []  # 初始化回调列表

    def __enter__(self):
        if len(self.logs) > 2000:
            self.logs = self.logs[-5:]

    def __exit__(self, exc_type, exc_value, traceback):
        pass

    def _notify(self):
        # 通知所有注册的回调
        for cb in self.callbacks:
            try:
                cb(self.return_logs())
            except Exception as e:
                # 可选：记录回调异常
                pass

    def append(self, text: str) -> None:
        # 记录日志，加入时间戳
        timestamp = datetime.datetime.now().strftime("[%Y-%m-%d %H:%M:%S]")
        with self.lock:
            self.logs.append(f"{timestamp} {text}")
        self._notify()  # 日志变化后通知

    def return_logs(self) -> str:
        return "\n".join(self.logs) if self.logs else ""
    
    def register_callback(self, cb):
        # 注册回调，避免重复注册
        if cb not in self.callbacks:
            self.callbacks.append(cb)

    def get_all(self) -> None:
        # 获取所有日志
        with self.lock:
            with open('log.txt', 'w', encoding='utf-8') as f:
                for line in self.logs:
                    f.write(line + '\n')

    def clear(self) -> None:
        # 清空日志
        with self.lock:
            self.logs.clear()
        self._notify()  # 日志变化后通知

Global_logger = Logger()