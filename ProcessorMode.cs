namespace Msbuild.Clean;

[Flags]
internal enum ProcessorMode {
    None = 0x0,

    DeleteFlag = 0x1,
    DryRunMask = ~0x1,

    //ConfirmNoConfirmFlag = 
    ConfirmPerSlnFlag = 0x10,
    ConfirmPerProjFlag = 0x20,
    ConfirmPerDirFlag = 0x40,

    ValidateBasicOutDirStructure = 0x80,

    DeleteDirectoryFlag = 0x100, // delete the directory, aka bin\Debug\Net8.0
    DeleteDirectoryForceDeleteIfEmptyFlag = 0x200, // delete the files under but not the dir aka bin\Debug\Net8.0\
    DeleteFilesNotDirectoriesFlag = 0x400, // delete the files under but not the dir aka bin\Debug\Net8.0\

    CleanOnlyNoncurrentTfms = 0x1000, // you changed the tfm from Net6.0 to Net8.0 but bin\Debug\Net6.0 is still there. Nuke bin\Debug\Net6.0 but do not touch bin\Debug\Net8.0

    CleanBuildOutput = 0x10_000,
    CleanBaseIntermediate = 0x20_000,
    CleanTestResults = 0x40_000,
    CleanDocker = 0x80_000,
    CleanNupkg = 0x100_000,
    CleanAll = CleanBuildOutput | CleanBaseIntermediate | CleanNupkg,

    Parallel = 0x200_000,

    Default = ConfirmPerSlnFlag | DeleteDirectoryFlag | ValidateBasicOutDirStructure | CleanAll
        | ProcessorMode.Parallel,
    DefaultDelete = ConfirmPerSlnFlag | DeleteDirectoryFlag | ValidateBasicOutDirStructure | CleanAll | DeleteFlag
        | ProcessorMode.Parallel,
    OnlyNoncurrent = ConfirmPerSlnFlag | DeleteDirectoryFlag | ValidateBasicOutDirStructure | (CleanBuildOutput | CleanTestResults | CleanDocker) | CleanOnlyNoncurrentTfms
        | ProcessorMode.Parallel,
}
