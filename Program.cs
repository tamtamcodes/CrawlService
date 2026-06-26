using SocialCrawler.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Social Crawler API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddSingleton<CrawlStateService>();
builder.Services.AddScoped<FacebookCrawlerService>();
builder.Services.AddScoped<TikTokCrawlerService>();
builder.Services.AddScoped<SessionValidationService>();

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Social Crawler API v1"));
app.MapControllers();

app.Run("http://0.0.0.0:5000");
