using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HallForge.Core;
using HallForge.Protocol.RongYuan;

namespace HallForge.App;

/// <summary>Sets the Roar mode keys from the key selection. The firmware reports them, so every write is read back and verified.</summary>
/// <param name="selectedKeys">Key ids currently selected on the keyboard view.</param>
/// <param name="legend">Printed label of a key id.</param>
public sealed partial class RoarViewModel(
    KeyboardCandidate target, IHidTransport transport, Func<ReviewViewModel, Task> review,
    Func<IReadOnlyList<string>> selectedKeys, Func<string, string> legend) : ObservableObject
{
    private IReadOnlyList<RoarSlot?>? _current;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseSelectionCommand), nameof(TurnOffCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseSelectionCommand), nameof(TurnOffCommand))]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    public partial string KeysText { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Show(await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(transport, target);
                return session.Client.ReadRoar();
            }));
            IsLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.ConnectFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanApply() => !IsBusy && IsLoaded;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task UseSelectionAsync()
    {
        var keys = selectedKeys();
        if (keys.Count is 0 or > Roar.SlotCount)
        {
            Status = Strings.RoarSelectKeys;
            return Task.CompletedTask;
        }
        var matrix = target.Definition!.Keys;
        return ApplyAsync(keys.Select(k => (RoarSlot?)new RoarSlot(matrix[k], true))
            .Concat(Enumerable.Repeat<RoarSlot?>(null, Roar.SlotCount - keys.Count)).ToList());
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task TurnOffAsync() => ApplyAsync(new RoarSlot?[Roar.SlotCount]);

    private async Task ApplyAsync(IReadOnlyList<RoarSlot?> wanted)
    {
        if (_current is null)
            return;
        // Slot order does not matter to the user, so the same keys in another order count as unchanged.
        if (Describe(wanted) == Describe(_current))
        {
            Status = Strings.RoarUnchanged;
            return;
        }
        var row = new ReviewRow(Strings.RoarTitle, $"{Describe(_current)} → {Describe(wanted)}", "");
        var definition = target.Definition!;
        await review(new ReviewViewModel(definition.Name, [row], Strings.RoarReviewSummary, [Strings.RoarReviewNote],
            definition.Verified ? null : Strings.LightingAcknowledgeExperimental, Strings.ApplyRoar,
            (r, _) => SendAsync(wanted, r)));
    }

    private async Task SendAsync(IReadOnlyList<RoarSlot?> wanted, ReviewViewModel review)
    {
        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(transport, target);
                session.Client.WriteRoar(wanted, session.Profile);
            });
            Show(wanted);
            Status = Strings.RoarVerified;
            review.Finish(1, ReviewRowStatus.Verified, false, Strings.RoarVerified);
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.RoarFailed, ex.Message);
            review.Finish(0, ReviewRowStatus.Verified, ex is not DeviceNotSupportedException, Status);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(IReadOnlyList<RoarSlot?> slots)
    {
        _current = slots;
        KeysText = Text.Format(Strings.RoarCurrent, Describe(slots));
    }

    // Disabled slots keep a key but have no effect, so they are not listed.
    private string Describe(IReadOnlyList<RoarSlot?> slots)
    {
        var names = slots.OfType<RoarSlot>().Where(s => s.Enabled)
            .Select(s => target.Definition!.Keys.FirstOrDefault(k => k.Value == s.MatrixIndex).Key is { } id ? legend(id) : $"#{s.MatrixIndex}")
            .Order(StringComparer.CurrentCulture)
            .ToList();
        return names.Count > 0 ? string.Join(", ", names) : Strings.RoarNone;
    }
}
