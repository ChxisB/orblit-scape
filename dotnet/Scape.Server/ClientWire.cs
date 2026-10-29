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
public sealed class ClientWire(IWireLink link, Inbox inbox)
{
    private readonly Dictionary<BadMessage, int> _refused = [];

    /// <summary>Messages understood after login.</summary>
    public long Understood { get; private set; }

    /// <summary>Logins sent by a player who is already logged in.</summary>
    public long Unexpected { get; private set; }

    /// <summary>
    /// Bytes that were not a message, by why. The host's budget refuses a message before it gets
    /// here and counts that itself.
    /// </summary>
    public IReadOnlyDictionary<BadMessage, int> Refused => _refused.AsReadOnly();

    /// <summary>The name is the whole of the identity, so any name nobody is logged in under is welcome.</summary>
    public ValueTask<Admission> Admit(byte[] credentials)
    {
        switch (MessageCodec.DecodeClient(credentials))
        {
            case Understood<ClientMessage> { Message: Login login }:
                return ValueTask.FromResult(AdmitLogin(login));
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

    private Admission AdmitLogin(Login login)
    {
        var key = Wire.NameKey(login.Name);
        return link.IsLive(key) ? new Refuse(LoginRefusal.NameTaken.ToWire()) : new Admit(key);
    }

    private void CountRefused(BadMessage why) => _refused[why] = _refused.GetValueOrDefault(why) + 1;
}
