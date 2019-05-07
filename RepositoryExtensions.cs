using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Intern
{
    using GitHubRepository = Octokit.Repository;
    using GitRepository = LibGit2Sharp.Repository;
    using GitCommit = LibGit2Sharp.Commit;

    internal static class RepositoryExtensions
    {
        private static HttpClient HttpClient = new HttpClient();

        public static void Fetch(this GitRepository repository, Remote remote)
        {
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification).ToList();

            Commands.Fetch(repository, remote.Name, refSpecs, options: null, "");
        }

        public static IReadOnlyList<string> ListFiles(this GitHubRepository repository)
        {
            return ListFiles(repository.CloneUrl, repository.DefaultBranch);
        }

        public static async Task<Stream> GetContentAsync(this GitHubRepository repository, string path)
        {
            var url = $"https://raw.githubusercontent.com/{repository.Owner.Login}/{repository.Name}/{repository.DefaultBranch}/{path}";
            var response = await HttpClient.GetAsync(url);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync();
        }

        internal static IReadOnlyList<string> ListFiles(string cloneUrl, string branchName)
        {
            using (var temp = new TemporaryDirectory())
            {
                // "git init"
                GitRepository.Init(temp.Path);
                using (var git = new GitRepository(temp.Path))
                {
                    // "git remote add origin <remote url>"
                    // "git fetch"
                    git.Fetch(git.Network.Remotes.Add("origin", cloneUrl));

                    // "git ls-tree origin/branch-name --full-tree --name-only -r"
                    return git.ListFiles($"origin/{branchName}");
                }
            }
        }

        public static IReadOnlyList<string> ListFiles(this GitRepository repository, string branch)
        {
            var tree = repository.Lookup<GitCommit>(branch)?.Tree;

            if (tree == null) return new List<string>();

            var result = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            var path = new List<string>();

            FindPaths(tree, path, result);

            return result.ToList();
        }

        private static void FindPaths(Tree tree, List<string> path, HashSet<string> result)
        {
            foreach (var item in tree)
            {
                path.Add(item.Name);

                switch (item.TargetType)
                {
                    case TreeEntryTargetType.Blob:
                        result.Add(string.Join('/', path.ToArray()));
                        break;

                    case TreeEntryTargetType.Tree:
                        FindPaths(item.Target.Peel<Tree>(), path, result);
                        break;

                    // TODO?
                    case TreeEntryTargetType.GitLink:
                        break;
                }

                path.RemoveAt(path.Count - 1);
            }
        }
    }
}
