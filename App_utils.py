import os
import pandas as pd
from fastapi import UploadFile

class ToolsBox:

    """读取Excel文件所有工作表的A列数据"""
    def read(filePath):
        data_list = []

        try:
            xl_file = pd.ExcelFile(filePath)    # 获取工作表名称
            
            for sheet_name in xl_file.sheet_names:
                df = pd.read_excel(filePath, sheet_name = sheet_name, header=None)
                col_data = df.iloc[:, 0]    # 读取第一列
                
                if len(col_data) > 0:   # 检查第一行是否为标题，如果是则跳过
                    first_val = str(col_data.iloc[0]).strip()
                    if first_val.lower() not in ['isbn', '标准号', ''] and not first_val.isdigit():
                        data_list.extend(col_data.dropna().astype(str).tolist())    # 如果不是常见标题，保留所有数据
                    else:
                        # 如果是标题，跳过第一行
                        data_list.extend(col_data.iloc[1:].dropna().astype(str).tolist())    # 如果是标题，跳过第一行
        except Exception as e:
            print(f"读取{filePath} 时出错: {e}")

        return data_list

    """清洗并去重ISBN数据"""
    def wash(selected):
        # 加载数据
        print("\n正在读取文件数据...")
        rawISBN = []
        
        for file in selected:
            print(f"处理: {file} 中")
            rawData = read_xl.read(file)
            rawISBN.extend(rawData)
            print(f"  - 正在读取 {file} , {len(rawData)} 条")
        
        print(f"\n共读取 {len(rawISBN)} 条ISBN")
        
        # 去重处理 - 使用集合去重，同时保持顺序
        seen = set()
        onlyISBN = []
        duplCount = 0
        
        print("\n开始查重")
        for item in rawISBN:
            item_str = item.strip() if isinstance(item, str) else str(item)
            if item_str not in seen:
                seen.add(item_str)
                onlyISBN.append(item_str)
            else:
                duplCount += 1
        
        print(f"去重后剩余 {len(onlyISBN)} 条，移除 {duplCount} 条")
        out = "ISBN.csv"
        
        # 创建DataFrame
        df_output = pd.DataFrame({'ISBN': onlyISBN})
        df_output.to_csv(out, index=False, encoding='utf-8-sig')
        print(f"数据已保存到 {out}")
        print(f"处理完成！共找到 {len(onlyISBN)} 条ISBN。")
        
    def del_file(files_path):
        try:
            if (files_path):
                os.remove(files_path)
                print(f"{files_path}已删除")
            else:
                print(f"未找到{files_path}")
                return
                
        except PermissionError:
            print("权限不足，请授权该程序对文件夹的编辑")
            return
            
        except Exception as err:
            print(f"删除文件时发生错误：{err}")
            return
        
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
