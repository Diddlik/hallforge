using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HallForge.Core;
using HallForge.Protocol.RongYuan;

namespace HallForge.App;

public sealed record LightingModeItem(LightingMode Mode, string Name);

public sealed record LightingVariantItem(int Option, string Name);

/// <summary>Edits the keyboard lighting. The firmware reports lighting, so every write is read back and verified.</summary>
public sealed partial class LightingViewModel(
    KeyboardCandidate target, IHidTransport transport, Func<ReviewViewModel, Task> review) : ObservableObject
{
    private LightingState? _current;

    public IReadOnlyList<LightingModeItem> Modes { get; } = Enum.GetValues<LightingMode>()
        .Where(LightingState.IsWritable)
        .Select(m => new LightingModeItem(m, Strings.ResourceManager.GetString($"Lighting{m}", CultureInfo.CurrentUICulture) ?? m.ToString()))
        .ToList();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(LoadCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Variants), nameof(Variant), nameof(HasVariants))]
    public partial LightingModeItem? Mode { get; set; }

    [ObservableProperty]
    public partial double Brightness { get; set; }

    [ObservableProperty]
    public partial double Speed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Variant))]
    public partial int Option { get; set; }

    public IReadOnlyList<LightingVariantItem> Variants => Mode is null ? [] : LightingState.VariantsOf(Mode.Mode)
        .Select((v, i) => new LightingVariantItem(i, Strings.ResourceManager.GetString($"LightingVariant{v}", CultureInfo.CurrentUICulture) ?? v.ToString()))
        .ToList();

    public bool HasVariants => Variants.Count > 0;

    public LightingVariantItem? Variant
    {
        get => Variants.FirstOrDefault(v => v.Option == Option);
        set
        {
            if (value is not null)
                Option = value.Option;
        }
    }

    // Variant indices mean different things per effect, so a newly chosen effect starts at its first variant.
    partial void OnModeChanged(LightingModeItem? oldValue, LightingModeItem? newValue)
    {
        if (oldValue is not null && newValue is not null && oldValue.Mode != newValue.Mode)
            Option = 0;
    }

    [ObservableProperty]
    public partial bool Rainbow { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorPreview))]
    public partial string ColorHex { get; set; } = "FFFFFF";

    /// <summary>Preview of the entered color, or null while the text is not a valid RRGGBB value.</summary>
    public string? ColorPreview => TryParseColor(ColorHex, out _) ? "#" + ColorHex.ToUpperInvariant() : null;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Show(await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(transport, target);
                return session.Client.ReadLighting();
            }));
            IsLoaded = true;
            Status = Strings.LightingLoaded;
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

    private bool IsIdle() => !IsBusy;

    private bool CanApply() => !IsBusy && IsLoaded;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (Mode is null || _current is null)
            return;
        if (!TryParseColor(ColorHex, out var rgb) && !Rainbow)
        {
            Status = Strings.InvalidColor;
            return;
        }
        // Rainbow ignores the color, so keep the stored color instead of overwriting it.
        var wanted = new LightingState(Mode.Mode, (int)Brightness, (int)Speed, Option, Rainbow,
            Rainbow ? _current.Red : rgb[0], Rainbow ? _current.Green : rgb[1], Rainbow ? _current.Blue : rgb[2]);
        if (wanted == _current)
        {
            Status = Strings.LightingUnchanged;
            return;
        }
        var row = new ReviewRow(Strings.TabLighting, $"{Describe(_current)} → {Describe(wanted)}", "");
        var definition = target.Definition!;
        await review(new ReviewViewModel(definition.Name, [row], Strings.LightingReviewSummary, [Strings.LightingReviewNote],
            definition.Verified ? null : Strings.LightingAcknowledgeExperimental, Strings.ApplyLighting,
            (r, _) => SendAsync(wanted, r)));
    }

    private async Task SendAsync(LightingState wanted, ReviewViewModel review)
    {
        IsBusy = true;
        try
        {
            Show(await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(transport, target);
                return session.Client.WriteLighting(wanted);
            }));
            Status = Strings.LightingVerified;
            review.Finish(1, ReviewRowStatus.Verified, false, Strings.LightingVerified);
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.LightingFailed, ex.Message);
            review.Finish(0, ReviewRowStatus.Verified, ex is not DeviceNotSupportedException, Status);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(LightingState state)
    {
        _current = state;
        Mode = Modes.FirstOrDefault(m => m.Mode == state.Mode);
        Brightness = state.Brightness;
        Speed = state.Speed;
        Option = state.Option;
        Rainbow = state.Rainbow;
        ColorHex = $"{state.Red:X2}{state.Green:X2}{state.Blue:X2}";
    }

    private string Describe(LightingState state) => Text.Format(Strings.LightingSummary,
        Modes.FirstOrDefault(m => m.Mode == state.Mode)?.Name ?? state.Mode.ToString(), state.Brightness, state.Speed,
        state.Rainbow ? Strings.LightingRainbow : $"#{state.Red:X2}{state.Green:X2}{state.Blue:X2}");

    private static bool TryParseColor(string text, out byte[] rgb)
    {
        rgb = [0, 0, 0];
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return false;
        rgb = Convert.FromHexString(text);
        return true;
    }
}
