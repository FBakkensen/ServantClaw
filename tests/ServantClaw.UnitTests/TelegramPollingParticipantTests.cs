using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServantClaw.Application.Intake;
using ServantClaw.Application.Intake.Models;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Telegram;
using ServantClaw.Telegram.Transport;
using Xunit;

namespace ServantClaw.UnitTests;

public sealed class TelegramPollingParticipantTests
{
    [Fact]
    public async Task StartAsyncShouldDropPendingUpdatesBeforePolling()
    {
        FakeTelegramPollingClient pollingClient = new();
        TelegramPollingParticipant participant = CreateParticipant(pollingClient, new RecordingChatUpdateIntake());

        await participant.StartAsync(CancellationToken.None);

        pollingClient.DropPendingUpdatesCalls.Should().Be(1);

        await participant.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OwnerTextMessageShouldReachApplicationIntake()
    {
        FakeTelegramPollingClient pollingClient = new();
        pollingClient.EnqueueBatch(
            new TelegramIncomingUpdate(
                1,
                new TelegramIncomingMessage(100, 42, "approved-owner", DateTimeOffset.UtcNow, "hello")));

        RecordingChatUpdateIntake intake = new();

        TelegramPollingParticipant participant = CreateParticipant(pollingClient, intake);

        await participant.StartAsync(CancellationToken.None);
        InboundChatUpdate update = await intake.NextUpdate.Task.WaitAsync(TimeSpan.FromSeconds(2));

        update.ChatId.Value.Should().Be(100);
        update.UserId.Value.Should().Be(42);
        update.Input.Should().BeOfType<InboundChatTextMessage>()
            .Which.Text.Should().Be("hello");

        await participant.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OwnerCommandShouldBeParsedIntoCommandInput()
    {
        FakeTelegramPollingClient pollingClient = new();
        pollingClient.EnqueueBatch(
            new TelegramIncomingUpdate(
                1,
                new TelegramIncomingMessage(100, 42, "approved-owner", DateTimeOffset.UtcNow, "/agent@ServantClaw coding")));

        RecordingChatUpdateIntake intake = new();

        TelegramPollingParticipant participant = CreateParticipant(pollingClient, intake);

        await participant.StartAsync(CancellationToken.None);
        InboundChatUpdate update = await intake.NextUpdate.Task.WaitAsync(TimeSpan.FromSeconds(2));

        InboundChatCommand command = update.Input.Should().BeOfType<InboundChatCommand>().Subject;
        command.Name.Should().Be("agent");
        command.Arguments.Should().ContainSingle().Which.Should().Be("coding");

        await participant.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UnauthorizedMessageShouldBeIgnored()
    {
        FakeTelegramPollingClient pollingClient = new();
        pollingClient.EnqueueBatch(
            new TelegramIncomingUpdate(
                1,
                new TelegramIncomingMessage(100, 7, "intruder", DateTimeOffset.UtcNow, "hello")));

        RecordingChatUpdateIntake intake = new();
        TelegramPollingParticipant participant = CreateParticipant(pollingClient, intake);

        await participant.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        intake.ReceivedUpdates.Should().Be(0);
        await participant.StopAsync(CancellationToken.None);
    }

    private static TelegramPollingParticipant CreateParticipant(
        ITelegramPollingClient pollingClient,
        IChatUpdateIntake intake) =>
        new(
            new TelegramConfiguration(
                "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZabcd",
                new PollingConfiguration(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10))),
            new OwnerConfiguration(new UserId(42), "approved-owner"),
            intake,
            new FakeTelegramPollingClientFactory(pollingClient),
            NullLogger<TelegramPollingParticipant>.Instance);

    private sealed class RecordingChatUpdateIntake : IChatUpdateIntake
    {
        public TaskCompletionSource<InboundChatUpdate> NextUpdate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReceivedUpdates { get; private set; }

        public ValueTask HandleAsync(InboundChatUpdate update, CancellationToken cancellationToken)
        {
            ReceivedUpdates++;
            NextUpdate.TrySetResult(update);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTelegramPollingClientFactory(ITelegramPollingClient pollingClient) : ITelegramPollingClientFactory
    {
        public ITelegramPollingClient Create(string botToken) => pollingClient;
    }

    private sealed class FakeTelegramPollingClient : ITelegramPollingClient
    {
        private readonly Queue<IReadOnlyList<TelegramIncomingUpdate>> batches = new();

        public int DropPendingUpdatesCalls { get; private set; }

        public void EnqueueBatch(params TelegramIncomingUpdate[] updates) => batches.Enqueue(updates);

        public ValueTask DropPendingUpdatesAsync(CancellationToken cancellationToken)
        {
            DropPendingUpdatesCalls++;
            return ValueTask.CompletedTask;
        }

        public async ValueTask<IReadOnlyList<TelegramIncomingUpdate>> GetUpdatesAsync(
            int? offset,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (batches.Count > 0)
            {
                return batches.Dequeue();
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }
}
