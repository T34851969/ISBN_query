// Windows 任务栏进度（ITaskbarList3 COM 互操作）。
// Windows 专用特性：任务运行时任务栏图标显示进度，失败静默降级。

using System.Runtime.InteropServices;

namespace ISBNQuery;

internal sealed class TaskbarProgress : IDisposable
{
    private enum TaskbarState
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8,
    }

    private static readonly Guid ClsidTaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90c9-9ad9e0e4f5a1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(nint hwnd);
        void DeleteTab(nint hwnd);
        void ActivateTab(nint hwnd);
        void SetActiveAlt(nint hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        void SetProgressState(nint hwnd, TaskbarState state);
        void SetProgressValue(nint hwnd, ulong completed, ulong total);
    }

    private ITaskbarList3? _taskbar;
    private nint _hwnd;

    public void Attach(nint hwnd)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidTaskbarList);
            if (type is null)
                return;
            _taskbar = (ITaskbarList3?)Activator.CreateInstance(type);
            _taskbar?.HrInit();
            _hwnd = hwnd;
        }
        catch
        {
            _taskbar = null;
        }
    }

    public void SetValue(double fraction)
    {
        if (_taskbar is null)
            return;
        try
        {
            _taskbar.SetProgressValue(_hwnd, (ulong)(Math.Clamp(fraction, 0, 1) * 100), 100);
        }
        catch
        {
        }
    }

    public void SetRunning(bool running)
    {
        if (_taskbar is null)
            return;
        try
        {
            _taskbar.SetProgressState(_hwnd, running ? TaskbarState.Normal : TaskbarState.NoProgress);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_taskbar is not null && Marshal.IsComObject(_taskbar))
            Marshal.ReleaseComObject(_taskbar);
        _taskbar = null;
    }
}
