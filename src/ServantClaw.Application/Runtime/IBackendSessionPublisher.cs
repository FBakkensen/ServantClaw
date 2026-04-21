namespace ServantClaw.Application.Runtime;

public interface IBackendSessionPublisher
{
    void Publish(BackendSession session);

    void Retract();
}
