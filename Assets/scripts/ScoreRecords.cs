using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// ======================================================================
//  成绩记录相关：数据结构 + 纯算法 + 磁盘读写
//
//  这一整个文件都刻意写成「纯静态 / 纯数据」，没有任何场景引用，
//  因此不需要在 Unity 里拖任何物体；也正因为不碰 Unity 对象，
//  这里的算法（学号校验、聚合、排序、排版、序列化）可以脱离运行时直接跑回归。
//
//  一条记录 = 四个数据一组：学号 / 平均分 / 最高得分 / 游玩局数。
//  其中「平均分」不落盘，而是由「累计总分 ÷ 游玩局数」在显示时现算 ——
//  存原始值才不会因为反复舍入而漂移。
// ======================================================================

/// <summary>
/// 一名学生的累计成绩记录（排行榜里的一行）。
/// 四个数据一组中的「平均分」是派生值，不单独存储，见 <see cref="Average"/>。
/// </summary>
[Serializable]
public class StudentRecord
{
    /// <summary>学号（本工程约定 0~300）。</summary>
    public int studentId;

    /// <summary>累计总分。平均分的分子，存原始值不存平均数。</summary>
    public int totalScore;

    /// <summary>历史最高得分。</summary>
    public int highScore;

    /// <summary>游玩局数。</summary>
    public int playCount;

    public StudentRecord()
    {
    }

    public StudentRecord(int id)
    {
        studentId = id;
    }

    /// <summary>平均分 = 累计总分 ÷ 游玩局数；一局都没玩时记 0（不返回 NaN）。</summary>
    public float Average
    {
        get { return playCount > 0 ? (float)totalScore / playCount : 0f; }
    }

    /// <summary>
    /// 把一局的成绩并入本记录：游玩局数 +1、累计总分 +score、最高得分取较大者。
    /// 负数分按 0 计（本工程不会产生负分，这里只是防御脏数据）。
    /// </summary>
    public void AddResult(int score)
    {
        if (score < 0) score = 0;

        playCount++;
        totalScore += score;
        if (score > highScore) highScore = score;
    }

    /// <summary>
    /// 把从磁盘读回来的脏数据修正到安全范围，避免出现负数局数、
    /// 负数总分这种会把平均分算成负值的数据。只做钳制，不改动合法数据。
    /// </summary>
    public void Sanitize()
    {
        if (playCount < 0) playCount = 0;
        if (totalScore < 0) totalScore = 0;
        if (highScore < 0) highScore = 0;
    }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture,
            "学号 {0}｜平均 {1:0.00}｜最高 {2}｜{3} 局", studentId, Average, highScore, playCount);
    }
}

// ======================================================================
//  学号校验
// ======================================================================

/// <summary>
/// 学号输入校验（需求 1）：只接受 0~300 的整数。
///
/// 判定规则（严格拒绝而非自动修正，配合「提示并拦住」的策略）：
///   - 空 / 纯空白            → 提示「请先输入学号」
///   - 含数字以外的任何字符   → 提示「只能由数字组成」（"12.5"、"-1"、"abc"、"1 2" 都会被拒）
///   - 数值超出 min~max      → 提示具体范围与当前输入
///   - 全角数字（１２３）会先自动转成半角，避免中文输入法下白白报错
///   - 前导零允许（"007" → 7），零宽空格会被丢弃
///
/// 纯静态、不碰 Unity 对象，可离线回归。
/// </summary>
public static class StudentIdValidator
{
    /// <summary>学号下限（含）。</summary>
    public const int DefaultMin = 0;

    /// <summary>学号上限（含）。</summary>
    public const int DefaultMax = 300;

    /// <summary>允许的最长数字位数，超过一律按「数值过大」处理，避免 int 溢出时给出含糊提示。</summary>
    private const int MaxDigits = 9;

    /// <summary>
    /// 规整输入：全角数字 ０-９ → 半角、全角空格 → 半角、丢弃零宽空格，最后去掉首尾空白。
    /// 内部空白故意保留 —— 这样 "1 2" 会在后续的数字检查里被拒掉。
    /// </summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        StringBuilder sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c >= '\uFF10' && c <= '\uFF19') c = (char)(c - '\uFF10' + '0');   // 全角数字
            else if (c == '\u3000') c = ' ';                                       // 全角空格
            else if (c == '\u200B') continue;                                      // 零宽空格：直接丢

            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>学号数值是否在合法区间内。</summary>
    public static bool IsValid(int id, int min, int max)
    {
        return id >= min && id <= max;
    }

    /// <summary>
    /// 校验并解析学号。
    /// </summary>
    /// <param name="raw">界面上的原始输入。</param>
    /// <param name="min">下限（含）。</param>
    /// <param name="max">上限（含）。</param>
    /// <param name="id">解析成功时的学号；失败为 -1。</param>
    /// <param name="error">失败时给玩家看的中文提示；成功为 null。</param>
    /// <returns>true = 输入合法。</returns>
    public static bool TryParse(string raw, int min, int max, out int id, out string error)
    {
        id = -1;
        error = null;

        string text = Normalize(raw);
        if (text.Length == 0)
        {
            error = "请先输入学号（" + min + " ~ " + max + " 的整数）。";
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9')
            {
                error = "学号只能由数字组成，请重新输入（" + min + " ~ " + max + " 的整数）。";
                return false;
            }
        }

        if (text.Length > MaxDigits)
        {
            error = "学号数值过大，请输入 " + min + " ~ " + max + " 之间的整数。";
            return false;
        }

        int parsed;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
        {
            error = "学号数值过大，请输入 " + min + " ~ " + max + " 之间的整数。";
            return false;
        }

        if (!IsValid(parsed, min, max))
        {
            error = "学号必须在 " + min + " ~ " + max + " 之间，当前输入是 " + parsed + "。";
            return false;
        }

        id = parsed;
        return true;
    }
}

// ======================================================================
//  排行榜排序
// ======================================================================

/// <summary>排行榜的排序口径。默认按学号（索引 0）。</summary>
public enum RankingSortMode
{
    /// <summary>按学号从小到大（默认）。</summary>
    ByStudentId = 0,

    /// <summary>按平均分从高到低。</summary>
    ByAverage = 1,

    /// <summary>按最高得分从高到低。</summary>
    ByHighScore = 2,

    /// <summary>按游玩局数从多到少。</summary>
    ByPlayCount = 3,
}

/// <summary>
/// 排行榜排序（需求 3）：四个指标各一个排行函数，用哪个由调用方选。
///
/// 所有函数都返回**新列表**，不会改动传入的原始记录集合 ——
/// 界面上换排序方式时不会把内存里的记录顺序搅乱。
///
/// 平均分的比较用「交叉相乘」而不是 float 相除：
/// 3/2 与 6/4 在整数运算下完全相等，不会因为浮点误差排出随机顺序。
/// </summary>
public static class RankingSort
{
    /// <summary>排序方式的数量。</summary>
    public const int ModeCount = 4;

    /// <summary>把排序方式翻译成给玩家看的中文说明。</summary>
    public static string Describe(RankingSortMode mode)
    {
        switch (mode)
        {
            case RankingSortMode.ByAverage: return "平均分（从高到低）";
            case RankingSortMode.ByHighScore: return "最高得分（从高到低）";
            case RankingSortMode.ByPlayCount: return "游玩局数（从多到少）";
            default: return "学号（从小到大）";
        }
    }

    /// <summary>按指定口径排序，返回新列表（null 记录会被剔除）。</summary>
    public static List<StudentRecord> Rank(List<StudentRecord> source, RankingSortMode mode)
    {
        List<StudentRecord> sorted = new List<StudentRecord>();
        if (source != null)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null) sorted.Add(source[i]);
            }
        }

        sorted.Sort((a, b) => Compare(a, b, mode));
        return sorted;
    }

    /// <summary>排行函数 1：按学号升序。</summary>
    public static List<StudentRecord> RankByStudentId(List<StudentRecord> source)
    {
        return Rank(source, RankingSortMode.ByStudentId);
    }

    /// <summary>排行函数 2：按平均分降序。</summary>
    public static List<StudentRecord> RankByAverage(List<StudentRecord> source)
    {
        return Rank(source, RankingSortMode.ByAverage);
    }

    /// <summary>排行函数 3：按最高得分降序。</summary>
    public static List<StudentRecord> RankByHighScore(List<StudentRecord> source)
    {
        return Rank(source, RankingSortMode.ByHighScore);
    }

    /// <summary>排行函数 4：按游玩局数降序。</summary>
    public static List<StudentRecord> RankByPlayCount(List<StudentRecord> source)
    {
        return Rank(source, RankingSortMode.ByPlayCount);
    }

    /// <summary>把 0~3 的整数转成排序方式（越界一律退回默认的「按学号」）。</summary>
    public static RankingSortMode ToMode(int modeIndex)
    {
        switch (modeIndex)
        {
            case (int)RankingSortMode.ByAverage: return RankingSortMode.ByAverage;
            case (int)RankingSortMode.ByHighScore: return RankingSortMode.ByHighScore;
            case (int)RankingSortMode.ByPlayCount: return RankingSortMode.ByPlayCount;
            default: return RankingSortMode.ByStudentId;
        }
    }

    /// <summary>
    /// 主指标比较（降序口径，负数 = a 排在前面）。
    /// 每个口径都带完整的次级比较链直到学号，因此结果完全确定 ——
    /// List.Sort 本身不稳定，没有完整链的话同分记录的顺序会随实现变化。
    /// </summary>
    public static int Compare(StudentRecord a, StudentRecord b, RankingSortMode mode)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;      // null 一律排到最后
        if (b == null) return -1;

        switch (mode)
        {
            case RankingSortMode.ByAverage:
            {
                int byAverage = CompareAverageDesc(a, b);
                if (byAverage != 0) return byAverage;

                int byHigh = b.highScore.CompareTo(a.highScore);
                if (byHigh != 0) return byHigh;

                int byCount = b.playCount.CompareTo(a.playCount);
                if (byCount != 0) return byCount;
                break;
            }
            case RankingSortMode.ByHighScore:
            {
                int byHigh = b.highScore.CompareTo(a.highScore);
                if (byHigh != 0) return byHigh;

                int byAverage = CompareAverageDesc(a, b);
                if (byAverage != 0) return byAverage;

                int byCount = b.playCount.CompareTo(a.playCount);
                if (byCount != 0) return byCount;
                break;
            }
            case RankingSortMode.ByPlayCount:
            {
                int byCount = b.playCount.CompareTo(a.playCount);
                if (byCount != 0) return byCount;

                int byHigh = b.highScore.CompareTo(a.highScore);
                if (byHigh != 0) return byHigh;

                int byAverage = CompareAverageDesc(a, b);
                if (byAverage != 0) return byAverage;
                break;
            }
        }

        // 兜底（也是「按学号」口径的全部内容）：学号升序
        return a.studentId.CompareTo(b.studentId);
    }

    /// <summary>
    /// 平均分降序比较：totalScore/playCount 用交叉相乘避免浮点误差。
    /// 一局都没玩的记录平均分按 0 处理，排在所有玩过的记录之后。
    /// </summary>
    public static int CompareAverageDesc(StudentRecord a, StudentRecord b)
    {
        if (a == null || b == null) return 0;

        if (a.playCount <= 0 && b.playCount <= 0) return 0;
        if (a.playCount <= 0) return 1;
        if (b.playCount <= 0) return -1;

        long left = (long)a.totalScore * b.playCount;
        long right = (long)b.totalScore * a.playCount;

        if (left == right) return 0;
        return left > right ? -1 : 1;
    }
}

// ======================================================================
//  排行榜文本排版
// ======================================================================

/// <summary>
/// 把记录列表拼成 rankingPanel 上那段文本（需求 3）。
///
/// 输出结构：标题行（可选）→ 表头行（可选）→ 每行一条记录。
/// **每行内部的指标顺序固定为：学号 → 平均分 → 最高得分 → 游玩局数**，
/// 换排序方式只改行的先后，绝不改指标顺序。
///
/// 标题行与表头行都是「填了才输出」：本工程已经把表头单独画在 ranking 面板的另一个
/// Text 上（那个写着「学号　平均分　最高分　游玩局数」的物体），所以默认两行都不输出，
/// 只往数据文本里写记录行。
///
/// 纯静态、只做字符串处理，可离线回归。
/// </summary>
public static class RankingTextBuilder
{
    /// <summary>默认标题（想显示标题时把它填进 UIManager.rankingTitleFormat）。{0} = 记录总数，{1} = 排序方式，{2} = 本次显示条数。</summary>
    public const string DefaultTitleFormat = "共 {0} 条记录｜排序方式：{1}｜显示 {2} 条";

    /// <summary>默认表头。指标顺序固定，不要改动这里的先后。</summary>
    public const string DefaultHeaderLine = "学号    平均分    最高得分    游玩局数";

    /// <summary>默认行格式：{0} 学号，{1} 平均分，{2} 最高得分，{3} 游玩局数。用制表符分列，与场景里已有的表头对齐。</summary>
    public const string DefaultLineFormat = "{0}\t\t\t{1}\t\t\t{2}\t\t\t{3}";

    /// <summary>默认平均分格式（两位小数，保证一列对齐）。</summary>
    public const string DefaultAverageFormat = "0.00";

    /// <summary>默认空数据文案。</summary>
    public const string DefaultEmptyText = "暂无记录";

    /// <summary>
    /// 拼装排行榜文本。
    /// </summary>
    /// <param name="records">要展示的记录（顺序即显示顺序，一般来自 RankingSort）。</param>
    /// <param name="mode">当前排序方式，只在输出标题时用到。</param>
    /// <param name="maxRows">最多显示多少条；0 或负数 = 全部显示。</param>
    /// <param name="titleFormat">标题模板；**留空则不输出标题行**。</param>
    /// <param name="headerLine">表头行；**留空则不输出表头行**（本工程表头画在另一个 Text 上，默认留空）。</param>
    /// <param name="lineFormat">行模板，null/空则用默认值。</param>
    /// <param name="averageFormat">平均分的数字格式，null/空则用默认值。</param>
    /// <param name="emptyText">无记录时的文案，null/空则用默认值。</param>
    public static string Build(
        List<StudentRecord> records,
        RankingSortMode mode,
        int maxRows,
        string titleFormat,
        string headerLine,
        string lineFormat,
        string averageFormat,
        string emptyText)
    {
        if (records == null || records.Count == 0)
        {
            return string.IsNullOrEmpty(emptyText) ? DefaultEmptyText : emptyText;
        }

        string rowTemplate = string.IsNullOrEmpty(lineFormat) ? DefaultLineFormat : lineFormat;
        string numberFormat = string.IsNullOrEmpty(averageFormat) ? DefaultAverageFormat : averageFormat;

        int rows = maxRows > 0 ? Math.Min(maxRows, records.Count) : records.Count;

        StringBuilder sb = new StringBuilder();

        if (!string.IsNullOrEmpty(titleFormat))
        {
            sb.Append(BuildTitle(titleFormat, records.Count, mode, rows)).Append('\n');
        }

        if (!string.IsNullOrEmpty(headerLine))
        {
            sb.Append(headerLine).Append('\n');
        }

        // 行与行之间才插换行：末尾不留空行，Text 的高度才好算
        int appended = 0;
        for (int i = 0; i < rows; i++)
        {
            StudentRecord r = records[i];
            if (r == null) continue;

            if (appended > 0) sb.Append('\n');
            sb.Append(FormatLine(rowTemplate, r, numberFormat));
            appended++;
        }

        return sb.ToString();
    }

    /// <summary>拼标题行，{0}/{1}/{2} 分别是总数、排序方式说明、本次显示条数。</summary>
    public static string BuildTitle(string titleFormat, int totalCount, RankingSortMode mode, int shownCount)
    {
        string template = string.IsNullOrEmpty(titleFormat) ? DefaultTitleFormat : titleFormat;

        return template
            .Replace("{0}", totalCount.ToString(CultureInfo.InvariantCulture))
            .Replace("{1}", RankingSort.Describe(mode))
            .Replace("{2}", shownCount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>拼一条记录：严格按「学号 / 平均分 / 最高得分 / 游玩局数」的顺序填。</summary>
    public static string FormatLine(string lineFormat, StudentRecord record, string averageFormat)
    {
        if (record == null) return string.Empty;

        string template = string.IsNullOrEmpty(lineFormat) ? DefaultLineFormat : lineFormat;

        return template
            .Replace("{0}", record.studentId.ToString(CultureInfo.InvariantCulture))
            .Replace("{1}", FormatAverage(record.totalScore, record.playCount, averageFormat))
            .Replace("{2}", record.highScore.ToString(CultureInfo.InvariantCulture))
            .Replace("{3}", record.playCount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 平均分文本。用固定区域（InvariantCulture）格式化，
    /// 避免将来改系统语言导致小数点变成逗号、把一列数字拆成两列。
    /// </summary>
    public static string FormatAverage(int totalScore, int playCount, string averageFormat)
    {
        string format = string.IsNullOrEmpty(averageFormat) ? DefaultAverageFormat : averageFormat;

        double average = playCount > 0 ? (double)totalScore / playCount : 0d;
        return average.ToString(format, CultureInfo.InvariantCulture);
    }
}

// ======================================================================
//  记录仓库：聚合 + 落盘
// ======================================================================

/// <summary>
/// 成绩记录的仓库（需求 2）。
///
/// 刻意做成**静态类**：不需要在场景里挂组件、不需要拖引用，
/// 也就不会因为「忘了拖」而导致记录功能整体失效。
///
/// 数据形态：一个学号一条记录（按学号聚合），
/// 游玩局数累加、累计总分累加、最高得分取历史最高，平均分由前两者现算。
///
/// 落盘位置：Application.persistentDataPath/score_records.csv
///   - 纯文本一行一条，方便直接打开核对或交给老师；
///   - Windows 上是 %USERPROFILE%\AppData\LocalLow\<公司名>\<产品名>\；
///   - 首次写入前会自动建目录，写入前后都打日志打印完整路径。
///
/// 读写失败（磁盘只读、被占用等）只报错并保留内存中的记录，不会让游戏流程崩掉。
/// </summary>
public static class ScoreRecordStore
{
    /// <summary>默认文件名。</summary>
    public const string DefaultFileName = "score_records.csv";

    /// <summary>文件头注释行，反序列化时会自动跳过所有以 # 开头的行。</summary>
    public const string FileHeader = "#HundredCards 成绩记录（学号,累计总分,最高得分,游玩局数）";

    /// <summary>文件名。想放到别的地方改这里即可（相对 persistentDataPath）。</summary>
    public static string fileName = DefaultFileName;

    private static List<StudentRecord> records;
    private static bool loaded;

    /// <summary>记录的完整落盘路径。</summary>
    public static string FilePath
    {
        get { return Path.Combine(Application.persistentDataPath, fileName); }
    }

    /// <summary>当前记录（首次访问时自动从磁盘加载）。返回的是内部列表，请勿直接改顺序。</summary>
    public static List<StudentRecord> Records
    {
        get
        {
            EnsureLoaded();
            return records;
        }
    }

    /// <summary>确保已经尝试过加载（只会真正读一次盘）。</summary>
    public static void EnsureLoaded()
    {
        if (loaded) return;

        // 先置位再读盘：ReadFromDisk 内部也走 Records 属性时不会再递归进来
        loaded = true;
        records = new List<StudentRecord>();
        ReadFromDisk();
    }

    /// <summary>
    /// 强制丢弃内存缓存、重新读盘（在外部改了记录文件之后刷新，见 UIManager.RefreshRanking）。
    /// 读盘失败时**保留原先内存里的那份记录**：宁可继续显示旧数据，
    /// 也不能因为一次读取异常就把整张榜清空。文件不存在不算失败 —— 那确实就是「还没有记录」。
    /// </summary>
    public static void ReloadFromDisk()
    {
        EnsureLoaded();                 // 先保证手上有一份可用的旧记录，供失败时回退

        List<StudentRecord> backup = records;
        records = new List<StudentRecord>();

        if (!ReadFromDisk() && backup != null) records = backup;
    }

    /// <summary>查找某个学号的记录；不存在返回 null。</summary>
    public static StudentRecord Find(int studentId)
    {
        EnsureLoaded();

        for (int i = 0; i < records.Count; i++)
        {
            if (records[i] != null && records[i].studentId == studentId) return records[i];
        }
        return null;
    }

    /// <summary>
    /// 记一局成绩（按学号聚合）：已有该学号则并入，没有则新建；随后立即落盘。
    /// 立即写盘是为了「程序被直接关掉」时数据不丢。
    /// </summary>
    /// <returns>并入之后的记录。</returns>
    public static StudentRecord AddResult(int studentId, int score)
    {
        EnsureLoaded();

        StudentRecord record = Find(studentId);
        if (record == null)
        {
            record = new StudentRecord(studentId);
            records.Add(record);
        }
        record.AddResult(score);

        SaveToDisk();

        Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "[ScoreRecordStore] 已记录：学号 {0} 本局 {1} 分 → 共 {2} 局，平均 {3:0.00}，最高 {4}。文件：{5}",
            record.studentId, score, record.playCount, record.Average, record.highScore, FilePath));

        return record;
    }

    /// <summary>清空全部记录。alsoDeleteFile = true 时连磁盘文件一起删。</summary>
    public static void ClearAll(bool alsoDeleteFile = false)
    {
        EnsureLoaded();
        records.Clear();

        if (!alsoDeleteFile)
        {
            SaveToDisk();
            return;
        }

        try
        {
            string path = FilePath;
            if (File.Exists(path)) File.Delete(path);
            Debug.Log("[ScoreRecordStore] 已清空记录并删除文件：" + path);
        }
        catch (Exception e)
        {
            Debug.LogError("[ScoreRecordStore] 删除记录文件失败：" + e.Message);
        }
    }

    // ------------------------------------------------------------------
    // 磁盘读写
    // ------------------------------------------------------------------

    /// <summary>从磁盘读记录。文件不存在视为「还没有记录」，不算错误。</summary>
    public static bool ReadFromDisk()
    {
        string path = FilePath;

        try
        {
            if (!File.Exists(path))
            {
                Debug.Log("[ScoreRecordStore] 还没有记录文件，从空记录开始：" + path);
                return true;
            }

            int ignored;
            List<StudentRecord> loadedRecords = Deserialize(File.ReadAllText(path), out ignored);
            records = loadedRecords;

            Debug.Log(string.Format("[ScoreRecordStore] 已读取 {0} 条记录（忽略 {1} 行）← {2}",
                loadedRecords.Count, ignored, path));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[ScoreRecordStore] 读取记录失败（保留内存中的记录）：" + e.Message);
            return false;
        }
    }

    /// <summary>把内存中的记录写回磁盘。</summary>
    public static void SaveToDisk()
    {
        EnsureLoaded();

        try
        {
            string path = FilePath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(path, Serialize(records), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Debug.LogError("[ScoreRecordStore] 写入记录失败：" + e.Message);
        }
    }

    // ------------------------------------------------------------------
    // 纯函数：序列化 / 反序列化（可离线回归，不碰磁盘）
    // ------------------------------------------------------------------

    /// <summary>
    /// 把记录列表序列化成 CSV 文本（首行为注释头）。
    /// 与 <see cref="Deserialize"/> 互为逆运算，纯函数。
    /// </summary>
    public static string Serialize(List<StudentRecord> source)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(FileHeader).Append('\n');

        if (source != null)
        {
            for (int i = 0; i < source.Count; i++)
            {
                StudentRecord r = source[i];
                if (r == null) continue;

                sb.Append(r.studentId.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.totalScore.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.highScore.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.playCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 解析 CSV 文本。空行与 # 注释行直接跳过、不计入忽略数；
    /// 字段数不对 / 数值解析失败 / 学号重复的行会被忽略并累加到 ignoredLines。
    /// </summary>
    public static List<StudentRecord> Deserialize(string text, out int ignoredLines)
    {
        ignoredLines = 0;

        List<StudentRecord> result = new List<StudentRecord>();
        if (string.IsNullOrEmpty(text)) return result;

        HashSet<int> seenIds = new HashSet<int>();
        string[] lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();      // 顺带去掉行尾的 \r
            if (line.Length == 0) continue;
            if (line[0] == '#') continue;

            string[] parts = line.Split(',');
            if (parts.Length < 4)
            {
                ignoredLines++;
                continue;
            }

            int id, total, high, plays;
            if (!TryParseInt(parts[0], out id)
                || !TryParseInt(parts[1], out total)
                || !TryParseInt(parts[2], out high)
                || !TryParseInt(parts[3], out plays))
            {
                ignoredLines++;
                continue;
            }

            if (seenIds.Contains(id))       // 同学号重复出现：保留先出现的那条
            {
                ignoredLines++;
                continue;
            }
            seenIds.Add(id);

            StudentRecord record = new StudentRecord(id);
            record.totalScore = total;
            record.highScore = high;
            record.playCount = plays;
            record.Sanitize();

            result.Add(record);
        }

        return result;
    }

    /// <summary>宽松一点的整数解析：允许前后空白与前导正负号，固定区域格式。</summary>
    private static bool TryParseInt(string raw, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(raw)) return false;

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
