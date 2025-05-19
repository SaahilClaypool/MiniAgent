using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MyAgent;

public static class GitHelper
{
    /// <summary>
    /// Returns true if <paramref name="path"/> (or any parent) contains a .git folder.
    /// </summary>
    public static bool IsGitDirectory(string path)
    {
        return GetGitRoot(path) != null;
    }

    /// <summary>
    /// Walks up from <paramref name="startPath"/> to find the git root (folder containing .git), or null.
    /// </summary>
    public static string? GetGitRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Creates a new git worktree for <paramref name="branchName"/> under the per-OS agent directory.
    /// </summary>
    public static string CreateWorktree(string branchName)
    {
        var gitRoot = GetGitRoot(Directory.GetCurrentDirectory());
        if (gitRoot == null)
            throw new InvalidOperationException("Not inside a git repo.");

        // ────────────────────────────────────────────────────────────────────────────────
        // 1) discover the current branch
        // ────────────────────────────────────────────────────────────────────────────────
        var revParse = new ProcessStartInfo("git")
        {
            WorkingDirectory    = gitRoot,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            Arguments              = "rev-parse --abbrev-ref HEAD"
        };
        string currentBranch;
        using (var rev = Process.Start(revParse)!)
        {
            currentBranch = rev.StandardOutput.ReadToEnd().Trim();
            rev.WaitForExit();
            if (rev.ExitCode != 0)
                throw new Exception($"git rev-parse failed: {rev.StandardError.ReadToEnd()}");
        }

        // sanitize branch name for filesystem
        var invalid = Path.GetInvalidFileNameChars();
        var safeBranch = new string(
            branchName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()
        );

        // build base agent folder
        string baseDir;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "agent"
            );
        }
        else
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "agent"
            );
        }

        // ensure it exists
        Directory.CreateDirectory(baseDir);

        // full worktree path
        var worktreePath = Path.Combine(baseDir, $"project-{safeBranch}");

        // remove existing if any
        if (Directory.Exists(worktreePath))
            Directory.Delete(worktreePath, recursive: true);

        // git worktree add
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory    = gitRoot,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            // add a new branch named `branchName` based on the current branch
            Arguments              = $"worktree add -b \"{branchName}\" \"{worktreePath}\" \"{currentBranch}\""
        };
        using (var p = Process.Start(psi))
        {
            p!.WaitForExit();
            if (p.ExitCode != 0)
            {
                var err = p.StandardError.ReadToEnd();
                throw new Exception($"git worktree failed: {err}");
            }
        }

        return worktreePath;
    }
}
