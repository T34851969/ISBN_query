
# ISBN查重系统

本项目是一个基于 Python、SQLite 和 NiceGUI 的本地图书馆 ISBN 查重系统，支持单次和批量查重，适合馆藏数据比对和管理。浏览器访问本地 Web 界面，所有数据均保存在本机，不上传任何外部服务。

## 功能简介

- **ISBN 检索**
  - 单次查询：输入一个 ISBN（可带连字符或空格），即时返回是否存在
  - 批量查询：文本框粘贴多行或上传文件，结果以分页表格展示（存在/不存在），可一键导出 CSV
  - 批量清洗：上传含 ISBN 列的表格，自动剔除库中已存在的行，整行结果导出到 `output/` 目录
- **数据库管理**
  - 首次运行从 `.xlsx` / `.csv` 文件创建本地 SQLite 数据库
  - 后续可上传文件增量更新数据库（自动去重，提示实际新增条数）
  - 支持创建、切换多个 `.db` 数据库文件
- **日志记录**：全局运行日志（自动保留最近 2000 条），支持查看、清空和导出
- **附带工具**：`tools/excel_dedup.py` —— 独立的 Excel/CSV ISBN 去重小工具（Tkinter 界面）

## 技术栈

- Python 3.11+
- pandas + python-calamine：数据处理；Excel 读取统一走 calamine 引擎（大文件比 openpyxl 快一个量级）
- sqlite3：本地数据库（持久连接 + 线程锁）
- NiceGUI：Web 界面
- fastapi / uvicorn：文件上传接口与 Web 服务
- threading：耗时任务在工作线程执行，界面不卡死

## 安装与运行

1. **克隆仓库**

   ```bash
   git clone https://github.com/T34851969/ISBN_query.git
   cd ISBN_query
   ```

2. **安装依赖**（建议使用虚拟环境）

   ```bash
   pip install -r requirements.txt
   ```

3. **运行**

   ```bash
   python main.py
   ```

   启动后浏览器访问 <http://localhost:8080>（首次运行会自动打开）。

## 使用说明

**首次使用**：程序启动后进入初始化页，上传一个或多个格式一致的 `.xlsx` / `.csv` 文件即可创建数据库，完成后自动进入主界面。

**输入格式**：上传文件需包含 ISBN 列，列名支持 `ISBN`、`标准号`、`ISBN标准号`（单列文件可用任意列名）。系统会自动清洗数据：去除连字符与空格、修正 Excel 数值单元格产生的浮点尾差（如 `9787111123458.0`）、剔除空行与残留表头行。

| 标签页 | 用途 |
| --- | --- |
| 单次查询 | 输入单个 ISBN 查重 |
| 批量查询 → 输入框输入 | 粘贴多行 ISBN 批量查重，结果进分页表格，可导出 CSV |
| 批量查询 → 上传文件 | 上传表格文件，剔除库中已有行，整行结果写入 `output/` |
| 更新数据库 | 上传表格文件，把其中的 ISBN 并入当前库 |
| 新建数据库 | 另建一个新的 `.db` 数据库（默认以首个上传文件名命名） |
| 选择数据库 | 切换工作目录下的其他 `.db` 文件 |

所有查询与导入产物（清洗结果、批量查询导出 CSV）统一保存在 `output/` 目录，文件名带时间戳，不会覆盖历史文件；日志可导出为 `log.txt`。

## 附带工具

`tools/excel_dedup.py` 是独立的 Excel/CSV ISBN 去重小工具（Tkinter 界面）：

```bash
python tools/excel_dedup.py
```

- 依赖工作目录下的 `isbn_records.db`（`isbn_records` 表，`isbn` 列）
- 模糊识别 ISBN 列（列名含 `isbn` 或 `书号`）
- 剔除库中已有的行后，输出 `原文件名_dedup.xlsx`（csv 输入则输出 `_dedup.csv`）
- 去重在大文件上为秒级（向量化过滤），处理过程在后台线程执行，界面保持响应

## 项目结构

```
ISBN_query/
├── main.py                  # 程序入口（含 PyInstaller frozen 兼容处理）
├── app/
│   ├── logger.py            # 全局日志：有界缓冲 + 节流增量刷新
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

1. **事件循环阻塞**：Excel 解析、全量入库等重活曾直接跑在 NiceGUI 事件循环上，执行期间整个界面冻结。现在所有耗时操作经 `run.io_bound` 卸载到工作线程，运行期间控件自动禁用并显示进度条（建库/更新按 5 万行分块回报进度）。
2. **日志回调风暴**：批量查询曾逐条刷新日志，每条触发全量重渲染 + WebSocket 推送（O(n²)）。现在日志只记摘要，逐条结果一次性写入分页结果表；日志面板由定时器节流刷新并增量推送，自动保留最近 2000 条。

实测参考（30 万行数据）：解析 1.35s、建库 0.5s、万条批量查询 0.02s；旧版生成的 `.db` 文件可直接使用（表结构完全兼容）。

## 打包发布

本地打包（在与目标平台一致的操作系统上执行）：

```bash
pip install -r requirements.txt
python -m PyInstaller --noconfirm --clean isbn_query.spec
```

产物：Windows 为 `dist/ISBNQuery.exe`（双击运行后浏览器访问本地端口）；Linux 为 `dist/ISBNQuery`（`./ISBNQuery` 启动后浏览器访问本地端口）。

也可推送代码由 GitHub Actions 自动构建：

- `.github/workflows/build-windows.yml` → `ISBNQuery-windows-x64.zip`
- `.github/workflows/build-linux.yml` → `ISBNQuery-linux-x64.tar.gz`（构建于 Ubuntu 22.04，glibc ≥ 2.35，兼容 Debian 12/13 等发行版；CI 内含启动冒烟测试）

产物在 Actions Artifacts 下载；打 `v*.*.*` 标签时自动发布到 GitHub Releases（两个平台产物并入同一 Release）。

## 常见问题

- **旧版创建的数据库还能用吗？** 能。新版表结构与旧版完全一致（`ISBN_table` + `idx_isbn`），旧 `.db` 放到程序工作目录即可在下拉框中选择。
- **默认端口被占用？** 8080 端口被占用时启动会报错，可在 `main.py` 的 `ui.run(...)` 中加 `port=xxxx` 更换。
- **上传文件有大小限制吗？** 单文件超过 1MB 会自动转存临时文件处理，无硬性上限；超大文件建议先拆分。
- **查询结果显示在哪？** 批量查询结果显示在"查询结果"表格（分页），需要留存时点"导出结果（CSV）"。

## 许可证

供个人学习和办公使用
