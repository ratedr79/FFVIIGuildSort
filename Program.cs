using FFVIIEverCrisisAnalyzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddScoped<PowerLevelCalculator>();
builder.Services.AddScoped<CsvProcessor>();
builder.Services.AddSingleton<NameCorrectionService>();
builder.Services.Configure<WeaponCatalogOptions>(builder.Configuration.GetSection("FeatureFlags"));
builder.Services.AddSingleton<WeaponSearchDataService>();
builder.Services.AddSingleton<WeaponCatalog>();
builder.Services.AddSingleton<SummonCatalog>();
builder.Services.AddSingleton<EnemyAbilityCatalog>();
builder.Services.AddSingleton<EnemyCatalog>();
builder.Services.AddSingleton<MemoriaCatalog>();
builder.Services.AddSingleton<TeamTemplateCatalog>();
builder.Services.AddScoped<Gb20Ingestion>();
builder.Services.AddScoped<TeamOptimizer>();
builder.Services.AddScoped<Gb20Analyzer>();
builder.Services.AddScoped<GuildAssigner>();
builder.Services.AddSingleton<StagePointEstimator>();
builder.Services.AddSingleton<IDispatcherRunner, DispatcherRunnerService>();
builder.Services.AddScoped<ShouldIAttackService>();
builder.Services.AddSingleton<SupportTeamBuilderService>();
builder.Services.AddSingleton<SupportTeamPresetCatalog>();
builder.Services.AddScoped<DamageCalcService>();
builder.Services.Configure<SharedAccessOptions>(builder.Configuration.GetSection(SharedAccessOptions.SectionName));
builder.Services.AddSingleton<SharedAccessGate>();

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

app.Use(async (context, next) =>
{
    var gate = context.RequestServices.GetRequiredService<SharedAccessGate>();

    if (!gate.RequiresUnlock(context.Request.Path))
    {
        await next();
        return;
    }

    var token = context.Request.Cookies[gate.CookieName];
    if (gate.IsValidToken(token))
    {
        await next();
        return;
    }

    var returnUrl = context.Request.Path + context.Request.QueryString;
    var unlockUrl = $"/Unlock?returnUrl={Uri.EscapeDataString(returnUrl)}";
    context.Response.Redirect(unlockUrl);
});

app.UseAuthorization();

app.MapRazorPages();

app.Run();
