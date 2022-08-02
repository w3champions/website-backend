# Introduction

The Statistic Service is the Backend for the https://github.com/w3champions/w3champions-ui Project. Among the data for
the website it also provides the chat backend and the clan functions used in the ingame client.

# Setup

## Setting up the database
We use mongodb. You can either remotely connect to the test db or spin up your own mongodb. The backend automatically connects to the test db at `mongodb://157.90.1.251:3512` if the environment variable `MONGO_CONNECTION_STRING` is missing, so you don't need to do anything.

If you want to run your own mongodb, you can either install [MongoDB](https://www.mongodb.com/try/download/community) for your OS, or run mongodb through docker:

```
docker run --name mongo1 --network=host mongo
```

You also need to install [MongoDB Database Tools](https://www.mongodb.com/try/download/database-tools) in order to use `mongodump` and `mongorestore`.

You should now be able to connect to mongodb://localhost:27017. Try it with:

```
mongosh localhost
```

or

```
mongo localhost
```

Now we need to fill the db up with some data. Dump the db from mongodb://157.90.1.251:3513:

```
mongodump --uri="mongodb://157.90.1.251:3513"
```

Restore your db from the dump:

```
mongorestore --uri="mongodb://localhost:27017" dump/
```

The backend should now be able to connect to `mongodb://localhost:27017`.

If you are using Visual Studio or VS Code, you can set the environment variable `MONGO_CONNECTION_STRING` in launch.json under `configurations.env`, or simply edit the line specifying the `mongoConnectionString` in Startup.cs to:

```
var mongoConnectionString = "mongodb://localhost:27017";
```

CAUTION:
When running locally, the readmodel handling is turned off, unless you set the corresponding environment variable. But only do that if you know what you are doing. You can end up overwriting data from prod/test, depending where you are connected. So always make sure the handlers are turned off.
(more on readmodels below).

## Running the project
Run the project through Visual Studio or VS Code. If you are using VS Code, you should install the official C# extension from Microsoft first.

# Linux setup

## Installing the .NET runtime
Go to [Microsoft](https://docs.microsoft.com/en-us/dotnet/core/install/linux) and click on "Download tarballs" and download version of .NET currently used in the Backend (Version 5.0 as of time of writing). Now we will make the dotnet binary available on the PATH:

```
$ mkdir -p /opt/dotnet
$ sudo tar zxf dotnet-sdk-5.0.408-linux-x64.tar.gz -C /opt/dotnet
$ sudo ln -s /opt/dotnet/dotnet /usr/bin
```

Now you should be able to start debugging in your favourite IDE.

# Testing
To run integration tests against the local database, edit this line:
https://github.com/w3champions/website-backend/blob/0f54e9216764aaf8617baacd54f3875036cc7b68/WC3ChampionsStatisticService.UnitTests/IntegrationTestBase.cs#L14

Warning: this WILL drop your local database, so be warned!

# Deploying to a Pull Request Environment
If you branch starts with "DEPLOY_" azure will create a automatic deployment for your pull request, so you can test it in an isolated environment. It will be deployed to whatever comes after "DEPLOY_". For example, if my branch is called DEPLOY_add-new-language the pr will be published to https://statistic-service-add-new-language.pr.w3champions.com. The https certificate will be generated after the deployment, but this can take some time.

If you need any other connection strings, just update the `docker-dompose.token.yaml` file accordingliy, for example if you want to use a different backend for the identification for example (which can also be deployed by a PR just like this repo).

When you are done, please contact one of the older devs, because they can delete the unused containers again.

# Read Model Handling
The readmodels for the DB are being generated from the event that the matchmaking service generates. After a match,
the matchmaking service pushes an event to the collection "MatchFinishedEvent". For every readmodel that we have, we
read the latest event and transform it into the model for the website. Like that we also generate statistics that are
bigger and would be too costly to calculate every time we enter the page (like the hero vs her stats for example).
This also decouples the matchmaking service from the stuff that we need to show on the website.

## Fixing Readmodels
The easiest way to fix models is to just recreate them from scratch. There is a flag in the `HandlerVersion` collection called `Stopped` for each handler, that you can put to `true` if you want the handler to stop. After that delete the corresponding collection and the handler version. Within the next 5 seconds the service should start from 0 again. Depending on the readmodel this might take a while (max 1h), so do this during off times, because the data will be bad within this time. You can not just delete the `HandlerVersion`, as for many statistics, we count the events. For example, if you replay the model for Player Games and you just delete the `HandlerVersion`, you will count old games twice. You can also delete data from one season only and then set the `HandlerVersion` to the first event within a season, to save some time. But this obviously is more hassle and deleting works just fine.

## Versioned Handlers
These are the default Handlers and you can implement one like so

```
public class PlayerWinrateHandler : IReadModelHandler
{
    private readonly IPlayerRepository _playerRepository;

    public PlayerWinrateHandler(
        IPlayerRepository playerRepository
        )
    {
        _playerRepository = playerRepository;
    }

    public async Task Update(MatchFinishedEvent nextEvent) // this is the method you need to implement
    {
        var playerMMrChanges = nextEvent.match.players;
        var winrateTasks = playerMMrChanges.Select(async p => await LoadAndApply(p, nextEvent.match.season));
        var newWinrates = (await Task.WhenAll(winrateTasks)).ToList();
        await _playerRepository.UpsertWins(newWinrates);
    }
}
```

As you can see you need to make the transformation and save the readmodel in our database. The convention always was, that we call the handler like the readmodel. In that case `PlayerWinrateHandler` and `PlayerWinrate`, so it is easier to find the collection to the handler. Also the handling framework saves the version of the last save event, when the function returns. (See ReadModelHandler<T> class for details). The version is saved in the collection `HandlerVersions`. So it makes sense to take a look at the versions from time to time. If they differ and do not move, it is likely that a handler is throwing exceptions and is stuck. The retry time is every 5 seconds.

## Unversioned Handlers
We also have unversioned handlers for stuff that is not tight to an event. For example the Rankings are also pushed to the database after a match and we load all of them in a handler, transform all ranks and then save all of them to our readmodel db. That is usually not needed, but the interface for that is `IAsyncUpdatable`. This function is called every 5 seconds aswell and you can do whatever you want in there periodically. Like a chron job for poor people ;)
