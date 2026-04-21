using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;

namespace ServantClaw.Infrastructure.State;

public sealed class FileStateStore : IStateStore
{
    private const string OwnerConfigurationFileName = "owner.json";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly Action<ILogger, string, string, string, Exception?> QuarantinedCorruptedStateLog =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(QuarantineCorruptedFileAsync)),
            "Quarantined corrupted {RecordType} state file from {CanonicalPath} to {QuarantinePath}.");

    private readonly string botRootPath;
    private readonly ILogger<FileStateStore> logger;

    public FileStateStore(ServiceConfiguration serviceConfiguration, ILogger<FileStateStore> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceConfiguration);
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        botRootPath = serviceConfiguration.BotRootPath;
    }

    public async ValueTask<ChatState?> GetChatStateAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        string path = GetChatStatePath(chatId);
        ChatStateFileModel? model = await ReadRecordAsync<ChatStateFileModel>("chat-state", path, cancellationToken);
        return model?.ToDomain();
    }

    public ValueTask SaveChatStateAsync(ChatState chatState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatState);
        return WriteRecordAsync(GetChatStatePath(chatState.ChatId), ChatStateFileModel.FromDomain(chatState), cancellationToken);
    }

    public async ValueTask<ThreadMapping?> GetThreadMappingAsync(ThreadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string path = GetThreadMappingPath(context);
        ThreadMappingFileModel? model = await ReadRecordAsync<ThreadMappingFileModel>("thread-mapping", path, cancellationToken);
        return model?.ToDomain();
    }

    public ValueTask SaveThreadMappingAsync(ThreadMapping threadMapping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(threadMapping);
        return WriteRecordAsync(
            GetThreadMappingPath(threadMapping.Context),
            ThreadMappingFileModel.FromDomain(threadMapping),
            cancellationToken);
    }

    public async ValueTask<ApprovalRecord?> GetApprovalAsync(ApprovalId approvalId, CancellationToken cancellationToken)
    {
        foreach (string path in GetApprovalPaths(approvalId))
        {
            ApprovalRecordFileModel? model = await ReadRecordAsync<ApprovalRecordFileModel>("approval-record", path, cancellationToken);
            if (model is not null)
            {
                return model.ToDomain();
            }
        }

        return null;
    }

    public async ValueTask<IReadOnlyCollection<ApprovalRecord>> GetPendingApprovalsAsync(CancellationToken cancellationToken)
    {
        string directory = GetPendingApprovalsDirectory();
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<ApprovalRecord> approvals = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            ApprovalRecordFileModel? model = await ReadRecordAsync<ApprovalRecordFileModel>("approval-record", path, cancellationToken);
            if (model is not null)
            {
                approvals.Add(model.ToDomain());
            }
        }

        return approvals;
    }

    public ValueTask SaveApprovalAsync(ApprovalRecord approvalRecord, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approvalRecord);

        string targetPath = approvalRecord.IsPending
            ? GetPendingApprovalPath(approvalRecord.ApprovalId)
            : GetResolvedApprovalPath(approvalRecord.ApprovalId);

        return SaveApprovalAsyncCore(approvalRecord, targetPath, cancellationToken);
    }

    public async ValueTask<OwnerConfiguration?> GetOwnerConfigurationAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(botRootPath, "config", OwnerConfigurationFileName);
        OwnerConfigurationFileModel? model = await ReadRecordAsync<OwnerConfigurationFileModel>("owner-configuration", path, cancellationToken);
        return model?.ToDomain();
    }

    private async ValueTask SaveApprovalAsyncCore(
        ApprovalRecord approvalRecord,
        string targetPath,
        CancellationToken cancellationToken)
    {
        await WriteRecordAsync(targetPath, ApprovalRecordFileModel.FromDomain(approvalRecord), cancellationToken);

        string obsoletePath = approvalRecord.IsPending
            ? GetResolvedApprovalPath(approvalRecord.ApprovalId)
            : GetPendingApprovalPath(approvalRecord.ApprovalId);

        if (File.Exists(obsoletePath))
        {
            File.Delete(obsoletePath);
        }
    }

    private async ValueTask<T?> ReadRecordAsync<T>(
        string recordType,
        string path,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = OpenRead(path);
            T? record = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
            if (record is null)
            {
                throw new JsonException("State file deserialized to null.");
            }

            return record;
        }
        catch (JsonException exception)
        {
            await QuarantineCorruptedFileAsync(recordType, path, exception, cancellationToken);
            return null;
        }
    }

    private static async ValueTask WriteRecordAsync<T>(string path, T record, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            throw new InvalidOperationException($"Path '{path}' does not have a parent directory.");
        }

        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        await using (FileStream stream = new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(path))
        {
            File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, path);
        }
    }

    private async ValueTask QuarantineCorruptedFileAsync(
        string recordType,
        string canonicalPath,
        Exception exception,
        CancellationToken cancellationToken)
    {
        DateTimeOffset detectedAtUtc = DateTimeOffset.UtcNow;
        string relativePath = Path.GetRelativePath(GetStateDirectory(), canonicalPath);
        string quarantinePath = Path.Combine(GetQuarantineDirectory(), relativePath);
        string quarantineDirectory = Path.GetDirectoryName(quarantinePath)
            ?? throw new InvalidOperationException($"Path '{quarantinePath}' does not have a parent directory.");

        Directory.CreateDirectory(quarantineDirectory);

        string uniqueQuarantinePath = AppendTimestamp(quarantinePath, detectedAtUtc, ".corrupt");
        File.Move(canonicalPath, uniqueQuarantinePath);

        StateCorruptionIncident incident = new(
            RecordType: recordType,
            CanonicalPath: canonicalPath,
            QuarantinePath: uniqueQuarantinePath,
            Failure: exception.Message,
            DetectedAtUtc: detectedAtUtc);

        string incidentPath = AppendTimestamp(
            Path.Combine(GetRecoveryIncidentsDirectory(), SanitizeRecordType(recordType)),
            incident.DetectedAtUtc,
            ".json");

        string? incidentDirectory = Path.GetDirectoryName(incidentPath);
        if (incidentDirectory is null)
        {
            throw new InvalidOperationException($"Path '{incidentPath}' does not have a parent directory.");
        }

        Directory.CreateDirectory(incidentDirectory);

        await WriteRecordAsync(incidentPath, incident, cancellationToken);

        QuarantinedCorruptedStateLog(
            logger,
            recordType,
            canonicalPath,
            uniqueQuarantinePath,
            exception);
    }

    private string GetChatStatePath(ChatId chatId) =>
        Path.Combine(GetStateDirectory(), "chats", $"{chatId.Value}.json");

    private string GetThreadMappingPath(ThreadContext context) =>
        Path.Combine(
            GetStateDirectory(),
            "threads",
            context.Agent.ToString().ToLowerInvariant(),
            context.ChatId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"{context.ProjectId.Value}.json");

    private IEnumerable<string> GetApprovalPaths(ApprovalId approvalId)
    {
        yield return GetPendingApprovalPath(approvalId);
        yield return GetResolvedApprovalPath(approvalId);
    }

    private string GetPendingApprovalPath(ApprovalId approvalId) =>
        Path.Combine(GetPendingApprovalsDirectory(), $"{approvalId.Value}.json");

    private string GetResolvedApprovalPath(ApprovalId approvalId) =>
        Path.Combine(GetResolvedApprovalsDirectory(), $"{approvalId.Value}.json");

    private string GetPendingApprovalsDirectory() => Path.Combine(GetStateDirectory(), "approvals", "pending");

    private string GetResolvedApprovalsDirectory() => Path.Combine(GetStateDirectory(), "approvals", "resolved");

    private string GetStateDirectory() => Path.Combine(botRootPath, "state");

    private string GetQuarantineDirectory() => Path.Combine(GetStateDirectory(), "quarantine");

    private string GetRecoveryIncidentsDirectory() => Path.Combine(GetStateDirectory(), "recovery", "incidents");

    private static string AppendTimestamp(string path, DateTimeOffset timestamp, string extension)
    {
        string? directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileNameWithoutExtension(path);
        string suffix = timestamp.ToUniversalTime().ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        string fullFileName = $"{fileName}.{suffix}{extension}";
        return directory is null ? fullFileName : Path.Combine(directory, fullFileName);
    }

    private static string SanitizeRecordType(string recordType) =>
        recordType.Replace(Path.DirectorySeparatorChar, '-').Replace(Path.AltDirectorySeparatorChar, '-');

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        return options;
    }

    private sealed record ChatStateFileModel(long ChatId, AgentKind ActiveAgent, AgentProjectBindingsFileModel ProjectBindings)
    {
        public ChatState ToDomain() => new(new ChatId(ChatId), ActiveAgent, ProjectBindings.ToDomain());

        public static ChatStateFileModel FromDomain(ChatState state) =>
            new(state.ChatId.Value, state.ActiveAgent, AgentProjectBindingsFileModel.FromDomain(state.ProjectBindings));
    }

    private sealed record AgentProjectBindingsFileModel(string? GeneralProjectId, string? CodingProjectId)
    {
        public AgentProjectBindings ToDomain() =>
            new(
                string.IsNullOrWhiteSpace(GeneralProjectId) ? null : new ProjectId(GeneralProjectId),
                string.IsNullOrWhiteSpace(CodingProjectId) ? null : new ProjectId(CodingProjectId));

        public static AgentProjectBindingsFileModel FromDomain(AgentProjectBindings bindings) =>
            new(bindings.GeneralProjectId?.Value, bindings.CodingProjectId?.Value);
    }

    private sealed record ThreadMappingFileModel(
        ThreadContextFileModel Context,
        string CurrentThread,
        IReadOnlyList<string> PreviousThreads)
    {
        public ThreadMapping ToDomain() =>
            new(
                Context.ToDomain(),
                new ThreadReference(CurrentThread),
                PreviousThreads.Select(value => new ThreadReference(value)).ToArray());

        public static ThreadMappingFileModel FromDomain(ThreadMapping mapping) =>
            new(
                ThreadContextFileModel.FromDomain(mapping.Context),
                mapping.CurrentThread.Value,
                mapping.PreviousThreads.Select(thread => thread.Value).ToArray());
    }

    private sealed record ThreadContextFileModel(long ChatId, AgentKind Agent, string ProjectId)
    {
        public ThreadContext ToDomain() => new(new ChatId(ChatId), Agent, new ProjectId(ProjectId));

        public static ThreadContextFileModel FromDomain(ThreadContext context) =>
            new(context.ChatId.Value, context.Agent, context.ProjectId.Value);
    }

    private sealed record ApprovalRecordFileModel(
        string ApprovalId,
        ApprovalClass ApprovalClass,
        ApprovalContextFileModel Context,
        string Summary,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ResolvedAt,
        ApprovalDecision? Decision,
        Dictionary<string, string> OperationMetadata)
    {
        public ApprovalRecord ToDomain()
        {
            ApprovalRecord record = new(
                new ApprovalId(ApprovalId),
                ApprovalClass,
                Context.ToDomain(),
                Summary,
                CreatedAt,
                OperationMetadata);

            return ResolvedAt is null || Decision is null
                ? record
                : record with { ResolvedAt = ResolvedAt, Decision = Decision };
        }

        public static ApprovalRecordFileModel FromDomain(ApprovalRecord record) =>
            new(
                record.ApprovalId.Value,
                record.ApprovalClass,
                ApprovalContextFileModel.FromDomain(record.Context),
                record.Summary,
                record.CreatedAt,
                record.ResolvedAt,
                record.Decision,
                new Dictionary<string, string>(record.OperationMetadata, StringComparer.Ordinal));
    }

    private sealed record ApprovalContextFileModel(long ChatId, AgentKind Agent, string ProjectId, string ThreadReference)
    {
        public ApprovalContext ToDomain() =>
            new(new ChatId(ChatId), Agent, new ProjectId(ProjectId), new ThreadReference(ThreadReference));

        public static ApprovalContextFileModel FromDomain(ApprovalContext context) =>
            new(context.ChatId.Value, context.Agent, context.ProjectId.Value, context.ThreadReference.Value);
    }

    private sealed record OwnerConfigurationFileModel(long UserId, string? Username)
    {
        public OwnerConfiguration ToDomain() => new(new UserId(UserId), Username);
    }
}
