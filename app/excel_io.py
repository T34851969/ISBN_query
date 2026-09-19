"""表格文件读写与 ISBN 列处理。

读统一走 calamine 引擎（xlsx/xls 大文件解析比 openpyxl 快一个量级）；
写统一落到 output/ 目录并带时间戳，避免静默覆盖。
本模块内函数均为阻塞式，只能在工作线程中调用。
"""

import datetime
import io
from pathlib import Path

import pandas as pd

from .logger import Global_logger

OUTPUT_DIR = Path('output')
ISBN_COLUMN_CANDIDATES = ('ISBN', '标准号', 'ISBN标准号')


def read_table(filename: str, content: bytes) -> pd.DataFrame:
    """按后缀解析上传文件内容为 DataFrame（全字符串读取，ISBN 不失真）。"""
    name = filename.lower()
    buf = io.BytesIO(content)
    if name.endswith('.csv'):
        return pd.read_csv(buf, dtype=str)
    if name.endswith('.xlsx') or name.endswith('.xls'):
        return pd.read_excel(buf, engine='calamine', dtype=str)
    raise ValueError(f"不支持的文件格式：{filename}（仅支持 .xlsx/.xls/.csv）")


def pick_isbn_column(df: pd.DataFrame) -> str | None:
    """按候选列名识别 ISBN 列。"""
    for col in ISBN_COLUMN_CANDIDATES:
        if col in df.columns:
            return col
    return None


def transform_isbn_series(values: pd.Series) -> pd.Series:
    """对已过滤非空的 ISBN 列做逐格变换（保持行对齐）：去首尾空格/连字符/空格，
    并修掉 Excel 数值单元格导入产生的浮点尾差（如 9787111123458.0）。"""
    s = values.astype(str).str.strip()
    s = s.str.replace('-', '', regex=False).str.replace(' ', '', regex=False)
    s = s.str.replace(r'\.0+$', '', regex=True)
    return s


def clean_isbn_values(values: pd.Series) -> list[str]:
    """提取并清洗 ISBN 值：去空值/表头行，去连字符与空格，修掉浮点尾差。"""
    s = values.dropna()
    s = s[s.astype(str) != '标准号']
    s = transform_isbn_series(s)
    s = s[s != '']
    s = s[s.str.lower() != 'nan']
    return s.tolist()


def parse_isbns_from_files(files: list[tuple[str, bytes]]) -> list[str]:
    """解析一组上传文件，提取并清洗 ISBN，返回去重后的列表。"""
    collected: list[str] = []
    seen: set[str] = set()
    for name, content in files:
        try:
            df = read_table(name, content)
        except ValueError as e:
            Global_logger.append(str(e))
            continue
        col = pick_isbn_column(df)
        if col is None:
            if df.shape[1] == 1:
                col = df.columns[0]
            else:
                Global_logger.append(f"文件 {name} 缺少 ISBN/标准号/ISBN标准号 列，已跳过。")
                continue
        for isbn in clean_isbn_values(df[col]):
            if isbn not in seen:
                seen.add(isbn)
                collected.append(isbn)
    return collected


def write_excel(df: pd.DataFrame, source_name: str) -> Path:
    """把清洗结果写入 output/，文件名带时间戳，避免静默覆盖。"""
    OUTPUT_DIR.mkdir(exist_ok=True)
    stamp = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    path = OUTPUT_DIR / f"{Path(source_name).stem}_cleaned_{stamp}.xlsx"
    df.to_excel(path, index=False)
    return path


def write_result_csv(rows: list[dict]) -> Path:
    """把批量查询结果写入 output/（utf-8-sig，Windows Excel 打开不乱码）。"""
    OUTPUT_DIR.mkdir(exist_ok=True)
    stamp = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    path = OUTPUT_DIR / f"isbn_check_result_{stamp}.csv"
    pd.DataFrame(rows, columns=['isbn', 'result']).to_csv(
        path, index=False, encoding='utf-8-sig')
    return path
