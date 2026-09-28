FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/ src/
RUN dotnet restore src/ExceptionKnowledgeBase.Api/ExceptionKnowledgeBase.Api.csproj
RUN dotnet publish src/ExceptionKnowledgeBase.Api/ExceptionKnowledgeBase.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExceptionKnowledgeBase.Api.dll"]
