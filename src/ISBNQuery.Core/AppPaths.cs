// 数据目录定位与配置持久化。
// 绿色单文件约定：数据库、output/、log.txt、config.json 全部相对 exe 所在目录
// （对应 Python 版的 cwd 相对路径行为），拷到 U 盘/新目录即整体迁移。

using System.Text.Json;

namespace ISBNQuery.Core;

public static class AppPaths
{
    public static string DataDir { get; } = AppContext.BaseDirectory;

    public static string OutputDir => System.IO.Path.Combine(DataDir, "output");

    public static string ConfigPath => System.IO.Path.Combine(DataDir, "config.json");

    public static string LogPath => System.IO.Path.Combine(DataDir, "log.txt");

    public static void EnsureOutputDir() => Directory.CreateDirectory(OutputDir);

    private sealed class Config
    {
        public string? LastDb { get; set; }
    }

    public static string? ReadLastDb()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return null;
            var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath));
            return cfg?.LastDb;
        }
        catch
        {
            return null;
        }
    }

    public static void WriteLastDb(string path)
    {
        try
        {
            var cfg = new Config { LastDb = path };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 配置写入失败不影响主流程
        }
    }
}
