using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using Vintagestory.API.Common;

namespace CakeBuild
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            return new CakeHost()
                .UseContext<BuildContext>()
                .Run(args);
        }
    }

    public class BuildContext : FrostingContext
    {
        public const string ProjectName = "asphyxiarebreathed";
        public const string ProjectDirectory = "asphyxiarebreathed";
        public const string PublishDirectory = "Build/publish";
        public const string PackageDirectory = "Build/package";
        public string BuildConfiguration { get; }
        public string Version { get; }
        public string Name { get; }
        public bool SkipJsonValidation { get; }

        public BuildContext(ICakeContext context)
            : base(context)
        {
            BuildConfiguration = context.Argument("configuration", "Release");
            SkipJsonValidation = context.Argument("skipJsonValidation", false);
            var modInfo = context.DeserializeJsonFromFile<ModInfo>($"{ProjectDirectory}/modinfo.json");
            Version = modInfo.Version;
            Name = modInfo.ModID;
        }
    }

    [TaskName("ValidateJson")]
    public sealed class ValidateJsonTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            if (context.SkipJsonValidation)
            {
                return;
            }

            var jsonFiles = context.GetFiles($"{BuildContext.ProjectDirectory}/assets/**/*.json");
            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(file.FullPath);
                    JToken.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new Exception($"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
                }
            }
        }
    }

    [TaskName("Build")]
    [IsDependentOn(typeof(ValidateJsonTask))]
    public sealed class BuildTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            context.DotNetClean($"{BuildContext.ProjectDirectory}/{BuildContext.ProjectName}.csproj",
                new DotNetCleanSettings
                {
                    Configuration = context.BuildConfiguration
                });

            context.CleanDirectory(BuildContext.PublishDirectory);

            context.DotNetPublish($"{BuildContext.ProjectDirectory}/{BuildContext.ProjectName}.csproj",
                new DotNetPublishSettings
                {
                    Configuration = context.BuildConfiguration,
                    OutputDirectory = BuildContext.PublishDirectory
                });
        }
    }

    [TaskName("Package")]
    [IsDependentOn(typeof(BuildTask))]
    public sealed class PackageTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            context.EnsureDirectoryExists("Releases");
            context.CleanDirectory(BuildContext.PackageDirectory);
            context.EnsureDirectoryExists(BuildContext.PackageDirectory);
            context.CopyFile($"{BuildContext.PublishDirectory}/{context.Name}.dll", $"{BuildContext.PackageDirectory}/{context.Name}.dll");

            if (context.DirectoryExists($"{BuildContext.ProjectDirectory}/assets"))
            {
                context.CopyDirectory($"{BuildContext.ProjectDirectory}/assets", $"{BuildContext.PackageDirectory}/assets");
            }

            context.CopyFile($"{BuildContext.ProjectDirectory}/modinfo.json", $"{BuildContext.PackageDirectory}/modinfo.json");
            if (context.FileExists($"{BuildContext.ProjectDirectory}/modicon.png"))
            {
                context.CopyFile($"{BuildContext.ProjectDirectory}/modicon.png", $"{BuildContext.PackageDirectory}/modicon.png");
            }

            var releaseArchive = $"Releases/{context.Name}_{context.Version}.zip";
            if (context.FileExists(releaseArchive))
            {
                throw new Exception($"Release archive already exists: {releaseArchive}");
            }

            context.Zip(BuildContext.PackageDirectory, releaseArchive);
        }
    }

    [TaskName("Default")]
    [IsDependentOn(typeof(PackageTask))]
    public class DefaultTask : FrostingTask
    {
    }
}
