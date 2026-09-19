# -*- coding: utf-8 -*-
"""Excel/CSV ISBN 去重工具（独立小工具，与主程序共用依赖）。

旧版逐行 openpyxl delete_rows 是 O(n²)，大表要跑几十分钟且全程阻塞界面。
现改为：pandas 一次性读入 → 向量化过滤 → 单次写回（秒级），
去重过程在后台线程执行，界面通过队列轮询进度，始终可响应。
"""

import os
import queue
import sqlite3
import threading
import tkinter as tk
from tkinter import filedialog, messagebox

import pandas as pd

DB_PATH = 'isbn_records.db'


def fuzzy_isbn_column(df: pd.DataFrame) -> str | None:
    """模糊匹配 ISBN 列（列名含 isbn / 书号 即命中），与旧版规则一致。"""
    for col in df.columns:
        val = str(col).lower()
        if any(x in val for x in ('isbn', '书号')):
            return col
    return None


def normalize(value) -> str:
    s = str(value).strip().replace('-', '').replace(' ', '')
    if s.endswith('.0') and s[:-2].isdigit():
        s = s[:-2]
    return s


def load_db_isbns(db_path: str) -> set[str]:
    conn = sqlite3.connect(db_path)
    try:
        cur = conn.execute('SELECT isbn FROM isbn_records')
        return {normalize(row[0]) for row in cur.fetchall() if row[0] is not None}
    finally:
        conn.close()


def dedup_excel(file_path: str, db_path: str, report=print) -> tuple:
    """全量去重：返回 (原始行数, 重复行数, 输出行数, 输出文件路径)。"""
    report('读取文件…')
    if file_path.lower().endswith('.csv'):
        df = pd.read_csv(file_path, dtype=str)
    else:
        df = pd.read_excel(file_path, engine='calamine', dtype=str)

    isbn_col = fuzzy_isbn_column(df)
    if isbn_col is None:
        raise Exception('未找到ISBN列，请检查表头')

    report('读取数据库…')
    db_isbns = load_db_isbns(db_path)

    report('比对去重…')
    values = df[isbn_col].map(lambda v: normalize(v) if pd.notna(v) else '')
    mask_keep = ~values.isin(db_isbns)
    df_kept = df[mask_keep]

    report('写出结果…')
    stem, ext = os.path.splitext(file_path)
    out_ext = '.csv' if file_path.lower().endswith('.csv') else '.xlsx'
    out_path = f'{stem}_dedup{out_ext}'
    if out_ext == '.csv':
        df_kept.to_csv(out_path, index=False, encoding='utf-8-sig')
    else:
        df_kept.to_excel(out_path, index=False)

    orig = len(df)
    kept = len(df_kept)
    return orig, orig - kept, kept, out_path


class App:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title('Excel/CSV ISBN去重工具')
        self.file_path = ''
        self.progress_var = tk.StringVar()
        self.result_var = tk.StringVar()
        self.setup_ui()

    def setup_ui(self):
        frm = tk.Frame(self.root)
        frm.pack(padx=20, pady=20)
        tk.Label(frm, text='选择Excel文件:').grid(row=0, column=0, sticky='e')
        tk.Button(frm, text='浏览', command=self.select_file).grid(row=0, column=1)
        self.file_label = tk.Label(frm, text='未选择文件', width=40, anchor='w')
        self.file_label.grid(row=0, column=2)
        self.start_btn = tk.Button(frm, text='开始去重', command=self.run_dedup)
        self.start_btn.grid(row=1, column=1, pady=10)
        tk.Label(frm, textvariable=self.progress_var, fg='blue').grid(
            row=2, column=0, columnspan=3)
        tk.Label(frm, textvariable=self.result_var, fg='green').grid(
            row=3, column=0, columnspan=3)

    def select_file(self):
        filetypes = [('表格文件', '*.xlsx *.csv')]
        path = filedialog.askopenfilename(filetypes=filetypes)
        if path:
            self.file_path = path
            self.file_label.config(text=os.path.basename(path))

    def run_dedup(self):
        if not self.file_path:
            messagebox.showerror('错误', '请先选择Excel文件')
            return
        if not os.path.exists(DB_PATH):
            messagebox.showerror('错误', f'数据库文件不存在: {DB_PATH}')
            return
        self.start_btn.config(state='disabled')
        self.progress_var.set('处理中…')
        self.result_var.set('')
        tasks: queue.Queue = queue.Queue()
        worker = threading.Thread(target=self._worker, args=(tasks,), daemon=True)
        worker.start()
        self._poll(tasks)

    def _worker(self, tasks: queue.Queue):
        try:
            orig, removed, kept, out_path = dedup_excel(
                self.file_path, DB_PATH,
                report=lambda text: tasks.put(('p', text)))
            tasks.put(('done', orig, removed, kept, out_path))
        except Exception as e:  # 后台线程异常须带回主线程展示
            tasks.put(('error', str(e)))

    def _poll(self, tasks: queue.Queue):
        try:
            while True:
                msg = tasks.get_nowait()
                if msg[0] == 'p':
                    self.progress_var.set(msg[1])
                elif msg[0] == 'done':
                    _, orig, removed, kept, out_path = msg
                    self.progress_var.set('')
                    self.result_var.set(
                        f'处理完成！\n原始行数: {orig}\n重复行数: {removed}\n'
                        f'输出行数: {kept}\n输出文件: {os.path.basename(out_path)}')
                    self.start_btn.config(state='normal')
                    return
                else:
                    messagebox.showerror('处理失败', msg[1])
                    self.progress_var.set('')
                    self.start_btn.config(state='normal')
                    return
        except queue.Empty:
            pass
        self.root.after(100, self._poll, tasks)


if __name__ == '__main__':
    root = tk.Tk()
    App(root)
    root.mainloop()
