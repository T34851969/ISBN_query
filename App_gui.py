from fastapi import UploadFile
from nicegui import ui
import os
import signal
import asyncio
from starlette.formparsers import MultiPartParser
from App_Logger import Global_logger as Logger

MultiPartParser.spool_max_size = 1024 * 1024 * 50



class Aplication:
    def __init__(self):
        self.logger = Logger

    def _build_ui(self) -> None:
        ui.markdown('# **ISBN 查重系统**').style("align-self:center;")

        with ui.tabs as main_tabs:
            tab1 = ui.tab('单次查询').style("align-self:center;")
            tab2 = ui.tab('批量查询').style("align-self:center;")
            tab3 = ui.tab('更新数据库').style("align-self:center;")
        main_tabs.style('font-size: 20px; min-width: 150px;')

        with ui.tab_panels(main_tabs, value=tab1):
            with ui.tab_panel(tab1):
                ui.markdown('## **单次**')
                ui.input('请输入 ISBN，可带连字符或空格：', placeholder='978-7-111-12345-6').style(
                    "width:400px;")
                ui.button('查询', on_click=self._on_single_check).style(
                    "font-size:22px; margin-left:10px;")

            with ui.tab_panel(tab2):
                ui.markdown('## **批量**')

                with ui.tabs() as sub_tabs:
                    sub_tab1 = ui.tab('输入框输入')
                    sub_tab2 = ui.tab('上传文件')

                with ui.tab_panels(sub_tabs, value=sub_tab1):
                    with ui.tab_panel(sub_tab1):
                        ui.markdown('### **请输入多个 ISBN，每行一个，可带连字符或空格：**')
                        ui.textarea(placeholder='978-7-111-12345-6\n978 7 111 12345 7\n9787111123458').style(
                            "width:600px; height:300px;")
                        ui.button('查询', on_click=self._on_batch_check).style(
                            "font-size:22px; margin-top:10px;")
                    with ui.tab_panel(sub_tab2):
                        ui.markdown(
                            '### **上传包含 ISBN 列的 .xlsx 或 .csv 文件，系统自动去重：**')
                        ui.upload(auto_upload=True, on_upload=self._send_to_query).style(
                            "font-size:20px;").classes('max-w-full').props('accept=".xlsx,.csv,.xls"')

            with ui.tab_panel(tab3):
                ui.markdown('## **更新数据库**')
                ui.upload(auto_upload=True, on_upload=self._send_to_update).style(
                    "font-size:20px;")
                ui.button('更新', on_click=self._update_db).style(
                    "font-size:22px;")

        ui.markdown('## **运行日志**').style("align-self:center;")
        self.log_area = ui.textarea(value=self.logger.get_all(), ).style(
            "width:800px; height:300px;")
        self.log_area.readonly = True

        ui.button('清空日志', on_click=self.logger.clear).style(
            'position: fixed; bottom: 20px; left: 20px; font-size: 16px; padding: 10px 20px;')
        ui.button('导出日志', on_click=self.logger.get_all).style(
            'position: fixed; bottom: 60px; left: 20px; font-size: 16px; padding: 10px 20px;')
        ui.button('关闭应用', on_click=self._close_app).style(
            'position: fixed; bottom: 20px; right: 20px; font-size: 16px; padding: 10px 20px;')

    def _on_new_log(self, text: str) -> None:
        try:
            self.logger.append(text)  # 记录日志到 Logger
            if self.log_area:
                self.log_area.value = self.logger.get_all()  # 刷新日志显示
                ui.run_javascript(
                    "var t = document.querySelector('textarea[readonly]'); if (t) { t.scrollTop = t.scrollHeight; }")
        except Exception:
            pass

    def _on_single_check(self, value: str) -> None:
        pass

    def _on_batch_check(self, value: str) -> None:
        pass

    def _send_to_query(self, file: UploadFile) -> None:
        pass

    def _send_to_update(self, file: UploadFile) -> None:
        pass
    
    async def _close_app(self):
        ui.run_javascript('window.close();')
        await asyncio.sleep(0.8)
        try:
            os.kill(os.getpid(), signal.SIGINT)
        except Exception:
            os._exit(0)