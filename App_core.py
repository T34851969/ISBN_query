import pandas as pd
import io
import sqlite3
from pathlib import Path
from App_logger import Global_logger

class ISBN_Database:

    PATH: str = 'ISBN.db'

    def __init__(self) -> None:
        self.path = ISBN_Database.PATH
        self.conn = None
        self.support_args = ['single', 'multi']

    def __enter__(self) -> sqlite3.Connection:
        self.conn = sqlite3.connect(ISBN_Database.PATH)
        self.conn.execute("PRAGMA cache_size = 100000")
        self.conn.execute("PRAGMA temp_store = MEMORY")
        return self.conn

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        if self.conn:
            try:
                self.conn.close()
            except Exception as e:
                Global_logger.append(f"关闭数据库出错: {e}")

    async def create_database(self, **kwargs) -> None:
        """创建ISBN数据库库表"""
        data = await Tools.uni_entry(self, **kwargs)
        data = data.drop_duplicates()
        data.columns = ['ISBN']
        data = data[data['ISBN'] != '标准号']
        Global_logger.append("正在创建数据库，请稍候...")
        with sqlite3.connect(ISBN_Database.PATH) as conn:
            data.to_sql('ISBN_table', conn,
                        if_exists='replace', index=False)
            conn.execute("PRAGMA cache_size = 100000")
            conn.execute("PRAGMA temp_store = MEMORY")
            conn.execute(
                "CREATE INDEX IF NOT EXISTS idx_isbn ON ISBN_table(ISBN)")
            conn.commit()
        Global_logger.append("数据库创建完成。")

    async def update_database(self, **kwargs):
        """更新ISBN数据库库表"""
        data = await Tools.uni_entry(self, **kwargs)
        
        data = data.drop_duplicates()
        data.columns = ['ISBN']
        data = data[data['ISBN'] != '标准号']

        Global_logger.append("正在更新数据库，请稍候...")

        with sqlite3.connect(ISBN_Database.PATH) as conn:
            data.to_sql('Temp_ISBN', conn, if_exists='replace', index=False)
            conn.execute("PRAGMA cache_size = 100000")
            conn.execute("PRAGMA temp_store = MEMORY")
            conn.execute(
                "CREATE INDEX IF NOT EXISTS idx_temp_isbn ON Temp_ISBN(ISBN)")
            conn.execute(
                "INSERT OR IGNORE INTO ISBN_table(ISBN) SELECT ISBN FROM Temp_ISBN")
            conn.commit()
            conn.execute("DROP TABLE Temp_ISBN")
        Global_logger.append("数据库更新完成。")

    def is_exist(self) -> bool:
        """检查数据库是否存在"""
        return Path(self.path).exists()

    def get_all_recs(self) -> int:
        with self.conn as conn:
            cursor = conn.execute("SELECT COUNT(*) FROM ISBN")
            return cursor.fetchone()[0]


class Tools:
    @staticmethod
    async def uni_entry(db: ISBN_Database, **kwargs) -> pd.DataFrame:
        if not any(arg in kwargs for arg in db.support_args):
            Global_logger.append("错误：不支持的参数")
            raise ValueError("不支持的参数")
        data = pd.DataFrame()
        if 'single' in kwargs:
            file = kwargs['single']
            if file.name.endswith('.xlsx') or file.name.endswith('.xls'):
                data = await Tools.handle_xlsx_file(file)
            elif file.name.endswith('.csv'):
                data = await Tools.handle_csv_file(file)
            else:
                Global_logger.append("错误：上传的文件格式不受支持。请上传 .xlsx/.xls 或 .csv 格式的文件。")
                raise ValueError("上传的文件格式不受支持。")
        elif 'multi' in kwargs:
            files = kwargs['multi']
            if all(file.name.endswith('.xlsx') or file.name.endswith('.xls') for file in files):
                data = await Tools.merge_xlsx_files(files)
            elif all(file.name.endswith('.csv') for file in files):
                data = await Tools.merge_csv_files(files)
            else:
                Global_logger.append("错误：上传的文件格式不一致或不受支持。请上传 全部 为 .xlsx/.xls 或 .csv 格式的文件。")
                raise ValueError("上传的文件格式不一致或不受支持。")
        return data
    
    @staticmethod
    async def merge_xlsx_files(file_list: list) -> pd.DataFrame:
        dfs = []
        for file in file_list:
            df = pd.read_excel(io.BytesIO(await file.read()), engine='calamine')
            dfs.append(df)
        merged_df = pd.concat(dfs, ignore_index=True)
        return merged_df

    @staticmethod
    async def handle_xlsx_file(file) -> pd.DataFrame:
        df = pd.read_excel(io.BytesIO(await file.read()), engine='calamine')
        return df

    @staticmethod
    async def handle_csv_file(file) -> pd.DataFrame:
        df = pd.read_csv(io.BytesIO(await file.read()))
        return df
    
    @staticmethod
    async def merge_csv_files(file_list: list) -> pd.DataFrame:
        dfs = []
        for file in file_list:
            df = pd.read_csv(io.BytesIO(await file.read()))
            dfs.append(df)
        merged_df = pd.concat(dfs, ignore_index=True)
        return merged_df
