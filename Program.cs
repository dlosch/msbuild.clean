using Msbuild.Clean;

if (ProcessorOptionsParser.TryParseArgs(args, out var options, out var errors)) {
    Console.WriteLine(options);
    var processor = new Processor(options, new SimpleConsoleLogger(options.Log));
    await processor.Process();
}
else {
    foreach (var item in errors) {
        Console.Error.WriteLine(item);
    }
}

record struct Stats(string Sln, string[]? Configurations = default, int TotalDirectories = 0, long TotalSize = 0L, int TotalFsiEntryCount = 0) {
    internal long TotalSizeMiB => TotalSize / (1024 * 1024);

    internal void Add(Stats stats) {
        TotalDirectories += stats.TotalDirectories;
        TotalSize += stats.TotalSize;
        TotalFsiEntryCount += stats.TotalFsiEntryCount;
    }
}
