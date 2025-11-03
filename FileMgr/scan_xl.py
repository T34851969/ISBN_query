import glob

def scan():
    """扫描当前目录下的Excel和CSV文件"""
    List = []
    excel = glob.glob("*.xlsx") + glob.glob("*.xls")
    csv = glob.glob("*.csv")
    
    List = excel + csv
    List.sort()  # 排序确保文件列表稳定
    
    print("当前目录下的文件列表：")
    for i, file in enumerate(List, 1):
        print(f"{i}. {file}")
    
    return List