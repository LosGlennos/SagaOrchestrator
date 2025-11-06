FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/RentalCarService/RentalCarService.csproj", "src/RentalCarService/"]
RUN dotnet restore "src/RentalCarService/RentalCarService.csproj"
COPY . .
WORKDIR "/src/src/RentalCarService"
RUN dotnet build "RentalCarService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RentalCarService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RentalCarService.dll"]

