// Seed for the FriendRankPromoted live test. Run once against the compose mongo:
//
//   docker exec -i -e USER_BATTLETAG="You#1234" mongodb-website-backend-local-compose \
//     mongosh "mongodb://w3champions:w3champions@localhost:27017" --quiet \
//     < bench/seed-friend-rank.mongosh.js
//
// Creates (idempotently):
//   1. Your Friendlist containing TestFriend#1234 — so promotions of TestFriend
//      fan out to you (FriendRankPromotionNotifier.LoadFriendlistsContaining).
//   2. A LeagueConstellationChangedEvent for a synthetic season 99 EU 1v1 ladder.
//      LeagueSyncHandler (polls every 5s, needs START_HANDLERS=true) turns it into
//      the LeagueConstellation the notifier resolves league names/orders from.
//
// The constellation mirrors the shipped shape: league ids 0..5 with order 0 = top.
// Names carry divisions so the launcher renders e.g. "Gold 2".

const USER_BATTLETAG = process.env.USER_BATTLETAG;
const FRIEND_BATTLETAG = process.env.FRIEND_BATTLETAG || "TestFriend#1234";
if (!USER_BATTLETAG) {
  print("ERROR: set USER_BATTLETAG to your battleTag (docker exec -e USER_BATTLETAG=...)");
  quit(1);
}

const svc = db.getSiblingDB("W3Champions-Statistic-Service");

// 1. Your friend list gains TestFriend. $addToSet keeps re-runs and an existing
//    list safe; $setOnInsert completes the Friendlist document shape on first write.
svc.Friendlist.updateOne(
  { _id: USER_BATTLETAG },
  {
    $addToSet: { Friends: FRIEND_BATTLETAG },
    $setOnInsert: { BlockedBattleTags: [], BlockAllRequests: false },
  },
  { upsert: true }
);

// 2. The constellation event, exactly as matchmaking would write it (lowercase
//    DTO fields, wasSyncedJustNow:false marks it for checkout). Fixed _id so
//    re-running re-queues the same event instead of stacking duplicates.
const league = (id, order, name, division) => ({
  id: NumberInt(id),
  order: NumberInt(order),
  name: name,
  division: NumberInt(division),
  maxParticipantCount: NumberInt(100),
});
svc.LeagueConstellationChangedEvent.replaceOne(
  { _id: NumberInt(990001) },
  {
    season: NumberInt(99),
    gateway: NumberInt(20), // GateWay.Europe
    gameMode: NumberInt(1), // GameMode.GM_1v1
    leagues: [
      league(0, 0, "Grandmaster", 0),
      league(1, 1, "Master", 0),
      league(2, 2, "Diamond", 1),
      league(3, 3, "Platinum", 1),
      league(4, 4, "Gold", 2),
      league(5, 5, "Silver", 1),
    ],
    wasSyncedJustNow: false,
  },
  { upsert: true }
);

print(`Seeded: ${USER_BATTLETAG} is friends with ${FRIEND_BATTLETAG}; season-99 EU 1v1 constellation queued.`);
print("LeagueSyncHandler picks the constellation up within ~5s. Then run promote-test-friend.mongosh.js.");
