namespace ServantClaw.Host.Runtime;

public sealed class HostRuntimeCoordinator(IEnumerable<IHostRuntimeParticipant> participants)
{
    private readonly IReadOnlyList<IHostRuntimeParticipant> participants = participants.ToArray();
    private readonly List<IHostRuntimeParticipant> startedParticipants = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (IHostRuntimeParticipant participant in participants)
        {
            try
            {
                await participant.StartAsync(cancellationToken);
                startedParticipants.Add(participant);
            }
            catch
            {
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
            await startedParticipants[index].StopAsync(cancellationToken);
        }

        startedParticipants.Clear();
    }
}
