
# ISBN查重系统

本项目是一个基于 Python、SQLite 和 NiceGUI 的本地图书馆ISBN查重系统，支持单次和批量查重，适合馆藏数据比对和管理。

## 功能简介

- **ISBN检索**
  - 支持单个ISBN输入查重
  - 支持批量ISBN查重（输入框或上传文件）
- **数据库管理**
  - 首次运行可自动从 `.xlsx` 或 `.csv` 文件创建本地SQLite数据库
  - 支持后续上传文件批量更新数据库
- **日志记录**
  - 全局日志记录，支持查看、清空和导出
- **界面**
  - 提供基于 NiceGUI 的Web界面，支持文件上传、查重、数据库更新等操作

## 技术栈

- Python 3.14
- pandas：数据处理与文件读取。包括 pandas-calamine
- sqlite3：本地数据库
- nicegui：Web界面
- fastapi：文件上传接口
- asyncio、threading：异步与线程安全

## 安装与运行

1. **克隆仓库**

   ```bash
   git clone https://github.com/T34851969/ISBN_query.git
   ```

2. **安装依赖**

   ```bash
   pip install pandas nicegui fastapi
   ```

3. **准备数据**
   - 将 `.xlsx` 或 `.csv` 文件放在项目根目录，作为初始馆藏数据

4. **运行项目**

   ```bash
   python main.py
   ```

   - 启动后访问本地Web界面进行查重和管理

## 主要文件说明

- [`main.py`](main.py )：程序入口，初始化数据库和界面
- [`App_core.py`](App_core.py )：数据库管理与初始化
- [`App_search.py`](App_search.py )：ISBN查重逻辑（单次/批量）
- [`App_gui.py`](App_gui.py )：Web界面与交互逻辑
- [`App_Logger.py`](App_Logger.py )：全局日志记录器

## 结构

```项目结构
ISBN_query/
├── App_core.py
├── App_gui.py
├── App_Logger.py
├── App_search.py
├── main.py
├── README.md
├── .gitignore
```

## 许可证

仅供个人学习和办公使用，禁止商业用途。如有建议欢迎反馈。
