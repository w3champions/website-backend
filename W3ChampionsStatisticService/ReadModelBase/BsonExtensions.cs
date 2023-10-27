using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;
using W3ChampionsStatisticService.Clans.ClanStates;

namespace W3ChampionsStatisticService.ReadModelBase;

public static class BsonExtensions
{
    public static IServiceCollection AddSpecialBsonRegistrations(this IServiceCollection services)
    {
        BsonClassMap.RegisterClassMap<ClanState>(cm => {
            cm.AutoMap();
            cm.SetIsRootClass(true);

            var featureType = typeof(ClanState);
            featureType.Assembly.GetTypes()
                .Where(type => featureType.IsAssignableFrom(type)).ToList()
                .ForEach(type => cm.AddKnownType(type));
        });

        return services;
    }
}
