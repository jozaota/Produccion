
using DocumentosElectronicos;
using DocumentosElectronicos.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.File;      // habilita WriteTo.File(...)
using Serilog.Sinks.SystemConsole;   // habilita WriteTo.Console(...)
using Serilog.Extensions.Hosting;   // habilita UseSerilog() en el HostBuilder
using QuestPDF.Infrastructure;

// ── Carpeta de logs ──────────────────────────────────────────────────────────
// AppContext.BaseDirectory apunta a bin\Debug\net8.0-windows\ en debug
// y al directorio del ejecutable en producción.
// Se crea el directorio explícitamente porque Serilog no lo hace si no existe.
var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
var logsPath = Path.Combine(logsDir, "log-.txt");

Directory.CreateDirectory(logsDir); // no lanza excepción si ya existe

Console.WriteLine($"BaseDirectory: {AppContext.BaseDirectory}");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: logsPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        encoding: System.Text.Encoding.UTF8,
        flushToDiskInterval: TimeSpan.FromSeconds(1)) // fuerza escritura inmediata en debug
    .CreateLogger();

try
{
    Log.Information("Iniciando servicio DocumentosElectronicos...");
    Log.Information("Logs en: {LogsDir}", logsDir);

    QuestPDF.Settings.License = LicenseType.Community; // o LicenseType.Pro si tienes una licencia de pago

    IHost host = Host.CreateDefaultBuilder(args)
        .UseWindowsService(options =>
        {
            options.ServiceName = "DocumentosElectronicosService";
        })
        .UseSerilog()   // Reemplaza el logger de .NET con Serilog
        .ConfigureServices((context, services) =>
        {
            services.Configure<AppSettings>(context.Configuration.GetSection("AppSettings"));

            services.AddSingleton<PostgresService>();
            services.AddSingleton<HanaService>();
            services.AddSingleton<SapServiceLayerService>();
            services.AddSingleton<EmailService>();

            services.AddSingleton<MovimientoHanaService>();
            services.AddSingleton<MovimientoPdfService>();
            services.AddSingleton<MovimientoEmailService>();

            services.AddHostedService<Worker>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"FATAL: {ex}");
    Log.Fatal(ex, "El servicio terminó de forma inesperada.");
}
finally
{
    await Log.CloseAndFlushAsync();
}