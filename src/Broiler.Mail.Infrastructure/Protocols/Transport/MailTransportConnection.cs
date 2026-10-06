// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Infrastructure.Protocols.Transport;

/// <summary>
/// Low-level transport connection managing TCP and TLS negotiation, buffered line I/O,
/// and streaming byte transfers. 100% native .NET, trimmer-safe.
/// </summary>
public sealed class MailTransportConnection : IAsyncDisposable, IDisposable
{
    private TcpClient? _tcpClient;
    private Stream? _stream;
    private readonly byte[] _readBuffer = new byte[4096];
    private int _bufferPos = 0;
    private int _bufferCount = 0;

    public bool IsEncrypted { get; private set; }
    public bool IsConnected => _tcpClient?.Connected == true;

    public async Task ConnectAsync(
        string host,
        int port,
        TransportSecurity security,
        RemoteCertificateValidationCallback? certValidator,
        CancellationToken cancellationToken)
    {
        _tcpClient = new TcpClient { NoDelay = true };
        await _tcpClient.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        Stream netStream = _tcpClient.GetStream();

        if (security == TransportSecurity.Tls)
        {
            var sslStream = new SslStream(netStream, false, certValidator);
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            };
            await sslStream.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
            _stream = sslStream;
            IsEncrypted = true;
        }
        else
        {
            _stream = netStream;
            IsEncrypted = false;
        }
        _bufferPos = 0;
        _bufferCount = 0;
    }

    public async Task UpgradeToTlsAsync(
        string host,
        RemoteCertificateValidationCallback? certValidator,
        CancellationToken cancellationToken)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");
        if (IsEncrypted) throw new InvalidOperationException("Already upgraded to TLS.");

        // Clear any residual unencrypted buffer
        _bufferPos = 0;
        _bufferCount = 0;

        var sslStream = new SslStream(_stream, false, certValidator);
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        };
        await sslStream.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
        _stream = sslStream;
        IsEncrypted = true;
    }

    /// <summary>
    /// Reads a single line terminated by CRLF or LF.
    /// </summary>
    public async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");
        var sb = new StringBuilder();

        while (true)
        {
            if (_bufferPos >= _bufferCount)
            {
                _bufferPos = 0;
                _bufferCount = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length, cancellationToken).ConfigureAwait(false);
                if (_bufferCount == 0)
                {
                    if (sb.Length > 0) return sb.ToString();
                    throw new IOException("Connection closed by remote host.");
                }
            }

            byte b = _readBuffer[_bufferPos++];
            if (b == (byte)'\n')
            {
                string line = sb.ToString();
                if (line.EndsWith('\r'))
                {
                    line = line[..^1];
                }
                return line;
            }
            sb.Append((char)b);
        }
    }

    /// <summary>
    /// Reads raw bytes of a specified length into a stream.
    /// </summary>
    public async Task ReadExactBytesAsync(Stream destination, int length, CancellationToken cancellationToken)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");
        int remaining = length;

        // Drain any buffered bytes first
        while (remaining > 0 && _bufferPos < _bufferCount)
        {
            int toTake = Math.Min(remaining, _bufferCount - _bufferPos);
            await destination.WriteAsync(_readBuffer.AsMemory(_bufferPos, toTake), cancellationToken).ConfigureAwait(false);
            _bufferPos += toTake;
            remaining -= toTake;
        }

        byte[] temp = new byte[Math.Min(8192, Math.Max(1024, remaining))];
        while (remaining > 0)
        {
            int toRead = Math.Min(temp.Length, remaining);
            int read = await _stream.ReadAsync(temp.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new IOException("Unexpected end of stream while reading payload.");
            await destination.WriteAsync(temp.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteBytesAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");
        await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }
        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
    }
}
