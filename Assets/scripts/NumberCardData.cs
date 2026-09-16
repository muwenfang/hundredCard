using UnityEngine;

/// <summary>
/// 数字卡的数据定义（ScriptableObject）。
/// 【设计说明 · 为什么不在这里扣减 number】
/// ScriptableObject 是工程内的共享资产。如果在运行时直接修改 number / canDraw，
/// 在编辑器里退出播放模式后改动会残留到 .asset 文件上，导致下次运行数据错乱。
/// 因此运行时的「剩余张数」由 GameManager.tempCardsPool 中「该牌面出现的次数」来表达：
/// 初始时每个牌面往池子里放 2 份，抽走一张就从池子里移除一项，池子里找不到即视为数量为 0、不可抽取。
/// 本类的 number / canDraw 只作为「初始定义」与 Inspector 说明使用。
/// </summary>
[CreateAssetMenu(fileName = "MyNumberCards", menuName = "NumberCardData", order = 1)]
public class NumberCardData : ScriptableObject
{
    [Header("牌面定义")]
    [Tooltip("牌面数字，取值 1~100")]
    public int value;

    [Tooltip("该牌面是否为质数。已随资产预先烘焙（1~100 中恰好有 25 个质数）")]
    public bool isPrime;

    [Tooltip("该牌面在整副牌中的总张数，初始为 2")]
    public int number = 2;

    [Tooltip("该牌面是否允许被抽取；运行时剩余张数为 0 时视为不可抽取")]
    public bool canDraw = true;

    /// <summary>
    /// 判断任意整数是否为质数。1 不是质数，小于 2 的数一律返回 false。
    /// 用「平方根上界 + 只试奇数」的试除法，1~100 范围内开销可忽略。
    /// </summary>
    public static bool IsPrimeNumber(int n)
    {
        if (n < 2) return false;
        if (n < 4) return true;          // 2、3
        if (n % 2 == 0) return false;    // 偶数直接排除
        for (int i = 3; (long)i * i <= n; i += 2)
        {
            if (n % i == 0) return false;
        }
        return true;
    }

    /// <summary>
    /// 自检用：资产里烘焙的 isPrime 是否与 value 的真实质数性质一致。
    /// GameManager 初始化时会用它做一次只读校验，不会改动资产。
    /// </summary>
    public bool IsPrimeFlagConsistent => isPrime == IsPrimeNumber(value);
}
