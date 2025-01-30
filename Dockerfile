FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

WORKDIR /app
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY ./W3ChampionsStatisticService ./W3ChampionsStatisticService
COPY ./W3C.Domain ./W3C.Domain
COPY ./W3C.Contracts ./W3C.Contracts
RUN dotnet build "./W3ChampionsStatisticService/W3ChampionsStatisticService.csproj" -c Release

RUN dotnet publish "./W3ChampionsStatisticService/W3ChampionsStatisticService.csproj" -c Release -o "../../app/out"

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build-env /app/out .

ENV ASPNETCORE_URLS http://*:80
EXPOSE 80

ENTRYPOINT dotnet W3ChampionsStatisticService.dll