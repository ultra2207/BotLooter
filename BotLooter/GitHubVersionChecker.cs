﻿using Octokit;
using Serilog;

namespace BotLooter;

public class GitHubVersionChecker
{
    private const long RepositoryId = 635245709;
    private const string RepositoryUrl = "https://github.com/SmallTailTeam/BotLooter";
    
    private readonly ILogger _logger;

    private readonly IGitHubClient _gitHubClient;

    public GitHubVersionChecker(ILogger logger)
    {
        _logger = logger;
        
        _gitHubClient = new GitHubClient(new ProductHeaderValue("BotLooter"));
    }

    public async Task Check(Version currentVersion)
    {
        Release? latestRelease = null;

        try
        {
            latestRelease = await _gitHubClient.Repository.Release.GetLatest(RepositoryId);
        }
        catch
        {
            // ignored
        }

        if (latestRelease is null)
        {
            _logger.Warning("Failed to fetch the latest version of BotLooter.");
            _logger.Information("Your version: {Version}. You can check the latest version here: {RepositoryUrl}", currentVersion, RepositoryUrl);
            return;
        }

        if (!Version.TryParse(latestRelease.TagName, out var releaseVersion))
        {
            _logger.Information("BotLooter {Version} {RepositoryUrl}", currentVersion, RepositoryUrl);
            return;
        }
        
        if (currentVersion == releaseVersion)
        {
            _logger.Information("BotLooter {Version} {RepositoryUrl}", currentVersion, RepositoryUrl);
            return;
        }

        if (currentVersion < releaseVersion)
        {
            _logger.Warning("You are using an outdated version of BotLooter. Version {YourVersion} < {LatestVersion}", currentVersion, releaseVersion);
            _logger.Information("You can download the latest version here: {RepositoryUrl}", RepositoryUrl);
            return;
        }

        if (currentVersion > releaseVersion)
        {
            _logger.Information("It looks like you are using a pre-release version of BotLooter. Version {YourVersion} > {LatestVersion}", currentVersion, releaseVersion);
            return;
        }
    }
}
