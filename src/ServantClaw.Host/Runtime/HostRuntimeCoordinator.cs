using ServantClaw.Application.Runtime;
using Microsoft.Extensions.Logging;

namespace ServantClaw.Host.Runtime;

public sealed partial class HostRuntimeCoordinator(
    IEnumerable<IHostRuntimeParticipant> participants,
    ILogger<HostRuntimeCoordinator> logger)
{
    private readonly IReadOnlyList<IHostRuntimeParticipant> participants = participants.ToArray();
    private readonly List<IHostRuntimeParticipant> startedParticipants = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (IHostRuntimeParticipant participant in participants)
        {
            string participantName = participant.GetType().Name;

            try
            {
                Log.StartingParticipant(logger, participantName);
                await participant.StartAsync(cancellationToken);
                startedParticipants.Add(participant);
                Log.ParticipantStarted(logger, participantName);
            }
            catch (Exception exception)
            {
                Log.ParticipantStartupFailed(logger, participantName, exception);
                await StopStartedParticipantsAsync(cancellationToken);
                throw;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        StopStartedParticipantsAsync(cancellationToken);

    private async Task StopStartedParticipantsAsync(CancellationToken cancellationToken)
    {
        for (int index = startedParticipants.Count - 1; index >= 0; index--)
        {
            IHostRuntimeParticipant participant = startedParticipants[index];
            string participantName = participant.GetType().Name;

            Log.StoppingParticipant(logger, participantName);
            await participant.StopAsync(cancellationToken);
            Log.ParticipantStopped(logger, participantName);
        }

        startedParticipants.Clear();
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "Starting runtime participant {ParticipantName}")]
        public static partial void StartingParticipant(ILogger logger, string participantName);

        [LoggerMessage(EventId = 21, Level = LogLevel.Information, Message = "Runtime participant {ParticipantName} started")]
        public static partial void ParticipantStarted(ILogger logger, string participantName);

        [LoggerMessage(EventId = 22, Level = LogLevel.Critical, Message = "Runtime participant {ParticipantName} failed during startup")]
        public static partial void ParticipantStartupFailed(ILogger logger, string participantName, Exception exception);

        [LoggerMessage(EventId = 23, Level = LogLevel.Information, Message = "Stopping runtime participant {ParticipantName}")]
        public static partial void StoppingParticipant(ILogger logger, string participantName);

        [LoggerMessage(EventId = 24, Level = LogLevel.Information, Message = "Runtime participant {ParticipantName} stopped")]
        public static partial void ParticipantStopped(ILogger logger, string participantName);
    }
}
