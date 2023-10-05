using HotelListing.API.Core.Configurations;
using HotelListing.API.Core.Contracts;
using HotelListing.API.Data;
using HotelListing.API.Core.Middleware;
using HotelListing.API.Core.Repository;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;
using HotelListing.API.SwaggerFilters;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration
    .GetConnectionString("HotelListingDbConnectionString");
builder.Services.AddDbContext<HotelListingDbContext>(options => { options.UseSqlServer(connectionString); });

builder.Services.AddIdentityCore<ApiUser>()
    .AddRoles<IdentityRole>()
    .AddTokenProvider<DataProtectorTokenProvider<ApiUser>>("HotelListingApi") //Token refresh
    .AddEntityFrameworkStores<HotelListingDbContext>()
    .AddDefaultTokenProviders(); //Token refresh

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Hotel Listing API",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = @"JWT Autherization header using the Bearer scheme.
                      Enter 'Bearer' [space] and then your token in the text input below.
                      Example: 'Bearer 1234abcdef'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
    });

    options.OperationFilter<AuthorizeCheckOperationFilter>();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        b => b.AllowAnyHeader()
            .AllowAnyOrigin()
            .AllowAnyMethod());
});

builder.Services.AddApiVersioning(options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.DefaultApiVersion = new Microsoft.AspNetCore.Mvc.ApiVersion(1, 0);
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("X-Version"),
        new MediaTypeApiVersionReader("ver")
    );
});

builder.Services.AddVersionedApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console().ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddAutoMapper(typeof(MapperConfig));

builder.Services.AddScoped(typeof(IGenericRepository<>),
    typeof(GenericRepository<>));
builder.Services.AddScoped<ICountriesRepository, CountriesRepository>();
builder.Services.AddScoped<IHotelsRepository, HotelsRepository>();
builder.Services.AddScoped<IAuthManager, AuthManager>();

// JWT JSON Web Token
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme; // "Bearer"
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
        ValidAudience = builder.Configuration["JwtSettings:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:Key"])
        )
    };
});

builder.Services.AddResponseCaching(options =>
{
    // in bytes the largest cacheable piece of data allowed to cache
    options.MaximumBodySize = 1024;
    // if someone requests a path with a different capitalization it will
    // store the new request as well
    options.UseCaseSensitivePaths = true;
});

builder.Services.AddHealthChecks()
    .AddCheck<CustomHealthCheck>(
        "Custom Health Check",
        failureStatus: HealthStatus.Degraded,
        // Tags allow us to filter between types of health checks
        tags: new[] { "custom" }
    ).AddSqlServer(
        builder.Configuration.GetConnectionString("HotelListingDbConnectionString"),
        tags: new[] { "database" }
    )
    // Can add more with different types than 'HotelListingDbContext' 
    // to monitor multiple databases
    .AddDbContextCheck<HotelListingDbContext>(tags: new[] { "database" });

builder.Services.AddControllers().AddOData(options => { options.Select().Filter().OrderBy(); });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
}

app.UseSwagger();
app.UseSwaggerUI();

// Can change the endpoint to anything. I chose '/healthcheck'
app.MapHealthChecks("/healthcheck", new HealthCheckOptions
{
    Predicate = healthcheck => healthcheck.Tags.Contains("custom"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
    ResponseWriter = WriteResponse
});

app.MapHealthChecks("/databasehealthcheck", new HealthCheckOptions
{
    Predicate = healthcheck => healthcheck.Tags.Contains("database"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
    ResponseWriter = WriteResponse
});
//.RequireAuthorization(); // returns 401 if unauthorized

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
    ResponseWriter = WriteResponse
});

app.MapHealthChecks("/health");

app.UseMiddleware<ExceptionMiddleware>();

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseResponseCaching();

app.Use(async (context, next) =>
{
    context.Response.GetTypedHeaders().CacheControl =
        new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(10)
        };
    context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.Vary] =
        new string[] { "Accept-Encoding" };

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Landing page for useful links
const string landingPageHtml = @"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Tim's API</title>
    <style>
        body {
            font-family: 'Arial', sans-serif;
            margin: 40px;
            background-color: #f5f5f5;
        }

        h1 {
            color: #2c3e50;
            border-bottom: 2px solid #3498db;
            padding-bottom: 10px;
            margin-bottom: 20px;
        }

        h2 {
            color: #7f8c8d;
        }

        h3 {
            color: #2c3e50;
            margin-top: 20px;
            margin-bottom: 10px;
        }

        ul {
            background-color: #fff;
            border: 1px solid #ecf0f1;
            padding: 15px 25px;
            border-radius: 5px;
            list-style-type: none;
        }

        li {
            margin-bottom: 10px;
        }

        a {
            text-decoration: none;
            color: #3498db;
        }

        a:hover {
            text-decoration: underline;
        }
    </style>
</head>
<body>
    <h1>Welcome to Tim's API</h1>
    <h2>Helpful links:</h2>

    <h3>Swagger UI</h3>
    <ul>
        <li><a href='/swagger'>API Documentation & Testing</a></li>
    </ul>

    <h3>Health Checks</h3>
    <ul>
        <li><a href='/healthz'>All</a></li>
        <li><a href='/healthcheck'>App</a></li>
        <li><a href='/databasehealthcheck'>Database</a></li>
    </ul>
</body>
</html>";

// Adding RouteEndpoint to IEndpointRouteBuilder for landing page
app.MapGet("/", context =>
{
    context.Response.ContentType = "text/html";
    return context.Response.WriteAsync(landingPageHtml);
});

app.Run();

static Task WriteResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    var options = new JsonWriterOptions { Indented = true };

    using var memoryStream = new MemoryStream();
    using (var jsonWriter = new Utf8JsonWriter(memoryStream, options))
    {
        jsonWriter.WriteStartObject();
        jsonWriter.WriteString("status", report.Status.ToString());
        jsonWriter.WriteStartObject("results");

        foreach (var healthReportEntry in report.Entries)
        {
            jsonWriter.WriteStartObject(healthReportEntry.Key);
            jsonWriter.WriteString("status",
                healthReportEntry.Value.Status.ToString());
            jsonWriter.WriteString("description",
                healthReportEntry.Value.Description);
            jsonWriter.WriteStartObject("data");

            foreach (var item in healthReportEntry.Value.Data)
            {
                jsonWriter.WritePropertyName(item.Key);

                JsonSerializer.Serialize(
                    jsonWriter, item.Value,
                    item.Value?.GetType() ?? typeof(object));
            }

            jsonWriter.WriteEndObject();
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndObject();
        jsonWriter.WriteEndObject();
    }

    return context.Response.WriteAsync(
        Encoding.UTF8.GetString(memoryStream.ToArray())
    );
}

class CustomHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var isHealthy = true;

        // custom checks. logic... etc.etc.

        if (isHealthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "All systems are looking good"
            ));
        }

        return Task.FromResult(new HealthCheckResult(
                context.Registration.FailureStatus, "System Unhealthy"
            )
        );
    }
}