# ISBN查重系统（C# 重构版）

基于 **.NET 10 + WinForms + SQLite** 的本地图书馆 ISBN 查重系统，Windows 专用。
由 Python/NiceGUI 版（[T34851969/ISBN_query](https://github.com/T34851969/ISBN_query)）重构而来：
发布形态改为**自包含单文件 exe**（免安装、无浏览器、无端口占用），功能与旧 `.db` 数据完全兼容。

## 功能

- **单次查询**：输入单个 ISBN（可带连字符/空格），即时返回存在与否
- **批量查询**：文本框粘贴多行批量比对，结果表格可一键导出 CSV（utf-8-sig）
- **批量清洗**：上传含 ISBN 列的表格，剔除库中已有的行，**整行**结果写入 `output/`
- **数据库管理**：从 `.xlsx/.xls/.csv` 建库、增量更新（提示实际新增条数）、多库切换
- **日志**：环形缓冲 2000 条，查看/清空/导出 `log.txt`
- **Windows 原生特性**：文件拖拽导入、任务栏进度条、高 DPI、记住上次数据库（`config.json`）

## 使用说明

1. 双击 `ISBNQuery.exe`（首次启动会把内嵌的原生库解压到 `%TEMP%\.net`，属预期行为）。
2. 首次使用在初始化面板选择 `.xlsx/.xls/.csv` 文件（或直接拖到窗口上）创建数据库。
3. 数据文件全部在 exe 所在目录：`*.db`、`output/`、`log.txt`、`config.json`——整体拷贝即迁移。
4. 旧 Python 版创建的 `.db` 直接放到 exe 目录即可在下拉框中选择（表结构完全一致：`ISBN_table` + `idx_isbn`）。

## 项目结构

```
ISBNQuery.sln
├── src/ISBNQuery.Core/            # UI 无关的类库（Linux 上可直接跑单元测试）
│   ├── IsbnDatabase.cs            # SQLite：长连接+锁、5 万行分块入库、Temp_ISBN 增量并入
│   ├── TableIo.cs                 # ExcelDataReader 读取（xlsx SAX/xls BIFF）、MiniExcel 写出、ISBN 清洗
│   ├── SearchService.cs           # 单查/批查/清洗
│   ├── RingLogger.cs              # 环形日志（2000 条）+ 版本号增量刷新
│   ├── AppPaths.cs                # 数据目录与配置
│   └── SelfTest.cs                # --selftest 无 UI 冒烟（CI 用）
├── src/ISBNQuery/                 # WinForms 界面（net10.0-windows）
│   ├── MainForm.cs                # 四标签页 + 日志面板 + 进度条 + busy 锁定
│   ├── TaskbarProgress.cs         # ITaskbarList3 任务栏进度
│   └── Program.cs
├── tests/ISBNQuery.Core.Tests/    # xUnit（含旧 .db 兼容、IN 分批 >500 等）
├── bench/IsbnBench/               # 30 万行性能基准（对比 Python 版）
└── .github/workflows/build-windows.yml
```

## 开发与构建

需要 **.NET 10 SDK**（Windows 目标必需；Linux 上可只跑 `ISBNQuery.Core` 单元测试）。
SDK 需在 `PATH` 中，或用 `DOTNET` 环境变量指向 `dotnet.exe`。

```cmd
scripts\build.cmd test       REM 单元测试
scripts\build.cmd publish    REM 出单文件 dist\ISBNQuery.exe
scripts\build.cmd selftest   REM Core 全链路冒烟（PASS/FAIL 退出码）
scripts\build.cmd bench      REM 30 万行性能基准
scripts\build.cmd clean      REM git clean -x（保留 dist/）
```

基准（8 vCPU / 16 GB）：解析 30 万行 xlsx 约 2.2 s / 建库约 1.1 s / 万条批量查询约 0.14 s。

## 发布

推送 `main` 后由 GitHub Actions（自托管 Windows runner，标签 `self-hosted, isbnq`）自动执行：
单元测试 → 单文件发布 → `--selftest` 冒烟 → 上传 `ISBNQuery.exe` 产物。

本机打包：`dotnet publish src/ISBNQuery/ISBNQuery.csproj -c Release`。产物约 40–60 MB
（自包含 .NET 运行时 + WinForms，已开压缩）。
## 与 Python 版的语义对应

| Python 版 | 本版 |
| --- | --- |
| calamine 引擎读表 | ExcelDataReader（xlsx SAX 流式 / xls BIFF），数值单元格直接按整数格式化 |
| `run.io_bound` + busy 标志 | `Task.Run` + `Progress<T>`，控件统一禁用 |
| 日志 0.3s 定时器轮询 + 版本号 | 同机制（`RingLogger` + WinForms Timer） |
| NiceGUI 浏览器界面（8080 端口） | WinForms 原生窗口（无端口依赖） |
| `tools/excel_dedup.py`（独立 Tkinter 工具，表结构 `isbn_records`） | 功能并入"批量查询→上传文件"，schema 统一为 `ISBN_table` |

清洗规则与旧版逐条对齐：候选列名 `ISBN`/`标准号`/`ISBN标准号`（单列文件任意列名）、去空格/连字符/浮点尾差、剔空行与表头残留行（`标准号`/`nan`）、IN 查询 500 参数分批、入库 5 万行分块回报进度。CSV 增强项：非 UTF-8 自动按 GBK 解码（旧版直接报错）。

一处有意的语义修正：旧版"更新数据库"的 `INSERT OR IGNORE` 在无 UNIQUE 约束的表上**并不去重**（重复上传会灌入重复行，"新增 N 条"实际是"插入 N 条"，与旧 README 承诺不符）。本版改为入库前先查库剔除已有行——表结构不变（旧 `.db` 兼容），"新增 N 条"从此是真实新增数。

## 验收清单（Windows 侧人工）

- [ ] 双击 exe 启动（无 .NET 运行时的机器）
- [ ] 初始化：选择文件建库 / 拖拽建库
- [ ] 单次查询（带连字符/空格/纯数字）
- [ ] 批量粘贴查询 → 结果表 → 导出 CSV
- [ ] 批量清洗 → `output/` 产物整行且 ISBN 已规范化
- [ ] 更新数据库 → "新增 N 条"计数正确
- [ ] 旧 Python 版 `.db` 可切换并正常查询
- [ ] 日志清空/导出
- [ ] `--selftest` 命令行冒烟输出 `SELFTEST PASS`

## 许可证

供个人学习和办公使用（沿用原项目约定）。
