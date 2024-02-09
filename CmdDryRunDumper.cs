using System.Text;

namespace Msbuild.Clean;

internal static class CmdDryRunDumper {
    internal static void Append(string dir, StringBuilder builder) {
        builder.AppendLine($"rmdir /q /s \"{dir}\"");
    }

    internal static void AppendFile(string fileName, StringBuilder builder) {
        builder.AppendLine($"del \"{fileName}\"");
    }
}
