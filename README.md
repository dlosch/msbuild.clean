# msbuild.clean

This is a tool to clean build output folders for (especially .net) MSBuild projects. It's also lame and quite s-s-s-slow.

Yes, you can use **dotnet clean** or **msbuild /t:clean** to clean build output from your solutions ... however, these tools ... well these
- don't clean old build targets (after migrating from net6.0 to net8.0, net6.0 output doesn't get cleaned)
- don't delete default publishing folders (which can be huge)
- dotnet clean can have limitations cleaning older framework-style projects

Yes, you can just use git/source control to nuke anything not under source control
- not all projects are under git/source control
- if the build output isn't below the repo, this doesn't work (dotnet\runtime)

Imagine you had a sln/csproj targeting net6.0, you have existing build output below /bin/Debug/net6.0, you switch to net8.0 ... happy days cleaning up the old build output.

What this tool does
- traverse directories looking for .sln
- process all configurations from the .sln
- invokes msbuild to evaluate the build output related properties from the project file per configuration (which is why this is so s-s-s-slow)
- enables you to delete only non-current build output (TagetFramework no longer in proj file)
- validates tfms for .net projects to make sure the correct stuff gets nuked
- by default doesn't delete, only dump stats and the command line to delete folders. Nothing gets touched unless you specify --delete

Upside:
- by default doesn't delete anything, just dumps statistics and rmdir /q /s ...
- confirmation configurable

## Sample
`msbuild.clean --root <rootDir> --depth 3 --parallel 24`

## Options
|switch||
|---|---|
|-v, --verbosity, --log|LogLevel (Debug,Verbose,Info,[Warning],Error)|
|--confirm|ConfirmLevel|
|--dry-run|[DEFAULT] do not delete, just dump stats and a list of folders to delete|
|--delete|delete folders/files after confirmation|
|--force|every tool needs an option --force|
|--delete-empty-directories|delete directory even if no files/subdirectories.|
|--delete-files|delete files instead of directories|
|--non-current|--noncurrent|delete only build output which is not targeting the current TargetFramework/TargetFrameworks from the csproj. Uses a list of tfms and the basic out dir structure \bin\<config>\<tfm>.|
|--obj|[DEFAULT] also clean BaseIntermediateOutputPath|
|--msbuild|explicit path to MSbuild.exe or MSbuild.dll. By default, the tool tries to guess the correct MSBuild location, preferring Visual Studio installations|
|--root|root path or path to .sln|
|-np|--non-parallal|process every sln and projs sequentially|
|-p|--parallel|processes sln, projs, and configurations slightly parallel (if it's slow, that's fine. If it's extremely slow, get a new machine)|
|--depth|if root dir is directory, the depth to which the tool scans for .sln|
|<path>|path to sln or path to root dir can also be specified as last argument. If missing, current directory used|

## msbuild WERfaults
msbuild getproperty pukes when it encounters an error during evaluation.
