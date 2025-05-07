namespace W3ChampionsStatisticService.Friends;

public class FriendResponseType
{
    private FriendResponseType(string value)
    {
        Value = value;
    }

    public string Value { get; private set; }

    public static FriendResponseType FriendResponseMessage
    {
        get { return new FriendResponseType("FriendResponseMessage"); }
    }
    public static FriendResponseType FriendResponseData
    {
        get { return new FriendResponseType("FriendResponseData"); }
    }
    public static FriendResponseType FriendsWithPictures
    {
        get { return new FriendResponseType("FriendsWithPictures"); }
    }
    public static FriendResponseType FriendOnlineStatus
    {
        get { return new FriendResponseType("FriendOnlineStatus"); }
    }

    public override string ToString()
    {
        return Value;
    }
}
