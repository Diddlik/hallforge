using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HallForge.App;

public enum ReviewStage
{
    Review,
    Sending,
    Done,
}

public enum ReviewRowStatus
{
    Queued,
    Sending,
    Sent,
    Verified,
    Uncertain,
    NotSent,
}

/// <summary>One item to write, e.g. one key, with the fields that change and those that stay as they are.</summary>
public sealed partial class ReviewRow(string name, string changes, string untouched) : ObservableObject
{
    public string Name { get; } = name;
    public string Changes { get; } = changes;
    public string Untouched { get; } = untouched;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial ReviewRowStatus Status { get; set; }

    public string StatusText => Status switch
    {
        ReviewRowStatus.Sending => Strings.RowSending,
        ReviewRowStatus.Sent => Strings.RowSent,
        ReviewRowStatus.Verified => Strings.RowVerified,
        ReviewRowStatus.Uncertain => Strings.RowUncertain,
        ReviewRowStatus.NotSent => Strings.RowNotSent,
        _ => Strings.RowQueued,
    };
}

/// <summary>
/// Review &amp; apply: shows exactly what will be written, asks for acknowledgement, then sends with per-item
/// progress. Sending can be stopped between items, never in the middle of one.
/// </summary>
public sealed partial class ReviewViewModel : ObservableObject
{
    private readonly Func<ReviewViewModel, CancellationToken, Task> _send;
    private Action? _stop;

    /// <param name="acknowledgement">Text the user must confirm before sending, or null when no confirmation is needed.</param>
    /// <param name="send">Writes the rows and reports progress through <see cref="Report"/> and <see cref="Finish"/>.</param>
    public ReviewViewModel(string target, IReadOnlyList<ReviewRow> rows, string summary, IReadOnlyList<string> notes,
        string? acknowledgement, string sendText, Func<ReviewViewModel, CancellationToken, Task> send)
    {
        Target = target;
        Rows = rows;
        Summary = summary;
        Notes = notes;
        Acknowledgement = acknowledgement;
        SendText = sendText;
        _send = send;
    }

    public string Target { get; }
    public IReadOnlyList<ReviewRow> Rows { get; }
    public string Summary { get; }
    public IReadOnlyList<string> Notes { get; }
    public string? Acknowledgement { get; }
    public bool NeedsAcknowledgement => Acknowledgement is not null;
    public string SendText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReview), nameof(IsSending), nameof(IsDone), nameof(Heading))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))]
    public partial ReviewStage Stage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool Acknowledged { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial int Completed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    public partial bool HasProblem { get; set; }

    [ObservableProperty]
    public partial string Result { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsStopping { get; set; }

    public bool IsReview => Stage == ReviewStage.Review;
    public bool IsSending => Stage == ReviewStage.Sending;
    public bool IsDone => Stage == ReviewStage.Done;

    /// <summary>True once sending started; the caller then reloads what the device received.</summary>
    public bool WasSent { get; private set; }

    public string Heading => Stage switch
    {
        ReviewStage.Sending => Strings.ReviewSendingHeading,
        ReviewStage.Done => HasProblem ? Strings.ReviewProblemHeading : Strings.ReviewDoneHeading,
        _ => Strings.ReviewHeading,
    };

    public string ProgressText => Text.Format(Strings.ReviewProgress, Completed, Rows.Count);

    private bool CanSend() => Stage == ReviewStage.Review && (!NeedsAcknowledgement || Acknowledged);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        WasSent = true;
        Stage = ReviewStage.Sending;
        if (Rows.Count > 0)
            Rows[0].Status = ReviewRowStatus.Sending;
        using var cancellation = new CancellationTokenSource();
        _stop = cancellation.Cancel;
        await _send(this, cancellation.Token);
        _stop = null;
    }

    private bool CanStop() => Stage == ReviewStage.Sending && !IsStopping;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        IsStopping = true;
        _stop?.Invoke();
    }

    /// <summary>Marks the next row as written. Ignored after <see cref="Finish"/>, because progress may arrive late.</summary>
    public void Report(ReviewRowStatus status)
    {
        if (Stage != ReviewStage.Sending || Completed >= Rows.Count)
            return;
        Rows[Completed].Status = status;
        Completed++;
        if (Completed < Rows.Count)
            Rows[Completed].Status = ReviewRowStatus.Sending;
    }

    /// <param name="written">Rows the device received.</param>
    /// <param name="interrupted">A write failed: the row after the written ones may hold its old or new value.</param>
    public void Finish(int written, ReviewRowStatus writtenStatus, bool interrupted, string result)
    {
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].Status = i < written ? writtenStatus
                : i == written && interrupted ? ReviewRowStatus.Uncertain
                : ReviewRowStatus.NotSent;
        Completed = written;
        HasProblem = written < Rows.Count;
        Result = result;
        Stage = ReviewStage.Done;
    }
}
