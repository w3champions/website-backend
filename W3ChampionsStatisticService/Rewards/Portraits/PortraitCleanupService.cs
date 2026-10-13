using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;
using W3C.Domain.Rewards.Entities;
using W3ChampionsStatisticService.Admin.CloudStorage.Alibaba;
using W3ChampionsStatisticService.Admin.CloudStorage.S3;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Rewards.Portraits;

public record PortraitCleanupRequest(bool RemoveAssignments, bool RemoveDefinition, bool DeleteS3, bool DeleteAlibaba);
public record PortraitCleanupResult(long UpdatedPlayers, int UpdatedRewards, List<string> Warnings);

// Each step is idempotent so an interrupted cleanup can be retried, even without a definition.
public class PortraitCleanupService(MongoClient client, IS3Service storage, IAlibabaService alibaba) : MongoDbRepositoryBase(client)
{
    public async Task<PortraitCleanupResult> Cleanup(int id, PortraitCleanupRequest request)
    {
        if (id < 1) throw new ValidationException("Portrait IDs must be positive.");
        if (!request.RemoveAssignments && !request.RemoveDefinition && !request.DeleteS3 && !request.DeleteAlibaba)
            throw new ValidationException("Choose at least one cleanup action.");
        var updatedRewards = 0;
        if (request.RemoveDefinition)
        {
            var rewards = CreateCollection<Reward>();
            foreach (var reward in await rewards.Find(r => r.ModuleId == "portrait_reward").ToListAsync())
            {
                if (!reward.Parameters.TryGetValue("portraitIds", out var value)) continue;
                var ids = JsonSerializer.SerializeToElement(value).EnumerateArray().Select(v => v.GetInt32()).ToArray();
                if (!ids.Contains(id)) continue;
                var remaining = ids.Where(v => v != id).ToArray();
                var update = Builders<Reward>.Update.Set("Parameters.portraitIds", (object)remaining).Set(r => r.UpdatedAt, DateTime.UtcNow);
                if (remaining.Length == 0) update = update.Set(r => r.IsActive, false);
                await rewards.UpdateOneAsync(r => r.Id == reward.Id, update);
                updatedRewards++;
            }
            await CreateCollection<PortraitDefinition>().DeleteOneAsync(p => p.Id == id.ToString());
        }
        long updatedPlayers = 0;
        if (request.RemoveAssignments)
        {
            var selected = new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { "$ProfilePicture.Race", (int)AvatarCategory.Special }),
                new BsonDocument("$eq", new BsonArray { "$ProfilePicture.PictureId", id })
            });
            PipelineDefinition<PersonalSetting, PersonalSetting> pipeline = new[]
            {
                new BsonDocument("$set", new BsonDocument
                {
                    { "SpecialPictures", new BsonDocument("$filter", new BsonDocument
                        {
                            { "input", new BsonDocument("$ifNull", new BsonArray { "$SpecialPictures", new BsonArray() }) },
                            { "as", "picture" }, { "cond", new BsonDocument("$ne", new BsonArray { "$$picture.PictureId", id }) }
                        }) },
                    { "ProfilePicture", new BsonDocument("$cond", new BsonArray { selected,
                        new BsonDocument { { "Race", (int)AvatarCategory.Starter }, { "PictureId", 1L }, { "IsClassic", false } }, "$ProfilePicture" }) },
                    { "LastUpdated", new BsonArray { DateTimeOffset.UtcNow.Ticks, 0 } }
                })
            };
            var result = await CreateCollection<PersonalSetting>().UpdateManyAsync(PortraitReplacementService.Affected(id), Builders<PersonalSetting>.Update.Pipeline(pipeline));
            updatedPlayers = result.ModifiedCount;
        }
        var warnings = new List<string>();
        // Storage failures must not undo a completed database cleanup. Report each provider separately.
        if (request.DeleteS3)
        {
            try { await storage.DeleteFile($"SPECIAL_{id}.jpg"); }
            catch (Exception) { warnings.Add("S3 deletion failed. Retry cleanup to delete the remaining file."); }
        }
        if (request.DeleteAlibaba)
        {
            try { alibaba.DeleteFile($"SPECIAL_{id}.jpg"); }
            catch (Exception) { warnings.Add("Alibaba deletion failed. Retry cleanup when its API is available."); }
        }
        return new PortraitCleanupResult(updatedPlayers, updatedRewards, warnings);
    }
}
