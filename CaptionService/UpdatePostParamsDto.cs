using System;
using System.Collections.Generic;

namespace AutoGenerate.CaptionService.Models  // ← change ici
{
    public class UpdatePostParamsDto
    {
        public DateTime? ScheduledAt { get; set; }
        public string? Tone { get; set; }
        public string? Hashtags { get; set; }
        public List<string>? Platforms { get; set; }
    }
}