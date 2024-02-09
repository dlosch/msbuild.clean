using System.Runtime.CompilerServices;

namespace Msbuild.Clean;

internal interface ILog {
    void Debug(string message, [CallerMemberName] string? src = default) => Log(LogLevel.Verbose, message, src);
    void Verbose(string message, [CallerMemberName] string? src = default) => Log(LogLevel.Verbose, message, src);
    void Info(string message, [CallerMemberName] string? src = default) => Log(LogLevel.Info, message, src);
    void Log(LogLevel level, string message, [CallerMemberName] string? src = default);
    void Warn(string message, [CallerMemberName] string? src = default) => Log(LogLevel.Warning, message, src);
    void Error(string message, [CallerMemberName] string? src = default) => Log(LogLevel.Error, message, src);
    void Error(Exception exception, string message, [CallerMemberName] string? src = default) => Log(LogLevel.Error, message + $"\r\n{exception.Message}", src);
}


internal class SimpleConsoleLogger(LogLevel Level = LogLevel.Info) : ILog {
    public void Log(LogLevel level, string message, [CallerMemberName] string? caller = "") {
        if (level >= Level) Console.WriteLine(message);
    }
}
