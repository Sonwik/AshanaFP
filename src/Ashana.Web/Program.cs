using Ashana.Web;

// The whole set-up lives in AshanaApp, so integration tests can start exactly the same app in memory.
var app = await AshanaApp.BuildAsync(new WebApplicationOptions { Args = args });
await app.RunAsync();

/// <summary>Entry point. Public so test projects can reference the web assembly.</summary>
public partial class Program;
