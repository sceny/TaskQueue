#tool "nuget:?package=GitVersion.CommandLine&version=4.0.0"
#tool "nuget:?package=GitReleaseNotes&version=0.7.1"

var target = Argument("target", "Default");

// Configuration
var solutionFile = File("./TaskQueue.sln");
var projectFile = File("./src/TaskQueue/Sceny.TaskQueue.csproj");
var artifacts = Directory("./artifacts");
var configuration = Argument("Configuration", "Release");

// Globals
GitVersion versionInfo;
DotNetCoreMSBuildSettings msbuildSettings;

// Tasks
Task("Clean")
    .Does(() => {
        if (DirectoryExists(artifacts))
            DeleteDirectory(artifacts, new DeleteDirectorySettings { Force = true, Recursive = true});
        CreateDirectory(artifacts);
    });

Task("Version")
    .Does(() => {
        var settings = new GitVersionSettings
        {
            OutputType = GitVersionOutput.Json,
            UpdateAssemblyInfo = true
        };
        var versionInfo = GitVersion(settings);
        msbuildSettings = new DotNetCoreMSBuildSettings()
            .WithProperty("Version", versionInfo.NuGetVersionV2)
            .WithProperty("AssemblyVersion", versionInfo.AssemblySemVer)
            .WithProperty("FileVersion", versionInfo.AssemblySemVer);

        Information("Version details");
        Information($"   - NuGetVersionV2: {versionInfo.NuGetVersionV2}");
        Information($"   - AssemblySemVer: {versionInfo.AssemblySemVer}");
        Information($"   - AssemblySemFileVer: {versionInfo.AssemblySemFileVer}");
    });

Task("Restore")
    .Does(() => {
        DotNetCoreRestore(solutionFile);
    });

Task("Build")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore")
    .IsDependentOn("Version")
    .Does(() => {
        var settings = new DotNetCoreBuildSettings
        {
            Configuration = configuration,
            MSBuildSettings = msbuildSettings,
            NoRestore = true
        };
        DotNetCoreBuild(solutionFile);
    });

Task("Tests")
    .IsDependentOn("Build")
    .Does(() => {
        var settings = new DotNetCoreTestSettings
        {
            Configuration = configuration,
            NoBuild = true,
            NoRestore = true
        };

        var projectFiles = GetFiles("./tests/**/*.csproj");
        foreach(var file in projectFiles)
        {
            DotNetCoreTest(file.FullPath, settings);
        }
    });

Task("ReleaseNotes")
    .Does(() => {
        var releaseNotesFile = artifacts + File("ReleaseNotes.md");

        var gitReleaseNotesTool = Context.Tools.Resolve("GitReleaseNotes.exe");
        var releaseNotesExitCode = StartProcess(
            gitReleaseNotesTool,
            new ProcessSettings { Arguments = $". /OutputFile {releaseNotesFile}", RedirectStandardOutput = true },
            out var redirectedOutput
        );

        Information(string.Join("\n", redirectedOutput));
        
        if (string.IsNullOrEmpty(System.IO.File.ReadAllText("./artifacts/releasenotes.md")))
            System.IO.File.WriteAllText("./artifacts/releasenotes.md", "No issues closed since last release");

        var settings = new GitReleaseNotesSettings
        {
            Verbose                  = true,
            IssueTracker             = GitReleaseNotesIssueTracker.GitHub,
            AllTags                  = true,
            Version                  = versionInfo.NuGetVersionV2,
            AllLabels                = true
        };
        GitReleaseNotes(releaseNotesFile, settings);
    });

Task("Pack")
    .IsDependentOn("Version")
    .IsDependentOn("Tests")
    .Does(() => {
        var settings = new DotNetCorePackSettings
        {
            OutputDirectory = artifacts,
            NoBuild = true,
            MSBuildSettings = msbuildSettings
        };

        DotNetCorePack(projectFile, settings);
    });

// Targets
Task("Default")
    .IsDependentOn("Pack");

RunTarget(target);