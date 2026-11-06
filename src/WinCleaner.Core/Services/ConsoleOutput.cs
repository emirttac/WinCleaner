using System.Globalization;
using System.Text;

namespace WinCleaner.Core.Services;

/// <summary>
/// Windows console tools write either UTF-8 (newer builds) or the OEM code page.
/// Pick UTF-8 only when the bytes are valid UTF-8 so a legacy Turkish console stays intact.
/// </summary>
public static class ConsoleOutput
{
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return "";

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes[3..]);

        if (IsUtf8(bytes))
            return Encoding.UTF8.GetString(bytes);

        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        return oem.GetString(bytes);
    }

    public static bool IsUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            utf8.GetCharCount(bytes);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
