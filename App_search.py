import sqlite3
from fastapi import UploadFile
import pandas as pd
from App_Logger import Global_logger
from typing import List

class SearchEngine:
    """ISBN查询引擎"""
    @staticmethod
    def search_single(connection: sqlite3.Connection, ISBN: str):
        """查询ISBN是否存在"""
        isbn = ISBN.replace("-", "").replace(" ", "")
        if not isbn:
            Global_logger.append("非法输入")
            return

        Global_logger.append(f"搜索: {isbn}")

        sql = "SELECT 1 FROM ISBN_table WHERE ISBN = ? LIMIT 1"
        cursor = connection.execute(sql, (isbn,))
        exists = cursor.fetchone() is not None

        Global_logger.append(f"{isbn} {'存在' if exists else '不存在'}")

    @staticmethod            
    def search_batch(connection: sqlite3.Connection, ISBN_list: list[str]):
        """批量查询ISBN是否存在"""
        isbn_in = [s.replace("-", "").replace(" ", "") for s in ISBN_list if s.strip()]
        if not isbn_in:
            Global_logger.append("没有可用的输入用于批量查询。")
            return

        found: set = set()
        batch_size = 500
        for i in range(0, len(isbn_in), batch_size):
            batch = isbn_in[i : i + batch_size]
            params = ','.join(['?'] * len(batch))
            sql = f"SELECT ISBN FROM ISBN_table WHERE ISBN IN ({params})"
            cursor = connection.execute(sql, batch)
            found.update(row[0] for row in cursor.fetchall())

        for isbn in isbn_in:
            Global_logger.append(f"{isbn} {'存在' if isbn in found else '不存在'}")

    @staticmethod
    def batch_clean(connection: sqlite3.Connection, excel_files: list[UploadFile]) -> List[str]:
        """清洗上传的文件，提取唯一ISBN列表"""
        alldata = []
        
        for file in excel_files:
            print(f"读取 {file.filename} 中...")
            
            if file.filename.endswith('.xlsx') or file.filename.endswith('.xls'):
                temp_data = pd.read_excel(file.file, dtype=str)
                alldata.append(temp_data)
                    
            elif file.filename.endswith('.csv'):
                temp_data = pd.read_csv(file.file, dtype=str)
                alldata.append(temp_data)

        if not alldata:
            print("没有可用的上传文件数据。")
            return []
        
        data = pd.concat(alldata, ignore_index=True)
        data = data.drop_duplicates()
        data.columns = ['ISBN']
        data = data[data['ISBN'] != '标准号']
        
        isbn_list = data['ISBN'].tolist()
        return isbn_list
