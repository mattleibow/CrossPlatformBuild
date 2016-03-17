#tool nuget:?package=XamarinComponent

#addin nuget:?package=Octokit
#addin nuget:?package=Cake.Xamarin
#addin nuget:?package=Cake.FileHelpers

using System.Net;
using Octokit;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default").ToUpper();
var configuration = Argument("configuration", "Release");

// special logic to tweak builds for platform limitations

var ForMacOnly = target.EndsWith("-MAC");
var ForWindowsOnly = target.EndsWith("-WINDOWS");
var ForEverywhere = !ForMacOnly && !ForWindowsOnly;

var ForWindows = ForEverywhere || !ForMacOnly;
var ForMac = ForEverywhere || !ForWindowsOnly;

target = target.Replace("-MAC", string.Empty).Replace("-WINDOWS", string.Empty);

Information("Building target '{0}' for {1}.", target, ForEverywhere ? "everywhere" : (ForWindowsOnly ? "Windows only" : "Mac only"));

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// the tools
FilePath XamarinComponentPath = "./tools/XamarinComponent/tools/xamarin-component.exe";

// the output folder
DirectoryPath outDir = "./output/";
FilePath outputZip = "output.zip";
if (!DirectoryExists(outDir)) {
    CreateDirectory(outDir);
}

// get CI variables
var sha = EnvironmentVariable("APPVEYOR_REPO_COMMIT") ?? EnvironmentVariable("TRAVIS_COMMIT");
var branch = EnvironmentVariable("APPVEYOR_REPO_BRANCH") ?? EnvironmentVariable("TRAVIS_BRANCH");
var tag = EnvironmentVariable("APPVEYOR_REPO_TAG_NAME") ?? EnvironmentVariable("TRAVIS_TAG");
var pull = EnvironmentVariable("APPVEYOR_PULL_REQUEST_NUMBER") ?? EnvironmentVariable("TRAVIS_PULL_REQUEST");

// get the temporary build artifacts filename
var buildType = "COMMIT";
if (!string.IsNullOrEmpty(pull) && !string.Equals(pull, "false", StringComparison.OrdinalIgnoreCase)) {
    buildType = "PULL" + pull;
} else if (!string.IsNullOrEmpty(tag)) {
    buildType = "TAG";
}
var tagOrBranch = branch;
if (!string.IsNullOrEmpty(tag)) {
    tagOrBranch = tag;
}
var TemporaryArtifactsFilename = string.Format("{0}_{1}_{2}.zip", buildType, tagOrBranch, sha);

// the GitHub communication (for storing the temporary build artifacts)
var GitHubToken = EnvironmentVariable("GitHubToken");
var GitHubUser = "mattleibow";
var GitHubRepository = "CrossPlatformBuild";
var GitHubBuildTag = "CI";

// make sure we use the correct version of the NuGet feed
var NuGetSource = new [] { ForWindows ? "https://api.nuget.org/v3/index.json" : "https://www.nuget.org/api/v2/" };

//////////////////////////////////////////////////////////////////////
// FUNCTIONS
//////////////////////////////////////////////////////////////////////

var Build = new Action<FilePath>((solution) =>
{
    if (IsRunningOnWindows()) {
        MSBuild(solution, s => s.SetConfiguration(configuration).SetMSBuildPlatform(MSBuildPlatform.x86));
    } else {
        XBuild(solution, s => s.SetConfiguration(configuration));
    }
});

void MergeDirectory(DirectoryPath source, DirectoryPath dest, bool replace)
{
    var sourceDirName = source.FullPath;
    var destDirName = dest.FullPath;
    
    if (!DirectoryExists(source)) {
        throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + source);
    }

    if (!DirectoryExists(dest)) {
        CreateDirectory(dest);
    }

    DirectoryInfo dir = new DirectoryInfo(sourceDirName);
    
    FileInfo[] files = dir.GetFiles();
    foreach (FileInfo file in files) {
        string temppath = dest.CombineWithFilePath(file.Name).FullPath;
        if (FileExists(temppath)) {
            if (replace) {
                DeleteFile(temppath);
            }
        } else {
            file.CopyTo(temppath);
        }
    }

    DirectoryInfo[] dirs = dir.GetDirectories();
    foreach (DirectoryInfo subdir in dirs) {
        string temppath = dest.Combine(subdir.Name).FullPath;
        MergeDirectory(subdir.FullName, temppath, replace);
    }
}

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    // clean can be done on any platform
    var dirs = new [] { 
        "./output",
        // source
        "./Source/packages",
        "./Source/*/bin", 
        "./Source/*/obj", 
        // samples
        "./Samples/packages",
        "./Samples/*/bin",
        "./Samples/*/obj",
    };
    foreach (var dir in dirs) {
        CleanDirectories(dir);
    }
});

Task("RestorePackages")
    .Does(() =>
{
    // restoring NuGets can be done on any platform
    var solutions = new [] { 
        "./Source/CrossPlatformBuild.sln", 
        "./Samples/CrossPlatformBuild.Samples.sln",
    };
    foreach (var solution in solutions) {
        Information("Restoring {0}...", solution);
        RestoreComponents(solution, new XamarinComponentRestoreSettings {
            ToolPath = XamarinComponentPath
        });
        NuGetRestore(solution, new NuGetRestoreSettings {
            Source = NuGetSource,
            Verbosity = NuGetVerbosity.Detailed
        });
    }
});

Task("Build")
    .IsDependentOn("RestorePackages")
    .Does(() =>
{
    // get the platform-specific solution name
    FilePath solution = "./Source/CrossPlatformBuild.sln";
    if (ForWindowsOnly) {
        solution = "./Source/CrossPlatformBuild.Windows.sln";
    } else if (ForMacOnly) {
        solution = "./Source/CrossPlatformBuild.Mac.sln";
    }
    
    // build the solution
    Information("Building {0}...", solution);
    Build(solution);
    
    // get the outputs, making suer to only include the ones that built
    var outputs = new Dictionary<string, string> {
        { "./Source/CrossPlatformBuild.Core/bin/{0}/CrossPlatformBuild.Core.dll", "pcl" },
    };
    if (ForWindows) {
        outputs.Add("./Source/CrossPlatformBuild.Android/bin/{0}/CrossPlatformBuild.Android.dll", "android");
        outputs.Add("./Source/CrossPlatformBuild.WindowsPhone/bin/{0}/CrossPlatformBuild.WindowsPhone.dll", "wpa81");
    }
    if (ForMac) {
        outputs.Add("./Source/CrossPlatformBuild.iOS/bin/{0}/CrossPlatformBuild.iOS.dll", "ios");
    }
    
    // copy the outputs
    foreach (var output in outputs) {
        FilePath source = string.Format(output.Key, configuration);
        FilePath dest = outDir.Combine(output.Value).CombineWithFilePath(source.GetFilename());
        DirectoryPath dir = dest.GetDirectory();
        if (!DirectoryExists(dir)) {
            CreateDirectory(dir);
        }
        CopyFile(source, dest);
    }
});

Task("BuildSamples")
    .IsDependentOn("RestorePackages")
    .Does(() =>
{
    if (ForWindows) {
        Build("./Samples/WindowsPhoneSample.sln");
        Build("./Samples/AndroidSample.sln");
    }
    if (ForMac) {
        Build("./Samples/iOSSample.sln");
    }
});

Task("PackageNuGet")
    .IsDependentOn("Build")
    .WithCriteria(ForWindows)
    .Does(() =>
{
    NuGetPack("./NuGet/CrossPlatformBuild.nuspec", new NuGetPackSettings {
        OutputDirectory = outDir,
        Verbosity = NuGetVerbosity.Detailed,
        BasePath = IsRunningOnUnix() ? "././" : "./",
    });
});

Task("PackageComponent")
    .IsDependentOn("Build")
    .IsDependentOn("PackageNuGet")
    .IsDependentOn("BuildSamples")
    .WithCriteria(ForWindows)
    .Does(() =>
{
    DeleteFiles("./Component/*.xam");
    PackageComponent("./Component/", new XamarinComponentSettings { 
        ToolPath = XamarinComponentPath 
    });
    DeleteFiles("./output/*.xam");
    MoveFiles("./Component/*.xam", outDir);
});

Task("Package")
    .IsDependentOn("DownloadArtifacts")
    .IsDependentOn("PackageNuGet")
    .IsDependentOn("PackageComponent")
    .Does(() =>
{
    // although this can be done on any platform, the build order
    // requires that the packaging is done on Windows (either after
    // the iOS build is done, or if eveything thing was built on
    // Windows using the iOS build host) 
});

//////////////////////////////////////////////////////////////////////
// TEMPORARY ARTIFACT MANAGEMENT
//////////////////////////////////////////////////////////////////////

Task("DownloadArtifacts")
    .WithCriteria(!string.IsNullOrEmpty(sha))
    .WithCriteria(!ForEverywhere)
    .Does(() =>
{
    if (ForWindowsOnly) {
        Information("Connecting to GitHub...");
        var client = new GitHubClient(new ProductHeaderValue("CrossPlatformBuild"));
        client.Credentials = new Credentials(GitHubToken);
        
        Information("Loading releases...");
        var releases = client.Release.GetAll(GitHubUser, GitHubRepository).Result;
        var releaseId = releases.Single(r => r.TagName == GitHubBuildTag).Id;
        
        Information("Loading CI release...");
        Release release = null;
        ReleaseAsset asset = null;
        var waitSeconds = 0;
        while (asset == null) {
            release = client.Release.Get(GitHubUser, GitHubRepository, releaseId).Result;
            Information("Loading asset...");
            asset = release.Assets.SingleOrDefault(a => a.Name == TemporaryArtifactsFilename);
            if (asset == null) {
                // only try for 15 minutes
                if (waitSeconds > 15 * 60) {
                    throw new Exception("Unable to download assets, maybe the build has failed.");
                }
                Information("Asset not found, waiting another 30 seconds.");
                waitSeconds += 30;
                System.Threading.Thread.Sleep(1000 * 30);
            }
        }
        Information("Found asset: {0}", asset.Id);
        Information("Url: {0}", asset.BrowserDownloadUrl);
        
        Information("Downloading asset...");
        if (FileExists(outputZip)) {
            DeleteFile(outputZip);
        }
        var url = string.Format("https://api.github.com/repos/{0}/{1}/releases/assets/{2}?access_token={3}", GitHubUser, GitHubRepository, asset.Id, GitHubToken);
        var wc = new WebClient();
        wc.Headers.Add("Accept", "application/octet-stream");
        wc.Headers.Add("User-Agent", "CrossPlatformBuild");
        wc.DownloadFile(url, outputZip.FullPath);
        
        Information("Extracting output...");
        DirectoryPath tmp = "./temp-output/";
        if (DirectoryExists(tmp)) {
            CleanDirectory(tmp);
        } else {
            CreateDirectory(tmp);
        }
        Unzip(outputZip, tmp);
        MergeDirectory(tmp, outDir, false);
    }
});

Task("UploadArtifacts")
    .WithCriteria(!string.IsNullOrEmpty(sha))
    .WithCriteria(!ForEverywhere)
    .Does(() =>
{
    Information("Connecting to GitHub...");
    var client = new GitHubClient(new ProductHeaderValue("CrossPlatformBuild"));
    client.Credentials = new Credentials(GitHubToken);

    Information("Loading releases...");
    var releases = client.Release.GetAll(GitHubUser, GitHubRepository).Result;
    var releaseId = releases.Single(r => r.TagName == GitHubBuildTag).Id;

    Information("Loading CI release...");
    var release = client.Release.Get(GitHubUser, GitHubRepository, releaseId).Result;

    Information("Loading asset...");
    var asset = release.Assets.SingleOrDefault(a => a.Name == TemporaryArtifactsFilename);
    
    if (asset != null) {
        Information("Deleting asset...");
        client.Release.DeleteAsset(GitHubUser, GitHubRepository, asset.Id).Wait();
    } else {
        Information("Asset not found.");
    }

    if (ForMacOnly) {
        Information("Compressing output...");
        if (FileExists(outputZip)) {
            DeleteFile(outputZip);
        }
        Zip(outDir, outputZip);

        Information("Creating asset...");
        var archiveContents = System.IO.File.OpenRead(outputZip.FullPath);
        var assetUpload = new ReleaseAssetUpload {
            FileName = TemporaryArtifactsFilename,
            ContentType = "application/zip",
            RawData = archiveContents
        };
        
        Information("Uploading asset...");
        asset = client.Release.UploadAsset(release, assetUpload).Result;
        Information("Uploaded asset: {0}", asset.Id);
        Information("Url: {0}", asset.BrowserDownloadUrl);
    }
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Build")
    .IsDependentOn("DownloadArtifacts")
    .IsDependentOn("Package")
    .IsDependentOn("UploadArtifacts");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
