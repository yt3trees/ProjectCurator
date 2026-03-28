using Avalonia.Threading;
using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class AvaloniaDispatcherService : IDispatcherService
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
    public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);
    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();
}
