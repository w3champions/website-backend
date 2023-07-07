using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Friends
{
    public class FriendRequest
    {
        public FriendRequest(string sender, string receiver)
        {
            Sender = sender;
            Receiver = receiver;
        }
        public ObjectId Id { get; set; }
        public string Sender { get; set; }
        public string Receiver  { get; set; }
    }
}
