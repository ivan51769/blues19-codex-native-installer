# 用 Windows 自带的 C# 编译器把 src\*.cs 编成单文件 exe。
# 不需要 Visual Studio、不需要 .NET SDK、不需要联网。
# 用法： powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Root    = Split-Path -Parent $MyInvocation.MyCommand.Definition
$SrcDir  = Join-Path $Root 'src'
$ProgramPath = Join-Path $SrcDir 'Program.cs'
$ProgramText = Get-Content -LiteralPath $ProgramPath -Raw -Encoding UTF8
$VersionMatch = [regex]::Match($ProgramText, 'public\s+const\s+string\s+AppVersion\s*=\s*"([^"]+)"')
if (-not $VersionMatch.Success) {
    throw "无法从 $ProgramPath 读取 AppVersion。"
}
$AppVersion = $VersionMatch.Groups[1].Value
$BuildDate = Get-Date -Format 'yyyy-MM-dd'
$ArtifactName = "blues19-codex-native-installer-v$AppVersion-$BuildDate.exe"
$OutExe  = Join-Path $Root $ArtifactName
$IcoPath = Join-Path $SrcDir 'app.ico'
$LogoPath= Join-Path $SrcDir 'wechat-logo.png'
$Manifest= Join-Path $SrcDir 'app.manifest'

function Write-Head($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "    $text" -ForegroundColor Green }
function Write-Warn2($text){ Write-Host "    $text" -ForegroundColor Yellow }

# ---- 1. 定位编译器 -----------------------------------------------------------
Write-Head '定位 C# 编译器'
$cscCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$Csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Csc) {
    throw "没有找到 csc.exe。本机可能缺少 .NET Framework 4.x（Windows 10/11 默认自带）。"
}
Write-Ok $Csc

# ---- 2. 收集源码 -------------------------------------------------------------
Write-Head '收集源码'
Write-Ok "输出文件：$ArtifactName"
$Sources = Get-ChildItem -Path $SrcDir -Filter *.cs -File | Sort-Object Name
if (-not $Sources) { throw "src 目录下没有找到 .cs 源文件。" }
$Sources | ForEach-Object { Write-Ok $_.Name }
if (-not (Test-Path $LogoPath)) { throw "缺少公众号 Logo：$LogoPath" }
Write-Ok 'wechat-logo.png（嵌入单文件 EXE）'

$FxDir = Split-Path -Parent $Csc
$Refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Xml.dll',
    '/r:System.Xml.Linq.dll',
    # 直接调用系统的 MSIX 部署接口。csc.exe 能直接 /r: 系统自带的 .winmd，
    # 不需要任何 SDK / 参考程序集。少了 System.Runtime.dll 会报 CS0012（System.Attribute 未引用）。
    "/r:$FxDir\System.Runtime.dll",
    "/r:$FxDir\System.Runtime.WindowsRuntime.dll",
    "/r:$env:WINDIR\System32\WinMetadata\Windows.Management.winmd",
    "/r:$env:WINDIR\System32\WinMetadata\Windows.Foundation.winmd",
    "/r:$env:WINDIR\System32\WinMetadata\Windows.ApplicationModel.winmd",
    "/r:$env:WINDIR\System32\WinMetadata\Windows.Storage.winmd",
    "/r:$env:WINDIR\System32\WinMetadata\Windows.System.winmd"
)

foreach ($r in $Refs) {
    $p = $r.Substring(3)
    if ($p -match '[\\/]' -and -not (Test-Path $p)) {
        throw "缺少编译依赖：$p（本机可能不是 Windows 10/11）。"
    }
}

$CommonArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug-',
    '/warnaserror-',
    '/nowarn:1701,1702',
    '/utf8output',
    # 源码固定按 UTF-8 解析。不指定的话 csc 会在没有 BOM 时回落到系统 ANSI 代码页，
    # 换一台非中文系统的机器编译，源码里的中文就全乱了。
    '/codepage:65001',
    '/main:Blues19.CodexInstaller.Program',
    "/win32manifest:$Manifest",
    "/resource:$LogoPath,Blues19.CodexInstaller.WeChatLogo.png"
) + $Refs

# ---- 3. 生成图标（第一遍编译产出临时 exe，再由它画出 .ico） -------------------
if (-not (Test-Path $IcoPath)) {
    Write-Head '生成应用图标'
    $TempExe = Join-Path $env:TEMP ("codex-installer-iconpass-{0}.exe" -f ([guid]::NewGuid().ToString('N')))
    & $Csc @CommonArgs "/out:$TempExe" @($Sources.FullName)
    if ($LASTEXITCODE -ne 0) { throw "图标预编译失败（退出码 $LASTEXITCODE）。" }
    & $TempExe --emit-icon $IcoPath | Out-Null
    if (-not (Test-Path $IcoPath)) { throw '图标生成失败。' }
    Remove-Item $TempExe -Force -ErrorAction SilentlyContinue
    Write-Ok ("app.ico  {0:N0} 字节" -f (Get-Item $IcoPath).Length)
} else {
    Write-Ok "复用已有图标 src\app.ico（想重新生成就删掉它）"
}

# ---- 4. 正式编译 -------------------------------------------------------------
Write-Head '编译'
if (Test-Path $OutExe) { Remove-Item $OutExe -Force }
& $Csc @CommonArgs "/win32icon:$IcoPath" "/out:$OutExe" @($Sources.FullName)
if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）。" }

$info = Get-Item $OutExe
Write-Ok ("{0}   {1:N0} 字节 ({2:N1} KB)" -f $info.Name, $info.Length, ($info.Length / 1KB))

# ---- 5. 冒烟检查 -------------------------------------------------------------
Write-Head '冒烟检查'
$probe = Join-Path $env:TEMP ("codex-icon-probe-{0}.ico" -f ([guid]::NewGuid().ToString('N')))
& $OutExe --emit-icon $probe | Out-Null
if ($LASTEXITCODE -eq 0 -and (Test-Path $probe)) {
    Write-Ok '可执行文件启动正常。'
    Remove-Item $probe -Force -ErrorAction SilentlyContinue
} else {
    Write-Warn2 "冒烟检查未通过（退出码 $LASTEXITCODE），请手动运行确认。"
}

Write-Host ''
Write-Host "构建完成：$OutExe" -ForegroundColor Green
Write-Host '这个 exe 不依赖 Node.js / 浏览器 / 任何运行库，可以直接拷到别的 Windows 电脑使用。' -ForegroundColor Gray
