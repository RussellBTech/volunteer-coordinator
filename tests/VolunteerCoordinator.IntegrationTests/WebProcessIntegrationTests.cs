using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class WebProcessIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private string _publishDirectory = string.Empty;

    public WebProcessIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _publishDirectory = Path.Combine(
            Path.GetTempPath(),
            $"VolunteerCoordinator.Web.ProcessTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_publishDirectory);

        try
        {
            await PublishWebAsync();
        }
        catch
        {
            CleanupPublishDirectory();
            throw;
        }
    }

    public Task DisposeAsync()
    {
        CleanupPublishDirectory();
        return Task.CompletedTask;
    }

    private string PublishedWebDll => Path.Combine(_publishDirectory, "VolunteerCoordinator.Web.dll");

    private async Task PublishWebAsync()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "VolunteerCoordinator.Web",
            "VolunteerCoordinator.Web.csproj");
        var processStartInfo = new ProcessStartInfo
        {
            FileName = ResolveDotnetHost(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repositoryRoot
        };
        processStartInfo.ArgumentList.Add("publish");
        processStartInfo.ArgumentList.Add(projectPath);
        processStartInfo.ArgumentList.Add("--configuration");
        processStartInfo.ArgumentList.Add("Release");
        processStartInfo.ArgumentList.Add("--no-restore");
        processStartInfo.ArgumentList.Add("--output");
        processStartInfo.ArgumentList.Add(_publishDirectory);
        processStartInfo.ArgumentList.Add("/p:UseAppHost=false");

        using var process = new Process { StartInfo = processStartInfo };
        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw new Xunit.Sdk.XunitException("Web publish did not exit within 2 minutes.");
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"Web publish failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{error}");
        Assert.True(File.Exists(PublishedWebDll));
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VolunteerCoordinator.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new Xunit.Sdk.XunitException("Could not locate the repository root.");
    }

    private void CleanupPublishDirectory()
    {
        if (Directory.Exists(_publishDirectory))
        {
            Directory.Delete(_publishDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MigrateOnlyAppliesSchemaExitsAndDoesNotListen()
    {
        var connectionString = await _fixture.CreateEmptyDatabaseAsync();
        var port = GetUnusedPort();
        try
        {
            var result = await RunToExitAsync(connectionString, port, "--migrate-only");

            Assert.Equal(0, result.ExitCode);
            await using var context = _fixture.CreateContext(connectionString);
            Assert.True(await context.Database.CanConnectAsync());
            Assert.NotEmpty(context.Database.GetAppliedMigrations());
            Assert.False(await IsPortOpenAsync(port));
        }
        finally
        {
            await _fixture.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task MigrateOnlyFailureIsNonZeroAndDoesNotListen()
    {
        var port = GetUnusedPort();
        var result = await RunToExitAsync(
            "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing",
            port,
            "--migrate-only");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(await IsPortOpenAsync(port));
    }

    [Fact]
    public async Task OrdinaryStartupDoesNotCreateMigrationHistory()
    {
        var connectionString = await _fixture.CreateEmptyDatabaseAsync();
        var port = GetUnusedPort();
        using var process = StartProcess(connectionString, port);
        try
        {
            await WaitForLivenessAsync(port, process);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """SELECT to_regclass('public."__EFMigrationsHistory"')::text""";
            var migrationHistory = await command.ExecuteScalarAsync();
            Assert.True(
                migrationHistory is null ||
                migrationHistory is DBNull ||
                string.IsNullOrEmpty(migrationHistory.ToString()));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            await _fixture.DropDatabaseAsync(connectionString);
        }
    }

    private async Task<ProcessResult> RunToExitAsync(
        string connectionString,
        int port,
        params string[] arguments)
    {
        using var process = StartProcess(connectionString, port, arguments);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw new Xunit.Sdk.XunitException("Web process did not exit within 30 seconds.");
        }

        await Task.WhenAll(standardOutput, standardError);
        return new ProcessResult(process.ExitCode);
    }

    private static string ResolveDotnetHost()
    {
        var runtimeRoot = Path.GetFullPath(
            Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var runtimeHost = Path.Combine(
            runtimeRoot,
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        if (File.Exists(runtimeHost))
        {
            return runtimeHost;
        }

        return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Environment.ProcessPath
            ?? "dotnet";
    }

    private Process StartProcess(
        string connectionString,
        int port,
        params string[] arguments)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = ResolveDotnetHost(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _publishDirectory
        };
        processStartInfo.ArgumentList.Add(PublishedWebDll);
        foreach (var argument in arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        processStartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        processStartInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        processStartInfo.Environment["ConnectionStrings__Postgres"] = connectionString;
        processStartInfo.Environment.Remove("ASPNETCORE_FORWARDEDHEADERS_ENABLED");

        var process = new Process { StartInfo = processStartInfo };
        Assert.True(process.Start());
        return process;
    }

    private static async Task WaitForLivenessAsync(int port, Process process)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (process.HasExited)
            {
                throw new Xunit.Sdk.XunitException("Web process exited before liveness was available.");
            }

            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}/health");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("Web liveness did not become available.");
    }

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<bool> IsPortOpenAsync(int port)
    {
        using var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private sealed record ProcessResult(int ExitCode);
}
