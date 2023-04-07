using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.WebSockets;
using Websocket.Client;
using OperationCanceledException = System.OperationCanceledException;

namespace Vallox.ValloxService;

public class ListenerService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _configuration;
    private List<byte[]> receivedlogs = new List<byte[]>();
    private readonly ILogger _logger;
    private readonly Uri url;
    readonly WebsocketClient _client;

    private int Timeout => Convert.ToInt32(_configuration.GetSection("Timeout").Value);

    public ListenerService()
    {

    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="lifetime"></param>
    /// <param name="loggerFactory"></param>
    /// <param name="configuration"></param>
    public ListenerService(IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _logger = loggerFactory.CreateLogger("ListenerService");
        _lifetime = lifetime;
        _configuration = configuration;
        url = new Uri(_configuration.GetSection("Uri").Value ?? string.Empty);
        _client = new WebsocketClient(url);

        _lifetime.ApplicationStarted.Register(() => _logger.LogDebug(
            "In ListenerService - host application started at: {time}.",
            DateTimeOffset.Now));
        _lifetime.ApplicationStopping.Register(() => _logger.LogDebug(
            "In ListenerService - host application stopping at: {time}.",
            DateTimeOffset.Now));
        _lifetime.ApplicationStopped.Register(() => _logger.LogDebug(
            "In ListenerService - host application stopped at: {time}.",
            DateTimeOffset.Now));
    }

    #region ServiceMethods

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("ListenerService started at: {time} and will take 1 seconds to complete.",
            DateTimeOffset.Now);

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopWatch = Stopwatch.StartNew();
        _logger.LogDebug("ListenerService stopped at: {time}", DateTimeOffset.Now);
        await base.StopAsync(cancellationToken);
        _logger.LogDebug("ListenerService took {ms} ms to stop.", stopWatch.ElapsedMilliseconds);
    }

    #endregion ServiceMethods

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(() => _logger.LogDebug(
            "In ListenerService - token was cancelled at: {time}.",
            DateTimeOffset.Now));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _client.ReconnectTimeout = TimeSpan.FromSeconds(10);
                _client.ReconnectionHappened.Subscribe(info =>
                    _logger.LogDebug($"Reconnection happened, type: {info.Type}")
                );

                _client.MessageReceived.Subscribe(ParseMessage,
                    () => _logger.LogDebug($"Completed"), stoppingToken);
                await _client.Start();

                await Task.Run(() => StartSendingRequests(stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException ex)
            {
                await _client.Stop(WebSocketCloseStatus.NormalClosure, "0");
                _logger.LogDebug($"Task was cancelled - {ex.Message}");
            }
            catch (Exception ex)
            {
                await _client.Stop(WebSocketCloseStatus.NormalClosure, "0");
                _logger.LogCritical($"Error - {ex.Message}");
            }
        }
    }

    public async Task StartSendingRequests(CancellationToken cancellationToken)
    {
        byte[] buf = new byte[] { 0x02, 0x00, 0xf3, 0x00, 0xf5, 0x00 };

        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation($"Send request");
            _client.Send(buf);
            await Task.Delay(TimeSpan.FromSeconds(Timeout), cancellationToken);   //TODO поставить потом 10 минут
        }
    }

    private void ParseMessage(ResponseMessage msg)
    {
        receivedlogs.Add(msg.Binary);
        if (receivedlogs.Count == 2)
        {
            _logger.LogInformation($"Received 2 packets");
            ProcessService.collection.Add(receivedlogs);
            ProcessService.collection.CompleteAdding();
            receivedlogs = new List<byte[]>();
        }
    }
}