"""主处理函数，包括查找文件，数据清洗及转化，最终输出"""
import pandas as pd
from FileMgr import scan_xl
from FileMgr import csv_read

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
    
    # 读取所有选中文件的数据
    print("\n正在读取文件数据...")
    rawISBN = []
    
    for file in selected:
        print(f"处理文件: {file}")
        rawData = csv_read.read(file)
        rawISBN.extend(rawData)
        print(f"  - 读取到 {len(rawData)} 条数据")
    
    print(f"\n总共读取到 {len(rawISBN)} 条原始数据")
    
    # 去重处理 - 使用集合去重，同时保持顺序
    seen = set()
    onlyISBN = []
    duplCount = 0
    
    for item in rawISBN:
        item_str = item.strip() if isinstance(item, str) else str(item)
        if item_str not in seen:
            seen.add(item_str)
            onlyISBN.append(item_str)
        else:
            duplCount += 1
    
    print(f"去重后剩余 {len(onlyISBN)} 条数据，移除 {duplCount} 条重复数据")
    o_name = "ISBN.csv"
    
    # 创建DataFrame
    df_output = pd.DataFrame({'ISBN': onlyISBN})
    df_output.to_csv(o_name, index=False, encoding='utf-8-sig')
    print(f"数据已保存到 {o_name}")
    print(f"处理完成！共找到 {len(onlyISBN)} 条ISBN。")
    return o_name
