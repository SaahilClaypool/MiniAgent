using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

public class DeveloperPlugin
{
    [KernelFunction]
    [Description("Get repository symbol overview")]
    public async Task<string> RepositoryOverview()
    {
        var psi = new ProcessStartInfo("aider", "--show-repo-map")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        var proc = Process.Start(psi);
        var repoMap = await proc!.StandardOutput.ReadToEndAsync();
        return repoMap;
    }

    [KernelFunction]
    [Description("Search local files using ripgrep")]
    public async Task<string> Rg(string search)
    {
        var rgPath = "rg";
        var arguments = $"\"{search}\" -C 2 --max-columns 200 ";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = rgPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Ripgrep failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }

    [KernelFunction]
    [Description(
        """
            Use the tool to think about something. It will not obtain new information or make any changes to the repository, but just log the thought. Use it when complex reasoning or brainstorming is needed. 

            Common use cases:
            1. When exploring a repository and discovering the source of a bug, call this tool to brainstorm several unique ways of fixing the bug, and assess which change(s) are likely to be simplest and most effective
            2. After receiving test results, use this tool to brainstorm ways to fix failing tests
            3. When planning a complex refactoring, use this tool to outline different approaches and their tradeoffs
            4. When designing a new feature, use this tool to think through architecture decisions and implementation details
            5. When debugging a complex issue, use this tool to organize your thoughts and hypotheses

            The tool simply logs your thought process for better transparency and does not execute any code or make changes.
            """
    )]
    public string Think(string thought)
    {
        return $"Your thought has been logged";
    }

    [KernelFunction]
    [Description("Read File")]
    public string ReadFile(string path)
    {
        return File.ReadAllText(path);
    }

    [KernelFunction]
    [Description("list files")]
    public string ListFiles(string path)
    {
        var files = string.Join(" ", Directory.GetFiles(path));
        var directories = string.Join(" ", Directory.GetDirectories(path));
        return $"Files: {files} Directories: {directories}";
    }

    [KernelFunction]
    [Description("Write File")]
    public string WriteFile(string path, string content)
    {
        File.WriteAllText(path, content);
        return $"wrote content to {path}";
    }

    [KernelFunction]
    [Description(
        """
            Edit a file by providing the path, the text to replace, and the replacement text.
            You should *almost always* use this over `WriteFile` to avoid overwriting the entire file.
            """
    )]
    public string EditFile(string path, string searchText, string replacement)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");

        var content = File.ReadAllText(path);

        if (content.Contains(searchText))
        {
            // replace only the first occurrence
            bool replaced = false;
            content = Regex.Replace(
                content,
                Regex.Escape(searchText),
                m =>
                {
                    if (!replaced)
                    {
                        replaced = true;
                        return replacement;
                    }
                    return m.Value;
                },
                RegexOptions.None,
                TimeSpan.FromSeconds(1)
            );
        }
        else
        {
            // fall back: find best matching line
            var lines = content.Split('\n');
            var best = lines
                .Select(
                    (line, idx) =>
                        new
                        {
                            Line = line,
                            Distance = EditDistance(line.Trim(), searchText.Trim()),
                            Index = idx
                        }
                )
                .OrderBy(x => x.Distance)
                .First();

            // threshold = half the length of the line we're comparing
            if (best.Distance > best.Line.Length / 2)
                throw new ArgumentException(
                    $"Text to replace not found. Closest match (distance {best.Distance}): {best.Line}"
                );

            // replace that single line
            lines[best.Index] = replacement;
            content = string.Join('\n', lines);
        }

        File.WriteAllText(path, content);
        return $"Wrote edits to {path}";
    }

    public static int EditDistance(string s, string t)
    {
        var d = new int[s.Length + 1, t.Length + 1];
        for (var i = 0; i <= s.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++)
            d[0, j] = j;

        for (var i = 1; i <= s.Length; i++)
        {
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(
                        d[i - 1, j] + 1, // deletion
                        d[i, j - 1] + 1
                    ), // insertion
                    d[i - 1, j - 1] + cost // substitution
                );
            }
        }

        return d[s.Length, t.Length];
    }
}
