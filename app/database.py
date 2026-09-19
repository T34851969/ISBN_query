"""SQLite 数据库管理。

核心约定：
- 持有一个长连接（check_same_thread=False），所有访问经 self._lock 串行化；
  连接与页缓存跨操作复用，不再每次查询重建。
- 建库/更新按 INSERT_CHUNK 分块 executemany 并回报进度，大文件入库不再一票阻塞。
- 表结构保持 ISBN_table(ISBN) + idx_isbn，与旧版生成的 .db 文件完全兼容。
本类方法为阻塞式，只能在工作线程中调用（exists_single/has_table 除外，
它们在事件循环里调用也只会短暂持锁）。
"""

import sqlite3
import threading
from pathlib import Path
from typing import Callable

ProgressCallback = Callable[[float, str], None]

QUERY_BATCH = 500       # IN 查询每批参数数（留足 SQLite 变量数上限余量）
INSERT_CHUNK = 50_000   # 入库分块行数，兼顾内存与进度粒度


class ISBN_Database:
    DEFAULT_PATH = 'ISBN.db'

    def __init__(self, path: str | None = None):
        self.path = path or self.DEFAULT_PATH
        self._lock = threading.Lock()
        self._conn: sqlite3.Connection | None = None
        self._connect()

    # ---- 连接管理 ----

    def _connect(self) -> None:
        self._conn = sqlite3.connect(self.path, check_same_thread=False)
        self._conn.execute("PRAGMA cache_size = -65536")  # 64MB 页缓存，连接复用后真正生效
        self._conn.execute("PRAGMA temp_store = MEMORY")

    def _reopen(self, path: str) -> None:
        with self._lock:
            if self._conn is not None:
                self._conn.close()
            self.path = path
            self._connect()

    def switch(self, path: str) -> None:
        self._reopen(path)

    def close(self) -> None:
        with self._lock:
            if self._conn is not None:
                self._conn.close()
                self._conn = None

    def is_exist(self) -> bool:
        """检查数据库文件是否存在。"""
        return Path(self.path).exists()

    def has_table(self) -> bool:
        return self._table_exists()

    def _table_exists(self) -> bool:
        with self._lock:
            cur = self._conn.execute(
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='ISBN_table'")
            return cur.fetchone() is not None

    # ---- 查询 ----

    def exists_single(self, isbn: str) -> bool:
        with self._lock:
            cur = self._conn.execute(
                "SELECT 1 FROM ISBN_table WHERE ISBN = ? LIMIT 1", (isbn,))
            return cur.fetchone() is not None

    def fetch_existing(self, isbns: list[str]) -> set[str]:
        """返回给定 ISBN 列表中已存在于库内的集合。"""
        found: set[str] = set()
        with self._lock:
            for i in range(0, len(isbns), QUERY_BATCH):
                batch = isbns[i:i + QUERY_BATCH]
                placeholders = ','.join(['?'] * len(batch))
                cur = self._conn.execute(
                    f"SELECT ISBN FROM ISBN_table WHERE ISBN IN ({placeholders})", batch)
                found.update(row[0] for row in cur.fetchall())
        return found

    # ---- 建库 / 更新 ----

    def create(self, isbns: list[str], path: str | None = None,
               progress: ProgressCallback | None = None) -> None:
        """以给定 ISBN 列表重建 ISBN_table（原表内容被替换），语义同旧版 to_sql(replace)。"""
        if path and path != self.path:
            self._reopen(path)
        total = len(isbns)
        with self._lock:
            cur = self._conn.cursor()
            cur.execute("DROP TABLE IF EXISTS ISBN_table")
            cur.execute("CREATE TABLE ISBN_table (ISBN TEXT)")
            for i in range(0, total, INSERT_CHUNK):
                chunk = [(v,) for v in isbns[i:i + INSERT_CHUNK]]
                cur.executemany("INSERT INTO ISBN_table (ISBN) VALUES (?)", chunk)
                self._conn.commit()
                self._report(progress, (i + len(chunk)) / total, f"写入 {i + len(chunk)}/{total}")
            cur.execute("CREATE INDEX IF NOT EXISTS idx_isbn ON ISBN_table (ISBN)")
            self._conn.commit()

    def update(self, isbns: list[str], progress: ProgressCallback | None = None) -> int:
        """把 ISBN 列表并入当前库，返回实际新增行数。流程保持旧版 Temp_ISBN 方案。"""
        if not self._table_exists():
            raise RuntimeError(f"数据库 {self.path} 尚未初始化，请先创建数据库。")
        total = len(isbns)
        with self._lock:
            cur = self._conn.cursor()
            cur.execute("DROP TABLE IF EXISTS Temp_ISBN")
            cur.execute("CREATE TABLE Temp_ISBN (ISBN TEXT)")
            for i in range(0, total, INSERT_CHUNK):
                chunk = [(v,) for v in isbns[i:i + INSERT_CHUNK]]
                cur.executemany("INSERT INTO Temp_ISBN (ISBN) VALUES (?)", chunk)
                self._conn.commit()
                self._report(progress, (i + len(chunk)) / total * 0.9, f"解析待并入 {i + len(chunk)}/{total}")
            cur.execute("CREATE INDEX IF NOT EXISTS idx_temp_isbn ON Temp_ISBN (ISBN)")
            cur.execute("INSERT OR IGNORE INTO ISBN_table (ISBN) SELECT ISBN FROM Temp_ISBN")
            added = cur.rowcount if cur.rowcount and cur.rowcount > 0 else 0
            self._conn.commit()
            cur.execute("DROP TABLE Temp_ISBN")
            self._conn.commit()
        return added

    @staticmethod
    def _report(progress: ProgressCallback | None, fraction: float, label: str) -> None:
        if progress is None:
            return
        try:
            progress(max(0.0, min(1.0, fraction)), label)
        except Exception:
            pass
