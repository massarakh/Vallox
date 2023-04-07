using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Vallox.ValloxService;

namespace Vallox
{
    public class Program
    {
        private static IConfigurationRoot? config;
        public static void Main(string[] args)
        {
            //var config = new ConfigurationBuilder()
            //    .SetBasePath(System.IO.Directory.GetCurrentDirectory()) //From NuGet Package Microsoft.Extensions.Configuration.Json
            //    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            //    .Build();
            IHostBuilder builder = CreateHostBuilder(args);
            IHost host = builder.Build();
            host.Run();

            config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                //.ConfigureAppConfiguration(app =>
                //{
                //    app.AddJsonFile("appsettings.json");
                //})
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<ListenerService>();
                    services.AddHostedService<ProcessService>();
                    services.AddLogging(loggingBuilder =>
                    {
                        // configure Logging with NLog
                        loggingBuilder.ClearProviders();
                        loggingBuilder.SetMinimumLevel(LogLevel.Trace);
                        loggingBuilder.AddNLog(config);
                    });
                });
    }
}