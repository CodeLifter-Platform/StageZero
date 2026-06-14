using System.Collections.Concurrent;

namespace StageZero.ReverseProxy.Services
{
    /// <summary>
    /// Holds in-flight ACME HTTP-01 challenge responses so the
    /// /.well-known/acme-challenge/{token} endpoint can serve them while a
    /// certificate order is being validated by Let's Encrypt.
    /// Registered as a singleton: the certificate request runs in a scoped
    /// service while the challenge HTTP request is served from a separate scope.
    /// </summary>
    public interface IAcmeChallengeStore
    {
        void AddChallenge(string token, string keyAuthorization);
        bool TryGetChallenge(string token, out string? keyAuthorization);
        void RemoveChallenge(string token);
    }

    public class InMemoryAcmeChallengeStore : IAcmeChallengeStore
    {
        private readonly ConcurrentDictionary<string, string> _challenges = new();

        public void AddChallenge(string token, string keyAuthorization)
            => _challenges[token] = keyAuthorization;

        public bool TryGetChallenge(string token, out string? keyAuthorization)
            => _challenges.TryGetValue(token, out keyAuthorization);

        public void RemoveChallenge(string token)
            => _challenges.TryRemove(token, out _);
    }
}
