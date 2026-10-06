using System.Runtime.CompilerServices;

// ---------------------------------------------------------------------------
// 程序集级特性（刻意写在源码里，而不是 .csproj 的 MSBuild 项里）
//
// 为什么：MSBuild 的 <InternalsVisibleTo> 项依赖 SDK 生成 AssemblyInfo 才生效，
// 而本仓库的降级构建通道（Roslyn csc 直编，见 docs/development/build.md）不走 SDK。
// 写成源码特性后，通道 A 与通道 B 的行为完全一致 —— 否则会出现
// "有 SDK 时测试全绿、没有 SDK 时 Core 编不过"这种极难排查的双通道分叉。
//
// 授权给测试程序集的原因：ActionSearch 的选靶辅助是"实现细节"（不该出现在公共 API 上，
// 否则表现层会直接调用它并绕过 AI 的行动一致性），
// 但排查行为异常（"为什么没人去采食物"）时必须能单独调用它们。
// ---------------------------------------------------------------------------

[assembly: InternalsVisibleTo("SandBoxSim.Tests")]
