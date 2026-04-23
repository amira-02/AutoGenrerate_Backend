using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using AutoGenerate.Auth;
using AutoGenerate.Shared.Data;

var builder = WebApplication.CreateBuilder(args);

var config = builder.Configuration;

// ================= DATABASE =================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(config.GetConnectionString("DefaultConnection")));

// ================= SERVICES =================
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<EmailService>();
//builder.Services.AddScoped<GroqService>();
//builder.Services.AddScoped<ImageService>();

// HttpClient (IMPORTANT: propre factory usage)
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IConfiguration>(config);

// ================= AUTH JWT =================
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = config["Jwt:Issuer"],
            ValidAudience = config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Key"]!)
            ),

            ClockSkew = TimeSpan.Zero // 🔥 important (évite tokens "valides en plus")
        };
    });

builder.Services.AddAuthorization();

// ================= CORS =================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:3000"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ================= CONTROLLERS =================
builder.Services.AddControllers();

// ================= SWAGGER =================
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AutoPost API",
        Version = "v1"
    });

    // 🔐 JWT in Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // ⚠️ garde seulement si tu as vraiment ce filter
    c.OperationFilter<SwaggerFileOperationFilter>();
});

var app = builder.Build();

app.Urls.Add("http://0.0.0.0:5220");
app.Urls.Add("https://0.0.0.0:7079");



// ================= MIDDLEWARE =================
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AutoPost API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

// ⚠️ ORDER IMPORTANT
app.UseCors("AllowFrontend");

app.UseStaticFiles();   // ← doit être AVANT UseRouting
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();