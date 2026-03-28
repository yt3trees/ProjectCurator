namespace ProjectCurator.Interfaces;

public interface IDispatcherService
{
    void Post(Action action);
    void Invoke(Action action);
    Task InvokeAsync(Action action);
}
