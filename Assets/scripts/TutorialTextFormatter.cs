using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 教程长文本的整理工具（纯静态、不碰任何 Unity 对象，可离线跑回归）。
///
/// 为什么需要它：教程正文是从 Word 文档导出的，直接塞进 uGUI 的 Text (Legacy) 有三个坑：
///   1. Word 的段内换行是 '\r'，Unity 的 Text 会把 '\r' 当一个缺字形的字符画出来（或直接吃掉），
///      结果就是「标题后面莫名少一行 / 多一行空隙」，所以要统一成 '\n'；
///   2. 每个 Word 段落里带一堆行尾空格（含全角空格），会让排版出现看不见的宽度；
///   3. 正文里的 <c>&lt;size=65&gt;</c> 这类富文本标记是**绝对像素值**（文档正文基准是 45）。
///      玩家把字号从 45 调到 30，标题却还是 65，头重脚轻。所以要按比例把标签一起缩放。
///
/// 这类处理放在静态方法里，就能用控制台宿主直接喂用例验证，不必依赖 Unity 运行时。
/// </summary>
public static class TutorialTextFormatter
{
    /// <summary>文档正文的基准字号（Word 里正文与标题的比例就是按它定的：正文 45 / 小标题 55 / 大标题 65）。</summary>
    public const float DefaultSourceFontSize = 45f;

    /// <summary>字号允许的范围。太大或太小都会让 ScrollView 的布局算不出合理高度。</summary>
    public const float MinFontSize = 1f;
    public const float MaxFontSize = 400f;

    /// <summary><c>&lt;size=65&gt;</c> 形式的富文本字号标签。</summary>
    private static readonly Regex SizeTagRegex = new Regex(@"<size=(\d+)>", RegexOptions.Compiled);

    /// <summary>任意尖括号标签（用于「关掉富文本」时把所有标签剥掉）。</summary>
    private static readonly Regex AnyTagRegex = new Regex(@"<[^>]*>", RegexOptions.Compiled);

    /// <summary>
    /// 一步到位地准备好要写进 Text 的字符串。
    /// </summary>
    /// <param name="raw">原始文本（通常来自 TextAsset）。</param>
    /// <param name="sourceFontSize">原文的基准字号，用于换算缩放比例；&lt;= 0 时按 <see cref="DefaultSourceFontSize"/> 处理。</param>
    /// <param name="targetFontSize">实际要用的字号。</param>
    /// <param name="richText">是否保留富文本标签。false = 把所有标签剥掉，标题与正文同字号。</param>
    public static string Prepare(string raw, float sourceFontSize, float targetFontSize, bool richText)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        string text = NormalizeLineEndings(raw);
        text = TrimLineEnds(text);
        text = MergeOrphanSizeCloseTags(text);
        text = CollapseBlankLines(text);

        if (!richText) return StripTags(text);

        float source = sourceFontSize > 0f ? sourceFontSize : DefaultSourceFontSize;
        float target = ClampFontSize(targetFontSize);
        return RescaleSizeTags(text, target / source);
    }

    /// <summary>把 \r\n、\r 统一成 \n（Unity 的 Text 只认 \n）。</summary>
    public static string NormalizeLineEndings(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>
    /// 去掉每行行尾的空白（半角空格、制表符、全角空格、零宽空格）。
    /// 行首不动 —— 文档里有靠全角空格做出的缩进，那是作者的本意。
    /// </summary>
    public static string TrimLineEnds(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string[] lines = text.Split('\n');
        StringBuilder sb = new StringBuilder(text.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines[i].TrimEnd(' ', '\t', '\u3000', '\u200B', '\uFEFF'));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 把「单独成行的 &lt;/size&gt;」并回上一行。
    ///
    /// Word 导出时长这样：一个段落里放了标题 + 段内换行 + 闭合标签，
    /// 结果就是标题、<c>&lt;/size&gt;</c>、正文各占一行。渲染出来标题后面会多出一个空行，
    /// 而且闭合标签自己站一行看着也别扭。这里把它并回去。
    ///
    /// 只并到「以 &lt;size= 开头的行」后面 —— 别的行后面出现 &lt;/size&gt; 说明文档本身结构不对，
    /// 不动它，免得掩盖掉真正的错误。
    /// 另外文档里有把闭合标签误写成 <c>&lt;size&gt;</c>（漏了斜杠）的，同样按闭合标签处理。
    /// </summary>
    public static string MergeOrphanSizeCloseTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string[] lines = text.Split('\n');
        List<string> kept = new List<string>(lines.Length);

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            if ((trimmed == "</size>" || trimmed == "<size>") && kept.Count > 0
                && kept[kept.Count - 1].StartsWith("<size=", StringComparison.Ordinal))
            {
                kept[kept.Count - 1] = kept[kept.Count - 1] + "</size>";
                continue;
            }

            kept.Add(lines[i]);
        }

        return string.Join("\n", kept.ToArray());
    }

    /// <summary>把连续的多个空行压成一个，并去掉整段文本首尾的空行。</summary>
    public static string CollapseBlankLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string[] lines = text.Split('\n');
        List<string> kept = new List<string>(lines.Length);

        for (int i = 0; i < lines.Length; i++)
        {
            bool blank = string.IsNullOrEmpty(lines[i]);
            if (blank && (kept.Count == 0 || string.IsNullOrEmpty(kept[kept.Count - 1]))) continue;   // 开头空行 / 连续空行
            kept.Add(lines[i]);
        }

        while (kept.Count > 0 && string.IsNullOrEmpty(kept[kept.Count - 1])) kept.RemoveAt(kept.Count - 1);

        return string.Join("\n", kept.ToArray());
    }

    /// <summary>
    /// 按比例改写所有 <c>&lt;size=NN&gt;</c> 标签的数值，并夹到合法范围。
    /// <paramref name="scale"/> = 1 时原样返回（不产生任何改动，便于「设置与原文一致」时零开销）。
    /// </summary>
    public static string RescaleSizeTags(string text, float scale)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (Mathf.Approximately(scale, 1f)) return text;

        return SizeTagRegex.Replace(text, delegate (Match m)
        {
            int original;
            if (!int.TryParse(m.Groups[1].Value, out original)) return m.Value;

            int scaled = Mathf.RoundToInt(ClampFontSize(original * scale));
            return "<size=" + scaled + ">";
        });
    }

    /// <summary>剥掉所有尖括号标签，只留纯文本。</summary>
    public static string StripTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return AnyTagRegex.Replace(text, string.Empty);
    }

    /// <summary>统计 <c>&lt;size=NN&gt;</c> 标签个数（自检用）。</summary>
    public static int CountSizeTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return SizeTagRegex.Matches(text).Count;
    }

    /// <summary>字号夹到 [MinFontSize, MaxFontSize]；传进来的 NaN 一律当默认值。</summary>
    public static float ClampFontSize(float size)
    {
        if (float.IsNaN(size)) return DefaultSourceFontSize;
        if (size < MinFontSize) return MinFontSize;
        if (size > MaxFontSize) return MaxFontSize;
        return size;
    }

    /// <summary>估算按当前字号渲染这段文本需要多少行（只用于日志与自检，不参与布局）。</summary>
    public static int CountVisualLines(string preparedText)
    {
        if (string.IsNullOrEmpty(preparedText)) return 0;

        string stripped = StripTags(NormalizeLineEndings(preparedText));
        int lines = 1;
        for (int i = 0; i < stripped.Length; i++)
        {
            if (stripped[i] == '\n') lines++;
        }
        return lines;
    }
}
