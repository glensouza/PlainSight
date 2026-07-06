namespace PlainSight.Server.Models;

public class TickerMessage
{
    private string text = string.Empty;

    public int Id { get; init; }
    public string Text
    {
        get => this.text;
        set
        {
            if (value.Length > 500)
                throw new ArgumentException("Ticker message text cannot exceed 500 characters.", nameof(value));
            this.text = value;
        }
    }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? TargetGroup { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
