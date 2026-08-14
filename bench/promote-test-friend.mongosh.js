// Promotes TestFriend one league per run, through the REAL pipeline: this only
// inserts a RankingChangedEvent — byte-identical to what matchmaking writes —
// and RankSyncHandler + FriendRankPromotionNotifier do the rest.
//
//   docker exec -i mongodb-website-backend-local-compose \
//     mongosh "mongodb://w3champions:w3champions@localhost:27017" --quiet \
//     < bench/promote-test-friend.mongosh.js
//
// Run 1: TestFriend appears in Silver — records the baseline, SILENT by design
//        (a first sighting is not a promotion).
// Run 2: Silver -> Gold. Expect the FriendRankPromoted push and the launcher toast.
// Run 3+: one league up each run, until Grandmaster.
//
// Wait ~10s between runs: the script reads the LeagueBaseline the 5s poller
// writes, so a too-quick re-run sees a stale baseline and re-inserts the same league.

const FRIEND_BATTLETAG = process.env.FRIEND_BATTLETAG || "TestFriend#1234";
const SEASON = 99, GATEWAY = 20, GAME_MODE_NAME = "GM_1v1"; // must match seed-friend-rank.mongosh.js

const svc = db.getSiblingDB("W3Champions-Statistic-Service");

// Baseline id mirrors Rank's: {season}_{btag}@{gateway}_{gameModeName}_{raceName}.
// The race suffix is the ENUM NAME ("UD" for race 8), appended because the event
// below carries a race — 1v1 standings are per-race.
const standingId = `${SEASON}_${FRIEND_BATTLETAG}@${GATEWAY}_${GAME_MODE_NAME}_UD`;
const baseline = svc.LeagueBaseline.findOne({ _id: standingId });

// Seed constellation has league ids 0..5 matching orders 0..5 (0 = top), so
// "one league better" is id - 1. No baseline yet -> start in Silver (5).
let targetLeague;
if (baseline == null) {
  targetLeague = 5;
  print(`No baseline for ${standingId} yet -> inserting Silver roster. This run is SILENT by design.`);
} else if (baseline.League <= 0) {
  print(`${FRIEND_BATTLETAG} is already Grandmaster. Drop the baseline to restart:`);
  print(`  use W3Champions-Statistic-Service; db.LeagueBaseline.deleteOne({_id: "${standingId}"})`);
  quit(0);
} else {
  targetLeague = baseline.League - 1;
  print(`Baseline league ${baseline.League} -> promoting to league ${targetLeague}. Expect the push within ~5s.`);
}

// Event _id is an int (epoch seconds): unique per run, upsert absorbs same-second re-runs.
svc.RankingChangedEvent.replaceOne(
  { _id: NumberInt(Math.floor(Date.now() / 1000)) },
  {
    season: NumberInt(SEASON),
    gateway: NumberInt(GATEWAY),
    gameMode: NumberInt(1), // GameMode.GM_1v1
    league: NumberInt(targetLeague),
    ranks: [{ battleTags: [FRIEND_BATTLETAG], rp: 1234, race: NumberInt(8) /* Race.UD */ }],
    wasSyncedJustNow: false,
  },
  { upsert: true }
);

print("RankingChangedEvent queued. RankSyncHandler checks it out within ~5s.");
