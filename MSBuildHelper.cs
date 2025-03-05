using System.Diagnostics;
using System.Text.Json;

namespace Msbuild.Clean;

internal static partial class MSBuildHelper {

    // if there is a global.json which references a sdk which is not installed, the msbuild invocation will fail, because msbuild always uses the global.json
    // One could use "dotnet msbuild" instead, but that would require a different approach to the msbuild invocation
    // dotnet msbuild: launched from the csproj dir: uses the global.json.
    // dotnet msbuild: launched from a project above or unrelated to global.json: does not use the global.json.    
    public static async ValueTask<Output?> QueryProjectProperties(MsbuildLocation locations, string projectFilePath, string? configuration, string? platform, params string[] properties) {
        ValueTask<Output?> Deser(Stream json) => JsonSerializer.DeserializeAsync<Output>(json, JsonExt._sDefaults);

        string args = (configuration, platform) switch {
            ( { }, { }) => $"-getproperty:{string.Join(",", properties)} -p:Configuration={configuration} -p:Platform={platform} \"{projectFilePath}\"",
            ( { }, null) => $"-getproperty:{string.Join(",", properties)} -p:Configuration={configuration} \"{projectFilePath}\"",
            (null, null) => $"-getproperty:{string.Join(",", properties)} \"{projectFilePath}\"",
            _ => $"-getproperty:{string.Join(",", properties)} \"{projectFilePath}\""
        };

        if (locations.ExecType != MsbuildExecType.ExecTypeExe) {
            args = $"\"{locations.FullPath}\" " + args;
        }

        var startInfo = new ProcessStartInfo {
            Arguments = args,
            CreateNoWindow = true,
            FileName = locations.ExecName,
            UseShellExecute = false,
            RedirectStandardOutput = true
        };

        var process = Process.Start(startInfo);

        if (process is null) return Output.Empty;
        await process.WaitForExitAsync();

        if (process.ExitCode != 0) return Output.Empty;

        return await Deser(process.StandardOutput.BaseStream);
    }
}
