namespace AutoGenerate.ExternalTasks
{
    public class ExternalTaskDto
    {
        public string TaskId { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime ScheduledAt { get; set; }
        public string CallbackUrl { get; set; } = "";
    }
}
