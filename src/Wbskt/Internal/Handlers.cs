namespace Wbskt.Internal;

/// <summary>
/// Raises an app's event so that a handler that throws cannot break the SDK: each subscriber is called
/// on its own, its exception goes to the error handler, and an error handler that throws is ignored.
/// </summary>
internal static class Handlers
{
    public static void Raise(Action? handlers, Action<Exception>? onError) => Raise(handlers, handler => handler(), onError);

    public static void Raise<T>(T? handlers, Action<T> invoke, Action<Exception>? onError) where T : Delegate
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                invoke((T)handler);
            }
            catch (Exception error)
            {
                try
                {
                    onError?.Invoke(error);
                }
                catch
                {
                    // An error handler that throws is ignored too.
                }
            }
        }
    }
}
