using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Octokit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Intern
{
    class Program
    {
        static async Task Main(string[] args)
        {
            ServicePointManager.DefaultConnectionLimit = 64;
            ServicePointManager.MaxServicePointIdleTime = 10000;

            var client = new GitHubClient(new ProductHeaderValue("my-cool-app"));

            var request = new SearchRepositoriesRequest
            {
                Language = Language.CSharp,
                SortField = RepoSearchSort.Stars,

                Page = 0
            };

            var response = await client.Search.SearchRepo(request);

            foreach (var repository in response.Items)
            {
                var stopwatch = Stopwatch.StartNew();
                Console.WriteLine($"Repository: {repository.CloneUrl}");
                Console.WriteLine($"Stars: {repository.StargazersCount}");
                Console.WriteLine();

                var filesEnumerable = repository
                    .ListFiles()
                    .Select(path =>
                    {
                        // TODO: Get other MSBuild items too...
                        var isProjectFile = Path.GetExtension(path) == ".csproj";
                        var isPackagesConfig = !isProjectFile && Path.GetFileName(path) == "packages.config";

                        return new ProjectItem
                        {
                            Path = path,
                            IsProjectFile = isProjectFile,
                            IsPackagesConfig = isPackagesConfig,
                        };
                    })
                    .Where(f => f.IsPackagesConfig || f.IsProjectFile);

                var files = new ConcurrentBag<ProjectItem>(filesEnumerable);
                var packages = new ConcurrentBag<PackageReference>();

                var tasks = Enumerable
                    .Range(0, 32)
                    .Select(async i =>
                    {
                        while (files.TryTake(out var item))
                        {
                            try
                            {
                                if (item.IsProjectFile || item.IsPackagesConfig)
                                {
                                    IReadOnlyList<PackageReference> results;
                                    using (var stream = await repository.GetContentAsync(item.Path))
                                    {
                                        results = item.IsProjectFile
                                            ? ParseProjectFile(stream)
                                            : ParsePackagesConfig(stream);
                                    }

                                    foreach (var result in results) packages.Add(result);
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Could not parse '{item.Path}' due to exception: {e}");
                            }
                        }
                    });

                await Task.WhenAll(tasks);

                foreach (var package in packages)
                {
                    Console.WriteLine($"Package: {package.PackageIdentity.Id} {package.AllowedVersions}");
                }

                Console.WriteLine($"Finished in {stopwatch.Elapsed.TotalSeconds} seconds.");
                Console.WriteLine();
            }
        }

        private static IReadOnlyList<PackageReference> ParseProjectFile(Stream stream)
        {
            // TODO: Version range may also be in <Version> subelement!
            return XDocument.Load(stream)
                .XPathSelectElements("//PackageReference")
                .Where(e => e.Attribute("Include") != null)
                .Select(e =>
                {
                    var id = e.Attribute("Include").Value;
                    var versionRange = e.Attribute("Version") == null
                        ? VersionRange.Parse(e.Attribute("Version").Value)
                        : null;

                    return new PackageReference(
                        new PackageIdentity(id, version: null),
                        NuGetFramework.AnyFramework,
                        userInstalled: true,
                        developmentDependency: false /* TODO */,
                        requireReinstallation: false /* TODO */,
                        allowedVersions: versionRange);
                })
                .ToList();
        }

        private static IReadOnlyList<PackageReference> ParsePackagesConfig(Stream stream)
        {
            return new PackagesConfigReader(stream)
                .GetPackages()
                .ToList();
        }

        internal class ProjectItem
        {
            public bool IsProjectFile { get; set; }
            public bool IsPackagesConfig { get; set; }

            public string Path { get; set; }
        }
    }
}
