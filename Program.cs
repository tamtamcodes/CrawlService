using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using SocialCrawler.Services;

const string EnterpriseOpenApiDescription = """
Social Crawler Enterprise API.

Production documentation:
- Scalar API Reference: /scalar
- OpenAPI JSON: /openapi/v1.json
- Swagger JSON compatibility: /swagger/v1/swagger.json
- Swagger UI compatibility: /swagger
- Enterprise integration guide: /docs/enterprise

Core endpoints:
- POST /crawl/facebook: Facebook crawl.
- POST /crawl/tiktok: TikTok crawl.
- POST /crawl: TikTok compatibility endpoint.
- POST /validate_facebook_session: Facebook cookie validation.
- POST /validate_session: TikTok cookie validation.
- GET /status: global crawler lock/status.
- POST /cancel: cancel current crawl.

Response modes:
- Default: JSON response wrapper with status, platform, targets, count, items, events, errors, aborted, started_at, finished_at, elapsed_ms.
- Legacy stream: set response_mode to stream or sse for text/event-stream.

Field projection:
- Use fields or response_fields to select returned paths.
- Omit fields to return everything.
- Use items.<path> for item data, events.<path> for crawl events, errors.<path> for errors.
- Heavy top-level sections that are not selected are not accumulated by the controller.

Heavy opt-in behavior:
- Facebook transcripts are not downloaded by default. Set include_transcripts=true or request items.transcript.
- TikTok comments are not crawled by default. Set include_comments=true or request items.comments.
- Facebook caption track metadata is lightweight and can be requested without downloading transcript SRT files.

Recommended enterprise pattern:
1. Validate session cookies.
2. Run light crawl with selected fields.
3. Enrich only required records with comments/transcripts.
4. Use /status and /cancel for operational control.
5. Use /scalar and /openapi/v1.json for client integration, SDK generation, Scalar Agent, and Scalar MCP metadata.
""";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Social Crawler Enterprise API",
        Version = "v1",
        Description = EnterpriseOpenApiDescription,
        Contact = new OpenApiContact
        {
            Name = "Enterprise Crawl Tools",
            Url = new Uri("https://scalar.com/")
        },
        License = new OpenApiLicense
        {
            Name = "Internal Enterprise Use"
        }
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddSingleton<CrawlStateService>();
builder.Services.AddScoped<SocialCrawler.Services.Human.IHumanBehaviorProvider, SocialCrawler.Services.Human.HumanBehaviorProvider>();
builder.Services.AddScoped<FacebookCrawlerService>();
builder.Services.AddScoped<TikTokCrawlerService>();
builder.Services.AddScoped<SessionValidationService>();

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.MapSwagger("/openapi/{documentName}.json");
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Social Crawler API v1"));

var publicBaseUrl = builder.Configuration["PublicBaseUrl"]
    ?? Environment.GetEnvironmentVariable("PUBLIC_BASE_URL")
    ?? "http://localhost:5000";

var scalarMcpServerUrl = builder.Configuration["Scalar:McpServerUrl"]
    ?? Environment.GetEnvironmentVariable("SCALAR_MCP_SERVER_URL")
    ?? "/mcp";

var scalarAgentKey = builder.Configuration["Scalar:AgentKey"]
    ?? Environment.GetEnvironmentVariable("SCALAR_AGENT_KEY");

app.MapScalarApiReference("/scalar", options =>
{
    options.WithTitle("Social Crawler Enterprise API")
        .WithOpenApiRoutePattern("/openapi/{documentName}.json")
        .AddDocument("v1", "Social Crawler v1")
        .AddServer(publicBaseUrl, "Configured server")
        .WithTheme(ScalarTheme.DeepSpace)
        .ForceDarkMode()
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl)
        .WithMcpServer("Social Crawler MCP", scalarMcpServerUrl)
        .ShowOperationId()
        .ExpandAllTags();

    if (!string.IsNullOrWhiteSpace(scalarAgentKey))
        options.WithAgentKey(scalarAgentKey);
});

app.MapGet("/docs/enterprise", async () =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "docs", "ENTERPRISE_API_INTEGRATION.md");
    if (!File.Exists(path))
        path = Path.Combine(Directory.GetCurrentDirectory(), "docs", "ENTERPRISE_API_INTEGRATION.md");

    var markdown = File.Exists(path)
        ? await File.ReadAllTextAsync(path)
        : EnterpriseOpenApiDescription;

    return Results.Text(markdown, "text/markdown; charset=utf-8");
})
.WithName("EnterpriseIntegrationGuide")
.WithTags("Documentation");

app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "5001";
app.Run($"http://0.0.0.0:{port}");

