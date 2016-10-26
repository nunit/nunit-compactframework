//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Debug");

//////////////////////////////////////////////////////////////////////
// SET ERROR LEVELS
//////////////////////////////////////////////////////////////////////

var ErrorDetail = new List<string>();

//////////////////////////////////////////////////////////////////////
// SET PACKAGE VERSION
//////////////////////////////////////////////////////////////////////

var version = "3.6.0";
var modifier = "";

var isCompactFrameworkInstalled = FileExists(Environment.GetEnvironmentVariable("windir") + "\\Microsoft.NET\\Framework\\v3.5\\Microsoft.CompactFramework.CSharp.targets");

var isAppveyor = BuildSystem.IsRunningOnAppVeyor;
var dbgSuffix = configuration == "Debug" ? "-dbg" : "";
var packageVersion = version + modifier + dbgSuffix;

//////////////////////////////////////////////////////////////////////
// DEFINE RUN CONSTANTS
//////////////////////////////////////////////////////////////////////

var PROJECT_DIR = Context.Environment.WorkingDirectory.FullPath + "/";
var PACKAGE_DIR = PROJECT_DIR + "package/";
var BIN_DIR = PROJECT_DIR + "bin/" + configuration + "/";
var IMAGE_DIR = PROJECT_DIR + "images/";

// Test Assemblies
var EXECUTABLE_FRAMEWORK_TESTS = "nunit.framework.tests.exe";
var EXECUTABLE_NUNITLITE_TESTS = "nunitlite.tests.exe";

// Packages
var SRC_PACKAGE = PACKAGE_DIR + "NUnitCF-" + version + modifier + "-src.zip";
var ZIP_PACKAGE = PACKAGE_DIR + "NUnitCF-" + packageVersion + ".zip";

//////////////////////////////////////////////////////////////////////
// CLEAN
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Description("Deletes all files in the BIN directory")
    .Does(() =>
    {
        CleanDirectory(BIN_DIR);
    });


//////////////////////////////////////////////////////////////////////
// INITIALIZE FOR BUILD
//////////////////////////////////////////////////////////////////////

Task("InitializeBuild")
    .Description("Initializes the build")
    .Does(() =>
    {
        if (isAppveyor)
        {
            var tag = AppVeyor.Environment.Repository.Tag;

            if (tag.IsTag)
            {
                packageVersion = tag.Name;
            }
            else
            {
                var buildNumber = AppVeyor.Environment.Build.Number.ToString("00000");
                var branch = AppVeyor.Environment.Repository.Branch;
                var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;

                if (branch == "master" && !isPullRequest)
                {
                    packageVersion = version + "-dev-" + buildNumber + dbgSuffix;
                }
                else
                {
                    var suffix = "-ci-" + buildNumber + dbgSuffix;

                    if (isPullRequest)
                        suffix += "-pr-" + AppVeyor.Environment.PullRequest.Number;
                    else if (AppVeyor.Environment.Repository.Branch.StartsWith("release", StringComparison.OrdinalIgnoreCase))
                        suffix += "-pre-" + buildNumber;
                    else
                        suffix += "-" + branch;

                    // Nuget limits "special version part" to 20 chars. Add one for the hyphen.
                    if (suffix.Length > 21)
                        suffix = suffix.Substring(0, 21);

                    packageVersion = version + suffix;
                }
            }

            AppVeyor.UpdateBuildVersion(packageVersion);
        }
    });

//////////////////////////////////////////////////////////////////////
// BUILD FRAMEWORKS
//////////////////////////////////////////////////////////////////////

Task("Build")
    .Description("Builds the framework")
    .IsDependentOn("InitializeBuild")
    .WithCriteria(IsRunningOnWindows())
    .Does(() =>
    {
        if(isCompactFrameworkInstalled)
        {
            BuildProject("src/NUnitFramework/framework/nunit.framework-netcf-3.5.csproj", configuration);
            BuildProject("src/NUnitFramework/mock-assembly/mock-assembly-netcf-3.5.csproj", configuration);
            BuildProject("src/NUnitFramework/testdata/nunit.testdata-netcf-3.5.csproj", configuration);
            BuildProject("src/NUnitFramework/tests/nunit.framework.tests-netcf-3.5.csproj", configuration);
            BuildProject("src/NUnitFramework/slow-tests/slow-nunit-tests-netcf-3.5.csproj", configuration);
            BuildProject("src/NUnitFramework/nunitlite/nunitlite-netcf-3.5.csproj", configuration);
            BuildProject("src/NUnitFramework/nunitlite.tests/nunitlite.tests-netcf-3.5.csproj", configuration);
            BuildProject("src/NUnitFramework/nunitlite-runner/nunitlite-runner-netcf-3.5.csproj", configuration);
        }
        else
        {
            Warning("Compact framework build skipped because files were not present.");
            if(isAppveyor)
                throw new Exception("Running Build on Appveyor, but CF not installed, please check that the appveyor-tools.ps1 script ran correctly.");
        }
    });

//////////////////////////////////////////////////////////////////////
// TEST
//////////////////////////////////////////////////////////////////////

Task("CheckForError")
    .Description("Checks for errors running the test suites")
    .Does(() => CheckForError(ref ErrorDetail));

//////////////////////////////////////////////////////////////////////
// TEST FRAMEWORK
//////////////////////////////////////////////////////////////////////

Task("Test")
    .Description("Builds and tests the framework")
    .IsDependentOn("Build")
    .WithCriteria(IsRunningOnWindows())
    .OnError(exception => { ErrorDetail.Add(exception.Message); })
    .Does(() =>
    {
        if(isCompactFrameworkInstalled)
        {
            var runtime = "netcf-3.5";
            var dir = BIN_DIR + runtime + "/";
            RunTest(dir + EXECUTABLE_FRAMEWORK_TESTS, dir, runtime, ref ErrorDetail);
            RunTest(dir + EXECUTABLE_NUNITLITE_TESTS, dir, runtime, ref ErrorDetail);
        }
        else
        {
            Warning("Compact framework tests skipped because files were not present.");
        }
    });

//////////////////////////////////////////////////////////////////////
// PACKAGE
//////////////////////////////////////////////////////////////////////

var RootFiles = new FilePath[]
{
    "LICENSE.txt",
    "NOTICES.txt",
    "CHANGES.txt"
};

var FrameworkFiles = new FilePath[]
{
    "mock-assembly.exe",
    "nunit.framework.dll",
    "nunit.framework.xml",
    "nunit.framework.tests.exe",
    "nunit.testdata.dll",
    "nunitlite.dll",
    "nunitlite.tests.exe",
    "slow-nunit-tests.dll",
    "nunitlite-runner.exe"
};

Task("PackageSource")
    .Description("Creates a ZIP file of the source code")
    .Does(() =>
    {
        CreateDirectory(PACKAGE_DIR);
        RunGitCommand(string.Format("archive -o {0} HEAD", SRC_PACKAGE));
    });

Task("CreateImage")
    .Description("Copies all files into the image directory")
    .Does(() =>
    {
        var currentImageDir = IMAGE_DIR + "NUnit-" + packageVersion + "/";
        var imageBinDir = currentImageDir + "bin/";

        CleanDirectory(currentImageDir);

        CopyFiles(RootFiles, currentImageDir);

        CreateDirectory(imageBinDir);
        Information("Created directory " + imageBinDir);

        var runtime = "netcf-3.5";
        var targetDir = imageBinDir + Directory(runtime);
        var sourceDir = BIN_DIR + Directory(runtime);
        CreateDirectory(targetDir);
        foreach (FilePath file in FrameworkFiles)
        {
            var sourcePath = sourceDir + "/" + file;
            if (FileExists(sourcePath))
                CopyFileToDirectory(sourcePath, targetDir);
        }
    });

Task("Package")
    .Description("Packages the framework")
    .IsDependentOn("CheckForError")
    .IsDependentOn("CreateImage")
    .Does(() =>
    {
        CreateDirectory(PACKAGE_DIR);

        var currentImageDir = IMAGE_DIR + "NUnit-" + packageVersion + "/";

        var zipFiles =
            GetFiles(currentImageDir + "*.*") +
            GetFiles(currentImageDir + "bin/netcf-3.5/*.*");

        Zip(currentImageDir, File(ZIP_PACKAGE), zipFiles);

        NuGetPack("nuget/framework/nunitCF.nuspec", new NuGetPackSettings()
        {
            Version = packageVersion,
            BasePath = currentImageDir,
            OutputDirectory = PACKAGE_DIR
        });
        NuGetPack("nuget/nunitlite/nunitLiteCF.nuspec", new NuGetPackSettings()
        {
            Version = packageVersion,
            BasePath = currentImageDir,
            OutputDirectory = PACKAGE_DIR
        });
    });

//////////////////////////////////////////////////////////////////////
// UPLOAD ARTIFACTS
//////////////////////////////////////////////////////////////////////

Task("UploadArtifacts")
    .Description("Uploads artifacts to AppVeyor")
    .IsDependentOn("Package")
    .Does(() =>
    {
        UploadArtifacts(PACKAGE_DIR, "*.nupkg");
        UploadArtifacts(PACKAGE_DIR, "*.zip");
    });

//////////////////////////////////////////////////////////////////////
// SETUP AND TEARDOWN TASKS
//////////////////////////////////////////////////////////////////////

Teardown(context => CheckForError(ref ErrorDetail));

//////////////////////////////////////////////////////////////////////
// HELPER METHODS - GENERAL
//////////////////////////////////////////////////////////////////////

void RunGitCommand(string arguments)
{
    StartProcess("git", new ProcessSettings()
    {
        Arguments = arguments
    });
}

void UploadArtifacts(string packageDir, string searchPattern)
{
    foreach(var zip in System.IO.Directory.GetFiles(packageDir, searchPattern))
        AppVeyor.UploadArtifact(zip);
}

void CheckForError(ref List<string> errorDetail)
{
    if(errorDetail.Count != 0)
    {
        var copyError = new List<string>();
        copyError = errorDetail.Select(s => s).ToList();
        errorDetail.Clear();
        throw new Exception("One or more unit tests failed, breaking the build.\n"
                              + copyError.Aggregate((x,y) => x + "\n" + y));
    }
}

//////////////////////////////////////////////////////////////////////
// HELPER METHODS - BUILD
//////////////////////////////////////////////////////////////////////

void BuildProject(string projectPath, string configuration)
{
    if(!IsRunningOnWindows()) return;

    MSBuild(projectPath, new MSBuildSettings()
                            .SetConfiguration(configuration)
                            .SetMSBuildPlatform(MSBuildPlatform.x86)
                            .UseToolVersion(MSBuildToolVersion.VS2008)
                            .SetVerbosity(Verbosity.Minimal)
                            .SetNodeReuse(false));
}

//////////////////////////////////////////////////////////////////////
// HELPER METHODS - TEST
//////////////////////////////////////////////////////////////////////

void RunTest(FilePath exePath, DirectoryPath workingDir, string framework, ref List<string> errorDetail)
{
    int rc = StartProcess(
        MakeAbsolute(exePath),
        new ProcessSettings()
        {
            WorkingDirectory = workingDir
        });

    if (rc > 0)
        errorDetail.Add(string.Format("{0}: {1} tests failed", framework, rc));
    else if (rc < 0)
        errorDetail.Add(string.Format("{0} returned rc = {1}", exePath, rc));
}

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Rebuild")
    .Description("Rebuilds the framework")
    .IsDependentOn("Clean")
    .IsDependentOn("Build");

Task("Appveyor")
    .Description("Builds, tests and packages on AppVeyor")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("Package")
    .IsDependentOn("UploadArtifacts");

Task("Default")
    .Description("Builds the framework")
    .IsDependentOn("Build");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
