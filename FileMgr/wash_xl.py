import pandas as pd
from FileMgr import read_xl

def wash(selected):
    # 加载数据
    print("\n正在读取文件数据...")
    rawISBN = []
    
    for file in selected:
        print(f"处理文件: {file}")
        rawData = read_xl.read(file)
        rawISBN.extend(rawData)
        print(f"  - 正在读取 {file} 的 {len(rawData)} 条数据")
    
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
    o_name = "ISBN.csv"
    
    # 创建DataFrame
    df_output = pd.DataFrame({'ISBN': onlyISBN})
    df_output.to_csv(o_name, index=False, encoding='utf-8-sig')
    print(f"数据已保存到 {o_name}")
    print(f"处理完成！共找到 {len(onlyISBN)} 条ISBN。")