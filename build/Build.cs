class Build : NukeBuild
{
    [Parameter] readonly string ArtifactoryUsername;
    [Parameter] readonly string ArtifactoryPassword;
    [Parameter] readonly string ArtifactoryNugetSourceUrl;

    [Parameter] readonly string AssemblySemVer;
    [Parameter] readonly string AssemblySemFileVer;
    [Parameter] readonly string InformationalVersion;

    [GitRepository] [Required] readonly GitRepository GitRepository;
    [Solution] readonly Solution Solution;

    const string ProjectName = "Api";
    Project Project => Solution.GetAllProjects("*").Single(p => ProjectName.Equals(p.Name, StringComparison.Ordinal));

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath BinaryArtifactsDirectory => ArtifactsDirectory / "bin";
    AbsolutePath TestResultsDirectory => ArtifactsDirectory / "test-results";
    AbsolutePath IntegrationTestsResultDirectory => ArtifactsDirectory / "integration-test-results";
    AbsolutePath CoverageReportsDirectory => ArtifactsDirectory / "coverage";

    public static int Main() => Execute<Build>(x => x.Publish);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    IEnumerable<Project> UnitTestProjects => Solution.GetAllProjects("*.Tests.Unit");
    IEnumerable<Project> IntegrationTestProjects => Solution.GetAllProjects("*.Tests.Integration");

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
                DotNet($"nuget update source Artifactory " +
                       $"--source {ArtifactoryNugetSourceUrl}/ " +
                       $"--username {ArtifactoryUsername} " +
                       $"--password {ArtifactoryPassword} " +
                       "--store-password-in-clear-text");
            }
        });

    Target Clean => d => d
        .Executes(() =>
        {
            // TODO: setup serilog properly
            // Serilog.Log.Information($"Cleaning bin artifact");
            BinaryArtifactsDirectory.CreateOrCleanDirectory();
            TestResultsDirectory.CreateOrCleanDirectory();
            IntegrationTestsResultDirectory.CreateOrCleanDirectory();
            CoverageReportsDirectory.CreateOrCleanDirectory();

            DotNetClean(settings => settings.SetProject(Solution)
                .SetConfiguration(Configuration)
                .SetVerbosity(DotNetVerbosity.quiet));

            // Solution.Directory.GlobDirectories("*/bin", "*/obj").DeleteDirectories();
            // Serilog.Log.Information($"Cleaning bin and obj directories");
        });

    Target Restore => d => d
        .DependsOn(ConfigureNuget)
        .DependsOn(Clean)
        .Executes(() =>
        {
            // TODO: SetVerbosity should be a parameter
            DotNetRestore(configurator => configurator
                .SetVerbosity(DotNetVerbosity.quiet));
        });

    Target Compile => d => d
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(configurator => configurator
                .SetConfiguration(Configuration)
                .SetNoRestore(true)
                .SetVerbosity(DotNetVerbosity.quiet)
                .SetRepositoryUrl(GitRepository.HttpsUrl)
            );
        });

    Target UnitTests => d => d
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(settings => settings.SetConfiguration(Configuration)
                .SetVerbosity(DotNetVerbosity.quiet)
                .SetResultsDirectory(TestResultsDirectory)
                .EnableCollectCoverage()
                .SetCoverletOutputFormat(CoverletOutputFormat.opencover)
                .SetLoggers("trx", "liquid.md")
                .CombineWith(UnitTestProjects, (settings, project) => settings
                    .SetProjectFile(project)
                    .SetCoverletOutput(CoverageReportsDirectory / $"{project.Name}.xml")));
        });

    Target IntegrationTests => d => d
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(settings => settings.SetConfiguration(Configuration)
                .SetVerbosity(DotNetVerbosity.quiet)
                .SetResultsDirectory(IntegrationTestsResultDirectory)
                .EnableCollectCoverage()
                .SetCoverletOutputFormat(CoverletOutputFormat.opencover)
                .SetLoggers("trx", "liquid.md")
                .CombineWith(IntegrationTestProjects, (settings, project) => settings
                    .SetProjectFile(project)
                    .SetCoverletOutput(CoverageReportsDirectory / $"{project.Name}.xml")));
        });

    Target Publish => d => d
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetPublish(settings => settings.SetProject(Project)
                .SetConfiguration(Configuration)
                // .SetRuntime("linux-x64")  // Removing this for now as it's causing issues with the publish: error : Manifest file not found
                .SetNoBuild(true)
                .SetOutput(BinaryArtifactsDirectory)
                // .EnablePublishSingleFile()  // Removing this for now as it's causing issues with the publish: error : Manifest file not found
                .SetVerbosity(DotNetVerbosity.quiet)
                .SetRepositoryUrl(GitRepository.HttpsUrl)
                .SetAssemblyVersion(AssemblySemVer)
                .SetFileVersion(AssemblySemFileVer)
                .SetInformationalVersion(InformationalVersion));
        });
}
