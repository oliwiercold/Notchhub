using System.Windows.Threading;
using NotchHub.Core.Contracts;

namespace NotchHub.App;

/// <summary><see cref="IUiDispatcher"/> over the WPF dispatcher of the main UI thread.</summary>
public sealed class UiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action) => dispatcher.BeginInvoke(DispatcherPriority.Normal, action);

    public Task InvokeAsync(Action action) =>
        dispatcher.CheckAccess() ? RunInline(action) : dispatcher.InvokeAsync(action).Task;

    public Task<T> InvokeAsync<T>(Func<T> func) =>
        dispatcher.CheckAccess() ? Task.FromResult(func()) : dispatcher.InvokeAsync(func).Task;

    private static Task RunInline(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
