using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;
using W3C.Domain.Rewards.Entities;
using W3ChampionsStatisticService.Admin.CloudStorage.S3;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Rewards.Portraits;

public record PortraitReplacementPreview(long Owners, long SelectedProfiles, int RewardDefinitions, int? ReplacementPortraitId);
public record PortraitReplacementResult(long UpdatedPlayers, int UpdatedRewards);
public record PortraitOwner(string BattleTag, bool IsSelected, string Tooltip);
public record PortraitAssetStatus(int PortraitId, bool InCatalogue, long Owners, long SelectedProfiles, int RewardDefinitions);
public record PortraitOwnersResponse(long Total, List<PortraitOwner> Players);

public class PortraitReplacementService(MongoClient mongoClient, IS3Service storage) : MongoDbRepositoryBase(mongoClient)
{
    private static FilterDefinition<PersonalSetting> Selected(int id) =>
        Builders<PersonalSetting>.Filter.Eq(p => p.ProfilePicture.Race, AvatarCategory.Special) &
        Builders<PersonalSetting>.Filter.Eq(p => p.ProfilePicture.PictureId, id);

    internal static FilterDefinition<PersonalSetting> Affected(int id) =>
        Builders<PersonalSetting>.Filter.ElemMatch(p => p.SpecialPictures, p => p.PictureId == id) | Selected(id);

    public async Task<PortraitReplacementPreview> Preview(int oldId)
    {
        var old = await FindPortrait(oldId);
        var settings = CreateCollection<PersonalSetting>();
        var rewards = await FindRewards(oldId);
        return new PortraitReplacementPreview(
            await settings.CountDocumentsAsync(Affected(oldId)),
            await settings.CountDocumentsAsync(Selected(oldId)),
            rewards.Count, old.ReplacementPortraitId);
    }

    public async Task<PortraitOwnersResponse> Owners(int portraitId, int page, int pageSize, string search)
    {
        // Ownership remains valid after removal from the admin catalogue.
        if (portraitId < 1) throw new ValidationException("Invalid portrait ID.");
        if (page < 1 || pageSize < 1 || pageSize > 100 || page > 100000)
            throw new ValidationException("Invalid pagination. Use a page size between 1 and 100.");
        var filter = Affected(portraitId);
        if (!string.IsNullOrWhiteSpace(search))
            filter &= Builders<PersonalSetting>.Filter.Regex(p => p.Id, new BsonRegularExpression(Regex.Escape(search.Trim()), "i"));
        var settings = CreateCollection<PersonalSetting>();
        var total = await settings.CountDocumentsAsync(filter);
        var players = await settings.Find(filter).SortBy(p => p.Id).Skip((page - 1) * pageSize).Limit(pageSize)
            .Project(p => new PortraitOwner(p.Id, p.ProfilePicture != null &&
                p.ProfilePicture.Race == AvatarCategory.Special && p.ProfilePicture.PictureId == portraitId,
                p.SpecialPictures == null ? "" : p.SpecialPictures.Where(s => s.PictureId == portraitId)
                    .Select(s => s.Description).FirstOrDefault())).ToListAsync();
        return new PortraitOwnersResponse(total, players);
    }

    public async Task<List<PortraitAssetStatus>> AssetStatuses(List<int> portraitIds)
    {
        if (portraitIds == null || portraitIds.Count == 0 || portraitIds.Count > 100 || portraitIds.Any(id => id < 1))
            throw new ValidationException("Request between 1 and 100 positive portrait IDs.");
        var ids = portraitIds.Distinct().ToArray();
        var values = new BsonArray(ids);
        var selected = new BsonDocument("$and", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { "$ProfilePicture.Race", (int)AvatarCategory.Special }),
            new BsonDocument("$in", new BsonArray { "$ProfilePicture.PictureId", values })
        });
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("SpecialPictures.PictureId", new BsonDocument("$in", values)),
                new BsonDocument { { "ProfilePicture.Race", (int)AvatarCategory.Special }, { "ProfilePicture.PictureId", new BsonDocument("$in", values) } }
            })),
            new BsonDocument("$project", new BsonDocument
            {
                { "SelectedId", new BsonDocument("$cond", new BsonArray { selected, "$ProfilePicture.PictureId", BsonNull.Value }) },
                { "Ids", new BsonDocument("$setUnion", new BsonArray
                    {
                        new BsonDocument("$map", new BsonDocument { { "input", new BsonDocument("$ifNull", new BsonArray { "$SpecialPictures", new BsonArray() }) }, { "as", "picture" }, { "in", "$$picture.PictureId" } }),
                        new BsonDocument("$cond", new BsonArray { selected, new BsonArray { "$ProfilePicture.PictureId" }, new BsonArray() })
                    }) }
            }),
            new BsonDocument("$unwind", "$Ids"),
            new BsonDocument("$match", new BsonDocument("Ids", new BsonDocument("$in", values))),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$Ids" }, { "Owners", new BsonDocument("$sum", 1) },
                { "Selected", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { new BsonDocument("$eq", new BsonArray { "$Ids", "$SelectedId" }), 1, 0 })) }
            })
        };
        var countsTask = CreateCollection<PersonalSetting>().Aggregate<BsonDocument>(pipeline, new AggregateOptions { MaxTime = TimeSpan.FromSeconds(5) }).ToListAsync();
        var catalogueTask = CreateCollection<PortraitDefinition>().Find(Builders<PortraitDefinition>.Filter.In(p => p.Id, ids.Select(id => id.ToString()))).Project(p => p.Id).ToListAsync();
        var rewardsTask = CreateCollection<Reward>().Find(r => r.ModuleId == "portrait_reward").Project(r => r.Parameters).ToListAsync();
        await Task.WhenAll(countsTask, catalogueTask, rewardsTask);
        var counts = countsTask.Result.ToDictionary(row => row["_id"].ToInt32());
        var catalogue = catalogueTask.Result.ToHashSet();
        var rewardCounts = new Dictionary<int, int>();
        foreach (var parameters in rewardsTask.Result)
        {
            if (!parameters.TryGetValue("portraitIds", out var value)) continue;
            var json = JsonSerializer.SerializeToElement(value);
            if (json.ValueKind != JsonValueKind.Array) continue;
            foreach (var id in json.EnumerateArray().Where(item => item.TryGetInt32(out _)).Select(item => item.GetInt32()).Distinct())
                if (ids.Contains(id)) rewardCounts[id] = rewardCounts.GetValueOrDefault(id) + 1;
        }
        return ids.Select(id => new PortraitAssetStatus(id, catalogue.Contains(id.ToString()),
            counts.TryGetValue(id, out var row) ? row["Owners"].ToInt64() : 0,
            counts.TryGetValue(id, out row) ? row["Selected"].ToInt64() : 0,
            rewardCounts.GetValueOrDefault(id))).ToList();
    }

    public async Task<PortraitReplacementResult> Replace(int oldId, int newId, bool removeOldAssignments = false)
    {
        if (oldId == newId) throw new ValidationException("The replacement must have a different portrait ID.");
        var old = await FindPortrait(oldId);
        var replacement = await FindPortrait(newId);
        if (old.ReplacementPortraitId.HasValue && old.ReplacementPortraitId != newId)
            throw new ValidationException($"Portrait {oldId} already has replacement {old.ReplacementPortraitId}. Retry that replacement instead.");
        if (replacement.ReplacementPortraitId.HasValue)
            throw new ValidationException("The new portrait has already been replaced. Select its latest replacement instead.");

        // Check the authoritative storage before changing any grants. Alibaba is best effort.
        await storage.DownloadFile($"SPECIAL_{newId}.jpg");
        var rewards = await FindRewards(oldId);

        // Claim a single target. A repeat of the same operation is safe after a partial failure.
        var definitions = CreateCollection<PortraitDefinition>();
        var claim = await definitions.UpdateOneAsync(
            p => p.Id == oldId.ToString() && (p.ReplacementPortraitId == null || p.ReplacementPortraitId == newId),
            Builders<PortraitDefinition>.Update.Set(p => p.ReplacementPortraitId, newId));
        if (claim.MatchedCount == 0) throw new ValidationException("This portrait was replaced by another admin. Reload before continuing.");

        var inheritedGroups = (replacement.Groups ?? []).Concat(old.Groups ?? []).Distinct().ToList();
        await definitions.UpdateOneAsync(p => p.Id == newId.ToString(),
            Builders<PortraitDefinition>.Update.Set(p => p.Groups, inheritedGroups));

        // Update just the referenced parameter, preserving concurrent edits to other reward fields.
        foreach (var (reward, ids) in rewards)
        {
            var updatedIds = ids.Select(id => id == oldId ? newId : id).Distinct().ToArray();
            await CreateCollection<Reward>().UpdateOneAsync(r => r.Id == reward.Id,
                Builders<Reward>.Update.Set("Parameters.portraitIds", (object)updatedIds)
                    .Set(r => r.UpdatedAt, DateTime.UtcNow));
        }

        // Transfer the tooltip and selected picture atomically; optionally retire the old grant.
        var update = await CreateCollection<PersonalSetting>().UpdateManyAsync(Affected(oldId), PlayerUpdate(oldId, newId, removeOldAssignments));
        return new PortraitReplacementResult(update.ModifiedCount, rewards.Count);
    }

    private async Task<PortraitDefinition> FindPortrait(int id)
    {
        if (id <= 0) throw new ValidationException("Portrait IDs must be positive.");
        return await CreateCollection<PortraitDefinition>().Find(p => p.Id == id.ToString()).FirstOrDefaultAsync()
            ?? throw new ValidationException($"Portrait {id} does not exist.");
    }

    private async Task<List<(Reward Reward, List<int> Ids)>> FindRewards(int oldId)
    {
        var rewards = await CreateCollection<Reward>().Find(r => r.ModuleId == "portrait_reward").ToListAsync();
        var affected = new List<(Reward, List<int>)>();
        foreach (var reward in rewards)
        {
            if (!reward.Parameters.TryGetValue("portraitIds", out var value)) continue;
            // Handles the int arrays, object arrays and JsonElements used by reward parameters.
            var ids = JsonSerializer.SerializeToElement(value).EnumerateArray().Select(id => id.GetInt32()).ToList();
            if (ids.Contains(oldId)) affected.Add((reward, ids));
        }
        return affected;
    }

    internal static UpdateDefinition<PersonalSetting> PlayerUpdate(int oldId, int newId, bool removeOldAssignments = false)
    {
        var pictures = new BsonDocument("$ifNull", new BsonArray { "$SpecialPictures", new BsonArray() });
        var oldPicture = new BsonDocument("$arrayElemAt", new BsonArray
        {
            new BsonDocument("$filter", new BsonDocument
            {
                { "input", pictures }, { "as", "picture" },
                { "cond", new BsonDocument("$eq", new BsonArray { "$$picture.PictureId", oldId }) }
            }), 0
        });
        var newPicture = new BsonDocument
        {
            { "PictureId", newId },
            { "Description", new BsonDocument("$let", new BsonDocument
                {
                    { "vars", new BsonDocument("old", oldPicture) },
                    { "in", new BsonDocument("$ifNull", new BsonArray { "$$old.Description", "" }) }
                }) }
        };
        var hasNew = new BsonDocument("$in", new BsonArray
        {
            newId, new BsonDocument("$map", new BsonDocument
            {
                { "input", pictures }, { "as", "picture" }, { "in", "$$picture.PictureId" }
            })
        });
        var selectedOld = new BsonDocument("$and", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { "$ProfilePicture.Race", (int)AvatarCategory.Special }),
            new BsonDocument("$eq", new BsonArray { "$ProfilePicture.PictureId", oldId })
        });
        var changed = new BsonDocument("$or", new BsonArray
        {
            new BsonDocument("$not", new BsonArray { hasNew }), selectedOld,
            removeOldAssignments ? new BsonDocument("$in", new BsonArray { oldId, new BsonDocument("$map", new BsonDocument { { "input", pictures }, { "as", "picture" }, { "in", "$$picture.PictureId" } }) }) : BsonBoolean.False
        });
        // PersonalSetting stores DateTimeOffset as [ticks, offsetMinutes]. Advance its
        // change-feed timestamp only when a grant or selected profile actually changes.
        var timestamp = new BsonArray { DateTimeOffset.UtcNow.Ticks, 0 };
        var grants = new BsonDocument("$cond", new BsonArray
            { hasNew, pictures, new BsonDocument("$concatArrays", new BsonArray { pictures, new BsonArray { newPicture } }) });
        BsonValue finalGrants = removeOldAssignments ? new BsonDocument("$filter", new BsonDocument
            { { "input", grants }, { "as", "picture" }, { "cond", new BsonDocument("$ne", new BsonArray { "$$picture.PictureId", oldId }) } }) : grants;
        PipelineDefinition<PersonalSetting, PersonalSetting> pipeline = new[]
        {
            new BsonDocument("$set", new BsonDocument
            {
                { "SpecialPictures", finalGrants },
                { "ProfilePicture", new BsonDocument("$cond", new BsonArray
                    { selectedOld, new BsonDocument("$mergeObjects", new BsonArray
                        { "$ProfilePicture", new BsonDocument("PictureId", newId) }), "$ProfilePicture" }) },
                { "LastUpdated", new BsonDocument("$cond", new BsonArray { changed, timestamp, "$LastUpdated" }) }
            })
        };
        return Builders<PersonalSetting>.Update.Pipeline(pipeline);
    }
}
