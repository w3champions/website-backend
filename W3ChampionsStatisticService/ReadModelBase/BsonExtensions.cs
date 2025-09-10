using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;
using W3ChampionsStatisticService.Clans.ClanStates;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.ReadModelBase;

public static class BsonExtensions
{
    public static IServiceCollection AddSpecialBsonRegistrations(this IServiceCollection services)
    {
        BsonClassMap.RegisterClassMap<ClanState>(cm =>
        {
            cm.AutoMap();
            cm.SetIsRootClass(true);

            var featureType = typeof(ClanState);
            featureType.Assembly.GetTypes().Where(type => featureType.IsAssignableFrom(type)).ToList().ForEach(type => cm.AddKnownType(type));
        });

        BsonClassMap.RegisterClassMap<Heroes.Hero>(heroMapper =>
        {
            heroMapper.AutoMap();
            heroMapper.MapCreator(hero => new Heroes.Hero(hero.Id, hero.Level));
        });

        BsonClassMap.RegisterClassMap<ChatColor>(chatColorMapper =>
        {
            chatColorMapper.AutoMap();
            chatColorMapper.MapCreator(chatColor => new ChatColor(chatColor.ColorId));
        });

        BsonClassMap.RegisterClassMap<ChatIcon>(chatIconMapper =>
        {
            chatIconMapper.AutoMap();
            chatIconMapper.MapCreator(chatIcon => new ChatIcon(chatIcon.IconId));
        });

        return services;
    }
}
