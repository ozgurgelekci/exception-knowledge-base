using ExceptionKnowledgeBase.Application;
using ExceptionKnowledgeBase.Infrastructure;
using ExceptionKnowledgeBase.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddExceptionKnowledgeBaseApplication(builder.Configuration)
    .AddExceptionKnowledgeBaseInfrastructure(builder.Configuration);

builder.Services.AddHostedService<ExceptionEmbeddingWorker>();
builder.Services.AddHostedService<KnowledgeEmbeddingWorker>();

var host = builder.Build();
host.Run();
