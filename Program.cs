using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Debugging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MarginCoin
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Capture Serilog internal sink errors to stderr for diagnostics
            SelfLog.Enable(message => Console.Error.WriteLine($"[SerilogSelfLog] {message}"));

            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var seqUrl = configuration["Seq:Url"] ?? Environment.GetEnvironmentVariable("SEQ_URL");

            var loggerConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .WriteTo.Console()
                .WriteTo.File("logs/.txt", shared: true, rollingInterval: RollingInterval.Day);

            if (!string.IsNullOrWhiteSpace(seqUrl))
            {
                try
                {
                    loggerConfig.WriteTo.Seq(seqUrl);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to configure Seq logging: {ex.Message}");
                }
            }

            Log.Logger = loggerConfig.CreateLogger();

            // Log any unhandled application-level exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                Log.Fatal(eventArgs.ExceptionObject as Exception, "Unhandled exception");
            };

            // Log any unobserved task exceptions before process exits
            TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
            {
                Log.Fatal(eventArgs.Exception, "Unobserved task exception");
                eventArgs.SetObserved();
            };

            try
            {
                Log.Warning("MarginCoin, started!");
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>


            Host.CreateDefaultBuilder(args)
                 .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
