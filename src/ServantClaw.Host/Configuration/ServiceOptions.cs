using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using ServantClaw.Domain.Configuration;

namespace ServantClaw.Host.Configuration;

public sealed class ServiceOptions
{
    public const string SectionName = "Service";
    public const string BackendExecutablePlaceholder = "__SET_CODEX_EXECUTABLE__";

    [Required]
    public string BotRootPath { get; set; } = string.Empty;

    [Required]
    public string ProjectsRootPath { get; set; } = string.Empty;

    [Required]
    [ValidateObjectMembers]
    public BackendOptions Backend { get; set; } = new();

    public ServiceConfiguration ToDomainConfiguration() =>
        new(BotRootPath, ProjectsRootPath, Backend.ToDomainConfiguration());
}
