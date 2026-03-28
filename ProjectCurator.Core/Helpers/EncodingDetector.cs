using System.IO;
using System.Text;

namespace ProjectCurator.Helpers;

public static class EncodingDetector
{
    /// <summary>
    /// ファイルを読み込み、エンコーディングを検出して内容と検出結果を返す。
    /// encoding は "UTF8BOM"/"UTF16LE"/"UTF16BE"/"UTF8"/"SJIS" のいずれか。
    /// </summary>
    public static (string content, string encoding) ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);

        // BOM検出
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            var content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            return (content, "UTF8BOM");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            var content = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            return (content, "UTF16LE");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            var content = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            return (content, "UTF16BE");
        }

        // Heuristic detection for UTF-16 without BOM.
        // Many markdown files created by external tools can be UTF-16LE/BE without BOM,
        // which otherwise gets misread as UTF-8 and introduces NUL characters.
        if (bytes.Length >= 4)
        {
            var evenZero = 0;
            var oddZero = 0;
            var pairCount = bytes.Length / 2;
            for (var i = 0; i < pairCount * 2; i++)
            {
                if (bytes[i] != 0) continue;
                if ((i & 1) == 0) evenZero++;
                else oddZero++;
            }

            var evenRatio = pairCount == 0 ? 0.0 : (double)evenZero / pairCount;
            var oddRatio = pairCount == 0 ? 0.0 : (double)oddZero / pairCount;

            // UTF-16LE ASCII-like text: odd bytes are often zero.
            if (oddRatio > 0.30 && evenRatio < 0.05)
            {
                return (Encoding.Unicode.GetString(bytes), "UTF16LE");
            }

            // UTF-16BE ASCII-like text: even bytes are often zero.
            if (evenRatio > 0.30 && oddRatio < 0.05)
            {
                return (Encoding.BigEndianUnicode.GetString(bytes), "UTF16BE");
            }
        }

        // UTF-8 strict (throwOnInvalidBytes=true)
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var content = utf8.GetString(bytes);
            return (content, "UTF8");
        }
        catch
        {
            // SJIS fallback
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sjis = Encoding.GetEncoding("shift_jis");
            var content = sjis.GetString(bytes);
            return (content, "SJIS");
        }
    }

    /// <summary>
    /// 指定したエンコーディングでファイルに書き込む。
    /// </summary>
    public static void WriteFile(string path, string content, string encoding)
    {
        Encoding enc = encoding switch
        {
            "UTF8BOM" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "UTF16LE" => Encoding.Unicode,
            "UTF16BE" => Encoding.BigEndianUnicode,
            "SJIS" => GetSjisEncoding(),
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        File.WriteAllText(path, content, enc);
    }

    private static Encoding GetSjisEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("shift_jis");
    }
}
