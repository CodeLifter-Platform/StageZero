using System.Net;
using System.Net.Sockets;
using System.Text;

namespace StageZero.Tests.Fakes;

/// <summary>
/// A minimal plaintext SMTP listener on a loopback port. It speaks just enough of the
/// protocol for <c>System.Net.Mail.SmtpClient</c> to hand it a message, records what it
/// received, and can be told to reject a recipient. No TLS, no AUTH: it stands in for a
/// local relay or mail sink.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task _acceptLoop;
    private readonly CancellationTokenSource _stop = new();

    public FakeSmtpServer(bool rejectRecipients = false)
    {
        RejectRecipients = rejectRecipients;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public bool RejectRecipients { get; }

    /// <summary>Every command line the client sent, in order, across all sessions.</summary>
    public List<string> Commands { get; } = new();

    /// <summary>Every accepted message, raw as transmitted after DATA.</summary>
    public List<ReceivedMessage> Messages { get; } = new();

    public sealed record ReceivedMessage(string From, IReadOnlyList<string> To, string Raw)
    {
        public string Header(string name)
        {
            foreach (var line in Raw.Split("\r\n"))
            {
                if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return line[(name.Length + 1)..].Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// The body with its Content-Transfer-Encoding undone, so tests can look for the
        /// code as the recipient would see it. Handles the single-part messages
        /// <c>SmtpClient</c> produces (base64, quoted-printable, or 7/8-bit).
        /// </summary>
        public string DecodedBody
        {
            get
            {
                var split = Raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                var body = split < 0 ? string.Empty : Raw[(split + 4)..];
                var encoding = Header("Content-Transfer-Encoding").ToLowerInvariant();

                return encoding switch
                {
                    "base64" => Encoding.UTF8.GetString(Convert.FromBase64String(body.Replace("\r\n", string.Empty))),
                    "quoted-printable" => DecodeQuotedPrintable(body),
                    _ => body,
                };
            }
        }

        private static string DecodeQuotedPrintable(string text)
        {
            var bytes = new List<byte>(text.Length);
            var unfolded = text.Replace("=\r\n", string.Empty);
            for (var i = 0; i < unfolded.Length; i++)
            {
                if (unfolded[i] == '=' && i + 2 < unfolded.Length
                    && byte.TryParse(unfolded.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
                {
                    bytes.Add(value);
                    i += 2;
                }
                else
                {
                    bytes.Add((byte)unfolded[i]);
                }
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => HandleSessionAsync(client));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task HandleSessionAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII);
            var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

            await writer.WriteLineAsync("220 fake-smtp ready");

            string? from = null;
            var to = new List<string>();

            while (await reader.ReadLineAsync() is { } line)
            {
                lock (Commands) Commands.Add(line);
                var verb = line.Split(' ', 2)[0].ToUpperInvariant();

                switch (verb)
                {
                    case "EHLO":
                        await writer.WriteLineAsync("250-fake-smtp");
                        await writer.WriteLineAsync("250 8BITMIME");
                        break;
                    case "HELO":
                        await writer.WriteLineAsync("250 fake-smtp");
                        break;
                    case "MAIL":
                        from = Address(line);
                        await writer.WriteLineAsync("250 OK");
                        break;
                    case "RCPT":
                        if (RejectRecipients)
                        {
                            await writer.WriteLineAsync("550 5.1.1 No such user here");
                        }
                        else
                        {
                            to.Add(Address(line));
                            await writer.WriteLineAsync("250 OK");
                        }
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        var body = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                        {
                            body.Append(dataLine.StartsWith("..") ? dataLine[1..] : dataLine).Append("\r\n");
                        }
                        lock (Messages) Messages.Add(new ReceivedMessage(from ?? string.Empty, to.ToArray(), body.ToString()));
                        await writer.WriteLineAsync("250 OK queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return;
                    case "RSET":
                    case "NOOP":
                        await writer.WriteLineAsync("250 OK");
                        break;
                    default:
                        // STARTTLS and AUTH land here: this relay offers neither.
                        await writer.WriteLineAsync("502 5.5.1 Command not implemented");
                        break;
                }
            }
        }
    }

    private static string Address(string line)
    {
        var start = line.IndexOf('<');
        var end = line.IndexOf('>');
        return start >= 0 && end > start ? line[(start + 1)..end] : line;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _acceptLoop; } catch { /* shutting down */ }
        _stop.Dispose();
    }
}
