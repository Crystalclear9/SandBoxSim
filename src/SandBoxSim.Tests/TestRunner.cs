using System.Collections.Generic;
using System.Reflection;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 反射测试运行器。
///
/// 行为契约（CI 依赖它）：
///   * 发现所有标记 <c>[Fact]</c> / <c>[Theory]</c> 的公有无参方法（Theory 的参数来自特性）；
///   * 任何一个用例失败 → 退出码 1，并把失败信息与堆栈打到 stderr；
///   * <c>--filter &lt;子串&gt;</c> 只跑名字匹配的用例（开发时按模块跑）；
///   * <c>--list</c> 只列出用例，供脚本检查覆盖率。
/// </summary>
public static class TestRunner
{
    public static int Main(string[] args)
    {
        string filter = string.Empty;
        bool listOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--filter":
                    if (i + 1 < args.Length) { filter = args[i + 1]; i++; }
                    break;
                case "--list":
                    listOnly = true;
                    break;
                default:
                    if (!args[i].StartsWith("-", System.StringComparison.Ordinal))
                    {
                        filter = args[i];
                    }
                    break;
            }
        }

        System.Console.WriteLine("SandBoxSim 测试运行器");
        System.Console.WriteLine("  过滤器：" + (string.IsNullOrEmpty(filter) ? "(无，跑全部)" : filter));
        System.Console.WriteLine();

        var cases = Discover(filter);
        if (listOnly)
        {
            for (int i = 0; i < cases.Count; i++) { System.Console.WriteLine("  " + cases[i].DisplayName); }
            System.Console.WriteLine();
            System.Console.WriteLine("共 " + cases.Count + " 个用例");
            return 0;
        }

        if (cases.Count == 0)
        {
            System.Console.Error.WriteLine("没有发现任何用例" + (string.IsNullOrEmpty(filter) ? string.Empty : "（过滤器：" + filter + "）"));
            return 1;
        }

        int passed = 0;
        int failed = 0;
        int skipped = 0;
        var failures = new List<string>();
        var slowest = new List<(string Name, double Seconds)>();

        var totalTimer = System.Diagnostics.Stopwatch.StartNew();

        foreach (TestCaseInstance test in cases)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                test.Invoke();
                timer.Stop();
                passed++;
                if (timer.Elapsed.TotalSeconds > 0.5)
                {
                    slowest.Add((test.DisplayName, timer.Elapsed.TotalSeconds));
                }
                System.Console.WriteLine("  [通过] " + test.DisplayName
                    + " (" + timer.Elapsed.TotalMilliseconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms)");
            }
            catch (Assert.SkippedException ex)
            {
                timer.Stop();
                skipped++;
                failed++;
                failures.Add("未执行验收：" + test.DisplayName + " — " + ex.Message);
                System.Console.WriteLine("  [跳过] " + test.DisplayName + " — " + ex.Message);
            }
            catch (Assert.AssertionException ex)
            {
                timer.Stop();
                failed++;
                string message = "  [失败] " + test.DisplayName + "\n         " + ex.Message;
                System.Console.WriteLine(message);
                failures.Add(message);
            }
            catch (System.Exception ex)
            {
                timer.Stop();
                failed++;
                string message = "  [异常] " + test.DisplayName + "\n         " + ex.GetType().Name + ": " + ex.Message
                    + "\n" + Indent(ex.StackTrace, "         ");
                System.Console.WriteLine(message);
                failures.Add(message);
            }
        }

        totalTimer.Stop();

        System.Console.WriteLine();
        System.Console.WriteLine("=== 结果 ===");
        System.Console.WriteLine("  用例总数：" + cases.Count);
        System.Console.WriteLine("  通过：" + passed);
        System.Console.WriteLine("  失败：" + failed);
        System.Console.WriteLine("  跳过：" + skipped);
        System.Console.WriteLine("  总用时：" + totalTimer.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " 秒");

        if (slowest.Count > 0)
        {
            System.Console.WriteLine("  最慢用例：");
            slowest.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
            for (int i = 0; i < slowest.Count && i < 5; i++)
            {
                System.Console.WriteLine("    " + slowest[i].Name + " — "
                    + slowest[i].Seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s");
            }
        }

        if (failed > 0)
        {
            System.Console.Error.WriteLine();
            System.Console.Error.WriteLine(failed + " 个用例失败：");
            for (int i = 0; i < failures.Count; i++)
            {
                System.Console.Error.WriteLine(failures[i]);
            }
            return 1;
        }

        return 0;
    }

    private static string Indent(string? text, string prefix)
    {
        if (string.IsNullOrEmpty(text)) { return string.Empty; }
        return prefix + text.Replace("\n", "\n" + prefix);
    }

    private sealed class TestCaseInstance
    {
        public required MethodInfo Method { get; init; }
        public required object?[] Arguments { get; init; }
        public required string DisplayName { get; init; }

        public void Invoke()
        {
            object? instance = Method.IsStatic ? null : System.Activator.CreateInstance(Method.DeclaringType!);
            try
            {
                Method.Invoke(instance, Arguments);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                // 保留原始异常类型与堆栈，否则运行器会把断言失败误报成反射失败。
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }
    }

    private static List<TestCaseInstance> Discover(string filter)
    {
        var result = new List<TestCaseInstance>();
        Assembly assembly = typeof(TestRunner).Assembly;

        foreach (System.Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) { continue; }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.ReturnType != typeof(void)) { continue; }
                if (method.IsGenericMethodDefinition) { continue; }
                if (method.GetCustomAttribute<SkipAttribute>() != null) { continue; }

                FactAttribute? fact = method.GetCustomAttribute<FactAttribute>();
                TheoryAttribute? theory = method.GetCustomAttribute<TheoryAttribute>();

                if (fact == null && theory == null) { continue; }

                string baseName = type.Name + "." + method.Name;
                int parameterCount = method.GetParameters().Length;

                // [Fact] 必须无参；[Theory] 必须有参（用例数据在方法体内，见 Framework/TestAttributes.cs）。
                if (fact != null)
                {
                    if (parameterCount != 0) { continue; }
                    if (Matches(baseName, filter))
                    {
                        result.Add(new TestCaseInstance
                        {
                            Method = method,
                            Arguments = System.Array.Empty<object?>(),
                            DisplayName = string.IsNullOrEmpty(fact.Name) ? baseName : baseName + " — " + fact.Name,
                        });
                    }
                    continue;
                }

                if (parameterCount == 0) { continue; }
                if (!Matches(baseName, filter)) { continue; }

                result.Add(new TestCaseInstance
                {
                    Method = method,
                    Arguments = System.Array.Empty<object?>(),
                    DisplayName = string.IsNullOrEmpty(theory!.Name) ? baseName + "（参数化）" : baseName + " — " + theory.Name,
                });
            }
        }

        result.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
        return result;
    }

    private static bool Matches(string name, string filter)
        => string.IsNullOrEmpty(filter) || name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
}
