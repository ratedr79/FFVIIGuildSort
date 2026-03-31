using FFVIIEverCrisisAnalyzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddScoped<PowerLevelCalculator>();
builder.Services.AddScoped<CsvProcessor>();
builder.Services.AddSingleton<NameCorrectionService>();
builder.Services.AddSingleton<WeaponSearchDataService>();
builder.Services.AddSingleton<WeaponCatalog>();
builder.Services.AddSingleton<SummonCatalog>();
builder.Services.AddSingleton<EnemyAbilityCatalog>();
builder.Services.AddSingleton<MemoriaCatalog>();
builder.Services.AddSingleton<TeamTemplateCatalog>();
builder.Services.AddScoped<Gb20Ingestion>();
builder.Services.AddScoped<TeamOptimizer>();
builder.Services.AddScoped<Gb20Analyzer>();
builder.Services.AddScoped<GuildAssigner>();
builder.Services.AddSingleton<StagePointEstimator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
