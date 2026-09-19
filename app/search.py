"""查询引擎：纯同步函数，返回结果对象，由 UI 层放入工作线程执行。

批量查询不再逐条刷日志——日志只记摘要，逐条结果通过返回值交给
界面一次性渲染，根治"每条日志全量重渲染 + WebSocket 推送"的回调风暴。
"""

from dataclasses import dataclass, field

from .database import ISBN_Database
from .excel_io import pick_isbn_column, read_table, transform_isbn_series, write_excel
from .logger import Global_logger


def normalize_isbn(value: str) -> str:
    return value.replace('-', '').replace(' ', '').strip()


@dataclass
class BatchResult:
    """批量查询结果：items 保持输入顺序，元素为 (isbn, 是否存在)。"""
    items: list[tuple[str, bool]] = field(default_factory=list)

    @property
    def total(self) -> int:
        return len(self.items)

    @property
    def found_count(self) -> int:
        return sum(1 for _, ok in self.items if ok)

    @property
    def missing_count(self) -> int:
        return self.total - self.found_count


def search_single(db: ISBN_Database, raw: str) -> bool | None:
    """单次查询，返回是否在库；输入无效时返回 None。"""
    isbn = normalize_isbn(raw)
    if not isbn:
        Global_logger.append("非法输入")
        return None
    exists = db.exists_single(isbn)
    Global_logger.append(f"{isbn} {'存在' if exists else '不存在'}")
    return exists


def search_batch(db: ISBN_Database, raw_lines: list[str]) -> BatchResult:
    """批量查询：去重、清洗后整批比对，结果按输入顺序返回。"""
    isbns: list[str] = []
    seen: set[str] = set()
    for line in raw_lines:
        isbn = normalize_isbn(line)
        if isbn and isbn not in seen:
            seen.add(isbn)
            isbns.append(isbn)
    if not isbns:
        Global_logger.append("没有可用的输入。")
        return BatchResult()

    found = db.fetch_existing(isbns)
    result = BatchResult(items=[(isbn, isbn in found) for isbn in isbns])
    Global_logger.append(
        f"批量查询完成：共 {result.total} 条，"
        f"存在 {result.found_count}，不存在 {result.missing_count}")
    return result


def batch_clean(db: ISBN_Database, files: list[tuple[str, bytes]],
                progress=None) -> list[str]:
    """批量清洗：剔除库中已存在的行，整行结果写入 output/，返回保留的 ISBN。"""
    kept_all: list[str] = []
    for idx, (name, content) in enumerate(files):
        if progress:
            progress(idx / len(files), f"读取 {name}")
        Global_logger.append(f"读取 {name} 中...")
        try:
            df = read_table(name, content)
        except ValueError as e:
            Global_logger.append(str(e))
            continue
        col = pick_isbn_column(df)
        if col is None:
            Global_logger.append(f"文件 {name} 缺少 ISBN/标准号/ISBN标准号 列。")
            continue

        # 与旧版一致：先剔除空行与表头行，再逐格清洗（保持行对齐供整行过滤导出）
        df = df[df[col].notnull()]
        df = df[df[col] != '标准号']
        df[col] = transform_isbn_series(df[col])
        found = db.fetch_existing(df[col].tolist())
        df_kept = df[~df[col].isin(found)]

        if progress:
            progress((idx + 0.5) / len(files), f"导出 {name} 的清洗结果")
        out_path = write_excel(df_kept, name)
        kept_all.extend(df_kept[col].dropna().tolist())
        Global_logger.append(
            f"{name} 完成：在库剔除 {len(found)} 行，保留 {len(df_kept)} 行 → {out_path}")

    Global_logger.append(f"清洗完成，共保留 {len(kept_all)} 条。")
    return kept_all
