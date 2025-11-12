import pandas as pd
import sqlite3
from pathlib import Path


class ISBN_Database:
    def __init__(self, database_path="ISBN.db"):
        self.database_path = database_path
        self.conn = sqlite3.connect(self.database_path)

    def __enter__(self):
        self.conn = sqlite3.connect(self.database_path)
        self.conn.execute("PRAGMA cache_size = 100000")
        self.conn.execute("PRAGMA temp_store = MEMORY")
        return self.conn

    def __exit__(self, exc_type, exc_value, traceback):
        if self.conn:
            self.conn.close()

    def _create_database(self, CSV_file):
        """创建ISBN数据库库表"""
        print(f"正从 {CSV_file}中创建ISBN数据库...")

        raw_data = pd.read_csv(CSV_file, dtype=str)

        with self.conn as conn:
            raw_data.to_sql('ISBN', conn, if_exists='replace', index=False)

        with self.conn as conn:
            conn.execute("CREATE INDEX IF NOT EXISTS idx_isbn ON ISBN(ISBN)")
            conn.commit()

        print("数据库创建完成！")

    def _is_database_exists(self):
        """检查数据库是否存在"""
        return Path(self.database_path).exists()

    def get_all_recs(self):
        with self.conn as conn:
            cursor = conn.execute("SELECT COUNT(*) FROM ISBN")
            return cursor.fetchone()[0]
