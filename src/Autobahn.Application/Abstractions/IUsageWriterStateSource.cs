namespace Autobahn.Application.Abstractions;
/// <summary>Occupancy of the asynchronous usage writer's queue, for the admin Overview.</summary>
public interface IUsageWriterStateSource
{
    /// <summary>Events accepted but not yet handed to persistence.</summary>
    int QueueDepth { get; }

    int Capacity { get; }
}
