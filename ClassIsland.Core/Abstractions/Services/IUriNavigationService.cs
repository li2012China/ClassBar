using ClassIsland.Core.Models.UriNavigation;
using ClassIsland.Shared.IPC.Abstractions.Services;

namespace ClassIsland.Core.Abstractions.Services;

/// <summary>
/// Uri导航服务，用于在ClassIsland内部和外部通过uri进行导航。
/// </summary>
public interface IUriNavigationService : IPublicUriNavigationService
{
    /// <summary>
    /// ClassIsland 内部的 uri 协议名
    /// </summary>
    public static string UriScheme { get; } = "classisland";

    /// <summary>
    /// ClassIsland 应用导航主机名
    /// </summary>
    public static string UriDomainApp { get; } = "app";

    internal void HandleNavigation(string domain, string path, Action<UriNavigationEventArgs> onNavigated);

    internal void HandleAppNavigation(string path, Action<UriNavigationEventArgs> onNavigated);

    
    /// <summary>
    /// 导航到指定 Uri，但在抛出异常时自动捕获，并显示错误提示。
    /// </summary>
    /// <param name="uri">要导航的 Uri</param>
    /// <param name="exception">导航时产生的异常（如有）</param>
    void NavigateWrapped(Uri uri, out Exception? exception);
}