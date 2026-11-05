namespace Broiler.Mail.Application.Preview;

/// <summary>UI-thread, message-local viewport. Position is a fraction of the vertical scroll range, not a text offset or exact paragraph mapping.</summary>
public interface IReaderViewport
{
    double ReadingPosition { get; }
    /// <summary>Clamp finite values to 0..1 and apply after the next nonempty layout, including when currently hidden. Reject NaN/infinity.</summary>
    void RestoreReadingPosition(double fraction);
}
