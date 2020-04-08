FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env

WORKDIR /app
COPY ./W3ChampionsStatisticService.sln ./

COPY ./W3ChampionsStatisticService/W3ChampionsStatisticService.csproj ./W3ChampionsStatisticService/W3ChampionsStatisticService.csproj
RUN dotnet restore ./W3ChampionsStatisticService/W3ChampionsStatisticService.csproj

COPY ./W3ChampionsStatisticService ./W3ChampionsStatisticService
RUN dotnet build ./W3ChampionsStatisticService/W3ChampionsStatisticService.csproj -c Release

RUN dotnet publish "./W3ChampionsStatisticService/W3ChampionsStatisticService.csproj" -c Release -o "../../app/out"

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build-env /app/out .

ENV ASPNETCORE_URLS http://*:80
EXPOSE 80

ENTRYPOINT dotnet W3ChampionsStatisticService.dll startHandlers=$START_HANDLERS mongoConnectionString=$MONGO_CONNECTION_STRING appInsights=$APP_INSIGHTS $TEST_ENV