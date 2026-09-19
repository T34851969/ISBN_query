"""NiceGUI 界面层：页面构建与事件处理。

反卡死的两条铁律：
1. 所有耗时操作（文件解析、入库、批量查询、导出）一律经 run.io_bound
   放入工作线程，事件循环里只做交互与轻量状态读取；
2. 日志与进度由定时器轮询节流刷新（LOG_REFRESH_INTERVAL），不逐条推送。

任务运行期间置位 self.busy，界面控件统一禁用并显示进度条，
防止并发任务互相踩踏数据库连接。
"""

from pathlib import Path

from nicegui import app as nicegui_app
from nicegui import run, ui

from . import search
from .database import ISBN_Database
from .excel_io import (clean_isbn_values, parse_isbns_from_files, pick_isbn_column,
                       read_table, write_result_csv)
from .logger import Global_logger

LOG_REFRESH_INTERVAL = 0.3  # 日志/进度轮询周期（秒）


class IsbnQueryUI:
    def __init__(self, database: ISBN_Database):
        self.db = database
        self.busy = False
        # 工作线程写入、定时器读取的进度状态
        self.progress = {'visible': False, 'value': 0.0, 'label': ''}

        @ui.page('/')
        def index():
            self._build_page()

    # ---- 页面构建 ----

    def _build_page(self) -> None:
        ui.markdown('# **ISBN 查重系统**' if self._db_ready()
                    else '# **初始化**').style('align-self:center;')

        lockables: list = []

        with ui.column().classes('w-full max-w-4xl mx-auto'):
            if self._db_ready():
                lockables += self._build_main_panel()
            else:
                lockables += self._build_init_panel()
            progress_elems = self._build_progress_bar()
            self._build_log_panel(lockables)

        ui.button('关闭应用', on_click=self._close_app).style(
            'position: fixed; bottom: 20px; right: 20px; '
            'font-size: 16px; padding: 10px 20px;')

        self._start_state_timer(lockables, progress_elems)

    def _db_ready(self) -> bool:
        return self.db.is_exist() and self.db.has_table()

    def _build_init_panel(self) -> list:
        ui.markdown('## **数据库初始化界面**')
        ui.markdown('### **上传 .xlsx 或 .csv 文件以创建数据库：**')
        upload = ui.upload(
            multiple=True, on_multi_upload=self._on_create_upload,
            label='单或多传文件，必须一致格式').style('font-size:20px;')
        return [upload]

    def _build_main_panel(self) -> list:
        lockables: list = []

        with ui.row().classes('w-full items-center'):
            db_select = ui.select(self._list_db_files(), value=self.db.path,
                                  label='选择数据库文件').style('width:300px;')
            switch_btn = ui.button('切换数据库', on_click=lambda: self._switch_database(db_select.value))
        lockables.append(switch_btn)

        ui.separator().style('width:800px; margin-top:10px; margin-bottom:10px;')

        with ui.tabs() as tabs:
            t1 = ui.tab('单次查询')
            t2 = ui.tab('批量查询')
            t3 = ui.tab('更新数据库')
            t4 = ui.tab('新建数据库')

        with ui.tab_panels(tabs, value=t1).classes('w-full'):
            with ui.tab_panel(t1):
                ui.markdown('## **单次**')
                single_input = ui.input('请输入 ISBN，可带连字符或空格：',
                                        placeholder='978-7-111-12345-6').style('width:400px;')
                single_btn = ui.button('查询', on_click=lambda: self._on_single(single_input.value))
                single_btn.style('font-size:22px; margin-left:10px;')
                lockables.append(single_btn)

            with ui.tab_panel(t2):
                ui.markdown('## **批量**')
                with ui.tabs() as sub_tabs:
                    s1 = ui.tab('输入框输入')
                    s2 = ui.tab('上传文件')
                with ui.tab_panels(sub_tabs, value=s1).classes('w-full'):
                    with ui.tab_panel(s1):
                        ui.markdown('### **请输入多个 ISBN，每行一个，可带连字符或空格：**')
                        batch_text = ui.textarea(
                            placeholder='978-7-111-12345-6\n978 7 111 12345 7\n9787111123458'
                        ).style('width:600px; height:300px;')
                        batch_btn = ui.button('查询', on_click=lambda: self._on_batch(batch_text.value))
                        batch_btn.style('font-size:22px; margin-top:10px;')
                        lockables.append(batch_btn)
                        lockables += self._build_result_panel()
                    with ui.tab_panel(s2):
                        ui.markdown('### **上传包含 ISBN 列的 .xlsx 或 .csv 文件，去重并返回**')
                        clean_upload = ui.upload(auto_upload=True, on_upload=self._on_clean_upload)
                        clean_upload.style('font-size:20px;').classes('max-w-full')
                        clean_upload.props('accept=".xlsx,.csv,.xls"')
                        lockables.append(clean_upload)

            with ui.tab_panel(t3):
                ui.markdown('## **更新数据库**')
                ui.markdown('### **上传 .xlsx 或 .csv 文件，系统自动更新：**')
                update_upload = ui.upload(auto_upload=True, multiple=True,
                                          on_upload=self._on_update_upload)
                update_upload.style('font-size:20px;')
                lockables.append(update_upload)

            with ui.tab_panel(t4):
                ui.markdown('# **新建数据库**').style('align-self:center;')
                ui.markdown('### **上传 .xlsx 或 .csv 文件以创建数据库：**')
                new_upload = ui.upload(multiple=True, on_multi_upload=self._on_create_upload,
                                       label='单或多传文件，必须一致格式')
                new_upload.style('font-size:20px;')
                lockables.append(new_upload)

        return lockables

    def _build_result_panel(self) -> list:
        """批量查询结果表：结果一次性渲染，分页展示，不进日志流。"""
        ui.markdown('### **查询结果**')
        columns = [
            {'name': 'isbn', 'label': 'ISBN', 'field': 'isbn', 'align': 'left'},
            {'name': 'result', 'label': '结果', 'field': 'result', 'align': 'left'},
        ]
        self._result_table = ui.table(columns=columns, rows=[], row_key='isbn',
                                      pagination=50).classes('w-full')
        export_btn = ui.button('导出结果（CSV）',
                               on_click=lambda: self._export_results(self._result_table.rows))
        return [export_btn]

    def _build_log_panel(self, lockables: list) -> None:
        ui.markdown('## **运行日志**').style('align-self:center;')
        log = ui.log(max_lines=2000).classes('w-full h-64')
        self._log_element = log
        _, _, lines = Global_logger.snapshot()
        if lines:
            log.push('\n'.join(lines))
        with ui.row().style('margin-top:20px; justify-content: flex-start; gap: 20px;'):
            clear_btn = ui.button('清空日志', on_click=Global_logger.clear)
            export_btn = ui.button('导出日志', on_click=lambda: self._export_log())
            clear_btn.style('font-size: 16px; padding: 10px 20px;')
            export_btn.style('font-size: 16px; padding: 10px 20px;')
        lockables += [clear_btn, export_btn]

    def _build_progress_bar(self) -> dict:
        row = ui.row().classes('w-full items-center gap-2')
        label = ui.label('').style('color:#666;')
        bar = ui.linear_progress(value=0, show_value=False).classes('flex-grow')
        row.set_visibility(False)
        return {'row': row, 'label': label, 'bar': bar}

    # ---- 定时器：节流刷新日志 / 进度 / 控件锁定 ----

    def _start_state_timer(self, lockables: list, progress_elems: dict) -> None:
        last_log_version = -1
        last_dropped = 0
        pushed_count = 0
        last_busy: bool | None = None
        last_progress: tuple = ()

        def poll() -> None:
            nonlocal last_log_version, last_dropped, pushed_count
            nonlocal last_busy, last_progress

            # 日志：版本号变化才刷新；未被裁剪时只推送新增行，避免全量重发
            version, dropped, lines = Global_logger.snapshot()
            if version != last_log_version:
                log = self._log_element
                if dropped != last_dropped or len(lines) < pushed_count:
                    log.clear()
                    if lines:
                        log.push('\n'.join(lines))
                    pushed_count = len(lines)
                else:
                    new_lines = lines[pushed_count:]
                    if new_lines:
                        log.push('\n'.join(new_lines))
                    pushed_count = len(lines)
                last_log_version = version
                last_dropped = dropped

            # 进度条
            p = self.progress
            state = (p['visible'], round(p['value'], 2), p['label'])
            if state != last_progress or self.busy != last_busy:
                progress_elems['row'].set_visibility(p['visible'] or self.busy)
                progress_elems['bar'].set_value(p['value'])
                progress_elems['label'].set_text(p['label'] or '处理中…')
                last_progress = state

            # 忙碌状态切换时统一锁定/解锁控件
            if self.busy != last_busy:
                for el in lockables:
                    el.disable() if self.busy else el.enable()
                last_busy = self.busy

        ui.timer(LOG_REFRESH_INTERVAL, poll)

    # ---- 事件处理（事件循环侧） ----

    def _acquire_busy(self) -> bool:
        if self.busy:
            ui.notify('已有任务进行中，请稍候…', type='warning')
            return False
        self.busy = True
        self.progress = {'visible': False, 'value': 0.0, 'label': ''}
        return True

    def _release_busy(self) -> None:
        self.busy = False
        self.progress['visible'] = False

    def _progress_cb(self, fraction: float, label: str) -> None:
        """供工作线程回调，只做无锁字段赋值（GIL 下安全）。"""
        self.progress['visible'] = True
        self.progress['value'] = max(0.0, min(1.0, fraction))
        self.progress['label'] = label

    async def _on_single(self, value: str) -> None:
        if not self._acquire_busy():
            return
        try:
            await run.io_bound(search.search_single, self.db, value)
        except Exception as e:
            self._report_error(e)
        finally:
            self._release_busy()

    async def _on_batch(self, value: str) -> None:
        if not self._acquire_busy():
            return
        try:
            result = await run.io_bound(search.search_batch, self.db, value.splitlines())
            rows = [{'isbn': isbn, 'result': '存在' if ok else '不存在'}
                    for isbn, ok in result.items]
            table = getattr(self, '_result_table', None)
            if table is not None:
                table.rows = rows
            if result.total:
                ui.notify(f'查询完成：存在 {result.found_count}，不存在 {result.missing_count}',
                          type='positive')
        except Exception as e:
            self._report_error(e)
        finally:
            self._release_busy()

    async def _on_clean_upload(self, e) -> None:
        if not self._acquire_busy():
            return
        try:
            content = await e.file.read()
            await run.io_bound(search.batch_clean, self.db,
                               [(e.file.name, content)], self._progress_cb)
            ui.notify('批量清洗完成，结果已写入 output/ 目录', type='positive')
        except Exception as e:
            self._report_error(e)
        finally:
            self._release_busy()

    async def _on_update_upload(self, e) -> None:
        if not self._acquire_busy():
            return
        try:
            content = await e.file.read()
            added = await run.io_bound(self._task_update, e.file.name, content)
            ui.notify(f'数据库更新完成：新增 {added} 条', type='positive')
        except Exception as e:
            self._report_error(e)
        finally:
            self._release_busy()

    async def _on_create_upload(self, e) -> None:
        files = list(getattr(e, 'files', None) or [])
        if not files:
            return
        if not self._acquire_busy():
            return
        try:
            payloads = [(f.name, await f.read()) for f in files]
            target = await run.io_bound(self._task_create, payloads)
            ui.notify(f'数据库创建完成：{target}', type='positive')
            ui.navigate.to('/')
        except Exception as e:
            self._report_error(e)
        finally:
            self._release_busy()

    async def _switch_database(self, path: str) -> None:
        if not path or path == self.db.path:
            return
        if not self._acquire_busy():
            return
        try:
            await run.io_bound(self.db.switch, path)
            Global_logger.append(f'已切换数据库：{path}')
            ui.navigate.to('/')
        except Exception as e:
            self._report_error(e)
        finally:
            self._release_busy()

    async def _export_results(self, rows) -> None:
        rows = [dict(r) for r in (rows or [])]
        if not rows:
            ui.notify('没有可导出的结果', type='warning')
            return
        try:
            path = await run.io_bound(write_result_csv, rows)
            ui.notify(f'结果已导出：{path}', type='positive')
        except Exception as e:
            self._report_error(e)

    async def _export_log(self) -> None:
        try:
            path = await run.io_bound(Global_logger.export, 'log.txt')
            ui.notify(f'日志已导出：{path}', type='positive')
        except Exception as e:
            self._report_error(e)

    async def _close_app(self) -> None:
        Global_logger.append('应用关闭')
        try:
            nicegui_app.shutdown()
        except Exception:
            import os
            os._exit(0)

    # ---- 工作线程任务（阻塞式，勿在事件循环直接调用） ----

    def _task_create(self, payloads: list[tuple[str, bytes]]) -> str:
        isbns = parse_isbns_from_files(payloads)
        if not isbns:
            Global_logger.append('没有从文件中解析到 ISBN 数据，未创建数据库。')
            raise ValueError('没有解析到 ISBN 数据')
        stem = payloads[0][0].rsplit('.', 1)[0]
        target = f'{stem}.db'
        Global_logger.append(f'正在创建数据库 {target}，共 {len(isbns)} 条，请稍候…')
        self.db.create(isbns, path=target, progress=self._progress_cb)
        Global_logger.append(f'数据库创建完成：{target}（共 {len(isbns)} 条）')
        return target

    def _task_update(self, name: str, content: bytes) -> int:
        df = read_table(name, content)
        col = pick_isbn_column(df)
        if col is None:
            raise ValueError(f'文件 {name} 缺少 ISBN/标准号/ISBN标准号 列')
        isbns = clean_isbn_values(df[col])
        Global_logger.append(f'正在更新数据库（{name}：{len(isbns)} 条），请稍候…')
        added = self.db.update(isbns, progress=self._progress_cb)
        Global_logger.append(f'数据库更新完成：读取 {len(isbns)} 条，新增 {added} 条。')
        return added

    # ---- 杂项 ----

    def _list_db_files(self) -> list[str]:
        names = sorted(str(p) for p in Path('.').glob('*.db'))
        if self.db.path not in names:
            names.insert(0, self.db.path)
        return names or ['ISBN.db']

    @staticmethod
    def _report_error(e: Exception) -> None:
        Global_logger.append(f'任务失败：{e}')
        ui.notify(f'任务失败：{e}', type='negative')
