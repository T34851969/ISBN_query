import pandas as pd
import sqlite3
from pathlib import Path
from fastapi import UploadFile
from App_Logger import Global_logger

class ISBN_Database:
    
    PATH: str = 'ISBN.db'
    
    def __init__(self, upload_file_list: list[UploadFile] | None = None) -> None:
        self.path = ISBN_Database.PATH
        self.conn = None
        if not self.is_exist():
            if upload_file_list:
                success = self._create_database(upload_file_list)
                if not success:
                    raise FileNotFoundError("数据库初始化失败：上传文件无有效数据或创建过程出错")
            else:
                raise FileNotFoundError("数据库不存在，且未提供上传文件")

    def __enter__(self) -> sqlite3.Connection:
        self.conn = sqlite3.connect(ISBN_Database.PATH)
        self.conn.execute("PRAGMA cache_size = 100000")
        self.conn.execute("PRAGMA temp_store = MEMORY")
        return self.conn

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        if self.conn:
            self.conn.close()

    def _create_database(self, upload_file_list: list[UploadFile]) -> bool:
        """创建ISBN数据库库表"""
        data = pd.DataFrame()
        alldata = []
        
        for file in upload_file_list:
            Global_logger.append(f"读取 {file.filename} 中...")
            
            if file.filename.endswith('.xlsx') or file.filename.endswith('.xls'):
                temp_data = pd.read_excel(file.file, dtype=str)
                alldata.append(temp_data)
                    
            elif file.filename.endswith('.csv'):
                temp_data = pd.read_csv(file.file, dtype=str)
                alldata.append(temp_data)

        if not alldata:
            Global_logger.append("没有可用的上传文件数据。")
            return False
        
        data = pd.concat(alldata, ignore_index=True)
        data = data.drop_duplicates()
        data.columns = ['ISBN']
        data = data[data['ISBN'] != '标准号']
        
        with sqlite3.connect(ISBN_Database.PATH) as conn:
            data.to_sql('ISBN_table', conn, if_exists='replace', index=False)
            conn.execute("PRAGMA cache_size = 100000")
            conn.execute("PRAGMA temp_store = MEMORY")
            conn.execute("CREATE INDEX IF NOT EXISTS idx_isbn ON ISBN_table(ISBN)")
            conn.commit()

        Global_logger.append("数据库创建完成。")
        return True
    
    def update_database(self, upload_file_list: list[UploadFile]):
        """更新ISBN数据库库表"""
        if not self.is_exist():
            Global_logger.append("数据库不存在，无法更新。")

        data = pd.DataFrame()
        alldata = []
        
        for file in upload_file_list:
            Global_logger.append(f"读取 {file.filename} 中...")
            
            if file.filename.endswith('.xlsx') or file.filename.endswith('.xls'):
                temp_data = pd.read_excel(file.file, dtype=str)
                alldata.append(temp_data)
                    
            elif file.filename.endswith('.csv'):
                temp_data = pd.read_csv(file.file, dtype=str)
                alldata.append(temp_data)

        if not alldata:
            Global_logger.append("没有可用的上传文件数据。")
            return False
        
        data = pd.concat(alldata, ignore_index=True)
        data = data.drop_duplicates()
        
        with sqlite3.connect(ISBN_Database.PATH) as conn:
            data.to_sql('Temp_ISBN', conn, if_exists='replace', index=False)
            conn.execute("PRAGMA cache_size = 100000")
            conn.execute("PRAGMA temp_store = MEMORY")
            conn.execute("CREATE INDEX IF NOT EXISTS idx_temp_isbn ON Temp_ISBN(ISBN)")
            conn.execute("INSERT OR IGNORE INTO ISBN_table(ISBN) SELECT ISBN FROM Temp_ISBN")
            conn.commit()
            conn.execute("DROP TABLE Temp_ISBN")  
        Global_logger.append("数据库更新完成。")
        return True
        

    def is_exist(self) -> bool:
        """检查数据库是否存在"""
        return Path(self.path).exists()

    def get_all_recs(self) -> int:
        with self.conn as conn:
            cursor = conn.execute("SELECT COUNT(*) FROM ISBN")
            return cursor.fetchone()[0]
