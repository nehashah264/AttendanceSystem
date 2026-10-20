namespace AttendanceSystem.Api.Services;

public interface ISaveFaultInjector
{
    void ThrowIfRequested();
}

public sealed class NoOpSaveFaultInjector : ISaveFaultInjector
{
    public void ThrowIfRequested()
    {
    }
}
