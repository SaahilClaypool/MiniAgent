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
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
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
            WorkingDirectory = gitRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments = "rev-parse --abbrev-ref HEAD"
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
            WorkingDirectory = gitRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // add a new branch named `branchName` based on the current branch
            Arguments = $"worktree add -b \"{branchName}\" \"{worktreePath}\" \"{currentBranch}\""
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

    /// <summary>
    /// Creates a git commit with the provided message in the current git repository.
    /// </summary>
    public static void CreateCommit(string message)
    {
        var gitRoot = GetGitRoot(Directory.GetCurrentDirectory());
        if (gitRoot == null)
            throw new InvalidOperationException("Not inside a git repo.");

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = gitRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments = $"commit -am \"{message.Replace("\"", "\\\"")}\""
        };
        using (var p = Process.Start(psi))
        {
            p!.WaitForExit();
            if (p.ExitCode != 0)
            {
                var err = p.StandardError.ReadToEnd();
                throw new Exception($"git commit failed: {err}");
            }
        }
    }

    /// <summary>
    /// Stages all changes (git add -A) in the current git repository.
    /// </summary>
    public static void AddAll()
    {
        var gitRoot = GetGitRoot(Directory.GetCurrentDirectory());
        if (gitRoot == null)
            throw new InvalidOperationException("Not inside a git repo.");

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = gitRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments = "add -A"
        };
        using (var p = Process.Start(psi))
        {
            p!.WaitForExit();
            if (p.ExitCode != 0)
            {
                var err = p.StandardError.ReadToEnd();
                throw new Exception($"git add -A failed: {err}");
            }
        }
    }

    /// <summary>
    /// Returns the output of git diff with optional arguments. Default is "HEAD~".
    /// </summary>
    /// <param name="diffArgs">Arguments to pass to git diff (e.g., "HEAD~", "HEAD~..HEAD", "--cached").</param>
    public static string Diff(string? diffArgs = null)
    {
        var gitRoot = GetGitRoot(Directory.GetCurrentDirectory());
        if (gitRoot == null)
            throw new InvalidOperationException("Not inside a git repo.");

        var args = "diff";
        if (!string.IsNullOrWhiteSpace(diffArgs))
            args += " " + diffArgs;
        else
            args += " HEAD~";

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = gitRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments = args
        };
        using (var p = Process.Start(psi))
        {
            var output = p!.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new Exception($"git diff failed: {err}");
            return output;
        }
    }

    /// <summary>
    /// Removes a git worktree and its associated branch by branch name.
    /// </summary>
    public static void RemoveWorktree(string branchName)
    {
        var gitRoot = GetGitRoot(Directory.GetCurrentDirectory());
        if (gitRoot == null)
            throw new InvalidOperationException("Not inside a git repo.");

        // sanitize branch name for filesystem
        var invalid = Path.GetInvalidFileNameChars();
        var safeBranch = new string(
            branchName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()
        );

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

        var worktreePath = Path.Combine(baseDir, $"project-{safeBranch}");

        // Remove the worktree using git
        var removePsi = new ProcessStartInfo("git")
        {
            WorkingDirectory = gitRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments = $"worktree remove \"{worktreePath}\""
        };
        using (var p = Process.Start(removePsi))
        {
            p!.WaitForExit();
            if (p.ExitCode != 0)
            {
                var err = p.StandardError.ReadToEnd();
                throw new Exception($"git worktree remove failed: {err}");
            }
        }

        // Delete the branch
        var branchPsi = new ProcessStartInfo("git")
        {
            WorkingDirectory = gitRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments = $"branch -D \"{branchName}\""
        };
        using (var p = Process.Start(branchPsi))
        {
            p!.WaitForExit();
            // If branch doesn't exist, ignore error
        }

        // Remove the directory if it still exists
        if (Directory.Exists(worktreePath))
            Directory.Delete(worktreePath, recursive: true);
    }
}
