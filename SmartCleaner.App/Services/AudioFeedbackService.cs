using System.Media;

namespace SmartCleaner.App.Services;

public class AudioFeedbackService
{
    public bool IsSoundEnabled { get; set; } = true;

    public void PlayScanComplete()
    {
        if (!IsSoundEnabled) return;
        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AudioFeedbackService] PlayScanComplete error: {ex.Message}"); }
    }

    public void PlayCleanComplete()
    {
        if (!IsSoundEnabled) return;
        try
        {
            SystemSounds.Beep.Play();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AudioFeedbackService] PlayCleanComplete error: {ex.Message}"); }
    }

    public void PlayBoostActivated()
    {
        if (!IsSoundEnabled) return;
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AudioFeedbackService] PlayBoostActivated error: {ex.Message}"); }
    }
}
