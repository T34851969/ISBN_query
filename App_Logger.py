"""全局日志记录器"""

import threading


class Logger:
    def __init__(self):
        self.logs = []
        self.lock = threading.Lock()
        
    def __enter__(self):
        if len(self.logs) > 2000:
            self.logs = self.logs[-5:]
    
    def __exit__(self, exc_type, exc_value, traceback):
        pass

    def append(self, text: str) -> None:
        # 记录日志
        with self.lock:
            self.logs.append(text)
            
    def return_logs(self) -> str:
        return "\n".join(self.logs) if self.logs else ""

    def get_all(self) -> None:
        # 获取所有日志
        with self.lock:
            with open('App_log.txt', 'w', encoding='utf-8') as f:
                for line in self.logs:
                    f.write(line + '\n')

    def clear(self) -> None:
        # 清空日志
        with self.lock:
            self.logs.clear()


Global_logger = Logger()
