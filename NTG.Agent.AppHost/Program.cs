using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var saPassword         = builder.AddParameter("sql-sa-password", "Admin123_Strong!", secret: true);
var githubToken        = builder.AddParameter("github-token",            secret: true);
var lightragApiKey     = builder.AddParameter("lightrag-api-key",        secret: true);
var graphitiApiKey     = builder.AddParameter("graphiti-api-key",        secret: true);
var neo4jPassword      = builder.AddParameter("neo4j-password",          secret: true);
var googleApiKey       = builder.AddParameter("google-api-key",          secret: true);
var googleSearchId     = builder.AddParameter("google-search-engine-id", secret: true);

var sql = builder.AddSqlServer("sqlserver", password: saPassword)
                 .WithImageTag("2022-latest")
                 .WithEndpoint("tcp", endpoint =>
                 {
                     endpoint.Port = 1433;
                     endpoint.TargetPort = 1433;
                 })
                 .WithDataVolume("ntg-agent-local-dev-sqlserver-data");

if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
    sql.WithContainerRuntimeArgs("--platform", "linux/amd64");


var db  = sql.AddDatabase("NTGAgent");

var elasticsearch = builder.AddElasticsearch("elasticsearch")
                           .WithImageTag("8.15.0")
                           .WithEndpoint("http", endpoint =>
                           {
                               endpoint.Port = 9200;
                               endpoint.TargetPort = 9200;
                           })
                           .WithDataVolume("ntg-agent-local-dev-elasticsearch-data");

builder.Eventing.Subscribe<ResourceReadyEvent>(elasticsearch.Resource, async (evt, ct) =>
{
    var password = await elasticsearch.Resource.PasswordParameter.GetValueAsync(ct);
    var endpoint = elasticsearch.GetEndpoint("http").Url;

    using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic",
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"elastic:{password}")));

    for (var attempt = 0; attempt < 90; attempt++)
    {
        try
        {
            var response = await http.PostAsJsonAsync(
                "/_security/user/kibana_system/_password",
                new { password },
                ct);
            if (response.IsSuccessStatusCode) return;
        }
        catch (HttpRequestException) { }
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
    }

    throw new InvalidOperationException(
        "Failed to set the kibana_system password on Elasticsearch after 180s.");
});

var kibana = builder.AddContainer("kibana", "docker.elastic.co/kibana/kibana", "8.15.0")
    .WithHttpEndpoint(port: 5601, targetPort: 5601, name: "http")
    .WithEnvironment("ELASTICSEARCH_HOSTS",     "http://elasticsearch:9200")
    .WithEnvironment("ELASTICSEARCH_USERNAME",  "kibana_system")
    .WithEnvironment("ELASTICSEARCH_PASSWORD",  elasticsearch.Resource.PasswordParameter)
    .WaitFor(elasticsearch);

var lightragServer = builder.AddContainer("lightrag-server", "ghcr.io/hkuds/lightrag", "v1.4.16")
    .WithHttpEndpoint(port: 9621, targetPort: 9621, name: "http")
    .WithEnvironment("LIGHTRAG_API_KEY",            lightragApiKey)
    .WithEnvironment("LIGHTRAG_VECTOR_STORAGE",     "ElasticsearchVectorDBStorage")
    .WithEnvironment("LIGHTRAG_GRAPH_STORAGE",      "NetworkXStorage")
    .WithEnvironment("ELASTICSEARCH_HOSTS",         "http://elasticsearch:9200")
    .WithEnvironment("ELASTICSEARCH_USERNAME",      "elastic")
    .WithEnvironment("ELASTICSEARCH_PASSWORD",      elasticsearch.Resource.PasswordParameter)
    .WithEnvironment("LLM_BINDING",                 "openai")
    .WithEnvironment("LLM_BINDING_HOST",            "https://models.github.ai/inference")
    .WithEnvironment("LLM_MODEL",                   "openai/gpt-4.1-mini")
    .WithEnvironment("LLM_BINDING_API_KEY",         githubToken)
    .WithEnvironment("EMBEDDING_BINDING",           "openai")
    .WithEnvironment("EMBEDDING_BINDING_HOST",      "https://models.github.ai/inference")
    .WithEnvironment("EMBEDDING_MODEL",             "openai/text-embedding-3-small")
    .WithEnvironment("EMBEDDING_DIM",               "1536")
    .WithEnvironment("EMBEDDING_BINDING_API_KEY",   githubToken)
    .WithVolume("ntg-agent-local-dev-lightrag-data", "/app/data/rag_storage")
    .WaitFor(elasticsearch);

var neo4j = builder.AddContainer("neo4j", "neo4j", "5")
    .WithHttpEndpoint(port: 7474, targetPort: 7474, name: "http")
    .WithEndpoint(port: 7687, targetPort: 7687, scheme: "tcp", name: "bolt")
    .WithEnvironment(async ctx =>
    {
        var password = await neo4jPassword.Resource.GetValueAsync(ctx.CancellationToken);
        ctx.EnvironmentVariables["NEO4J_AUTH"] = $"neo4j/{password}";
    })
    .WithVolume("ntg-agent-local-dev-neo4j-data", "/data");

var graphitiServer = builder.AddContainer("graphiti-server", "zepai/graphiti", "0.22.0")
    .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")
    .WithEnvironment("NEO4J_URI",                   "bolt://neo4j:7687")
    .WithEnvironment("NEO4J_USER",                  "neo4j")
    .WithEnvironment(async ctx =>
    {
        var password = await neo4jPassword.Resource.GetValueAsync(ctx.CancellationToken);
        ctx.EnvironmentVariables["NEO4J_PASSWORD"] = password ?? string.Empty;
    })
    .WithEnvironment("OPENAI_API_KEY",              githubToken)
    .WithEnvironment("OPENAI_BASE_URL",             "https://models.github.ai/inference")
    .WithEnvironment("MODEL_NAME",                  "openai/gpt-4.1-mini")
    .WithEnvironment("EMBEDDER_MODEL_NAME",         "openai/text-embedding-3-small")
    .WithEnvironment("GRAPHITI_API_KEY",            graphitiApiKey)
    .WaitFor(neo4j);

if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
{
    lightragServer.WithContainerRuntimeArgs("--platform", "linux/amd64");
    graphitiServer.WithContainerRuntimeArgs("--platform", "linux/amd64");
}

var migrateAdmin = builder.AddExecutable(
        "db-migrate-admin",
        "dotnet",
        workingDirectory: "..",
        "ef", "database", "update",
        "--project",         "NTG.Agent.Admin/NTG.Agent.Admin/NTG.Agent.Admin.csproj",
        "--startup-project", "NTG.Agent.Admin/NTG.Agent.Admin/NTG.Agent.Admin.csproj")
    .WithEnvironment("ConnectionStrings__DefaultConnection", db)
    .WaitFor(db);

var migrateOrchestrator = builder.AddExecutable(
        "db-migrate-orchestrator",
        "dotnet",
        workingDirectory: "..",
        "ef", "database", "update",
        "--project",         "NTG.Agent.Orchestrator/NTG.Agent.Orchestrator.csproj",
        "--startup-project", "NTG.Agent.Orchestrator/NTG.Agent.Orchestrator.csproj")
    .WithEnvironment("ConnectionStrings__DefaultConnection", db)
    .WaitForCompletion(migrateAdmin);

var mcpServer = builder.AddProject<Projects.NTG_Agent_MCP_Server>("ntg-agent-mcp-server")
    .WithEnvironment("Google__ApiKey",         googleApiKey)
    .WithEnvironment("Google__SearchEngineId", googleSearchId);

var knowledge = builder.AddProject<Projects.NTG_Agent_Knowledge>("ntg-agent-knowledge")
    .WaitFor(db)
    .WaitFor(lightragServer)
    .WithReference(lightragServer.GetEndpoint("http"))
    .WithEnvironment("ConnectionStrings__DefaultConnection", db)
    .WithEnvironment("LightRag__ApiKey",                     lightragApiKey);

var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator")
    .WithExternalHttpEndpoints()
    .WithReference(mcpServer)
    .WithReference(knowledge)
    .WithReference(graphitiServer.GetEndpoint("http"))
    .WaitFor(graphitiServer)
    .WaitForCompletion(migrateOrchestrator)
    .WithEnvironment("ConnectionStrings__DefaultConnection", db)
    .WithEnvironment("LightRag__ApiKey",                     lightragApiKey)
    .WithEnvironment("Graphiti__ApiKey",                     graphitiApiKey)
    .WithEnvironment("GitHub__Models__GitHubToken",          githubToken);

builder.AddProject<Projects.NTG_Agent_WebClient>("ntg-agent-webclient")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator)
    .WaitForCompletion(migrateOrchestrator)
    .WithEnvironment("ConnectionStrings__DefaultConnection", db);

builder.AddProject<Projects.NTG_Agent_Admin>("ntg-agent-admin")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator)
    .WaitForCompletion(migrateOrchestrator)
    .WithEnvironment("ConnectionStrings__DefaultConnection", db);

builder.Build().Run();
