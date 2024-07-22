namespace Codibre.MSSqlSession.Impl.Utils;

internal static class Helper
{
    internal static bool Try(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch
        {
            return false;
        }
    }
}