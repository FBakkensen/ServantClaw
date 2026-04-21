using ServantClaw.Domain.Configuration;

namespace ServantClaw.Application.Runtime;

public interface IBackendProcessLauncher
{
    IBackendProcessHandle Launch(BackendConfiguration configuration);
}
