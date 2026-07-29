using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// 这一行不是装饰品，它决定了程序能不能走 HTTPS。
// csc.exe 默认不会写入 TargetFrameworkAttribute，CLR 于是套用 .NET 4.0 的兼容性开关，
// ServicePointManager.SecurityProtocol 的默认值退化成 Ssl3|Tls，所有到微软服务器的
// HTTPS 请求都会直接报“未能创建 SSL/TLS 安全通道”。加上它以后默认值变为 SystemDefault，
// 由系统协商到 TLS 1.2/1.3。
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

[assembly: AssemblyTitle("Codex 原生安装器")]
[assembly: AssemblyDescription("拾玖说跨境AI出品；作者：拾玖Blues。用于从微软官方分发服务器获取并安装 Codex 离线安装包")]
[assembly: AssemblyCompany("拾玖说跨境AI")]
[assembly: AssemblyProduct("Codex Native Installer")]
[assembly: AssemblyCopyright("Copyright (C) 2026 拾玖Blues")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: AssemblyInformationalVersion("2.0.0")]
[assembly: ComVisible(false)]
