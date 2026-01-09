from nicegui import ui
from starlette.formparsers import MultiPartParser
from App_Logger import Global_logger
from App_search import SearchEngine
from App_core import ISBN_Database
from nicegui.elements.upload import UploadEventArguments
from nicegui.elements.upload import MultiUploadEventArguments
import sys
import os
import tempfile
MultiPartParser.spool_max_size = 1024 * 1024 * 50


# 如果被 PyInstaller 打包（frozen），尝试把 sys.argv[0] 指向实际的脚本文件（明文）
if getattr(sys, 'frozen', False):
    # 优先从 sys._MEIPASS（PyInstaller 解压目录）寻找 main.py
    meipass = getattr(sys, '_MEIPASS', None)
    if meipass:
        candidate = os.path.join(meipass, 'main.py')
        if os.path.exists(candidate):
            sys.argv[0] = candidate
    else:
        # 回退：把当前 __main__ 的源写入临时文件并指向它（尽量避免，inspect 在 frozen 下可能失败）
        try:
            import inspect
            src = inspect.getsource(sys.modules['__main__'])
            fd, path = tempfile.mkstemp(suffix='.py', text=True)
            with os.fdopen(fd, 'w', encoding='utf-8') as f:
                f.write(src)
            sys.argv[0] = path
        except Exception:
            pass


class Aplication:
    def get_db_files(self):
        import os
        db_files = [f for f in os.listdir('.') if f.endswith('.db')]
        return db_files if db_files else ['ISBN.db']

    def __init__(self):
        Global_logger.append("应用启动")
        self.database = ISBN_Database()

    def __enter__(self):
        if not self.database.is_exist():
            self._build_init_ui()
            Global_logger.append("初始化中...请上传文件创建数据库")
        else:
            self._build_main_ui()
            Global_logger.append("数据库已存在，进入主界面")
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        Global_logger.append("应用关闭")

    def _build_init_ui(self) -> None:
        """初始化页面
        """
        ui.markdown('# **初始化**').style("align-self:center;")
        ui.markdown('## **数据库初始化界面**')
        ui.markdown(
            '### **上传 .xlsx 或 .csv 文件以创建数据库：**')
        ui.upload(
            multiple=True,
            on_upload=self._setup_database,
            on_multi_upload=self._setup_database_multi,
            label='单或多传文件，必须一致格式'
        ).style("font-size:20px;")
        ui.separator().style("width:800px; margin-top:10px; margin-bottom:10px;")

        ui.markdown('## **运行日志**').style("align-self:center;")
        with ui.scroll_area() as scroll_area:
            self.text = ui.markdown('').style(
                "white-space: pre-wrap; font-size:16px;")
            self.text.set_content(Global_logger.return_logs())

            def update_logs(logs):
                self.text.set_content(logs)
                scroll_area.scroll_to(percent=1.0)
            Global_logger.register_callback(update_logs)
            self.text.props('readonly')

        ui.separator().style("flex-grow:1;")

        with ui.row().style('margin-top:20px; justify-content: flex-start; gap: 20px;'):
            ui.button('清空日志', on_click=Global_logger.clear).style(
                'font-size: 16px; padding: 10px 20px;')
            ui.button('导出日志', on_click=Global_logger.get_all).style(
                'font-size: 16px; padding: 10px 20px;')

        ui.separator().style("flex-grow:1;")

        ui.button('关闭应用', on_click=self._close_app).style(
            'position: fixed; bottom: 20px; right: 20px; font-size: 16px; padding: 10px 20px;')
        ui.run(reload=False, title='ISBN查重系统 - 初始化界面')

    def _build_main_ui(self) -> None:
        """主页面"""
        ui.markdown('# **ISBN 查重系统**').style("align-self:center;")

        # 自动扫描数据库文件并下拉选择
        db_files = self.get_db_files()
        db_select = ui.select(db_files, value=self.database.path, label='选择数据库文件').style(
            'width:300px; margin-bottom:20px;')
        ui.button('切换数据库', on_click=lambda: self._switch_database(
            db_select.value)).style('margin-left:10px;') # type: ignore

        ui.separator().style("width:800px; margin-top:10px; margin-bottom:10px;")

        with ui.tabs() as main_tabs:
            tab1 = ui.tab('单次查询').style(
                "align-self:center; font-size: 20px; min-width: 150px;")
            tab2 = ui.tab('批量查询').style(
                "align-self:center; font-size: 20px; min-width: 150px;")
            tab3 = ui.tab('更新数据库').style(
                "align-self:center; font-size: 20px; min-width: 150px;")
            tab4 = ui.tab('新建数据库').style(
                "align-self:center; font-size: 20px; min-width: 150px;")

        with ui.tab_panels(main_tabs, value=tab1):
            with ui.tab_panel(tab1):
                ui.markdown('## **单次**')
                text_in_single = ui.input(
                    '请输入 ISBN，可带连字符或空格：', placeholder='978-7-111-12345-6').style("width:400px;")
                ui.button('查询', on_click=lambda: self._on_single_check(
                    text_in_single.value)).style("font-size:22px; margin-left:10px;")

            with ui.tab_panel(tab2):
                ui.markdown('## **批量**')
                with ui.tabs() as sub_tabs:
                    sub_tab1 = ui.tab('输入框输入')
                    sub_tab2 = ui.tab('上传文件')
                with ui.tab_panels(sub_tabs, value=sub_tab1):
                    with ui.tab_panel(sub_tab1):
                        ui.markdown('### **请输入多个 ISBN，每行一个，可带连字符或空格：**')
                        batch_textarea = ui.textarea(
                            placeholder='978-7-111-12345-6\n978 7 111 12345 7\n9787111123458').style("width:600px; height:300px;")
                        ui.button('查询', on_click=lambda _: self._on_batch_check(
                            batch_textarea.value)).style("font-size:22px; margin-top:10px;")
                    with ui.tab_panel(sub_tab2):
                        ui.markdown(
                            '### **上传包含 ISBN 列的 .xlsx 或 .csv 文件，去重并返回**')
                        ui.upload(auto_upload=True, on_upload=self._send_to_query).style(
                            "font-size:20px;").classes('max-w-full').props('accept=".xlsx,.csv,.xls"')

            with ui.tab_panel(tab3):
                ui.markdown('## **更新数据库**')
                ui.markdown('### **上传 .xlsx 或 .csv 文件，系统自动更新：**')
                ui.upload(auto_upload=True, multiple=True,
                          on_upload=self._send_to_update).style("font-size:20px;")

            with ui.tab_panel(tab4):
                ui.markdown('# **新建数据库**').style("align-self:center;")
                ui.markdown('### **上传 .xlsx 或 .csv 文件以创建数据库：**')
                ui.upload(multiple=True, on_upload=self._setup_database,
                          on_multi_upload=self._setup_database_multi,
                          label='单或多传文件，必须一致格式').style("font-size:20px;")

        ui.separator().style("width:800px; margin-top:10px; margin-bottom:10px;")

        ui.markdown('## **运行日志**').style("align-self:center;")
        with ui.scroll_area() as scroll_area:
            self.text = ui.markdown('').style(
                "white-space: pre-wrap; font-size:16px;")
            self.text.set_content(Global_logger.return_logs())

            def update_logs(logs):
                self.text.set_content(logs)
                scroll_area.scroll_to(percent=1.0)
            Global_logger.register_callback(update_logs)
            self.text.props('readonly')

        ui.separator().style("flex-grow:1;")

        with ui.row().style('margin-top:20px; justify-content: flex-start; gap: 20px;'):
            ui.button('清空日志', on_click=Global_logger.clear).style(
                'font-size: 16px; padding: 10px 20px;')
            ui.button('导出日志', on_click=Global_logger.get_all).style(
                'font-size: 16px; padding: 10px 20px;')

        ui.separator().style("flex-grow:1;")

        ui.button('关闭应用', on_click=self._close_app).style(
            'position: fixed; bottom: 20px; right: 20px; font-size: 16px; padding: 10px 20px;')

        ui.run(reload=False, title='ISBN查重系统')

    def _switch_database(self, db_path: str):
        self.database = ISBN_Database(db_path)
        Global_logger.append(f"已切换数据库：{db_path}")
        ui.navigate.to('/')

    def _on_single_check(self, value: str) -> None:
        SearchEngine.search_single(self.database, value) # pyright: ignore[reportArgumentType]

    def _on_batch_check(self, value: str) -> None:
        isbn_list = value.splitlines()
        SearchEngine.search_batch(self.database, isbn_list) # type: ignore

    async def _send_to_query(self, event) -> None:
        files = getattr(event, 'files', None) or [getattr(event, 'file', None)]
        files = [f for f in files if f]
        await SearchEngine.batch_clean(self.database, files) # type: ignore

    async def _send_to_update(self, event) -> None:
        await self.database.update_database(single=event.file)

    async def _setup_database(self, event: UploadEventArguments):
        await self.database.create_database(single=event.file)

    async def _setup_database_multi(self, event: MultiUploadEventArguments):
        await self.database.create_database(multi=event.files)

    async def _close_app(self):
        if Global_logger:
            Global_logger.clear()
        import asyncio
        ui.run_javascript('window.close();')
        await asyncio.sleep(1.5)
        try:
            import signal
            os.kill(os.getpid(), signal.SIGINT)
        except Exception:
            os._exit(0)


if __name__ == "__main__":
    app = Aplication()
    if not app.database.is_exist():
        app._build_init_ui()
        Global_logger.append("初始化中...请上传文件创建数据库")
    else:
        app._build_main_ui()
        Global_logger.append("数据库已存在，进入主界面")
