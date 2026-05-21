namespace AutoGenerate.Shared.Models;

// Tracks the last-known state of each Google Sheet row per client.
// Used to detect additions, modifications, and cancellations.
public class SheetRow
{
    public int      Id           { get; set; }
    public int      ClientId     { get; set; }
    public string   RowKey       { get; set; } = "";  // "N° post" value, or row index as string
    public string   ContentHash  { get; set; } = "";  // SHA256 of the row, for change detection
    public int?     PostId       { get; set; }         // linked Post (null if row had no caption)
    public DateTime LastSeenAt   { get; set; }

    public Client? Client { get; set; }
    public Post?   Post   { get; set; }
}
