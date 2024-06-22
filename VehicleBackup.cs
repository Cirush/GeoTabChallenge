using Geotab.Checkmate.ObjectModel;

public class VehicleBackup
{
    public Id? Id { get; set; }
    public string? Vin { get; set; } = String.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Odometer { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string ToCsv() => $"{Id},{Vin},{Latitude},{Longitude},{Odometer}, {Timestamp}";
}