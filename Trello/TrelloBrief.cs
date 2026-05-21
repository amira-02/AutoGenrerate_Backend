namespace AutoGenerate.Shared.Models;

public class TrelloBrief
{
    public int      Id          { get; set; }
    public string   CardId      { get; set; } = "";
    public int?     ClientId    { get; set; }      // null = not yet assigned
    public string   Title       { get; set; } = "";
    public string?  Description { get; set; }
    public string?  SheetUrl    { get; set; }
    public string?  Due         { get; set; }
    public string?  LabelsJson  { get; set; }      // JSON string[]
    public string?  CardUrl     { get; set; }      // Trello shortUrl
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? AssignedAt { get; set; }

    public Client?  Client      { get; set; }
}
