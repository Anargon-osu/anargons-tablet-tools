// Native Windows process-lifetime regression helper. No USB or tablet commands are used.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Gd32Emu;

namespace AnargonsTabletTools.Tests
{
    public static class ProcessLifecycle
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GenerateConsoleCtrlEvent(int kind, int group);

        public static int Main(string[] args)
        {
            try
            {
                string mode = args[0], folder = args[1];
                string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (mode == "leaf")
                {
                    File.WriteAllText(Path.Combine(folder, "leaf.pid"), Process.GetCurrentProcess().Id.ToString());
                    Thread.Sleep(60000);
                    return 0;
                }
                if (mode == "child")
                {
                    if (Console.In.ReadToEnd().Length != 0) return 13;
                    ProcessStartInfo start = new ProcessStartInfo(self, "leaf " + BackupCapture.Quote(folder));
                    start.UseShellExecute = false; start.CreateNoWindow = true;
                    using (Process leaf = Process.Start(start))
                    {
                        File.WriteAllText(Path.Combine(folder, "child.pid"), Process.GetCurrentProcess().Id.ToString());
                        Console.WriteLine("child stdout"); Console.Error.WriteLine("child stderr");
                        Thread.Sleep(60000);
                    }
                    return 0;
                }
                string[] result = null;
                Thread worker = new Thread(delegate()
                {
                    result = P.RunInResult(folder, self, "child " + BackupCapture.Quote(folder), "", false);
                });
                worker.IsBackground = true;
                worker.Start();
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(Path.Combine(folder, "leaf.pid")))
                {
                    if (!worker.IsAlive) throw new Exception("Child failed: " + (result == null ? "no result" : result[1]));
                    if (DateTime.UtcNow > deadline) throw new Exception("Timed out waiting for child.");
                    Thread.Sleep(20);
                }
                File.WriteAllText(Path.Combine(folder, "ready"), "ready");
                if (mode == "exit") Environment.Exit(0);
                if (mode == "cancel")
                {
                    if (!GenerateConsoleCtrlEvent(0, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
                if (mode == "cancel-gate")
                {
                    ManualResetEvent cancelled = new ManualResetEvent(false);
                    Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { e.Cancel = true; cancelled.Set(); };
                    if (!GenerateConsoleCtrlEvent(0, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    if (!cancelled.WaitOne(5000)) throw new Exception("Cancellation handler did not run.");
                    string[] rejected = P.RunInResult(folder, self, "leaf " + BackupCapture.Quote(folder), "", false);
                    if (rejected[0] != "-1" || rejected[1].IndexOf("shutting down", StringComparison.Ordinal) < 0)
                        throw new Exception("A subsequent command was not refused.");
                    File.WriteAllText(Path.Combine(folder, "gate-passed"), "passed");
                    Environment.Exit(0);
                }
                Thread.Sleep(60000);
                return 12;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
