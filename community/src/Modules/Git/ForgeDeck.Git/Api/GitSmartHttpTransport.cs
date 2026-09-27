using System.Diagnostics;
using System.Text;
using ForgeDeck.Git.Application;
using ForgeDeck.Git.Infrastructure;

namespace ForgeDeck.Git.Api;

/// <summary>
/// Minimal git smart-HTTP transport (HTTPS clone/fetch/push) over the system git CLI.
/// Serves bare repos from <see cref="IGitObjectStore.GetRepositoryPath"/>.
/// </summary>
public sealed class GitSmartHttpTransport(IGitObjectStore objectStore, GitRepositoryService repositories)
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public bool IsSupported =>
        objectStore is FileSystemGitObjectStore && FileSystemGitObjectStore.IsGitAvailable();

    public async Task<(string ContentType, byte[] Body)> AdvertiseAsync(
        Guid repositoryId,
        string service,
        CancellationToken cancellationToken = default)
    {
        var repoPath = RequireBareRepo(repositoryId);
        var normalized = NormalizeService(service);
        var advertise = await RunGitBinaryAsync(
            ["--git-dir=" + repoPath, GitVerb(normalized), "--stateless-rpc", "--advertise-refs", repoPath],
            input: null,
            cancellationToken);

        var header = $"# service={normalized}\n";
        var packet = EncodePktLine(header);
        var flush = "0000"u8.ToArray();
        var body = new byte[packet.Length + flush.Length + advertise.Length];
        Buffer.BlockCopy(packet, 0, body, 0, packet.Length);
        Buffer.BlockCopy(flush, 0, body, packet.Length, flush.Length);
        Buffer.BlockCopy(advertise, 0, body, packet.Length + flush.Length, advertise.Length);
        return ($"application/x-{normalized}-advertisement", body);
    }

    public async Task<(string ContentType, byte[] Body)> HandleRpcAsync(
        Guid repositoryId,
        string service,
        Stream requestBody,
        CancellationToken cancellationToken = default)
    {
        var repoPath = RequireBareRepo(repositoryId);
        var normalized = NormalizeService(service);
        await using var buffer = new MemoryStream();
        await requestBody.CopyToAsync(buffer, cancellationToken);
        var input = buffer.ToArray();

        var result = await RunGitBinaryAsync(
            ["--git-dir=" + repoPath, GitVerb(normalized), "--stateless-rpc", repoPath],
            input,
            cancellationToken);

        if (normalized == "git-receive-pack")
        {
            // Best-effort: refresh domain tips after a successful push.
            try
            {
                repositories.SyncRefsFromObjectStore(repositoryId);
            }
            catch
            {
                // Domain sync is additive; pack was already accepted.
            }
        }

        return ($"application/x-{normalized}-result", result);
    }

    private string RequireBareRepo(Guid repositoryId)
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException(
                "Smart-HTTP requires Git:ObjectStore=FileSystem and a system git CLI.");
        }

        var repository = repositories.Find(repositoryId)
            ?? throw new KeyNotFoundException("Repository was not found.");
        var key = GitRepositoryService.ObjectStoreKey(repository);
        objectStore.EnsureRepository(key);
        var path = objectStore.GetRepositoryPath(key);
        if (path.StartsWith("memory://", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(path))
        {
            throw new InvalidOperationException("Repository object store path is not a bare git directory.");
        }

        return path;
    }

    private static string NormalizeService(string service)
    {
        var value = (service ?? "").Trim();
        if (value.Equals("git-upload-pack", StringComparison.OrdinalIgnoreCase)
            || value.Equals("upload-pack", StringComparison.OrdinalIgnoreCase))
        {
            return "git-upload-pack";
        }

        if (value.Equals("git-receive-pack", StringComparison.OrdinalIgnoreCase)
            || value.Equals("receive-pack", StringComparison.OrdinalIgnoreCase))
        {
            return "git-receive-pack";
        }

        throw new ArgumentException($"Unsupported git smart-HTTP service '{service}'.");
    }

    /// <summary>Maps HTTP service name to the <c>git</c> CLI verb (<c>upload-pack</c> / <c>receive-pack</c>).</summary>
    private static string GitVerb(string service) =>
        service switch
        {
            "git-upload-pack" => "upload-pack",
            "git-receive-pack" => "receive-pack",
            _ => throw new ArgumentException($"Unsupported git smart-HTTP service '{service}'.")
        };

    private static byte[] EncodePktLine(string line)
    {
        var payload = Utf8NoBom.GetBytes(line);
        var hex = (payload.Length + 4).ToString("x4");
        var prefix = Utf8NoBom.GetBytes(hex);
        var packet = new byte[prefix.Length + payload.Length];
        Buffer.BlockCopy(prefix, 0, packet, 0, prefix.Length);
        Buffer.BlockCopy(payload, 0, packet, prefix.Length, payload.Length);
        return packet;
    }

    private static async Task<byte[]> RunGitBinaryAsync(
        IReadOnlyList<string> args,
        byte[]? input,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");

        await using (var stdin = process.StandardInput.BaseStream)
        {
            if (input is { Length: > 0 })
            {
                await stdin.WriteAsync(input, cancellationToken);
            }
        }

        await using var stdoutBuffer = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(stdoutBuffer, cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var verb = args.FirstOrDefault(a => a is "upload-pack" or "receive-pack") ?? "command";
            throw new InvalidOperationException(
                $"git {verb} failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return stdoutBuffer.ToArray();
    }
}
