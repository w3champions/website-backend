# w3champions-statistic-service

The statisticservice is the Backend for the https://github.com/w3champions/w3champions-ui Project. Among the data for
the website it also provides the chat backend that is used from the ingame client and the clan functions.

## Setup
You will need a mongodb to run the service. If you do not have a local mongo container, spin one up with

```
docker run mongo
```
and your local service should be able to connect to this default mongo adress. The default is our open test db with connectionstring `mongodb://157.90.1.251:3513`

If you have your own MongoDb, you need to run the service with a Env Variable Called "MONGO_CONNECTION_STRING" and
set it to the corresponding connection string. You can also just replace the line in the Startup.cs with the needed
connection string (Line 57).

CAUTION:
When running locally, the readmodel handling is turned off, unless you set the corresponding env variable. But only
do that, if you know what you are doing. You can end up overwriting data from prod/test, depending where you are
connected. So always make sure the handlers are turned off, or you have breakpoints set before you save any data.
(more to readmodels below).

## Read Model Handling
The readmodels for the DB are being generated from the event that the matchmaking service generates. After a match,
the matchmaking service pushes an event to the collection "MatchFinishedEvent". For every readmodel that we have, we
read the latest event and transform it into the model for the website. Like that we also generate statistics that are
bigger and would be too costly to calculate every time we enter the page (like the hero vs her stats for example).
This also decouples the matchmaking service from the stuff that we need to show on the website.

CAUTION:
When running locally, the readmodel handling is turned off, unless you set the corresponding env variable. But only
do that, if you know what you are doing. You can end up overwriting data from prod/test, depending where you are
connected. So always make sure the handlers are turned off, or you have breakpoints set before you save any data.

## Fixing Readmodels
The easiest way to fix models is to just recreate them from scratch. There is a flag in the `HandlerVersion` collection called `Stopped` for each handler, that you can put to `true` if you want the handler to stop. After that delete the corresponding collection and the handler version. Within the next 5 seconds the service should start from 0 again. Depending on the readmodel this might take a while (max 1h), so do this during off times, because the data will be bad within this time. You can not just delete the `HandlerVersion`, as for many statistics, we count the events. For example, if you replay the model for Player Games and you just delete the `HandlerVersion`, you will count old games twice. You can also delete data from one season only and then set the `HandlerVersion` to the first event within a season, to save some time. But this obviously is more hassle and deleting works just fine.

### Versioned Handlers
Those are the default Handlers and you can implement one like so
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

As you can see you need to make the transformation and save the readmodel in our database. The convention always was,
 that we call the handler like the readmodel. In that case `PlayerWinrateHandler` and `PlayerWinrate`, so it is
 easier to find the collection to the handler.
 Also the handling framework saves the version of the last save event, when the function returns. (See
 ReadModelHandler<T> class for details). The version is saved in the collection `HandlerVersions`. So it makes sens
 to take a look at the versions from time to time. If they differ and do not move, it is likely that a handler is
 throwing exceptions and is stuck. The retry time is every 5 seconds.

 ### Unversioned Handlers
 We also have unversioned handlers for stuff that is not tight to an event. For example the Rankings are also pushed
 to the database after a match and we load all of them in a handler, transform all ranks and then save all of them to
  our readmodel db. That is usually not needed, but the interface for that is `IAsyncUpdatable`. This function is
  called every 5 seconds aswell and you can do whatever you want in there periodically. Like a chron job for poor
  people ;)

## Import Export Mongodata
To run the service locally with data, you will need to install: 
- [MongoDB](https://www.mongodb.com/try/download/community)
- [MongoDB Database Tools](https://www.mongodb.com/try/download/database-tools)
- [MongoDB Compass](https://www.mongodb.com/products/compass) (not necessary but recommended)

There is a dump of the W3Champions production database from Season 11 here: 
https://drive.google.com/drive/folders/1mfH_jECJI6kisaA0uBDsXkYxk42FcRF9

Note that the `MatchFinishedEvents` collection is separate. This collection is only useful if you plan to work on pre-S10 match results pages.

Download the collections and put them in a folder `dump` then to import it, run:

```
mongorestore --uri="mongodb://localhost:27017" dump/
```

We also have an open test db here, but be warned it may be unstable due to people adding new properties or collections, feel free to edit it as you require, or run integration tests against it `mongodb://157.90.1.251:3513`
    
If you need access to the test environment database, ask a Dev and they can give you the connection string. Please dont run integration tests against the test DB!

Change this line to your localhost, and you should be good to go!
https://github.com/w3champions/website-backend/blob/0f54e9216764aaf8617baacd54f3875036cc7b68/W3ChampionsStatisticService/Startup.cs#L63

To run integration tests against the local database, edit this line:
https://github.com/w3champions/website-backend/blob/0f54e9216764aaf8617baacd54f3875036cc7b68/WC3ChampionsStatisticService.UnitTests/IntegrationTestBase.cs#L14

Warning: this WILL drop your local database, so be warned!

### Deploying to a Pull Request Environment
If you branch starts with "DEPLOY_" azure will create a automatic deployment for your pull request, so you can test it in an isolated environment. It will be deployed to whatever comes after "DEPLOY_". For example, if my branch is called DEPLOY_add-new-language the pr will be published to https://statistic-service-add-new-language.pr.w3champions.com. The https certificate will be generated after the deployment, but this can take some time.

If you need any other connection strings, just update the `docker-dompose.token.yaml` file accordingliy, for example if you want to use a different backend for the identification for example (which can also be deployed by a PR just like this repo).

When you are done, please contact one of the older devs, because they can delete the unused containers again.
