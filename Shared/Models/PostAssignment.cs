namespace AutoGenerate.Shared.Models;

public class PostAssignment
{
    public int     Id                { get; set; }
    public int?    PostId            { get; set; }
    public int     ClientId          { get; set; }
    public string  RowKey            { get; set; } = "";
    public string  BriefTitle        { get; set; } = "";
    public int     AssignedToUserId  { get; set; }
    public int     AssignedByUserId  { get; set; }
    public string  TaskType          { get; set; } = ""; // "image" | "caption" | "full"
    public string  Status            { get; set; } = "pending"; // "pending" | "done"
    public string? SubmittedContent  { get; set; }
    public string? Notes             { get; set; }
    public int?    ParentAssignmentId { get; set; }   // set when ChefEquipe delegates to a member
    public DateTime  AssignedAt      { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt     { get; set; }

    // Navigation
    public User?           AssignedTo  { get; set; }
    public User?           AssignedBy  { get; set; }
    public Client?         Client      { get; set; }
    public PostAssignment? Parent      { get; set; }
}
