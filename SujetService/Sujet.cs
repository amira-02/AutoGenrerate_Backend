using System.Text.Json.Serialization;

namespace AutoGenerate.Campaign;

public class CampaignWorkflow
{
    public string Name { get; set; }
    public List<WorkflowStep> Steps { get; set; }
}

public class WorkflowStep
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Previewable { get; set; }

    [JsonPropertyName("depends_on")]
    public List<string> DependsOn { get; set; } = new List<string>();
}