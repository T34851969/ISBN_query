import os

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