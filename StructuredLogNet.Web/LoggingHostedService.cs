using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TessBox.Sdk.Dotnet.Logging;

namespace StructuredLogNet.Web;

internal class LoggingHostedService : BackgroundService
{
    private readonly IStructuredLogger<LoggingHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;

    private volatile bool _ready = false;

    public LoggingHostedService(
        IConfiguration configuration,
        IStructuredLogger<LoggingHostedService> logger,
        IServiceProvider serviceProvider,
        IHostApplicationLifetime lifetime
    )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        lifetime.ApplicationStarted.Register(() => _ready = true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!_ready)
        {
            // App hasn't started yet, keep looping!
            await Task.Delay(1_000, stoppingToken);
        }

        _logger.Info("Ready to listen job");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var logItem = await _queue.DequeueAsync(stoppingToken);

                _loggerTarget.Log(logItem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing Jobs.");
            }
        }
    }

    private async Task RunJob(JobRun run)
    {
        try
        {
            _logger.Info(
                $"Start job {run.Name}",
                ("", run.Id),
                ("Parameters", run.Parameters ?? string.Empty)
            );
            run.State = JobRunState.InRunning;
            run.StartDate = DateTime.UtcNow;

            var jobInstance = (IJob)
                ActivatorUtilities.CreateInstance(_serviceProvider, run.ClassTypeToInvoke);
            await jobInstance.InvokeAsync(run.Parameters, CancellationToken.None);

            run.State = JobRunState.Completed;
            run.EndDate = DateTime.UtcNow;
            _logger.Info($"Job {run.Name} completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred executing Job {Name}.", run.Name);

            if (run != null)
            {
                run.State = JobRunState.InError;
            }
        }
        finally
        {
            _logger.LogInformation("Job run {runId}/{name} finished", run.Id, run.Name);
        }
    }

    private void OnTaskCompleted(Task completedTask)
    {
        lock (_runningTasks)
        {
            if (!_runningTasks.Remove(completedTask))
                throw new Exception("An unexpected error occured");

            _remainingConcurrency++;
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queued Hosted Service is stopping.");

        await base.StopAsync(stoppingToken);
    }
}

internal interface ILogItemQueue
{
    void Queue(LogItem item);

    Task<LogItem> DequeueAsync(CancellationToken cancellationToken);
}

internal class LogItemQueue : ILogItemQueue
{
    private readonly Channel<LogItem> _queue;

    public LogItemQueue()
    {
        var options = new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait, };

        _queue = Channel.CreateBounded<LogItem>(options);
    }

    public void Queue(LogItem item)
    {
        _queue.Writer.TryWrite(item);
    }

    public async Task<LogItem> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}

internal class QueueFileLoggerTarget : ILoggerTarget
{
    private readonly ILogItemQueue _queue;

    public QueueFileLoggerTarget(ILogItemQueue logItemQueue)
    {
        _queue = logItemQueue;
    }

    public void Log(LogItem item)
    {
        _queue.Queue(item);
    }
}

[Description("Write the log in files")]
internal class FileLoggerJob : IJob
{
    private readonly ILogItemQueue _queue;
    private readonly FileLoggerTarget _loggerTarget;

    public FileLoggerJob(
        ILogItemQueue queue,
        IAppInfo appInfo,
        IStructuredLogger<FileLoggerJob> logger
    )
    {
        _queue = queue;

        logger.Info("Log to file", new[] { ("LogPath", appInfo.LogPath) });
        _loggerTarget = new FileLoggerTarget(appInfo.LogPath);
    }

    public async Task InvokeAsync(string? parameters, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}