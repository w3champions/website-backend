FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

WORKDIR /src

# Copy only the files needed to restore dependencies first to leverage Docker layer caching.
COPY Directory.Build.props Directory.Packages.props ./

# Copy csproj files to restore packages. This avoids re-downloading packages when source code changes.
COPY W3ChampionsStatisticService/*.csproj W3ChampionsStatisticService/
COPY W3C.Domain/*.csproj W3C.Domain/
COPY W3C.Contracts/*.csproj W3C.Contracts/

RUN dotnet restore "W3ChampionsStatisticService/W3ChampionsStatisticService.csproj"

# Copy the rest of the sources and build
COPY . .

WORKDIR /src/W3ChampionsStatisticService
RUN dotnet build "W3ChampionsStatisticService.csproj" -c Release --no-restore

RUN dotnet publish "W3ChampionsStatisticService.csproj" -c Release -o "/app/out" --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build-env /app/out .

ENV ASPNETCORE_URLS=http://*:80
EXPOSE 80

ENTRYPOINT ["dotnet", "W3ChampionsStatisticService.dll"]
