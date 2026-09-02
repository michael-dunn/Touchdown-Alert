extern alias SimulatorAssembly;

using Microsoft.AspNetCore.Mvc.Testing;
using SimulatorProgram = SimulatorAssembly::Program;

namespace TouchdownAlert.IntegrationTests;

/// <summary>Hosts the Simulator in-process (TestServer, no real socket) for the whole test class.</summary>
public sealed class SimulatorHostFixture : WebApplicationFactory<SimulatorProgram>, IAsyncLifetime
{
    public HttpClient Client { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Client = CreateClient();
        return Task.CompletedTask;
    }

    public new Task DisposeAsync() => Task.CompletedTask;
}
