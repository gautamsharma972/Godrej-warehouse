namespace WarehouseGate.Domain;

public class OutwardGateArrivalPhoto
{
    public int Id { get; set; }

    public int OutwardGateArrivalId { get; set; }
    public OutwardGateArrival? OutwardGateArrival { get; set; }

    public OutwardPhotoType Type { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
}
