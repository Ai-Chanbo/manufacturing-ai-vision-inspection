namespace VisionInspectionHmi.Models;

public class InspectionHistory
{
    public DateTime InspectedAt { get; set; }
    public string ImageFileName { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public double Score { get; set; }
    public string DefectType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ApiStatus { get; set; } = string.Empty;
    public double InferenceMs { get; set; }
    public string CapturedImagePath { get; set; } = string.Empty;
}
