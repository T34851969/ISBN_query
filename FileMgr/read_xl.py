"""读取Excel文件所有工作表的A列数据"""

import pandas as pd

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