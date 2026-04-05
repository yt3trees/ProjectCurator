using Wpf.Ui;

namespace Curia.Services;

/// <summary>
/// wpf-ui の IPageService を実装するサービス。
/// DI コンテナからページを解決する。
/// </summary>
public class PageService : IPageService
{
    private readonly IServiceProvider _serviceProvider;

    public PageService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public T? GetPage<T>() where T : class
    {
        return _serviceProvider.GetService(typeof(T)) as T;
    }

    public System.Windows.FrameworkElement? GetPage(Type pageType)
    {
        return _serviceProvider.GetService(pageType) as System.Windows.FrameworkElement;
    }
}
