"""交互界面"""
# 标准库文件
import tkinter as Tk
from tkinter import filedialog
# 自带库文件
from db import ISBN_database
from FileMgr import engage as eg
from FileMgr import del_files as delf

def user_menu():
    """查询ISBN号，支持精确查询"""
    print("图书馆馆藏ISBN号检索系统")
    print("=" * 30)

    ISBN_base = ISBN_database()
    if not ISBN_base.isDB():
        print("未找到数据库，从数据文件创建...")

        # 获取数据文件的路径
        data_path = eg.process()
        if not data_path:
            print("未找到数据文件，程序退出")
            return

        # 创建数据库，使用单个文件路径
        ISBN_base.create_db_ISBN(data_path)
        all_recs = ISBN_base.getAllRecs()
        print(f"已创建新数据库，条目总计: {all_recs}")
        # 删除使用过的临时文件，但保留提供的Excel文件
        try:
            delf.del_file(data_path)
        except UnboundLocalError:
            data_path = ""

    else:
        all_recs = ISBN_base.getAllRecs()
        print(f"已连接现有数据库，总记录数: {all_recs}")
        # 删除使用过的临时文件，但保留提供的Excel文件
        try:
            delf.del_file(data_path)
        except UnboundLocalError:
            data_path = ""
        
    while True:
        choice = input("请选择：\n 1、搜索 \n 2、批量查询 \n 3、退出\n > ")

        if choice == '1':
            ISBN_input = input("输入ISBN，可带间隔符：").strip()
            
            if not ISBN_input:
                print("无效输入")
                continue

            try:
                results = ISBN_base.search_single(ISBN_input)
                if results:
                    print("✅ 该书存在于图书馆内")
                else:
                    print("❌ 未找到此书在馆藏中的记录")
            except Exception as 错误:
                print(f"搜索出错：{str(错误)}")

        elif choice == '2':
            try:
                root = Tk()
                root.withdraw()  # 隐藏主窗口
                root.attributes('-topmost', True)  # 确保对话框置顶
                
                files_path = filedialog.askopenfilename(
                    title="选择ISBN列表文件",
                    filetypes=[("文本文件", "*.txt")]
                )
                
                root.destroy()  # 立即销毁窗口
                
                if not files_path:  # 用户取消选择
                    print("操作已取消")
                    continue
                
                # 读取文件内容
                with open(files_path, 'r', encoding='utf-8') as f:
                    ISBN_list = [line.strip() for line in f if line.strip()]
                    
                is_set = ISBN_base.search_batch(ISBN_list)
                
                repeated = []
                new_record = []
                for ISBN_ori in ISBN_list:
                    ISBN_progress = ISBN_ori.replace("-", "").replace(" ", "")
                    if ISBN_progress in is_set:
                        repeated.append(ISBN_ori)
                    else:
                        new_record.append(ISBN_ori)
                
                # 写入结果文件
                with open('重复项.txt', 'w', encoding='utf-8') as f:
                    f.write('\n'.join(repeated))
                with open('新项.txt', 'w', encoding='utf-8') as f:
                    f.write('\n'.join(new_record))
                    
                print(f"处理完成！找到重复项 {len(repeated)} 条，新项 {len(new_record)} 条")
                print(f"结果已保存至 重复项.txt 和 新项.txt")

            except FileNotFoundError:
                print("错误：文件不存在或路径错误")
            except Exception as e:
                print(f"处理过程中发生错误：{str(e)}")

        elif choice == '3':
            print("退出中...")
            break

        else:
            print("无效选择，请重新输入")
