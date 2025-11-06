FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/SagaOrchestrator/SagaOrchestrator.csproj", "src/SagaOrchestrator/"]
RUN dotnet restore "src/SagaOrchestrator/SagaOrchestrator.csproj"
COPY . .
WORKDIR "/src/src/SagaOrchestrator"
RUN dotnet build "SagaOrchestrator.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SagaOrchestrator.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SagaOrchestrator.dll"]

