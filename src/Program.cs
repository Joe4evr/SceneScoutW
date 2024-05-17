using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SceneScoutW;

sealed partial class Program
{
    private static readonly string[] _scriptFiles = [@"main.py", @"detect.py", @"utilities.py", @"requirements.txt"];

    static Task Main(string[] args)
    {
        Log("SceneScoutWrapper started");
        return new Program().Go(args);
    }

    public DirectoryInfo CurrentDirectory { get; }

    public DirectoryInfo ScriptDirectory { get; }

    [DisallowNull]
    public FileInfo? PythonExe { get; set; }

    public Program()
    {
        CurrentDirectory = new(AppContext.BaseDirectory);
        ScriptDirectory = CurrentDirectory.CreateSubdirectory("scripts");
    }

    private async Task Go(string[] args)
    {
        Log("Checking for prerequisites...");

        var prereqResult = await CheckPrereqs().ConfigureAwait(false);
        if (!prereqResult.IsSuccess)
        {
            Log("Prerequisite checks FAILED: ", prereqResult.Message);
            return;
        }

        Log("Prerequisites are installed");

        if (args.Length == 0)
        {
            Log("Drag files/folders onto the executable to scout for scenes");
            Log("Press any key to close this window");
            Console.ReadKey();
            return;
        }

        await ScoutScenes(args).ConfigureAwait(false);
    }

    private async Task<Result> CheckPrereqs()
    {
        if (!CheckPython())
        {
            var installResult = await InstallPython().ConfigureAwait(false);
            if (!installResult.IsSuccess)
            {
                return installResult;
            }
        }

        Log("Using python location: ", PythonExe!.FullName);

        if (!CheckScripts())
        {
            var installResult = await InstallScripts().ConfigureAwait(false);
            if (!installResult.IsSuccess)
            {
                return installResult;
            }
        }

        Log("Using scripts: ", ScriptDirectory.FullName);

        return Result.Success();
    }

    [MemberNotNullWhen(returnValue: true, member: nameof(PythonExe))]
    private bool CheckPython()
    {
        Log("Checking for python install...");
        if (ExistsOnPath(@"python.exe") is { } fullPath)
        {
            PythonExe = new(fullPath);
            return true;
        }

        return false;
    }

    private async Task<Result> InstallPython()
    {
        Log("Python not installed. Downloading...");
        try
        {
            var fileName = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => @"python-3.12.3-amd64.exe",
                Architecture.X86 => @"python-3.12.3.exe",
                _ => throw new InvalidOperationException(message: "This app does not support this processor architechture.")
            };

            using var client = new HttpClient();
            var response = await client.GetAsync($@"https://www.python.org/ftp/python/3.12.3/{fileName}").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fault(response.ToString());
            }

            var installer = new FileInfo(Path.Join(CurrentDirectory.FullName, fileName));
            using (var fs = installer.Open(FileMode.Create, FileAccess.ReadWrite))
            {
                await response.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            var proc  = Process.Start(new ProcessStartInfo()
            {
                FileName = installer.FullName,
            });
            await proc!.WaitForExitAsync().ConfigureAwait(false);

            if (proc.HasExited && proc.ExitCode == 0
                && ExistsOnPath(@"python.exe") is { } fullPath)
            {
                PythonExe = new(fullPath);
                return Result.Success();
            }

            return Result.Fault("Python was not successfully installed");
        }
        catch (Exception ex)
        {
            return Result.Fault(ex);
        }
    }

    private bool CheckScripts()
    {
        Log("Checking for scripts...");
        return ScriptDirectory.EnumerateFiles()
            .IntersectBy(_scriptFiles, fi => fi.Name)
            .Count() == _scriptFiles.Length;
    }

    private async Task<Result> InstallScripts()
    {
        Log("Scripts not installed. Downloading...");
        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(@"https://github.com/Mark-Shun/SceneScout/archive/refs/heads/main.zip").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fault(response.ToString());
            }


            using var fs = File.Create(response.Content.Headers.ContentDisposition!.FileName!);
            await response.Content.CopyToAsync(fs).ConfigureAwait(false);

            fs.Seek(0, SeekOrigin.Begin);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);

            var prefix = Path.GetFileNameWithoutExtension(fs.Name);
            var scriptDirName = ScriptDirectory.FullName;
            foreach (var targetFile in _scriptFiles)
            {
                if (archive.GetEntry($"{prefix}/{targetFile}") is { } entry)
                {
                    entry.ExtractToFile(Path.Join(scriptDirName, targetFile), overwrite: true);
                }
            }

            return await RunRequirements();
        }
        catch (Exception ex)
        {
            return Result.Fault(ex);
        }
    }

    private async Task<Result> RunRequirements()
    {
        if (ExistsOnPath(@"pip.exe") is not { } pip)
        {
            return Result.Fault("Could not find 'pip.exe', check your python installation");
        }

        Log("Using pip: ", pip);

        var proc = Process.Start(new ProcessStartInfo()
        {
            WorkingDirectory = ScriptDirectory.FullName,
            FileName = pip,
            Arguments = @"install -r requirements.txt --user"
        });
        await proc!.WaitForExitAsync().ConfigureAwait(false);

        return (proc.HasExited && proc.ExitCode == 0)
            ? Result.Success()
            : Result.Fault("Dependencies not installed");
    }

    private async Task ScoutScenes(string[] args)
    {
        var psi = new ProcessStartInfo()
        {
            WorkingDirectory = ScriptDirectory.FullName,
            FileName = PythonExe!.FullName,
            ArgumentList = { _scriptFiles[0] }
        };
        for (int i = 0; i < args.Length; i++)
        {
            psi.ArgumentList.Add(args[i]);
        }

        var proc = Process.Start(psi);
        await proc!.WaitForExitAsync().ConfigureAwait(false);
    }

    private static void Log(string? message)
    {
        Console.Write("SSW> ");
        Console.WriteLine(message);
    }
    private static void Log(string? message1, string? message2)
    {
        Console.Write("SSW> ");
        Console.Write(message1);
        Console.WriteLine(message2);
    }

    private static string? ExistsOnPath(string fileName)
    {
        if (File.Exists(fileName))
        {
            return fileName;
        }

        var values = Environment.GetEnvironmentVariable("PATH").AsSpan();
        foreach (var path in new SpanSplitReader(values, Path.PathSeparator))
        {
            var fullPath = Path.Join(path, fileName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}
