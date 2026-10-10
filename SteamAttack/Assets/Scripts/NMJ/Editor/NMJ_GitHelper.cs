using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace SteamAttack.EditorTools
{
    /// <summary>에디터에서 git 명령을 실행하고 출력을 돌려준다 (NMJ 작업용 도구).</summary>
    public static class NMJ_GitHelper
    {
        public static string Run(string args)
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return $"[exit {p.ExitCode}]\n{o}{e}";
            }
        }
    }
}
