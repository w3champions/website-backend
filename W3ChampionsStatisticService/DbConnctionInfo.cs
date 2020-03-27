namespace W3ChampionsStatisticService
{
    public class DbConnctionInfo
    {
        public string ConnectionString { get; }

        public DbConnctionInfo(string connectionString)
        {
            ConnectionString = connectionString;
        }
    }
}