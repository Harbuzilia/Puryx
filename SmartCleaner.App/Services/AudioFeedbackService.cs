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
        catch { }
    }

    public void PlayCleanComplete()
    {
        if (!IsSoundEnabled) return;
        try
        {
            SystemSounds.Beep.Play();
        }
        catch { }
    }

    public void PlayBoostActivated()
    {
        if (!IsSoundEnabled) return;
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch { }
    }
}
