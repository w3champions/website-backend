namespace W3C.Contracts.GameObjects
{
    public class MapForce
    {
        public int Team { get; set; }
        public MapForceSlot[] Slots { get; set; }
        public MapForceComputer[] Computers { get; set; } = new MapForceComputer[0];
    }
}
