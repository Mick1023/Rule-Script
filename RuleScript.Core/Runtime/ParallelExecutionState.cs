using System.Diagnostics;

namespace RuleScript.Core.Runtime;

internal sealed class ParallelExecutionState
{
    public long StartedTimestamp { get; set; } = Stopwatch.GetTimestamp();

    public long ExecutedStatements;
}
