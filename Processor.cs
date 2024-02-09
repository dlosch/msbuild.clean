using Microsoft.Build.Construction;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Dotnet.Clean.Tests")]

namespace Msbuild.Clean;

internal class Processor(ProcessorOptions Options, ILog? Log = default) {

    private static MsbuildLocation? Location { get; set; }
    private static MsbuildLocation? InitBl() => MSBuildHelper.GetPreferredLocations().FirstOrDefault();

    private static readonly StringComparer DefaultComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparison DefaultComparison = StringComparison.OrdinalIgnoreCase;

    internal async ValueTask Process() {
        if (Options.MSBuildPath is not null) {
            Location = MsbuildLocation.From(Options.MSBuildPath);
        }
        else {
            Location = InitBl();
        }

        if (Location is null) {
            Log?.Error($"No MSBuild instance found. We try to resolve MSBuild from Visual Studio first, .NET SDK second.");
            Log?.Error($"Please make sure this machine has a MSBuild instance or specify the path to MSBuild.exe/MSBuild.dll explicitly.");
            Log?.Error($"Cannot continue.");
            return;
        }

        if (!File.Exists(Location!.FullPath)) {
            Log?.Error($"No MSBuild instance found @{Location!.FullPath}.");
            Log?.Error($"Please make sure this machine has a MSBuild instance or specify the path to MSBuild.exe/MSBuild.dll explicitly.");
            Log?.Error($"Cannot continue.");
            return;
        }

        Log?.Info($"Using MSBuild from {Location!.FullPath}");

        var sw = Stopwatch.StartNew();

        var task = Options switch {
            ProcessorDirOptions dirOptions => ProcessDirectory(dirOptions.RootPath),
            ProcessorOptions dirOptions => ProcessSln(dirOptions.RootPath),

        };

        await task;

        await ProcessDirs();
        await DumpDirs();

        if (Options.Mode.HasFlag(ProcessorMode.DeleteFlag)) {
            Log?.Warn("Deleting ...");
            await DelDirs();
            await DelFiles();
        }
        sw.Stop();
        Log?.Info($"elapsed millis: {sw.ElapsedMilliseconds}");
    }

    private ParallelOptions _parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Options.Parallel ?? ProcessorOptions.DefaultParallelValue };
    async ValueTask Enumerate<T>(IEnumerable<T> e, Func<T, ValueTask> f) {
        if (Options.Mode.HasFlag(ProcessorMode.Parallel)) {
            await Parallel.ForEachAsync(e, _parallelOptions, async (item, cancellation) => {
                await f(item);
            });
        }
        else {
            foreach (var item in e) {
                await f(item);
            }
        }
    }

    internal async ValueTask ProcessDirectory(string path) {
        var dirOptions = Options as ProcessorDirOptions;

        if (Directory.Exists(path)) {
            var pathRooted = DirExt.EnsureRooted(path, Environment.CurrentDirectory);
            if (!Directory.Exists(pathRooted)) throw new ArgumentException($"{path} from {Environment.CurrentDirectory} -> {pathRooted}");

            var fileSearcher = Directory.EnumerateFiles(pathRooted, dirOptions!.Filter, new EnumerationOptions {
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.PlatformDefault,
                MatchType = MatchType.Simple,
                MaxRecursionDepth = dirOptions.Depth,
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false
            });

            await Enumerate(fileSearcher, ProcessSln);
        }
    }

    private HashSet<string> _deleteFiles = new HashSet<string>(DefaultComparer);
    private ConcurrentDictionary<string, List<Dir>> _deleteDirs = new ConcurrentDictionary<string, List<Dir>>(DefaultComparer);
    private EnumerationOptions _enumerateFiles = new EnumerationOptions { MatchType = MatchType.Simple, MaxRecursionDepth = 10 /*TODO FROM COMMANDLINE*/, RecurseSubdirectories = true, ReturnSpecialDirectories = false, IgnoreInaccessible = false };

    private Task DumpDirs() {

        Console.WriteLine($"\r\nResults:");

        (long, int) GetSize(DirectoryInfo dirInfo) {
            var affectedFiles = dirInfo.EnumerateFiles("*", _enumerateFiles);
            return (affectedFiles.Sum(a => a.Length), affectedFiles.Count());
        }

        var sbuilder = new StringBuilder();
        if (_deleteFiles.Any()) {
            foreach (var fileName in _deleteFiles.OrderBy(kvp => kvp).Where(kvp => File.Exists(kvp)).Distinct()) {
                CmdDryRunDumper.AppendFile(fileName, sbuilder);
            }
        }
        if (_deleteDirs.Any()) {
            var sumCount = 0;
            var sumLen = 0L;
            foreach (var kvp in _deleteDirs.OrderBy(kvp => kvp.Key).Select(kvp => kvp)) {
                if (Directory.Exists(kvp.Key)) {
                    var (len, cnt) = GetSize(new DirectoryInfo(kvp.Key));
                    if (cnt > 0 || len > 0L) {
                        sumCount += cnt;
                        sumLen += len;
                        Log?.Info($"{cnt,4} files with {len / (1024),7} KiB below {kvp.Key}");
                        CmdDryRunDumper.Append(kvp.Key, sbuilder);
                    }
                    else {
                        Log?.Verbose($"No files below {kvp.Key}.");
                    }
                }
            }

            if (sumLen > 0L || sumCount > 0) {
                Log?.Info($"\r\nTotal:\r\n\t{sumCount,7} files\r\n\t{sumLen / (1024),7} KiB\r\n\t{sumLen / (1024 * 1024),7} MiB");
            }
        }

        Log?.Info("\r\n");
        Log?.Warn(sbuilder.ToString());
        Log?.Info("\r\n");
        return Task.CompletedTask;
    }

    private bool ConfirmDir(DirectoryInfo dirInfo) {
        Console.WriteLine($"Delete directory {dirInfo.FullName}? (y|n)");
        var keyInfo = Console.ReadKey();
        if (keyInfo.KeyChar == 'y') return true;
        return false;
    }
    private bool ConfirmFiles(DirectoryInfo dirInfo) {
        Console.WriteLine($"Delete files below directory {dirInfo.FullName}? (y|n)");
        var keyInfo = Console.ReadKey();
        if (keyInfo.KeyChar == 'y') return true;
        return false;
    }
    private bool ConfirmFile(string fullName) {
        Console.WriteLine($"Delete file {fullName}? (y|n)");
        var keyInfo = Console.ReadKey();
        if (keyInfo.KeyChar == 'y') return true;
        return false;
    }
    private Task DelFiles() {
        foreach (var fileName in _deleteFiles) {
            if (File.Exists(fileName) && ConfirmFile(fileName)) {
                Log?.Debug($"Deleting {fileName}");
                try {
                    File.Delete(fileName);

                }
                catch (Exception xcptn) {
                    Log?.Warn($"Deletion of file {fileName} failed with message: {xcptn.Message}.");
                }
            }

        }

        return Task.CompletedTask;
    }

    private Task DelDirs() {
        if (_deleteDirs.Any()) {
            foreach (var kvp in _deleteDirs.OrderBy(kvp => kvp.Key).Select(kvp => kvp)) {
                if (Directory.Exists(kvp.Key)) {
                    var dirInfo = new DirectoryInfo(kvp.Key);
                    var isEmpty = dirInfo.IsEmpty();

                    if (isEmpty) {
                        if (Options.Mode.HasFlag(ProcessorMode.DeleteDirectoryForceDeleteIfEmptyFlag)) {
                            if (ConfirmDir(dirInfo)) {
                                Log?.Debug($"Deleting {dirInfo.FullName}");
                                dirInfo.SafeDelete(true, false, Log);
                            }
                        }
                        else {
                            Log?.Verbose($"{kvp.Key} is empty.");
                        }
                    }
                    else {
                        if (Options.Mode.HasFlag(ProcessorMode.DeleteFilesNotDirectoriesFlag)) {
                            if (ConfirmFiles(dirInfo)) {
                                dirInfo.SafeDelete(true, true, Log, _enumerateFiles);
                            }
                        }
                        else {
                            if (ConfirmDir(dirInfo)) {
                                Log?.Debug($"Deleting {dirInfo.FullName}");
                                dirInfo.SafeDelete(true, false, Log);
                            }
                        }
                    }
                }
            }
        }
        return Task.CompletedTask;
    }

    private Task ProcessDirs() {
        if (_dirs.Any()) {
            foreach (var dir in _dirs.Values) {

                foreach ((string path, DirType type) item in dir.AbsPath.Distinct()) {
                    Log?.Verbose($"Process {item.path} [{item.type}]");

                    var dirInfo = new DirectoryInfo(item.path);

                    bool NotSafeToDelete(Dir dir) {
                        // no proj or sln may be below target / inexact science
                        if (dir.AbsProjPath.Any(absProjPath => DirExt.IsNestedBelow(absProjPath.Key, item.path))) {
                            Log?.Warn($"");
                            return true;
                        }

                        if (dir.AbsParentPath.Any(absProjPath => DirExt.IsNestedBelow(absProjPath, item.path))) {
                            return true;
                        }

                        return false;
                    }

                    if (NotSafeToDelete(dir)) {
                        Log?.Warn($"Dir cannot be safely deleted {dir}");

                        return Task.CompletedTask;
                    }

                    Stats OutDirDelete(string absPath, DirType dirType, Dir dir) {
                        var dirInfo = new DirectoryInfo(absPath);
                        var exists = dirInfo.Exists;
#if !DIREXISTS

#else
                        if (!Directory.Exists(absPath)) return default;
                        var dirInfo = new DirectoryInfo(absPath);
                        if (!dirInfo.Exists) return default;
#endif
                        if (exists && dirInfo.IsEmpty()) {
                            if (Options.Mode.HasFlag(ProcessorMode.DeleteDirectoryForceDeleteIfEmptyFlag)) {
                                // delete dir
                            }
                        }

                        var deleteCandidates = default(IEnumerable<DirectoryInfo>);

                        if (Options.Mode.HasFlag(ProcessorMode.ValidateBasicOutDirStructure)) {
                            if (NetUtil.Instance.IsTfmName(dirInfo.Name, DefaultComparison)
                            && dir.Configs.Any(cfg => 0 == string.Compare(cfg, dirInfo.Parent?.Name, DefaultComparison))
                            ) {
                                Trace.Assert(dirInfo.Parent is not null);

                                var cfgDir = dirInfo.Parent;

                                if (!cfgDir.Exists) {
                                    Log?.Debug($"{cfgDir.FullName} does not exist.");
                                    return default;
                                }

                                IEnumerable<DirectoryInfo> GetCfgNestedAffected(DirectoryInfo cfgDir2, Dir dir2, bool onlyNonCurrent2) => cfgDir2.EnumerateDirectories()
                                        .Where(tfmDir => NetUtil.Instance.IsTfmName(tfmDir.Name, DefaultComparison)
                                        && (!onlyNonCurrent2 || !dir2.Tfms.Contains(tfmDir.Name))
                                        );


                                var onlyNonCurrent = Options.Mode.HasFlag(ProcessorMode.CleanOnlyNoncurrentTfms);
                                Log?.Verbose($"{absPath} onlyNonCurrent: {onlyNonCurrent}");
                                if (onlyNonCurrent
                                    || (cfgDir.EnumerateFiles().Any())
                                    || (cfgDir.EnumerateDirectories().Any(tfmDir => !NetUtil.Instance.IsTfmName(tfmDir.Name, DefaultComparison)))
                                    ) {

                                    Log?.Debug($"{absPath} contains files or directories which don't match tfm format. Selectively adding subdirectories ...");

                                    deleteCandidates = GetCfgNestedAffected(cfgDir, dir, onlyNonCurrent);
                                }
                                else {

                                    if (cfgDir.Parent is { } binDir) {
                                        if (0 == string.Compare(binDir.Name, "bin", DefaultComparison)
                                            || dir.AbsProjPath.Any(kvp => kvp.Value is { } projectName && (0 == string.Compare(binDir.Name, projectName, DefaultComparison)))) {

                                            Log?.Verbose($"{absPath} #2");

                                            if (binDir.EnumerateFiles().Any()
                                            || binDir.EnumerateDirectories().Any(cfgDir => !dir.Configs.Contains(cfgDir.Name))
                                            ) {
                                                Log?.Debug($"{absPath} contains files or directories which don't match configurations format. Selectively adding subdirectories ...");

                                                deleteCandidates = GetCfgNestedAffected(cfgDir, dir, onlyNonCurrent);
                                            }
                                            else {
                                                deleteCandidates = GetCfgNestedAffected(cfgDir, dir, onlyNonCurrent);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else {
                            if (exists) deleteCandidates = new DirectoryInfo[] { dirInfo };
                        }


                        if (deleteCandidates is { }) {
                            foreach (var d in deleteCandidates) {
                                _deleteDirs.AddOrUpdate(d.FullName, new List<Dir>() { dir }, (unused, dirList) => { dirList.Add(dir); return dirList; });
                                Log?.Verbose($"Directory marked for deletion: {d.FullName}");
                            }
                        }

                        // todo 
                        dir.SetProcessed();
                        return default;
                    }


                    Stats BaseOutDirDelete(string absPath, DirType dirType, Dir dir) {
                        Log?.Debug("Not Implemented :(");
                        return default;
                    }

                    Stats BaseIntermediateOutputDirDelete(string absPath, DirType dirType, Dir dir) {
                        if (Directory.Exists(absPath)) {
                            _deleteDirs.AddOrUpdate(absPath, new List<Dir>() { dir }, (unused, dirList) => { dirList.Add(dir); return dirList; });
                        }
                        return default;
                    }

                    Stats VcxDir(string absPath, DirType dirType, Dir dir) {
                        if (Directory.Exists(absPath)) {
                            _deleteDirs.AddOrUpdate(absPath, new List<Dir>() { dir }, (unused, dirList) => { dirList.Add(dir); return dirList; });
                        }
                        return default;
                    }

                    var deleteTask = (dir.ProjType, item.type) switch {
                        (ProjectType.Vcxproj, _) => VcxDir(item.path, item.type, dir),
                        (_, DirType.OutDir) => OutDirDelete(item.path, item.type, dir),
                        (_, DirType.BaseOutputPath) => BaseOutDirDelete(item.path, item.type, dir),
                        (_, DirType.BaseIntermediateOutputPath) => BaseIntermediateOutputDirDelete(item.path, item.type, dir),
                        _ => default,
                    };
                }
            }

        }
        return Task.CompletedTask;
    }

    async ValueTask ProcessSln(string slnPath) {
        Log?.Info(slnPath);
        try {
            var sln = SolutionFile.Parse(slnPath);

            var projEnumerator = sln.ProjectsInOrder.Where(p => File.Exists(p.AbsolutePath) && p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat);

            ValueTask Proc(ProjectInSolution proj) => ProcessProject(proj, Path.GetDirectoryName(slnPath));

            await Enumerate(projEnumerator, Proc);
        }
        catch (Exception xcptn) {
            Log?.Error(xcptn, $"Error processing sln {slnPath}:");
        }
    }

    private HashSet<string> _projHs = new HashSet<string>(DefaultComparer);

    async ValueTask ProcessProject(ProjectInSolution proj, string parentPath) {
        if (proj?.AbsolutePath is null || !File.Exists(proj.AbsolutePath)) return;
        Log?.Debug($"[{Thread.CurrentThread.ManagedThreadId}]: ProcessProject");
        var absProjPath = DirExt.EnsureRooted(proj.AbsolutePath, parentPath ?? Environment.CurrentDirectory);
        var queryPlatform = false;
        var queryConfigurationName = false;
        switch (Path.GetExtension(proj.AbsolutePath)!.ToLowerInvariant()) {
            case ".csproj":
            case ".fsproj":
            case ".sqlproj":
            case ".vbproj": // old proj do not have the <tfm>
                queryConfigurationName = true;
                queryPlatform = false;
                break;
            case ".vcxproj":
                queryConfigurationName = true;
                queryPlatform = true;
                break;
            default:
                Log?.Warn($"Unsupported project type: {proj.AbsolutePath}. Seems to be msbuild project type, but not implemented in this app.");
                return;
        }

        var count = 1;
        var timer = Stopwatch.StartNew();
        if (proj.ProjectConfigurations is null) {

            // todo fixme
            var key = $"_#_#" + absProjPath;
            lock (_projHs) {
                if (_projHs.Contains(key)) return;
                _projHs.Add(key);
            }

            var props = await TryReadProjectPropertiesMsbuildInvoke(absProjPath, properties, null, null);
            if (props is null) {
                Log?.Error($"MSBuild invocation to resolve properties from project did not return a result.\r\n\tProject {absProjPath}\r\n\tUsing MSBuild from {Location!.FullPath}.");
                Log?.Error($"\tWe try to resolve MSBuild from Visual Studio first. The MSBuild instance has to have any .targets file referenced.");
                Log?.Error($"\tTry running this tool with explicit path to MSBuild.exe or MSBuild.dll.");
                Log?.Warn($"Cannot clean project {absProjPath}.");

                return;
            }

            await ProcessProjProperties(props, proj, null, parentPath);
        }
        else {
            var cfgs = proj.ProjectConfigurations
                            .Select(x => (x.Value?.ConfigurationName, queryPlatform ? x.Value?.PlatformName : null))
                            .Where(x => x.ConfigurationName is not null)
                            .Distinct();
            count = cfgs.Count();

            async ValueTask P((string ConfigurationName, string? PlatformName) cfg) {
                // todo fixme
                var key = $"{cfg.ConfigurationName}#{cfg.PlatformName}#" + absProjPath;
                lock (_projHs) {
                    if (_projHs.Contains(key)) return;
                    _projHs.Add(key);
                }

                var props = await TryReadProjectPropertiesMsbuildInvoke(absProjPath, properties, cfg.ConfigurationName, queryPlatform ? cfg.PlatformName : null);
                if (props is null) {
                    Log?.Error($"MSBuild invocation to resolve properties from project did not return a result.\r\n\tProject {absProjPath}\r\n\tUsing MSBuild from {Location!.FullPath}.");
                    Log?.Error($"\tWe try to resolve MSBuild from Visual Studio first. The MSBuild instance has to have any .targets file referenced.");
                    Log?.Error($"\tTry running this tool with explicit path to MSBuild.exe or MSBuild.dll.");
                    Log?.Warn($"Cannot clean project {absProjPath}.");

                    return;
                }

                await ProcessProjProperties(props, proj, cfg.ConfigurationName, parentPath);
            }

            await Enumerate(cfgs, P);
        }

        timer.Stop();
        Log?.Info($"Processing {proj.AbsolutePath} with {count} configurations took {timer.ElapsedMilliseconds} ms.");
    }

    class Comp : IEqualityComparer<(string, string?)> {
        public bool Equals((string, string?) x, (string, string?) y) {
            if (x.Item1 is null && y.Item1 is null) return false;
            if (x.Item2 is null != y.Item2 is null) return false;
            return GetHashCode(x) == GetHashCode(y);
        }

        public int GetHashCode([DisallowNull] (string, string?) obj) {
            return (obj.Item1 + (obj.Item2 ?? "")).GetHashCode();
        }
    }

    async ValueTask ProcessProjProperties(Output props, ProjectInSolution proj, string? cfg, string? parentPath) {
        var projPath = DirExt.EnsureRooted(proj.AbsolutePath, parentPath ?? Environment.CurrentDirectory);
        var projDir = Path.GetDirectoryName(projPath)!;

        if (Options.Mode.HasFlag(ProcessorMode.CleanBuildOutput)) {
            if (props.OutDir is string outDir && !string.IsNullOrEmpty(outDir)) {
                outDir = DirExt.EnsureRooted(outDir, projDir);
                // todo HOGH even if the dir does not exist, older tfms may
#if !DIREXISTS
                await AddDir(outDir, DirType.OutDir, projPath, props.ProjectName, cfg, props.TfmsCombined, parentPath);
#else
                if (Directory.Exists(outDir)) await AddDir(outDir, DirType.OutDir, projPath, props.ProjectName, cfg, props.TfmsCombined, parentPath);
                else Log?.Verbose($"{outDir} does not exist.");
#endif
            }
            else {
                if (props.BaseOutputPath is string baseOut && !string.IsNullOrEmpty(baseOut)) {
                    baseOut = DirExt.EnsureRooted(baseOut, projDir);

                    if (Directory.Exists(baseOut)) await AddDir(baseOut, DirType.BaseOutputPath, projPath, props.ProjectName, cfg, props.TfmsCombined, parentPath);
                    else Log?.Verbose($"{baseOut} does not exist.");
                }
            }
        }

        if (Options.Mode.HasFlag(ProcessorMode.CleanNupkg)) {
            if (props.PackageOutputPath is string pkgOutDir && !string.IsNullOrEmpty(pkgOutDir)) {
                pkgOutDir = DirExt.EnsureRooted(pkgOutDir, projDir);

                var packageId = props.PackageId ?? props.ProjectName ?? props.AssemblyName;

                if (Directory.Exists(pkgOutDir)) {
                    var pkgDir = new DirectoryInfo(pkgOutDir);
                    var nupkgs = pkgDir.EnumerateFiles($"{props.PackageId}.*.nupkg");
                    if (nupkgs.Any()) {
                        foreach (var file in nupkgs) {
                            Log?.Debug($"Delete {file.FullName}");
                            _deleteFiles.Add(file.FullName);
                        }
                    }
                }

            }
        }

        if (Options.Mode.HasFlag(ProcessorMode.CleanBaseIntermediate)) {
            if (props.BaseIntermediateOutputPath is string interDir && !string.IsNullOrEmpty(interDir)) {
                interDir = DirExt.EnsureRooted(interDir, projDir);

                if (Directory.Exists(interDir)) await AddDir(interDir, DirType.BaseIntermediateOutputPath, projPath, props.ProjectName, cfg, props.TfmsCombined, parentPath);
                else Log?.Verbose($"{interDir} does not exist.");
            }
        }

        // docker
        if (Options.Mode.HasFlag(ProcessorMode.CleanDocker)) {
            if (props.HasDockerProperties() && props.IsDotnet8OrHigher()) {
                Log?.Verbose($"Project has Docker Properties: {projPath}, {props.TargetFramework}");
            }
            else {
                var dirName = Path.GetDirectoryName(projPath);
                if (Directory.Exists(dirName)) {
                    var dockerfileName = Path.Combine(dirName, "Dockerfile");
                    if (File.Exists(dockerfileName)) {
                        Log?.Verbose($"Project has Dockerfile: {projPath}, {dockerfileName}");
                    }
                }
            }
        }
    }

    private Task AddDir(string interDir, DirType baseIntermediateOutputPath, string projPath, object projectName, string? cfg, HashSet<string> tfmsCombined, string? parentPath) {
        throw new NotImplementedException();
    }

    internal enum ProjectType {
        Unknown,

        Csproj,
        CsprojWeb,
        CsprojLegacy,

        Fsproj,
        Vbproj,
        Sqlproj,
        Vcxproj,

    }
    internal record class Dir(List<(string, DirType)> AbsPath
        , Dictionary<string, string?> AbsProjPath, HashSet<string> Configs, HashSet<string> Tfms, HashSet<string> AbsParentPath) {
        internal bool IsProcessed = false;
        internal void SetProcessed() => IsProcessed = true;
        internal ProjectType ProjType => GetProjectType(AbsProjPath.FirstOrDefault().Key);

        private static ProjectType GetProjectType(string? projectFileAbsPath) {
            if (projectFileAbsPath == null) return ProjectType.Unknown;
            switch (Path.GetExtension(projectFileAbsPath).ToLowerInvariant()) {
                case ".csproj": return ProjectType.Csproj;
                case ".fsproj": return ProjectType.Fsproj;
                case ".vbproj": return ProjectType.CsprojLegacy;
                case ".sqlproj": return ProjectType.Sqlproj;
                case ".vcxproj": return ProjectType.Vcxproj;
                default: return ProjectType.Unknown;
            }

        }
    }

    // todo win vs unix
    private ConcurrentDictionary<string, Dir> _dirs = new ConcurrentDictionary<string, Dir>(DefaultComparer);

    private ValueTask AddDir(string absPath, DirType dirType, string absProjPath, string? projName, string? cfg, HashSet<string> tfms, string? parentPath) {
        Log?.Verbose(absPath);

        //HashSet<string> GetHashSet(IEnumerable<string>? items) {
        //    if (items is not null && items.Any()) return items.ToHashSet(DefaultComparer); // todo win
        //    return new HashSet<string>(DefaultComparer);
        //}
        HashSet<string> GetHashSetS(string? item) {
            var hs = new HashSet<string>(DefaultComparer);
            if (item is not null) hs.Add(item);
            return hs;
        }
        Dictionary<string, string?> GetDict(string? item, string? val) {
            var hs = new Dictionary<string, string?>(DefaultComparer);
            if (item is not null) hs.Add(item, val);
            return hs;
        }

        {
            _dirs.AddOrUpdate(absProjPath,
                (key) => new Dir(new List<(string, DirType)>() { (absPath, dirType) }, GetDict(absProjPath, projName), GetHashSetS(cfg), tfms, GetHashSetS(parentPath)),
                // todo honestly unsure about the concurrency guarantee here
                (key, existDir) => {
                    existDir.AbsPath.Add((absPath, dirType));
                    existDir.AbsProjPath.TryAdd(absProjPath, projName);
                    if (tfms is not null && tfms.Any()) {
                        existDir.Tfms.UnionWith(tfms);
                    }
                    if (parentPath is not null) {
                        existDir.AbsParentPath.Add(parentPath);
                    }
                    if (cfg is not null) {
                        existDir.Configs.Add(cfg);
                    }
                    return existDir;
                });
        }
        return ValueTask.CompletedTask;
    }

    ValueTask ProcessDir(string absPath, DirType dirType) => ValueTask.CompletedTask;

    internal enum DirType {
        OutDir,
        BaseOutputPath,
        BaseIntermediateOutputPath,
    }

    static string[] properties = [
        @"OutDir",
        @"BaseIntermediateOutputPath",
        @"BaseOutputPath",
        "ProjectName",

        @"TargetFramework",
        @"TargetFrameworks",
        //@"PublishTrimmed",
        //@"PublishAot",

        "UsingMicrosoftNETSdk", // true or empty

        // todo
        "IsPackable",

        "PackageOutputPath",
        "PackageId",
        "AssemblyName",

        // ContainerBaseImage
        // ContainerFamily
        // ContainerRuntimeIdentifier
        // ContainerRegistry
        // ContainerRepository
        // ContainerImageTag
        // ContainerImageTags

        //"ContainerBaseImage",
        //"ContainerFamily",
        //"ContainerRuntimeIdentifier",
        //"ContainerRegistry",
        //"ContainerRepository",
        //"ContainerImageTag",
        //"ContainerImageTags"
    ];

    async ValueTask<Output?> TryReadProjectPropertiesMsbuildInvoke(string absPath, string[] propertyNames, string? configuration, string? platform) {
        var res = await MSBuildHelper.QueryProjectProperties(Location!, absPath, configuration, platform, propertyNames);
        if (res is null || res.Properties is null || !res.Properties.Any()) {
            // when executing NetCore msbuild against unsupported, returns empty properties I beliebe
            Log?.Warn($"MSBuild invocation did not return any property values for this project");
            return null;
        }
        return res;
    }
}
