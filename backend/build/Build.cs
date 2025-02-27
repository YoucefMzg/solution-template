
class Build : NukeBuild
{
    const string BuildContainerImage = "solution-template-build";

    [Parameter] readonly string ArtifactoryUsername;
    [Parameter] readonly string ArtifactoryPassword;
    [Parameter] readonly string ArtifactoryNugetSourceUrl;

    [Parameter] readonly string AssemblySemVer;
    [Parameter] readonly string AssemblySemFileVer;
    [Parameter] readonly string InformationalVersion;

    [GitRepository] [Required] readonly GitRepository GitRepository;
    [Solution] readonly Solution Solution;

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath BinaryArtifactsDirectory => ArtifactsDirectory / "bin";
    AbsolutePath TestResultsDirectory => ArtifactsDirectory / "test-results";
    AbsolutePath IntegrationTestsResultDirectory => ArtifactsDirectory / "integration-test-results";
    AbsolutePath CoverageReportsDirectory => ArtifactsDirectory / "coverage";
    AbsolutePath Dockerfile => RootDirectory / "backend/build.dockerfile";
    AbsolutePath DockerBuildContextPath => RootDirectory / "backend";

    public static int Main() => Execute<Build>(x => x.Compile);

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
        // .OnlyWhenStatic(() => !IsLocalBuild)
        .Executes(() =>
        {
            // TODO: setup serilog properly
            // Serilog.Log.Information($"Cleaning bin artifact");
            BinaryArtifactsDirectory.CreateOrCleanDirectory();
            TestResultsDirectory.CreateOrCleanDirectory();
            IntegrationTestsResultDirectory.CreateOrCleanDirectory();
            CoverageReportsDirectory.CreateOrCleanDirectory();

            DotNetClean(settings => settings.SetProject(Solution)
                .SetVerbosity(DotNetVerbosity.quiet));
            // Serilog.Log.Information($"Cleaning bin and obj directories");
            // Solution.Directory.GlobDirectories("*/bin", "*/obj").DeleteDirectories();
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
                .SetNoRestore(SucceededTargets.Contains(Restore))
                .SetVerbosity(DotNetVerbosity.quiet)
                .SetRepositoryUrl(GitRepository.HttpsUrl)
            );
        });

    Target UnitTests => d => d
        .After(Compile)
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
        .After(Compile)
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
        .After(Compile)
        .Executes(() =>
        {
            DotNetPublish(settings => settings.SetProject(Solution)
                .SetConfiguration(Configuration)
                .SetNoBuild(SucceededTargets.Contains(Compile))
                .SetRuntime("linux-x64")
                .SetOutput(BinaryArtifactsDirectory)
                .SetVerbosity(DotNetVerbosity.quiet)
                .SetRepositoryUrl(GitRepository.HttpsUrl)
                .SetAssemblyVersion(AssemblySemVer)
                .SetFileVersion(AssemblySemFileVer)
                .SetInformationalVersion(InformationalVersion));
        });

    Target BuildContainer => d => d
        .Executes(() =>
        {
            Console.WriteLine($"@@@@DockerBuildContextPath: {DockerBuildContextPath}");
            DockerTasks.DockerBuild(settings => settings
                .SetFile("build.dockerfile")
                .SetTag(BuildContainerImage)
                .SetPath(DockerBuildContextPath));
        });
}
