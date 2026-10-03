using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems.Actions;

/// <summary>
/// 仅供测试使用的转发器：把 <see cref="ActionSearch"/> 的选靶逻辑暴露给测试程序集。
///
/// 为什么不直接把 ActionSearch 改成 public：
/// 表现层只需要"个体在做什么"，不需要看到"怎么找目标"这些实现细节 ——
/// 一旦暴露，就会有人从 UI 或脚本里直接调用它，从而绕过 AI 的行动一致性。
/// 因此这里用一个显式的测试转发类，把"给测试用"这件事写在名字上。
///
/// 可见性由 Core 项目里的 <c>InternalsVisibleTo</c> 授权（只对 SandBoxSim.Tests 开放）。
/// </summary>
internal static class ActionSearchProbe
{
    public static bool TryFindResource(in ActionContext ctx, ResourceKind kind, out Int2 found)
        => ActionSearch.TryFindResource(in ctx, kind, out found);

    public static bool TryFindWaterAccess(in ActionContext ctx, out Int2 found)
        => ActionSearch.TryFindWaterAccess(in ctx, out found);

    public static float HomeDistance01(in ActionContext ctx) => ActionSearch.HomeDistance01(in ctx);
}
