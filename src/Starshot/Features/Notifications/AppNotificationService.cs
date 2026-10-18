using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Starshot.Features.Notifications;

/// <summary>
/// 应用通知（Toast）：走系统通知中心，横幅消失后仍能在通知中心找到并点击。
/// 未打包应用需要 AppNotificationManager 注册应用身份（会写 HKCU 注册表），
/// 注册后通知头部显示的应用名与图标由系统提供。
/// 提权进程中 IsSupported 返回 false，此时由调用方退回托盘气泡。
/// </summary>
internal static class AppNotificationService
{
    private const string UpdateTag = "update";

    private const string UpdateAction = "update";

    private static readonly ILogger _logger = AppConfig.GetLogger("AppNotificationService");

    private static DispatcherQueue? _dispatcher;

    private static Action? _onUpdateActivated;

    public static bool IsAvailable { get; private set; }

    /// <summary>
    /// 订阅点击回调并注册应用身份。必须在读取冷启动激活参数之前调用。
    /// </summary>
    public static void Initialize(DispatcherQueue dispatcher, Action onUpdateActivated)
    {
        _dispatcher = dispatcher;
        _onUpdateActivated = onUpdateActivated;
        if (!AppNotificationManager.IsSupported())
        {
            _logger.LogWarning(
                "App notifications unsupported (elevated process?), falling back to tray"
            );
            return;
        }
        try
        {
            var manager = AppNotificationManager.Default;
            // 先订阅再注册：否则点击通知会另起一个新进程来处理，而不是回调到当前进程
            manager.NotificationInvoked += OnNotificationInvoked;
            // 无参注册：显示名取 exe 文件名（Starshot），图标由系统从 exe 内嵌图标导出
            manager.Register();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Register app notifications failed, falling back to tray");
            IsAvailable = false;
        }
    }

    /// <summary>
    /// 更新提醒。点击后直接打开更新窗口，不经过主窗口。
    /// 返回 false 表示没有真正发出去（未注册、系统通知被关闭、Show 抛异常），调用方应回退托盘气泡。
    /// </summary>
    public static bool ShowUpdate(string title, string message)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("App notification unavailable, falling back to tray");
            return false;
        }
        try
        {
            // 用户关掉本应用通知或系统静音时 Show 仍会静默成功，必须先看设置再决定是否回退
            var setting = AppNotificationManager.Default.Setting;
            if (setting != AppNotificationSetting.Enabled)
            {
                _logger.LogWarning(
                    "App notifications disabled ({Setting}), falling back to tray",
                    setting
                );
                return false;
            }
            var notification = new AppNotificationBuilder()
                .AddArgument("action", UpdateAction)
                .AddText(title)
                .AddText(message)
                .SetAppLogoOverride(AppLogoUri)
                .BuildNotification();
            // 同 Tag 覆盖旧通知，避免多次检查在通知中心堆一屏
            notification.Tag = UpdateTag;
            AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show app notification failed, falling back to tray");
            return false;
        }
    }

    /// <summary>
    /// 通知正文左侧的大图标，必须用正方形图。
    /// </summary>
    private static Uri AppLogoUri =>
        new(Path.Combine(AppContext.BaseDirectory, "Assets", "update.png"));

    /// <summary>
    /// 处理应用激活参数。应用未运行时点击通知，进程带 ----AppNotificationActivated: 启动，
    /// 第一次点击的参数只能从这里取（之后的点击才走 NotificationInvoked）。
    /// 返回 true 表示这是更新通知的点击，调用方不应再打开主窗口。
    /// </summary>
    public static bool HandleActivation(AppActivationArguments args)
    {
        if (args.Kind is not ExtendedActivationKind.AppNotification)
            return false;
        if (args.Data is not AppNotificationActivatedEventArgs data)
            return false;
        if (!data.Arguments.TryGetValue("action", out string? action) || action != UpdateAction)
            return false;
        Invoke();
        return true;
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args
    )
    {
        if (!args.Arguments.TryGetValue("action", out string? action) || action != UpdateAction)
            return;
        Invoke();
    }

    private static void Invoke()
    {
        // 回调来自 COM 激活，在后台线程执行，开窗口必须切回 UI 线程
        _dispatcher?.TryEnqueue(() => _onUpdateActivated?.Invoke());
    }
}
