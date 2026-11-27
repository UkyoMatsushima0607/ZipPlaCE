using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZipPla
{
    public static class NgenManager
    {
        private const string InstallCommand = "<NGEN_INSTALL>";
        private const string UninstallCommand = "<NGEN_UNINSTALL>";

        public static async Task<int> InstallByOtherProcess(string acceptor, string ngen, string target)
        {
            using (var p = Process.Start(new ProcessStartInfo(acceptor, $"{InstallCommand} \"{ngen}\" \"{target}\"") { Verb = "runas" }))
            {
                await Task.Run(() => p.WaitForExit());
                return p.ExitCode;
            }
        }

        public static int UninstallByOtherProcess(string acceptor, string ngen, string target)
        {
            using (var p = Process.Start(new ProcessStartInfo(acceptor, $"{UninstallCommand} \"{ngen}\" \"{target}\"") { Verb = "runas" }))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        public static bool CommandLineAcceptor(string deleteTarget, string[] cmds, out int exitCode)
        {
            exitCode = 0;
            if (cmds == null || cmds.Length <= 1) return false;
            var cmds1 = cmds[1];
            var install = cmds1 == InstallCommand;
            if (!install && cmds1 != UninstallCommand) return false;
            if (cmds.Length != 4)
            {
                exitCode = -1;
                return true;
            }
            var ngen = cmds[2];
            var target = cmds[3];
            try
            {
                if (deleteTarget != null)
                {
                    Display(ngen, deleteTarget, out var ngenRoots, out var ngenRootsThatDependOnTarget, out var nativeImages);
                    foreach (var path in ngenRootsThatDependOnTarget)
                    {
                        if (path == target ? !install : !File.Exists(path))
                        {
                            exitCode = ExecNgenForEdit(ngen, "uninstall", path);
                            if (exitCode != 0) return true;
                        }
                    }
                }

                if (install) exitCode = ExecNgenForEdit(ngen, "install", target);
            }
            catch
            {
                exitCode = -1;
            }
            return true;
        }

        private static int ExecNgenForEdit(string ngen, string action, string target)
        {
            using (var p = Process.Start(new ProcessStartInfo(ngen, $"{action} \"{target}\" /nologo")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            }))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        public static async Task<bool> IsInstalledAsync(string target, string executablePath, string nativeImageWithoutNiExe)
        {
            var suffix = $"\\{nativeImageWithoutNiExe}.ni.exe";
            if (!executablePath.EndsWith(suffix)) executablePath = null;
            return await Task.Run(() =>
            {
                using (var process = Process.GetCurrentProcess())
                {
                    foreach (ProcessModule m in process.Modules)
                    {
                        var fileName = m.FileName;
                        if (fileName.EndsWith(suffix) && fileName != executablePath)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            });
        }


        public static bool LostNativeImageExists(string ngen, string target, params string[] ignoreFiles)
        {
            Display(ngen, target, out var ngenRoots, out var ngenRootsThatDependOnTarget, out var nativeImages);
            return ngenRootsThatDependOnTarget.Any(path => ignoreFiles.Contains(path) || !File.Exists(path));
        }
        
        private static async Task<bool> ExecNgenForEditAsync(string ngen, string action, string target)
        {
            using (var p = Process.Start(GetNgenStartInfo(ngen, action, target, runas: true)))
            {
                await Task.Run(() => p.WaitForExit());
                return p.ExitCode == 0;
            }
        }

        private static ProcessStartInfo GetNgenStartInfo(string ngen, string action, string target, bool runas)
        {
            var psi = new ProcessStartInfo(ngen, $"{action} \"{target}\" /nologo") { CreateNoWindow = true };
            if (runas)
            {
                psi.Verb = "runas";
            }
            else
            {
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
            }
            return psi;
        }

        private static void Display(string ngen, string target, out string[] ngenRoots, out string[] ngenRootsThatDependOnTarget, out string[] nativeImages)
        {
            using (var p = Process.Start(GetNgenStartInfo(ngen, "display", target, runas: false)))
            {
                var lists = new List<string>[3] { new List<string>(), new List<string>(), new List<string>() };
                var standardOutput = p.StandardOutput;
                string line;
                var mode = -1;
                var ngenRootsDependOnTargetString = $"NGEN Roots that depend on \"{target}\":";

                while ((line = standardOutput.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line == "NGEN Roots:") mode = 0;
                    else if (line == ngenRootsDependOnTargetString) mode = 1;
                    else if (line == "Native Images:") mode = 2;
                    else if (line.EndsWith(":")) mode = -1;
                    else if (mode >= 0 && line != "") lists[mode].Add(line);
                }
                ngenRoots = lists[0].ToArray();
                ngenRootsThatDependOnTarget = lists[0].ToArray();
                nativeImages = lists[0].ToArray();
                
                p.WaitForExit();
            }
        }

        public static bool ShouldBeInstalled(string target, string defaultParentFolderName)
        {
            target = Path.GetFullPath(target);

            // ngen.exe のデフォルト値を on にする場合、試用ユーザー向けにこの処理を加えたほうが望ましい

            bool In(string dir)
            {
                if (dir == null) return false;
                if (dir.Length == 0 || dir.Last() != Path.DirectorySeparatorChar) dir += Path.DirectorySeparatorChar;
                return target.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
            }
            
            string GetTempPathWithoutPlatformNotSupportedException()
            {
                try
                {
                    return Path.GetTempPath();
                }
                catch (System.Security.SecurityException)
                {
                    return null;
                }
            }

            if (In(GetTempPathWithoutPlatformNotSupportedException())) return false;

            string GetFolderPathWithoutPlatformNotSupportedException(Environment.SpecialFolder folder)
            {
                try
                {
                    return Environment.GetFolderPath(folder);
                }
                catch (PlatformNotSupportedException)
                {
                    return null;
                }
            }

            if (In(GetFolderPathWithoutPlatformNotSupportedException(Environment.SpecialFolder.Templates))) return false;
            if (In(GetFolderPathWithoutPlatformNotSupportedException(Environment.SpecialFolder.CommonTemplates))) return false;
            
            var drive = new DriveInfo(target);
            if (drive.DriveType != DriveType.Fixed) return false;

            return true;
        }

        public static string GetNgenPath()
        {
            try
            {
                var ngenPath = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "ngen.exe");
                return File.Exists(ngenPath) ? ngenPath : null;
            }
            catch
            {
                return null;
            }
        }

    }
}
