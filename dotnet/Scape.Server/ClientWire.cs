using Orblit.Net;
using Scape.Core;

namespace Scape.Server;

/// <summary>What the wire needs from the host. Declared here, beside the code that calls it.</summary>
public interface IWireLink
{
    bool IsLive(string key);

    void Send(string key, byte[] body);
}

/// <summary>Turns what clients send into logins and intents, and counts what it refuses.</summary>
/// <param name="link">The host.</param>
/// <param name="inbox">Where intents go.</param>
/// <param name="log">Takes a line about a login that failed. It is called off the tick.</param>
/// <param name="gate">
/// What a login passes before the player joins, such as a password and a save that may take a
/// while to read. It answers null for a login that may join, or why not. A throw is an unavailable
/// refusal.
/// </param>
public sealed class ClientWire(IWireLink link, Inbox inbox, Action<string> log, Func<string, Login, Task<LoginRefusal?>>? gate = null)
{
    private readonly Dictionary<BadMessage, int> _refused = [];

    /// <summary>
    /// Keys whose login is waiting on the gate. A second login for one of them would race the
    /// first, so it is turned away.
    /// </summary>
    private readonly HashSet<string> _deciding = [];

    private long _unavailable;

    /// <summary>Messages understood after login.</summary>
    public long Understood { get; private set; }

    /// <summary>Logins sent by a player who is already logged in.</summary>
    public long Unexpected { get; private set; }

    /// <summary>
    /// Logins the gate failed on, so neither admitted nor refused for a reason the player could fix.
    /// </summary>
    public long Unavailable => Interlocked.Read(ref _unavailable);

    /// <summary>
    /// Bytes that were not a message, by why. The host's budget refuses a message before it gets
    /// here and counts that itself.
    /// </summary>
    public IReadOnlyDictionary<BadMessage, int> Refused => _refused.AsReadOnly();

    /// <summary>
    /// Without a gate the name is the whole of the identity, so any name nobody is logged in under
    /// is welcome. With one, the gate decides, and the host keeps ticking while it does.
    /// </summary>
    public ValueTask<Admission> Admit(byte[] credentials)
    {
        switch (MessageCodec.DecodeClient(credentials))
        {
            case Understood<ClientMessage> { Message: Login login }:
                return AdmitLogin(login);
            case Unreadable<ClientMessage> unreadable:
                CountRefused(unreadable.Why);
                break;
        }

        return ValueTask.FromResult<Admission>(new Refuse(LoginRefusal.Unreadable.ToWire()));
    }

    public void OnJoin(string key) => link.Send(key, MessageCodec.Encode(new LoginOk(key)));

    public void OnGone(string key) => inbox.Forget(key);

    public void OnMessage(string key, byte[] body)
    {
        switch (MessageCodec.DecodeClient(body))
        {
            case Understood<ClientMessage> { Message: Intent intent }:
                Understood++;
                inbox.Add(key, intent);
                break;
            case Understood<ClientMessage> { Message: Login }:
                Unexpected++;
                break;
            case Unreadable<ClientMessage> unreadable:
                CountRefused(unreadable.Why);
                break;
        }
    }

    private ValueTask<Admission> AdmitLogin(Login login)
    {
        var key = Wire.NameKey(login.Name);
        if (link.IsLive(key))
        {
            return ValueTask.FromResult<Admission>(new Refuse(LoginRefusal.NameTaken.ToWire()));
        }

        if (gate is null)
        {
            return ValueTask.FromResult<Admission>(new Admit(key));
        }

        lock (_deciding)
        {
            if (!_deciding.Add(key))
            {
                return ValueTask.FromResult<Admission>(new Refuse(LoginRefusal.Busy.ToWire()));
            }
        }

        return new ValueTask<Admission>(DecideAsync(gate, key, login));
    }

    private async Task<Admission> DecideAsync(Func<string, Login, Task<LoginRefusal?>> check, string key, Login login)
    {
        try
        {
            var why = await check(key, login);
            return why is { } refusal ? new Refuse(refusal.ToWire()) : new Admit(key);
        }
#pragma warning disable CA1031 // Any failure of the gate is an unavailable refusal, so the host never sees a throw.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Interlocked.Increment(ref _unavailable);
            log($"Login of {key} failed: {e.Message}");
            return new Refuse(LoginRefusal.Unavailable.ToWire());
        }
        finally
        {
            lock (_deciding)
            {
                _deciding.Remove(key);
            }
        }
    }

    private void CountRefused(BadMessage why) => _refused[why] = _refused.GetValueOrDefault(why) + 1;
}
