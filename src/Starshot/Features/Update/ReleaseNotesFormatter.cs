using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Starshot.Features.Update;

/// <summary>
/// Release note 按 ss: 语言区域标签分区（如 ss:en-US），只展示当前界面语言对应的区域，
/// 没有对应语言时退回英文；完全没有标签的旧 note 原样展示。
/// </summary>
internal static class ReleaseNotesFormatter
{
    // <!-- ss:<语言> --> … <!-- /ss:<语言> -->，内容跨行
    private static readonly Regex RegionPattern = new(
        @"<!--\s*ss:([A-Za-z0-9-]+)\s*-->(.*?)<!--\s*/ss:\1\s*-->",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    private const string FallbackLanguage = "en-US";

    public static string Filter(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "";

        var regions = new List<(string Language, string Content)>();
        foreach (Match match in RegionPattern.Matches(notes))
        {
            regions.Add((match.Groups[1].Value, match.Groups[2].Value.Trim()));
        }
        // 没有语言区域标签：整篇就是正文（旧 note 兼容），原样展示
        if (regions.Count == 0)
            return notes;

        string uiLanguage = CultureInfo.CurrentUICulture.Name;
        var shown = regions.FindAll(r =>
            string.Equals(r.Language, uiLanguage, StringComparison.OrdinalIgnoreCase)
        );
        if (shown.Count == 0)
        {
            shown = regions.FindAll(r =>
                string.Equals(r.Language, FallbackLanguage, StringComparison.OrdinalIgnoreCase)
            );
        }
        return string.Join("\n\n", shown.ConvertAll(r => r.Content));
    }
}
