namespace Msbuild.Clean;

internal record class MsbuildLocation(string FullPath, MsbuildExecType ExecType, MsbuildLocationType MsbuildLocationType) {
    public string ExecName => ExecType switch {
        MsbuildExecType.ExecTypeDllDotnet => $"dotnet.exe",
        MsbuildExecType.ExecTypeExe => FullPath,
        _ => throw new InvalidOperationException()
    };

    internal static MsbuildLocation? From(string fullPath) {
        static MsbuildLocation ResolveFromFile(string path) {
            if (0 == string.Compare(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) {
                return new MsbuildLocation(path, MsbuildExecType.ExecTypeExe, MsbuildLocationType.Manual);
            }
            return new MsbuildLocation(path, MsbuildExecType.ExecTypeDllDotnet, MsbuildLocationType.Manual);
        }
        if (File.Exists(fullPath)) {
            return ResolveFromFile(fullPath);
        }
        else {
            if (Directory.Exists(fullPath)) {
                var path = Directory.GetFiles(fullPath).FirstOrDefault(file => {
                    var fileName = Path.GetFileName(file);
                    return fileName.ToLowerInvariant() == "msbuild.exe" || fileName.ToLowerInvariant() == "msbuild.dll";
                });

                if (path is not null) {
                    return ResolveFromFile(path);
                }
            }
        }
        return default;
    }
}
