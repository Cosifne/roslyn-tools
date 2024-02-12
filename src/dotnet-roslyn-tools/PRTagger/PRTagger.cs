// Licensed to the.NET Foundation under one or more agreements.
// The.NET Foundation licenses this file to you under the MIT license.
// See the License.txt file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.RoslynTools.Authentication;
using Microsoft.RoslynTools.Extensions;
using Microsoft.RoslynTools.Products;
using Microsoft.RoslynTools.Utilities;
using Microsoft.RoslynTools.VS;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Newtonsoft.Json;
using Repository = LibGit2Sharp.Repository;

namespace Microsoft.RoslynTools.PRTagger;

internal static class PRTagger
{
    private const string InsertionLabel = "vs-insertion";

    /// <summary>
    /// Creates GitHub issues containing the PRs inserted into a given VS build.
    /// </summary>
    /// <param name="vsBuild">VS build number</param>
    /// <param name="vsCommitSha">Commit SHA for VS build</param>
    /// <param name="settings">Authentication tokens</param>
    /// <param name="logger"></param>
    /// <returns>Exit code indicating whether issue was successfully created.</returns>
    public static async Task<int> TagPRs(
        RoslynToolsSettings settings,
        AzDOConnection devdivConnection,
        HttpClient gitHubClient,
        ILogger logger)
    {
        using var dncengConnection = new AzDOConnection(settings.DncEngAzureDevOpsBaseUri, "internal", settings.DncEngAzureDevOpsToken);
        // vsBuildsAndCommitSha is ordered from new to old.
        // For each of the product, check if the product is changed from the newest build, keep creating issues if the product has change.
        // Stop when
        // 1. All the VS build has been checked.
        // 2. If some errors happens.
        // 3. If we found the issue with the same title has been created. It means the issue is created because the last run of the tagger.
        foreach (var product in VSBranchInfo.AllProducts)
        {
            var vsBuildsAndCommitSha = GetVSBuildsAndCommitsAsync(de)

            foreach (var (vsBuild, vsCommitSha, previousVsCommitSha) in vsBuildsAndCommitSha)
            {
                var result = await TagProductAsync(product, logger, vsCommitSha, vsBuild, previousVsCommitSha, settings, devdivConnection, dncengConnection, gitHubClient).ConfigureAwait(false);
                if (result is TagResult.Failed or TagResult.IssueAlreadyCreated)
                {
                    break;
                }
            }
        }

        return 0;
    }

    private static async Task<TagResult> TagProductAsync(
        IProduct product, ILogger logger, string vsCommitSha, string vsBuild, string previousVsCommitSha, RoslynToolsSettings settings, AzDOConnection devdivConnection, AzDOConnection dncengConnection, HttpClient gitHubClient)
    {
        var connections = new[] { devdivConnection, dncengConnection };
        // We currently only support creating issues for GitHub repos
        if (!product.RepoHttpBaseUrl.Contains("github.com"))
        {
            return TagResult.Failed;
        }

        var gitHubRepoName = product.RepoHttpBaseUrl.Split('/').Last();
        logger.LogInformation($"GitHub repo: {gitHubRepoName}");

        // Get associated product build for current and previous VS commit SHAs
        var currentBuild = await TryGetBuildNumberForReleaseAsync(product.ComponentJsonFileName, product.ComponentName, vsCommitSha, devdivConnection, logger).ConfigureAwait(false);
        var previousBuild = await TryGetBuildNumberForReleaseAsync(product.ComponentJsonFileName, product.ComponentName, previousVsCommitSha, devdivConnection, logger).ConfigureAwait(false);

        if (currentBuild is null)
        {
            logger.LogError($"{gitHubRepoName} build not found for VS commit SHA {currentBuild}.");
            return TagResult.Failed;
        }

        if (previousBuild is null)
        {
            logger.LogError($"{gitHubRepoName} build not found for VS commit SHA {previousBuild}.");
            return TagResult.Failed;
        }

        // If builds are the same, there are no PRs to tag
        if (currentBuild.Equals(previousBuild))
        {
            logger.LogInformation($"No PRs found to tag; {gitHubRepoName} build numbers are equal: {currentBuild}.");
            return TagResult.NoChangeBetweenVSBuilds;
        }

        // Get commit SHAs for product builds
        var previousProductCommitSha = await TryGetProductCommitShaFromBuildAsync(product, connections, previousBuild, logger).ConfigureAwait(false);
        var currentProductCommitSha = await TryGetProductCommitShaFromBuildAsync(product, connections, currentBuild, logger).ConfigureAwait(false);

        if (previousProductCommitSha is null || currentProductCommitSha is null)
        {
            logger.LogError($"Error retrieving {gitHubRepoName} commit SHAs.");
            return TagResult.Failed;
        }

        logger.LogInformation($"Finding PRs between {gitHubRepoName} commit SHAs {previousProductCommitSha} and {currentProductCommitSha}.");

        // Retrieve GitHub repo
        string? gitHubRepoPath;
        try
        {
            gitHubRepoPath = Environment.CurrentDirectory + "\\" + gitHubRepoName;
            if (!Repository.IsValid(gitHubRepoPath))
            {
                logger.LogInformation("Cloning GitHub repo...");
                gitHubRepoPath = Repository.Clone(product.RepoHttpBaseUrl, workdirPath: gitHubRepoPath);
            }
            else
            {
                logger.LogInformation($"Repo already exists at {gitHubRepoPath}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Exception while cloning repo: " + ex);
            return TagResult.Failed;
        }

        // Find PRs between product commit SHAs
        var prDescription = new StringBuilder();
        var isSuccess = PRFinder.PRFinder.FindPRs(previousProductCommitSha, currentProductCommitSha, PRFinder.PRFinder.DefaultFormat, logger, gitHubRepoPath, prDescription);
        if (isSuccess != 0)
        {
            // Error occurred; should be logged in FindPRs method
            return TagResult.Failed;
        }

        var issueTitle = $"[Automated] PRs inserted in VS build {vsBuild}";
        var hasIssueAlreadyCreated = await HasIssueAlreadyCreatedAsync(gitHubClient, gitHubRepoName, issueTitle, logger).ConfigureAwait(false);
        if (hasIssueAlreadyCreated)
        {
            logger.LogInformation($"Issue with name: {issueTitle} exists in repo: {gitHubRepoName}. Skip creation.");
            return TagResult.IssueAlreadyCreated;
        }

        logger.LogInformation($"Creating issue...");

        // Create issue
        return await TryCreateIssueAsync(gitHubClient, issueTitle, gitHubRepoName, prDescription.ToString(), logger).ConfigureAwait(false);
    }

    public static async Task<ImmutableArray<(string vsBuild, string vsCommit, string previousVsCommitSha)>> GetVSBuildsAndCommitsAsync(
        string repoName,
        AzDOConnection devdivConnection,
        HttpClient gitHubClient,
        ILogger logger,
        int vsBuildNumber,
        CancellationToken cancellationToken)
    {
        var lastVsBuildNumberReported = await FindTheLastReportedVSBuildAsync(gitHubClient, repoName, logger).ConfigureAwait(false);
        var builds = await devdivConnection.TryGetBuildsAsync(
            "DD-CB-TestSignVS",
            logger: logger,
            maxBuildNumberFetch: vsBuildNumber,
            resultsFilter: BuildResult.Succeeded,
            buildQueryOrder: BuildQueryOrder.FinishTimeDescending).ConfigureAwait(false);

        var vsRepository = await GetVSRepositoryAsync(devdivConnection.GitClient);
        if (builds is not null)
        {
            // Find previous VS commit SHA
            var buildInfoTask = builds.Select(async build =>
            {
                var vsCommit = await devdivConnection.GitClient.GetCommitAsync(
                    build.SourceVersion, vsRepository.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                var previousVsCommitSha = vsCommit.Parents.First();
                return (build.BuildNumber, build.SourceVersion, previousVsCommitSha);
            });

            var vsBuildAndCommitSha = await Task.WhenAll(buildInfoTask).ConfigureAwait(false);
            return vsBuildAndCommitSha.ToImmutableArray();
        }
        else
        {
            return ImmutableArray<(string vsBuild, string vsCommit, string previousVsCommitSha)>.Empty;
        }
    }

    private static async Task<GitRepository> GetVSRepositoryAsync(GitHttpClient gitClient)
    {
        return await gitClient.GetRepositoryAsync("DevDiv", "VS");
    }

    private static async Task<string?> TryGetBuildNumberForReleaseAsync(
        string componentJsonFileName,
        string componentName,
        string vsCommitSha,
        AzDOConnection vsConnection,
        ILogger logger)
    {
        var url = await VisualStudioRepository.GetUrlFromComponentJsonFileAsync(
            vsCommitSha, GitVersionType.Commit, vsConnection, componentJsonFileName, componentName);
        if (url is null)
        {
            logger.LogError($"Could not retrieve URL from component JSON file.");
            return null;
        }

        try
        {
            var buildNumber = VisualStudioRepository.GetBuildNumberFromUrl(url);
            logger.LogInformation($"Retrieved build number from URL: {buildNumber}");
            return buildNumber;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error retrieving build number from URL: {ex}");
            return null;
        }
    }

    private static async Task<string?> TryGetProductCommitShaFromBuildAsync(
        IProduct product,
        AzDOConnection[] connections,
        string buildNumber,
        ILogger logger)
    {
        foreach (var connection in connections)
        {
            var buildPipelineName = product.GetBuildPipelineName(connection.BuildProjectName);
            logger.LogInformation($"Build pipeline name: {buildPipelineName}");
            if (buildPipelineName is not null)
            {
                var build = (await connection.TryGetBuildsAsync(buildPipelineName, buildNumber, logger))?.SingleOrDefault();
                if (build is not null)
                {
                    logger.LogInformation($"Build source version: {build.SourceVersion}");
                    return build.SourceVersion;
                }
            }
        }

        return null;
    }

    private static async Task<TagResult> TryCreateIssueAsync(
        HttpClient client,
        string title,
        string gitHubRepoName,
        string issueBody,
        ILogger logger)
    {

        // https://docs.github.com/en/rest/issues/issues#create-an-issue
        var response = await client.PostAsyncAsJson($"repos/dotnet/{gitHubRepoName}/issues", JsonConvert.SerializeObject(
            new
            {
                title = title,
                body = issueBody,
                labels = new string[] { InsertionLabel }
            }));

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError($"Issue creation failed with status code: {response.StatusCode}");
            return TagResult.Failed;
        }

        logger.LogInformation("Successfully created issue.");
        return TagResult.Succeed;
    }

    /// <summary>
    /// Check if the issue with <param name="title"/> exists in repo.
    /// </summary>
    private static async Task<bool> HasIssueAlreadyCreatedAsync(
        HttpClient client,
        string repoName,
        string title,
        ILogger logger)
    {
        var response = await client.GetAsync($"search/issues?q={title}+repo:dotnet/{repoName}+is:issue+label:{InsertionLabel}").ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Failed to search on GitHub");
            throw new Exception($"Error happens when try to search {title} in {repoName}.");
        }

        var content = await response.Content.ReadAsStringAsync();
        var jsonResponseContent = JsonObject.Parse(content)!;
        var issueNumber = int.Parse(jsonResponseContent["total_count"]!.ToString());
        return issueNumber != 0;
    }

    private static async Task<string?> FindTheLastReportedVSBuildAsync(
        HttpClient client,
        string repoName,
        ILogger logger)
    {
        var jsonResponse = await SearchIssuesOnGitHubAsync(client, repoName, logger, label: InsertionLabel).ConfigureAwait(false);
        var totalCountNumber = TotalCountNumber(jsonResponse);
        if (totalCountNumber == 0)
        {
            logger.LogInformation($"No existing issue has been found for repo: {repoName}.");
            return null;
        }

        // 'Items' is required in response schema.
        // https://docs.github.com/en/rest/search/search?apiVersion=2022-11-28
        var lastReportedIssue = jsonResponse["Items"]!.AsArray().First();
        var lastReportedIssueTitle = lastReportedIssue!["title"]!.ToString();
        return lastReportedIssueTitle["[Automated] PRs inserted in VS build".Length..];

    }

    private static int TotalCountNumber(JsonNode response)
    {
        // https://docs.github.com/en/rest/search/search?apiVersion=2022-11-28
        // total_count is required in response schema
        return int.Parse(response["total_count"]!.ToString());
    }

    /// <summary>
    /// Search issues by using <param name="title"/> and <param name="label"/> in <param name="repoName"/>
    /// By default this is ordered from new to old. See https://docs.github.com/en/rest/search/search?apiVersion=2022-11-28#ranking-search-results
    /// </summary>
    /// <param name="client"></param>
    /// <param name="repoName"></param>
    /// <param name="logger"></param>
    /// <param name="title"></param>
    /// <param name="label"></param>
    /// <returns></returns>
    private static async Task<JsonNode> SearchIssuesOnGitHubAsync(
        HttpClient client,
        string repoName,
        ILogger logger,
        string? title = null,
        string? label = null)
    {
        // If title and label are both null, there is nothing to search.
        Debug.Assert(title is null && label is null);
        var queryBuilder = new StringBuilder();
        queryBuilder.Append("search/issues?q=");
        if (title is not null)
        {
            queryBuilder.Append($"{title}+");
        }

        if (label is not null)
        {
            queryBuilder.Append($"label:{label}+");
        }

        queryBuilder.Append($"is:issue+repo:dotnet/{repoName}");
        var query = queryBuilder.ToString();

        logger.LogInformation($"Searching query is {query}.");
        var response = await client.GetAsync(query).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var jsonResponseContent = JsonObject.Parse(content)!;
        return jsonResponseContent;
    }
}
