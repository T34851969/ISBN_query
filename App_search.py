import pandas as pd

class SearchEngine:
    """ISBN查询引擎"""
    def search_single(self, ISBN):
        """查询ISBN是否存在，无返回值"""
        ISBN = ISBN.replace("-", "").replace(" ","")
        if not ISBN:
            print("非法输入")
            return None
        print(f"搜索的标准号: {ISBN}")
        
        with self.conn as conn:
            sql = f"SELECT * FROM ISBN WHERE ISBN=?"
            files = pd.read_sql_query(sql, conn, params = (ISBN,))
            
        if not files.empty:
            return True
        else:
            return False    
                
    def search_batch(self, ISBN_list):
        """批量查询ISBN是否存在"""
        ISBN_in = [ISBN.replace("-", "").replace(" ", "") for ISBN in ISBN_list if ISBN.strip()]
        if not ISBN_in:
            return set()
            
        with self.conn as conn:
            is_ISBN = set()
            batch_size = 500  
            for i in range(0, len(ISBN_in), batch_size):
                batch = ISBN_in[i:i+batch_size]
                params = ','.join(['?']*len(batch))
            
                sql = f"SELECT ISBN FROM ISBN WHERE ISBN IN ({params})"
                cursor = conn.execute(sql, batch)
                is_ISBN.update({line[0] for line in cursor.fetchall()})
        
        return is_ISBN
