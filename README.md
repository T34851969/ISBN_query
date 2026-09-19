
# ISBN查重系统

本项目是一个基于 Python、SQLite 和 NiceGUI 的本地图书馆ISBN查重系统，支持单次和批量查重，适合馆藏数据比对和管理。

## 功能简介

- **ISBN检索**
  - 支持单个ISBN输入查重
  - 支持批量ISBN查重（输入框或上传文件），结果以分页表格展示，可导出 CSV
  - 支持上传文件批量清洗：剔除库中已存在的行，整行结果导出到 `output/` 目录
- **数据库管理**
  - 首次运行可自动从 `.xlsx` 或 `.csv` 文件创建本地SQLite数据库
  - 支持后续上传文件批量更新数据库（提示实际新增条数）
  - 支持切换/新建多个 `.db` 数据库文件
- **日志记录**
  - 全局日志记录（自动保留最近 2000 条），支持查看、清空和导出
- **附带工具**
  - `tools/excel_dedup.py`：独立的 Excel/CSV ISBN 去重小工具（基于 isbn_records.db）

## 技术栈

- Python 3.11+
- pandas + pandas-calamine：数据处理；Excel 读取统一走 calamine 引擎（大文件快一个量级）
- sqlite3：本地数据库（持久连接 + 线程锁）
- nicegui：Web界面
- fastapi / uvicorn：文件上传接口与 Web 服务
- threading：耗时任务在工作线程执行，界面不卡死

## 安装与运行

1. **克隆仓库**

   ```bash
   git clone https://github.com/T34851969/ISBN_query.git
   ```

2. **安装依赖**

   ```bash
   pip install -r requirements.txt
   ```

3. **运行项目**

   ```bash
   python main.py
   ```

   - 启动后浏览器访问本地 Web 界面（默认 <http://localhost:8080>）进行查重和管理

## 项目结构

```
ISBN_query/
├── main.py                  # 程序入口（含 PyInstaller frozen 兼容处理）
├── app/
│   ├── logger.py            # 全局日志：有界缓冲 + 节流刷新（根治日志刷屏卡顿）
│   ├── database.py          # 数据库管理：持久连接、分块入库、进度回报
│   ├── search.py            # 查询引擎：单次/批量/清洗（同步纯函数，供线程池调用）
│   ├── excel_io.py          # 表格读写与 ISBN 列清洗（calamine 引擎、输出到 output/）
│   └── ui.py                # NiceGUI 界面与事件处理（重活全部卸载到工作线程）
├── tools/
│   └── excel_dedup.py       # 独立的 Excel/CSV ISBN 去重小工具（Tkinter）
├── isbn_query.spec          # PyInstaller 打包配置
└── requirements.txt
```

## 性能设计（2026-09 重构）

旧版卡死的两个根因及对应修复：

1. **事件循环阻塞**：Excel 解析、全量入库等重活曾直接跑在 NiceGUI 事件循环上，
   执行期间整个界面冻结。现在所有耗时操作经 `run.io_bound` 卸载到工作线程，
   运行期间控件自动禁用并显示进度条（建库/更新按 5 万行分块回报进度）。
2. **日志回调风暴**：批量查询曾逐条刷新日志，每条触发全量重渲染 + WebSocket 推送
   （O(n²)）。现在日志只记摘要，逐条结果一次性写入分页结果表；日志面板由定时器
   节流刷新并增量推送，自动保留最近 2000 条。

## 打包 Windows exe

在 Windows 环境执行（也可推送到 GitHub 由 `.github/workflows/build-windows.yml` 自动构建）：

```bash
pip install -r requirements.txt
python -m PyInstaller --noconfirm --clean isbn_query.spec
```

产物为 `dist/ISBNQuery.exe`，双击运行后浏览器访问本地端口。

## 许可证

供个人学习和办公使用
