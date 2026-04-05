using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ProjectCurator.Services;

/// <summary>
/// [[wikilink]] 形式のリンクを解析・解決するユーティリティ。
/// </summary>
public static class WikiLinkParser
{
    private static readonly Regex WikiLinkRegex = new(@"\[\[([^\[\]]+)\]\]", RegexOptions.Compiled);

    /// <summary>テキスト中の [[wikilink]] をすべて抽出する。</summary>
    public static IReadOnlyList<string> ExtractLinks(string content)
    {
        var results = new List<string>();
        foreach (Match m in WikiLinkRegex.Matches(content))
            results.Add(m.Groups[1].Value.Trim());
        return results;
    }

    /// <summary>
    /// リンク名からページファイルを検索する。
    /// pages/ 配下の全 .md ファイルから、ファイル名(拡張子なし)またはフロントマターの title が一致するものを返す。
    /// </summary>
    public static string? ResolveLink(string wikiRoot, string linkName)
    {
        var pagesDir = Path.Combine(wikiRoot, "pages");
        if (!Directory.Exists(pagesDir)) return null;

        foreach (var file in Directory.EnumerateFiles(pagesDir, "*.md", SearchOption.AllDirectories))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(stem, linkName, StringComparison.OrdinalIgnoreCase))
                return file;

            // フロントマターの title を確認
            try
            {
                var title = ExtractFrontmatterTitle(file);
                if (!string.IsNullOrWhiteSpace(title) &&
                    string.Equals(title, linkName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
            catch { /* ignore I/O errors */ }
        }
        return null;
    }

    private static string? ExtractFrontmatterTitle(string file)
    {
        using var reader = new StreamReader(file);
        var firstLine = reader.ReadLine();
        if (!string.Equals(firstLine, "---", StringComparison.Ordinal))
            return null;

        while (true)
        {
            var line = reader.ReadLine();
            if (line == null || string.Equals(line, "---", StringComparison.Ordinal))
                return null;

            if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                return line["title:".Length..].Trim().Trim('"');
        }
    }

    /// <summary>ページ内の [[wikilink]] を解決し、存在しないリンクを返す。</summary>
    public static IReadOnlyList<string> FindBrokenLinks(string wikiRoot, string content)
    {
        var broken = new List<string>();
        foreach (var link in ExtractLinks(content))
        {
            if (ResolveLink(wikiRoot, link) == null)
                broken.Add(link);
        }
        return broken;
    }
}
