using System.Diagnostics;
using OpenTelemetry;

namespace W3ChampionsStatisticService.Services.Tracing;

public class BaggageToTagProcessor : BaseProcessor<Activity>
{
    public const string SessionIdKey = "session_id"; // This key is used to correlate Faro traces across backend services
    public const string BattleTagKey = "battle_tag";

    public override void OnStart(Activity activity)
    {
        ProcessBaggageItem(activity, SessionIdKey);
        ProcessBaggageItem(activity, BattleTagKey);
    }

    private void ProcessBaggageItem(Activity activity, string baggageKey)
    {
        var baggageValue = activity.GetBaggageItem(baggageKey);
        if (!string.IsNullOrEmpty(baggageValue))
        {
            bool tagExists = false;
            foreach (var tag in activity.TagObjects)
            {
                if (tag.Key == baggageKey)
                {
                    tagExists = true;
                    break;
                }
            }
            if (!tagExists)
            {
                activity.SetTag(baggageKey, baggageValue);
            }
        }
    }
}
