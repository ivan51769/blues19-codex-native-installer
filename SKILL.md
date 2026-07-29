## Codex 原生安装包独立安装器 v2.0

一个单文件 exe，直接从**微软官方分发服务器**取得 Codex 的离线安装包（`.msix`）并调用系统部署接口安装。

双击 `blues19-codex-native-installer.exe` 即可，**不需要 Node.js、不需要浏览器、不需要任何运行库**，拷到别的 Windows 电脑上照样能跑。

### v2.0 做了什么

v1.x 是用 Puppeteer 驱动 Edge/Chrome 去爬 `store.rg-adguard.net` 拿直链，链路长、依赖多，还经常卡在 Cloudflare 人机验证上。

v2.0 改成直接走微软自己的接口，把整条链路收进一个 141 KB 的原生程序里：

| | v1.x | v2.0 |
|---|---|---|
| 运行依赖 | Node.js + npm + puppeteer-core + Edge/Chrome | 无（Windows 自带的 .NET Framework 4.x） |
| 直链来源 | 第三方站点 store.rg-adguard.net | 微软 DisplayCatalog + Windows Update 分发服务 |
| 人机验证 | 经常需要手动过 Cloudflare | 没有这一步 |
| 安装方式 | 起 PowerShell 跑 Add-AppxPackage | 直接调系统 WinRT 部署接口（快约 12 倍，能拿到真正的错误码） |
| 分发形态 | 一堆 .bat + scripts 目录 + node_modules | 单个 exe |
| 检查一次耗时 | 20~120 秒（还要看验证码） | 约 8 秒 |

### 工作原理

1. **查分类 ID**：`displaycatalog.mp.microsoft.com` → 拿到 Codex 的 `WuCategoryId`
2. **取票据**：向 `fe3.delivery.mp.microsoft.com` 请求匿名访问票据
3. **同步更新树**：反复调用 `SyncUpdates`，把已见到的节点回传，一层层剥到安装包本体（通常 3~4 轮）
4. **换取直链**：用安装包的 UpdateID 调 `GetExtendedUpdateInfo2`，得到带签名的 CDN 直链
5. **多线程下载**：8 线程分块 + 断点续传 + 大小与 SHA-1 双重校验
6. **安装**：调用 `Windows.Management.Deployment.PackageManager` 部署

### 使用

双击 `blues19-codex-native-installer.exe` 打开界面，默认自动检查更新。界面上的按钮：

- **一键更新**：检查 → 下载 → 安装，一路到底
- **检查更新**：只看本机版本和最新版本
- **仅下载**：只把安装包下到工作目录，不安装
- **安装本地包**：跳过网络，直接装工作目录里已有的安装包
- **网络设置**：下载 CDN 连不上时在这里配代理（支持自动探测本地代理端口）
- **打开目录 / 打开日志 / 取消**

也支持命令行参数，方便做成快捷方式或计划任务：

```text
blues19-codex-native-installer.exe                 打开界面并自动检查更新
blues19-codex-native-installer.exe --update        自动完成 检查 → 下载 → 安装
blues19-codex-native-installer.exe --check         只检查
blues19-codex-native-installer.exe --download      只下载
blues19-codex-native-installer.exe --install-local 直接安装本地已有的包
blues19-codex-native-installer.exe --help          显示帮助
```

### 输出位置

产物默认放在 exe 所在目录；该目录不可写时（比如放在 `Program Files`）会自动改用 `%LOCALAPPDATA%\Blues19\CodexInstaller`。

- `OpenAI.Codex_<版本>_x64__2p2nqsd0c76g0.msix`：下载到的安装包，装完也会保留
- `install.log`：本次运行日志（每次启动清空重写）
- `logs\`：每次退出自动归档一份，文件名形如 `install-20260727-001530-update-success.log`，保留最近 100 份
- `status.json`：当前/最后一次运行状态，供外部脚本读取
- `settings.ini`：网络设置（不填就是跟随系统代理）

### 常见问题

**下载一直超时，但检查更新是正常的**
微软的下载 CDN（`dl.delivery.mp.microsoft.com`）在国内网络下经常连不上，而查询用的 API 域名是通的。程序会自动依次尝试 HTTP、HTTPS、绝对域名写法三种连法；都不通就点**网络设置**填代理，或先开好你的代理工具再重试。

**提示 Codex 正在运行**
系统不能替换正在使用的文件。程序会弹窗让你选：立即关闭 Codex 安装、或者先装好等下次退出 Codex 时自动生效（推荐，不会丢未保存的内容）。

**安装失败并显示一串 0x8007xxxx**
程序已经把常见错误码翻译成了中文处理建议，按提示做即可。完整的系统原文在日志里。

**要装到别的电脑上**
把 `blues19-codex-native-installer.exe` 单独拷过去就行，不需要带任何其它文件。要求 Windows 10 版本 2004（内部版本 19041）及以上——这也是 Codex 本身的要求。

**首次运行弹出「Windows 已保护你的电脑」**
这个 exe 没有购买代码签名证书，所以从网上/邮件传过去的副本会被 SmartScreen 拦一次。点「更多信息 → 仍要运行」即可，之后不再提示。用 U 盘或局域网直接拷贝通常不会触发。

**放的目录层级太深**
Windows 路径上限 260 字符，分块下载还要在包名后再加后缀。程序会在下载前检查并提示，把工具挪到浅一点的目录（比如 `D:\CodexInstaller`）就行。

### 从源码构建

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

用的是 Windows 自带的 C# 编译器（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`），**不需要装 Visual Studio 或 .NET SDK**，也不需要联网。

源码在 `src\`：

| 文件 | 职责 |
|---|---|
| `Program.cs` | 入口、命令行参数、单实例、全局异常兜底 |
| `MainForm.cs` | 界面与流程编排 |
| `StoreApi.cs` | 商店目录查询、架构过滤、版本选择 |
| `Fe3Client.cs` | Windows Update 分发服务的 SOAP 客户端 |
| `Downloader.cs` | 多线程分块下载、断点续传、完整性校验 |
| `AppxInstaller.cs` | 版本检测与安装（WinRT 优先，PowerShell 兜底） |
| `WinRtAppx.cs` | WinRT 部署接口的隔离封装 |
| `Settings.cs` | 代理配置与本地代理探测 |
| `Logger.cs` / `Util.cs` | 日志、归档、路径、格式化 |
| `ProgressPanel.cs` / `Glass.cs` / `IconFactory.cs` | 自绘进度条、窗口外观、图标 |

注意：编译器只支持 **C# 5** 语法——没有字符串插值、`?.`、`nameof`、表达式体成员。改代码时注意别用新语法。

界面排版自检（离屏渲染，不占用桌面）：

```powershell
.\blues19-codex-native-installer.exe --render-ui out.png 1.5   # 模拟 150% 缩放
```
