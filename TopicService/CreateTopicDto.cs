namespace AutoGenerate.Caption.Dto;

public class CreateTopicDto
{
    public int ClientId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Platform { get; set; }
}