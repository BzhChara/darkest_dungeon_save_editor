using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core.Dson;

internal static class NativeDsonCodec
{
    internal static async Task DecodeAsync(string input, string output, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckDistinctPaths(input, output);
        var bytes = await File.ReadAllBytesAsync(input, token).ConfigureAwait(false);
        var json = await Task.Run(() =>
        {
            var document = DsonReader.Read(bytes, token);
            return JsonSerializer.SerializeToUtf8Bytes(document.ToJson(token), JsonSupport.SerializerOptions);
        }, token).ConfigureAwait(false);
        await PublishAsync(output, json, token).ConfigureAwait(false);
    }

    internal static async Task EncodeAsync(string jsonPath, string output, string? originalPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckDistinctPaths(jsonPath, output);
        if (!string.IsNullOrWhiteSpace(originalPath)) CheckDistinctPaths(originalPath, output);
        var json = await File.ReadAllTextAsync(jsonPath, token).ConfigureAwait(false);
        var original = string.IsNullOrWhiteSpace(originalPath) ? null
            : await File.ReadAllBytesAsync(originalPath, token).ConfigureAwait(false);
        var bytes = await Task.Run(() =>
        {
            var desired = DsonDocument.ReadJson(json);
            var source = original is null ? null : DsonReader.Read(original, token);
            return DsonWriter.Encode(desired, source, token);
        }, token).ConfigureAwait(false);
        await PublishAsync(output, bytes, token).ConfigureAwait(false);
    }

    private static void CheckDistinctPaths(string input, string output)
    {
        if (Path.GetFullPath(input).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The codec output must not overwrite its input or original save.");
    }

    private static async Task PublishAsync(string output, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            // Only this operation's uniquely named temporary file is removed.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
