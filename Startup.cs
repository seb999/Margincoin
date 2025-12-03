using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.SpaServices.AngularCli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using MarginCoin.Model;
using System.Text.Json;
using System;
using MarginCoin.Misc;
using Serilog;
using MarginCoin.Service;
using MarginCoin.Configuration;
using System.Net.Http;

namespace MarginCoin
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Register configuration options
            services.Configure<BinanceConfiguration>(Configuration.GetSection("Binance"));
            services.Configure<CoinMarketCapConfiguration>(Configuration.GetSection("CoinMarketCap"));
            services.Configure<TradingConfiguration>(Configuration.GetSection("Trading"));

            // HttpClient for ML prediction service
            services.AddHttpClient();

            // Singleton - same instance during lifetime (state & long-running services)
            services.AddSingleton<ITradingState, TradingStateService>();
            services.AddSingleton<LSTMPredictionService>();
            services.AddSingleton<IMLService>(sp => sp.GetRequiredService<LSTMPredictionService>());
            services.AddSingleton<IWatchDog, WatchDog>();
            services.AddSingleton<IWebSocket, WebSocket>();

            // Scoped - new instance per request
            services.AddScoped<IBinanceService, BinanceService>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<ISymbolService, SymbolService>();
            services.AddScoped<ITradingSettingsService, TradingSettingsService>();

            services.AddSignalR();

            services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString;
            });

            services.AddControllersWithViews();
            // In production, the Angular files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "ClientApp/dist";
            });

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(Configuration.GetConnectionString("SqLite")));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseSerilogRequestLogging();

            app.UseWebSockets();
            var webSocketOptions = new WebSocketOptions() { KeepAliveInterval = TimeSpan.FromSeconds(180) };
            app.UseWebSockets(webSocketOptions);

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            if (!env.IsDevelopment())
            {
                app.UseSpaStaticFiles();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<SignalRHub>("/Signalr");
                endpoints.MapControllers();
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller}/{action=Index}/{id?}");
            });

            app.UseSpa(spa =>
            {
                // To learn more about options for serving an Angular SPA from ASP.NET Core,
                // see https://go.microsoft.com/fwlink/?linkid=864501

                spa.Options.SourcePath = "ClientApp";

                if (env.IsDevelopment() && IsSpaDevServerAvailable())
                {
                    spa.UseProxyToSpaDevelopmentServer("http://localhost:4201");
                }
            });

            using (var scope = app.ApplicationServices.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Database.EnsureCreated();
                var settingsService = scope.ServiceProvider.GetRequiredService<ITradingSettingsService>();
                settingsService.ApplyOverridesAsync().GetAwaiter().GetResult();
            }
        }

        private bool IsSpaDevServerAvailable()
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(1)
                };
                var response = client.GetAsync("http://localhost:4201", HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
