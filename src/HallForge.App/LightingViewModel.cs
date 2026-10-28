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
    // Slider drags produce several values in a row; only the last one within this window is written.
    private static readonly TimeSpan LiveDelay = TimeSpan.FromMilliseconds(250);
    private LightingState? _current;
    private bool _showing;
    private bool _writing;
    private bool _changedWhileWriting;

    public bool RequiresExperimentalConsent => !target.Definition!.Verified;

    /// <summary>Writes every change right away, without the review dialog. Each write is still read back and verified.</summary>
    [ObservableProperty]
    public partial bool ApplyImmediately { get; set; }

    partial void OnApplyImmediatelyChanged(bool value) => _ = ApplyLiveAsync();
    partial void OnBrightnessChanged(double value) => _ = ApplyLiveAsync();
    partial void OnSpeedChanged(double value) => _ = ApplyLiveAsync();
    partial void OnOptionChanged(int value) => _ = ApplyLiveAsync();
    partial void OnRainbowChanged(bool value) => _ = ApplyLiveAsync();
    partial void OnColorHexChanged(string value) => _ = ApplyLiveAsync();

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
        _ = ApplyLiveAsync();
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
        if (_current is null)
            return;
        if (Wanted() is not { } wanted)
        {
            Status = Strings.InvalidColor;
            return;
        }
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

    /// <returns>The edited setting, or null while the color is not a valid RRGGBB value.</returns>
    private LightingState? Wanted()
    {
        if (Mode is null || _current is null || (!TryParseColor(ColorHex, out var rgb) && !Rainbow))
            return null;
        // Rainbow ignores the color, so keep the stored color instead of overwriting it.
        return Rainbow
            ? new LightingState(Mode.Mode, (int)Brightness, (int)Speed, Option, true, _current.Red, _current.Green, _current.Blue)
            : new LightingState(Mode.Mode, (int)Brightness, (int)Speed, Option, false, rgb[0], rgb[1], rgb[2]);
    }

    private async Task ApplyLiveAsync()
    {
        if (!ApplyImmediately || !IsLoaded || _showing)
            return;
        if (_writing)
        {
            _changedWhileWriting = true;
            return;
        }
        _writing = true;
        try
        {
            do
            {
                _changedWhileWriting = false;
                await Task.Delay(LiveDelay);
                if (Wanted() is not { } wanted || wanted == _current)
                    continue;
                IsBusy = true;
                try
                {
                    var actual = await Task.Run(() =>
                    {
                        using var session = RongYuanSession.Open(transport, target);
                        return session.Client.WriteLighting(wanted);
                    });
                    _current = actual;
                    // Edits made during the write win; showing the written state would undo them.
                    if (!_changedWhileWriting)
                        Show(actual);
                    Status = Strings.LightingLiveApplied;
                }
                catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or UnauthorizedAccessException)
                {
                    Status = Text.Format(Strings.LightingFailed, ex.Message);
                }
                finally
                {
                    IsBusy = false;
                }
            }
            while (_changedWhileWriting);
        }
        finally
        {
            _writing = false;
        }
    }

    private void Show(LightingState state)
    {
        _showing = true;
        _current = state;
        Mode = Modes.FirstOrDefault(m => m.Mode == state.Mode);
        Brightness = state.Brightness;
        Speed = state.Speed;
        Option = state.Option;
        Rainbow = state.Rainbow;
        ColorHex = $"{state.Red:X2}{state.Green:X2}{state.Blue:X2}";
        _showing = false;
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
