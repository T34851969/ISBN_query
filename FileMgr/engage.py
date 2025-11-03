"""主处理函数，包括查找文件，数据清洗及转化，最终输出"""
import pandas as pd
from FileMgr import scan_xl
from FileMgr import wash_xl

def process():

    print("正在扫描文件...")
    all_files = scan_xl.scan()
    
    if not all_files:
        print("未找到Excel或CSV文件！")
        return
    
    print(f"\n共找到 {len(all_files)} 个文件")
    files_in = input("请输入要处理的文件序号（序号用空格隔开）: ")
    # —————— 主要处理部分 ——————
    try:
        file_sel = [int(x.strip()) - 1 for x in files_in.split()]
        selected = [all_files[i] for i in file_sel if 0 <= i < len(all_files)]
        
        if not selected:
            print("没有选择有效的文件！")
            return
        
        print(f"已选择 {len(selected)} 个文件:")
        for file in selected:
            print(f"  - {file}")
        
    except ValueError:
        print("输入格式错误！请输入数字序号，用空格隔开。")
        return
   
    wash_xl.wash(selected)
    return "ISBN.csv"
