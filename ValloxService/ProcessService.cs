using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using ValloxLogs;

namespace Vallox.ValloxService;

public class ProcessService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    public static BlockingCollection<List<byte[]>> collection = new(1);
    int KPageSize = 65536;

    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
    private int Timeout => Convert.ToInt32(_configuration.GetSection("Timeout").Value);

    public ProcessService(IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _logger = loggerFactory.CreateLogger("ProcessService");
        _lifetime = lifetime;
        _configuration = configuration;

        _lifetime.ApplicationStarted.Register(() => _logger.LogDebug(
            "In ProcessService - host application started at: {time}.",
            DateTimeOffset.Now));
        _lifetime.ApplicationStopping.Register(() => _logger.LogDebug(
            "In ProcessService - host application stopping at: {time}.",
            DateTimeOffset.Now));
        _lifetime.ApplicationStopped.Register(() => _logger.LogDebug(
            "In ProcessService - host application stopped at: {time}.",
            DateTimeOffset.Now));
    }


    #region ServiceMethods

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("ProcessService started at: {time} and will take 1 seconds to complete.",
            DateTimeOffset.Now);

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        await base.StartAsync(cancellationToken);
        _logger.LogInformation($"Timeout between requests - {Timeout}");
        if (CheckDB())
        {
            _logger.LogInformation($"Database - OK");
        }
        else
        {
            _logger.LogCritical($"Database is not accessible! Exit");
            Environment.Exit(1);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopWatch = Stopwatch.StartNew();
        _logger.LogDebug("ProcessService stopped at: {time}", DateTimeOffset.Now);
        await base.StopAsync(cancellationToken);
        _logger.LogDebug("ProcessService took {ms} ms to stop.", stopWatch.ElapsedMilliseconds);
    }

    #endregion ServiceMethods


    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(() => _logger.LogDebug(
            "In ProcessService - token was cancelled at: {time}.",
            DateTimeOffset.Now));

        try
        {
            while (!collection.IsCompleted)
            {
                List<byte[]> item = collection.Take();
                _logger.LogInformation($"Parsing data from packets");
                byte[] metaDataArr = item[0];
                byte[] dataArr = item[1];
                if (metaDataArr.Length == 0 || dataArr.Length == 0)
                {
                    throw new Exception("Empty data from Vallox");
                }
                var tuple = StructConverter.Unpack<Tuple<UInt16, UInt16, UInt16>>("HHH", metaDataArr);

                int pageCount = tuple.Item3;
                int expected_total_len = KPageSize * pageCount;
                if (dataArr.Length != expected_total_len)
                {
                    throw new Exception("Expected data length is not equal to received data length");
                }

                List<byte[]> datas = new List<byte[]>();
                //Нарезка из массива подмассивов размером в 8 байт
                for (int i = 0; i < dataArr.Length; i += KPageSize)
                {
                    byte[] sliced = dataArr[i..(i + KPageSize)];
                    datas.AddRange(sliced.Chunk(8));
                }

                List<DirtyData> parsed = datas.Select(data => new DirtyData(data)).ToList();

                //группировка по дате для последующей "склейки" в одну запись
                var grouped = parsed.GroupBy(i => i.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Datas = g.Select(c => c)
                    });
                var ci = new CultureInfo("ru-RU");
                List<Data> CleanData = grouped.Select(arg => new Data
                {
                    Date = arg.Date,
                    ExaustAirTemp = Convert.ToDecimal((from i in arg.Datas where i.LogItemId == "ExaustAirTemp" select i.Value).FirstOrDefault(), ci),
                    ExtractAirTemp = Convert.ToDecimal((from i in arg.Datas where i.LogItemId == "ExtractAirTemp" select i.Value).FirstOrDefault(), ci),
                    OutdoorAirTemp = Convert.ToDecimal((from i in arg.Datas where i.LogItemId == "OutdoorAirTemp" select i.Value).FirstOrDefault(), ci),
                    SupplyAirTemp = Convert.ToDecimal((from i in arg.Datas where i.LogItemId == "SupplyAirTemp" select i.Value).FirstOrDefault(), ci),
                    CO2 = Convert.ToInt32((from i in arg.Datas where i.LogItemId == "CO2" select i.Value).FirstOrDefault()),
                    Humidity = Convert.ToInt32((from i in arg.Datas where i.LogItemId == "Humidity" select i.Value).FirstOrDefault())
                })
                    .ToList();

                try
                {
                    await SaveToDb(CleanData, stoppingToken);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error saving to DB - {ex.Message}");
                }
                stoppingToken.ThrowIfCancellationRequested();
                collection = new BlockingCollection<List<byte[]>>(1);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug($"Task was cancelled - {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogCritical($"Error while process data - {ex.Message}");
        }

    }

    private async Task SaveToDb(List<Data> info, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Save data");
        using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            var sql = "INSERT INTO logs (datetime, extractairtemp, exaustairtemp, outdoorairtemp, supplyairtemp, co2, humidity) " +
                      "VALUES (@Date, @ExtractAirTemp, @ExaustAirTemp, @OutdoorAirTemp, @SupplyAirTemp, @CO2, @Humidity) on conflict (datetime) do nothing;";

            var rowsAffected = await connection.ExecuteAsync(sql, info);
            _logger.LogInformation($"Successfully saved {rowsAffected} records to DB");
            cancellationToken.ThrowIfCancellationRequested();
            await connection.CloseAsync();
        }
    }

    private bool CheckDB()
    {
        using (var connection = new NpgsqlConnection(ConnectionString))
        {
            var sql = "SELECT version()";
            connection.Open();
            using var cmd = new NpgsqlCommand(sql, connection);
            var version = cmd.ExecuteScalar()?.ToString();
            connection.Close();
            return !string.IsNullOrEmpty(version);
        }
    }
}