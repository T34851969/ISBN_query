import pandas as pd

def read(filePath):
    """读取csv文件所有工作表的A列数据（数据已经提前转化）"""
    data_list = []
    
    if filePath.lower().endswith('.csv'):   # 读取CSV文件
        df = pd.read_csv(filePath, header=None)
        col_data = df.iloc[:, 0]  # 读取第一列
        # 检查第一行是否为标题，如果是则跳过
        if len(col_data) > 0:
            first_val = str(col_data.iloc[0]).strip()
            if first_val.lower() not in ['isbn', '标准号', ''] and not first_val.isdigit():
                # 如果不是常见标题，保留所有数据
                data_list.extend(col_data.dropna().astype(str).tolist())
            else:
                # 如果是标题，跳过第一行
                data_list.extend(col_data.iloc[1:].dropna().astype(str).tolist())

    return data_list