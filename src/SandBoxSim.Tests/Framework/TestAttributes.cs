namespace SandBoxSim.Tests.Framework;

/// <summary>
/// 标记一个测试方法（无参、返回 void）。
/// 用自定义特性而不是 xUnit：本仓库要求"只有 Roslyn csc 也能编译并运行测试"
/// （见 docs/build.md 的构建通道说明），任何 NuGet 依赖都会破坏这条通道。
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)]
public sealed class FactAttribute : System.Attribute
{
    public string Name { get; }

    public FactAttribute(string name = "") => Name = name;
}

/// <summary>标记一组参数化用例。参数直接写在测试方法上，运行时由运行器逐一执行。</summary>
public sealed class TheoryCase
{
    public string Name { get; }
    public object?[] Arguments { get; }

    public TheoryCase(string name, params object?[] arguments)
    {
        Name = name ?? string.Empty;
        Arguments = arguments ?? System.Array.Empty<object?>();
    }
}

/// <summary>
/// 参数化测试的特性。
///
/// 注意这里的取舍：C# 的特性实参只接受编译期常量，而 <c>new TheoryCase(...)</c> 不是常量，
/// 所以**不能**把用例写在特性里（会被编译器拒绝：CS0182）。
/// 因此约定是：
///   * 特性只带一个显示名（可选），用于筛选与报告；
///   * 用例数据写在方法体内，通过 <c>Framework.Theory.Cases(...)</c> 返回；
///   * 运行器按 CS0182 的错误提示无法做到的事，用"方法内部循环 + 逐条断言"达成，
///     代价是需要应用一个约定（见 docs/build.md 的测试章节）。
/// 这个约定在测试里由 <c>RunnerConstraintsTests</c> 把关：标记了 [Theory] 的方法
/// 必须至少有一个参数，否则会被判为误用。
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)]
public sealed class TheoryAttribute : System.Attribute
{
    public string Name { get; }

    public TheoryAttribute(string name = "") => Name = name;
}

/// <summary>用例集合的构造入口（纯运行期，避开特性实参限制）。</summary>
public static class Theory
{
    public static TheoryCase[] Cases(params TheoryCase[] cases) => cases;

    public static TheoryCase Case(string name, params object?[] arguments) => new TheoryCase(name, arguments);
}

public sealed class SkipAttribute : System.Attribute
{
    public string Reason { get; }

    public SkipAttribute(string reason = "") => Reason = reason;
}
