using HallForge.Core;

namespace HallForge.Protocol.RongYuan;

public sealed class DeviceNotSupportedException(string message) : Exception(message);

/// <summary>
/// An open, identified RongYuan keyboard: the firmware model id matches the definition and the active onboard
/// profile is known. Shared by GUI and CLI so both apply the same checks before any write.
/// </summary>
public sealed class RongYuanSession : IDisposable
{
    private readonly IHidConnection _connection;

    private RongYuanSession(IHidConnection connection, DeviceDefinition definition, uint modelId, byte profile)
    {
        _connection = connection;
        Client = new RongYuanClient(connection);
        Definition = definition;
        ModelId = modelId;
        Profile = profile;
    }

    public RongYuanClient Client { get; }
    public DeviceDefinition Definition { get; }
    public uint ModelId { get; }
    public byte Profile { get; }

    /// <exception cref="DeviceNotSupportedException">No RongYuan definition, or the firmware reports another model.</exception>
    public static RongYuanSession Open(IHidTransport transport, KeyboardCandidate target)
    {
        if (target.Definition is not { Protocol: "rongyuan" } definition)
            throw new DeviceNotSupportedException("The device has no RongYuan device definition.");

        var connection = transport.Open(target.Interface);
        try
        {
            var client = new RongYuanClient(connection);
            var modelId = client.Identify().DeviceId;
            if (!definition.FirmwareModelIds.Contains(modelId))
                throw new DeviceNotSupportedException($"Firmware model id {modelId} is not listed in '{definition.Id}'.");
            return new RongYuanSession(connection, definition, modelId, client.ReadActiveProfile());
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <param name="sent">Called after each key is sent and recorded, for progress reporting.</param>
    public void Apply(ApplyPlan plan, StateStore store, Action<KeyChange>? sent = null) =>
        plan.Execute(
            change => Client.WriteKeyTrigger(change.After, Profile, change.MatrixIndex),
            change =>
            {
                store.RecordWrite(Definition, ModelId, Profile, change.KeyId, change.After);
                sent?.Invoke(change);
            });

    public void Dispose() => _connection.Dispose();
}
