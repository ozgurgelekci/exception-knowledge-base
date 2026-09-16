FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ExceptionKnowledgeBase.sln .
COPY src/ src/
RUN dotnet restore ExceptionKnowledgeBase.sln
RUN dotnet publish src/ExceptionKnowledgeBase.Api/ExceptionKnowledgeBase.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExceptionKnowledgeBase.Api.dll"]
