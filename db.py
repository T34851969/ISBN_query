import pandas as pd
import sqlite3
from pathlib import Path
from contextlib import contextmanager

class ISBN_Database:
    def __init__(self, database_path = "ISBN.db"):
        self.database_path = database_path
        self.conn_progress = None
    
    def connection(self):
        """ISBN数据库链接"""
        self.conn_progress = sqlite3.connect(self.database_path)
        self.conn_progress.execute("PRAGMA cache_size = 100000")
        self.conn_progress.execute("PRAGMA temp_store = MEMORY")
        return self.conn_progress
    
    def close(self):
        """关闭ISBN数据库链接"""
        if self.conn_progress:
            self.conn_progress.close()
            
    @contextmanager
    def getConn(self):
        conn = self.connection()
        try:
            yield conn
        finally:
            conn.close()
            
    def create_db_ISBN(self, CSV_file):
        """创建ISBN数据库库表"""
        print(f"正从 {CSV_file}中创建ISBN数据库...")
        
        raw_data = pd.read_csv(CSV_file, dtype = str)

        with self.getConn() as conn_ing:
            raw_data.to_sql('ISBN', conn_ing, if_exists='replace', index=False)

        with self.getConn() as conn_ing:
            conn_ing.execute("CREATE INDEX IF NOT EXISTS idx_title ON ISBN(ISBN)")
            conn_ing.commit()

        print("数据库创建完成！")

    def isDB(self):
        """检查数据库是否存在"""
        return Path(self.database_path).exists()
    
    def getAllRecs(self):
        with self.getConn() as conn_ing:
            cursor = conn_ing.execute("SELECT COUNT(*) FROM ISBN")
            return cursor.fetchone()[0]
        
    def search_single(self, ISBN):
        """查询ISBN是否存在，无返回值"""
        ISBN = ISBN.replace("-", "").replace(" ","")
        if not ISBN:
            print("非法输入")
            return None
        print(f"搜索的标准号: {ISBN}")
        
        with self.getConn() as conn:
            sql = f"SELECT * FROM ISBN WHERE ISBN=?"
            files = pd.read_sql_query(sql, conn, params = [ISBN])
            
        if not files.empty:
            return True
        else:
            return False    
                
    def search_batch(self, ISBN_list):
        """批量查询ISBN是否存在"""
        ISBN_new = [ISBN.replace("-", "").replace(" ", "") for ISBN in ISBN_list if ISBN.strip()]
        if not ISBN_new:
            return set()
            
        with self.getConn() as conn:
            is_ISBN = set()
            batch_size = 500  # 分批处理防止参数过多
            for i in range(0, len(ISBN_new), batch_size):
                batch = ISBN_new[i:i+batch_size]
                params = ','.join(['?']*len(batch))
            
                sql = f"SELECT ISBN FROM ISBN WHERE ISBN IN ({params})"
                cursor = conn.execute(sql, batch)
                is_ISBN.update({line[0] for line in cursor.fetchall()})
        
        return is_ISBN
