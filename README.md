# ISBN查重系统

一个基于 Python 和 SQLite 的本地图书馆馆藏查重系统，基于馆藏ISBN条目对输入的条目进行查重，支持单次与批量查重。适用于馆藏拉取比对。

## 功能

- **检索**
  - 仅支持ISBN输入
  - 支持直接输入ISBN
  - 支持导入.txt文件进行批量查询
- **自动建库**
  - 首次运行时从 .xlsx 或 .csv 文件创建SQLite库
- **输出**
  - 单次查询，直接在终端输出“存在”/“不存在”
  - 批量查询，输出“重复项.txt”与“新项.txt”，前者保存重复的项目，后者保存馆藏不包含的项目

## 技术栈

- Python 3.7+
- `pandas`：数据处理与读取文件(.csv与.xlsx)
- `sqlite3`：本地数据库
- `tkinter`：图形界面，仅用于拉取选择文件界面(Python 3.7+自带)

> 本项目推荐在本地运行

## 安装与运行

### 1. 克隆仓库

```bash
git clone https://github.com/T34851969/ISBN_query.git
```

### 2. 安装与依赖

```bash
pip install pandas openpyxl
```

- 另请自行删除不需要的部分

### 3. 准备数据

- 将 .xlsx 文件放在项目根目录，支持读取多个，并读取全部工作表。仅支持.xlsx

### 4. 运行

```bash
python main.py
```

> 或者，打开VS Code，在安装Python之后直接运行

## 界面

- 命令行终端，输出：“请选择：\n 1、搜索 \n 2、批量查询 \n 3、退出\n > ”
- 若选择 2 ，则启动一个文件选择界面

## 项目结构

```结构
- ISBN_query  # 工作目录
  - FileMgr  # 存放交互组件
    - __init__.py  # 初始化文件
    - del_files.py  # 自定义删除功能，用于删除临时文件
    - engage.py  # 本工作夹主模块
    - read_xl.py  # 读取.xlsx文件
    - scan_xl.py  # 提供根目录下.xlsx文件目录
    - wash_xl.py  # 数据清洗与输出，输出.csv文件
  - __init__.py
  - db.py  # 数据库文件
  - main.py  # 主函数，启动程序
  - menu.py  # 交互逻辑文件
  - ISBN数据库.db(如果你创建了)
  - .gitgnore
  - (自选xlsx文件)
  - (临时生成的.csv文件)
```

## 许可证

- 本项目是个人学习办公编程的阶段性作品，如有不足请多关照。可供个人学习，不用于商业用途
