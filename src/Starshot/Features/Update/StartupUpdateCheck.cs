using System;
using System.Threading.Tasks;

namespace Starshot.Features.Update;

/// <summary>
/// 启动自动更新检查：与托盘/全局热键/心跳同一条常驻生命周期，--hide 自启、从不打开主窗口的用户也照常检查。
/// 呈现分两种：主窗口可见 → 直接弹更新窗口；否则托盘气泡提示并挂起，等主窗口可见时再弹
/// （不在登录后凭空弹窗打断用户操作）。
/// </summary>
internal static class StartupUpdateCheck
{
    private static ReleaseInfo? _pendingRelease;

    private static bool _updateWindowOpen;

    public static void Start()
    {
#if !DEBUG
        _ = CheckAsync();
#endif
    }

    private static async Task CheckAsync()
    {
        try
        {
            if (!AppConfig.EnableAutoUpdateCheck)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - AppConfig.LastUpdateCheckTime < 86400)
                return;
            var (release, _) = await UpdateService.CheckUpdateAsync();
            // 成功查询后才更新时间戳：网络失败抛异常会跳过本行（catch 兜底），下次启动即重试
            AppConfig.LastUpdateCheckTime = now;
            if (release is null)
                return;
            Present(release);
        }
        catch { }
    }

    private static void Present(ReleaseInfo release)
    {
        if (App.Current.MainWindow is { Visible: true })
        {
            ShowUpdateWindow(release);
            return;
        }
        _pendingRelease = release;
        App.Current.SystemTray?.ShowNotification(
            Lang.Starshot_UpdateAvailableTitle,
            string.Format(Lang.Starshot_UpdateAvailableTrayMessage, release.TagName)
        );
    }

    /// <summary>
    /// 主窗口可见后调用（首次启动 splash 结束 / 从托盘重新打开）：把挂起的新版本弹出来。
    /// 没有挂起则什么都不做。
    /// </summary>
    public static void FlushPending()
    {
        var release = _pendingRelease;
        _pendingRelease = null;
        if (release is null)
            return;
        ShowUpdateWindow(release);
    }

    private static void ShowUpdateWindow(ReleaseInfo release)
    {
        // 手动检查（关于页）可能已开着更新窗口，别叠第二个
        if (_updateWindowOpen)
            return;
        var window = new UpdateWindow();
        _updateWindowOpen = true;
        window.Closed += (_, _) => _updateWindowOpen = false;
        window.SetRelease(release);
    }
}
