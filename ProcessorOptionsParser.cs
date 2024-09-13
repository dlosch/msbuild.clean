using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Msbuild.Clean;

internal record class ProcessorOptions(string RootPath = ".",
    bool DryRun = true,
    string? MSBuildPath = default,
    ConfirmLevel Confirm = ConfirmLevel.Dir,
    LogLevel Log = LogLevel.Warning,
    int? Parallel = default,
    ProcessorMode Mode = ProcessorMode.Default
    ) {
    internal const int DefaultParallelValue = 6;
}

internal record class ProcessorDirOptions(string RootPath,
    string Filter = "*.sln",
    Predicate<string>? FileNameFilter = default,
    int Depth = 0,
    bool DryRun = true,
    string? MSBuildPath = default,
    ConfirmLevel Confirm = ConfirmLevel.Dir,
    LogLevel Log = LogLevel.Warning,
    int? Parallel = default,
    ProcessorMode Mode = ProcessorMode.Default)
    : ProcessorOptions(RootPath, DryRun, MSBuildPath, Confirm, Log, Parallel, Mode) {
    internal static bool FilterSupportedSlnFileFormats(string file) =>
        // no support for slnx
        file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) 
        || file.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
}

internal enum ConfirmLevel {
    Force, // none

    Sln,
    Proj,
    Dir,
}
internal enum LogLevel {
    Debug,
    Verbose,
    Info,
    Warning,
    Error,
}

internal static class ProcessorOptionsParser {
    private static ProcessorDirOptions DefaultCurrentDirOptions(string root) => new ProcessorDirOptions(root);

    internal static bool TryParseArgs(string[] args, [NotNullWhen(true)] out ProcessorOptions? options, [NotNullWhen(false)] out IEnumerable<string>? errorsOut) {
        options = default;
        errorsOut = default;

        if (args.Length == 0) {
            options = DefaultCurrentDirOptions(Environment.CurrentDirectory);
        }
        else {
            var errors = new List<string>();
            var hasError = false;
            var forceFlag = false;
            var delete = default(bool?);
            var dryRunFlag = default(bool?);
            var deleteNupkg = default(bool?);
            var deleteEmptyDirsFlag = false;
            var deleteFilesOnlyFlag = false;
            var nonCurrentOnly = false;
            var objFolder = default(bool?);
            var confirmLevel = default(ConfirmLevel?);
            var logLevel = default(LogLevel?); // LogLevel.Warning;
            var msbuildPath = default(string?);
            var rootPath = default(string?);
            var parallel = default(int?);
            var depth = default(int?);


            for (int idx = 0; idx < args.Length; idx++) {
                var curArg = args[idx];
                var nextArgRead = false;

                var nextArgAvailable = idx < args.Length - 1;
                switch (curArg) {
                    case "-h":
                    case "--help":
                        void PrintUsage() {
                            const int align = -60;
                            Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Name);
                            Console.WriteLine($"\t{"-h|--help",align}Print help.");
                            Console.WriteLine($"\t{"-v|--verbosity|--log Debug,Verbose,Info,[Warning],Error",align}Log level.");
                            //Console.WriteLine("--confirm");
                            Console.WriteLine($"\t{"--force",align}Do not confirm delete.");
                            Console.WriteLine($"\t{"--dry-run",align}[Default] Do not delete anything.");
                            Console.WriteLine($"\t{"--delete-empty-directories",align}Also delete empty directories.");
                            Console.WriteLine($"\t{"--nupkg",align}Also delete $(PackageId)<version>.nupkg from $(PackageOutputPath).");
                            Console.WriteLine($"\t{"--delete-files",align}Delete files instead of directories.");
                            Console.WriteLine($"\t{"--non-current|--noncurrent",align}Only delete directories labelled after tfm not in TargetFramework(s).");
                            Console.WriteLine($"\t{"--obj",align}[Default]Also delete BaseIntermediateOutputPath.");
                            Console.WriteLine($"\t{"--msbuild",align}Explicit path to msbuild.exe or msbuild.dll.");
                            Console.WriteLine($"\t{"--root",align}[Default: .]Root directory or .sln. Can also be specified as last parameter.");
                            Console.WriteLine($"\t{"--depth",align}If --root is directory, specify how many levels are scanned for .sln.");
                            Console.WriteLine($"\t{"-p|--parallel <int>",align}Parallel processing.");
                        }
                        errorsOut = errors;
                        PrintUsage();
                        return false;
                    case "-v":
                    case "--verbosity":
                    case "--log": {
                        if (logLevel.HasValue) {
                            errors.Add("duplicate -v|--verbosity switch");
                            break;
                        }
                        if (!nextArgAvailable) {
                            errors.Add("-v|--verbosity requires one of (Debug,Verbose,Info,[Warning],Error)");
                            break;
                        }
                        nextArgRead = true;
                        if (!Enum.TryParse<LogLevel>(args[idx + 1], out var ll)) {
                            errors.Add($"invalid level '{args[idx + 1]}' for {curArg} switch. Valid values: {string.Join(",", Enum.GetNames<LogLevel>())}");
                            break;
                        }
                        logLevel = ll;
                    }
                    break;
                    case "--confirm": {
                        if (confirmLevel.HasValue) {
                            errors.Add("duplicate --confirm switch");
                            break;
                        }

                        if (!nextArgAvailable) {
                            hasError = true;
                            break;
                        }
                        nextArgRead = true;
                        if (!Enum.TryParse<ConfirmLevel>(args[idx + 1], out var ll)) {
                            errors.Add($"invalid level '{args[idx + 1]}' for {curArg} switchs. Valid values: {string.Join(",", Enum.GetNames<ConfirmLevel>())}");
                            break;
                        }
                        confirmLevel = ll;
                    }
                    break;
                    case "--dry-run":
                        if (delete ?? false) {
                            errors.Add("--delete and --dry-run are mutually exclusive.");
                            break;
                        }
                        dryRunFlag = true;
                        break;
                    case "--delete":
                        if (dryRunFlag ?? false) {
                            errors.Add("--delete and --dry-run are mutually exclusive.");
                            break;
                        }
                        delete = true;
                        break;
                    case "--force":
                        forceFlag = true;
                        break;
                    case "--delete-empty-directories":
                        deleteEmptyDirsFlag = true;
                        break;
                    case "--nupkg":
                        deleteNupkg = true;
                        break;
                    case "--delete-files":
                        deleteFilesOnlyFlag = true;
                        break;
                    case "--non-current":
                    case "--noncurrent":
                        nonCurrentOnly = true;
                        if (!objFolder.HasValue) objFolder = false;
                        break;
                    case "--obj":
                        objFolder = true;
                        break;

                    case "--msbuild": {
                        if (msbuildPath is not null) {
                            errors.Add("duplicate --msbuild switch");
                            break;
                        }

                        if (!nextArgAvailable) {
                            hasError = true;
                            break;
                        }
                        nextArgRead = true;
                        msbuildPath = args[idx + 1];
                    }
                    break;
                    case "--root": {
                        if (rootPath is not null) {
                            errors.Add("duplicate --root switch");
                            break;
                        }

                        if (!nextArgAvailable) {
                            hasError = true;
                            break;
                        }
                        nextArgRead = true;
                        rootPath = args[idx + 1];
                    }
                    break;
                    case "-np":
                    case "--nonparallel": {
                        if (parallel is not null) {
                            errors.Add("duplicate --(non)parallel switch");
                            break;
                        }

                        parallel = 0;
                        break;
                    }
                    case "-p":
                    case "--parallel": {
                        if (parallel is not null) {
                            errors.Add("duplicate --(non)parallel switch");
                            break;
                        }

                        if (!nextArgAvailable) {
                            parallel = ProcessorOptions.DefaultParallelValue;
                        }
                        else {
                            nextArgRead = true;
                            if (int.TryParse(args[idx + 1], out var pVal)) {
                                parallel = pVal;
                            }
                            else {
                                errors.Add("invalid --(non)parallel value: " + args[idx + 1]);
                                break;
                            }
                        }

                        break;
                    }

                    case "--depth": {
                        if (depth is not null) {
                            errors.Add("duplicate --depth switch");
                            break;
                        }

                        if (!nextArgAvailable) {
                            depth = 2;
                        }
                        else {
                            nextArgRead = true;
                            if (int.TryParse(args[idx + 1], out var pVal)) {
                                depth = pVal;
                            }
                            else {
                                errors.Add("invalid --depth value: " + args[idx + 1]);
                                break;
                            }
                        }

                        break;
                    }

                    default: {
                        if (!nextArgAvailable) {
                            if (File.Exists(curArg)) {
                                rootPath = curArg;
                            }
                            else if (Directory.Exists(curArg)) {
                                rootPath = curArg;
                            }
                        }
                        else {
                            errors.Add($"Invalid argument: '{curArg}'");
                        }
                        break;
                    }
                }

                if (nextArgRead) idx++;
            }

            if (confirmLevel.HasValue) {
                if (confirmLevel.Value == ConfirmLevel.Force && !forceFlag) {
                    errors.Add("--confirm force must specify --force");
                }

                if (forceFlag && confirmLevel.Value != ConfirmLevel.Force) {
                    errors.Add("--confirm (sln|proj|dir) plus --force specified. --force implies no confirmation.");
                }
            }

            if (rootPath is not null) {
                if (!Directory.Exists(rootPath) && !File.Exists(rootPath)) {
                    errors.Add($"File/Directory '{rootPath}' not found.");
                }
                rootPath = DirExt.EnsureRooted(rootPath, Environment.CurrentDirectory);
            }

            if (msbuildPath is not null) {
                if (!Directory.Exists(msbuildPath) && !File.Exists(msbuildPath)) {
                    errors.Add($"File/Directory '{msbuildPath}' not found.");
                }
            }

            if (forceFlag) {
                if (rootPath is null) {
                    errors.Add("if --force is used, root directory or path to .sln MUST be specified explicitly.");
                }
                confirmLevel = ConfirmLevel.Force;
            }


            if (hasError || errors.Any()) {
                options = null;
                errorsOut = errors;
                return false;
            }

            if (!delete.HasValue && !dryRunFlag.HasValue) {
                dryRunFlag = true;
                delete = false;
            }
            else if (delete.HasValue && !dryRunFlag.HasValue) {
                dryRunFlag = !delete.Value;
            }
            else if (!delete.HasValue && dryRunFlag.HasValue) {
                delete = !dryRunFlag.Value;
            }

            rootPath ??= Environment.CurrentDirectory;

            var flags = ProcessorMode.CleanAll;
            //if (forceFlag) flags|= ProcessorMode.
            if (delete!.Value) flags |= ProcessorMode.DeleteFlag;

            if (deleteEmptyDirsFlag) flags |= ProcessorMode.DeleteDirectoryForceDeleteIfEmptyFlag;
            if (deleteFilesOnlyFlag) flags |= ProcessorMode.DeleteFilesNotDirectoriesFlag;
            if (nonCurrentOnly) {
                flags |= ProcessorMode.CleanOnlyNoncurrentTfms;
                flags ^= ProcessorMode.CleanBaseIntermediate;
            }
            if (objFolder ?? false) flags |= ProcessorMode.CleanBaseIntermediate;
            if (parallel.HasValue && parallel.Value > 0) flags |= ProcessorMode.Parallel;

            flags |= ProcessorMode.ValidateBasicOutDirStructure;

            if (File.Exists(rootPath)) {
                options = new ProcessorOptions(rootPath, dryRunFlag ?? !delete.Value, msbuildPath, confirmLevel ?? ConfirmLevel.Dir, logLevel ?? LogLevel.Info, parallel, flags);
            }
            else if (Directory.Exists(rootPath)) {
                options = new ProcessorDirOptions(rootPath, "*.sln?", ProcessorDirOptions.FilterSupportedSlnFileFormats, depth ?? 2, dryRunFlag ?? !delete.Value, msbuildPath, confirmLevel ?? ConfirmLevel.Dir, logLevel ?? LogLevel.Info, parallel, flags);
            }
        }

        return options is not null;
    }
}
