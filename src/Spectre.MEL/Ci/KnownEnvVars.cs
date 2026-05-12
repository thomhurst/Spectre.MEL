namespace Spectre.MEL.Ci;

internal static class KnownEnvVars
{
    public const string GitHubActions = "GITHUB_ACTIONS";
    public const string AzurePipelines = "TF_BUILD";
    public const string GitLabCi = "GITLAB_CI";
    public const string TeamCity = "TEAMCITY_VERSION";
    public const string Buildkite = "BUILDKITE";
    public const string CircleCi = "CIRCLECI";
    public const string AppVeyor = "APPVEYOR";
    public const string Travis = "TRAVIS";
    public const string JenkinsUrl = "JENKINS_URL";
    public const string JenkinsHome = "JENKINS_HOME";
}
