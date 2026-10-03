<#
.SYNOPSIS
    SandBoxSim 双通道构建脚本。

.DESCRIPTION
    本仓库刻意支持两条构建通道，理由见 docs/12-Milestones.md：

      通道 A（首选）：机器上有 .NET 8 SDK 时，走标准 dotnet build / dotnet run。
      通道 B（降级）：机器上只有 .NET 运行时 + Roslyn csc（例如只装了 VS2022）时，
                      直接用 csc.exe 编译 BCL-only 源码，再由 dotnet 运行时托管执行。
                      本仓库不引用任何 NuGet 包，因此通道 B 与通道 A 行为一致。

    脚本会自动探测，也可用 -Channel sdk|csc 强制指定。

.EXAMPLE
    .\tools\build.ps1                       # 自动探测，构建全部
    .\tools\build.ps1 -Mode run             # 构建并启动 TUI
    .\tools\build.ps1 -Mode test            # 构建并运行测试，失败返回非零退出码
    .\tools\build.ps1 -Mode snapshot -SnapshotDays 100
#>

# 本文件是**函数库**：只有函数定义与模块级常量，没有任何 param() 块。
# 为什么特意拆出 tools/build.ps1 作为 CLI：
#   包装脚本（run.ps1/test.ps1/dev.ps1）需要 dot-source 本文件来复用这些函数。
#   如果本文件自己带 param()，点源时它的参数默认值会覆盖调用方同名变量
#   （实测：run.ps1 的 $Mode=digest 被本文件的默认值 build 覆盖，导致 run.ps1 变成"直接构建"）。
#   把 param 块挪到 CLI 文件后，库文件点源不再污染任何调用方变量。


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 路径与常量
# ---------------------------------------------------------------------------

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:BuildRoot = Join-Path $script:RepoRoot 'artifacts'
$script:FrameworkMajorMinor = '8.0'

# ---------------------------------------------------------------------------
# 平台适配（CI 在 windows / ubuntu / macos 三个 runner 上都跑同一套脚本）
# ---------------------------------------------------------------------------
#
# 这一节存在的理由：CI 曾经在 Linux/macOS 上直接崩在
#   Cannot find drive. A drive with the name 'C' does not exist.
# 根因有两个，都是"把 Windows 的写法当成了通用写法"：
#   1. 可执行文件名写死成 'dotnet.exe'（非 Windows 上叫 'dotnet'）；
#   2. 兜底的 SDK 路径写死成 'C:\Users\...\net8'，在 Linux 上连
#      Join-Path 都无法求值（Join-Path 'C:\...' 'dotnet' 会去访问 C: 盘）。
#
# 修法不是"再加一个 if"，而是**把平台差异收敛到两个函数里**：
# 所有路径与可执行名都从这里取，其它地方一行都不用改。

$script:SandBoxSimIsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

# 当前平台上的 dotnet 可执行文件名。
function Get-DotnetExeName {
    if ($script:SandBoxSimIsWindows) { return 'dotnet.exe' }
    return 'dotnet'
}

# 本项目私有 SDK 的默认安装位置（由 tools/install-sdk.ps1 使用）。
#
# 三平台统一放在**用户主目录**下的 .sandboxsim-tool/net8。
# 为什么用主目录而不是 LOCALAPPDATA / Application Support：三个平台都有 HOME，
# 一条规则就够；而且它必须与 install-sdk.ps1 的实际安装位置一致 ——
# 实测踩过：本机 SDK 装在 ~/.sandboxsim-tool/net8，而探测逻辑找的是
# %LOCALAPPDATA%\sandboxsim-tool\net8，于是"明明装了 SDK 却退回通道 B"，
# 而且**没有任何报错**（只是悄悄降级），非常难发现。
#
# **绝不硬编码盘符**：一旦出现 'C:\...' 这种字面量，非 Windows 平台上
# 连 Join-Path 都会抛异常（Join-Path 会去访问那个盘符）。
function Get-DefaultSdkRoot {
    $homeDir = $null
    if ($env:HOME) { $homeDir = $env:HOME }
    elseif ($env:USERPROFILE) { $homeDir = $env:USERPROFILE }
    else { $homeDir = [System.Environment]::GetFolderPath('UserProfile') }
    if (-not $homeDir) { $homeDir = [System.IO.Path]::GetTempPath() }

    return (Join-Path $homeDir '.sandboxsim-tool/net8')
}

# 系统级 dotnet 安装根目录（只用于"兜底探测"）。
function Get-SystemDotnetRoots {
    $roots = New-Object System.Collections.Generic.List[string]

    if ($script:SandBoxSimIsWindows) {
        $programFiles = if ($env:ProgramFiles) { $env:ProgramFiles } else { $null }
        if ($programFiles) { $roots.Add((Join-Path $programFiles 'dotnet')) }
    }
    else {
        $roots.Add('/usr/share/dotnet')
        $roots.Add('/usr/local/share/dotnet')
        if ($env:HOME) { $roots.Add((Join-Path $env:HOME '.dotnet')) }
    }

    return $roots
}

function Get-BuildPaths {
    param([string]$Configuration)

    $bin = Join-Path $script:BuildRoot $Configuration
    [pscustomobject]@{
        Root      = $bin
        Core      = Join-Path $bin 'SandBoxSim.Core.dll'
        Console   = Join-Path $bin 'SandBoxSim.Console.dll'
        Tests     = Join-Path $bin 'SandBoxSim.Tests.dll'
        ObjRoot   = Join-Path $script:BuildRoot "obj/$Configuration"
    }
}

function Get-CSharpSourceList {
    param(
        [Parameter(Mandatory)][string]$ProjectDir,
        [string[]]$AlsoInclude = @()
    )

    $dirs = @($ProjectDir) + $AlsoInclude
    $files = New-Object System.Collections.Generic.List[string]
    foreach ($d in $dirs) {
        $full = Join-Path $script:RepoRoot $d
        if (-not (Test-Path $full)) { continue }
        Get-ChildItem -Path $full -Recurse -Filter '*.cs' -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj|artifacts)[\\/]' } |
            Sort-Object FullName |
            ForEach-Object { $files.Add($_.FullName) }
    }
    return $files.ToArray()
}

# ---------------------------------------------------------------------------
# 工具链探测
# ---------------------------------------------------------------------------

function Select-CompatibleSdk {
    param([string[]]$Installed)

    $matching = @($Installed | ForEach-Object {
        if ($_ -match '^([0-9]+\.[0-9]+\.[0-9]+)\s+\[(.+)\]$') {
            $version = [version]$Matches[1]
            if ("$($version.Major).$($version.Minor)" -eq $script:FrameworkMajorMinor) {
                [pscustomobject]@{ Version = $version; Root = Split-Path -Parent $Matches[2] }
            }
        }
    } | Sort-Object Version -Descending)
    if ($matching.Count -gt 0) { return $matching[0] }
    return $null
}

function Resolve-DotnetSdk {
    param([string]$Explicit)

    $exeName = Get-DotnetExeName
    $candidates = New-Object System.Collections.Generic.List[string]

    # 显式路径**必须先检查它是不是一个真实存在的目录**再 Join-Path：
    # 否则在 Linux 上把 'C:\...' 交给 Join-Path 会直接抛
    # "Cannot find drive. A drive with the name 'C' does not exist."
    # —— 一个"路径不存在"的问题被报成了"盘符不存在"，非常难懂。
    if ($Explicit -and (Test-Path -LiteralPath $Explicit -PathType Container)) {
        $candidates.Add((Join-Path $Explicit $exeName))
    }
    if ($env:SANDBOXSIM_DOTNET_ROOT -and (Test-Path -LiteralPath $env:SANDBOXSIM_DOTNET_ROOT -PathType Container)) {
        $candidates.Add((Join-Path $env:SANDBOXSIM_DOTNET_ROOT $exeName))
    }
    if ($env:DOTNET_ROOT -and (Test-Path -LiteralPath $env:DOTNET_ROOT -PathType Container)) {
        $candidates.Add((Join-Path $env:DOTNET_ROOT $exeName))
    }

    $default = Get-DefaultSdkRoot
    if (Test-Path -LiteralPath $default -PathType Container) {
        $candidates.Add((Join-Path $default $exeName))
    }

    $onPath = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($onPath) { $candidates.Add($onPath.Source) }

    foreach ($root in Get-SystemDotnetRoots) {
        if (Test-Path -LiteralPath $root -PathType Container) {
            $candidates.Add((Join-Path $root $exeName))
        }
    }

    foreach ($exe in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $exe)) { continue }
        # Ask the host: PATH entries on Unix often point to a symlink outside the SDK root.
        $installed = @(& $exe --list-sdks 2>$null)
        if ($LASTEXITCODE -ne 0) { continue }
        $selected = Select-CompatibleSdk -Installed $installed
        if (-not $selected) { continue }
        $hostPath = Join-Path $selected.Root $exeName
        if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) { continue }
        return [pscustomobject]@{ Exe = $hostPath; Root = $selected.Root; Version = $selected.Version.ToString() }
    }
    return $null
}

# 运行时共享目录（通道 B 的兜底引用来源，也是 runtimeconfig 版本来源）
function Resolve-DotnetRuntime {
    param([string]$DotnetExe)

    $roots = New-Object System.Collections.Generic.List[string]
    if ($DotnetExe) { $roots.Add((Split-Path -Parent $DotnetExe)) }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) { $roots.Add((Split-Path -Parent $dotnet.Source)) }
    foreach ($r in Get-SystemDotnetRoots) { $roots.Add($r) }

    foreach ($r in $roots) {
        $shared = Join-Path $r 'shared/Microsoft.NETCore.App'
        if (-not (Test-Path $shared)) { continue }
        $dir = Get-ChildItem $shared -Directory |
               Where-Object { $_.Name -match "^$([regex]::Escape($script:FrameworkMajorMinor))\.\d+$" } |
               Sort-Object { [version]$_.Name } -Descending |
               Select-Object -First 1
        if ($dir) {
            return [pscustomobject]@{ Root = $r; Version = $dir.Name; Dir = $dir.FullName }
        }
    }
    return $null
}

function Resolve-RoslynCsc {
    param([string]$DotnetRoot)

    # 首选 dotnet SDK 自带的 Roslyn（版本与 SDK 一致，且三平台都有）
    if ($DotnetRoot -and (Test-Path $DotnetRoot)) {
        $sdkDir = Get-ChildItem (Join-Path $DotnetRoot 'sdk') -Directory -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -match '^8\.0\.\d+$' } |
                  Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if ($sdkDir) {
            $csc = Join-Path $sdkDir.FullName 'Roslyn/bincore/csc.dll'
            if (Test-Path $csc) { return [pscustomobject]@{ Kind = 'dll'; Path = $csc } }
        }
    }

    # 其次 VS2022 自带的 csc.exe（只有 Windows 才有；非 Windows 上这段直接跳过）
    if (-not $script:SandBoxSimIsWindows) { return $null }

    if (-not $env:ProgramFiles) { return $null }
    $vsRoots = @('Community', 'Professional', 'Enterprise', 'BuildTools') |
        ForEach-Object { Join-Path $env:ProgramFiles "Microsoft Visual Studio/2022/$_" }
    foreach ($vs in $vsRoots) {
        $exe = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe'
        if (Test-Path $exe) { return [pscustomobject]@{ Kind = 'exe'; Path = $exe } }
    }
    return $null
}

function Resolve-ReferenceAssemblies {
    param([string]$DotnetRoot, [string]$RuntimeDir)

    # 首选：SDK 的 targeting pack（正确的 ref 程序集）
    if ($DotnetRoot) {
        $pack = Join-Path $DotnetRoot "packs\Microsoft.NETCore.App.Ref"
        if (Test-Path $pack) {
            $ver = Get-ChildItem $pack -Directory |
                Where-Object { $_.Name -match '^8\.0\.\d+$' } |
                Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
            if ($ver) {
                $refDir = Join-Path $ver.FullName "ref/net$($script:FrameworkMajorMinor)"
                if (Test-Path $refDir) { return [pscustomobject]@{ Dir = $refDir; Kind = 'refpack' } }
            }
        }
    }
    # 兜底：shared 运行时里的托管 DLL。必须按内容过滤掉原生 DLL（coreclr/clrjit/...），
    # 否则 csc 会报 CS0009「PE 映像不包含任何托管元数据」。
    if ($RuntimeDir) { return [pscustomobject]@{ Dir = $RuntimeDir; Kind = 'runtime-filter' } }
    return $null
}

function Test-PortableExecutableHasMetadata {
    param([string]$Path)

    try {
        $fs = [System.IO.File]::OpenRead($Path)
        try {
            $br = New-Object System.IO.BinaryReader($fs)
            if ($br.ReadUInt16() -ne 0x5A4D) { return $false }
            $fs.Position = 0x3C
            $peOffset = $br.ReadInt32()
            $fs.Position = $peOffset
            if ($br.ReadUInt32() -ne 0x00004550) { return $false }
            $fs.Position = $peOffset + 6
            $numSections = $br.ReadUInt16()
            $fs.Position = $peOffset + 20
            $optSize = $br.ReadUInt16()
            $fs.Position = $peOffset + 24
            $magic = $br.ReadUInt16()
            $dataDirBase = if ($magic -eq 0x20B) { $peOffset + 24 + 112 } else { $peOffset + 24 + 96 }
            # 第 15 个数据目录项（从 0 计数 14）= COM Descriptor / CLR Runtime Header
            $fs.Position = $dataDirBase + (14 * 8)
            $rva = $br.ReadUInt32()
            return ($rva -ne 0)
        }
        finally { $fs.Dispose() }
    }
    catch { return $false }
}

function Get-ReferenceArgs {
    param([string]$RefDir, [string]$Kind)

    $dlls = Get-ChildItem $RefDir -Filter '*.dll' -File
    if ($Kind -eq 'runtime-filter') {
        $dlls = $dlls | Where-Object { Test-PortableExecutableHasMetadata $_.FullName }
    }
    return ($dlls | Sort-Object Name | ForEach-Object { '-r:' + $_.FullName })
}

function Get-ManagedRuntimeVersion {
    param([string]$RuntimeDir, [string]$Fallback)

    # 共享框架目录名就是可加载的精确版本（如 8.0.21）
    $name = Split-Path -Leaf $RuntimeDir
    if ($name -match '^\d+\.\d+\.\d+') { return $name }
    return $Fallback
}

# ---------------------------------------------------------------------------
# 编译
# ---------------------------------------------------------------------------

function Invoke-SdkBuild {
    param([string]$DotnetExe, [string]$Configuration, [bool]$ParallelBuild = $false)

    $env:DOTNET_ROOT = Split-Path -Parent $DotnetExe
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

    $sln = Join-Path $script:RepoRoot 'SandBoxSim.sln'

    # -nodeReuse:false -maxCpuCount:1 不是"为了稳"的迷信，而是**受限环境下唯一能跑通的方式**：
    # MSBuild 的多进程构建依赖**命名管道**做节点间通信。在文件/进程沙箱（AI 协作环境、
    # 部分企业安全策略）下命名管道被拦截，restore 会在 `_GenerateRestoreProjectPathWalk`
    # 里静默失败 —— 输出是"生成失败 / 0 个错误"，连一行错误都没有，极难排查。
    # 串行构建牺牲一点速度（本项目 3 个工程全量约 20 秒，增量几乎瞬时），换来"在哪都能构建"。
    # 环境允许命名管道时可以加 -ParallelBuild 找回并行速度。
    $buildArgs = @('build', $sln, '-c', $Configuration, '-nologo', '-v', 'minimal')
    if ($Configuration -eq 'Release') { $buildArgs += '-p:DebugType=none' }
    if (-not $ParallelBuild) { $buildArgs += @('-nodeReuse:false', '-maxCpuCount:1') }

    # 注意：外部命令的输出也会进入管道并成为本函数的返回值（PowerShell 的经典坑），
    # 因此这里显式 Out-Host，只把输出打印给用户，不污染返回值。
    & $DotnetExe @buildArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build FAILED (exit code $LASTEXITCODE)" }

    # 注意：Copy-Item 默认会把复制结果写进管道，导致本函数的返回值变成"对象数组"，
    # 调用方 $build.Paths.Root 就会报 "property not found"。
    # 因此所有有副作用的命令都必须 Out-Null（这是 PowerShell 函数返值的经典坑）。
    #
    # 另外：构建输出要搬到 artifacts/ 供脚本直接运行，但 artifacts 里可能正有程序在跑
    # （同一台机器上开了两个 TUI、或者上一个长跑还没退），文件会被锁住。
    # 直接 Copy-Item 会抛 IOException 把整个构建搞失败，而实际原因只是"文件正在被使用"。
    # 因此这里做一个简短的退避重试，并在彻底失败时给出**能看懂的原因**。
    $paths = Get-BuildPaths -Configuration $Configuration
    New-Item -ItemType Directory -Force -Path $paths.Root | Out-Null

    function Copy-Artifact {
        param([string]$Source, [string]$DestinationDir)
        for ($attempt = 1; $attempt -le 4; $attempt++) {
            try {
                Copy-Item $Source -Destination $DestinationDir -Force -ErrorAction Stop
                return
            }
            catch [System.IO.IOException] {
                if ($attempt -eq 4) {
                    throw "Cannot copy $Source -> $DestinationDir because the target is locked " +
                          "(another SandBoxSim instance is probably still running). " +
                          "Close it, or use -Configuration Release to keep artifacts in a separate folder."
                }
                Start-Sleep -Milliseconds (150 * $attempt)
            }
        }
    }

    foreach ($name in @('SandBoxSim.Core', 'SandBoxSim.Console', 'SandBoxSim.Tests')) {
        $projDir = Join-Path $script:RepoRoot "src\$name\bin\$Configuration\net$($script:FrameworkMajorMinor)"
        foreach ($leaf in @("$name.dll", "$name.pdb", "$name.deps.json", "$name.runtimeconfig.json")) {
            $src = Join-Path $projDir $leaf
            if (Test-Path $src) { Copy-Artifact -Source $src -DestinationDir $paths.Root }
        }
        # Console 的依赖（Core.dll 已在上面复制）
        $coreFromConsole = Join-Path $projDir 'SandBoxSim.Core.dll'
        if (Test-Path $coreFromConsole) { Copy-Artifact -Source $coreFromConsole -DestinationDir $paths.Root }
    }
    return $paths
}

function Invoke-CscBuild {
    param(
        [string]$Configuration,
        [string]$DotnetRoot,
        [string]$RuntimeDir,
        [string]$CscPath,
        [string]$CscKind
    )

    $paths = Get-BuildPaths -Configuration $Configuration
    New-Item -ItemType Directory -Force -Path $paths.Root | Out-Null
    New-Item -ItemType Directory -Force -Path $paths.ObjRoot | Out-Null

    $refs = Resolve-ReferenceAssemblies -DotnetRoot $DotnetRoot -RuntimeDir $RuntimeDir
    if (-not $refs) { throw 'Channel B FAILED: no usable reference assemblies (neither targeting pack nor shared runtime found)' }
    Write-BuildLog "引用来源：$($refs.Dir)（$($refs.Kind)）"

    $refArgs = @(Get-ReferenceArgs -RefDir $refs.Dir -Kind $refs.Kind)
    if ($refArgs.Count -lt 10) { throw "Channel B FAILED: too few reference assemblies ($($refArgs.Count)); probe is likely wrong" }

    # 通道 B 的编译约定（必须与通道 A 的 dotnet build 对齐）：
    #   * Core 是类库，Console/Tests 是可执行程序 —— 因此 -target 必须逐工程指定。
    #     （csc 收到 .cs 文件时默认按控制台程序处理，于是没有 Main 的 Core 会报
    #       CS5001 "Program does not contain a static 'Main' method"。这个坑一开始
    #       被别的编译选项掩盖了，属于定时炸弹，所以现在显式声明。）
    #   * Debug 配置下带上 -define:DEBUG;TRACE。
    $defineArgs = if ($Configuration -eq 'Release') { @() } else { @('-define:DEBUG;TRACE') }
    $commonLangArgs = if ($Configuration -eq 'Release') {
        @('-nologo', '-noconfig', '-nostdlib+', '-langversion:latest', '-nullable:enable',
          '-preferreduilang:en-US', '-nowarn:CS1701,CS1702,CS8019,CS8632',
          '-optimize+', '-debug-') + $defineArgs
    }
    else {
        @('-nologo', '-noconfig', '-nostdlib+', '-langversion:latest', '-nullable:enable',
          '-preferreduilang:en-US', '-nowarn:CS1701,CS1702,CS8019,CS8632',
          '-optimize-', '-debug:portable') + $defineArgs
    }
    $libraryArgs = $commonLangArgs + @('-target:library')
    $consoleArgs = $commonLangArgs + @('-target:exe')

    $invoke = {
        param([string]$Out, [string]$OutRef, [string[]]$Sources, [string[]]$ExtraRefs, [string[]]$ExtraArgs)

        $argv = New-Object System.Collections.Generic.List[string]
        foreach ($a in $ExtraArgs) { $argv.Add($a) }
        $argv.Add("-out:$Out")
        if ($OutRef) { $argv.Add("-refout:$OutRef") }
        foreach ($r in $ExtraRefs) { $argv.Add($r) }
        foreach ($r in $refArgs) { $argv.Add($r) }
        foreach ($s in $Sources) { $argv.Add($s) }

        if ($CscKind -eq 'dll') {
            & (Join-Path $DotnetRoot (Get-DotnetExeName)) $CscPath @($argv) | Out-Host
        } else {
            & $CscPath @($argv) | Out-Host
        }
        if ($LASTEXITCODE -ne 0) { throw "csc compilation FAILED (exit code $LASTEXITCODE): $Out" }
    }

    Write-BuildLog '通道 B：编译 SandBoxSim.Core（类库）'
    & $invoke $paths.Core (Join-Path $paths.ObjRoot 'SandBoxSim.Core.ref.dll') `
        (Get-CSharpSourceList -ProjectDir 'src\SandBoxSim.Core') @() $libraryArgs

    Write-BuildLog '通道 B：编译 SandBoxSim.Console（可执行）'
    & $invoke $paths.Console (Join-Path $paths.ObjRoot 'SandBoxSim.Console.ref.dll') `
        (Get-CSharpSourceList -ProjectDir 'src\SandBoxSim.Console') @("-r:$($paths.Core)") $consoleArgs

    Write-BuildLog '通道 B：编译 SandBoxSim.Tests（可执行）'
    & $invoke $paths.Tests (Join-Path $paths.ObjRoot 'SandBoxSim.Tests.ref.dll') `
        (Get-CSharpSourceList -ProjectDir 'src\SandBoxSim.Tests') @("-r:$($paths.Core)", "-r:$($paths.Console)") $consoleArgs

    $runtimeVersion = Get-ManagedRuntimeVersion -RuntimeDir $RuntimeDir -Fallback "$($script:FrameworkMajorMinor).0.0"
    foreach ($app in @('SandBoxSim.Console', 'SandBoxSim.Tests')) {
        $rc = Join-Path $paths.Root "$app.runtimeconfig.json"
        @{
            runtimeOptions = @{
                tfm        = "net$($script:FrameworkMajorMinor)"
                framework  = @{ name = 'Microsoft.NETCore.App'; version = $runtimeVersion }
                # 键名含点号，必须加引号（否则 PowerShell 会把它当成属性访问）
                'configProperties' = @{ 'System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization' = $false }
            }
        } | ConvertTo-Json -Depth 6 | Set-Content -Path $rc -Encoding UTF8
    }
    return $paths
}

function Write-BuildLog {
    param([string]$Message)
    Write-Host "  [build] $Message" -ForegroundColor DarkGray
}

function Invoke-Build {
    param(
        [string]$Channel,
        [string]$Configuration,
        [string]$SdkRoot,
        [bool]$ParallelBuild = $false
    )

    $paths = Get-BuildPaths -Configuration $Configuration

    # Only a user-supplied root takes precedence over environment overrides.
    $sdk = Resolve-DotnetSdk -Explicit $SdkRoot
    $sdkExe = if ($sdk) { $sdk.Exe } else { $null }
    $runtime = Resolve-DotnetRuntime -DotnetExe $sdkExe

    $useChannel = $Channel
    if ($useChannel -eq 'auto') {
        $useChannel = if ($sdk) { 'sdk' } else { 'csc' }
        if (-not $sdk) { Write-Host 'No .NET SDK found - falling back to channel B (Roslyn csc)' -ForegroundColor Yellow }
    }

    if ($useChannel -eq 'sdk') {
        if (-not $sdk) { throw '-Channel sdk requested but no .NET 8 SDK found. Run tools/install-sdk.ps1 to install one.' }
        Write-Host "Channel A: dotnet $($sdk.Version) @ $($sdk.Root)" -ForegroundColor Cyan
        $paths = Invoke-SdkBuild -DotnetExe $sdk.Exe -Configuration $Configuration -ParallelBuild $ParallelBuild
    }
    else {
        if (-not $runtime) { throw 'Channel B FAILED: .NET 8 runtime not found (shared\Microsoft.NETCore.App\8.x)' }
        $sdkRootOrEmpty = if ($sdk) { $sdk.Root } else { '' }
        $csc = Resolve-RoslynCsc -DotnetRoot $sdkRootOrEmpty
        if (-not $csc) { throw 'Channel B FAILED: Roslyn csc not found (no SDK and no VS2022 Roslyn)' }
        Write-Host "Channel B: csc [$($csc.Kind)] @ $($csc.Path); runtime $($runtime.Version)" -ForegroundColor Cyan
        $paths = Invoke-CscBuild -Configuration $Configuration `
            -DotnetRoot $sdkRootOrEmpty -RuntimeDir $runtime.Dir `
            -CscPath $csc.Path -CscKind $csc.Kind
    }

    Write-Host "Build complete: $($paths.Root)" -ForegroundColor Green
    return [pscustomobject]@{ Paths = $paths; Channel = $useChannel; Sdk = $sdk; Runtime = $runtime }
}

# ---------------------------------------------------------------------------
# 运行
# ---------------------------------------------------------------------------

function Get-DotnetHost {
    param($Sdk, $Runtime)

    if ($Sdk) { return $Sdk.Exe }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) { return $dotnet.Source }
    if ($Runtime) { return (Join-Path $Runtime.Root (Get-DotnetExeName)) }
    return 'dotnet'
}

function Get-ConsoleArgs {
    param([string]$Mode, [int]$Seed, [int]$Days, [int]$Ticks, [int]$Width, [int]$Height,
          [string]$ConfigPath, [string]$OutDir, [int]$SnapshotDays, [int[]]$Seeds, [bool]$NoColor,
          [string]$SeedRange = '', [int]$Agents = 0, [int]$AgentRadius = 0)

    $a = New-Object System.Collections.Generic.List[string]
    switch ($Mode) {
        'run'      { }                                  # 默认就是交互模式
        'headless' { $a.Add('--headless') }
        'digest'   { $a.Add('--digest') }
        'batch'    { $a.Add('--batch') }
        'snapshot' { $a.Add('--headless'); $a.Add('--snapshot') }
        default    { }
    }
    $a.Add("--seed"); $a.Add("$Seed")
    if ($Days -gt 0 -and ($Mode -ne 'run')) { $a.Add("--days"); $a.Add("$Days") }
    if ($Ticks -gt 0) { $a.Add("--ticks"); $a.Add("$Ticks") }
    if ($Width -gt 0) { $a.Add("--width"); $a.Add("$Width") }
    if ($Height -gt 0) { $a.Add("--height"); $a.Add("$Height") }
    if ($ConfigPath) { $a.Add("--config"); $a.Add($ConfigPath) }
    if ($OutDir) { $a.Add("--out"); $a.Add($OutDir) }
    if ($SnapshotDays -gt 0) { $a.Add("--snapshot-days"); $a.Add("$SnapshotDays") }
    # --seeds 优先使用 -SeedRange 的原始字符串：这样 "1..20" 与 "1,2,7" 都能原样交给
    # 目标程序的 IntList（它同时支持区间与列表两种写法）。
    # 为什么需要这个参数：用 `powershell -File run.ps1 -Seeds 1,2,3` 时，逗号会被当作
    # **参数分隔符**，实际只传进来 "1"，于是静默变成"只跑一个种子"。字符串形式能绕开这个陷阱。
    if ($SeedRange) {
        $a.Add("--seeds"); $a.Add($SeedRange)
    }
    elseif ($Seeds.Count -gt 0) {
        $a.Add("--seeds"); $a.Add(($Seeds -join ','))
    }
    if ($Agents -gt 0) { $a.Add("--agents"); $a.Add("$Agents") }
    if ($AgentRadius -gt 0) { $a.Add("--agent-radius"); $a.Add("$AgentRadius") }
    if ($NoColor) { $a.Add('--no-color') }
    return $a.ToArray()
}

function Invoke-Run {
    param(
        [string]$Mode, [string]$Channel, [string]$Configuration, [string]$SdkRoot,
        [int]$Seed, [int]$Days, [int]$Ticks, [int]$Width, [int]$Height,
        [string]$ConfigPath, [string]$OutDir, [int]$SnapshotDays, [int[]]$Seeds, [bool]$NoColor,
        [string]$SeedRange = '',
        [int]$Agents = 0, [int]$AgentRadius = 0,
        [bool]$ParallelBuild = $false
    )

    $build = Invoke-Build -Channel $Channel -Configuration $Configuration -SdkRoot $SdkRoot -ParallelBuild $ParallelBuild
    $hostExe = Get-DotnetHost -Sdk $build.Sdk -Runtime $build.Runtime

    if ($Mode -eq 'test') {
        & $hostExe $build.Paths.Tests
        exit $LASTEXITCODE
    }

    $argv = @($build.Paths.Console) + (Get-ConsoleArgs -Mode $Mode -Seed $Seed -Days $Days -Ticks $Ticks `
        -Width $Width -Height $Height -ConfigPath $ConfigPath -OutDir $OutDir `
        -SnapshotDays $SnapshotDays -Seeds $Seeds -NoColor $NoColor -SeedRange $SeedRange `
        -Agents $Agents -AgentRadius $AgentRadius)

    Write-Host "Run: dotnet $($argv -join ' ')" -ForegroundColor DarkGray
    $old = $env:DOTNET_ROOT
    $env:DOTNET_ROOT = if ($build.Sdk) { $build.Sdk.Root } else { $build.Runtime.Root }
    try {
        & $hostExe @argv
        exit $LASTEXITCODE
    }
    finally { $env:DOTNET_ROOT = $old }
}

function Invoke-Clean {
    if (Test-Path $script:BuildRoot) { Remove-Item $script:BuildRoot -Recurse -Force }
    foreach ($proj in @('SandBoxSim.Core', 'SandBoxSim.Console', 'SandBoxSim.Tests')) {
        foreach ($d in @('bin', 'obj')) {
            $p = Join-Path $script:RepoRoot "src\$proj\$d"
            if (Test-Path $p) { Remove-Item $p -Recurse -Force }
        }
    }
    Write-Host 'Build artifacts cleaned' -ForegroundColor Green
}
