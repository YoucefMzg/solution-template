class Build : NukeBuild
{
    [Parameter] readonly string ArtifactoryUsername;
    [Parameter] readonly string ArtifactoryPassword;
    [Parameter] readonly string ArtifactoryNugetSourceUrl;

    [GitRepository] [Required] readonly GitRepository GitRepository;
    [Solution] readonly Solution Solution;

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath BinaryArtifactDirectory => ArtifactsDirectory / "bin";


    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    Target ConfigureNuget => tgt => tgt
        .OnlyWhenStatic(() => !string.IsNullOrWhiteSpace(ArtifactoryNugetSourceUrl)
                              && !string.IsNullOrWhiteSpace(ArtifactoryUsername)
                              && !string.IsNullOrWhiteSpace(ArtifactoryPassword))
        .Executes(() =>
        {
            try
            {
                DotNetNuGetAddSource(n => n
                    .SetSource($"{ArtifactoryNugetSourceUrl}")
                    .SetName("Artifactory")
                    .SetUsername(ArtifactoryUsername)
                    .SetPassword(ArtifactoryPassword)
                    .EnableStorePasswordInClearText()
                );
            }
            catch
            {
                DotNet(
                    $"nuget update source Artifactory --source {ArtifactoryNugetSourceUrl}/ --username {ArtifactoryUsername} --password {ArtifactoryPassword} --store-password-in-clear-text");
            }
        });

    Target Clean => d => d
        .OnlyWhenStatic(() => !IsLocalBuild)
        .Executes(() =>
        {
            // TODO: setup serilog properly
            // Serilog.Log.Information($"Cleaning bin artifact");
            BinaryArtifactDirectory.CreateOrCleanDirectory();

            // Serilog.Log.Information($"Cleaning bin and obj directories");
            Solution.Directory.GlobDirectories("*/bin", "*/obj").DeleteDirectories();
        });

    Target Restore => _ => _
        .DependsOn(ConfigureNuget)
        .DependsOn(Clean)
        .Executes(() =>
        {
            // TODO: SetVerbosity should be a parameter
            DotNetRestore(configurator => configurator
                .SetVerbosity(DotNetVerbosity.quiet));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(configurator => configurator
                .SetConfiguration(Configuration)
                .SetNoRestore(SucceededTargets.Contains(Restore))
                .SetVerbosity(DotNetVerbosity.quiet)
                .SetRepositoryUrl(GitRepository.HttpsUrl)
            );
        });
}
