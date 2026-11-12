namespace Wbskt.Internal;

/// <summary>
/// Raises an app's event so that a handler that throws cannot break the SDK: its exception goes to
/// the error handler instead, and an error handler that throws is ignored.
/// </summary>
internal static class Handlers
{
    public static void Raise(Action? handler, Action<Exception>? onError)
    {
        try
        {
            handler?.Invoke();
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
