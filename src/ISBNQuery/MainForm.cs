// NiceGUI 版界面的 WinForms 移植（对应 Python 版 app/ui.py）。
// 反卡死两条铁律同样适用：
// 1. 所有耗时操作（文件解析、入库、批量查询、导出）一律 Task.Run 放入工作线程，
//    UI 线程只做交互；进度经 Progress<T> 自动封送回 UI 线程；
// 2. 日志由定时器轮询 RingLogger 快照做增量刷新（300ms 节流），不逐条推送。
// 任务运行期间统一禁用控件并显示进度条（含任务栏进度），防止并发任务踩库。

using System.Data;
using ISBNQuery.Core;

namespace ISBNQuery;

internal sealed class MainForm : Form
{
    // ---- Core 服务 ----
    private readonly IsbnDatabase _db = new();
    private readonly TaskbarProgress _taskbar = new();
    private readonly IProgress<(double Value, string Label)> _progress;

    // ---- 状态 ----
    private bool _busy;
    private List<(string Isbn, bool Exists)> _batchItems = new();

    // 日志轮询状态（对应 Python 版 _start_state_timer 的非局部变量）
    private long _lastLogVersion = -1;
    private long _lastDropped;
    private int _pushedCount;

    // ---- 控件 ----
    private TabControl _mainTabs = null!;
    private TabControl _batchSubTabs = null!;
    private TabPage _tabBatch = null!;
    private TabPage _tabUpdate = null!;
    private TabPage _tabNew = null!;
    private TabPage _tabClean = null!;
    private Panel _initPanel = null!;
    private ComboBox _dbCombo = null!;
    private TextBox _logBox = null!;
    private TextBox _singleInput = null!;
    private TextBox _batchInput = null!;
    private Label _resultLabel = null!;
    private Label _statusLabel = null!;
    private Label _progressLabel = null!;
    private ProgressBar _progressBar = null!;
    private Panel _progressRow = null!;
    private DataGridView _grid = null!;
    private Button _initBtn = null!;
    private readonly List<Control> _lockablesMain = new();
    private readonly List<string> _dbPaths = new();

    private System.Windows.Forms.Timer _uiTimer = null!;
    private System.Windows.Forms.Timer _statusTimer = null!;

    public MainForm()
    {
        Text = "ISBN查重系统";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 620);
        Size = new Size(980, 720);
        AllowDrop = true;

        _progress = new Progress<(double Value, string Label)>(OnProgress);
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Shown += (_, _) => _taskbar.Attach(Handle);

        BuildLayout();
        RestoreLastDb();
        ApplyDbState();

        // 启动时预填历史日志（对应 Python 版 _build_log_panel）
        var (_, _, lines) = RingLogger.Global.Snapshot();
        if (lines.Length > 0)
        {
            _logBox.AppendText(string.Join(Environment.NewLine, lines) + Environment.NewLine);
            _pushedCount = lines.Length;
        }

        _uiTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _uiTimer.Tick += (_, _) => PollLogs();
        _uiTimer.Start();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        _statusTimer.Tick += (_, _) => { _statusLabel.Text = ""; _statusTimer.Stop(); };
    }

    // ---- 布局 ----

    private void BuildLayout()
    {
        // 中部：主标签页 / 初始化面板（Dock Fill，二选一可见）
        _mainTabs = BuildMainTabs();
        _initPanel = BuildInitPanel();

        // 底部：日志 + 进度（Dock Bottom）
        var bottom = BuildBottomPanel();

        // 顶部：数据库选择 + 状态栏（Dock Top）
        var top = BuildTopPanel();

        // 加入顺序决定停靠次序：先 Fill，后 Bottom/Top（后加入者先停靠）
        Controls.Add(_mainTabs);
        Controls.Add(_initPanel);
        Controls.Add(bottom);
        Controls.Add(top);
    }

    private Panel BuildTopPanel()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 78 };

        var dbLabel = new Label
        {
            Text = "数据库:",
            Location = new Point(12, 14),
            AutoSize = true,
        };
        _dbCombo = new ComboBox
        {
            Location = new Point(72, 10),
            Width = 380,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        var switchBtn = new Button
        {
            Text = "切换数据库",
            Location = new Point(462, 8),
            Width = 110,
        };
        switchBtn.Click += async (_, _) => await OnSwitchDb();

        _statusLabel = new Label
        {
            Location = new Point(12, 48),
            AutoSize = true,
            ForeColor = Color.RoyalBlue,
        };

        panel.Controls.AddRange([dbLabel, _dbCombo, switchBtn, _statusLabel]);
        _lockablesMain.AddRange([switchBtn, _dbCombo]);
        return panel;
    }

    private TabControl BuildMainTabs()
    {
        _mainTabs = new TabControl { Dock = DockStyle.Fill };

        var tabSingle = new TabPage("单次查询");
        BuildSingleTab(tabSingle);

        _tabBatch = new TabPage("批量查询");
        _batchSubTabs = new TabControl { Dock = DockStyle.Fill };
        var tabPaste = new TabPage("输入框输入");
        BuildPasteTab(tabPaste);
        _tabClean = new TabPage("上传文件");
        BuildSimpleActionTab(_tabClean,
            "上传包含 ISBN 列的 .xlsx/.xls/.csv 文件（可多选）",
            "剔除库中已有的行，整行结果写入 output/ 目录（文件名带时间戳）",
            "选择文件（批量清洗）",
            (_, _) => OnPickAndRun(PickFiles(multiple: true), OnCleanFiles));
        _batchSubTabs.TabPages.AddRange([tabPaste, _tabClean]);
        _tabBatch.Controls.Add(_batchSubTabs);

        _tabUpdate = new TabPage("更新数据库");
        BuildSimpleActionTab(_tabUpdate,
            "上传 .xlsx/.xls/.csv 文件（可多选），系统自动去重并入当前数据库",
            "返回实际新增条数；入库按 5 万行分块并显示进度",
            "选择文件（更新数据库）",
            (_, _) => OnPickAndRun(PickFiles(multiple: true), OnUpdateFiles));

        _tabNew = new TabPage("新建数据库");
        BuildSimpleActionTab(_tabNew,
            "上传一个或多个格式一致的 .xlsx/.xls/.csv 文件以创建数据库",
            "默认以首个上传的文件名命名（{文件名}.db）",
            "选择文件（新建数据库）",
            (_, _) => OnPickAndRun(PickFiles(multiple: true), OnCreateFiles));

        _mainTabs.TabPages.AddRange([tabSingle, _tabBatch, _tabUpdate, _tabNew]);
        return _mainTabs;
    }

    private void BuildSingleTab(TabPage tab)
    {
        var hint = new Label
        {
            Text = "请输入 ISBN，可带连字符或空格：",
            Location = new Point(16, 20),
            AutoSize = true,
        };
        _singleInput = new TextBox
        {
            Location = new Point(16, 48),
            Width = 380,
            PlaceholderText = "978-7-111-12345-6",
        };
        _singleInput.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OnSingleQuery();
            }
        };
        var btn = new Button
        {
            Text = "查询",
            Location = new Point(410, 46),
            Width = 110,
            Height = 30,
        };
        btn.Click += (_, _) => OnSingleQuery();
        _resultLabel = new Label
        {
            Location = new Point(16, 92),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
        };

        tab.Controls.AddRange([hint, _singleInput, btn, _resultLabel]);
        _lockablesMain.Add(btn);
    }

    private void BuildPasteTab(TabPage tab)
    {
        var hint = new Label
        {
            Text = "请输入多个 ISBN，每行一个，可带连字符或空格：",
            Location = new Point(16, 14),
            AutoSize = true,
        };
        _batchInput = new TextBox
        {
            Location = new Point(16, 40),
            Width = 640,
            Height = 150,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "978-7-111-12345-6\n978 7 111 12345 7\n9787111123458",
        };
        var batchBtn = new Button
        {
            Text = "查询",
            Location = new Point(16, 202),
            Width = 110,
            Height = 32,
        };
        batchBtn.Click += (_, _) => OnBatchQuery();

        var resultHint = new Label
        {
            Text = "查询结果（留存请点\"导出结果\"）",
            Location = new Point(16, 246),
            AutoSize = true,
        };

        _grid = new DataGridView
        {
            Location = new Point(16, 270),
            Size = new Size(700, 160),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            VirtualMode = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _grid.ColumnCount = 2;
        _grid.Columns[0].Name = "ISBN";
        _grid.Columns[1].Name = "结果";
        _grid.Columns[0].FillWeight = 70;
        _grid.Columns[1].FillWeight = 30;
        _grid.CellValueNeeded += Grid_CellValueNeeded;

        tab.Controls.AddRange([hint, _batchInput, batchBtn, resultHint, _grid]);
        _lockablesMain.Add(batchBtn);
    }

    private void BuildSimpleActionTab(TabPage tab, string line1, string line2, string buttonText, EventHandler onClick)
    {
        var l1 = new Label { Text = line1, Location = new Point(16, 24), AutoSize = true };
        var l2 = new Label { Text = line2, Location = new Point(16, 52), AutoSize = true, ForeColor = Color.DimGray };
        var btn = new Button
        {
            Text = buttonText,
            Location = new Point(16, 92),
            Width = 220,
            Height = 34,
        };
        btn.Click += onClick;
        var dragHint = new Label
        {
            Text = "也可以直接把文件拖拽到窗口上",
            Location = new Point(16, 140),
            AutoSize = true,
            ForeColor = Color.Gray,
        };
        tab.Controls.AddRange([l1, l2, btn, dragHint]);
        _lockablesMain.Add(btn);
    }

    private Panel BuildInitPanel()
    {
        _initPanel = new Panel { Dock = DockStyle.Fill, Visible = false };

        var title = new Label
        {
            Text = "数据库初始化",
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            Location = new Point(24, 40),
            AutoSize = true,
        };
        var l1 = new Label
        {
            Text = "上传 .xlsx/.xls/.csv 文件以创建数据库（可多选，格式需一致）：",
            Location = new Point(24, 90),
            AutoSize = true,
        };
        _initBtn = new Button
        {
            Text = "选择文件（创建数据库）",
            Location = new Point(24, 130),
            Width = 220,
            Height = 36,
        };
        _initBtn.Click += (_, _) => OnPickAndRun(PickFiles(multiple: true), OnCreateFiles);
        var l2 = new Label
        {
            Text = "也可以直接把文件拖拽到窗口上。文件需包含 ISBN 列（ISBN / 标准号 / ISBN标准号）。",
            Location = new Point(24, 180),
            AutoSize = true,
            ForeColor = Color.Gray,
        };

        _initPanel.Controls.AddRange([title, l1, _initBtn, l2]);
        return _initPanel;
    }

    private Panel BuildBottomPanel()
    {
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 210 };

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            Font = new Font("Consolas", 9F),
        };

        var btnRow = new Panel { Dock = DockStyle.Bottom, Height = 38 };
        var clearBtn = new Button { Text = "清空日志", Location = new Point(12, 4), Width = 100 };
        clearBtn.Click += (_, _) => RingLogger.Global.Clear();
        var exportLogBtn = new Button { Text = "导出日志", Location = new Point(122, 4), Width = 100 };
        exportLogBtn.Click += async (_, _) => await OnExportLog();
        var exportCsvBtn = new Button { Text = "导出结果（CSV）", Location = new Point(240, 4), Width = 140 };
        exportCsvBtn.Click += async (_, _) => await OnExportCsv();
        btnRow.Controls.AddRange([clearBtn, exportLogBtn, exportCsvBtn]);
        _lockablesMain.AddRange([clearBtn, exportLogBtn, exportCsvBtn]);

        _progressRow = new Panel { Dock = DockStyle.Bottom, Height = 34, Visible = false };
        _progressBar = new ProgressBar
        {
            Location = new Point(12, 8),
            Width = 620,
            Height = 18,
        };
        _progressLabel = new Label
        {
            Location = new Point(642, 10),
            AutoSize = true,
            ForeColor = Color.DimGray,
        };
        _progressRow.Controls.AddRange([_progressBar, _progressLabel]);

        // 停靠次序：progressRow（最底）→ btnRow → logBox（填充剩余）
        panel.Controls.Add(_logBox);
        panel.Controls.Add(btnRow);
        panel.Controls.Add(_progressRow);
        return panel;
    }

    // ---- 状态管理 ----

    private bool DbReady => _db.IsExist() && _db.HasTable();

    private void RestoreLastDb()
    {
        var last = AppPaths.ReadLastDb();
        if (!string.IsNullOrEmpty(last) && last != _db.Path && File.Exists(last))
            _db.Switch(last);
    }

    private void ApplyDbState()
    {
        bool ready = DbReady;
        _initPanel.Visible = !ready;
        _mainTabs.Visible = ready;
        RefreshDbCombo();
        UpdateLockables();
        Status(ready ? "数据库就绪" : "尚未创建数据库：请选择 .xlsx/.csv 文件，或直接拖拽到窗口");
    }

    private void UpdateLockables()
    {
        bool enabled = _mainTabs.Visible && !_busy;
        foreach (var c in _lockablesMain)
            c.Enabled = enabled;
        _initBtn.Enabled = _initPanel.Visible && !_busy;
    }

    private void RefreshDbCombo()
    {
        _dbCombo.Items.Clear();
        _dbPaths.Clear();
        var files = Directory.GetFiles(AppPaths.DataDir, "*.db")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!files.Contains(_db.Path, StringComparer.OrdinalIgnoreCase))
        {
            files.Insert(0, _db.Path);
        }
        foreach (var f in files)
        {
            _dbCombo.Items.Add(System.IO.Path.GetFileName(f));
            _dbPaths.Add(f);
        }
        int idx = _dbPaths.FindIndex(p => string.Equals(p, _db.Path, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            _dbCombo.SelectedIndex = idx;
    }

    private bool AcquireBusy()
    {
        if (_busy)
        {
            Status("已有任务进行中，请稍候…");
            return false;
        }
        _busy = true;
        _progressRow.Visible = true;
        _progressBar.Value = 0;
        _progressLabel.Text = "处理中…";
        _taskbar.SetRunning(true);
        UpdateLockables();
        return true;
    }

    private void ReleaseBusy()
    {
        _busy = false;
        _progressRow.Visible = false;
        _taskbar.SetRunning(false);
        _taskbar.SetValue(0);
        UpdateLockables();
    }

    private void OnProgress((double Value, string Label) p)
    {
        _progressBar.Value = (int)Math.Round(Math.Clamp(p.Value, 0, 1) * 100);
        _progressLabel.Text = string.IsNullOrEmpty(p.Label) ? "处理中…" : p.Label;
        _taskbar.SetValue(p.Value);
    }

    private void Status(string text)
    {
        _statusLabel.Text = text;
        _statusTimer.Stop();
        if (!string.IsNullOrEmpty(text))
            _statusTimer.Start();
    }

    private void ReportError(Exception e)
    {
        RingLogger.Global.Append($"任务失败：{e.Message}");
        Status($"任务失败：{e.Message}");
    }

    private Action<double, string> ProgressCb => (f, l) => _progress.Report((f, l));

    // ---- 文件选择与拖拽 ----

    private (string Name, byte[] Content)[] PickFiles(bool multiple)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择表格文件",
            Filter = "表格文件 (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|全部文件 (*.*)|*.*",
            Multiselect = multiple,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.FileNames.Length == 0)
            return [];
        return dlg.FileNames
            .Select(f => (System.IO.Path.GetFileName(f), File.ReadAllBytes(f)))
            .ToArray();
    }

    private void OnPickAndRun((string Name, byte[] Content)[] files,
        Action<(string Name, byte[] Content)[]> handler)
    {
        if (files.Length == 0)
            return;
        handler(files);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (_busy)
        {
            Status("已有任务进行中，请稍候…");
            return;
        }
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            return;

        var files = new List<(string Name, byte[] Content)>();
        foreach (var p in paths)
        {
            try
            {
                files.Add((System.IO.Path.GetFileName(p), File.ReadAllBytes(p)));
            }
            catch (Exception ex)
            {
                Status($"无法读取文件 {p}：{ex.Message}");
                return;
            }
        }

        // 按当前可见页面路由
        if (_initPanel.Visible)
        {
            OnCreateFiles(files.ToArray());
            return;
        }
        var tab = _mainTabs.SelectedTab;
        if (tab == _tabNew)
            OnCreateFiles(files.ToArray());
        else if (tab == _tabUpdate)
            OnUpdateFiles(files.ToArray());
        else if (tab == _tabBatch && _batchSubTabs.SelectedTab == _tabClean)
            OnCleanFiles(files.ToArray());
        else
            Status("当前页面不支持文件导入：请切到【批量查询→上传文件 / 更新数据库 / 新建数据库】");
    }

    // ---- 任务入口（事件循环侧，全部 async） ----

    private async void OnSingleQuery()
    {
        if (!AcquireBusy())
            return;
        try
        {
            var value = _singleInput.Text;
            bool? result = await Task.Run(() => SearchService.SearchSingle(_db, value));
            if (result == true)
                SetResult("存在", Color.Green);
            else if (result == false)
                SetResult("不存在", Color.Firebrick);
            else
                SetResult("非法输入", Color.DarkOrange);
        }
        catch (Exception e)
        {
            ReportError(e);
        }
        finally
        {
            ReleaseBusy();
        }
    }

    private async void OnBatchQuery()
    {
        if (!AcquireBusy())
            return;
        try
        {
            var text = _batchInput.Text;
            var result = await Task.Run(() => SearchService.SearchBatch(_db, text.Split('\n')));
            _batchItems = result.Items;
            _grid.RowCount = _batchItems.Count;
            _grid.Invalidate();
            if (result.Total > 0)
                Status($"查询完成：存在 {result.FoundCount}，不存在 {result.MissingCount}");
            else
                Status("没有可用的输入。");
        }
        catch (Exception e)
        {
            ReportError(e);
        }
        finally
        {
            ReleaseBusy();
        }
    }

    private async void OnCreateFiles((string Name, byte[] Content)[] files)
    {
        if (!AcquireBusy())
            return;
        try
        {
            string target = await Task.Run(() => TaskCreate(files));
            Status($"数据库创建完成：{System.IO.Path.GetFileName(target)}");
            ApplyDbState();
        }
        catch (Exception e)
        {
            ReportError(e);
        }
        finally
        {
            ReleaseBusy();
        }
    }

    private async void OnUpdateFiles((string Name, byte[] Content)[] files)
    {
        if (!AcquireBusy())
            return;
        try
        {
            int added = await Task.Run(() =>
            {
                int total = 0;
                foreach (var (name, content) in files)
                    total += TaskUpdate(name, content);
                return total;
            });
            Status($"数据库更新完成：新增 {added} 条");
        }
        catch (Exception e)
        {
            ReportError(e);
        }
        finally
        {
            ReleaseBusy();
        }
    }

    private async void OnCleanFiles((string Name, byte[] Content)[] files)
    {
        if (!AcquireBusy())
            return;
        try
        {
            await Task.Run(() => SearchService.BatchClean(_db, files, ProgressCb));
            Status("批量清洗完成，结果已写入 output/ 目录");
        }
        catch (Exception e)
        {
            ReportError(e);
        }
        finally
        {
            ReleaseBusy();
        }
    }

    private async Task OnSwitchDb()
    {
        int idx = _dbCombo.SelectedIndex;
        if (idx < 0 || idx >= _dbPaths.Count)
            return;
        var path = _dbPaths[idx];
        if (path == _db.Path)
            return;
        if (!AcquireBusy())
            return;
        try
        {
            await Task.Run(() => _db.Switch(path));
            RingLogger.Global.Append($"已切换数据库：{path}");
            AppPaths.WriteLastDb(path);
            ApplyDbState();
        }
        catch (Exception e)
        {
            ReportError(e);
        }
        finally
        {
            ReleaseBusy();
        }
    }

    private async Task OnExportLog()
    {
        try
        {
            var path = await Task.Run(() => RingLogger.Global.Export(AppPaths.LogPath));
            Status($"日志已导出：{path}");
        }
        catch (Exception e)
        {
            ReportError(e);
        }
    }

    private async Task OnExportCsv()
    {
        if (_batchItems.Count == 0)
        {
            Status("没有可导出的结果");
            return;
        }
        try
        {
            var rows = _batchItems.Select(i => (i.Isbn, i.Exists ? "存在" : "不存在")).ToList();
            var path = await Task.Run(() => TableIo.WriteResultCsv(rows));
            Status($"结果已导出：{path}");
        }
        catch (Exception e)
        {
            ReportError(e);
        }
    }

    // ---- 工作线程任务（阻塞式，勿在 UI 线程直接调用） ----

    private string TaskCreate((string Name, byte[] Content)[] files)
    {
        var isbns = TableIo.ParseIsbnsFromFiles(files);
        if (isbns.Count == 0)
        {
            RingLogger.Global.Append("没有从文件中解析到 ISBN 数据，未创建数据库。");
            throw new InvalidOperationException("没有解析到 ISBN 数据");
        }
        var stem = System.IO.Path.GetFileNameWithoutExtension(files[0].Name);
        var target = System.IO.Path.Combine(AppPaths.DataDir, stem + ".db");
        RingLogger.Global.Append($"正在创建数据库 {stem}.db，共 {isbns.Count} 条，请稍候…");
        _db.Create(isbns, target, ProgressCb);
        RingLogger.Global.Append($"数据库创建完成：{stem}.db（共 {isbns.Count} 条）");
        return target;
    }

    private int TaskUpdate(string name, byte[] content)
    {
        DataTable dt = TableIo.ReadTable(name, content);
        var col = TableIo.PickIsbnColumn(dt)
            ?? throw new InvalidOperationException($"文件 {name} 缺少 ISBN/标准号/ISBN标准号 列");
        var isbns = TableIo.CleanIsbnValues(dt, col);
        RingLogger.Global.Append($"正在更新数据库（{name}：{isbns.Count} 条），请稍候…");
        int added = _db.Update(isbns, ProgressCb);
        RingLogger.Global.Append($"数据库更新完成：读取 {isbns.Count} 条，新增 {added} 条。");
        return added;
    }

    // ---- 杂项 ----

    private void SetResult(string text, Color color)
    {
        _resultLabel.Text = text;
        _resultLabel.ForeColor = color;
    }

    private void Grid_CellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        int i = e.RowIndex;
        if (i < 0 || i >= _batchItems.Count)
            return;
        var item = _batchItems[i];
        e.Value = e.ColumnIndex == 0 ? item.Isbn : (item.Exists ? "存在" : "不存在");
    }

    private void PollLogs()
    {
        var (version, dropped, lines) = RingLogger.Global.Snapshot();
        if (version == _lastLogVersion)
            return;

        if (dropped != _lastDropped || lines.Length < _pushedCount)
        {
            // 发生裁剪：全量重推（对应 Python 版 log.clear + push 全量）
            _logBox.Clear();
            if (lines.Length > 0)
                _logBox.AppendText(string.Join(Environment.NewLine, lines) + Environment.NewLine);
            _pushedCount = lines.Length;
        }
        else
        {
            for (int i = _pushedCount; i < lines.Length; i++)
                _logBox.AppendText(lines[i] + Environment.NewLine);
            _pushedCount = lines.Length;
        }
        _lastLogVersion = version;
        _lastDropped = dropped;

        // 跟随滚动到底部
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiTimer?.Stop();
        _statusTimer?.Stop();
        _taskbar.Dispose();
        _db.Dispose();
        RingLogger.Global.Append("应用关闭");
        base.OnFormClosed(e);
    }
}
