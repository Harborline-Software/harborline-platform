namespace Harborline.Foundation.Scheduling;

/// <summary>One query's shared work allowance, including input processing before date expansion.</summary>
public sealed class RruleExpansionBudget
{
    /// <summary>Charges rule text before parsing or rebuilding selector lists.</summary>
    public RruleExpansionBudget(string recurrenceId, DateOnly anchor, DateOnly requestedRangeStart,
        DateOnly requestedRangeEnd, int candidateLimit = 50_000)
    {
        ArgumentNullException.ThrowIfNull(recurrenceId);
        ArgumentOutOfRangeException.ThrowIfLessThan(candidateLimit, 1);
        RecurrenceId = recurrenceId;
        Anchor = anchor;
        RequestedRangeStart = requestedRangeStart;
        RequestedRangeEnd = requestedRangeEnd;
        CandidateLimit = candidateLimit;
        Charge(recurrenceId.Length);
        Bound = RecurrenceBound.None;
        // Only bounded text is inspected; syntax admission still belongs to RruleSubset.
        foreach (var range in recurrenceId.AsSpan().Split(';'))
        {
            var token = recurrenceId.AsSpan()[range];
            var equals = token.IndexOf('=');
            if (equals < 0) continue;
            var key = token[..equals].Trim();
            if (key.Equals("UNTIL", StringComparison.OrdinalIgnoreCase)) Bound = RecurrenceBound.Until;
            else if (key.Equals("COUNT", StringComparison.OrdinalIgnoreCase) && Bound != RecurrenceBound.Until)
                Bound = RecurrenceBound.Count;
        }
    }

    /// <summary>The original rule.</summary>
    public string RecurrenceId { get; }
    /// <summary>The original DTSTART.</summary>
    public DateOnly Anchor { get; }
    /// <summary>The query's lower bound.</summary>
    public DateOnly RequestedRangeStart { get; }
    /// <summary>The horizon, extended after relevant override originals have been selected.</summary>
    public DateOnly RequestedRangeEnd { get; set; }
    /// <summary>The shared allowance.</summary>
    public int CandidateLimit { get; }
    /// <summary>Units spent, including the first refused unit.</summary>
    public int CandidatesExamined { get; private set; }
    /// <summary>The last matched date, if any.</summary>
    public DateOnly? LastEvaluatedOccurrence { get; internal set; }
    /// <summary>The rule bound; null when input processing exhausted the budget before classification.</summary>
    public RecurrenceBound? Bound { get; }

    /// <summary>Charges one override, recurrence period or matched date before processing it.</summary>
    public void Charge() => Charge(1);

    private void Charge(int units)
    {
        if (units > CandidateLimit - CandidatesExamined)
        {
            CandidatesExamined = CandidateLimit == int.MaxValue ? int.MaxValue : CandidateLimit + 1;
            throw new RruleExpansionBudgetExceededException(RecurrenceId, Anchor, RequestedRangeStart, RequestedRangeEnd,
                CandidateLimit, CandidatesExamined, LastEvaluatedOccurrence, Bound);
        }
        CandidatesExamined += units;
    }
}
