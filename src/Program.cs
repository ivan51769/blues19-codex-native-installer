using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Blues19.CodexInstaller
{
    public static class Program
    {
        /// <summary>
        /// 单实例锁真正要保护的是「同一个工作目录」——分块文件、install.log、status.json 都在那儿。
        /// 所以锁名按工作目录取哈希：放在两个不同文件夹的副本可以同时跑，互不干扰。
        ///
        /// 用 Local\ 而不是 Global\：Global 是整机唯一，会出现「另一个 Windows 账号开着，
        /// 你就被挡住」的情况，而且那个互斥体属于别的账号时 new Mutex 会直接抛
        /// UnauthorizedAccessException。Codex 本来就是按用户安装的，按会话隔离才对。
        /// </summary>
        private static string BuildMutexName()
        {
            string key;
            try { key = Paths.BaseDir.ToLowerInvariant(); }
            catch { key = "default"; }

            try
            {
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                    return @"Local\Blues19.CodexInstaller." +
                           BitConverter.ToString(hash).Replace("-", string.Empty);
                }
            }
            catch
            {
                return @"Local\Blues19.CodexInstaller.SingleInstance";
            }
        }
        private static Logger _log;

        [STAThread]
        public static int Main(string[] rawArgs)
        {
            // 构建期内部开关：把图标画成 .ico 供 csc.exe /win32icon 使用，
            // 这样图标和窗口图标共用同一份绘制代码，不会画风不一致。
            if (rawArgs != null && rawArgs.Length == 2 &&
                rawArgs[0].TrimStart('-', '/').Equals("emit-icon", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    IconFactory.WriteIcoFile(rawArgs[1]);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            // 构建期内部开关：把界面离屏渲染成 PNG，用来核对不同缩放比例下的排版，
            // 不需要把窗口摆到前台，也就不会打扰正在用电脑的人。
            if (rawArgs != null && rawArgs.Length >= 2 &&
                rawArgs[0].TrimStart('-', '/').Equals("render-ui", StringComparison.OrdinalIgnoreCase))
            {
                return RenderUi(rawArgs[1], rawArgs.Length > 2 ? rawArgs[2] : null,
                    rawArgs.Length > 3 ? rawArgs[3] : null);
            }

            string action = ParseAction(rawArgs);
            if (action == "help")
            {
                MessageBox.Show(HelpText(), "Codex 原生安装器 — 命令行参数",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            // 同目录同时跑两个实例会互相覆盖分块文件与日志，这里直接挡掉。
            // 拿不到互斥体不该让程序起不来——单实例是保护措施，不是运行前提。
            Mutex mutex = null;
            bool isFirst = true;
            try
            {
                mutex = new Mutex(true, BuildMutexName(), out isFirst);
            }
            catch (Exception)
            {
                mutex = null;
                isFirst = true;
            }

            if (!isFirst)
            {
                MessageBox.Show("Codex 原生安装器已经在运行了。\n请切换到已打开的窗口继续操作。",
                    "已在运行", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (mutex != null) mutex.Close();
                return 0;
            }

            try
            {
                return Run(action);
            }
            finally
            {
                if (mutex != null)
                {
                    // 进程被强杀过会留下 abandoned 状态，ReleaseMutex 可能抛，忽略即可
                    try { mutex.ReleaseMutex(); }
                    catch { }
                    try { mutex.Close(); }
                    catch { }
                }
            }
        }

        private static int Run(string action)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Http.Init();

            _log = new Logger(true);
            _log.Info("Codex 原生安装器 v" + AppVersion + " 启动（" + RuntimeSummary() + "）");
            _log.Info("TLS 设置：" + Http.ProtocolSummary());
            if (_unknownArgs.Count > 0)
            {
                _log.Warn("忽略了无法识别的参数：" + string.Join(" ", _unknownArgs.ToArray()) +
                          "（用 --help 查看支持的参数）");
            }

            if (!CheckOsSupported()) return 1;

            // 进程被强杀（关控制台、任务管理器结束）时 FormClosing 不会触发，
            // 这里补一道，尽量保住本次日志的归档副本。
            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                try { if (_log != null) _log.Archive("exit", "unknown"); }
                catch { }
            };

            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                ReportFatal(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                ReportFatal(e.ExceptionObject as Exception);
            };

            try
            {
                using (MainForm form = new MainForm(_log, action))
                {
                    Application.Run(form);
                }
                return 0;
            }
            catch (Exception ex)
            {
                ReportFatal(ex);
                return 1;
            }
        }

        /// <summary>
        /// 离屏渲染界面用于排版自检。scaleOverride 可以模拟高分屏（如 1.5、2.0），
        /// 这样不用真的换一台机器就能验证缩放后的排版。
        /// </summary>
        private static int RenderUi(string outPath, string scaleOverride, string installerUpdateTag)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                float scale = 0;
                if (!string.IsNullOrEmpty(scaleOverride))
                    float.TryParse(scaleOverride, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out scale);

                if (scale > 0) MainForm.PresetScale(scale);
                Logger log = new Logger(false);
                using (MainForm form = new MainForm(log, "none"))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(-32000, -32000);
                    form.Show();
                    Application.DoEvents();
                    if (!string.IsNullOrEmpty(installerUpdateTag))
                    {
                        form.PreviewInstallerUpdate(installerUpdateTag);
                        Application.DoEvents();
                    }

                    using (System.Drawing.Bitmap bmp =
                        new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    // winexe 没有控制台，结果写到文件里给构建脚本读
                    System.IO.File.WriteAllText(outPath + ".txt",
                        form.Width + "x" + form.Height + " scale=" + scale);
                    form.Hide();
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(outPath + ".txt", "ERROR " + ex); }
                catch { }
                return 1;
            }
        }

        public const string AppVersion = "2.1.0";

        /// <summary>
        /// Codex 的清单要求 Windows 10 2004（内部版本 19041）及以上。
        /// 与其让用户下载完 700MB 再被部署服务拒绝，不如一开始就说清楚。
        /// （清单里声明了 Win10 supportedOS，所以这里的 OSVersion 是真实版本而不是兼容性垫片值。）
        /// </summary>
        private static bool CheckOsSupported()
        {
            try
            {
                Version os = Environment.OSVersion.Version;
                if (os.Major > 10 || (os.Major == 10 && os.Build >= 19041)) return true;

                _log.Error("系统版本过低：" + os + "，Codex 需要 Windows 10 2004（19041）及以上。");
                MessageBox.Show(
                    "Codex 要求 Windows 10 版本 2004（内部版本 19041）或更高。\n\n" +
                    "当前系统版本：" + os + "\n\n请先升级 Windows 后再安装。",
                    "系统版本不支持", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch
            {
                return true;   // 判断不出来就别拦着用户
            }
        }

        private static string RuntimeSummary()
        {
            try
            {
                return "Windows " + Environment.OSVersion.Version +
                       (Environment.Is64BitOperatingSystem ? " x64" : " x86") +
                       " / .NET " + Environment.Version;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string ParseAction(string[] args)
        {
            if (args == null) return string.Empty;
            string action = string.Empty;

            // 把所有参数都扫一遍再返回。一遇到认识的就 return 的话，
            // 排在它后面的拼写错误永远不会被发现。
            foreach (string raw in args)
            {
                string a = (raw ?? string.Empty).Trim().ToLowerInvariant().TrimStart('-', '/');
                string matched = MatchAction(a);
                if (matched != null)
                {
                    if (action.Length == 0) action = matched;
                }
                else if (a.Length > 0)
                {
                    _unknownArgs.Add(raw);
                }
            }
            return action;
        }

        /// <summary>认识这个参数就返回对应动作；不认识返回 null。</summary>
        private static string MatchAction(string a)
        {
            switch (a)
            {
                case "update":
                case "auto":
                case "install":
                    return "update";
                case "check":
                case "check-only":
                    return "check";
                case "download":
                case "download-only":
                    return "download";
                case "install-local":
                case "resume":
                case "continue":
                    return "install-local";
                case "help":
                case "?":
                case "h":
                    return "help";
                default:
                    return null;
            }
        }

        private static readonly System.Collections.Generic.List<string> _unknownArgs =
            new System.Collections.Generic.List<string>();

        private static string HelpText()
        {
            string exeName = "blues19-codex-native-installer-v" + AppVersion + "-<YYYY-MM-DD>.exe";
            try { exeName = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("用法：" + exeName + " [参数]");
            sb.AppendLine();
            sb.AppendLine("  不带参数        打开界面并自动检查更新");
            sb.AppendLine("  --update        打开界面并自动完成 检查 → 下载 → 安装");
            sb.AppendLine("  --check         只检查最新版本");
            sb.AppendLine("  --download      只下载最新安装包，不安装");
            sb.AppendLine("  --install-local 直接安装工作目录里已有的安装包");
            sb.AppendLine("  --help          显示本帮助");
            sb.AppendLine();
            sb.AppendLine("工作目录：" + Paths.BaseDir);
            return sb.ToString();
        }

        private static void ReportFatal(Exception ex)
        {
            string message = ex != null ? ex.ToString() : "未知错误";
            try { if (_log != null) _log.Error("发生未处理的异常：" + message); }
            catch { }
            try { if (_log != null) _log.Archive("fatal", "failed"); }
            catch { }

            string shown = ex != null ? ex.Message : "未知错误";
            try
            {
                MessageBox.Show(
                    "程序遇到未处理的错误：\n\n" + shown + "\n\n详细信息已写入日志：\n" +
                    (_log != null ? _log.LogPath : Paths.LogFile),
                    "出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
