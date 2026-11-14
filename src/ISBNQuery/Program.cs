// ISBN查重系统入口。--selftest 走 Core 无 UI 冒烟（CI/交付验收用）。

namespace ISBNQuery;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest")
            return Core.SelfTest.Run();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
