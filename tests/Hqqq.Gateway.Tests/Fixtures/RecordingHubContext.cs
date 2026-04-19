using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Hqqq.Gateway.Tests.Fixtures;

/// <summary>
/// Minimal recording <see cref="IHubContext{THub}"/> for unit tests of
/// SignalR fan-out paths. Only <c>Clients.All.SendAsync(method, arg, ct)</c>
/// is exercised by Phase 2D2 broadcasts; everything else is intentionally
/// left as <c>NotSupportedException</c> so accidental usage surfaces loudly.
/// </summary>
public sealed class RecordingHubContext<THub> : IHubContext<THub> where THub : Hub
{
    private readonly RecordingHubClients _clients = new();

    public IHubClients Clients => _clients;

    public IGroupManager Groups => throw new NotSupportedException(
        "RecordingHubContext does not implement group management.");

    public IReadOnlyList<RecordedSend> Sends => _clients.AllProxy.Sends;

    /// <summary>If set, the next SendAsync on Clients.All throws this.</summary>
    public Exception? ThrowOnAllSend
    {
        get => _clients.AllProxy.ThrowOnSend;
        set => _clients.AllProxy.ThrowOnSend = value;
    }

    public sealed record RecordedSend(string Method, object?[] Args);

    private sealed class RecordingHubClients : IHubClients
    {
        public RecordingClientProxy AllProxy { get; } = new();

        public IClientProxy All => AllProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Throw();
        public IClientProxy Client(string connectionId) => Throw();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Throw();
        public IClientProxy Group(string groupName) => Throw();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Throw();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Throw();
        public IClientProxy User(string userId) => Throw();
        public IClientProxy Users(IReadOnlyList<string> userIds) => Throw();

        private static IClientProxy Throw() =>
            throw new NotSupportedException("Only Clients.All is supported in this test fake.");
    }

    public sealed class RecordingClientProxy : IClientProxy
    {
        private readonly ConcurrentQueue<RecordedSend> _sends = new();

        public IReadOnlyList<RecordedSend> Sends => _sends.ToArray();

        public Exception? ThrowOnSend { get; set; }

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend is { } ex)
                throw ex;

            _sends.Enqueue(new RecordedSend(method, args));
            return Task.CompletedTask;
        }
    }
}
