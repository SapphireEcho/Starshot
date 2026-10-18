using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starshot.Features.Update;

/// <summary>
/// 自动更新检查：与托盘/全局热键/心跳同一条常驻生命周期，--hide 自启、从不打开主窗口的用户也照常检查。
/// 进程内每 5 小时查一次（启动后 30~60 秒随机首查，与心跳同节奏），长驻数天也能收到新版本。
/// 查询失败时改为 10 分钟后重试，成功后再回到 5 小时节奏。
/// 呈现分两种：主窗口可见 → 直接弹更新窗口；否则托盘气泡提示并挂起，等主窗口可见时再弹
/// （不在登录后凭空弹窗打断用户操作）。
/// </summary>
internal static class StartupUpdateCheck
{
    /// <summary>与心跳一致的首次延迟（随机区间，避免同机多实例同时打接口）</summary>
    private const int InitialDelayMinSeconds = 30;

    private const int InitialDelayMaxSeconds = 60;

    private const int IntervalHours = 5;

    /// <summary>失败后的重试间隔：5 小时对一次临时网络故障来说太长</summary>
    private const int RetryDelayMinutes = 10;

    private static ReleaseInfo? _pendingRelease;

    private static bool _updateWindowOpen;

#if !DEBUG
    private static Timer? _timer;

    private static int _busy;
#endif

    public static void Start()
    {
#if !DEBUG
        if (_timer is not null)
            return;
        _timer = new Timer(
            _ => _ = CheckAsync(),
            null,
            Random.Shared.Next(InitialDelayMinSeconds, InitialDelayMaxSeconds + 1) * 1000,
            IntervalHours * 3600 * 1000
        );
#endif
    }

    private static async Task CheckAsync()
    {
#if !DEBUG
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;
#endif
        bool failed = false;
        try
        {
            var (release, _) = await UpdateService.CheckUpdateAsync();
            if (release is null)
                return;
            Present(release);
        }
        catch
        {
            failed = true;
        }
        finally
        {
            ApplyInterval(failed);
#if !DEBUG
            Interlocked.Exchange(ref _busy, 0);
#endif
        }
    }

    /// <summary>失败 → 10 分钟后重试；成功/跳过 → 恢复 5 小时节奏</summary>
    private static void ApplyInterval(bool retry)
    {
#if !DEBUG
        _timer?.Change(
            retry ? TimeSpan.FromMinutes(RetryDelayMinutes) : TimeSpan.FromHours(IntervalHours),
            TimeSpan.FromHours(IntervalHours)
        );
#endif
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
            string.Format(Lang.Starshot_UpdateAvailableTrayMessage, release.TagName),
            // 点通知就是要点更新：直接开更新窗口，不碰主窗口
            onClick: FlushPending
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
