namespace HallForge.App.Tests;

public class ReviewViewModelTests
{
    private static ReviewViewModel Review(Func<ReviewViewModel, CancellationToken, Task> send, string? acknowledgement = "ack") =>
        new("Test Board", [new ReviewRow("W", "Actuation 1.20 → 0.80", ""), new ReviewRow("A", "Actuation 1.20 → 0.80", "")],
            "2 keys", [], acknowledgement, "Send", send);

    [Fact]
    public void Send_RequiresAcknowledgement()
    {
        var review = Review((_, _) => Task.CompletedTask);

        Assert.False(review.SendCommand.CanExecute(null));
        review.Acknowledged = true;
        Assert.True(review.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task Interrupted_MarksTheCurrentRowUncertainAndTheRestNotSent()
    {
        var review = Review((r, _) =>
        {
            r.Report(ReviewRowStatus.Sent);
            r.Finish(1, ReviewRowStatus.Sent, interrupted: true, "unplugged");
            return Task.CompletedTask;
        }, acknowledgement: null);

        await review.SendCommand.ExecuteAsync(null);

        Assert.Equal([ReviewRowStatus.Sent, ReviewRowStatus.Uncertain], review.Rows.Select(r => r.Status));
        Assert.True(review.IsDone);
        Assert.True(review.HasProblem);
        Assert.True(review.WasSent);
    }

    [Fact]
    public async Task Stop_CancelsSending()
    {
        var started = new TaskCompletionSource();
        var review = Review(async (r, cancellation) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellation);
            }
            catch (OperationCanceledException)
            {
                r.Finish(0, ReviewRowStatus.Sent, interrupted: false, "stopped");
            }
        }, acknowledgement: null);

        var sending = review.SendCommand.ExecuteAsync(null);
        await started.Task;
        review.StopCommand.Execute(null);
        await sending;

        Assert.All(review.Rows, r => Assert.Equal(ReviewRowStatus.NotSent, r.Status));
    }
}
