FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ExceptionKnowledgeBase.sln .
COPY src/ src/
RUN dotnet restore ExceptionKnowledgeBase.sln
RUN dotnet publish src/ExceptionKnowledgeBase.Worker/ExceptionKnowledgeBase.Worker.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ExceptionKnowledgeBase.Worker.dll"]
