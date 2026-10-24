using SmartCleaner.Core.Localization;

namespace SmartCleaner.Core.Privacy;

public enum PrivacyCategory
{
    Telemetry,
    Advertising,
    FeedbackAndErrors,
    TrackingAndSensors
}

public class PrivacyTweakItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PrivacyCategory Category { get; set; } = PrivacyCategory.Telemetry;
    public string CategoryName => Category switch
    {
        PrivacyCategory.Telemetry => Localizer.GetString("Privacy_Category_Telemetry"),
        PrivacyCategory.Advertising => Localizer.GetString("Privacy_Category_Advertising"),
        PrivacyCategory.FeedbackAndErrors => Localizer.GetString("Privacy_Category_FeedbackAndErrors"),
        PrivacyCategory.TrackingAndSensors => Localizer.GetString("Privacy_Category_TrackingAndSensors"),
        _ => Localizer.GetString("Privacy_Category_General")
    };

    public bool IsApplied { get; set; }
    public bool IsRecommended { get; set; } = true;
    public string? RegistryRoot { get; set; } // "HKLM" or "HKCU"
    public string? SubKeyPath { get; set; }
    public string? ValueName { get; set; }
    public object? DisabledValue { get; set; }
    public object? DefaultValue { get; set; }
    public string? ServiceName { get; set; }
}
