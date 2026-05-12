using MEL.Spectre.Ci;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class CiDetectorTests
{
    [Test]
    [Arguments("GITHUB_ACTIONS", "true", CiMode.GitHubActions)]
    [Arguments("TF_BUILD", "True", CiMode.AzurePipelines)]
    [Arguments("GITLAB_CI", "true", CiMode.GitLabCi)]
    [Arguments("TEAMCITY_VERSION", "2024.1", CiMode.TeamCity)]
    [Arguments("BUILDKITE", "true", CiMode.Buildkite)]
    [Arguments("CIRCLECI", "true", CiMode.CircleCi)]
    [Arguments("APPVEYOR", "True", CiMode.AppVeyor)]
    [Arguments("TRAVIS", "true", CiMode.Travis)]
    [Arguments("JENKINS_URL", "http://jenkins/", CiMode.Jenkins)]
    public async Task Detects_provider_from_single_var(string key, string value, CiMode expected)
    {
        var env = new Dictionary<string, string?> { [key] = value };
        var detected = CiDetector.DetectFromEnvironment(env);
        await Assert.That(detected).IsEqualTo(expected);
    }

    [Test]
    public async Task Returns_off_when_no_vars_set()
    {
        var detected = CiDetector.DetectFromEnvironment(new Dictionary<string, string?>());
        await Assert.That(detected).IsEqualTo(CiMode.Off);
    }

    [Test]
    public async Task GitHub_wins_when_multiple_present()
    {
        var env = new Dictionary<string, string?>
        {
            ["GITHUB_ACTIONS"] = "true",
            ["TF_BUILD"] = "true",
            ["GITLAB_CI"] = "true",
        };
        await Assert.That(CiDetector.DetectFromEnvironment(env)).IsEqualTo(CiMode.GitHubActions);
    }

    [Test]
    public async Task False_value_is_treated_as_off()
    {
        var env = new Dictionary<string, string?> { ["GITHUB_ACTIONS"] = "false" };
        await Assert.That(CiDetector.DetectFromEnvironment(env)).IsEqualTo(CiMode.Off);
    }
}
