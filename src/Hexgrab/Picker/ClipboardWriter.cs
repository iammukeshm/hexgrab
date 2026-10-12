using System.Runtime.InteropServices;
using System.Windows;

namespace Hexgrab.Picker;

/// <summary>
/// The clipboard is a shared resource; another app can hold it open for a few milliseconds.
/// Retry instead of failing on the first CLIPBRD_E_CANT_OPEN (same pattern PowerToys uses).
/// </summary>
internal static class ClipboardWriter
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(10);

    public static bool TryCopy(string text)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (ExternalException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(RetryDelay);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        return false;
    }
}
