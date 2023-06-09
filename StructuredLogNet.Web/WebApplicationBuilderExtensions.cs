﻿using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace StructuredLogNet.Web;

public static class WebApplicationBuilderExtensions
{
    public static ILoggerProvider AddStructuredLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddHostedService<LoggingHostedService>();

        var logItemQueue = new LogItemQueue();
        builder.Services.AddSingleton<ILogItemQueue>(logItemQueue);

        // logger
        var loggerProvider = LoggerProviderFactory
            .With(builder.Services, builder.Logging)
            .AddConsoleLogger()
            .AddLoggerTarget(() => new QueueFileLoggerTarget(logItemQueue))
            .Build();

        return loggerProvider;
    }
}
