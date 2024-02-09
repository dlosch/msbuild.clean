using System.Diagnostics;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.Json;
using static Msbuild.Clean.Processor;

namespace Msbuild.Clean;

internal enum MsbuildExecType {
    ExecTypeExe = 0x1,
    ExecTypeDllDotnet = 0x2,
}

internal enum MsbuildLocationType {
    VisualStudio,
    DotNetSdk,
    NetFx,

    Manual,
}

internal enum MsbuildCleanExecutionType {
    PreferVs,
    PeekProjFile,
}

#if DEBUG
[JsonSerializable(typeof(Dir))]
[JsonSerializable(typeof(ProcessorDirOptions))]
[JsonSerializable(typeof(ProcessorOptions))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSerializable(typeof(HashSet<string>))]
#endif
[JsonSerializable(typeof(Output))]
[JsonSerializable(typeof(ImmutableDictionary<string, string>))]
internal partial class OutputJsonSerializerContext : JsonSerializerContext;

internal static class JsonExt {
    internal static JsonSerializerOptions _sDefaults { get; } = Init();
    private static JsonSerializerOptions Init() {
        var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, };
        options.TypeInfoResolverChain.Insert(0, new OutputJsonSerializerContext());
        return options;
    }

    internal static string Ser(this object obj) => JsonSerializer.Serialize(obj, _sDefaults);
}

internal record class Output(ImmutableDictionary<string, string> Properties) {
    internal static Output Empty { get; } = new Output(ImmutableDictionary<string, string>.Empty);

    internal string? OutDir => Properties.TryGetValue("OutDir", out var val) ? val : default;
    internal string? BaseIntermediateOutputPath => Properties.TryGetValue("BaseIntermediateOutputPath", out var val) ? val : default;
    internal string? BaseOutputPath => Properties.TryGetValue("BaseOutputPath", out var val) ? val : default;

    internal string? PackageOutputPath => Properties.TryGetValue("PackageOutputPath", out var val) ? val : default;
    internal string? AssemblyName => Properties.TryGetValue("AssemblyName", out var val) ? val : default;
    internal string? PackageId => Properties.TryGetValue("PackageId", out var val) ? val : default;

    internal string? ProjectName => Properties.TryGetValue("ProjectName", out var val) ? val : default;
    internal string? TargetFramework => Properties.TryGetValue("TargetFramework", out var val) ? val : default;
    internal IEnumerable<string> TargetFrameworks => Properties.TryGetValue("TargetFrameworks", out var val) ? Split(val) : Enumerable.Empty<string>();
    internal HashSet<string> TfmsCombined {
        get {
            var hs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (TargetFramework is string tfm && tfm is not null) {
                hs.Add(tfm);
            }
            if (TargetFrameworks is { } tfms) {
                hs.UnionWith(tfms);
            }
            return hs;
        }
    }

    private IEnumerable<string> Split(string? val) {
        if (string.IsNullOrWhiteSpace(val)) return Enumerable.Empty<string>();

        return val
            .Split(";", StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim());
    }

    internal string? this[string key] => Properties.TryGetValue(key, out var val) ? val : default;

    internal bool IsDotnet8OrHigher() => TargetFramework?.StartsWith("Net8", StringComparison.OrdinalIgnoreCase) ?? false || TfmsCombined.Any(tfm => NetUtil.IsNet8OrHigher(tfm));
    private readonly string[] _dockerPropertyNames = ["ContainerBaseImage",
        "ContainerFamily",
        "ContainerRuntimeIdentifier",
        "ContainerRegistry",
        "ContainerRepository",
        "ContainerImageTag",
        "ContainerImageTags"];

    internal bool HasDockerProperties() => _dockerPropertyNames.Any(propName => Properties.TryGetValue(propName, out var val) && !string.IsNullOrWhiteSpace(val));
}

internal class NetUtil {
    internal static NetUtil Instance { get; } = new();

    internal static bool IsNet8OrHigher(string? tfm) {
        if (tfm == null) return false;
        if (tfm.StartsWith("net8", StringComparison.OrdinalIgnoreCase)) return true;
        if (tfm.StartsWith("net9", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal bool IsTfmName(string name, StringComparison defaultComparison) {
        if (defaultComparison == StringComparison.OrdinalIgnoreCase) return _validTfms.Contains(name);
        return ValidTfms.Any(x => 0 == string.Compare(name, x, defaultComparison));
    }

    private NetUtil() {
        _validTfms = new HashSet<string>(ValidTfms, StringComparer.OrdinalIgnoreCase);
    }

    private HashSet<string> _validTfms { get; init; }

    private readonly string[] ValidTfms = new string[] {
"netcoreapp1.0"
, "netcoreapp1.1"
, "netcoreapp2.0"
, "netcoreapp2.1"
, "netcoreapp2.2"
, "netcoreapp3.0"
, "netcoreapp3.1"
, "net5.0"
, "net6.0"
, "net7.0"
, "net8.0"
, "net9.0"

, "netstandard1.0"
, "netstandard1.1"
, "netstandard1.2"
, "netstandard1.3"
, "netstandard1.4"
, "netstandard1.5"
, "netstandard1.6"
, "netstandard2.0"
, "netstandard2.1"

, "net11"
, "net20"
, "net35"
, "net40"
, "net403"
, "net45"
, "net451"
, "net452"
, "net46"
, "net461"
, "net462"
, "net47"
, "net471"
, "net472"
, "net48" };
}