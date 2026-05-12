namespace MEL.Spectre.Ci;

internal static class CiDetector
{
    public static CiMode DetectFromEnvironment(IDictionary<string, string?> env) =>
        Detect(key => env.TryGetValue(key, out var v) ? v : null);

    public static CiMode DetectFromEnvironment() =>
        Detect(Environment.GetEnvironmentVariable);

    private static CiMode Detect(Func<string, string?> read)
    {
        if (Truthy(read(KnownEnvVars.GitHubActions))) return CiMode.GitHubActions;
        if (Truthy(read(KnownEnvVars.AzurePipelines))) return CiMode.AzurePipelines;
        if (Truthy(read(KnownEnvVars.GitLabCi))) return CiMode.GitLabCi;
        if (HasValue(read(KnownEnvVars.TeamCity))) return CiMode.TeamCity;
        if (Truthy(read(KnownEnvVars.Buildkite))) return CiMode.Buildkite;
        if (Truthy(read(KnownEnvVars.CircleCi))) return CiMode.CircleCi;
        if (Truthy(read(KnownEnvVars.AppVeyor))) return CiMode.AppVeyor;
        if (Truthy(read(KnownEnvVars.Travis))) return CiMode.Travis;
        if (HasValue(read(KnownEnvVars.JenkinsUrl)) || HasValue(read(KnownEnvVars.JenkinsHome))) return CiMode.Jenkins;
        return CiMode.Off;
    }

    private static bool Truthy(string? v) =>
        !string.IsNullOrEmpty(v) && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) && v != "0";

    private static bool HasValue(string? v) => !string.IsNullOrEmpty(v);
}
