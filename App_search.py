import sqlite3
import pandas as pd
from App_logger import Global_logger
from pathlib import Path


class SearchEngine:
    """ISBN查询引擎"""
    @staticmethod
    def search_single(db: sqlite3, ISBN: str):
        """查询ISBN是否存在"""
        isbn = ISBN.replace("-", "").replace(" ", "")
        if not isbn:
            Global_logger.append("非法输入")
            return

        Global_logger.append(f"搜索: {isbn}")
        
        with db as conn:
            sql = "SELECT 1 FROM ISBN_table WHERE ISBN = ? LIMIT 1"
            cursor = conn.execute(sql, (isbn,))
            exists = cursor.fetchone() is not None

        Global_logger.append(f"{isbn} {'存在' if exists else '不存在'}")

    @staticmethod
    def search_batch(db: sqlite3, ISBN_list: list[str]):
        """批量查询ISBN是否存在"""
        isbn_in = [s.replace("-", "").replace(" ", "") for s in ISBN_list if s.strip()]
        if not isbn_in:
            Global_logger.append("没有可用的输入。")
            return

        found: set = set()
        batch_size = 500
        with db as conn:
            for i in range(0, len(isbn_in), batch_size):
                batch = isbn_in[i: i + batch_size]
                params = ','.join(['?'] * len(batch))
                sql = f"SELECT ISBN FROM ISBN_table WHERE ISBN IN ({params})"
                cursor = conn.execute(sql, batch)
                found.update(row[0] for row in cursor.fetchall())

        for isbn in isbn_in:
            Global_logger.append(f"{isbn} {'存在' if isbn in found else '不存在'}")

    @staticmethod
    async def batch_clean(db: sqlite3, excel_files):
        """批量清洗Excel文件，自动识别ISBN相关列"""
        import io
        result_isbns = []

        # 支持的列名
        isbn_col_candidates = ['ISBN', '标准号', 'ISBN标准号']

        for file in excel_files:
            Global_logger.append(f"读取 {file.name} 中...")

            if file.name.endswith('.xlsx'):
                content = await file.read()
                df = pd.read_excel(io.BytesIO(content), dtype=str)
            elif file.name.endswith('.csv'):
                content = await file.read()
                df = pd.read_csv(io.BytesIO(content), dtype=str)
            else:
                Global_logger.append(f"文件 {file.name} 格式不支持")
                continue

            # 自动检测ISBN相关列名
            isbn_col = None
            for col in isbn_col_candidates:
                if col in df.columns:
                    isbn_col = col
                    break

            if not isbn_col:
                Global_logger.append(f"文件 {file.name} 缺少 ISBN/标准号/ISBN标准号 列。")
                continue

            df = df[df[isbn_col].notnull()]
            df = df[df[isbn_col] != '标准号']
            df[isbn_col] = df[isbn_col].astype(str).str.replace('-', '').str.replace(' ', '')

            isbn_list = df[isbn_col].tolist()

            found = set()
            batch_size = 500

            with db as conn:
                for i in range(0, len(isbn_list), batch_size):
                    batch = isbn_list[i:i+batch_size]
                    params = ','.join(['?'] * len(batch))
                    sql = f"SELECT ISBN FROM ISBN_table WHERE ISBN IN ({params})"
                    cursor = conn.execute(sql, batch)
                    found.update(row[0] for row in cursor.fetchall())

            df_cleaned = df[~df[isbn_col].isin(found)]

            file_path = Path(file.name)
            df_cleaned.to_excel(file_path, index=False)
            result_isbns.extend(df_cleaned[isbn_col].dropna().tolist())
        if not result_isbns:
            Global_logger.append("没有可用的上传文件数据。")
        Global_logger.append(f"完成，保留 {len(result_isbns)} 条")
