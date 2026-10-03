using Avalonia.Headless;
using Rowles.Morphogenesis.Desktop;

namespace Rowles.Morphogenesis.Desktop.Tests.Testing;

internal static class AvaloniaTestHost
{
    internal static async Task RunAsync(Func<Task> action)
    {
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(App));
        Func<Task<bool>> dispatch = async () =>
        {
            await action();
            return true;
        };
        await session.Dispatch(dispatch, CancellationToken.None);
    }

    internal static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(App));
        return await session.Dispatch(action, CancellationToken.None);
    }
}
